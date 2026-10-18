<#
.SYNOPSIS
  Builds a release package of the Demka Sales Portal on the development PC.
.DESCRIPTION
  Runs the tests, publishes the web app in Release mode and zips it, ready to copy to the server.
  Output: out\DemkaPortal-<yyyyMMdd-HHmm>-<git commit>.zip
.EXAMPLE
  cd C:\dev\Demka-Sales-Portal
  .\deploy\build-release.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host 'Running tests...' -ForegroundColor Cyan
dotnet test --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Fix them before building a release.' }

$commit = (git rev-parse --short HEAD).Trim()
$dirty = git status --porcelain
if ($dirty) { Write-Warning 'There are uncommitted changes. Commit and push first so the release matches GitHub.' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$name = "DemkaPortal-$stamp-$commit"
$out = Join-Path $root 'out'
$publish = Join-Path $out $name
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host 'Publishing...' -ForegroundColor Cyan
dotnet publish src/Demka.Portal.Web -c Release -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# Never ship development settings or secrets.
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $publish 'appsettings.Development.json')
Set-Content -Path (Join-Path $publish 'release.txt') -Value "Release $name`nBuilt $(Get-Date -Format s) from commit $commit"

$zip = "$publish.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
Remove-Item -Recurse -Force $publish
Write-Host "Release package ready: $zip" -ForegroundColor Green
Write-Host 'Copy it to the server (C:\DemkaPortal\incoming) and run deploy-release.ps1 there.'
