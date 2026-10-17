using System.Text.Json;
using Demka.Portal.Core.Seed;
using Demka.Portal.Core.Targets;
using Demka.Portal.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Services;

/// <summary>Feeds the database into the target engine, and publishes results as a locked version.</summary>
public sealed class TargetService(PortalDbContext db)
{
    public async Task<TargetCycle> GetCycleAsync(string? name = null, CancellationToken ct = default) =>
        await db.TargetCycles
            .Where(c => name == null || c.Name == name)
            .OrderByDescending(c => c.TargetStart)
            .FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException("No target cycle exists yet.");

    public static TargetAssumptions ReadAssumptions(TargetCycle cycle) =>
        JsonSerializer.Deserialize<TargetAssumptions>(cycle.AssumptionsJson, SeedLoader.Json)
        ?? throw new InvalidDataException($"Cycle {cycle.Name} has unreadable assumptions.");

    /// <summary>Runs the engine on the cycle's draft assumptions and the current allocation.</summary>
    public async Task<(TargetCalculation Result, Dictionary<int, int?> Holders)> CalculateAsync(TargetCycle cycle, CancellationToken ct = default)
    {
        var assumptions = ReadAssumptions(cycle);
        var firstMonth = cycle.BaselineStart;
        var lastMonth = firstMonth.AddMonths(8);

        var outlets = await db.Outlets.AsNoTracking().ToListAsync(ct);
        var holders = await db.Allocations.AsNoTracking()
            .Where(a => a.ValidTo == null)
            .ToDictionaryAsync(a => a.OutletId, a => a.HolderUserId, ct);
        var sales = await db.SalesActuals.AsNoTracking()
            .Where(s => s.Month >= firstMonth && s.Month <= lastMonth)
            .ToListAsync(ct);
        var overrides = await db.OutletOverrides.AsNoTracking()
            .Where(o => o.CycleId == cycle.Id)
            .ToDictionaryAsync(o => o.OutletId, o => o.BaselinePerMonth, ct);

        var salesByOutlet = sales.ToLookup(s => s.OutletId);
        var inputs = outlets.Select(o =>
        {
            var months = new decimal[9];
            foreach (var s in salesByOutlet[o.Id])
                months[(s.Month.Year - firstMonth.Year) * 12 + s.Month.Month - firstMonth.Month] += s.Amount;
            return new OutletInput
            {
                OutletId = o.Id,
                Region = o.Region,
                Status = ParseStatus(o.Status),
                InScope = o.InScope,
                HasHolder = holders.TryGetValue(o.Id, out var h) && h is not null,
                BaselineMonths = months,
                BaselineOverride = overrides.TryGetValue(o.Id, out var ovr) ? ovr : null
            };
        }).ToList();

        // Merchandisers holding at least one outlet, by their own region (as the workbook counts them).
        var holderIds = holders.Values.Where(v => v is not null).Select(v => v!.Value).Distinct().ToList();
        var merchandisers = await db.Users.AsNoTracking()
            .Where(u => holderIds.Contains(u.Id) && u.Role == UserRole.Merchandiser && u.Region != null)
            .GroupBy(u => u.Region!)
            .Select(g => new { Region = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Region, x => x.Count, StringComparer.OrdinalIgnoreCase, ct);

        return (TargetEngine.Calculate(inputs, assumptions, merchandisers), holders);
    }

    /// <summary>Locks the current draft as a new published version that users will see.</summary>
    public async Task<TargetVersion> PublishAsync(TargetCycle cycle, int? userId, string? note, CancellationToken ct = default)
    {
        var (result, holders) = await CalculateAsync(cycle, ct);
        var months = Enumerable.Range(0, cycle.TargetMonths).Select(i => cycle.TargetStart.AddMonths(i)).ToArray();

        var version = new TargetVersion
        {
            CycleId = cycle.Id,
            Version = cycle.PublishedVersion + 1,
            AssumptionsJson = cycle.AssumptionsJson,
            Total = result.Total,
            PublishedAt = DateTimeOffset.UtcNow,
            PublishedByUserId = userId,
            Note = note
        };
        db.TargetVersions.Add(version);
        await db.SaveChangesAsync(ct);

        foreach (var o in result.Outlets.Where(o => o.Target != 0))
            for (var i = 0; i < months.Length; i++)
                db.OutletTargets.Add(new OutletTarget
                {
                    TargetVersionId = version.Id,
                    OutletId = o.OutletId,
                    HolderUserId = holders.GetValueOrDefault(o.OutletId),
                    Month = months[i],
                    Amount = o.MonthlyTargets[i],
                    BaselinePerMonth = o.BaselinePerMonth,
                    Growth = o.Growth,
                    Recovery = o.Recovery,
                    RecoveryUpliftPerMonth = o.RecoveryUpliftPerMonth
                });

        foreach (var r in result.Regions)
            for (var i = 0; i < months.Length; i++)
                db.RegionTargets.Add(new RegionTarget
                {
                    TargetVersionId = version.Id,
                    Region = r.Region,
                    Month = months[i],
                    Target = r.MonthlyTargets[i],
                    Expansion = r.Expansion * result.PhasingWeights[i]
                });

        cycle.PublishedVersion = version.Version;
        cycle.PublishedAt = version.PublishedAt;
        cycle.PublishedByUserId = userId;
        cycle.Status = CycleStatus.Published;
        db.TargetCycles.Update(cycle);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "PublishTargets",
            Entity = nameof(TargetCycle),
            EntityId = cycle.Name,
            NewValue = JsonSerializer.Serialize(new { version.Version, version.Total, note })
        });
        await db.SaveChangesAsync(ct);
        return version;
    }

    private static OutletStatus ParseStatus(string s) => s switch
    {
        "Active" => OutletStatus.Active,
        "New" => OutletStatus.New,
        "Ceased" => OutletStatus.Ceased,
        "NotTrading" or "Not trading" => OutletStatus.NotTrading,
        _ => OutletStatus.Active
    };
}
