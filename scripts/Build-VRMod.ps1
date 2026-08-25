[CmdletBinding()]
param(
    [string]$GameRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
$vrmodRoot = Join-Path $GameRoot 'vrmod'
$env:DOTNET_CLI_HOME = Join-Path $vrmodRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $vrmodRoot '.nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$coreTestProject = Join-Path $vrmodRoot 'tests\SongPrismVR.Core.Tests\SongPrismVR.Core.Tests.csproj'
$managementTestProject = Join-Path $vrmodRoot 'tests\SongPrismVR.Management.Tests\SongPrismVR.Management.Tests.csproj'
$runtimeProject = Join-Path $vrmodRoot 'src\SongPrismVR.RuntimeBootstrap\SongPrismVR.RuntimeBootstrap.csproj'
$configuratorProject = Join-Path $vrmodRoot 'src\SongPrismVR.Configurator\SongPrismVR.Configurator.csproj'
$installerProject = Join-Path $vrmodRoot 'src\SongPrismVR.Installer\SongPrismVR.Installer.csproj'
$nativeProbeBuild = Join-Path $vrmodRoot 'native\UnityD3D12Probe\build.ps1'

dotnet restore $coreTestProject --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Core test restore failed.' }
dotnet run --project $coreTestProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }

dotnet restore $managementTestProject --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Management test restore failed.' }
dotnet run --project $managementTestProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Management tests failed.' }

dotnet restore $runtimeProject --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Runtime bootstrap restore failed.' }
& $nativeProbeBuild -Configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Unity D3D12 native interface probe build failed.' }
dotnet build $runtimeProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Runtime bootstrap build failed.' }

dotnet build $configuratorProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Configurator build failed.' }
dotnet build $installerProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

Write-Host 'CHECK: SongPrismVR core/management tests and native/runtime/configurator/installer builds passed'
