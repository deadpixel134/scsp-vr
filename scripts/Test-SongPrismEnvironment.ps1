[CmdletBinding()]
param(
    [string]$GameRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'

$game = [System.IO.Path]::GetFullPath($GameRoot)
Import-Module (Join-Path $PSScriptRoot '..\installer\SongPrismVR.Installation.psm1') -Force

$running = @(Get-Process -Name 'imasscprism' -ErrorAction SilentlyContinue)
$localify = Get-LocalifyStatus $game
$d3d12Core = Test-Path -LiteralPath (Join-Path $game 'D3D12\D3D12Core.dll') -PathType Leaf
$gameGuard = Test-Path -LiteralPath (Join-Path $game 'GameGuard') -PathType Container
$npDll = Test-Path -LiteralPath (Join-Path $game 'imasscprism_Data\Plugins\x86_64\NPGameDLL.dll') -PathType Leaf
$metadata = Test-Path -LiteralPath (Join-Path $game 'imasscprism_Data\il2cpp_data\Metadata\global-metadata.dat') -PathType Leaf

[PSCustomObject]@{
    GameRoot = $game
    ImasscprismRunning = $running.Count -gt 0
    LocalifyStatus = $localify
    D3D12CorePresent = $d3d12Core
    GameGuardDirectoryPresent = $gameGuard
    NPGameDllPresent = $npDll
    Il2CppMetadataPresent = $metadata
    RecommendedInitialLoader = if ($localify -in @('Installed', 'LoaderOnly', 'Partial')) {
        'winhttp-doorstop'
    }
    else {
        'bundled-scsp-loader'
    }
} | Format-List

if ($running.Count -gt 0) {
    Write-Warning '게임이 실행 중입니다. 런타임·설치 파일을 교체하기 전에 종료해야 합니다.'
}
if (-not $d3d12Core) {
    Write-Warning 'D3D12Core.dll이 없습니다. 기본 D3D12 경로를 검증할 수 없습니다.'
}
if (-not $npDll) {
    Write-Warning 'NPGameDLL.dll이 없습니다. Localify 공존 검증 시 실제 GameGuard 중립화 경로를 재확인해야 합니다.'
}

& (Join-Path $PSScriptRoot 'Verify-Baseline.ps1') -GameRoot $game
