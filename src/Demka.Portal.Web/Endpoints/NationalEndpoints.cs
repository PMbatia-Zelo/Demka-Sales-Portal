using System.Security.Claims;
using Demka.Portal.Core.Security;
using Demka.Portal.Web.Auth;
using Demka.Portal.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Endpoints;

public sealed record CreateUserRequest(string? Name, string? Role, string? Region, string? Phone);
public sealed record UpdateUserRequest(string? Name, string? Role, string? Region, bool? Active);

/// <summary>National view (read-only, for national admins and viewers) and user management (admins).</summary>
public static class NationalEndpoints
{
    public static readonly string[] Regions = ["Nairobi", "Central", "Western", "Coast"];

    public static void MapNationalEndpoints(this IEndpointRouteBuilder app)
    {
        var national = app.MapGroup("/api/national").RequireAuthorization(Policies.National);
        national.MapGet("/overview", async (PortalDbContext db, CancellationToken ct) => Results.Ok(await OverviewAsync(db, ct)));
        national.MapGet("/holders/{userId:int}/targets", async (int userId, PortalDbContext db, CancellationToken ct) =>
            await TargetsReadModel.ForHolderAsync(db, userId, ct) is { } model ? Results.Ok(model) : Results.NotFound());

        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policies.Admin);

