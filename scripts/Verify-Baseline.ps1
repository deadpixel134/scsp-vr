[CmdletBinding()]
param(
    [string]$GameRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\baseline\installation-manifest.json')
)

$ErrorActionPreference = 'Stop'

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$results = foreach ($entry in $manifest.files) {
    $relativePath = $entry.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $fullPath = Join-Path $GameRoot $relativePath

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        [PSCustomObject]@{
            Path = $entry.path
            Status = 'Missing'
            ExpectedHash = $entry.sha256
            ActualHash = $null
        }
        continue
    }

    $file = Get-Item -LiteralPath $fullPath
    $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
    $status = if ($file.Length -eq [long]$entry.length -and $actualHash -eq $entry.sha256) {
        'Match'
    }
    else {
        'Changed'
    }

    [PSCustomObject]@{
        Path = $entry.path
        Status = $status
        ExpectedHash = $entry.sha256
        ActualHash = $actualHash
    }
}

$results | Format-Table Path, Status -AutoSize

$blockingPaths = @(
    'imasscprism.exe',
    'GameAssembly.dll',
    'UnityPlayer.dll',
    'version.dll',
    'imasscprism_Data/ScriptingAssemblies.json',
    'imasscprism_Data/il2cpp_data/Metadata/global-metadata.dat'
)

$blockingMismatch = $results | Where-Object {
    $_.Path -in $blockingPaths -and $_.Status -ne 'Match'
}

if ($blockingMismatch) {
    Write-Error '지원 기준선과 핵심 바이너리가 다릅니다. VR 플러그인을 활성화하지 마십시오.'
    exit 2
}

$localifyMismatch = $results | Where-Object {
    $_.Path -like 'scsp-config.json' -and $_.Status -ne 'Match'
}

if ($localifyMismatch) {
    Write-Warning 'scsp-config.json이 기준선 이후 변경되었습니다. 공존 테스트를 다시 실행하십시오.'
}

Write-Output '핵심 바이너리 기준선 검증을 통과했습니다.'
exit 0
