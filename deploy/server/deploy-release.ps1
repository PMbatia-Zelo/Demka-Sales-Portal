<#
.SYNOPSIS
  Deploys a release package built by build-release.ps1 to the live or test site.
.DESCRIPTION
  1. Unpacks the zip into C:\DemkaPortal\releases\<name>
  2. Backs up the database (if it exists)
  3. Takes the site offline (app_offline.htm), copies the new files, keeps appsettings.Production.json
  4. Creates or updates the database and loads starting data (dotnet ... --setup-database, as you)
  5. Gives the IIS pool read/write access to the database
  6. Brings the site back and checks /api/health
  Run in PowerShell as Administrator. Your Windows account must be a SQL Server administrator.
.EXAMPLE
  .\deploy-release.ps1 -ZipPath C:\DemkaPortal\incoming\DemkaPortal-20261020-0930-ab12cd3.zip -HostName portal.demkadairy.co.ke
.EXAMPLE
  # Roll back to an earlier release already on the server
  .\deploy-release.ps1 -ReleaseFolder C:\DemkaPortal\releases\DemkaPortal-20261018-1500-9f8e7d6 -HostName portal.demkadairy.co.ke
#>
#Requires -RunAsAdministrator
param(
    [string] $ZipPath,
    [string] $ReleaseFolder,
    [Parameter(Mandatory)] [string] $HostName,
    [ValidateSet('live', 'test')] [string] $Environment = 'live',
    [string] $Instance = 'localhost\SQLEXPRESS',
    [int] $KeepReleases = 5
)
$ErrorActionPreference = 'Stop'
Import-Module SqlServer
$pool = if ($Environment -eq 'live') { 'DemkaPortal' } else { 'DemkaPortalTest' }
$site = if ($Environment -eq 'live') { 'C:\DemkaPortal\site' } else { 'C:\DemkaPortal\site-test' }
$settings = Join-Path $site 'appsettings.Production.json'

if (-not (Test-Path $settings)) {
    throw "Missing $settings. Copy appsettings.Production.template.json there and fill it in first."
}
$conn = (Get-Content $settings -Raw | ConvertFrom-Json).ConnectionStrings.Portal
if ($conn -notmatch 'Database=([^;]+)') { throw 'Could not read the database name from the connection string.' }
$database = $Matches[1]

# 1. Unpack
if ($ZipPath) {
    if (-not (Test-Path $ZipPath)) { throw "Release package not found: $ZipPath" }
    $name = [IO.Path]::GetFileNameWithoutExtension($ZipPath)
    $ReleaseFolder = "C:\DemkaPortal\releases\$name"
    if (Test-Path $ReleaseFolder) { Remove-Item -Recurse -Force $ReleaseFolder }
    Expand-Archive -Path $ZipPath -DestinationPath $ReleaseFolder
} elseif (-not $ReleaseFolder -or -not (Test-Path $ReleaseFolder)) {
    throw 'Give -ZipPath (new release) or -ReleaseFolder (existing release to roll back to).'
}
Write-Host "Deploying $(Split-Path -Leaf $ReleaseFolder) to $Environment ($HostName)" -ForegroundColor Cyan

# 2. Back up first, if the database exists
$exists = Invoke-Sqlcmd -ServerInstance $Instance -TrustServerCertificate -Query "SELECT DB_ID(N'$database') AS Id"
if ($exists.Id -isnot [DBNull]) {
    & (Join-Path $PSScriptRoot 'backup-database.ps1') -DatabaseName $database -Instance $Instance -Label 'predeploy'
}

# 3. Offline, copy, keep server-only files
Set-Content -Path (Join-Path $site 'app_offline.htm') -Value '<!doctype html><title>Updating</title><p style="font-family:sans-serif;padding:2em">The Demka Sales Portal is being updated. Please try again in a minute.</p>'
Start-Sleep -Seconds 3
robocopy $ReleaseFolder $site /MIR /XF appsettings.Production.json app_offline.htm /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying files failed (robocopy exit code $LASTEXITCODE). The site is still offline; fix and re-run." }

# 4. Database: create or update, starting data, verified name links, bootstrap admin
Push-Location $site
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    dotnet .\Demka.Portal.Web.dll --setup-database
    if ($LASTEXITCODE -ne 0) { throw 'Database setup failed. The site is still offline. Restore the pre-deploy backup or fix and re-run.' }
} finally { Pop-Location }

# 5. App pool access to the database
Invoke-Sqlcmd -ServerInstance $Instance -TrustServerCertificate -InputFile (Join-Path $PSScriptRoot 'grant-app-access.sql') `
    -Variable "PoolName=$pool", "DatabaseName=$database"

# 6. Back online and check
Remove-Item -Force (Join-Path $site 'app_offline.htm')
Restart-WebAppPool -Name $pool
Start-Sleep -Seconds 5
try {
    $health = Invoke-RestMethod -Uri "https://$HostName/api/health" -TimeoutSec 60
} catch {
    Write-Warning "HTTPS check failed ($($_.Exception.Message)); trying HTTP (normal before the certificate is installed)."
    $health = Invoke-RestMethod -Uri "http://$HostName/api/health" -TimeoutSec 60
}
$health | Format-List

# Keep the last few releases for roll-back
Get-ChildItem 'C:\DemkaPortal\releases' -Directory | Sort-Object CreationTime -Descending | Select-Object -Skip $KeepReleases | Remove-Item -Recurse -Force
Write-Host 'Deployment complete.' -ForegroundColor Green
