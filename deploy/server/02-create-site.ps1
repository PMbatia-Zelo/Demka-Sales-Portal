<#
.SYNOPSIS
  Creates the IIS application pool and website for the portal, with folder permissions and firewall rules.
.PARAMETER HostName
  The portal address, e.g. sales.demkadairy.co.ke (the DNS A record must point at this server).
.PARAMETER Environment
  'live' (default) or 'test'. Test uses its own folder, pool and host name, e.g. test-sales.demkadairy.co.ke.
.EXAMPLE
  .\02-create-site.ps1 -HostName sales.demkadairy.co.ke
  .\02-create-site.ps1 -HostName test-sales.demkadairy.co.ke -Environment test
#>
#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory)] [string] $HostName,
    [ValidateSet('live', 'test')] [string] $Environment = 'live'
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

$pool = if ($Environment -eq 'live') { 'DemkaPortal' } else { 'DemkaPortalTest' }
$site = $pool
$path = if ($Environment -eq 'live') { 'C:\DemkaPortal\site' } else { 'C:\DemkaPortal\site-test' }

if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
# .NET Core runs outside the .NET Framework CLR: "No Managed Code".
Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$pool" -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.loadUserProfile -Value $true
Set-ItemProperty "IIS:\AppPools\$pool" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)

if (-not (Get-Website -Name $site)) {
    New-Website -Name $site -PhysicalPath $path -ApplicationPool $pool -HostHeader $HostName -Port 80 | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$site" -Name physicalPath -Value $path
}
Set-ItemProperty "IIS:\Sites\$site" -Name applicationDefaults.preloadEnabled -Value $true

# The pool's identity reads the site and writes keys and logs; nothing else.
$identity = "IIS AppPool\$pool"
icacls $path /grant "${identity}:(OI)(CI)RX" /T /Q | Out-Null
foreach ($d in 'C:\DemkaPortal\keys', 'C:\DemkaPortal\logs') {
    icacls $d /grant "${identity}:(OI)(CI)M" /T /Q | Out-Null
}
# Keys folder: administrators and the app only.
icacls 'C:\DemkaPortal\keys' /inheritance:r /grant 'Administrators:(OI)(CI)F' 'SYSTEM:(OI)(CI)F' "${identity}:(OI)(CI)M" /Q | Out-Null

foreach ($port in 80, 443) {
    $rule = "Demka Portal HTTP $port"
    if (-not (Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow | Out-Null
    }
}

Write-Host "IIS site '$site' created for http://$HostName (folder $path)." -ForegroundColor Green
Write-Host "Next: put appsettings.Production.json in $path (from the template), deploy the first release, then add HTTPS with win-acme."
