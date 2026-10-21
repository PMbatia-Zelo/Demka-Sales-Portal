using Demka.Portal.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Endpoints;

/// <summary>Builds the "My targets" view for one holder from the published targets and uploaded actuals.</summary>
public static class TargetsReadModel
{
    public static async Task<object?> ForHolderAsync(PortalDbContext db, int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var cycle = await db.TargetCycles.AsNoTracking().OrderByDescending(c => c.TargetStart).FirstOrDefaultAsync(ct);
        if (cycle is null || cycle.PublishedVersion == 0) return Empty(user, cycle);
        var version = await db.TargetVersions.AsNoTracking()
            .FirstAsync(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion, ct);

        var months = Enumerable.Range(0, cycle.TargetMonths).Select(i => cycle.TargetStart.AddMonths(i)).ToArray();
        var baselineMonths = Enumerable.Range(0, 9).Select(i => cycle.BaselineStart.AddMonths(i)).ToArray();

        var targets = await db.OutletTargets.AsNoTracking()
            .Where(t => t.TargetVersionId == version.Id && t.HolderUserId == userId)
            .ToListAsync(ct);
        var outletIds = targets.Select(t => t.OutletId).Distinct().ToList();

        var outlets = await db.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var routes = await db.Allocations.AsNoTracking()
            .Where(a => a.ValidTo == null && outletIds.Contains(a.OutletId))
            .ToDictionaryAsync(a => a.OutletId, a => a.Route, ct);
        var firstBaseline = baselineMonths[0];
        var lastTarget = months[^1];
        var sales = (await db.SalesActuals.AsNoTracking()
                .Where(s => outletIds.Contains(s.OutletId) && s.Month >= firstBaseline && s.Month <= lastTarget)
                .ToListAsync(ct))
            .ToLookup(s => s.OutletId);

        var lastLedgerUpload = await db.UploadBatches.AsNoTracking()
            .Where(b => b.Type == UploadType.LedgerEntries)
            .OrderByDescending(b => b.UploadedAt)
            .Select(b => new { b.UploadedAt, b.PeriodTo })
            .FirstOrDefaultAsync(ct);

        // Solutech check-ins at these outlets in the target months (anyone's), and the last check-in ever.
        var nairobi = TimeSpan.FromHours(3);
        var visitFrom = new DateTimeOffset(months[0].ToDateTime(TimeOnly.MinValue), nairobi);
        var visitTo = new DateTimeOffset(lastTarget.AddMonths(1).ToDateTime(TimeOnly.MinValue), nairobi);
        var visits = (await db.Visits.AsNoTracking()
                .Where(v => v.OutletId != null && outletIds.Contains(v.OutletId.Value) && v.VisitedAt >= visitFrom && v.VisitedAt < visitTo)
                .Select(v => new { OutletId = v.OutletId!.Value, v.VisitedAt, v.UserId })
                .ToListAsync(ct))
            .ToLookup(v => v.OutletId);
        var lastVisits = await db.Visits.AsNoTracking()
            .Where(v => v.OutletId != null && outletIds.Contains(v.OutletId.Value))
            .GroupBy(v => v.OutletId!.Value)
            .Select(g => new { OutletId = g.Key, Last = g.Max(v => v.VisitedAt) })
            .ToDictionaryAsync(x => x.OutletId, x => x.Last, ct);
        var lastSolutech = await db.UploadBatches.AsNoTracking()
            .Where(b => b.Type == UploadType.SolutechCheckIns)
            .OrderByDescending(b => b.UploadedAt)
            .Select(b => b.PeriodTo)
            .FirstOrDefaultAsync(ct);
        int[] VisitsByMonth(int outletId, bool mine) => months.Select(m => visits[outletId].Count(v =>
            (!mine || v.UserId == userId) && v.VisitedAt.ToOffset(nairobi).Year == m.Year && v.VisitedAt.ToOffset(nairobi).Month == m.Month)).ToArray();

        var rows = targets.GroupBy(t => t.OutletId).Select(g =>
        {
            var o = outlets[g.Key];
            var s = sales[g.Key].ToDictionary(x => x.Month, x => x.Amount);
            var first = g.First();
            var monthRows = months.Select(m => new
            {
                month = m,
                target = Math.Round(g.Where(t => t.Month == m).Sum(t => t.Amount), 2),
                actual = s.TryGetValue(m, out var a) ? Math.Round(a, 2) : (decimal?)null
            }).ToList();
            return new
            {
                id = o.Id,
                name = o.Name,
                chain = o.Chain,
                region = o.Region,
                status = o.Status,
                route = routes.GetValueOrDefault(o.Id),
                months = monthRows,
                q4Target = monthRows.Sum(x => x.target),
                q4Actual = monthRows.Sum(x => x.actual ?? 0),
                q3Actual = Math.Round(baselineMonths[6..].Sum(m => s.GetValueOrDefault(m)), 2),
                baselinePerMonth = Math.Round(first.BaselinePerMonth, 2),
                growth = first.Growth,
                recovery = first.Recovery,
                recoveryUpliftPerMonth = Math.Round(first.RecoveryUpliftPerMonth, 2),
                visits = VisitsByMonth(o.Id, false),
                myVisits = VisitsByMonth(o.Id, true),
                lastVisit = lastVisits.TryGetValue(o.Id, out var lv) ? DateOnly.FromDateTime(lv.ToOffset(nairobi).DateTime) : (DateOnly?)null,
                history = baselineMonths.Select(m => new { month = m, amount = Math.Round(s.GetValueOrDefault(m), 2) })
            };
        }).OrderByDescending(x => x.q4Target).ToList();

        return new
        {
            holder = new { user.Id, name = user.FullName, role = user.Role.ToString(), user.Region },
            cycle = cycle.Name,
            version = version.Version,
            publishedAt = version.PublishedAt,
            months,
            actualsThrough = lastLedgerUpload?.PeriodTo,
            actualsUploadedAt = lastLedgerUpload?.UploadedAt,
            visitsThrough = lastSolutech,
            totals = new
            {
                q4Target = rows.Sum(r => r.q4Target),
                q4Actual = rows.Sum(r => r.q4Actual),
                q3Actual = rows.Sum(r => r.q3Actual),
                months = months.Select((m, i) => new
                {
                    month = m,
                    target = rows.Sum(r => r.months[i].target),
                    actual = rows.Any(r => r.months[i].actual is not null) ? rows.Sum(r => r.months[i].actual ?? 0) : (decimal?)null,
                    visits = rows.Sum(r => r.visits[i]),
                    myVisits = rows.Sum(r => r.myVisits[i])
                })
            },
            outlets = rows
        };
    }

    private static object Empty(AppUser user, TargetCycle? cycle) => new
    {
        holder = new { user.Id, name = user.FullName, role = user.Role.ToString(), user.Region },
        cycle = cycle?.Name,
        version = 0,
        months = Array.Empty<DateOnly>(),
        outlets = Array.Empty<object>()
    };
}
