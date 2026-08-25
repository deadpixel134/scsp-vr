[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedDobbyHash = '8015DE7D867245A1095D13947A63763878E4CF5FD3D3089B63CC39200B055DED'
$package = [System.IO.Path]::GetFullPath($PackageRoot)
$manifestPath = Join-Path $package 'package-manifest.json'
$payload = Join-Path $package 'payload'
$installScript = Join-Path $package 'Install-SongPrismVR.ps1'
$uninstallScript = Join-Path $package 'Uninstall-SongPrismVR.ps1'
$installationModule = Join-Path $package 'SongPrismVR.Installation.psm1'
$installerExe = Join-Path $package 'SongPrismVR.Installer.exe'

function Assert-True {
    param([bool]$Value, [string]$Message)
    if (-not $Value) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected=[$Expected] Actual=[$Actual]"
    }
}

function New-FakeGameRoot {
    param([string]$Parent, [string]$Name)
    $root = Join-Path $Parent $Name
    [System.IO.Directory]::CreateDirectory($root) | Out-Null
    foreach ($required in @('imasscprism.exe', 'GameAssembly.dll', 'UnityPlayer.dll')) {
        [System.IO.File]::WriteAllText((Join-Path $root $required), "fixture-$required")
    }
    return $root
}

function Assert-NativeExport {
    param([string]$Path, [string]$Export)
    $handle = [System.Runtime.InteropServices.NativeLibrary]::Load($Path)
    try {
        $address = [IntPtr]::Zero
        $found = [System.Runtime.InteropServices.NativeLibrary]::TryGetExport(
            $handle,
            $Export,
            [ref]$address)
        Assert-True ($found -and $address -ne [IntPtr]::Zero) "Native export missing: $Export in $Path"
    }
    finally {
        [System.Runtime.InteropServices.NativeLibrary]::Free($handle)
    }
}

