param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$packs = Get-Content -LiteralPath (Join-Path $Directory 'runtime-packs.txt')
@(
    "Microsoft .NET runtime packs: $($packs -join ', ')",
    '=== .NET Core license ===',
    (Get-Content -LiteralPath (Join-Path $Directory 'NETCore-LICENSE.txt') -Raw),
    '=== .NET Core third-party notices ===',
    (Get-Content -LiteralPath (Join-Path $Directory 'NETCore-THIRD-PARTY-NOTICES.txt') -Raw),
    '=== Windows Desktop license ===',
    (Get-Content -LiteralPath (Join-Path $Directory 'WindowsDesktop-LICENSE.txt') -Raw)
) -join ([Environment]::NewLine * 2)
