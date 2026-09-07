[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipExpandedSeed,
    [switch]$SkipJsonlExports
)

Write-Warning 'generate_needle_training_seed_300.ps1 is retained for compatibility. Use generate-training-datasets.ps1 instead.'
& (Join-Path $PSScriptRoot 'generate-training-datasets.ps1') @PSBoundParameters
if (-not $?) {
    exit 1
}
