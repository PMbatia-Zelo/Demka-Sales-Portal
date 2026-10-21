<#
.SYNOPSIS
  Writes appsettings.Production.json for the live or test site (connection string, keys folder, address, first admin).
  Run once per site as Administrator, after 02-create-site.ps1 and before the first deploy-release.ps1. Will not overwrite.
.EXAMPLE
  .\write-settings.ps1 -Environment test -HostName test-sales.demkadairy.co.ke -AdminPhone 07XXXXXXXX -AdminPin 482913
#>
#Requires -RunAsAdministrator
param(
    [ValidateSet('live', 'test')] [string] $Environment = 'live',
    [Parameter(Mandatory)] [string] $HostName,
    [Parameter(Mandatory)] [string] $AdminPhone,
    [Parameter(Mandatory)] [string] $AdminPin,
    [string] $Instance = 'localhost\SQLEXPRESS'
)
$ErrorActionPreference = 'Stop'
$live = $Environment -eq 'live'
$site = if ($live) { 'C:\DemkaPortal\site' } else { 'C:\DemkaPortal\site-test' }
$db = if ($live) { 'DemkaPortal' } else { 'DemkaPortalTest' }
$keys = if ($live) { 'C:\DemkaPortal\keys' } else { 'C:\DemkaPortal\keys-test' }
$pool = if ($live) { 'DemkaPortal' } else { 'DemkaPortalTest' }
$file = Join-Path $site 'appsettings.Production.json'
if (Test-Path $file) { throw "$file already exists. Edit it by hand, or delete it first if you really mean to replace it." }

[ordered]@{
    ConnectionStrings = @{ Portal = "Server=$Instance;Database=$db;Trusted_Connection=True;TrustServerCertificate=True" }
    Database          = @{ MigrateAndSeedOnStartup = $false }
    DataProtection    = @{ KeysPath = $keys }
    # Temporary: used once to give the national admin a phone and PIN. Remove after the first admin sign-in.
    Bootstrap         = @{ AdminPhone = $AdminPhone; AdminPin = $AdminPin }
    AllowedHosts      = $HostName
} | ConvertTo-Json | Set-Content $file -Encoding UTF8

# Only administrators and this site's app pool can read it.
icacls $file /inheritance:r /grant 'Administrators:F' 'SYSTEM:F' "IIS AppPool\${pool}:R" /Q | Out-Null
Write-Host "Written $file (database $db, keys $keys, address $HostName)." -ForegroundColor Green
