<#
.SYNOPSIS
  Prepare the ignored local Needle base checkpoint required by the fine-tune wrappers.

.DESCRIPTION
  -Download resolves immutable metadata from the official Hugging Face repository
  (Cactus-Compute/needle2 by default), verifies its published LFS SHA-256, and
  records the resolved revision. -Source imports a user-supplied file only when
  its expected SHA-256 is supplied via -Sha256 and matches.

.EXAMPLE
  pwsh scripts/prepare-needle-base-checkpoint.ps1 -Download

.EXAMPLE
  pwsh scripts/prepare-needle-base-checkpoint.ps1 -Source C:\path\needle2.pkl -Sha256 <hash>
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Download,
    [string]$Source = '',
    [string]$Sha256 = '',
    [string]$Repository = 'Cactus-Compute/needle2',
    [string]$Filename = 'checkpoints/needle2.pkl',
    [string]$Output = '',
    [string]$PythonBin = '',
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Test-Sha256 {
    param([string]$Value)
    return $Value -match '^[0-9a-fA-F]{64}$'
}

if ($Download -and $Source) {
    throw 'Choose exactly one of -Download or -Source.'
}
if (-not $Download -and -not $Source) {
    throw 'Choose -Download or -Source <path>. Use -? for usage.'
}

$mode = if ($Download) { 'download' } else { 'source' }

if ($mode -eq 'source') {
    if (-not (Test-Sha256 $Sha256)) {
        throw '-Source requires a 64-character SHA-256 supplied with -Sha256.'
    }
}
elseif ($Sha256) {
    throw '-Sha256 is only used with -Source; official downloads verify published metadata.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([string]$RelativeOrAbsolutePath)
    if ([System.IO.Path]::IsPathRooted($RelativeOrAbsolutePath)) {
        return [System.IO.Path]::GetFullPath($RelativeOrAbsolutePath)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RelativeOrAbsolutePath))
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = 'IntentEvalHarness/weights/base/needle2.pkl'
}
$outputPath = Resolve-RepoPath $Output
$outputDir = Split-Path -Parent $outputPath
$manifestPath = Join-Path $outputDir 'base-checkpoint.manifest.json'
if ([string]::IsNullOrWhiteSpace($PythonBin)) {
    $PythonBin = Join-Path $repoRoot '.venv-needle\Scripts\python.exe'
}

if ($DryRun) {
    if ($mode -eq 'download') {
        Write-Host "Would download $Repository/$Filename using $PythonBin, resolve an immutable revision, verify the published LFS SHA-256, and write $outputPath"
    }
    else {
        $resolvedSource = Resolve-RepoPath $Source
        Write-Host "Would verify and import $resolvedSource to $outputPath with SHA-256 $Sha256"
    }
    return
}

if (-not $PSCmdlet.ShouldProcess($outputPath, 'Prepare verified base checkpoint')) {
    return
}

