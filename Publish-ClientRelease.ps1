[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$Version,

    [string]$ClientPath,

    [string]$RealmAddress = 'swarena.swami.dev',

    [string]$Bucket = 'swarena-updates'
)

$ErrorActionPreference = 'Stop'
$releaseRoot = Join-Path $PSScriptRoot 'release'

if ($ClientPath) {
    & (Join-Path $PSScriptRoot 'New-ClientRelease.ps1') -Version $Version -RealmAddress $RealmAddress -ClientPath $ClientPath
} else {
    & (Join-Path $PSScriptRoot 'New-ClientRelease.ps1') -Version $Version -RealmAddress $RealmAddress
}
if ($LASTEXITCODE -ne 0) {
    throw "Could not generate release $Version."
}

$versionRoot = Join-Path $releaseRoot "files\$Version"
$payloads = Get-ChildItem -LiteralPath $versionRoot -File -Recurse
foreach ($file in $payloads) {
    $key = $file.FullName.Substring($releaseRoot.Length + 1).Replace('\', '/')
    Write-Host "Uploading $key" -ForegroundColor Cyan
    npx --yes wrangler r2 object put "$Bucket/$key" --file $file.FullName --content-type 'application/octet-stream' --cache-control 'public, max-age=31536000, immutable' --remote
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload $key. The manifest has not been published."
    }
}

Write-Host 'Publishing manifest.json last' -ForegroundColor Yellow
npx --yes wrangler r2 object put "$Bucket/manifest.json" --file (Join-Path $releaseRoot 'manifest.json') --content-type 'application/json; charset=utf-8' --cache-control 'no-cache, no-store, must-revalidate' --remote
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to upload the manifest.'
}

Write-Host "Version $Version was successfully published to R2." -ForegroundColor Green
