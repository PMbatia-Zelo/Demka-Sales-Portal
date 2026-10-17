using System.Security.Claims;
using Demka.Portal.Core.Security;
using Demka.Portal.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Auth;

public sealed record LoginRequest(string? Phone, string? Pin);
public sealed record ChangePinRequest(string? CurrentPin, string? NewPin);

public static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    // Used so that unknown phone numbers take as long to check as real ones.
    private static readonly string DummyHash = PinHasher.Hash("000000");

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/login", async (LoginRequest req, PortalDbContext db, HttpContext http) =>
        {
            const string wrong = "Phone number or PIN is not right.";
            var phone = PhoneNumber.Normalize(req.Phone);
            var pin = req.Pin ?? "";
            var user = phone is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Phone == phone && u.IsActive);

            if (user is null || user.PinHash is null)
            {
                PinHasher.Verify(pin, DummyHash);
                return Results.Json(new { error = wrong }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (user.LockedUntil is { } locked && locked > DateTimeOffset.UtcNow)
                return Results.Json(new { error = "This account is locked after too many wrong PINs. Ask your team leader or the Modern Trade office to reset your PIN." },
                    statusCode: StatusCodes.Status423Locked);

            if (!PinHasher.Verify(pin, user.PinHash))
            {
                user.FailedPinAttempts++;
                var left = PortalAuth.MaxFailedAttempts - user.FailedPinAttempts;
                if (left <= 0)
                {
                    user.LockedUntil = DateTimeOffset.MaxValue; // stays locked until an admin resets the PIN
                    db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "AccountLocked", Entity = nameof(AppUser), EntityId = user.Id.ToString() });
                }
                await db.SaveChangesAsync();
                return Results.Json(new { error = left > 0 ? $"{wrong} {left} tries left." : "Too many wrong PINs. This account is now locked. Ask your team leader or the Modern Trade office to reset your PIN." },
                    statusCode: left > 0 ? StatusCodes.Status401Unauthorized : StatusCodes.Status423Locked);
            }

            user.FailedPinAttempts = 0;
            db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "SignIn", Entity = nameof(AppUser), EntityId = user.Id.ToString() });
            await db.SaveChangesAsync();
            await PortalAuth.SignInAsync(http, user);
            return Results.Ok(Me(user));
        }).RequireRateLimiting(LoginRateLimit);

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        auth.MapPost("/change-pin", async (ChangePinRequest req, ClaimsPrincipal principal, PortalDbContext db, HttpContext http) =>
        {
            var user = await db.Users.FindAsync(principal.UserId());
            if (user is null) return Results.Unauthorized();
            if (!PinHasher.Verify(req.CurrentPin ?? "", user.PinHash))
                return Results.Json(new { error = "Your current PIN is not right." }, statusCode: StatusCodes.Status400BadRequest);
            if (PinPolicy.Check(req.NewPin) is { } problem)
                return Results.Json(new { error = problem }, statusCode: StatusCodes.Status400BadRequest);
            if (req.NewPin == req.CurrentPin)
                return Results.Json(new { error = "Choose a PIN different from the one you were given." }, statusCode: StatusCodes.Status400BadRequest);

            user.PinHash = PinHasher.Hash(req.NewPin!);
            user.MustChangePin = false;
            db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "ChangePin", Entity = nameof(AppUser), EntityId = user.Id.ToString() });
            await db.SaveChangesAsync();
            await PortalAuth.SignInAsync(http, user); // refresh the cookie with the new PIN stamp
            return Results.Ok(Me(user));
        }).RequireAuthorization(Policies.SignedIn);

        app.MapGet("/api/me", async (ClaimsPrincipal principal, PortalDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == principal.UserId());
            return user is null ? Results.Unauthorized() : Results.Ok(Me(user));
        }).RequireAuthorization(Policies.SignedIn);
    }

    private static object Me(AppUser u) => new
    {
        id = u.Id,
        name = u.FullName,
        role = u.Role.ToString(),
        region = u.Region,
        mustChangePin = u.MustChangePin
    };
}