if ((Test-Path -LiteralPath $outputPath) -and -not $Force) {
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Existing checkpoint lacks provenance: $outputPath. Re-run with -Force to replace it."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $recordedHash = [string]$manifest.sha256
    if (-not (Test-Sha256 $recordedHash)) {
        throw "Existing checkpoint provenance manifest has an invalid SHA-256: $manifestPath. Re-run with -Force to replace it."
    }
    $recordedHash = $recordedHash.ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($recordedHash -ne $actualHash) {
        throw "Existing checkpoint hash does not match $manifestPath. Re-run with -Force to replace it."
    }
    Write-Host "Verified existing base checkpoint: $outputPath"
    return
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$stage = Join-Path $outputDir ('.prepare-base-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    if ($mode -eq 'download') {
        if (-not (Test-Path -LiteralPath $PythonBin)) {
            throw "Pinned Python environment was not found: $PythonBin. Run scripts/bootstrap-needle.ps1 first."
        }

        $metadataPath = Join-Path $stage 'official-metadata.json'
        $downloadScript = @'
import hashlib
import json
import pathlib
import re
import sys

from huggingface_hub import HfApi, get_hf_file_metadata, hf_hub_download, hf_hub_url

stage = pathlib.Path(sys.argv[1])
metadata_path = pathlib.Path(sys.argv[2])
repository = sys.argv[3]
filename = sys.argv[4]
info = HfApi().model_info(repository)
url = hf_hub_url(repository, filename, revision=info.sha)
metadata = get_hf_file_metadata(url)
expected = metadata.etag.strip('"').lower()
if not re.fullmatch(r"[0-9a-f]{64}", expected):
    raise SystemExit(f"Official metadata did not provide a SHA-256 LFS ETag for {filename}: {expected!r}")
downloaded = pathlib.Path(hf_hub_download(
    repo_id=repository,
    filename=filename,
    repo_type="model",
    revision=info.sha,
    local_dir=str(stage),
))
actual = hashlib.sha256(downloaded.read_bytes()).hexdigest()
if actual != expected:
    raise SystemExit(f"Downloaded checkpoint SHA-256 mismatch: expected {expected}, got {actual}")
pathlib.Path(metadata_path).write_text(json.dumps({
    "source": "official-hugging-face",
    "repository": repository,
    "filename": filename,
    "revision": info.sha,
    "url": url,
    "sha256": actual,
    "bytes": downloaded.stat().st_size,
}, indent=2) + "\n", encoding="utf-8")
'@

        $downloaded = $false
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            $downloadScript | & $PythonBin - $stage $metadataPath $Repository $Filename
            if ($LASTEXITCODE -eq 0) {
                $downloaded = $true
                break
            }
            if ($attempt -lt 3) {
                $delay = [math]::Pow(2, $attempt)
                Write-Warning "Official checkpoint metadata/download attempt $attempt failed; retrying in ${delay}s."
                Start-Sleep -Seconds $delay
            }
        }
        if (-not $downloaded) {
            throw "Official checkpoint acquisition failed after three attempts. Check network access to https://huggingface.co/$Repository and retry."
        }

        $candidate = Join-Path $stage $Filename
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Official download completed without $Filename."
        }
        Copy-Item -LiteralPath $candidate -Destination $outputPath -Force

        $finalizeScript = @'
import hashlib
import json
import pathlib
import sys
from datetime import datetime, timezone

metadata = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
output = pathlib.Path(sys.argv[3])
actual = hashlib.sha256(output.read_bytes()).hexdigest()
if actual != metadata["sha256"]:
    raise SystemExit(f"Copied checkpoint SHA-256 mismatch: expected {metadata['sha256']}, got {actual}")
metadata.update({
    "schemaVersion": 1,
    "preparedUtc": datetime.now(timezone.utc).isoformat(),
    "localFile": output.name,
})
pathlib.Path(sys.argv[2]).write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
'@
        $finalizeScript | & $PythonBin - $metadataPath $manifestPath $outputPath
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to finalize checkpoint provenance manifest.'
        }
    }
    else {
        $resolvedSource = Resolve-RepoPath $Source
        if (-not (Test-Path -LiteralPath $resolvedSource)) {
            throw "User-supplied checkpoint was not found: $resolvedSource"
        }
        $actualHash = (Get-FileHash -LiteralPath $resolvedSource -Algorithm SHA256).Hash.ToLowerInvariant()
        $expectedHash = $Sha256.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "User-supplied checkpoint SHA-256 mismatch: expected $expectedHash, got $actualHash."
        }
        Copy-Item -LiteralPath $resolvedSource -Destination $outputPath -Force
        $copiedHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($copiedHash -ne $expectedHash) {
            throw "Copied checkpoint SHA-256 mismatch: expected $expectedHash, got $copiedHash."
        }
        [ordered]@{
            schemaVersion = 1
            preparedUtc = (Get-Date).ToUniversalTime().ToString('o')
            source = 'user-supplied-import'
            sourceFileName = (Split-Path -Leaf $resolvedSource)
            localFile = (Split-Path -Leaf $outputPath)
            sha256 = $copiedHash
            bytes = (Get-Item -LiteralPath $outputPath).Length
        } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    }
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}

$finalHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Prepared verified base checkpoint: $outputPath"
Write-Host "SHA-256: $finalHash"
Write-Host "Manifest: $manifestPath"
