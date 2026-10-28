using System.Threading.RateLimiting;
using Demka.Portal.Web.Auth;
using Demka.Portal.Web.Data;
using Demka.Portal.Web.Endpoints;
using Demka.Portal.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var setupDatabase = args.Contains("--setup-database");
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--setup-database").ToArray());

builder.Services.AddDbContext<PortalDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Portal")
                   ?? throw new InvalidOperationException("Connection string 'Portal' is missing.")));
builder.Services.AddScoped<TargetService>();
builder.Services.AddScoped<LedgerImportService>();
builder.Services.AddScoped<SolutechImportService>();
// Ledger exports are large (the 18 Oct full export is 40 MB).
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = DataEndpoints.MaxUploadBytes);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Cookie encryption keys: keep them in a folder on the server so sign-ins survive restarts.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
var dp = builder.Services.AddDataProtection().SetApplicationName("DemkaSalesPortal");
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(); // keys encrypted at rest for the app pool account
}

builder.Services.AddPortalAuth(builder.Environment);

// At most 10 sign-in attempts per minute from one address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Apply migrations and load the starting data when configured (on by default in Development).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    // "--setup-database": create or update the database and load the starting data, then exit.
    // Run by an administrator during deployment, so the website itself never needs rights to change tables.
    if (setupDatabase || app.Configuration.GetValue("Database:MigrateAndSeedOnStartup", false))
    {
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db, Path.Combine(AppContext.BaseDirectory, "seed", "q4-2026"), logger);
    }
    await LedgerImportService.SeedAliasesAsync(db, Path.Combine(AppContext.BaseDirectory, "seed", "q4-2026", "customer-aliases.json"), logger);
    await BootstrapAdmin.RunAsync(db, app.Configuration, logger);
    if (setupDatabase)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync();
        logger.LogWarning("Database ready: {Count} migrations applied, latest {Latest}.", applied.Count(), applied.LastOrDefault());
        return;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Basic security headers on every response.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["X-Frame-Options"] = "DENY";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; manifest-src 'self'; worker-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c =>
    {
        // The app shell and service worker must always be revalidated so updates reach phones.
        var name = c.File.Name;
        if (name is "index.html" or "sw.js" or "manifest.webmanifest")
            c.Context.Response.Headers.CacheControl = "no-cache";
    }
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", async (PortalDbContext db) =>
{
    var cycle = await db.TargetCycles.OrderByDescending(c => c.TargetStart).FirstOrDefaultAsync();
    var version = cycle is null ? null : await db.TargetVersions
        .Where(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion).FirstOrDefaultAsync();
    return Results.Ok(new
    {
        status = "ok",
        outlets = await db.Outlets.CountAsync(),
        users = await db.Users.CountAsync(),
        cycle = cycle?.Name,
        publishedVersion = cycle?.PublishedVersion,
        publishedTotalKes = version is null ? (decimal?)null : Math.Round(version.Total, 2)
    });
});

app.MapAuthEndpoints();
app.MapPortalEndpoints();
app.MapDataEndpoints();
app.MapNationalEndpoints();

// Any other non-API path returns the app shell, so deep links work.
app.MapFallbackToFile("index.html");

app.Run();
