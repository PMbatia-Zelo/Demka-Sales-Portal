using System.Text.Json;
using Demka.Portal.Core.Imports;
using Demka.Portal.Core.Seed;
using Demka.Portal.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Services;

/// <summary>Turns a Customer Ledger Entries upload into monthly outlet actuals.</summary>
public sealed class LedgerImportService(PortalDbContext db, ILogger<LedgerImportService> log)
{
    /// <summary>Posting groups whose unplaced sales are worth the admin's review (Modern Trade accounts).</summary>
    private static readonly HashSet<string> ReviewGroups = new(StringComparer.OrdinalIgnoreCase) { "SUPERMARKET", "CREDIT", "" };

    public static DateOnly TodayInNairobi() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    public async Task<object> ImportAsync(Stream file, string fileName, DateOnly refreshFrom, int? userId, CancellationToken ct)
    {
        var today = TodayInNairobi();
        var vat = 0.16m;
        var cycle = await db.TargetCycles.AsNoTracking().OrderByDescending(c => c.TargetStart).FirstOrDefaultAsync(ct);
        if (cycle is not null)
        {
            var a = JsonSerializer.Deserialize<Demka.Portal.Core.Targets.TargetAssumptions>(cycle.AssumptionsJson, SeedLoader.Json);
            if (a is not null) vat = a.VatRate;
        }

        var parsed = LedgerImport.Parse(TabularReader.ReadRows(file, fileName), vat, today);
        if (parsed.LinesUsed == 0)
            throw new InvalidDataException("No invoices or credit memos were found in this file.");

        var saved = await db.CustomerMappings.AsNoTracking()
            .ToDictionaryAsync(m => (m.CustomerNo, m.NameKey), m => new MappingTarget(m.OutletId, m.Ignore, m.Source), ct);
        var outletIdByName = (await db.Outlets.AsNoTracking().Select(o => new { o.Id, o.Name }).ToListAsync(ct))
            .GroupBy(o => LedgerImport.NormalizeName(o.Name)).ToDictionary(g => g.Key, g => g.First().Id);
        var alloc = LedgerImport.Allocate(parsed.Lines, refreshFrom, saved, outletIdByName);

        var lastMonth = new DateOnly(parsed.LastDate!.Value.Year, parsed.LastDate.Value.Month, 1);
        if (lastMonth < refreshFrom)
            throw new InvalidDataException($"The file's latest entry is {parsed.LastDate:d MMM yyyy}, before the month you chose to refresh.");
        var closed = await db.MonthStatuses.Where(m => m.IsClosed).Select(m => m.Month).ToListAsync(ct);
        var knownOutlets = (await db.Outlets.Select(o => o.Id).ToListAsync(ct)).ToHashSet();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var batch = new UploadBatch
        {
            Type = UploadType.LedgerEntries,
            FileName = Path.GetFileName(fileName),
            PeriodFrom = refreshFrom,
            PeriodTo = parsed.LastDate,
            RefreshFrom = refreshFrom,
            RowsRead = parsed.RowsRead,
            RowsMatched = alloc.Outlets.Sum(o => o.Lines),
            RowsRejected = parsed.Rejected.Count,
            UploadedByUserId = userId
        };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        // Replace the refreshed months: a re-upload of a corrected file is always safe.
        var replaced = await db.SalesActuals.Where(s => s.Month >= refreshFrom && s.Month <= lastMonth).ExecuteDeleteAsync(ct);
        foreach (var o in alloc.Outlets.Where(o => knownOutlets.Contains(o.OutletId)))
            db.SalesActuals.Add(new SalesActual
            {
                OutletId = o.OutletId,
                Month = o.Month,
                Amount = o.Amount,
                IsProvisional = !closed.Contains(o.Month),
                UploadBatchId = batch.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Remember automatic matches so future uploads, and the review list, stay consistent.
        foreach (var m in alloc.NewMappings.Where(m => !saved.ContainsKey((m.CustomerNo, m.NameKey))))
            db.CustomerMappings.Add(new CustomerMapping { CustomerNo = m.CustomerNo, NameKey = m.NameKey, OutletId = m.OutletId, Source = m.Source, CreatedByUserId = userId });

        // Only the latest upload's unplaced names matter.
        await db.UnmappedLedgerNames.ExecuteDeleteAsync(ct);
        var review = alloc.Unmapped.Where(u => ReviewGroups.Contains(u.PostingGroup)).ToList();
        foreach (var u in review)
            db.UnmappedLedgerNames.Add(new UnmappedLedgerName
            {
                UploadBatchId = batch.Id, CustomerNo = u.CustomerNo, NameKey = u.NameKey, CustomerName = u.CustomerName,
                PostingGroup = u.PostingGroup, Month = u.Month, Amount = u.Amount, Lines = u.Lines
            });
        batch.UnmappedCustomers = review.Select(u => (u.CustomerNo, u.NameKey)).Distinct().Count();

        var months = alloc.Outlets.GroupBy(o => o.Month).OrderBy(g => g.Key)
            .Select(g => new { month = g.Key, outlets = g.Count(), amount = Math.Round(g.Sum(x => x.Amount), 2), provisional = !closed.Contains(g.Key) }).ToList();
        var summary = new
        {
            batchId = batch.Id,
            file = batch.FileName,
            rowsRead = parsed.RowsRead,
            invoiceAndCreditLines = parsed.LinesUsed,
            otherDocumentLines = parsed.OtherDocumentTypes,
            firstDate = parsed.FirstDate,
            lastDate = parsed.LastDate,
            refreshFrom,
            refreshedTo = lastMonth,
            previousRowsReplaced = replaced,
            months,
            modernTradeAmount = Math.Round(alloc.MappedAmount, 2),
            reviewNames = batch.UnmappedCustomers,
            reviewAmount = Math.Round(review.Sum(u => u.Amount), 2),
            otherChannelsAmount = Math.Round(alloc.UnmappedAmount - review.Sum(u => u.Amount) + alloc.IgnoredAmount, 2),
            newAutomaticLinks = alloc.NewMappings.Count(m => !saved.ContainsKey((m.CustomerNo, m.NameKey))),
            rejected = parsed.Rejected.Count,
            rejectedExamples = parsed.Rejected.Take(20).Select(r => new { row = r.RowNumber, reason = r.Reason })
        };
        batch.SummaryJson = JsonSerializer.Serialize(summary, SeedLoader.Json);
        db.AuditLogs.Add(new AuditLog { UserId = userId, Action = "UploadLedger", Entity = nameof(UploadBatch), EntityId = batch.Id.ToString(), NewValue = batch.SummaryJson });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.LogInformation("Ledger upload {File}: {Lines} lines, MT KES {Amount:N0}, {Review} names to review.",
            batch.FileName, parsed.LinesUsed, alloc.MappedAmount, batch.UnmappedCustomers);
        return summary;
    }

    /// <summary>Links (or ignores) an unplaced name and moves its waiting sales onto the outlet straight away.</summary>
    public async Task<object> LinkAsync(string customerNo, string nameKey, int? outletId, bool ignore, int? userId, CancellationToken ct)
    {
        if (!ignore && (outletId is null || !await db.Outlets.AnyAsync(o => o.Id == outletId, ct)))
            throw new InvalidDataException("Choose an outlet to link this name to.");

        var map = await db.CustomerMappings.FirstOrDefaultAsync(m => m.CustomerNo == customerNo && m.NameKey == nameKey, ct);
        var old = map is null ? null : JsonSerializer.Serialize(new { map.OutletId, map.Ignore, map.Source });
        if (map is null) { map = new CustomerMapping { CustomerNo = customerNo, NameKey = nameKey }; db.CustomerMappings.Add(map); }
        map.OutletId = ignore ? null : outletId;
        map.Ignore = ignore;
        map.Source = "Admin";
        map.CreatedByUserId = userId;
        map.CreatedAt = DateTimeOffset.UtcNow;

        var waiting = await db.UnmappedLedgerNames.Where(u => u.CustomerNo == customerNo && u.NameKey == nameKey).ToListAsync(ct);
        map.CustomerName ??= waiting.FirstOrDefault()?.CustomerName;
        decimal moved = 0;
        if (!ignore)
        {
            var closed = await db.MonthStatuses.Where(m => m.IsClosed).Select(m => m.Month).ToListAsync(ct);
            foreach (var w in waiting)
            {
                var actual = await db.SalesActuals.FirstOrDefaultAsync(s => s.OutletId == outletId && s.Month == w.Month, ct);
                if (actual is null)
                    db.SalesActuals.Add(new SalesActual { OutletId = outletId!.Value, Month = w.Month, Amount = w.Amount, IsProvisional = !closed.Contains(w.Month), UploadBatchId = w.UploadBatchId });
                else { actual.Amount += w.Amount; actual.UpdatedAt = DateTimeOffset.UtcNow; }
                moved += w.Amount;
            }
        }
        db.UnmappedLedgerNames.RemoveRange(waiting);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, Action = ignore ? "IgnoreLedgerName" : "LinkLedgerName", Entity = nameof(CustomerMapping),
            EntityId = $"{customerNo}|{nameKey}", OldValue = old, NewValue = JsonSerializer.Serialize(new { outletId, ignore, moved })
        });
        await db.SaveChangesAsync(ct);
        return new { customerNo, nameKey, outletId = map.OutletId, ignore, movedAmount = Math.Round(moved, 2) };
    }

