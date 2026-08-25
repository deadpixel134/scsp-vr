[CmdletBinding()]
param(
    [string]$GameRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'

$game = [System.IO.Path]::GetFullPath($GameRoot)
Import-Module (Join-Path $PSScriptRoot '..\installer\SongPrismVR.Installation.psm1') -Force
Assert-SongPrismGameRoot $game | Out-Null
Assert-SongPrismStopped

& (Join-Path $PSScriptRoot 'Build-VRMod.ps1') -GameRoot $game

$vrmodRoot = Join-Path $game 'vrmod'
$vendorRoot = Join-Path $vrmodRoot 'vendor\staging\bepinex-6.0.0-be.785'
$runtimeOutput = Join-Path $vrmodRoot 'src\SongPrismVR.RuntimeBootstrap\bin\Release\net6.0'
$runtimeDestination = Join-Path $vrmodRoot 'runtime'
New-Item -ItemType Directory -Force -Path $runtimeDestination | Out-Null

$runtimeFiles = @(
    'SongPrismVR.RuntimeBootstrap.dll',
    'SongPrismVR.RuntimeBootstrap.deps.json',
    'SongPrismVR.Core.dll'
)
foreach ($file in $runtimeFiles) {
    $source = Join-Path $runtimeOutput $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "런타임 빌드 산출물이 없습니다: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $runtimeDestination $file) -Force
}

$nativeProbe = Join-Path $vrmodRoot 'artifacts\native\SongPrismVR.UnityD3D12Probe.dll'
if (-not (Test-Path -LiteralPath $nativeProbe -PathType Leaf)) {
    throw "native probe 산출물이 없습니다: $nativeProbe"
}
Copy-Item -LiteralPath $nativeProbe `
    -Destination (Join-Path $runtimeDestination 'SongPrismVR.UnityD3D12Probe.dll') `
    -Force

$loaderFiles = @(
    @{ Source = (Join-Path $vendorRoot 'winhttp.dll'); Destination = 'winhttp.dll' },
    @{ Source = (Join-Path $vrmodRoot 'vendor\openxr-loader-1.1.59\openxr_loader.dll'); Destination = 'vrmod\runtime\openxr_loader.dll' },
    @{ Source = (Join-Path $vendorRoot 'BepInEx\core\dobby.dll'); Destination = 'BepInEx\core\dobby.dll' },
    @{ Source = (Join-Path $PSScriptRoot '..\..\doorstop_config.ini'); Destination = 'doorstop_config.ini' },
    @{ Source = (Join-Path $vendorRoot '.doorstop_version'); Destination = '.doorstop_version' }
)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$rollbackRoot = Join-Path $vrmodRoot "rollback\dev-bootstrap-$stamp"
New-Item -ItemType Directory -Force -Path $rollbackRoot | Out-Null

foreach ($item in $loaderFiles) {
    if (-not (Test-Path -LiteralPath $item.Source -PathType Leaf)) {
        throw "vendor 파일이 없습니다: $($item.Source)"
    }
    $destination = [System.IO.Path]::GetFullPath((Join-Path $game $item.Destination))
    $sourceFull = [System.IO.Path]::GetFullPath($item.Source)
    if (-not $destination.StartsWith($game.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "잘못된 설치 경로입니다: $($item.Destination)"
    }
    if ([System.StringComparer]::OrdinalIgnoreCase.Equals($sourceFull, $destination)) {
        continue
    }
    $destinationDir = [System.IO.Path]::GetDirectoryName($destination)
    New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        $backupRelative = $item.Destination.Replace('\', '_').Replace('/', '_')
        Copy-Item -LiteralPath $destination -Destination (Join-Path $rollbackRoot $backupRelative) -Force
    }
    Copy-Item -LiteralPath $item.Source -Destination $destination -Force
}

$dotnetDestination = Join-Path $game 'dotnet'
New-Item -ItemType Directory -Force -Path $dotnetDestination | Out-Null
Get-ChildItem -LiteralPath (Join-Path $vendorRoot 'dotnet') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $dotnetDestination $_.Name) -Force
}

Write-Host "SONGforPRISM VR bootstrap 설치 완료: $game"
Write-Host "ROLLBACK: $rollbackRoot"
