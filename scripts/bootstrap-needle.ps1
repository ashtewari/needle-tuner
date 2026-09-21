[CmdletBinding()]
param(
    [string]$VenvPath = '',
    [ValidateSet('2.0.10', '3.0.2')]
    [string]$EngineVersion = '2.0.10',
    [switch]$Cuda
)

$ErrorActionPreference = 'Stop'

$cactusVersionDefault = '2.0.10'

# Pinned wheel provenance per supported engine version; never accept an
# arbitrary caller-supplied URL/hash pair here. 3.0.2 (Needle3) is
# inference-only in this repo, so it skips the JAX/flax/optax training stack
# entirely.
$pinnedWheels = @{
    '2.0.10' = @{
        url = 'https://files.pythonhosted.org/packages/24/ac/84d720ba744e79f3fa5483f0ea6b71f14d329fae3623dd0f74236f19c938/cactus_needle-2.0.10-py3-none-any.whl'
        sha256 = 'f263b8e74fde5e225bb19a1c2747a3c2164272e27f1a7b948ecea3f3e1d925d9'
        requirementsCactus = 'requirements-cactus-needle.txt'
        requirementsNeedle = 'requirements-needle.txt'
    }
    '3.0.2' = @{
        url = 'https://files.pythonhosted.org/packages/f3/b0/7b2ac5951fc639aa113a8244530f7d236645212bc6144f55a099dd6c594f/cactus_needle-3.0.2-py3-none-any.whl'
        sha256 = '99200776c42b2af93325326f1030b49da6af3fa9d66e5d979b44f5e472e4e739'
        requirementsCactus = 'requirements-cactus-needle-v3.txt'
        requirementsNeedle = 'requirements-needle-v3.txt'
    }
}

$cactusVersion = $EngineVersion
$wheelUrl = $pinnedWheels[$EngineVersion].url
$wheelSha256 = $pinnedWheels[$EngineVersion].sha256
$isInferenceOnly = $EngineVersion -ne $cactusVersionDefault
$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($VenvPath)) {
    $VenvPath = if ($isInferenceOnly) { Join-Path $repoRoot ".venv-needle-$EngineVersion" } else { Join-Path $repoRoot '.venv-needle' }
}
elseif (-not [System.IO.Path]::IsPathRooted($VenvPath)) {
    $VenvPath = Join-Path $repoRoot $VenvPath
}

if ($Cuda -and $isInferenceOnly) {
    throw "-Cuda is only supported with the default engine version ($cactusVersionDefault); $EngineVersion is inference-only in this repo."
}

& py -3.12 -c "import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 12) else 1)"
if ($LASTEXITCODE -ne 0) {
    throw 'Python 3.12 is required. Install it with the Python launcher enabled, then retry.'
}

if ($Cuda) {
    $nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($null -eq $nvidiaSmi -or -not (& $nvidiaSmi.Source 2>$null)) {
        throw 'CUDA was requested but nvidia-smi cannot query an NVIDIA GPU. Install/update the NVIDIA driver, then retry; this script will not install drivers.'
    }
}
elseif (-not $isInferenceOnly -and $null -eq (Get-Command nvidia-smi -ErrorAction SilentlyContinue)) {
    Write-Warning 'No NVIDIA GPU diagnostic command is available. Installing the CPU-compatible pinned stack.'
}

$venvPython = Join-Path $VenvPath 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    & py -3.12 -m venv $VenvPath
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create the Python 3.12 virtual environment.' }
}

& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install --require-hashes --no-deps --requirement (Join-Path $PSScriptRoot $pinnedWheels[$EngineVersion].requirementsCactus)
& $venvPython -m pip install --requirement (Join-Path $PSScriptRoot $pinnedWheels[$EngineVersion].requirementsNeedle)
if ($Cuda) {
    & $venvPython -m pip install 'jax[cuda12]==0.11.1'
}
& $venvPython -m pip check

if ($isInferenceOnly) {
    $verification = @"
import importlib.metadata as metadata
expected = {"cactus-needle": "$cactusVersion"}
actual = {name: metadata.version(name) for name in expected}
if actual != expected:
    raise SystemExit(f"Pin verification failed: {actual}")
print("Pinned packages verified:", ", ".join(f"{name}={version}" for name, version in actual.items()))
"@
}
else {
    $verification = @'
import importlib.metadata as metadata
expected = {
    "cactus-needle": "2.0.10",
    "jax": "0.11.1",
    "jaxlib": "0.11.1",
    "flax": "0.12.9",
    "optax": "0.2.8",
}
actual = {name: metadata.version(name) for name in expected}
if actual != expected:
    raise SystemExit(f"Pin verification failed: {actual}")
print("Pinned packages verified:", ", ".join(f"{name}={version}" for name, version in actual.items()))
'@
}
& $venvPython -c $verification
if ($LASTEXITCODE -ne 0) { throw 'Pinned package verification failed.' }

[ordered]@{
    cactusNeedle = [ordered]@{
        version = $cactusVersion
        wheelUrl = $wheelUrl
        wheelSha256 = $wheelSha256
    }
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $VenvPath '.needle-bootstrap.json') -Encoding utf8

if (-not $isInferenceOnly) {
    & $venvPython -c 'import jax; print("JAX version:", jax.__version__); print("Backend:", jax.default_backend()); print("Devices:", jax.devices())'
    if ($LASTEXITCODE -ne 0) { throw 'JAX diagnostics failed.' }
}

Write-Host "Needle environment ready: $VenvPath"
