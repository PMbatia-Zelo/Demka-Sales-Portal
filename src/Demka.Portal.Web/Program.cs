using Demka.Portal.Web.Data;
using Demka.Portal.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PortalDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Portal")
                   ?? throw new InvalidOperationException("Connection string 'Portal' is missing.")));
builder.Services.AddScoped<TargetService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

// Apply database migrations and load the starting data when configured (on by default in Development).
if (app.Configuration.GetValue("Database:MigrateAndSeedOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, Path.Combine(AppContext.BaseDirectory, "seed", "q4-2026"), logger);
}

app.MapGet("/", () => Results.Text("Demka Sales Capability Portal API is running. Try /api/health"));

app.MapGet("/api/health", async (PortalDbContext db) =>
{
    var cycle = await db.TargetCycles.OrderByDescending(c => c.TargetStart).FirstOrDefaultAsync();
    var version = cycle is null ? null : await db.TargetVersions
        .Where(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion).FirstOrDefaultAsync();
    return Results.Ok(new
    {
        status = "ok",
        database = db.Database.GetDbConnection().Database,
        outlets = await db.Outlets.CountAsync(),
        users = await db.Users.CountAsync(),
        salesRows = await db.SalesActuals.CountAsync(),
        cycle = cycle?.Name,
        publishedVersion = cycle?.PublishedVersion,
        publishedTotalKes = version is null ? (decimal?)null : Math.Round(version.Total, 2)
    });
});

// Published targets by region and month (what users see).
app.MapGet("/api/targets/summary", async (PortalDbContext db, TargetService targets) =>
{
    var cycle = await targets.GetCycleAsync();
    var version = await db.TargetVersions.SingleAsync(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion);
    var regions = await db.RegionTargets.Where(r => r.TargetVersionId == version.Id)
        .GroupBy(r => r.Region)
        .Select(g => new { region = g.Key, target = g.Sum(x => x.Target), expansion = g.Sum(x => x.Expansion) })
        .ToListAsync();
    var months = await db.RegionTargets.Where(r => r.TargetVersionId == version.Id)
        .GroupBy(r => r.Month)
        .Select(g => new { month = g.Key, target = g.Sum(x => x.Target) })
        .OrderBy(x => x.month)
        .ToListAsync();
    return Results.Ok(new { cycle = cycle.Name, version = version.Version, total = version.Total, regions, months });
});

// One holder's outlet targets (the merchandiser "My targets" screen will use this once sign-in exists).
app.MapGet("/api/holders/{userId:int}/targets", async (int userId, PortalDbContext db, TargetService targets) =>
{
    var cycle = await targets.GetCycleAsync();
    var version = await db.TargetVersions.SingleAsync(v => v.CycleId == cycle.Id && v.Version == cycle.PublishedVersion);
    var user = await db.Users.FindAsync(userId);
    if (user is null) return Results.NotFound(new { error = $"No user with id {userId}." });

    var rows = await db.OutletTargets
        .Where(t => t.TargetVersionId == version.Id && t.HolderUserId == userId)
        .Select(t => new { t.OutletId, outlet = t.Outlet!.Name, t.Month, t.Amount, t.BaselinePerMonth, t.Growth, t.Recovery })
        .ToListAsync();
    var outlets = rows.GroupBy(r => new { r.OutletId, r.outlet })
        .Select(g => new
        {
            id = g.Key.OutletId,
            name = g.Key.outlet,
            q4 = g.Sum(x => x.Amount),
            months = g.OrderBy(x => x.Month).Select(x => new { x.Month, x.Amount }),
            baselinePerMonth = g.First().BaselinePerMonth,
            growth = g.First().Growth,
            recovery = g.First().Recovery
        })
        .OrderByDescending(o => o.q4);
    return Results.Ok(new { holder = user.FullName, user.Region, cycle = cycle.Name, version = version.Version, total = rows.Sum(r => r.Amount), outlets });
});

// Admin endpoints. Development only until sign-in and roles arrive in the next increment.
if (app.Environment.IsDevelopment())
{
    var admin = app.MapGroup("/api/admin");

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
                r.Region, r.CurrentMerchandisers,
                bottomUpOutlets = Math.Round(r.BottomUpOutlets, 2),
                expansion = Math.Round(r.Expansion, 2),
                target = Math.Round(r.Target, 2)
            }),
            recoveryOutlets = result.Outlets.Count(o => o.Recovery)
        });
    });

    admin.MapPost("/targets/publish", async (TargetService targets, string? note) =>
    {
        var cycle = await targets.GetCycleAsync();
        var v = await targets.PublishAsync(cycle, null, note);
        return Results.Ok(new { cycle = cycle.Name, version = v.Version, total = Math.Round(v.Total, 2) });
    });
}

app.Run();
