param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$zig = Join-Path $projectRoot 'tools\zig-x86_64-windows-0.16.0\zig.exe'
$outputDirectory = Join-Path $projectRoot 'artifacts\native'
$output = Join-Path $outputDirectory 'SongPrismVR.UnityD3D12Probe.dll'
$globalCache = Join-Path $projectRoot '.cache\zig-global'
$localCache = Join-Path $projectRoot '.cache\zig-local'

if (-not (Test-Path -LiteralPath $zig)) {
    throw "Portable Zig compiler is missing: $zig"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $globalCache -Force | Out-Null
New-Item -ItemType Directory -Path $localCache -Force | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR = $globalCache
$env:ZIG_LOCAL_CACHE_DIR = $localCache
$optimization = if ($Configuration -eq 'Debug') { '-O0' } else { '-O2' }
& $zig cc -target x86_64-windows-gnu -shared $optimization `
    -Werror -Wall -Wextra `
    -o $output `
    (Join-Path $PSScriptRoot 'UnityD3D12Probe.c')
if ($LASTEXITCODE -ne 0) {
    throw "Native probe build failed with exit code $LASTEXITCODE."
}

Get-Item -LiteralPath $output
