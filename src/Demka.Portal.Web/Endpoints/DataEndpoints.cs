using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Demka.Portal.Web.Auth;
using Demka.Portal.Web.Data;
using Demka.Portal.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Demka.Portal.Web.Endpoints;

public sealed record LinkRequest(string? CustomerNo, string? NameKey, int? OutletId, bool Ignore);

/// <summary>Admin data screens: ledger upload, names to review, month close.</summary>
public static class DataEndpoints
{
    public const long MaxUploadBytes = 200L * 1024 * 1024;

    public static void MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policies.Admin);

        // Upload a Customer Ledger Entries export (.xlsx or .csv). Form fields: file, refreshFrom (yyyy-MM).
        admin.MapPost("/uploads/ledger", async (HttpRequest req, ClaimsPrincipal me, LedgerImportService import, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "Send the file as a form upload." });
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Choose a file to upload." });
            if (!DateOnly.TryParseExact((form["refreshFrom"].ToString() ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                return Results.BadRequest(new { error = "Choose the first month to refresh." });
            try
            {
                await using var s = file.OpenReadStream();
                return Results.Ok(await import.ImportAsync(s, file.FileName, from, me.UserId(), ct));
            }
            catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes));

        admin.MapGet("/uploads", async (PortalDbContext db) =>
        {
            var rows = await db.UploadBatches.AsNoTracking().OrderByDescending(b => b.UploadedAt).Take(10).ToListAsync();
            var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName);
            return Results.Ok(rows.Select(b => new
            {
                b.Id, type = b.Type.ToString(), b.FileName, b.UploadedAt, b.PeriodTo, b.RowsRead, b.RowsMatched, b.RowsRejected, b.UnmappedCustomers,
                by = b.UploadedByUserId is { } id ? names.GetValueOrDefault(id) : null,
                summary = b.SummaryJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(b.SummaryJson)
            }));
        });

        // Names from the latest ledger upload that could not be placed on an outlet, largest first.
        admin.MapGet("/unmapped", async (PortalDbContext db) =>
        {
            var rows = await db.UnmappedLedgerNames.AsNoTracking()
                .GroupBy(u => new { u.CustomerNo, u.NameKey })
                .Select(g => new
                {
                    g.Key.CustomerNo, g.Key.NameKey,
                    customerName = g.Max(x => x.CustomerName),
                    postingGroup = g.Max(x => x.PostingGroup),
                    amount = g.Sum(x => x.Amount),
                    lines = g.Sum(x => x.Lines),
                    lastMonth = g.Max(x => x.Month)
                })
                .OrderByDescending(x => x.amount)
                .ToListAsync();
            return Results.Ok(rows);
        });

        admin.MapPost("/mappings", async (LinkRequest req, ClaimsPrincipal me, LedgerImportService import, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.CustomerNo) || string.IsNullOrWhiteSpace(req.NameKey))
                return Results.BadRequest(new { error = "Customer number and name are required." });
            try { return Results.Ok(await import.LinkAsync(req.CustomerNo.Trim(), req.NameKey.Trim(), req.OutletId, req.Ignore, me.UserId(), ct)); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        admin.MapGet("/outlets", async (PortalDbContext db, string? q) =>
        {
            var query = db.Outlets.AsNoTracking().Where(o => o.InScope);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(o => o.Name.Contains(q.Trim()));
            var ordered = query.OrderBy(o => o.Name);
            var rows = await (string.IsNullOrWhiteSpace(q) ? ordered : ordered.Take(25)).Select(o => new { o.Id, o.Name, o.Chain, o.Region }).ToListAsync();
            return Results.Ok(rows);
        });

        // Month status: target months of the current cycle, with uploaded totals.
        admin.MapGet("/months", async (PortalDbContext db) =>
        {
            var cycle = await db.TargetCycles.AsNoTracking().OrderByDescending(c => c.TargetStart).FirstOrDefaultAsync();
            if (cycle is null) return Results.Ok(Array.Empty<object>());
            var months = Enumerable.Range(0, cycle.TargetMonths).Select(i => cycle.TargetStart.AddMonths(i)).ToList();
            var statuses = await db.MonthStatuses.AsNoTracking().Where(m => months.Contains(m.Month)).ToDictionaryAsync(m => m.Month);
            var totals = await db.SalesActuals.AsNoTracking().Where(s => months.Contains(s.Month))
                .GroupBy(s => s.Month).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount), Outlets = g.Count() })
                .ToDictionaryAsync(x => x.Key);
            return Results.Ok(months.Select(m => new
            {
                month = m,
                closed = statuses.TryGetValue(m, out var st) && st.IsClosed,
                closedAt = statuses.GetValueOrDefault(m)?.ClosedAt,
                amount = totals.TryGetValue(m, out var t) ? Math.Round(t.Amount, 2) : 0,
                outlets = totals.TryGetValue(m, out var t2) ? t2.Outlets : 0
            }));
        });

        admin.MapPost("/months/{month}/{action}", async (string month, string action, ClaimsPrincipal me, PortalDbContext db) =>
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                return Results.BadRequest(new { error = "Month must look like 2026-10." });
            if (action is not ("close" or "reopen")) return Results.BadRequest(new { error = "Use close or reopen." });
            var close = action == "close";
            var st = await db.MonthStatuses.FindAsync(m);
            if (st is null) { st = new MonthStatus { Month = m }; db.MonthStatuses.Add(st); }
            st.IsClosed = close;
            st.ClosedAt = close ? DateTimeOffset.UtcNow : null;
            st.ClosedByUserId = close ? me.UserId() : null;
            await db.SalesActuals.Where(s => s.Month == m).ExecuteUpdateAsync(u => u.SetProperty(s => s.IsProvisional, !close));
            db.AuditLogs.Add(new AuditLog { UserId = me.UserId(), Action = close ? "CloseMonth" : "ReopenMonth", Entity = nameof(MonthStatus), EntityId = month });
            await db.SaveChangesAsync();
            return Results.Ok(new { month = m, closed = close });
        });
    }
}
