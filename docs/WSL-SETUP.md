# Clean WSL setup

This guide starts from a clean clone. It prepares the repository without
training. GPU setup and native acquisition are separate so a validation-only
machine does not accidentally download large packages or start a run.

## 1. Install host prerequisites

Use WSL 2 with a supported Linux distribution. Install or make available:

- .NET SDK 9.0.310
- PowerShell 7 (`pwsh`)
- Python 3.12 and `python3.12-venv`
- Bash, `curl`, `sha256sum`, and standard build/runtime utilities

Use the official Microsoft/.NET, PowerShell, Python, and NVIDIA WSL
installation instructions for those host prerequisites. This repository's
scripts do not install system packages, GPU drivers, or WSL itself.

From the clone:

```bash
cd /path/to/needle
dotnet --version
pwsh --version
python3.12 --version
```

The .NET command must resolve the SDK pinned in `global.json`; Python must
report 3.12.x.

## 2. Run non-training validation

These commands are safe on a CPU-only machine:

```bash
pwsh -NoProfile -File scripts/generate-training-datasets.ps1
dotnet test Needle.sln --nologo
dotnet run --project IntentEvalHarness -- --help
bash -n scripts/bootstrap-wsl.sh scripts/acquire-needle-native.sh scripts/needle-finetune.sh
bash scripts/bootstrap-wsl.sh --help
bash scripts/acquire-needle-native.sh --dry-run
bash scripts/needle-finetune.sh --dry-run
bash scripts/needle-finetune.sh --smoke-test --dry-run
```

The dry runs do not create environments, native libraries, weights, or
checkpoints. The dataset generator creates or verifies only the authored
dataset derivatives and their integrity hashes.

## 3. Prepare the Python environment

For validation-only work, omit `--cuda`:

```bash
bash scripts/bootstrap-wsl.sh
```

This creates `.venv-needle`, installs the pinned CPU-compatible stack, and
prints JAX diagnostics. It may warn that no GPU is visible. It does not make
training possible: the fine-tune wrapper requires a GPU backend.

For native acquisition or training, first confirm that the Windows NVIDIA
driver exposes a GPU inside WSL:

```bash
nvidia-smi
bash scripts/bootstrap-wsl.sh --cuda
```

The CUDA branch installs the pinned JAX CUDA 12 extra after the visibility
check. It never installs or updates a host driver.

## 4. Acquire the native engine (GPU path)

After the CUDA bootstrap succeeds:

```bash
bash scripts/acquire-needle-native.sh
```

The script auto-selects `linux-x64` or `linux-arm64`, verifies the pinned
`cactus-needle` 2.0.10 wheel provenance, runs the official fetch command, and
writes `IntentEvalHarness/native/<rid>/acquisition.json`. The binary is ignored
by Git. Use `--rid linux-x64` or `--rid linux-arm64` to override detection.

## 5. Prepare the local base checkpoint

Explicitly acquire the compatible official base checkpoint:

```bash
bash scripts/prepare-needle-base-checkpoint.sh --download
```

The command resolves the immutable revision of
`Cactus-Compute/needle2/checkpoints/needle2.pkl`, verifies the official
Hugging Face LFS SHA-256, and writes the ignored local checkpoint plus
`IntentEvalHarness/weights/base/base-checkpoint.manifest.json`. The checkpoint
is about 90 MB; it is never committed or hosted by this repository.

For a locally pre-acquired file, import it only with a known SHA-256:

```bash
bash scripts/prepare-needle-base-checkpoint.sh \
  --source <checkpoint-path> --sha256 <expected-sha256>
```

The wrapper verifies the provenance manifest and copied checkpoint hash before
training, then copies it into the run's ignored `checkpoints/` directory.

## 6. Smoke or full training (explicit only)

Smoke training is one epoch and requires the same GPU, native engine, Python
environment, and base checkpoint as a full run:

```bash
run_name="smoke_$(date -u +%Y%m%d_%H%M%S)"
bash scripts/needle-finetune.sh --smoke-test --run-name "$run_name"
```

Do not run full training unless it is explicitly requested. The current
wrapper defaults are 30 epochs, rank 16, alpha 32, learning rate 0.0001, batch
size 4, max length 1024, validation split 0.1, and the 345-row standard JSONL.
They are not a quality recommendation: the recorded clean-room run of this
configuration performed worse than base Needle and must not be promoted:

```bash
run_name="$(date -u +%Y%m%d_%H%M%S)"
bash scripts/needle-finetune.sh --run-name "$run_name" \
  --dataset-path IntentEvalHarness/Dataset/training_set.300.jsonl \
  --epochs 30 --lora-rank 16 --lora-alpha 32 --learning-rate 0.0001 \
  --batch-size 4 --max-len 1024 --val-split 0.1
```

Outputs remain under `IntentEvalHarness/weights/<run-name>/` and are ignored.
Do not add those files to source control.

## 7. Evaluate a selected run offline

Read the generated `manifest.json`, verify the `.cact` SHA-256, and export the
manifest's exact provider key, display name, path, and hash before evaluation.
The complete Bash example is in the root README. Then compare against the
immutable 45-case baseline:

```bash
dotnet run --project IntentEvalHarness -- \
  --providers "needleBase,<manifest-provider-key>" \
  --compare-baseline Baselines/openAi_frozen_20260823_full45
```

Reports are written to `artifacts/eval/run_<UTC timestamp>/`. This path is
ignored and contains local runtime evidence only.

The 45-case set is frozen and excluded from training, but its results have
already guided experiment selection. Treat it as a regression set. Use a
separately created, unseen test set before making generalization, deployment,
or production-quality claims.

The sanitized release evidence for the recorded clean-room run, including the
27m31s training duration, exact 45-case tuned/base metrics, and post-run checks,
is retained under
[`docs/experiments/provenance/20260905_validation_full/`](experiments/provenance/20260905_validation_full/).

## 8. Offline OpenAI policy

`IntentEvalHarness/appsettings.json` disables OpenAI and live calls by default.
Validation and baseline comparison must not make paid requests. Do not set an
API key or enable `OpenAI:AllowLiveCalls` for the standard workflow. Use the
committed frozen baseline for OpenAI comparison and the local Needle providers
for offline regression checks.

## 9. Cleanup and provenance

It is safe to remove `.venv-needle`, local native files, `weights/` run
directories, and `artifacts/eval/` when no longer needed; they are reproducible
local outputs. Keep authored datasets, manifests, tests, and documentation.
Record completed, failed, or regressed experiments in the canonical
[`docs/experiments/EXPERIMENT_LOG.md`](experiments/EXPERIMENT_LOG.md).
