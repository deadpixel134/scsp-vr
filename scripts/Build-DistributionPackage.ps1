[CmdletBinding()]
param(
    [string]$Version = '0.1.1',
    [string]$OutputRoot,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$vrmodRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = [System.IO.Path]::GetFullPath((Join-Path $vrmodRoot '..'))
$buildRoot = Join-Path $vrmodRoot 'build'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $vrmodRoot 'release'
}
$releaseRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$runtimeBuildRoot = Join-Path $vrmodRoot 'src\SongPrismVR.RuntimeBootstrap\bin\Release\net6.0'
$nativeBuildRoot = Join-Path $vrmodRoot 'artifacts\native'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw "Version must use semantic major.minor.patch[-prerelease] form: $Version"
}

function Assert-LastExitCode {
    param([string]$Message)
    if ($LASTEXITCODE -ne 0) { throw $Message }
}

function Copy-OwnedFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required release file is missing: $Source"
    }
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Destination)) | Out-Null
    [System.IO.File]::Copy($Source, $Destination, $true)
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-VRMod.ps1')
    Assert-LastExitCode 'Standard VR mod build failed.'
}

[System.IO.Directory]::CreateDirectory($buildRoot) | Out-Null
$workRoot = Join-Path $buildRoot ('distribution-' + [Guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $workRoot 'package'
$payloadRoot = Join-Path $packageRoot 'payload'
$configuratorPublish = Join-Path $workRoot 'configurator-publish'
$installerPublish = Join-Path $workRoot 'installer-publish'
$finalPackage = Join-Path $releaseRoot "SongPrismVR-v$Version"
$finalZip = Join-Path $releaseRoot "SongPrismVR-v$Version.zip"
$checksumPath = $finalZip + '.sha256'

foreach ($target in @($finalPackage, $finalZip, $checksumPath)) {
    if (Test-Path -LiteralPath $target) {
        throw "Release target already exists; choose a new version or output root: $target"
    }
}

try {
    [System.IO.Directory]::CreateDirectory($payloadRoot) | Out-Null

    dotnet publish (Join-Path $vrmodRoot 'src\SongPrismVR.Configurator\SongPrismVR.Configurator.csproj') `
        -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None `
        -o $configuratorPublish
    Assert-LastExitCode 'Self-contained Configurator publish failed.'
    dotnet publish (Join-Path $vrmodRoot 'src\SongPrismVR.Installer\SongPrismVR.Installer.csproj') `
        -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None `
        -o $installerPublish
    Assert-LastExitCode 'Self-contained Installer publish failed.'

    Copy-OwnedFile (Join-Path $gameRoot 'winhttp.dll') (Join-Path $payloadRoot 'winhttp.dll')
    Copy-OwnedFile (Join-Path $gameRoot 'doorstop_config.ini') (Join-Path $payloadRoot 'doorstop_config.ini')
    Copy-Item -LiteralPath (Join-Path $gameRoot 'dotnet') -Destination (Join-Path $payloadRoot 'dotnet') -Recurse
    Copy-OwnedFile (Join-Path $gameRoot 'BepInEx\core\dobby.dll') (Join-Path $payloadRoot 'BepInEx\core\dobby.dll')

    foreach ($name in @(
        'SongPrismVR.RuntimeBootstrap.dll',
        'SongPrismVR.RuntimeBootstrap.deps.json',
        'SongPrismVR.Core.dll')) {
        Copy-OwnedFile (Join-Path $runtimeBuildRoot $name) (Join-Path $payloadRoot "vrmod\runtime\$name")
    }
    Copy-OwnedFile (Join-Path $nativeBuildRoot 'SongPrismVR.UnityD3D12Probe.dll') `
        (Join-Path $payloadRoot 'vrmod\runtime\SongPrismVR.UnityD3D12Probe.dll')
    Copy-OwnedFile (Join-Path $vrmodRoot 'runtime\openxr_loader.dll') `
        (Join-Path $payloadRoot 'vrmod\runtime\openxr_loader.dll')

    $packagedRuntime = Join-Path $payloadRoot 'vrmod\runtime\SongPrismVR.RuntimeBootstrap.dll'
    $runtimeProductVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        $packagedRuntime).ProductVersion
    if ([string]::IsNullOrWhiteSpace($runtimeProductVersion) -or
        -not $runtimeProductVersion.StartsWith(
            $Version,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime product version '$runtimeProductVersion' does not match package version '$Version'."
    }
    Copy-OwnedFile (Join-Path $configuratorPublish 'SongPrismVR.Configurator.exe') `
        (Join-Path $payloadRoot 'vrmod\tools\SongPrismVR.Configurator.exe')
    Copy-OwnedFile (Join-Path $vrmodRoot 'release-assets\default-settings.json') `
        (Join-Path $payloadRoot 'vrmod\config\settings.json')
    Copy-OwnedFile (Join-Path $vrmodRoot 'LICENSE') (Join-Path $payloadRoot 'vrmod\LICENSE.txt')
    Copy-OwnedFile (Join-Path $vrmodRoot 'release-assets\THIRD_PARTY_NOTICES.txt') `
        (Join-Path $payloadRoot 'vrmod\THIRD_PARTY_NOTICES.txt')
    Copy-OwnedFile (Join-Path $vrmodRoot 'release-assets\licenses\Dobby-Apache-2.0.txt') `
        (Join-Path $payloadRoot 'vrmod\licenses\Dobby-Apache-2.0.txt')

    foreach ($name in @('Install-SongPrismVR.ps1', 'Uninstall-SongPrismVR.ps1', 'SongPrismVR.Installation.psm1')) {
        Copy-OwnedFile (Join-Path $vrmodRoot "installer\$name") (Join-Path $packageRoot $name)
    }
    Copy-OwnedFile (Join-Path $installerPublish 'SongPrismVR.Installer.exe') `
        (Join-Path $packageRoot 'SongPrismVR.Installer.exe')

    $manifestFiles = New-Object System.Collections.Generic.List[object]
    foreach ($file in Get-ChildItem -LiteralPath $payloadRoot -File -Recurse | Sort-Object FullName) {
        $relative = [System.IO.Path]::GetRelativePath($payloadRoot, $file.FullName).Replace('\', '/')
        $preserveExisting = $relative -ieq 'vrmod/config/settings.json' -or $relative -ieq 'BepInEx/core/dobby.dll'
        $preserveOnUninstall = $relative -ieq 'vrmod/config/settings.json'
        $manifestFiles.Add([ordered]@{
            path = $relative
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
            preserveExisting = $preserveExisting
            preserveOnUninstall = $preserveOnUninstall
        })
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $Version
        loader = 'winhttp-doorstop'
        localifyPolicy = 'preserve'
        files = $manifestFiles
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        (Join-Path $packageRoot 'package-manifest.json'),
        $manifestJson + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    & (Join-Path $vrmodRoot 'tests\Test-DistributionPackage.ps1') -PackageRoot $packageRoot
    Assert-LastExitCode 'PowerShell distribution validation failed.'
    dotnet run --project (Join-Path $vrmodRoot 'tests\SongPrismVR.Management.Tests\SongPrismVR.Management.Tests.csproj') `
        -c Release -- $packageRoot
    Assert-LastExitCode 'Management distribution validation failed.'

    [System.IO.Directory]::CreateDirectory($releaseRoot) | Out-Null
    [System.IO.Directory]::Move($packageRoot, $finalPackage)
    Compress-Archive -Path (Join-Path $finalPackage '*') -DestinationPath $finalZip -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $finalZip -Algorithm SHA256).Hash.ToUpperInvariant()
    [System.IO.File]::WriteAllText(
        $checksumPath,
        "$zipHash  $([System.IO.Path]::GetFileName($finalZip))$([Environment]::NewLine)",
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "CHECK: standalone release package validated: $finalZip"
}
finally {
    $resolvedWorkRoot = [System.IO.Path]::GetFullPath($workRoot)
    $allowedPrefix = [System.IO.Path]::GetFullPath($buildRoot).TrimEnd('\') + '\'
    if ($resolvedWorkRoot.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedWorkRoot)) {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
    }
}
