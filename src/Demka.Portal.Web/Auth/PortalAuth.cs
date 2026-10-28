using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Demka.Portal.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Auth;

public static class PortalClaims
{
    public const string UserId = ClaimTypes.NameIdentifier;
    public const string Region = "region";
    public const string MustChangePin = "must_change_pin";
    /// <summary>Fingerprint of the PIN hash: changes when the PIN is reset, which signs out older sessions.</summary>
    public const string PinStamp = "pin_stamp";
}

public static class Policies
{
    /// <summary>Signed in, even if a PIN change is still required (sign-out, change PIN, who am I).</summary>
    public const string SignedIn = "SignedIn";
    /// <summary>Signed in with a personal PIN: field screens.</summary>
    public const string Ready = "Ready";
    public const string TeamLead = "TeamLead";
    public const string Admin = "Admin";
    /// <summary>National read-only view: national admins and national viewers.</summary>
    public const string National = "National";
}

public static class PortalAuth
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan SessionLength = TimeSpan.FromDays(30);

    public static IServiceCollection AddPortalAuth(this IServiceCollection services, IWebHostEnvironment env)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "demka.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = SessionLength;
                o.SlidingExpiration = true;
                // This is an API: answer 401/403 instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = ValidateSessionAsync;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.SignedIn, p => p.RequireAuthenticatedUser())
            .AddPolicy(Policies.Ready, p => p.RequireAuthenticatedUser().RequireAssertion(NoPinChangePending))
            .AddPolicy(Policies.TeamLead, p => p.RequireAuthenticatedUser().RequireAssertion(NoPinChangePending)
                .RequireRole(nameof(UserRole.TeamLeader), nameof(UserRole.RegionalManager), nameof(UserRole.NationalAdmin)))
            .AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().RequireAssertion(NoPinChangePending)
                .RequireRole(nameof(UserRole.NationalAdmin)))
            .AddPolicy(Policies.National, p => p.RequireAuthenticatedUser().RequireAssertion(NoPinChangePending)
                .RequireRole(nameof(UserRole.NationalAdmin), nameof(UserRole.NationalViewer)));

        return services;
    }

    private static bool NoPinChangePending(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext ctx) =>
        ctx.User.FindFirstValue(PortalClaims.MustChangePin) != "true";

    public static string PinStamp(string? pinHash) =>
        pinHash is null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pinHash)))[..16];

    public static ClaimsPrincipal BuildPrincipal(AppUser u) => new(new ClaimsIdentity(
    [
        new Claim(PortalClaims.UserId, u.Id.ToString()),
        new Claim(ClaimTypes.Name, u.FullName),
        new Claim(ClaimTypes.Role, u.Role.ToString()),
        new Claim(PortalClaims.Region, u.Region ?? ""),
        new Claim(PortalClaims.MustChangePin, u.MustChangePin ? "true" : "false"),
        new Claim(PortalClaims.PinStamp, PinStamp(u.PinHash))
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static Task SignInAsync(HttpContext http, AppUser u) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, BuildPrincipal(u),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

    /// <summary>Every request: the user must still exist, be active, unlocked, and have the same PIN.</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var id = ctx.Principal?.FindFirstValue(PortalClaims.UserId);
        if (!int.TryParse(id, out var userId)) { await Reject(ctx); return; }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<PortalDbContext>();
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (u is null || !u.IsActive || (u.LockedUntil is { } l && l > DateTimeOffset.UtcNow)
            || PinStamp(u.PinHash) != ctx.Principal!.FindFirstValue(PortalClaims.PinStamp)
            || u.Role.ToString() != ctx.Principal!.FindFirstValue(ClaimTypes.Role))
        {
            await Reject(ctx);
        }

        static async Task Reject(CookieValidatePrincipalContext c)
        {
            c.RejectPrincipal();
            await c.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(PortalClaims.UserId) ?? throw new InvalidOperationException("Not signed in."));
}
