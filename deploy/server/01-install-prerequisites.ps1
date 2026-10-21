<#
.SYNOPSIS
  One-time server preparation: IIS, the .NET 10 Hosting Bundle, PowerShell SQL tools and the portal folders.
  Run in PowerShell as Administrator on the Azure VM. Safe to run again.
#>
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

Write-Host '1/4 Installing IIS...' -ForegroundColor Cyan
Install-WindowsFeature -Name Web-Server, Web-Static-Content, Web-Default-Doc, Web-Http-Errors, Web-Http-Logging, `
    Web-Request-Monitor, Web-Filtering, Web-Stat-Compression, Web-Dyn-Compression, Web-Mgmt-Console -IncludeManagementTools | Out-Null

Write-Host '2/4 .NET 10 Hosting Bundle (runtime + IIS module)...' -ForegroundColor Cyan
# Skip when the server already has it: installing restarts IIS, which interrupts every other site on the server.
$ancm = Test-Path "$env:windir\System32\inetsrv\aspnetcorev2.dll"
$rt10 = (& dotnet --list-runtimes 2>$null) -match '^Microsoft\.AspNetCore\.App 10\.'
if ($ancm -and $rt10) {
    Write-Host '    Already installed, skipping (no IIS restart).'
} else {
    $installer = Join-Path $env:TEMP 'dotnet-hosting-10-win.exe'
    Invoke-WebRequest -Uri 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe' -OutFile $installer -UseBasicParsing
    $p = Start-Process -FilePath $installer -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) { throw "Hosting Bundle install failed (exit code $($p.ExitCode))." }
    # IIS must restart to pick up the new module.
    net stop was /y | Out-Null
    net start w3svc | Out-Null
}

Write-Host '3/4 Installing the SqlServer PowerShell module (for database scripts and backups)...' -ForegroundColor Cyan
if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force | Out-Null
    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
    Install-Module -Name SqlServer -Scope AllUsers -Force -AllowClobber
}

Write-Host '4/4 Creating folders under C:\DemkaPortal...' -ForegroundColor Cyan
# Everything lives on C: — on Azure VMs D: is a temporary disk that can be wiped.
foreach ($d in 'site', 'site-test', 'releases', 'incoming', 'keys', 'logs', 'backups') {
    New-Item -ItemType Directory -Force -Path "C:\DemkaPortal\$d" | Out-Null
}

Write-Host ''
Write-Host 'Done. Next: install SQL Server 2025 Express if the server has none (see the guide), then run 02-create-site.ps1.' -ForegroundColor Green
dotnet --list-runtimes | Select-String 'AspNetCore'
