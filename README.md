# Needle

Standalone training and evaluation tooling for Needle structured intent
extraction. The repository contains the .NET harness, authored and generated
dataset contracts, cross-platform native loading, pinned setup scripts, and
explicitly gated fine-tuning wrappers. Needle2 is the fine-tuning target;
Needle3 is supported for base-model evaluation only.

Generated weights, checkpoints, adapters, native binaries, Python environments,
loss plots, and runtime reports are local-only. Recreate them; do not commit or
host them. The Apache-2.0 license in [`LICENSE`](LICENSE) covers this
repository's source and documentation. It does not grant rights to third-party
models, packages, or API services.

## Prerequisites

For non-training validation:

- .NET SDK 9.0.310 (the required SDK is pinned by [`global.json`](global.json)).
- PowerShell 7 (`pwsh`) for deterministic dataset generation.
- Bash, Python 3, and standard WSL/Linux utilities.

For training or native acquisition:

- WSL 2 on Linux/Windows, Python 3.12, and network access to the pinned package
  source.
- An NVIDIA GPU visible to WSL and a GPU-enabled JAX backend. Training refuses
  CPU fallback.
- A verified local Needle base checkpoint at
  `IntentEvalHarness/weights/base/needle2.pkl`. Prepare it explicitly with the
  repository command below; fine-tune wrappers never download it themselves.

The Windows PowerShell setup is supported for environment/native preparation and
fine-tuning, but the reproducible training path documented here is WSL 2.

## Repository layout

```text
IntentEvalHarness/
  Dataset/       authored seed, held-out set, generated JSONL, manifests
  Baselines/     immutable frozen OpenAI baseline
  native/        ignored RID-specific libneedle binaries and provenance
  weights/       ignored local runs; README and manifests remain explainable
scripts/         setup, acquisition, dataset, fine-tuning, and plot utilities
tests/           offline .NET regression tests
docs/experiments/  canonical append-only history and small provenance records
artifacts/       ignored local evaluation output
```

## Clean validation (no GPU and no training)

From the repository root, these commands do not train or call OpenAI:

```bash
pwsh -NoProfile -File scripts/generate-training-datasets.ps1
dotnet test Needle.sln --nologo
dotnet run --project IntentEvalHarness -- --help
bash -n scripts/bootstrap-wsl.sh scripts/acquire-needle-native.sh scripts/needle-finetune.sh scripts/prepare-needle-base-checkpoint.sh
bash scripts/bootstrap-wsl.sh --help
bash scripts/acquire-needle-native.sh --dry-run
bash scripts/acquire-needle-native.sh --engine-version 3.0.2 --dry-run
bash scripts/prepare-needle-base-checkpoint.sh --download --dry-run
bash scripts/needle-finetune.sh --dry-run
bash scripts/needle-finetune.sh --smoke-test --dry-run
```

The generator deterministically verifies 345 expanded-seed rows, 345 standard
JSONL rows, 391 UNCLEAR-oversampled rows, and their pinned SHA-256 entries.
The frozen `IntentEvalHarness/Dataset/intent-eval-dataset.json` has 45 rows
(nine intents, five cases each) and must never be used for training. Because
its results have informed repeated experiment choices, it is held out from
training but is now a regression/model-selection set, not an unbiased test set
for generalization or production-readiness claims.

## WSL setup and native acquisition

See [`docs/WSL-SETUP.md`](docs/WSL-SETUP.md) for the clean-room sequence.
In brief, install the prerequisites, then choose CPU-compatible setup for
validation or CUDA setup for training:

```bash
# No GPU required for setup diagnostics; training still requires GPU JAX.
bash scripts/bootstrap-wsl.sh

# Required only for training/native evaluation on a GPU machine.
bash scripts/bootstrap-wsl.sh --cuda
bash scripts/acquire-needle-native.sh
bash scripts/prepare-needle-base-checkpoint.sh --download
```

The bootstrap pins `cactus-needle` 2.0.10, JAX 0.11.1, Flax 0.12.9, and
Optax 0.2.8. It never installs host GPU drivers. Native acquisition uses the
official `needle fetch --out` command, selects `linux-x64` or `linux-arm64`,
and records local `acquisition.json` provenance.

