# Demka Sales Capability Portal

Mobile-first portal for Demka Dairy's Modern Trade team: outlet-level value targets, performance against target and action plans for merchandisers, with a national admin panel.

Stack: ASP.NET Core (.NET 10) · Entity Framework Core · SQL Server 2025 · hosted on IIS on a Windows Server VM.

> This repository is **private**. `data/seed` contains outlet sales and merchandiser names from Demka's Q4 2026 targets workbook.

## What is in here

| Folder | Contents |
|---|---|
| `src/Demka.Portal.Core` | The target engine (a port of the Q4 2026 workbook) and seed-file readers. No database code. |
| `src/Demka.Portal.Web` | The web app and API: database tables, starting-data loader, endpoints. |
| `tests/Demka.Portal.Tests` | Tests. `TargetEngineWorkbookTests` must reproduce the workbook's KES 437,253,549.66 exactly. |
| `data/seed/q4-2026` | Starting data: 776 outlets, 62 users, Jan–Sep 2026 sales, Q4 assumptions. |

## First run on a development PC

Prerequisites: .NET 10 SDK, SQL Server 2025 (Developer) running as the default instance, Git.

Run these in PowerShell from the repository folder (`C:\dev\Demka-Sales-Portal`):

```powershell
# 1. Download packages and run the tests (all should pass)
dotnet restore
dotnet test

# 2. Install the database tool (once) and create the first database migration
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/Demka.Portal.Web --output-dir Data/Migrations

# 3. Start the app: creates the DemkaPortal database, loads the starting data, publishes targets v1
dotnet run --project src/Demka.Portal.Web
```

Then open:

- http://localhost:5072/api/health — counts of outlets, users and sales rows, and the published total (should be 437253549.66)
- http://localhost:5072/api/targets/summary — published targets by region and month
- http://localhost:5072/api/holders/1/targets — one merchandiser's outlet targets
- http://localhost:5072/api/admin/targets/preview — draft calculation (development only)

Stop the app with Ctrl+C.

## Starting again with an empty database

```powershell
dotnet ef database drop --project src/Demka.Portal.Web --force
dotnet run --project src/Demka.Portal.Web
```

## Configuration

| Setting | Development | Production |
|---|---|---|
| `ConnectionStrings:Portal` | `appsettings.Development.json` (Windows sign-in to localhost) | Set on the server as an environment variable or in IIS, never committed |
| `Database:MigrateAndSeedOnStartup` | `true` | `false` (migrations are applied by the deployment script) |

## Status

Increment 1 (this commit): target engine, database model, starting-data load, read-only API.
Next: phone + PIN sign-in and roles, then the merchandiser screens.
