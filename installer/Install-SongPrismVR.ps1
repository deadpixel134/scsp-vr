[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameRoot,

    [string]$PackageRoot = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SongPrismVR.Installation.psm1') -Force

$game = Assert-SongPrismGameRoot $GameRoot
Assert-SongPrismStopped
$package = [System.IO.Path]::GetFullPath($PackageRoot)
$manifestPath = Join-Path $package 'package-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "패키지 manifest를 찾지 못했습니다: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([int]$manifest.schemaVersion -ne 1) {
    throw "지원하지 않는 패키지 manifest 버전입니다: $($manifest.schemaVersion)"
}
if ([string]$manifest.loader -ine 'winhttp-doorstop' -or
    [string]$manifest.localifyPolicy -ine 'preserve') {
    throw '패키지 loader/Localify 보존 정책이 지원되는 제품 계약과 다릅니다.'
}

$payloadRoot = Join-Path $package 'payload'
if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "패키지 payload 폴더가 없습니다: $payloadRoot"
}

$filesByPath = [System.Collections.Generic.Dictionary[string, object]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.files) {
    $relative = ([string]$file.path).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative) -or $filesByPath.ContainsKey($relative)) {
        throw "패키지에 비어 있거나 중복된 경로가 있습니다: $relative"
    }
    $filesByPath.Add($relative, $file)
    $source = Resolve-ContainedPath $payloadRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "패키지 파일이 없습니다: $relative"
    }
    if ((Get-FileSha256 $source) -ne ([string]$file.sha256).ToUpperInvariant()) {
        throw "패키지 파일 해시가 manifest와 다릅니다: $relative"
    }
}

$requiredProductFiles = @(
    'winhttp.dll',
    'doorstop_config.ini',
    'dotnet/.version',
    'dotnet/coreclr.dll',
    'dotnet/hostpolicy.dll',
    'dotnet/Microsoft.NETCore.App.deps.json',
    'dotnet/Microsoft.NETCore.App.runtimeconfig.json',
    'dotnet/System.Private.CoreLib.dll',
    'dotnet/System.Runtime.dll',
    'dotnet/mscorlib.dll',
    'BepInEx/core/dobby.dll',
    'vrmod/runtime/SongPrismVR.RuntimeBootstrap.dll',
    'vrmod/runtime/SongPrismVR.RuntimeBootstrap.deps.json',
    'vrmod/runtime/SongPrismVR.Core.dll',
    'vrmod/runtime/SongPrismVR.UnityD3D12Probe.dll',
    'vrmod/runtime/openxr_loader.dll',
    'vrmod/config/settings.json',
    'vrmod/tools/SongPrismVR.Configurator.exe',
    'vrmod/LICENSE.txt',
    'vrmod/THIRD_PARTY_NOTICES.txt',
    'vrmod/licenses/Dobby-Apache-2.0.txt'
)
foreach ($relative in $requiredProductFiles) {
    if (-not $filesByPath.ContainsKey($relative)) {
        throw "필수 standalone 패키지 파일이 manifest에 없습니다: $relative"
    }
}
$settingsPolicy = $filesByPath['vrmod/config/settings.json']
$dobbyPolicy = $filesByPath['BepInEx/core/dobby.dll']
if (-not [bool]$settingsPolicy.preserveExisting -or
    -not [bool]$settingsPolicy.preserveOnUninstall -or
    -not [bool]$dobbyPolicy.preserveExisting -or
    [bool]$dobbyPolicy.preserveOnUninstall) {
    throw '패키지의 설정/Dobby 보존 정책이 안전한 제품 계약과 다릅니다.'
}

$localifyStatus = Get-LocalifyStatus $game
switch ($localifyStatus) {
    'Installed' { Write-Host 'scsp-localify 한글패치 감지: 기존 로더와 번역 데이터를 보존한 채 VR을 설치합니다.' }
    'LoaderOnly' { Write-Host 'scsp-localify 로더 전용 구성 감지: 기존 로더를 보존하고 번역 데이터는 건드리지 않습니다.' }
    'Partial' { Write-Warning 'scsp-localify 흔적이 일부만 발견되었습니다. 관련 파일은 보존하고 VR만 설치합니다.' }
    'Absent' { Write-Host 'scsp-localify 없음: 패키지에 포함된 로더 전용 파일만 설치합니다.' }
}

$timestamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$backupRootRelative = "vrmod/rollback/product-install-$($manifest.version)-$timestamp"
$backupRoot = Resolve-ContainedPath $game $backupRootRelative
$statePath = Join-Path $game 'vrmod\install-state.json'
$previousStateBackup = $null

if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $previousState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ([int]$previousState.schemaVersion -ne 1) {
        throw "기존 설치 상태 버전을 지원하지 않아 안전하게 업그레이드할 수 없습니다: $($previousState.schemaVersion)"
    }
    $previousStateBackup = '_previous-install-state.json'
    [System.IO.Directory]::CreateDirectory($backupRoot) | Out-Null
    [System.IO.File]::Copy(
        $statePath,
        (Join-Path $backupRoot $previousStateBackup),
        $true)
    Write-Host "기존 SongPrism VR $($previousState.version) 설치 상태를 중첩 롤백용으로 보존합니다."
}

$completed = New-Object System.Collections.Generic.List[object]
$stateFiles = New-Object System.Collections.Generic.List[object]

try {
    foreach ($file in $manifest.files) {
        $relative = [string]$file.path
        $source = Resolve-ContainedPath (Join-Path $package 'payload') $relative
        $destination = Resolve-ContainedPath $game $relative
        $sourceHash = Get-FileSha256 $source
        if ($sourceHash -ne ([string]$file.sha256).ToUpperInvariant()) {
            throw "패키지 파일 해시가 manifest와 다릅니다: $relative"
        }

        $preserve = [bool]$file.preserveExisting
        $priorFile = Test-Path -LiteralPath $destination -PathType Leaf
        if ($preserve -and $priorFile) {
            $stateFiles.Add([ordered]@{
                path = $relative
                action = 'preserved'
                installedHash = $null
                priorFile = $true
                backupRelative = $null
                preserveOnUninstall = [bool]$file.preserveOnUninstall
            })
            continue
        }

        $backupRelative = $null
        if ($priorFile) {
            $backup = Resolve-ContainedPath $backupRoot $relative
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($backup)) | Out-Null
            [System.IO.File]::Copy($destination, $backup, $true)
            $backupRelative = $relative
        }

        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
        [System.IO.File]::Copy($source, $destination, $true)
        $installedHash = Get-FileSha256 $destination
        $entry = [ordered]@{
            path = $relative
            action = 'installed'
            installedHash = $installedHash
            priorFile = [bool]$priorFile
            backupRelative = $backupRelative
            preserveOnUninstall = [bool]$file.preserveOnUninstall
        }
        $completed.Add($entry)
        $stateFiles.Add($entry)
    }

    $state = [ordered]@{
        schemaVersion = 1
        version = [string]$manifest.version
        installedUtc = [DateTime]::UtcNow.ToString('o')
        localifyStatus = $localifyStatus
        backupRoot = $backupRootRelative
        previousStateBackup = $previousStateBackup
        files = $stateFiles
    }
    Write-JsonAtomic $statePath $state
    Write-Host "SongPrism VR $($manifest.version) 설치 완료. scsp-localify 상태: $localifyStatus"
}
catch {
    Write-Warning '설치 실패. 이번 실행에서 변경한 파일을 되돌립니다.'
    for ($index = $completed.Count - 1; $index -ge 0; $index--) {
        $entry = $completed[$index]
        $destination = Resolve-ContainedPath $game ([string]$entry.path)
        if ([bool]$entry.priorFile) {
            $backup = Resolve-ContainedPath $backupRoot ([string]$entry.backupRelative)
            if (Test-Path -LiteralPath $backup -PathType Leaf) {
                [System.IO.File]::Copy($backup, $destination, $true)
            }
        }
        elseif (Test-Path -LiteralPath $destination -PathType Leaf) {
            [System.IO.File]::Delete($destination)
        }
    }
    throw
}
