using System.Text.Json;
using Demka.Portal.Core.Imports;
using Demka.Portal.Core.Seed;
using Demka.Portal.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Services;

public sealed record VisitorCount(string Name, int Visits);

/// <summary>Kind: VisitedByOther (someone else visits more than the holder), NotVisited (holder, no check-ins), NoHolder (visited, nobody holds it).</summary>
public sealed record HolderCheckRow(
    string Kind, int OutletId, string Outlet, string Region, decimal Q4Target,
    int? HolderId, string? Holder, int HolderVisits, int TotalVisits,
    int? TopVisitorId, string? TopVisitor, int TopVisits, List<VisitorCount> Visitors);

/// <summary>Turns a Solutech check-in report into visits per outlet, and checks them against who holds each outlet.</summary>
public sealed class SolutechImportService(PortalDbContext db, ILogger<SolutechImportService> log)
{
    private static readonly TimeSpan Nairobi = TimeSpan.FromHours(3);

    private static DateTimeOffset StartOfDay(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), Nairobi);

    private async Task<SolutechImport.OutletIndex> OutletIndexAsync(CancellationToken ct) =>
        new((await db.Outlets.AsNoTracking().Where(o => o.InScope).Select(o => new { o.Id, o.Name }).ToListAsync(ct))
            .Select(o => new OutletName(o.Id, o.Name)));

    public async Task<object> ImportAsync(Stream file, string fileName, int? userId, CancellationToken ct)
    {
        var parsed = SolutechImport.Parse(TabularReader.ReadRows(file, fileName), LedgerImportService.TodayInNairobi());
        if (parsed.Lines.Count == 0)
            throw new InvalidDataException("No check-ins were found in this file.");

        var index = await OutletIndexAsync(ct);
        var ledgerLinks = (await db.CustomerMappings.AsNoTracking().Where(m => m.OutletId != null && !m.Ignore)
                .Select(m => new { m.CustomerNo, m.NameKey, m.OutletId }).ToListAsync(ct))
            .GroupBy(m => (m.CustomerNo, m.NameKey)).ToDictionary(g => g.Key, g => g.First().OutletId!.Value);
        var saved = await db.ShopMappings.AsNoTracking().ToDictionaryAsync(m => m.ShopId, ct);
        var users = (await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToListAsync(ct))
            .GroupBy(u => SolutechImport.NormalizePerson(u.FullName)).ToDictionary(g => g.Key, g => g.First().Id);

        // Place each shop once.
        var placement = new Dictionary<int, int?>();
        var ignored = new HashSet<int>();
        var newLinks = new List<ShopMapping>();
        foreach (var g in parsed.Lines.GroupBy(l => l.ShopId))
        {
            var first = g.First();
            if (saved.TryGetValue(g.Key, out var m))
            {
                placement[g.Key] = m.Ignore ? null : m.OutletId;
                if (m.Ignore) ignored.Add(g.Key);
                continue;
            }
            var match = index.Match(first.ShopKey, first.ErpCode, ledgerLinks);
            placement[g.Key] = match?.OutletId;
            if (match is not null)
                newLinks.Add(new ShopMapping { ShopId = g.Key, ShopName = first.ShopName, ErpCode = first.ErpCode, OutletId = match.OutletId, Source = match.Source, CreatedByUserId = userId });
        }

        var from = StartOfDay(parsed.FirstDate!.Value);
        var to = StartOfDay(parsed.LastDate!.Value.AddDays(1));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = new UploadBatch
        {
            Type = UploadType.SolutechCheckIns,
            FileName = Path.GetFileName(fileName),
            PeriodFrom = parsed.FirstDate,
            PeriodTo = parsed.LastDate,
            RowsRead = parsed.RowsRead,
            RowsMatched = parsed.Lines.Count(l => placement[l.ShopId] is not null),
            RowsRejected = parsed.Rejected.Count,
            UploadedByUserId = userId
        };
        db.UploadBatches.Add(batch);
        db.ShopMappings.AddRange(newLinks);
        await db.SaveChangesAsync(ct);

        // Replace the days the file covers: re-uploading a corrected or longer report is always safe.
        var replaced = await db.Visits.Where(v => v.VisitedAt >= from && v.VisitedAt < to).ExecuteDeleteAsync(ct);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        foreach (var l in parsed.Lines)
            db.Visits.Add(new Visit
            {
                ExternalId = l.VisitId,
                OutletId = placement[l.ShopId],
                UserId = users.TryGetValue(l.RepKey, out var uid) ? uid : null,
                RepName = Clip(l.RepName),
                ShopId = l.ShopId,
                ShopName = Clip(l.ShopName),
                ErpCode = l.ErpCode == "" ? null : Clip(l.ErpCode, 30),
                VisitedAt = new DateTimeOffset(l.CheckIn, Nairobi),
                CheckedOutAt = l.CheckOut is { } o ? new DateTimeOffset(o, Nairobi) : null,
                MinutesOnSite = l.MinutesOnSite,
                Route = l.Route == "" ? null : Clip(l.Route),
                Region = l.Region == "" ? null : Clip(l.Region),
                UploadBatchId = batch.Id
            });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.AutoDetectChangesEnabled = true;

        var reps = parsed.Lines.GroupBy(l => l.RepKey).ToList();
        var unmatchedReps = reps.Where(r => !users.ContainsKey(r.Key)).Select(r => new { name = r.First().RepName, visits = r.Count() }).OrderByDescending(r => r.visits).ToList();
        var review = parsed.Lines.Where(l => placement[l.ShopId] is null && !ignored.Contains(l.ShopId)).ToList();
        var placed = parsed.Lines.Count(l => placement[l.ShopId] is not null);
        var summary = new
        {
            batchId = batch.Id,
            file = batch.FileName,
            rowsRead = parsed.RowsRead,
            visits = parsed.Lines.Count,
            duplicateRows = parsed.DuplicateRows,
            firstDate = parsed.FirstDate,
            lastDate = parsed.LastDate,
            previousVisitsReplaced = replaced,
            placedVisits = placed,
            placedShare = Math.Round((double)placed / parsed.Lines.Count, 4),
            shops = placement.Count,
            newAutomaticLinks = newLinks.Count,
            reviewShops = review.Select(l => l.ShopId).Distinct().Count(),
            reviewVisits = review.Count,
            ignoredVisits = parsed.Lines.Count(l => ignored.Contains(l.ShopId)),
            reps = reps.Count,
            repsMatched = reps.Count - unmatchedReps.Count,
            unmatchedReps,
            noCheckout = parsed.Lines.Count(l => l.CheckOut is null),
            rejected = parsed.Rejected.Count,
            rejectedExamples = parsed.Rejected.Take(20).Select(r => new { row = r.RowNumber, reason = r.Reason })
        };
        batch.UnmappedCustomers = summary.reviewShops;
        batch.SummaryJson = JsonSerializer.Serialize(summary, SeedLoader.Json);
        db.AuditLogs.Add(new AuditLog { UserId = userId, Action = "UploadSolutech", Entity = nameof(UploadBatch), EntityId = batch.Id.ToString(), NewValue = batch.SummaryJson });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.LogInformation("Solutech upload {File}: {Visits} visits {From}..{To}, {Placed} placed, {Review} shops to review.",
            batch.FileName, parsed.Lines.Count, parsed.FirstDate, parsed.LastDate, placed, summary.reviewShops);
        return summary;
    }

    private static string Clip(string s, int max = 200) => s.Length <= max ? s : s[..max];

    /// <summary>Shops whose check-ins could not be placed on an outlet, most-visited first, with suggested outlets.</summary>
    public async Task<object> UnplacedShopsAsync(CancellationToken ct)
    {
        var ignoredIds = await db.ShopMappings.AsNoTracking().Where(m => m.Ignore).Select(m => m.ShopId).ToListAsync(ct);
        var rows = await db.Visits.AsNoTracking()
            .Where(v => v.OutletId == null && !ignoredIds.Contains(v.ShopId))
            .GroupBy(v => v.ShopId)
            .Select(g => new
            {
                shopId = g.Key,
                shopName = g.Max(v => v.ShopName),
                erpCode = g.Max(v => v.ErpCode),
                region = g.Max(v => v.Region),
                rep = g.Max(v => v.RepName),
                reps = g.Select(v => v.RepName).Distinct().Count(),
                visits = g.Count(),
                lastVisit = g.Max(v => v.VisitedAt)
            })
            .OrderByDescending(x => x.visits)
            .ToListAsync(ct);
        var index = await OutletIndexAsync(ct);
        return rows.Select(r => new
        {
            r.shopId, r.shopName, r.erpCode, r.region, r.rep, r.reps, r.visits, r.lastVisit,
            suggestions = index.Suggest(r.shopName ?? "").Select(s => new { outletId = s.OutletId, name = s.Name, score = s.Score })
        });
    }

    /// <summary>Links (or ignores) a Solutech shop; its check-ins move onto the outlet straight away.</summary>
    public async Task<object> LinkShopAsync(int shopId, int? outletId, bool ignore, int? userId, CancellationToken ct)
    {
        if (!ignore && (outletId is null || !await db.Outlets.AnyAsync(o => o.Id == outletId, ct)))
            throw new InvalidDataException("Choose an outlet to link this shop to.");
        var sample = await db.Visits.AsNoTracking().Where(v => v.ShopId == shopId).Select(v => new { v.ShopName, v.ErpCode }).FirstOrDefaultAsync(ct);
        var map = await db.ShopMappings.FindAsync([shopId], ct);
        var old = map is null ? null : JsonSerializer.Serialize(new { map.OutletId, map.Ignore, map.Source });
        if (map is null) { map = new ShopMapping { ShopId = shopId, ShopName = sample?.ShopName ?? "", ErpCode = sample?.ErpCode }; db.ShopMappings.Add(map); }
        map.OutletId = ignore ? null : outletId;
        map.Ignore = ignore;
        map.Source = "Admin";
        map.CreatedByUserId = userId;
        map.CreatedAt = DateTimeOffset.UtcNow;
        var newOutlet = ignore ? null : outletId;
        var moved = await db.Visits.Where(v => v.ShopId == shopId).ExecuteUpdateAsync(u => u.SetProperty(v => v.OutletId, newOutlet), ct);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, Action = ignore ? "IgnoreSolutechShop" : "LinkSolutechShop", Entity = nameof(ShopMapping),
            EntityId = shopId.ToString(), OldValue = old, NewValue = JsonSerializer.Serialize(new { outletId = newOutlet, ignore, visits = moved })
        });
        await db.SaveChangesAsync(ct);
        return new { shopId, outletId = newOutlet, ignore, visits = moved };
    }

    /// <summary>
    /// Compares who visits each outlet with who holds its published target, over a window (default: the 28 days up to
    /// the latest check-in). Read-only: it suggests, the admin decides.
    /// </summary>
    public async Task<object> HolderCheckAsync(DateOnly? fromDay, DateOnly? toDay, CancellationToken ct)
    {
        var latest = await db.Visits.AsNoTracking().OrderByDescending(v => v.VisitedAt).Select(v => (DateTimeOffset?)v.VisitedAt).FirstOrDefaultAsync(ct);
        if (latest is null) return new { from = (DateOnly?)null, to = (DateOnly?)null, counts = new { }, rows = Array.Empty<object>() };
        var to = toDay ?? DateOnly.FromDateTime(latest.Value.ToOffset(Nairobi).DateTime);
        var from = fromDay ?? to.AddDays(-27);
        if (from > to) (from, to) = (to, from);

        var cycle = await db.TargetCycles.AsNoTracking().OrderByDescending(c => c.TargetStart).FirstOrDefaultAsync(ct);
        var version = cycle is null ? null : await db.TargetVersions.AsNoTracking().FirstOrDefaultAsync(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion, ct);
        var versionId = version?.Id ?? -1;
        var targets = await db.OutletTargets.AsNoTracking().Where(t => t.TargetVersionId == versionId)
            .GroupBy(t => t.OutletId).Select(g => new { OutletId = g.Key, Holder = g.Max(t => t.HolderUserId), Amount = g.Sum(t => t.Amount) }).ToListAsync(ct);
        var targetBy = targets.ToDictionary(t => t.OutletId);

        DateTimeOffset a = StartOfDay(from), b = StartOfDay(to.AddDays(1));
        var visits = await db.Visits.AsNoTracking().Where(v => v.OutletId != null && v.VisitedAt >= a && v.VisitedAt < b)
            .GroupBy(v => new { v.OutletId, v.UserId, v.RepName }).Select(g => new { g.Key.OutletId, g.Key.UserId, g.Key.RepName, N = g.Count() }).ToListAsync(ct);
        var byOutlet = visits.ToLookup(v => v.OutletId!.Value);
        var outlets = await db.Outlets.AsNoTracking().Where(o => o.InScope).ToDictionaryAsync(o => o.Id, ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var rows = new List<HolderCheckRow>();
        int different = 0, notVisited = 0, unassigned = 0, ok = 0;
        foreach (var o in outlets.Values)
        {
            var t = targetBy.GetValueOrDefault(o.Id);
            if (t is null && !byOutlet.Contains(o.Id)) continue;
            var holder = t?.Holder;
            var v = byOutlet[o.Id].ToList();
            var total = v.Sum(x => x.N);
            var top = v.GroupBy(x => x.UserId?.ToString() ?? "rep:" + x.RepName)
                .Select(g => new { g.First().UserId, Name = g.First().UserId is { } id ? names.GetValueOrDefault(id, g.First().RepName) : g.First().RepName, N = g.Sum(x => x.N) })
                .OrderByDescending(x => x.N).ToList();
            var holderVisits = holder is null ? 0 : v.Where(x => x.UserId == holder).Sum(x => x.N);
            string kind;
            if (holder is null) { if (total == 0) continue; kind = "NoHolder"; unassigned++; }
            else if (total == 0) { kind = "NotVisited"; notVisited++; }
            else if (top[0].UserId != holder && top[0].N > holderVisits) { kind = "VisitedByOther"; different++; }
            else { ok++; continue; }
            var first = top.FirstOrDefault();
            rows.Add(new HolderCheckRow(kind, o.Id, o.Name, o.Region, Math.Round(t?.Amount ?? 0, 2),
                holder, holder is { } h ? names.GetValueOrDefault(h) : null, holderVisits, total,
                first?.UserId, first?.Name, first?.N ?? 0, top.Take(4).Select(x => new VisitorCount(x.Name, x.N)).ToList()));
        }
        return new
        {
            from, to,
            counts = new { visitedByOther = different, notVisited, noHolder = unassigned, holderVisiting = ok },
            rows = rows.OrderBy(r => r.Kind switch { "VisitedByOther" => 0, "NoHolder" => 1, _ => 2 })
                .ThenByDescending(r => r.Q4Target).ToList()
        };
    }
}
