using Demka.Portal.Core.Security;
using Demka.Portal.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Auth;

/// <summary>
/// Gives the first national admin a phone number and PIN so someone can sign in and issue PINs to everyone else.
/// Reads Bootstrap:AdminPhone and Bootstrap:AdminPin (set with user-secrets on a dev PC, or as environment
/// variables on the server). Only acts while the admin has no PIN, so it is safe to leave configured.
/// </summary>
public static class BootstrapAdmin
{
    public static async Task RunAsync(PortalDbContext db, IConfiguration config, ILogger logger)
    {
        var phone = PhoneNumber.Normalize(config["Bootstrap:AdminPhone"]);
        var pin = config["Bootstrap:AdminPin"];
        if (phone is null || string.IsNullOrEmpty(pin)) return;

        var admin = await db.Users.OrderBy(u => u.Id).FirstOrDefaultAsync(u => u.Role == UserRole.NationalAdmin);
        if (admin is null || admin.PinHash is not null) return;

        if (PinPolicy.Check(pin) is { } problem)
        {
            logger.LogError("Bootstrap admin PIN not accepted: {Problem}", problem);
            return;
        }
        if (await db.Users.AnyAsync(u => u.Phone == phone && u.Id != admin.Id))
        {
            logger.LogError("Bootstrap admin phone is already used by another user.");
            return;
        }

        admin.Phone = phone;
        admin.PinHash = PinHasher.Hash(pin);
        admin.MustChangePin = true;
        db.AuditLogs.Add(new AuditLog { Action = "BootstrapAdmin", Entity = nameof(AppUser), EntityId = admin.Id.ToString() });
        await db.SaveChangesAsync();
        logger.LogWarning("Bootstrap: national admin {Name} can now sign in with {Phone}; a PIN change is required at first sign-in.",
            admin.FullName, PhoneNumber.Mask(phone));
    }
}