Assert-True (Test-Path -LiteralPath $manifestPath -PathType Leaf) 'package-manifest.json is missing.'
Assert-True (Test-Path -LiteralPath $payload -PathType Container) 'payload directory is missing.'
Assert-True (Test-Path -LiteralPath $installScript -PathType Leaf) 'PowerShell installer is missing.'
Assert-True (Test-Path -LiteralPath $uninstallScript -PathType Leaf) 'PowerShell uninstaller is missing.'
Assert-True (Test-Path -LiteralPath $installationModule -PathType Leaf) 'PowerShell installation module is missing.'
Assert-True (Test-Path -LiteralPath $installerExe -PathType Leaf) 'Standalone installer executable is missing.'

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
Assert-Equal 'winhttp-doorstop' ([string]$manifest.loader) 'Unexpected package loader.'
$verified = 0
foreach ($file in $manifest.files) {
    $relative = ([string]$file.path).Replace('/', '\')
    $path = Join-Path $payload $relative
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Manifest file missing: $($file.path)"
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    Assert-Equal ([string]$file.sha256).ToUpperInvariant() $hash "Manifest hash mismatch: $($file.path)"
    $verified++
}
Assert-Equal ([int]$manifest.files.Count) $verified 'Manifest verification count mismatch.'

$dobbyPath = Join-Path $payload 'BepInEx\core\dobby.dll'
$openXrPath = Join-Path $payload 'vrmod\runtime\openxr_loader.dll'
Assert-Equal $expectedDobbyHash (Get-FileHash -LiteralPath $dobbyPath -Algorithm SHA256).Hash.ToUpperInvariant() 'Unexpected Dobby binary.'
Assert-NativeExport $dobbyPath 'DobbyHook'
Assert-NativeExport $openXrPath 'xrGetInstanceProcAddr'

$doorstopConfig = Get-Content -Raw -LiteralPath (Join-Path $payload 'doorstop_config.ini')
Assert-True ($doorstopConfig -match '(?m)^target_assembly\s*=\s*vrmod\\runtime\\SongPrismVR\.RuntimeBootstrap\.dll\s*$') 'Doorstop target assembly is invalid.'
Assert-True ($doorstopConfig -match '(?m)^coreclr_path\s*=\s*dotnet\\coreclr\.dll\s*$') 'Doorstop CoreCLR path is invalid.'
Assert-True ($doorstopConfig -match '(?m)^corlib_dir\s*=\s*dotnet\s*$') 'Doorstop corlib path is invalid.'
Assert-Equal '6.0.7' (Get-Content -LiteralPath (Join-Path $payload 'dotnet\.version') | Select-Object -Last 1).Trim() 'Unexpected packaged .NET runtime version.'
$defaultSettings = Get-Content -Raw -LiteralPath (Join-Path $payload 'vrmod\config\settings.json') | ConvertFrom-Json
Assert-Equal 0.65 ([double]$defaultSettings.render.eyeRenderScale) 'Unsafe packaged eye render scale.'
Assert-Equal 0.275 ([double]$defaultSettings.render.worldEyeOffsetScale) 'Unsafe packaged eye/head scale.'
Assert-Equal 'all-off' ([string]$defaultSettings.render.visualEffectMode) 'Unsupported packaged VFX mode.'
Assert-True ([bool]$defaultSettings.input.requireGameFocus) 'Packaged focus-safety default must be enabled.'

$temporaryBase = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) 'SongPrismVR.Distribution.Tests'))
$runRoot = Join-Path $temporaryBase ([Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
try {
    $cleanGame = New-FakeGameRoot $runRoot 'clean-game'
    & $installScript -GameRoot $cleanGame -PackageRoot $package
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $cleanGame 'version.dll'))) 'Clean install created version.dll.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $cleanGame 'scsp_localify'))) 'Clean install created scsp_localify.'
    Assert-Equal $expectedDobbyHash (Get-FileHash -LiteralPath (Join-Path $cleanGame 'BepInEx\core\dobby.dll') -Algorithm SHA256).Hash.ToUpperInvariant() 'Clean install Dobby mismatch.'
    Assert-Equal (Get-FileHash -LiteralPath (Join-Path $payload 'vrmod\config\settings.json') -Algorithm SHA256).Hash (Get-FileHash -LiteralPath (Join-Path $cleanGame 'vrmod\config\settings.json') -Algorithm SHA256).Hash 'Clean install settings mismatch.'
    & $uninstallScript -GameRoot $cleanGame
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $cleanGame 'BepInEx\core\dobby.dll'))) 'Clean uninstall left the packaged Dobby file.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $cleanGame 'winhttp.dll'))) 'Clean uninstall left winhttp.dll.'
    Assert-True (Test-Path -LiteralPath (Join-Path $cleanGame 'vrmod\config\settings.json') -PathType Leaf) 'Clean uninstall removed user settings.'

    $localifyGame = New-FakeGameRoot $runRoot 'localify-game'
    [System.IO.File]::WriteAllText((Join-Path $localifyGame 'version.dll'), 'localify-proxy')
    [System.IO.File]::WriteAllText((Join-Path $localifyGame 'scsp-config.json'), '{}')
    [System.IO.Directory]::CreateDirectory((Join-Path $localifyGame 'scsp_localify')) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $localifyGame 'scsp_localify\localify.json'), '{}')
    [System.IO.Directory]::CreateDirectory((Join-Path $localifyGame 'BepInEx\core')) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $localifyGame 'BepInEx\core\dobby.dll'), 'existing-dobby-sentinel')
    [System.IO.Directory]::CreateDirectory((Join-Path $localifyGame 'vrmod\config')) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $localifyGame 'vrmod\config\settings.json'), 'existing-settings-sentinel')
    & $installScript -GameRoot $localifyGame -PackageRoot $package
    Assert-Equal 'localify-proxy' (Get-Content -Raw -LiteralPath (Join-Path $localifyGame 'version.dll')) 'Localify proxy was changed.'
    Assert-Equal 'existing-dobby-sentinel' (Get-Content -Raw -LiteralPath (Join-Path $localifyGame 'BepInEx\core\dobby.dll')) 'Existing Dobby was changed.'
    Assert-Equal 'existing-settings-sentinel' (Get-Content -Raw -LiteralPath (Join-Path $localifyGame 'vrmod\config\settings.json')) 'Existing settings were changed.'
    & $uninstallScript -GameRoot $localifyGame
    Assert-Equal 'localify-proxy' (Get-Content -Raw -LiteralPath (Join-Path $localifyGame 'version.dll')) 'Localify proxy was removed.'
    Assert-Equal 'existing-dobby-sentinel' (Get-Content -Raw -LiteralPath (Join-Path $localifyGame 'BepInEx\core\dobby.dll')) 'Existing Dobby was removed.'
    Assert-Equal 'existing-settings-sentinel' (Get-Content -Raw -LiteralPath (Join-Path $localifyGame 'vrmod\config\settings.json')) 'Existing settings were removed.'

    $modifiedGame = New-FakeGameRoot $runRoot 'modified-game'
    & $installScript -GameRoot $modifiedGame -PackageRoot $package
    $installedProxyHash = (Get-FileHash -LiteralPath (Join-Path $modifiedGame 'winhttp.dll') -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText(
        (Join-Path $modifiedGame 'vrmod\runtime\SongPrismVR.RuntimeBootstrap.dll'),
        'user-modified-runtime')
    & $uninstallScript -GameRoot $modifiedGame
    Assert-Equal $installedProxyHash (Get-FileHash -LiteralPath (Join-Path $modifiedGame 'winhttp.dll') -Algorithm SHA256).Hash 'Uninstall changed an earlier file before discovering a modified file.'
    Assert-True (Test-Path -LiteralPath (Join-Path $modifiedGame 'vrmod\install-state.json') -PathType Leaf) 'Modified-file uninstall removed install-state.'
}
finally {
    $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    $allowedPrefix = $temporaryBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($resolvedRunRoot.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRunRoot)) {
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
}

Write-Host "CHECK: distribution package $($manifest.version) passed $verified manifest hashes, native dependency loading, clean install, Localify coexistence and uninstall."
