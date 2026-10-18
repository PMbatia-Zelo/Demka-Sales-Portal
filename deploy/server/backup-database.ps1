<#
.SYNOPSIS
  Backs up the portal database to C:\DemkaPortal\backups and deletes backups older than -KeepDays.
  Used by the nightly scheduled task and before every deployment.
#>
param(
    [string] $DatabaseName = 'DemkaPortal',
    [string] $Instance = 'localhost\SQLEXPRESS',
    [int] $KeepDays = 14,
    [string] $Label = 'nightly'
)
$ErrorActionPreference = 'Stop'
Import-Module SqlServer
$folder = 'C:\DemkaPortal\backups'
$file = Join-Path $folder ("{0}_{1}_{2}.bak" -f $DatabaseName, (Get-Date -Format 'yyyyMMdd_HHmm'), $Label)
Invoke-Sqlcmd -ServerInstance $Instance -TrustServerCertificate -QueryTimeout 600 -Query `
    "BACKUP DATABASE [$DatabaseName] TO DISK = N'$file' WITH INIT, CHECKSUM, NAME = N'$DatabaseName $Label';"
Get-ChildItem $folder -Filter "$DatabaseName*.bak" | Where-Object LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) | Remove-Item -Force
Write-Host "Backup written: $file"
