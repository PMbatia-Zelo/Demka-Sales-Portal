using System.Text.Json;
using Demka.Portal.Core.Seed;
using Demka.Portal.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Data;

/// <summary>
/// Fills an empty database with the Q4 2026 starting data (outlets, users, allocation, Jan–Sep sales,
/// assumptions) and publishes targets version 1. Does nothing if outlets already exist.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(PortalDbContext db, string seedFolder, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Outlets.AnyAsync(ct))
        {
            logger.LogInformation("Seed skipped: the database already has outlets.");
            return;
        }

        var seed = SeedLoader.LoadSeed(Path.Combine(seedFolder, "seed.json"));
        var assumptionsFile = SeedLoader.LoadAssumptions(Path.Combine(seedFolder, "assumptions.json"));
        var baselineStart = ParseMonth(assumptionsFile.BaselineMonths[0]);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Users: merchandisers and team leaders from the workbook, plus one national admin placeholder.
        var users = seed.Users.Select(u => new AppUser
        {
            FullName = u.Name,
            Region = u.Region,
            Role = u.Role == "Merchandiser" ? UserRole.Merchandiser : UserRole.TeamLeader
        }).ToList();
        users.Add(new AppUser { FullName = "Modern Trade Admin", Role = UserRole.NationalAdmin });

        // Any holder missing from the user list becomes a user, so no outlet silently loses its holder.
        var missingHolders = seed.Outlets
            .Where(o => o.Holder is not null && users.All(u => u.FullName != o.Holder))
            .GroupBy(o => o.Holder!).Select(g => g.First()).ToList();
        foreach (var o in missingHolders)
        {
            logger.LogWarning("Holder {Holder} was not in the user list; adding as {Type}.", o.Holder, o.HolderType);
            users.Add(new AppUser
            {
                FullName = o.Holder!,
                Region = o.Region,
                Role = o.HolderType == "Team leader" ? UserRole.TeamLeader : UserRole.Merchandiser
            });
        }
        db.Users.AddRange(users);
        await db.SaveChangesAsync(ct);
        var userIds = users.ToDictionary(u => u.FullName, u => u.Id);

        var cycleStart = ParseMonth(assumptionsFile.Assumptions.Phasing[0].Name);
        var cycle = new TargetCycle
        {
            Name = assumptionsFile.Cycle,
            BaselineStart = baselineStart,
            TargetStart = cycleStart,
            TargetMonths = assumptionsFile.Assumptions.Phasing.Count,
            AssumptionsJson = JsonSerializer.Serialize(assumptionsFile.Assumptions, SeedLoader.Json),
            Status = CycleStatus.Draft
        };
        db.TargetCycles.Add(cycle);
        await db.SaveChangesAsync(ct);

        foreach (var o in seed.Outlets)
        {
            db.Outlets.Add(new Outlet
            {
                Id = o.Id,
                Name = o.Name,
                Chain = o.Chain,
                Region = o.Region,
                RegionBasis = o.RegionBasis,
                HqCreditAccount = o.HqCreditAccount,
                InScope = o.InScope,
                Status = o.ParsedStatus.ToString()
            });

            db.Allocations.Add(new Allocation
            {
                OutletId = o.Id,
                HolderUserId = o.Holder is null ? null : userIds[o.Holder],
                Route = o.Route,
                VisitsInWindow = o.Visits,
                ValidFrom = cycleStart,
                Source = AllocationSource.Seed
            });

            for (var i = 0; i < o.Monthly.Length; i++)
            {
                if (o.Monthly[i] == 0) continue;
                db.SalesActuals.Add(new SalesActual
                {
                    OutletId = o.Id,
                    Month = baselineStart.AddMonths(i),
                    Amount = o.Monthly[i],
                    IsProvisional = i == o.Monthly.Length - 1 // September was provisional at extraction
                });
            }

            if (o.BaselineOverride is not null)
                db.OutletOverrides.Add(new OutletOverride
                {
                    CycleId = cycle.Id,
                    OutletId = o.Id,
                    BaselinePerMonth = o.BaselineOverride,
                    Note = "Set in the Q4 2026 workbook"
                });
        }
        await db.SaveChangesAsync(ct);

        var version = await new TargetService(db).PublishAsync(cycle, null, "Initial load from the Q4 2026 workbook", ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Seeded {Outlets} outlets and {Users} users; published {Cycle} v{Version}, total KES {Total:N2}.",
            seed.Outlets.Count, users.Count, cycle.Name, version.Version, version.Total);
    }

    /// <summary>Parses "yyyy-MM" into the first day of that month.</summary>
    private static DateOnly ParseMonth(string yyyyMm) =>
        DateOnly.ParseExact(yyyyMm + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
