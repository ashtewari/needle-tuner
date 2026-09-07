[CmdletBinding()]
param(
    [string]$VenvPath = '',
    [switch]$Cuda
)

$ErrorActionPreference = 'Stop'

$cactusVersion = '2.0.10'
$wheelUrl = 'https://files.pythonhosted.org/packages/24/ac/84d720ba744e79f3fa5483f0ea6b71f14d329fae3623dd0f74236f19c938/cactus_needle-2.0.10-py3-none-any.whl'
$wheelSha256 = 'f263b8e74fde5e225bb19a1c2747a3c2164272e27f1a7b948ecea3f3e1d925d9'
$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($VenvPath)) {
    $VenvPath = Join-Path $repoRoot '.venv-needle'
}
elseif (-not [System.IO.Path]::IsPathRooted($VenvPath)) {
    $VenvPath = Join-Path $repoRoot $VenvPath
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
elseif ($null -eq (Get-Command nvidia-smi -ErrorAction SilentlyContinue)) {
    Write-Warning 'No NVIDIA GPU diagnostic command is available. Installing the CPU-compatible pinned stack.'
}

$venvPython = Join-Path $VenvPath 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    & py -3.12 -m venv $VenvPath
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create the Python 3.12 virtual environment.' }
}

& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install --require-hashes --no-deps --requirement (Join-Path $PSScriptRoot 'requirements-cactus-needle.txt')
& $venvPython -m pip install --requirement (Join-Path $PSScriptRoot 'requirements-needle.txt')
if ($Cuda) {
    & $venvPython -m pip install 'jax[cuda12]==0.11.1'
}
& $venvPython -m pip check

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
& $venvPython -c $verification
if ($LASTEXITCODE -ne 0) { throw 'Pinned package verification failed.' }

[ordered]@{
    cactusNeedle = [ordered]@{
        version = $cactusVersion
        wheelUrl = $wheelUrl
        wheelSha256 = $wheelSha256
    }
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $VenvPath '.needle-bootstrap.json') -Encoding utf8

& $venvPython -c 'import jax; print("JAX version:", jax.__version__); print("Backend:", jax.default_backend()); print("Devices:", jax.devices())'
if ($LASTEXITCODE -ne 0) { throw 'JAX diagnostics failed.' }

Write-Host "Needle environment ready: $VenvPath"
