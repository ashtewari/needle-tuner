# Validation summary

## Scope and limits

- **Run date:** 2026-09-06 UTC recording of the September 5 clean-room work.
- **Purpose:** verify the standalone WSL setup, one explicit 30-epoch run,
  exact manifest-selected evaluation, skill stages, and post-run checks.
- **Held-out data:** `IntentEvalHarness/Dataset/intent-eval-dataset.json`,
  45 cases (nine intents × five). It was not used for training, but repeated
  use to choose experiments makes it a regression/model-selection set rather
  than an unbiased final test.
- **Artifact policy:** generated weights, checkpoints, adapters, native
  binaries, environments, and runtime reports are omitted. Values below are
  retained as provenance only.

## Bounded clean-room setup

The recorded setup categories and commands were:

```text
environment: Python 3.12; pinned JAX GPU backend; .NET SDK; PowerShell 7
bootstrap: bash scripts/bootstrap-wsl.sh --cuda
native: bash scripts/acquire-needle-native.sh
checkpoint: bash scripts/prepare-needle-base-checkpoint.sh --download
dataset: pwsh -NoProfile -File scripts/generate-training-datasets.ps1
```

The environment exposed an NVIDIA GPU to WSL and acquired the RID-specific
native engine. The package pin was `cactus-needle 2.0.10`; native binary
provenance is limited to the pinned package/fetch process because the binary
itself is not retained in this bundle.

## Explicit 30-epoch training

```text
bash scripts/needle-finetune.sh --run-name <run-name> \
  --dataset-path IntentEvalHarness/Dataset/training_set.300.jsonl \
  --epochs 30 --lora-rank 16 --lora-alpha 32 --learning-rate 0.0001 \
  --batch-size 4 --max-len 1024 --val-split 0.1
```

Recorded interval: `00:36:57.347Z` → `01:04:28.564Z`; duration **27m31s**
(approximately **27.5 minutes**). The run used CUDA-backed JAX on an RTX 3060
with 12 GB VRAM. Hardware and duration are observations for this run, not
portable performance promises.

The omitted candidate `.cact` manifest recorded SHA-256:

```text
8518c4774ebb41573021ab86f8ab6e5dc06422ac730b9ebbd30c866b26b03295
```

The candidate artifact is unavailable for independent re-hashing. This value
is therefore manifest/log provenance, not an independently reverified binary
hash.

The official base checkpoint provenance recorded:

```text
repository: Cactus-Compute/needle2
revision: 32e9e3a93b205f786929697446ae669cf0a84579
path: checkpoints/needle2.pkl
bytes: 90426504
sha256: 4b0a972d163ffc7678fb3c36bace508114872e9d2ce9e10f225825752d3795bc
```

## Exact 45-case evaluation

The candidate was selected from its generated manifest using its provider key,
relative `.cact` path, display name, and SHA-256. The evaluation categories
were:

```text
dotnet run --project IntentEvalHarness -- \
  --providers needleBase,<manifest-provider-key> \
  --compare-baseline Baselines/openAi_frozen_20260823_full45
```

Measured results on all 45 cases:

| Provider | Intent | Parameters | Fallbacks |
|---|---:|---:|---:|
| Tuned candidate | 19/45 (42.22%) | 23/45 (51.11%) | 15 |
| Base Needle | 28/45 (62.22%) | 30/45 (66.67%) | 9 |
| Frozen OpenAI reference | 42/45 (93.33%) | 32/45 (71.11%) | 0 |

The tuned candidate is a regression and is not a replacement or promotion
candidate. The frozen OpenAI reference is committed baseline evidence; no live
OpenAI request is part of this record.

## Skill execution and post-run checks

The skill execution covered these bounded stages:

```text
validation: dataset generation, integrity checks, .NET tests, harness help
setup: pinned WSL environment, CUDA diagnostics, native acquisition
training: explicit 30-epoch GPU run
selection: exact manifest/provider/path/hash environment selection
evaluation: base+tuned 45-case comparison against frozen baseline
post-run: artifact presence/log/plot checks and .NET regression tests
```

The historical WSL post-training .NET suite passed **32/32** during the
recorded execution. A later reviewer-fix verification passed **36/36** after
repository changes; it was not part of the historical training execution and
did not produce new training or evaluation results. The exact clean-room file
count and raw skill transcript are not claimed here; ephemeral identifiers and
verbose logs were intentionally excluded.

## Release interpretation

This bundle supports reproducibility review, not model-quality promotion. The
45-case set has influenced model selection, the tuned artifact is omitted, and
the native binary has no retained independently verifiable digest. A future
release decision requires human review and a separately created unseen test
set.