Base checkpoint preparation resolves the immutable revision of the official
`Cactus-Compute/needle2` Hugging Face artifact
`checkpoints/needle2.pkl`, verifies its published LFS SHA-256, then records
the revision, size, and hash in ignored
`IntentEvalHarness/weights/base/base-checkpoint.manifest.json`. It downloads
about 90 MB only when explicitly requested. To import a pre-acquired file,
require its hash instead:

```bash
bash scripts/prepare-needle-base-checkpoint.sh --source <checkpoint-path> --sha256 <expected-sha256>
```

The script accepts `--repository` and `--filename` to target a different
official artifact, and `scripts/prepare-needle-base-checkpoint.ps1` is the
equivalent Windows entry point.

### Needle3 engine (evaluation only)

Needle3 uses a different, mutually incompatible native engine, so it gets its
own pinned environment and a version-suffixed native directory. Pass the
pinned engine version to both setup steps:

```bash
bash scripts/bootstrap-wsl.sh --engine-version 3.0.2
bash scripts/acquire-needle-native.sh --engine-version 3.0.2
bash scripts/prepare-needle-base-checkpoint.sh --download \
  --repository Cactus-Compute/needle3 --filename needle3.cact \
  --output IntentEvalHarness/weights/base-v3/needle3.cact \
  --python-bin .venv-needle-3.0.2/bin/python
```

That creates `.venv-needle-3.0.2/` and
`IntentEvalHarness/native/<rid>/3.0.2/libneedle.so`, leaving the default
Needle2 environment and engine untouched. The checkpoint download is about
35 MB and is verified against its published LFS SHA-256. The 3.0.2 path is
inference-only: it installs no JAX/Flax/Optax training stack and rejects
`--cuda`. The Windows equivalents are `-EngineVersion 3.0.2` on
`scripts/bootstrap-needle.ps1` and `scripts/acquire-needle-native.ps1`.

## Dataset generation and JSONL export

Edit only the authored seed when a data change is justified:
`IntentEvalHarness/Dataset/needle-training-seed.json` (167 rows). Regenerate
all derived artifacts with:

```bash
pwsh -NoProfile -File scripts/generate-training-datasets.ps1
```

The standard training input is
`IntentEvalHarness/Dataset/training_set.300.jsonl` (345 rows). The
391-row `training_set.300.oversample_unclear.jsonl` is an explicit experiment
input, not a default. To export the 167-row seed directly:

```bash
dotnet run --project IntentEvalHarness -- \
  --export-training-jsonl Dataset/needle-training-seed.json \
  --output Dataset/training_set.seed.jsonl
```

The historical `generate_needle_training_seed_300.ps1` is only a warning
compatibility shim; use `generate-training-datasets.ps1`.

## Evaluation and the frozen baseline

The harness writes reports to
`artifacts/eval/run_<UTC timestamp>/`: `summary.json`, `results.csv`,
`needle_diagnostics.json`, and, when requested,
`baseline_comparison.json`. Base Needle evaluation is offline:

```bash
dotnet run --project IntentEvalHarness -- --providers needleBase
dotnet run --project IntentEvalHarness -- \
  --providers needleBase \
  --compare-baseline Baselines/openAi_frozen_20260823_full45
```

The frozen baseline contains 45 cases and records 93.33% intent accuracy and
71.11% parameter accuracy. It is immutable and must be compared offline; do
not regenerate it during validation. The default configuration has
`OpenAI:Enabled=false` and `OpenAI:AllowLiveCalls=false`. Do not enable live
OpenAI calls or add an API key for routine checks. The frozen baseline is the
approved evidence for comparison.

The `needleV3` provider evaluates the Needle3 base checkpoint once its engine
and weights are prepared. Point `Needle:V3WeightsPath` at the checkpoint
(default `weights/base-v3/needle3.cact`) and run it in its own invocation:

```bash
dotnet run --project IntentEvalHarness -- --providers needleV3
```

The Needle2 and Needle3 native engines cannot be loaded in the same process,
so the harness rejects a `--providers` selection that combines `needleV3` with
`needleBase` or a tuned Needle2 key. Run each engine separately and compare
the resulting `summary.json` reports offline.

## Smoke training and explicit full training

Smoke training is one GPU epoch and is still expensive relative to validation.
Request it explicitly only after CUDA setup, native acquisition, and a local
base checkpoint are ready:

```bash
run_name="smoke_$(date -u +%Y%m%d_%H%M%S)"
bash scripts/needle-finetune.sh --smoke-test --run-name "$run_name"
```

