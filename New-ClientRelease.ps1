[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$Version,

    [string]$RealmAddress = 'swarena.swami.dev',

    [string]$ClientPath
)

$ErrorActionPreference = 'Stop'
$launcherRoot = $PSScriptRoot
$stageRoot = Join-Path $launcherRoot 'artifacts\stage'
$releaseRoot = Join-Path $launcherRoot 'release'
$mappingPath = Join-Path $launcherRoot 'managed-files.json'

if ($RealmAddress -notmatch '^[A-Za-z0-9.-]+$') {
    throw "Invalid realm address: $RealmAddress"
}

# artifacts/stage is a temporary directory owned exclusively by this script.
if (Test-Path -LiteralPath $stageRoot) {
    $resolvedStage = (Resolve-Path -LiteralPath $stageRoot).Path
    $expectedStage = [IO.Path]::GetFullPath($stageRoot)
    if ($resolvedStage -ne $expectedStage -or -not $resolvedStage.StartsWith($launcherRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected stage path: $resolvedStage"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Path $stageRoot | Out-Null

$managedFiles = Get-Content -LiteralPath $mappingPath -Raw | ConvertFrom-Json
foreach ($entry in $managedFiles) {
    if ($ClientPath) {
        $resolvedClient = (Resolve-Path -LiteralPath $ClientPath).Path
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedClient 'Wow.exe') -PathType Leaf)) {
            throw "The specified directory does not contain Wow.exe: $resolvedClient"
        }
        $source = [IO.Path]::GetFullPath((Join-Path $resolvedClient ($entry.target -replace '/', '\')))
        if (-not $source.StartsWith($resolvedClient, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The source is outside the client directory: $source"
        }
    } else {
        $source = [IO.Path]::GetFullPath((Join-Path $launcherRoot $entry.source))
        if (-not $source.StartsWith($launcherRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The source is outside the repository: $source"
        }
    }
    $target = [IO.Path]::GetFullPath((Join-Path $stageRoot ($entry.target -replace '/', '\')))
    if (-not $target.StartsWith($stageRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The destination is outside the stage directory: $target"
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Managed file is missing: $source"
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    Write-Host "+ $($entry.target)" -ForegroundColor Cyan
}

$realmlistPath = Join-Path $stageRoot 'Data\enUS\realmlist.wtf'
New-Item -ItemType Directory -Path (Split-Path -Parent $realmlistPath) -Force | Out-Null
Set-Content -LiteralPath $realmlistPath -Value "set realmlist $RealmAddress" -Encoding ASCII
Write-Host '+ Data/enUS/realmlist.wtf' -ForegroundColor Cyan

dotnet run --project (Join-Path $launcherRoot 'src\SwArena.Publisher') -c Release -- $stageRoot $releaseRoot $Version
if ($LASTEXITCODE -ne 0) {
    throw "The publisher exited with code $LASTEXITCODE."
}

Write-Host "`nRelease prepared in $releaseRoot" -ForegroundColor Green
Write-Host 'Upload it to R2 and publish manifest.json last.' -ForegroundColor Yellow