    /// <summary>
    /// Adds any verified customer aliases not yet in the database, so uploads place the known spelling variants.
    /// Names the admin has already linked (or ignored) are left alone.
    /// </summary>
    public static async Task SeedAliasesAsync(PortalDbContext db, string path, ILogger logger)
    {
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var outlets = (await db.Outlets.Select(o => o.Id).ToListAsync()).ToHashSet();
        var existing = (await db.CustomerMappings.Select(m => new { m.CustomerNo, m.NameKey }).ToListAsync())
            .Select(m => (m.CustomerNo, m.NameKey)).ToHashSet();
        var added = 0;
        foreach (var m in doc.RootElement.GetProperty("mappings").EnumerateArray())
        {
            var cust = m.GetProperty("customerNo").GetString()!;
            var key = LedgerImport.NormalizeName(m.GetProperty("nameKey").GetString());
            var outletId = m.GetProperty("outletId").GetInt32();
            if (!outlets.Contains(outletId) || existing.Contains((cust, key))) continue;
            db.CustomerMappings.Add(new CustomerMapping { CustomerNo = cust, NameKey = key, OutletId = outletId, Source = "Seed" });
            added++;
        }
        if (added == 0) return;
        await db.SaveChangesAsync();
        logger.LogInformation("Loaded {Count} customer name links from {File}.", added, Path.GetFileName(path));
    }
}
