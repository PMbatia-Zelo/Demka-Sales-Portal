<#
.SYNOPSIS
  Schedules the nightly database backup (01:30 Nairobi time) and gives SQL Server and the task the rights they need.
  Run once as Administrator after SQL Server Express is installed and the database exists (after the first deployment).
#>
#Requires -RunAsAdministrator
param(
    [string] $DatabaseName = 'DemkaPortal',
    [string] $Instance = 'localhost\SQLEXPRESS'
)
$ErrorActionPreference = 'Stop'
Import-Module SqlServer

# SQL Server's own service account writes the .bak files.
$sqlService = 'NT Service\MSSQL$' + ($Instance.Split('\')[-1])
icacls 'C:\DemkaPortal\backups' /grant "${sqlService}:(OI)(CI)M" /Q | Out-Null

# The task runs as SYSTEM, which may back up this database and nothing more.
Invoke-Sqlcmd -ServerInstance $Instance -TrustServerCertificate -Query @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'NT AUTHORITY\SYSTEM')
    CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
USE [$DatabaseName];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'NT AUTHORITY\SYSTEM')
    CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];
"@

$script = Join-Path $PSScriptRoot 'backup-database.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -DatabaseName $DatabaseName -Instance `"$Instance`""
$trigger = New-ScheduledTaskTrigger -Daily -At '01:30'
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName "Demka Portal backup ($DatabaseName)" -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null

Write-Host "Nightly backup scheduled for $DatabaseName at 01:30. Running one now to check..." -ForegroundColor Cyan
Start-ScheduledTask -TaskName "Demka Portal backup ($DatabaseName)"
Start-Sleep -Seconds 15
Get-ChildItem 'C:\DemkaPortal\backups' -Filter "$DatabaseName*.bak" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 Name, Length, LastWriteTime
