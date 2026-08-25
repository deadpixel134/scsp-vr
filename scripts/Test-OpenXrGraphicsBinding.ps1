param(
    [ValidateSet('d3d11', 'd3d12')]
    [string]$Api = 'd3d11'
)

$ErrorActionPreference = 'Stop'

$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $python) {
    throw 'python was not found on PATH.'
}

& $python.Source (Join-Path $PSScriptRoot 'Test-OpenXrGraphicsBinding.py') --api $Api
