[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('auto', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')]
    [string]$Rid = 'auto',
    [ValidateSet('2.0.10', '3.0.2')]
    [string]$EngineVersion = '3.0.2',
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Pinned wheel provenance per supported engine version; never accept an
# arbitrary caller-supplied URL/hash pair here.
$pinnedWheels = @{
    '2.0.10' = @{
        url = 'https://files.pythonhosted.org/packages/24/ac/84d720ba744e79f3fa5483f0ea6b71f14d329fae3623dd0f74236f19c938/cactus_needle-2.0.10-py3-none-any.whl'
        sha256 = 'f263b8e74fde5e225bb19a1c2747a3c2164272e27f1a7b948ecea3f3e1d925d9'
    }
    '3.0.2' = @{
        url = 'https://files.pythonhosted.org/packages/f3/b0/7b2ac5951fc639aa113a8244530f7d236645212bc6144f55a099dd6c594f/cactus_needle-3.0.2-py3-none-any.whl'
        sha256 = '99200776c42b2af93325326f1030b49da6af3fa9d66e5d979b44f5e472e4e739'
    }
}

$cactusVersion = $EngineVersion
$wheelUrl = $pinnedWheels[$EngineVersion].url
$wheelSha256 = $pinnedWheels[$EngineVersion].sha256

function Resolve-Rid {
    if ($Rid -ne 'auto') {
        return $Rid
    }

    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    $suffix = if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) { 'arm64' } elseif ($architecture -eq [System.Runtime.InteropServices.Architecture]::X64) { 'x64' } else { throw "Unsupported processor architecture: $architecture" }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return "win-$suffix"
    }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        return "linux-$suffix"
    }

    throw 'Only Windows and Linux/WSL native engines are supported.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedRid = Resolve-Rid
$nativeRoot = Join-Path $repoRoot 'IntentEvalHarness\native'
# Keyed by literal engine version (not "which one is default") so native/venv
# locations are stable regardless of the -EngineVersion parameter default.
$destinationDirectory = if ($EngineVersion -eq '2.0.10') { Join-Path $nativeRoot $resolvedRid } else { Join-Path (Join-Path $nativeRoot $resolvedRid) $EngineVersion }
$libraryName = if ($resolvedRid.StartsWith('win-')) { 'libneedle.dll' } else { 'libneedle.so' }
$destination = Join-Path $destinationDirectory $libraryName
$provenancePath = Join-Path $destinationDirectory 'acquisition.json'
$venvRoot = if ($EngineVersion -eq '2.0.10') { Join-Path $repoRoot '.venv-needle' } else { Join-Path $repoRoot ".venv-needle-$EngineVersion" }
$venvPython = if ($resolvedRid.StartsWith('win-')) { Join-Path $venvRoot 'Scripts\python.exe' } else { Join-Path $venvRoot 'bin\python' }
$bootstrapPath = Join-Path $venvRoot '.needle-bootstrap.json'

if ($DryRun) {
    Write-Host "Would use RID: $resolvedRid"
    Write-Host "Would require verified cactus-needle $cactusVersion from $wheelUrl"
    Write-Host "Would run its official 'needle fetch --out' command and install $destination"
    return
}

if (-not $PSCmdlet.ShouldProcess($destination, "Acquire verified $libraryName")) {
    return
}

if ((Test-Path -LiteralPath $destination) -and -not $Force) {
    if (-not (Test-Path -LiteralPath $provenancePath)) {
        throw "Existing native engine has no provenance manifest: $destination. Re-run with -Force to reacquire it."
    }
    $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    $actualHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($provenance.sha256 -ne $actualHash) {
        throw "Existing native engine hash does not match its provenance manifest. Re-run with -Force to reacquire it."
    }
    Write-Host "Verified existing native engine: $destination"
    return
}

if (-not (Test-Path -LiteralPath $venvPython)) {
    throw "Pinned Python environment not found at $venvPython. Run 'bash scripts/bootstrap-wsl.sh' (WSL) or create the equivalent .venv-needle environment first."
}
if (-not (Test-Path -LiteralPath $bootstrapPath)) {
    throw "Cannot verify package provenance: $bootstrapPath is missing. Recreate the environment with scripts/bootstrap-wsl.sh."
}

$bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw | ConvertFrom-Json
if ($bootstrap.cactusNeedle.version -ne $cactusVersion -or
    $bootstrap.cactusNeedle.wheelUrl -ne $wheelUrl -or
    $bootstrap.cactusNeedle.wheelSha256 -ne $wheelSha256) {
    throw 'Cannot verify cactus-needle provenance from the bootstrap manifest. Recreate the environment with scripts/bootstrap-wsl.sh.'
}

$installedVersion = (& $venvPython -c "import importlib.metadata as m; print(m.version('cactus-needle'))").Trim()
if ($LASTEXITCODE -ne 0 -or $installedVersion -ne $cactusVersion) {
    throw "Expected cactus-needle $cactusVersion in the pinned environment, found '$installedVersion'. Recreate the environment with scripts/bootstrap-wsl.sh."
}

$stage = Join-Path $nativeRoot ('.acquire-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    $needleCli = if ($resolvedRid.StartsWith('win-')) { Join-Path $venvRoot 'Scripts\needle.exe' } else { Join-Path $venvRoot 'bin\needle' }
    if (Test-Path -LiteralPath $needleCli) {
        & $needleCli fetch --out $stage
    }
    else {
        & $venvPython -m needle.cli fetch --out $stage
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Official cactus-needle fetch failed with exit code $LASTEXITCODE."
    }

    $fetched = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object Name -eq $libraryName | Select-Object -First 1
    if ($null -eq $fetched) {
        throw "Official cactus-needle fetch did not produce $libraryName for $resolvedRid. Verify that this RID is supported by the current official package."
    }

    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $fetched.FullName -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        schemaVersion = 1
        acquiredUtc = (Get-Date).ToUniversalTime().ToString('o')
        rid = $resolvedRid
        library = $libraryName
        sha256 = $hash
        source = [ordered]@{
            command = 'needle fetch --out'
            package = 'cactus-needle'
            version = $cactusVersion
            wheelUrl = $wheelUrl
            wheelSha256 = $wheelSha256
        }
        binaryChecksumPublished = $false
        verification = 'No published native-binary checksum was available in the documented official source. Provenance is verified through the pinned official PyPI wheel hash; the local binary SHA-256 is recorded for subsequent tamper detection.'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $provenancePath -Encoding utf8
    Write-Host "Native engine acquired: $destination"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
