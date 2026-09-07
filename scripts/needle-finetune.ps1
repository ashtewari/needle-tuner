[CmdletBinding()]
param(
    [string]$DatasetPath = 'IntentEvalHarness/Dataset/training_set.300.jsonl',
    [string]$RunName = '',
    [int]$Epochs = 30,
    [int]$LoraRank = 16,
    [int]$LoraAlpha = 32,
    [double]$LearningRate = 0.0001,
    [int]$BatchSize = 4,
    [int]$MaxLen = 1024,
    [double]$ValSplit = 0.1,
    [string]$BaseCheckpoint = 'IntentEvalHarness/weights/base/needle2.pkl',
    [string]$NativeLibrary = '',
    [string]$PythonBin = '',
    [string]$NeedleBin = '',
    [switch]$SmokeTest,
    [switch]$DryRun,
    [switch]$SkipInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryPath {
    param([string]$BasePath, [string]$PathValue)
    if ([System.IO.Path]::IsPathRooted($PathValue)) { return [System.IO.Path]::GetFullPath($PathValue) }
    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Get-RepositoryRelativePath {
    param([string]$BasePath, [string]$PathValue)
    return [System.IO.Path]::GetRelativePath($BasePath, $PathValue).Replace('\', '/')
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'IntentEvalHarness'
$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
$architectureSuffix = if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) { 'arm64' } elseif ($architecture -eq [System.Runtime.InteropServices.Architecture]::X64) { 'x64' } else { throw "Unsupported processor architecture: $architecture" }
$rid = if ($runningOnWindows) { "win-$architectureSuffix" } else { "linux-$architectureSuffix" }
$venvRoot = Join-Path $repoRoot '.venv-needle'
$defaultPython = if ($runningOnWindows) { Join-Path $venvRoot 'Scripts/python.exe' } else { Join-Path $venvRoot 'bin/python' }
$defaultNeedle = if ($runningOnWindows) { Join-Path $venvRoot 'Scripts/needle.exe' } else { Join-Path $venvRoot 'bin/needle' }
$libraryName = if ($runningOnWindows) { 'libneedle.dll' } else { 'libneedle.so' }

$datasetFullPath = Resolve-RepositoryPath $repoRoot $DatasetPath
$baseCheckpointPath = Resolve-RepositoryPath $repoRoot $BaseCheckpoint
$baseManifestPath = Join-Path (Split-Path -Parent $baseCheckpointPath) 'base-checkpoint.manifest.json'
$PythonBin = if ([string]::IsNullOrWhiteSpace($PythonBin)) { $defaultPython } else { Resolve-RepositoryPath $repoRoot $PythonBin }
$NeedleBin = if ([string]::IsNullOrWhiteSpace($NeedleBin)) { $defaultNeedle } else { Resolve-RepositoryPath $repoRoot $NeedleBin }
$NativeLibrary = if ([string]::IsNullOrWhiteSpace($NativeLibrary)) { Join-Path $projectRoot "native/$rid/$libraryName" } else { Resolve-RepositoryPath $repoRoot $NativeLibrary }
$lossPlotScript = Join-Path $PSScriptRoot 'needle-loss-plot.py'

if ($SmokeTest) {
    $Epochs = 1
    if ([string]::IsNullOrWhiteSpace($RunName)) { $RunName = 'smoke_' + (Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss') }
}
if ($MaxLen -lt 1024) { throw '-MaxLen must be at least 1024 for a quality-comparable run.' }
if ($Epochs -lt 1) { throw '-Epochs must be positive.' }
if ([string]::IsNullOrWhiteSpace($RunName)) { $RunName = (Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss') }
if ($RunName -notmatch '^[A-Za-z0-9._-]+$') { throw '-RunName may contain only letters, digits, ".", "_", and "-".' }

$runRoot = Join-Path $projectRoot "weights/$RunName"
$checkpointsRoot = Join-Path $runRoot 'checkpoints'
$adapterPath = Join-Path $checkpointsRoot 'needle_lora.pkl'
$runBaseCheckpoint = Join-Path $checkpointsRoot 'needle2.pkl'
$artifactFileName = 'needle_tuned.cact'
$artifactPath = Join-Path $runRoot $artifactFileName
$manifestPath = Join-Path $runRoot 'manifest.json'
$finetuneLogPath = Join-Path $runRoot 'finetune.log'
$lossPlotPath = Join-Path $runRoot 'loss_curve.svg'

if ($DryRun) {
    Write-Host "Mode: $(if ($SmokeTest) { 'smoke-test' } else { 'quality-run' })"
    Write-Host "Dataset: $datasetFullPath"
    Write-Host "Base checkpoint: $baseCheckpointPath"
    Write-Host "Native library: $NativeLibrary"
    Write-Host "Python: $PythonBin"
    Write-Host "Run: $RunName (epochs=$Epochs, rank=$LoraRank, alpha=$LoraAlpha, lr=$LearningRate, batch=$BatchSize, max-len=$MaxLen, val-split=$ValSplit)"
    return
}

foreach ($required in @($datasetFullPath, $baseCheckpointPath, $NativeLibrary, $PythonBin, $lossPlotScript)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required training prerequisite was not found: $required" }
}
if (-not (Test-Path -LiteralPath $baseManifestPath)) {
    throw "Base checkpoint provenance was not found: $baseManifestPath. Run scripts/prepare-needle-base-checkpoint.sh from WSL."
}
$recordedBaseHash = (Get-Content -LiteralPath $baseManifestPath -Raw | ConvertFrom-Json).sha256
$actualBaseHash = (Get-FileHash -LiteralPath $baseCheckpointPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($recordedBaseHash -ne $actualBaseHash) {
    throw "Base checkpoint hash does not match $baseManifestPath. Re-run scripts/prepare-needle-base-checkpoint.sh."
}
if ($SkipInstall) { Write-Warning '-SkipInstall is a compatibility no-op; this wrapper never installs packages.' }
$nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($null -eq $nvidiaSmi) { throw 'nvidia-smi is unavailable. GPU training requires a visible GPU.' }
& $nvidiaSmi.Source | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'nvidia-smi could not query a GPU. Repair GPU support before training.' }
$backend = (& $PythonBin -c 'import jax; print(jax.default_backend())').Trim()
if ($LASTEXITCODE -ne 0 -or $backend -ne 'gpu') { throw "JAX selected backend '$backend', not GPU. Recreate the pinned environment with CUDA support." }

if (-not (Test-Path -LiteralPath $NeedleBin)) {
    $NeedleBin = $PythonBin
    $needlePrefix = @('-m', 'needle.cli')
}
else {
    $needlePrefix = @()
}

New-Item -ItemType Directory -Force -Path $checkpointsRoot | Out-Null
if ([System.IO.Path]::GetFullPath($baseCheckpointPath) -ne [System.IO.Path]::GetFullPath($runBaseCheckpoint)) {
    Copy-Item -LiteralPath $baseCheckpointPath -Destination $runBaseCheckpoint -Force
}

$env:XLA_PYTHON_CLIENT_PREALLOCATE = if ($env:XLA_PYTHON_CLIENT_PREALLOCATE) { $env:XLA_PYTHON_CLIENT_PREALLOCATE } else { 'false' }
$learningRateText = $LearningRate.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$valSplitText = $ValSplit.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$finetuneArgs = @($needlePrefix + @('finetune', $datasetFullPath, '--epochs', $Epochs, '--lora-rank', $LoraRank, '--lora-alpha', $LoraAlpha, '--lr', $learningRateText, '--batch-size', $BatchSize, '--max-len', $MaxLen, '--val-split', $valSplitText, '--out', 'checkpoints/needle_lora.pkl'))
$buildArgs = @($needlePrefix + @('build', 'checkpoints/needle2.pkl', '--lora', 'checkpoints/needle_lora.pkl', '--out', $artifactFileName))

Push-Location $runRoot
try {
    Write-Host "Starting Needle $(if ($SmokeTest) { 'smoke' } else { 'fine-tune' }) run '$RunName'"
    & $NeedleBin @finetuneArgs 2>&1 | Tee-Object -FilePath $finetuneLogPath
    if ($LASTEXITCODE -ne 0) { throw "Needle fine-tune failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $adapterPath)) { throw "Needle fine-tune completed without adapter: $adapterPath" }
    & $PythonBin $lossPlotScript $finetuneLogPath $lossPlotPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $lossPlotPath)) { throw "Loss plot was not created: $lossPlotPath" }
    & $NeedleBin @buildArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $artifactPath)) { throw "Needle build completed without artifact: $artifactPath" }
}
finally {
    Pop-Location
}

$manifest = [ordered]@{
    providerKey = 'needleTuned' + ($RunName -replace '[^A-Za-z0-9]', '')
    displayName = "Needle Tuned ($RunName)"
    weightsFile = $artifactFileName
    sha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    createdUtc = (Get-Date).ToUniversalTime().ToString('o')
    datasetPath = Get-RepositoryRelativePath $repoRoot $datasetFullPath
    baseCheckpointPath = Get-RepositoryRelativePath $repoRoot $baseCheckpointPath
    adapterPath = Get-RepositoryRelativePath $repoRoot $adapterPath
    command = [ordered]@{ script = 'scripts/needle-finetune.ps1'; runName = $RunName; epochs = $Epochs; loraRank = $LoraRank; loraAlpha = $LoraAlpha; learningRate = $LearningRate; batchSize = $BatchSize; maxLen = $MaxLen; valSplit = $ValSplit; smokeTest = [bool]$SmokeTest }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Tuned artifact: $(Get-RepositoryRelativePath $repoRoot $artifactPath)"
Write-Host "Manifest: $(Get-RepositoryRelativePath $repoRoot $manifestPath)"
Write-Host "Provider key: $($manifest.providerKey)"