        admin.MapPost("/users", async (CreateUserRequest req, ClaimsPrincipal me, PortalDbContext db) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length < 3) return Results.BadRequest(new { error = "Enter the person's full name." });
            if (!Enum.TryParse<UserRole>(req.Role, out var role)) return Results.BadRequest(new { error = "Choose a role." });
            if (await db.Users.AnyAsync(u => u.FullName == name)) return Results.Conflict(new { error = "A user with this name already exists." });
            string? phone = null;
            if (!string.IsNullOrWhiteSpace(req.Phone))
            {
                phone = PhoneNumber.Normalize(req.Phone);
                if (phone is null) return Results.BadRequest(new { error = "Enter a Kenyan mobile number, e.g. 0712 345 678." });
                if (await db.Users.AnyAsync(u => u.Phone == phone)) return Results.Conflict(new { error = "Another user already has this phone number." });
            }
            var user = new AppUser { FullName = name, Role = role, Region = CleanRegion(req.Region), Phone = phone };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.AuditLogs.Add(new AuditLog { UserId = me.UserId(), Action = "CreateUser", Entity = nameof(AppUser), EntityId = user.Id.ToString(), NewValue = $"{name} | {role} | {user.Region}" });
            await db.SaveChangesAsync();
            return Results.Ok(new { user.Id, name = user.FullName, role = user.Role.ToString(), user.Region, user.Phone });
        });

        admin.MapPut("/users/{id:int}", async (int id, UpdateUserRequest req, ClaimsPrincipal me, PortalDbContext db) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null) return Results.NotFound(new { error = "User not found." });
            var old = $"{user.FullName} | {user.Role} | {user.Region} | active={user.IsActive}";
            if (req.Role is not null)
            {
                if (!Enum.TryParse<UserRole>(req.Role, out var role)) return Results.BadRequest(new { error = "Unknown role." });
                if (id == me.UserId() && role != user.Role) return Results.BadRequest(new { error = "You can't change your own role. Ask another admin." });
                user.Role = role;
            }
            if (req.Name is not null)
            {
                var name = req.Name.Trim();
                if (name.Length < 3) return Results.BadRequest(new { error = "Enter the person's full name." });
                user.FullName = name;
            }
            if (req.Region is not null) user.Region = CleanRegion(req.Region);
            if (req.Active is { } active)
            {
                if (id == me.UserId() && !active) return Results.BadRequest(new { error = "You can't deactivate yourself." });
                user.IsActive = active;
            }
            db.AuditLogs.Add(new AuditLog
            {
                UserId = me.UserId(), Action = "UpdateUser", Entity = nameof(AppUser), EntityId = id.ToString(),
                OldValue = old, NewValue = $"{user.FullName} | {user.Role} | {user.Region} | active={user.IsActive}"
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { user.Id, name = user.FullName, role = user.Role.ToString(), user.Region, active = user.IsActive });
        });
    }

    private static string? CleanRegion(string? r) =>
        Regions.FirstOrDefault(x => string.Equals(x, (r ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Targets, sales to date and check-ins for every region and every holder, from the published targets.</summary>
    public static async Task<object> OverviewAsync(PortalDbContext db, CancellationToken ct)
    {
        var cycle = await db.TargetCycles.AsNoTracking().OrderByDescending(c => c.TargetStart).FirstOrDefaultAsync(ct);
        if (cycle is null || cycle.PublishedVersion == 0) return new { cycle = (string?)null, months = Array.Empty<DateOnly>() };
        var version = await db.TargetVersions.AsNoTracking().FirstAsync(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion, ct);
        var months = Enumerable.Range(0, cycle.TargetMonths).Select(i => cycle.TargetStart.AddMonths(i)).ToArray();

        var targets = await db.OutletTargets.AsNoTracking().Where(t => t.TargetVersionId == version.Id)
            .Select(t => new { t.OutletId, t.HolderUserId, t.Month, t.Amount }).ToListAsync(ct);
        var holderOf = targets.GroupBy(t => t.OutletId).ToDictionary(g => g.Key, g => g.First().HolderUserId);
        var regionOf = await db.Outlets.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Region, ct);
        var actuals = await db.SalesActuals.AsNoTracking().Where(s => months.Contains(s.Month))
            .Select(s => new { s.OutletId, s.Month, s.Amount }).ToListAsync(ct);
        var nairobi = TimeSpan.FromHours(3);
        var from = new DateTimeOffset(months[0].ToDateTime(TimeOnly.MinValue), nairobi);
        var to = new DateTimeOffset(months[^1].AddMonths(1).ToDateTime(TimeOnly.MinValue), nairobi);
        var visits = (await db.Visits.AsNoTracking().Where(v => v.OutletId != null && v.VisitedAt >= from && v.VisitedAt < to)
                .Select(v => new { OutletId = v.OutletId!.Value, v.VisitedAt }).ToListAsync(ct))
            .Select(v => (v.OutletId, Month: new DateOnly(v.VisitedAt.ToOffset(nairobi).Year, v.VisitedAt.ToOffset(nairobi).Month, 1))).ToList();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);

        // A holder key per outlet: the user, or "unassigned in <region>".
        string Key(int outletId) => holderOf.GetValueOrDefault(outletId) is { } h ? $"u{h}" : $"r{regionOf.GetValueOrDefault(outletId)}";
        int Mi(DateOnly m) => Array.IndexOf(months, m);

        var rows = new Dictionary<string, (decimal[] T, decimal[] A, int[] V, HashSet<int> Outlets)>();
        (decimal[] T, decimal[] A, int[] V, HashSet<int> Outlets) Row(string k)
        {
            if (!rows.TryGetValue(k, out var r)) rows[k] = r = (new decimal[months.Length], new decimal[months.Length], new int[months.Length], []);
            return r;
        }
        foreach (var t in targets) { var r = Row(Key(t.OutletId)); var i = Mi(t.Month); if (i >= 0) r.T[i] += t.Amount; r.Outlets.Add(t.OutletId); }
        foreach (var a in actuals.Where(a => holderOf.ContainsKey(a.OutletId))) { var i = Mi(a.Month); if (i >= 0) Row(Key(a.OutletId)).A[i] += a.Amount; }
        foreach (var v in visits.Where(v => holderOf.ContainsKey(v.OutletId))) { var i = Mi(v.Month); if (i >= 0) Row(Key(v.OutletId)).V[i]++; }

        var holders = rows.Select(kv =>
        {
            var isUser = kv.Key[0] == 'u';
            var u = isUser ? users.GetValueOrDefault(int.Parse(kv.Key[1..])) : null;
            var region = u?.Region ?? (isUser ? kv.Value.Outlets.Select(o => regionOf.GetValueOrDefault(o)).GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key : kv.Key[1..]);
            return new
            {
                id = u?.Id,
                name = u?.FullName ?? $"Unassigned – {region}",
                role = u?.Role.ToString(),
                region,
                outlets = kv.Value.Outlets.Count,
                targets = kv.Value.T.Select(x => Math.Round(x, 2)).ToArray(),
                actuals = kv.Value.A.Select(x => Math.Round(x, 2)).ToArray(),
                visits = kv.Value.V,
                q4Target = Math.Round(kv.Value.T.Sum(), 2),
                q4Actual = Math.Round(kv.Value.A.Sum(), 2)
            };
        }).OrderBy(h => h.region).ThenByDescending(h => h.q4Target).ToList();

        var regions = holders.GroupBy(h => h.region).Select(g => new
        {
            region = g.Key,
            outlets = g.Sum(h => h.outlets),
            holders = g.Count(h => h.id is not null),
            targets = months.Select((_, i) => g.Sum(h => h.targets[i])).ToArray(),
            actuals = months.Select((_, i) => g.Sum(h => h.actuals[i])).ToArray(),
            visits = months.Select((_, i) => g.Sum(h => h.visits[i])).ToArray(),
            q4Target = g.Sum(h => h.q4Target),
            q4Actual = g.Sum(h => h.q4Actual)
        }).OrderByDescending(r => r.q4Target).ToList();

        var lastLedger = await db.UploadBatches.AsNoTracking().Where(b => b.Type == UploadType.LedgerEntries)
            .OrderByDescending(b => b.UploadedAt).Select(b => b.PeriodTo).FirstOrDefaultAsync(ct);
        var lastVisits = await db.UploadBatches.AsNoTracking().Where(b => b.Type == UploadType.SolutechCheckIns)
            .OrderByDescending(b => b.UploadedAt).Select(b => b.PeriodTo).FirstOrDefaultAsync(ct);

        return new
        {
            cycle = cycle.Name,
            version = version.Version,
            months,
            actualsThrough = lastLedger,
            visitsThrough = lastVisits,
            totals = new
            {
                targets = months.Select((_, i) => regions.Sum(r => r.targets[i])).ToArray(),
                actuals = months.Select((_, i) => regions.Sum(r => r.actuals[i])).ToArray(),
                visits = months.Select((_, i) => regions.Sum(r => r.visits[i])).ToArray(),
                q4Target = regions.Sum(r => r.q4Target),
                q4Actual = regions.Sum(r => r.q4Actual)
            },
            regions,
            holders
        };
    }
}
