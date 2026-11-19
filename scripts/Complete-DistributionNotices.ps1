param(
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$PayloadRoot,
    [Parameter(Mandatory)][string[]]$RuntimeNoticeDirectory
)

$ErrorActionPreference = 'Stop'
$verifiedFiles = @{
    'winhttp.dll' = '8c6cdbc38836dee87e3368f5de1994d7c0ccebf29e4ce7aba3c0981f9375412c'
    'BepInEx/core/dobby.dll' = '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'
    'vrmod/runtime/openxr_loader.dll' = '98faa0b1571b2d68f2daea62b816fe539e2c075a14528b8d328e55c7ab66f3a0'
    'dotnet/coreclr.dll' = '04377968c540f704aae92ecb0ec9bbfd5fddfe226dfca30aa3eeb52006a44fd8'
    'dotnet/System.Private.CoreLib.dll' = '2caa46d2ae726e135592033b6c682232a08698c23dbd1a9207f86c4cca081ad7'
}
foreach ($entry in $verifiedFiles.GetEnumerator()) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $PayloadRoot $entry.Key) -Algorithm SHA256).Hash
    if ($actual -ine $entry.Value) {
        throw "Packaged component does not match its license provenance: $($entry.Key)"
    }
}

$licenseRoot = Join-Path $PackageRoot 'licenses'
[IO.Directory]::CreateDirectory($licenseRoot) | Out-Null
$source = Join-Path $licenseRoot 'UnityDoorstop-4.5.0-source.zip'
Invoke-WebRequest -Uri 'https://github.com/NeighTools/UnityDoorstop/archive/33dab9a6733862eb81869ff08431d9478b28784b/UnityDoorstop-4.5.0.zip' -OutFile $source
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine 'a71789ffcb359f6a37c6d2c7bbdd0527231e9db00bf46637693cff88ba7ffb80') {
    throw 'Unity Doorstop source archive does not match its expected SHA-256.'
}

$archive = [IO.Compression.ZipFile]::OpenRead($source)
try {
    $entry = $archive.Entries | Where-Object FullName -Like '*/LICENSE'
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $doorstopLicense = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}
finally { $archive.Dispose() }

$coreNotices = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'NETCore-6.0.7-THIRD-PARTY-NOTICES.txt') -Raw
$jsonCppLicense = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'OpenXR-JsonCpp-LICENSE.txt') -Raw
$desktopNotices = foreach ($directory in $RuntimeNoticeDirectory) {
    & (Join-Path $PSScriptRoot 'Get-RuntimeNotices.ps1') -Directory $directory
}
Add-Content -LiteralPath (Join-Path $PayloadRoot 'vrmod/THIRD_PARTY_NOTICES.txt') -Encoding utf8 -Value @(
    '', '=== Unity Doorstop 4.5.0 license ===', $doorstopLicense,
    '=== BepInEx mini CoreCLR 6.0.7 third-party notices ===', $coreNotices,
    '=== JsonCpp bundled with OpenXR Loader 1.1.59 ===', $jsonCppLicense,
    ($desktopNotices | Select-Object -Unique)
)
