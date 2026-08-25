Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SongPrismGameRoot {
    param([Parameter(Mandatory = $true)][string]$GameRoot)

    $resolved = [System.IO.Path]::GetFullPath($GameRoot)
    foreach ($required in @('imasscprism.exe', 'GameAssembly.dll', 'UnityPlayer.dll')) {
        if (-not [System.IO.File]::Exists([System.IO.Path]::Combine($resolved, $required))) {
            throw "게임 폴더가 아닙니다. 필수 파일을 찾지 못했습니다: $required"
        }
    }
    return $resolved
}

function Assert-SongPrismStopped {
    $running = @(Get-Process -Name 'imasscprism' -ErrorAction SilentlyContinue)
    if ($running.Count -ne 0) {
        throw 'imasscprism이 실행 중입니다. 게임을 완전히 종료한 뒤 다시 실행하세요.'
    }
}

function Get-LocalifyStatus {
    param([Parameter(Mandatory = $true)][string]$GameRoot)

    $versionProxy = Test-Path -LiteralPath (Join-Path $GameRoot 'version.dll') -PathType Leaf
    $config = Test-Path -LiteralPath (Join-Path $GameRoot 'scsp-config.json') -PathType Leaf
    $localifyRoot = Join-Path $GameRoot 'scsp_localify'
    $translationMarkers = @(
        (Join-Path $localifyRoot 'localify.json'),
        (Join-Path $localifyRoot 'local2.json'),
        (Join-Path $localifyRoot 'lyrics.json'),
        (Join-Path $localifyRoot 'scsp-bundle')
    )
    $markerCount = @($translationMarkers | Where-Object { Test-Path -LiteralPath $_ }).Count

    if ($versionProxy -and $config -and $markerCount -gt 0) {
        return 'Installed'
    }
    if ($versionProxy -and $config) {
        return 'LoaderOnly'
    }
    if ($versionProxy -or $config -or $markerCount -gt 0 -or (Test-Path -LiteralPath $localifyRoot)) {
        return 'Partial'
    }
    return 'Absent'
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Test-SafeRelativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        return $false
    }
    $normalized = $RelativePath.Replace('/', '\')
    foreach ($segment in $normalized.Split('\')) {
        if ($segment -eq '..') {
            return $false
        }
    }
    return $true
}

function Resolve-ContainedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if (-not (Test-SafeRelativePath $RelativePath)) {
        throw "허용되지 않는 패키지 경로입니다: $RelativePath"
    }
    $protected = $RelativePath.Replace('/', '\')
    if ($protected -ieq 'version.dll' -or
        $protected -ieq 'scsp-config.json' -or
        $protected -ieq 'scsp_localify' -or
        $protected.StartsWith('scsp_localify\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "scsp-localify 소유 경로는 변경할 수 없습니다: $RelativePath"
    }
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $candidate = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($rootFull, $RelativePath.Replace('/', '\')))
    if (-not $candidate.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "패키지 경로가 대상 폴더 밖을 가리킵니다: $RelativePath"
    }
    return $candidate
}

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $directory = [System.IO.Path]::GetDirectoryName($Path)
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = [System.IO.Path]::Combine(
        $directory,
        '.' + [System.IO.Path]::GetFileName($Path) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $replaceBackup = $Path + '.replace-backup'
    try {
        $json = $Value | ConvertTo-Json -Depth 12
        [System.IO.File]::WriteAllText(
            $temporary,
            $json + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false))
        if ([System.IO.File]::Exists($Path)) {
            [System.IO.File]::Replace($temporary, $Path, $replaceBackup, $true)
            if ([System.IO.File]::Exists($replaceBackup)) {
                [System.IO.File]::Delete($replaceBackup)
            }
        }
        else {
            [System.IO.File]::Move($temporary, $Path)
        }
    }
    finally {
        if ([System.IO.File]::Exists($temporary)) {
            [System.IO.File]::Delete($temporary)
        }
        if ([System.IO.File]::Exists($replaceBackup)) {
            [System.IO.File]::Delete($replaceBackup)
        }
    }
}

Export-ModuleMember -Function @(
    'Assert-SongPrismGameRoot',
    'Assert-SongPrismStopped',
    'Get-LocalifyStatus',
    'Get-FileSha256',
    'Test-SafeRelativePath',
    'Resolve-ContainedPath',
    'Write-JsonAtomic'
)