The current wrapper defaults for an explicitly requested full run are 30
epochs, LoRA rank 16, alpha 32, learning rate 0.0001, batch size 4, max length
1024, validation split 0.1, and `training_set.300.jsonl`. They are not a quality
recommendation: the clean-room run of this exact configuration regressed to
42.22% intent accuracy, 51.11% parameter accuracy, and 15 fallbacks, versus
62.22%, 66.67%, and 9 fallbacks for base Needle:

```bash
run_name="$(date -u +%Y%m%d_%H%M%S)"
bash scripts/needle-finetune.sh --run-name "$run_name" \
  --dataset-path IntentEvalHarness/Dataset/training_set.300.jsonl \
  --epochs 30 --lora-rank 16 --lora-alpha 32 --learning-rate 0.0001 \
  --batch-size 4 --max-len 1024 --val-split 0.1
```

Each run creates local `weights/<run-name>/` output containing a manifest,
adapter, checkpoint copy, `.cact` artifact, log, and loss plot. Select a
specific artifact by its manifest values and hash; do not rely on automatic
discovery:

```bash
run_name="<run-name>"
manifest="IntentEvalHarness/weights/$run_name/manifest.json"
export Needle__TunedWeightsPath="$(python3 -c 'import json,sys; print("weights/" + sys.argv[2] + "/" + json.load(open(sys.argv[1]))["weightsFile"])' "$manifest" "$run_name")"
export Needle__TunedProviderKey="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["providerKey"])' "$manifest")"
export Needle__TunedWeightsDisplayName="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["displayName"])' "$manifest")"
export Needle__TunedWeightsSha256="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sha256"])' "$manifest")"
dotnet run --project IntentEvalHarness -- \
  --providers "needleBase,$Needle__TunedProviderKey" \
  --compare-baseline Baselines/openAi_frozen_20260823_full45
```

Record every attempted experiment, including failures and regressions, only in
[`docs/experiments/EXPERIMENT_LOG.md`](docs/experiments/EXPERIMENT_LOG.md).
Historical results are evidence, not current defaults. Do not automatically
promote, deploy, or execute actions from a tuned artifact. A release candidate
needs human review, per-intent and fallback analysis, and a separately created
unseen test set.

## Troubleshooting

- `pwsh` missing: install PowerShell 7 before running the generator.
- Python version or package pin failure: recreate `.venv-needle` with the
  matching bootstrap script; do not manually mix package versions.
- Native engine missing or hash mismatch: rerun acquisition after restoring the
  pinned environment; do not copy a binary from another machine.
- `needleV3` skipped with a warning: acquire the 3.0.2 engine and Needle3
  checkpoint first; the provider fails open so the rest of the run continues.
- `needleV3` rejected at startup: it cannot share a process with `needleBase`
  or a tuned Needle2 key. Run it in its own invocation.
- `nvidia-smi` unavailable or JAX backend is not `gpu`: training is blocked.
  Repair WSL GPU visibility and rerun `bootstrap-wsl.sh --cuda`.
- Base checkpoint missing or unverified: run
  `bash scripts/prepare-needle-base-checkpoint.sh --download`, or use
  `--source` with its expected SHA-256; the training wrappers intentionally
  fail closed.
- OpenAI errors during validation: check that OpenAI is disabled. Use the
  frozen baseline instead of paid live requests.
- A result differs from the historical log: record the new measured result,
  hardware, settings, and artifact hash; do not silently rewrite history.

## Further reading

- [`docs/WSL-SETUP.md`](docs/WSL-SETUP.md) — clean WSL setup sequence.
- [`docs/experiments/EXPERIMENT_LOG.md`](docs/experiments/EXPERIMENT_LOG.md) —
  append-only measured history and artifact policy.
- [`docs/RESPONSIBLE-AI.md`](docs/RESPONSIBLE-AI.md) — data, evaluation,
  safety, privacy, artifact, and third-party release constraints.
- [`docs/experiments/provenance/20260905_lowcap/`](docs/experiments/provenance/20260905_lowcap/) —
  retained best-run provenance without generated weights.
- [`docs/experiments/provenance/20260905_validation_full/`](docs/experiments/provenance/20260905_validation_full/) —
  sanitized clean-room validation evidence and checksums.

## License

Licensed under [Apache-2.0](LICENSE).
