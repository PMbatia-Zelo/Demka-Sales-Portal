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

### Sign in as the admin (first time only)

The starting data has one national admin with no phone or PIN. Give it yours with .NET's secret store (kept on this PC, never in Git):

```powershell
dotnet user-secrets set "Bootstrap:AdminPhone" "07XXXXXXXX" --project src/Demka.Portal.Web
dotnet user-secrets set "Bootstrap:AdminPin" "482913" --project src/Demka.Portal.Web
```

Restart the app, open http://localhost:5072, sign in with that phone and PIN, and choose a new PIN when asked.
From the admin panel you can set any user's phone number and issue them a PIN.

Then open:

- http://localhost:5072/api/health — counts of outlets, users and sales rows, and the published total (should be 437253549.66)
- http://localhost:5072 — the app (sign-in, My targets, outlets; admin panel for the national admin)

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

- Increment 1: target engine, database model, starting-data load.
- Increment 2: phone + PIN sign-in (hashed PINs, lockout after 5 wrong tries, forced PIN change, 30-day sessions), roles, merchandiser My targets / outlets / outlet detail screens, admin Users page (set phone, issue or reset PIN), installable phone app (PWA) with offline copy of the last figures.
- Increment 3: Business Central ledger upload (Admin → Data uploads). Taxable value incl. VAT = line VAT × 7.25; outlet = Invoice Customer Name (branch) or the account name; names link by saved link → exact outlet name → the account's only outlet. Unplaced Modern Trade names go to a review list; months can be closed after the day-5 close. Verified on the 18 Oct export: Jan–Aug totals KES 904.46M against the workbook's 904.56M; 758 of 776 outlets exact, 9 within 1%. The Nirash ledger is fresh milk only (no VAT), so it adds nothing to taxable targets and needs no upload.
- Deployment kit: `deploy/build-release.ps1` (dev PC) and `deploy/server/` scripts for the Azure VM (IIS, .NET 10 Hosting Bundle, SQL Server 2025 Express, nightly backups, releases with roll-back). The app supports `--setup-database` so the website itself only needs read/write database rights.
- Next: Solutech check-in upload (visits, holder suggestions), team leader view.

## Data uploads (Admin → Data uploads)

Upload the full Customer Ledger Entries export (.xlsx or .csv). Choose the first month to replace (normally the current month); earlier months stay as they are, so the published targets' baseline never changes. `data/seed/q4-2026/customer-aliases.json` holds 32 verified spelling variants and merges (including Naivas Machakos Super Centre → Naivas Mbaitu); any missing ones are added at start-up. Names ending in "CHEESE" on head-office accounts go to the branch without the suffix.

## Deploying to the server

See the Azure Server Setup Guide (shared doc) for the full steps. In short:

```powershell
.\deploy\build-release.ps1          # on the dev PC: tests, publish, zip into out\
# copy the zip to the VM: C:\DemkaPortal\incoming
.\deploy-release.ps1 -ZipPath C:\DemkaPortal\incoming\<zip> -HostName portal.demkadairy.co.ke   # on the VM
```
