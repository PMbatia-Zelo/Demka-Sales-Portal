using System.Security.Claims;
using Demka.Portal.Core.Security;
using Demka.Portal.Web.Auth;
using Demka.Portal.Web.Data;
using Demka.Portal.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Endpoints;

public sealed record SetPhoneRequest(string? Phone);

public static class PortalEndpoints
{
    public static void MapPortalEndpoints(this IEndpointRouteBuilder app)
    {
        // ----- Signed-in users -----
        app.MapGet("/api/me/targets", async (ClaimsPrincipal user, PortalDbContext db, CancellationToken ct) =>
                await TargetsReadModel.ForHolderAsync(db, user.UserId(), ct) is { } model ? Results.Ok(model) : Results.Unauthorized())
            .RequireAuthorization(Policies.Ready);

        // ----- National admin -----
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policies.Admin);

        admin.MapGet("/users", async (PortalDbContext db, CancellationToken ct) =>
        {
            var holderCounts = await db.Allocations.AsNoTracking()
                .Where(a => a.ValidTo == null && a.HolderUserId != null)
                .GroupBy(a => a.HolderUserId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.Region).ThenBy(u => u.FullName).ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            return Results.Ok(users.Select(u => new
            {
                u.Id,
                name = u.FullName,
                role = u.Role.ToString(),
                u.Region,
                u.Phone,
                outlets = holderCounts.GetValueOrDefault(u.Id),
                active = u.IsActive,
                pinStatus = u.PinHash is null ? "Not issued"
                    : u.LockedUntil is { } l && l > now ? "Locked"
                    : u.MustChangePin ? "Issued, not yet changed"
                    : "Active"
            }));
        });

        admin.MapPut("/users/{id:int}/phone", async (int id, SetPhoneRequest req, ClaimsPrincipal me, PortalDbContext db) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null) return Results.NotFound(new { error = "User not found." });
            var phone = PhoneNumber.Normalize(req.Phone);
            if (phone is null) return Results.BadRequest(new { error = "Enter a Kenyan mobile number, e.g. 0712 345 678." });
            if (await db.Users.AnyAsync(u => u.Phone == phone && u.Id != id))
                return Results.Conflict(new { error = "Another user already has this phone number." });
            var old = user.Phone;
            user.Phone = phone;
            db.AuditLogs.Add(new AuditLog { UserId = me.UserId(), Action = "SetPhone", Entity = nameof(AppUser), EntityId = id.ToString(), OldValue = old, NewValue = phone });
            await db.SaveChangesAsync();
            return Results.Ok(new { user.Id, user.Phone });
        });

        admin.MapPost("/users/{id:int}/reset-pin", async (int id, ClaimsPrincipal me, PortalDbContext db) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null) return Results.NotFound(new { error = "User not found." });
            if (user.Phone is null) return Results.BadRequest(new { error = "Set the user's phone number first." });
            var pin = PinHasher.GenerateTemporaryPin();
            user.PinHash = PinHasher.Hash(pin);
            user.MustChangePin = true;
            user.FailedPinAttempts = 0;
            user.LockedUntil = null;
            db.AuditLogs.Add(new AuditLog { UserId = me.UserId(), Action = "ResetPin", Entity = nameof(AppUser), EntityId = id.ToString() });
            await db.SaveChangesAsync();
            // Shown once to the admin, who passes it to the user. Never stored or logged in plain text.
            return Results.Ok(new { user.Id, name = user.FullName, temporaryPin = pin });
        });

        admin.MapGet("/holders/{userId:int}/targets", async (int userId, PortalDbContext db, CancellationToken ct) =>
            await TargetsReadModel.ForHolderAsync(db, userId, ct) is { } model ? Results.Ok(model) : Results.NotFound());

        admin.MapGet("/targets/summary", async (PortalDbContext db, TargetService targets) =>
        {
            var cycle = await targets.GetCycleAsync();
            var version = await db.TargetVersions.SingleAsync(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion);
            var regions = await db.RegionTargets.Where(r => r.TargetVersionId == version.Id)
                .GroupBy(r => r.Region)
                .Select(g => new { region = g.Key, target = g.Sum(x => x.Target), expansion = g.Sum(x => x.Expansion) })
                .ToListAsync();
            return Results.Ok(new { cycle = cycle.Name, version = version.Version, total = version.Total, regions });
        });

        admin.MapGet("/targets/preview", async (TargetService targets) =>
        {
            var cycle = await targets.GetCycleAsync();
            var (result, _) = await targets.CalculateAsync(cycle);
            return Results.Ok(new
            {
                cycle = cycle.Name,
                draftTotal = Math.Round(result.Total, 2),
                regions = result.Regions.Select(r => new
                {
                    r.Region,
                    r.CurrentMerchandisers,
                    bottomUpOutlets = Math.Round(r.BottomUpOutlets, 2),
                    expansion = Math.Round(r.Expansion, 2),
                    target = Math.Round(r.Target, 2)
                }),
                recoveryOutlets = result.Outlets.Count(o => o.Recovery)
            });
        });

        admin.MapPost("/targets/publish", async (TargetService targets, ClaimsPrincipal me, string? note) =>
        {
            var cycle = await targets.GetCycleAsync();
            var v = await targets.PublishAsync(cycle, me.UserId(), note);
            return Results.Ok(new { cycle = cycle.Name, version = v.Version, total = Math.Round(v.Total, 2) });
        });
    }
}
