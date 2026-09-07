[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipExpandedSeed,
    [switch]$SkipJsonlExports
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Core') {
    throw 'PowerShell 7 (pwsh) is required for deterministic UTF-8-without-BOM output.'
}

function ConvertTo-CanonicalLf {
    param([Parameter(Mandatory)][string]$Path)

    $content = [System.IO.File]::ReadAllText($Path)
    $content = $content -replace "`r`n?", "`n"
    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

function Get-LineCount {
    param([Parameter(Mandatory)][string]$Path)

    return @([System.IO.File]::ReadLines($Path)).Count
}

function Assert-IntegrityHash {
    param(
        [Parameter(Mandatory)][string]$IntegrityManifestPath,
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Path
    )

    $manifest = Get-Content -LiteralPath $IntegrityManifestPath -Raw | ConvertFrom-Json
    $expected = @($manifest.files | Where-Object { $_.path -eq $RelativePath })
    if ($expected.Count -ne 1) {
        throw "No unique integrity entry was found for $RelativePath."
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected[0].sha256) {
        throw "Integrity mismatch for $RelativePath. Expected $($expected[0].sha256), actual $actual."
    }
}

$Root = [System.IO.Path]::GetFullPath($Root)
$datasetDirectory = Join-Path $Root 'IntentEvalHarness/Dataset'
$projectPath = Join-Path $Root 'IntentEvalHarness/IntentEvalHarness.csproj'
$integrityManifestPath = Join-Path $Root 'IntentEvalHarness/integrity.manifest.json'
$expandedSeedPath = Join-Path $datasetDirectory 'needle-training-seed.300.json'
$standardJsonlPath = Join-Path $datasetDirectory 'training_set.300.jsonl'
$standardSummaryPath = Join-Path $datasetDirectory 'training_set.300.summary.json'
$oversamplePath = Join-Path $datasetDirectory 'training_set.300.oversample_unclear.jsonl'

foreach ($requiredPath in @($datasetDirectory, $projectPath, $integrityManifestPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required dataset-generation path was not found: $requiredPath"
    }
}

if (-not $SkipExpandedSeed) {
    & (Join-Path $PSScriptRoot 'generate-expanded-training-seed.ps1') -Root $Root
    if (-not $?) {
        throw 'Expanded-seed generator failed.'
    }

    ConvertTo-CanonicalLf -Path $expandedSeedPath
}

if (-not $SkipJsonlExports) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        throw 'dotnet SDK was not found. The standard JSONL artifacts require the standalone harness exporter.'
    }

    Push-Location $Root
    try {
        & $dotnet.Source run --project $projectPath -- --export-training-jsonl Dataset/needle-training-seed.300.json --output Dataset/training_set.300.jsonl
        if ($LASTEXITCODE -ne 0) {
            throw "Harness JSONL export failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    ConvertTo-CanonicalLf -Path $standardJsonlPath
    ConvertTo-CanonicalLf -Path $standardSummaryPath
}

if ((Get-LineCount -Path $standardJsonlPath) -ne 345) {
    throw "Expected 345 rows in $standardJsonlPath."
}

$writer = [System.IO.StreamWriter]::new($oversamplePath, $false, [System.Text.UTF8Encoding]::new($false))
$writer.NewLine = "`n"
$unclearRows = 0
$oversampledRows = 0
try {
    foreach ($line in [System.IO.File]::ReadLines($standardJsonlPath)) {
        $writer.WriteLine($line)
        $oversampledRows++

        try {
            $row = $line | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw "Cannot parse JSONL row while creating UNCLEAR oversample: $line"
        }

        if (@($row.answers).Count -eq 0) {
            $writer.WriteLine($line)
            $unclearRows++
            $oversampledRows++
        }
    }
}
finally {
    $writer.Dispose()
}

if ($unclearRows -ne 46 -or $oversampledRows -ne 391) {
    throw "Expected 46 UNCLEAR source rows and 391 oversampled rows; found $unclearRows and $oversampledRows."
}

Assert-IntegrityHash -IntegrityManifestPath $integrityManifestPath -RelativePath 'Dataset/needle-training-seed.300.json' -Path $expandedSeedPath
Assert-IntegrityHash -IntegrityManifestPath $integrityManifestPath -RelativePath 'Dataset/training_set.300.jsonl' -Path $standardJsonlPath
Assert-IntegrityHash -IntegrityManifestPath $integrityManifestPath -RelativePath 'Dataset/training_set.300.oversample_unclear.jsonl' -Path $oversamplePath

Write-Host "Generated deterministic dataset artifacts: expanded=345, standard=345, oversample=391."
