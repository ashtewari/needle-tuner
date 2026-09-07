#!/usr/bin/env bash

set -euo pipefail

dataset_path="IntentEvalHarness/Dataset/training_set.300.jsonl"
run_name=""
epochs=30
lora_rank=16
lora_alpha=32
learning_rate="0.0001"
batch_size=4
max_len=1024
val_split="0.1"
base_checkpoint="IntentEvalHarness/weights/base/needle2.pkl"
native_library=""
python_bin=""
needle_bin=""
smoke_test=0
dry_run=0
skip_install=0

usage() {
    cat <<'EOF'
Usage: bash scripts/needle-finetune.sh [options]

Defaults: 30 epochs, rank 16, alpha 32, learning rate 0.0001, batch size 4,
max length 1024, validation split 0.1, and training_set.300.jsonl.

Options:
  --dataset-path <path>       Training JSONL, relative to the repository root.
  --run-name <name>           Run directory name; default is a UTC timestamp.
  --epochs <count>            Epochs; default: 30.
  --lora-rank <count>         LoRA rank; default: 16.
  --lora-alpha <value>        LoRA alpha; default: 32.
  --learning-rate <value>     Learning rate; default: 0.0001.
  --batch-size <count>        Batch size; default: 4.
  --max-len <count>           Must be at least 1024; default: 1024.
  --val-split <fraction>      Validation split; default: 0.1.
  --base-checkpoint <path>    Existing local Needle base checkpoint.
  --native-library <path>     Matching local libneedle.so; default is RID-specific.
  --python-bin <path>         Pinned environment Python; default: .venv-needle/bin/python.
  --needle-bin <path>         Needle CLI; default: .venv-needle/bin/needle.
  --smoke-test                Bound the run to one epoch (still requires all prerequisites).
  --dry-run                   Print resolved commands without creating files or training.
  --skip-install              Compatibility no-op; bootstrap scripts own installation.
  -h, --help                  Show this help.

Run bash scripts/bootstrap-wsl.sh first. This wrapper never installs packages,
downloads a base checkpoint, or falls back to CPU when GPU/native prerequisites fail.
EOF
}

die() { printf 'ERROR: %s\n' "$1" >&2; exit 1; }
warn() { printf 'WARN: %s\n' "$1" >&2; }
require_value() { [[ -n "${2:-}" ]] || die "Missing value for $1."; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dataset-path|--run-name|--epochs|--lora-rank|--lora-alpha|--learning-rate|--batch-size|--max-len|--val-split|--base-checkpoint|--native-library|--python-bin|--needle-bin)
            require_value "$1" "${2:-}"
            case "$1" in
                --dataset-path) dataset_path="$2" ;; --run-name) run_name="$2" ;;
                --epochs) epochs="$2" ;; --lora-rank) lora_rank="$2" ;; --lora-alpha) lora_alpha="$2" ;;
                --learning-rate) learning_rate="$2" ;; --batch-size) batch_size="$2" ;; --max-len) max_len="$2" ;;
                --val-split) val_split="$2" ;; --base-checkpoint) base_checkpoint="$2" ;;
                --native-library) native_library="$2" ;; --python-bin) python_bin="$2" ;; --needle-bin) needle_bin="$2" ;;
            esac
            shift 2 ;;
        --smoke-test) smoke_test=1; shift ;;
        --dry-run) dry_run=1; shift ;;
        --skip-install) skip_install=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) die "Unknown option '$1'. Use --help for usage." ;;
    esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
project_root="$repo_root/IntentEvalHarness"
resolve_path() { [[ "$2" = /* ]] && realpath -m "$2" || realpath -m "$1/$2"; }
relative_path() { realpath -m --relative-to="$1" "$2"; }

case "$(uname -m)" in
    x86_64) rid="linux-x64" ;;
    aarch64|arm64) rid="linux-arm64" ;;
    *) die "Unsupported Linux architecture: $(uname -m)" ;;
esac

dataset_full_path="$(resolve_path "$repo_root" "$dataset_path")"
base_checkpoint_path="$(resolve_path "$repo_root" "$base_checkpoint")"
base_manifest_path="$(dirname -- "$base_checkpoint_path")/base-checkpoint.manifest.json"
python_bin="${python_bin:-$repo_root/.venv-needle/bin/python}"
native_library="${native_library:-$project_root/native/$rid/libneedle.so}"
native_library="$(resolve_path "$repo_root" "$native_library")"
loss_plot_script="$script_dir/needle-loss-plot.py"

if [[ $smoke_test -eq 1 ]]; then
    epochs=1
    [[ -n "$run_name" ]] || run_name="smoke_$(date -u +%Y%m%d_%H%M%S)"
fi
[[ "$max_len" =~ ^[0-9]+$ ]] && (( max_len >= 1024 )) ||
    die "--max-len must be at least 1024 for a quality-comparable run."
[[ "$epochs" =~ ^[1-9][0-9]*$ ]] || die "--epochs must be a positive integer."
[[ -z "$run_name" ]] && run_name="$(date -u +%Y%m%d_%H%M%S)"
[[ "$run_name" =~ ^[A-Za-z0-9._-]+$ ]] ||
    die "--run-name may contain only letters, digits, '.', '_', and '-'."

run_root="$project_root/weights/$run_name"
checkpoints_root="$run_root/checkpoints"
adapter_path="$checkpoints_root/needle_lora.pkl"
run_base_checkpoint="$checkpoints_root/needle2.pkl"
artifact_file_name="needle_tuned.cact"
artifact_path="$run_root/$artifact_file_name"
manifest_path="$run_root/manifest.json"
finetune_log_path="$run_root/finetune.log"
loss_plot_path="$run_root/loss_curve.svg"

if [[ $dry_run -eq 1 ]]; then
    printf 'Mode: %s\n' "$([[ $smoke_test -eq 1 ]] && echo smoke-test || echo quality-run)"
    printf 'Dataset: %s\nBase checkpoint: %s\nNative library: %s\nPython: %s\n' "$dataset_full_path" "$base_checkpoint_path" "$native_library" "$python_bin"
    printf 'Run: %s (epochs=%s, rank=%s, alpha=%s, lr=%s, batch=%s, max-len=%s, val-split=%s)\n' "$run_name" "$epochs" "$lora_rank" "$lora_alpha" "$learning_rate" "$batch_size" "$max_len" "$val_split"
    exit 0
fi

[[ -f "$dataset_full_path" ]] || die "Training dataset was not found: $dataset_full_path"
[[ -f "$base_checkpoint_path" ]] || die "Base checkpoint was not found: $base_checkpoint_path. Acquire it explicitly before training; this wrapper will not download it."
[[ -f "$native_library" ]] || die "Native Needle engine was not found: $native_library. Run scripts/acquire-needle-native.sh first."
[[ -x "$python_bin" ]] || die "Pinned Python environment was not found: $python_bin. Run scripts/bootstrap-wsl.sh first."
[[ -f "$loss_plot_script" ]] || die "Loss plot utility was not found: $loss_plot_script"
[[ -f "$base_manifest_path" ]] || die "Base checkpoint provenance was not found: $base_manifest_path. Run scripts/prepare-needle-base-checkpoint.sh."
recorded_base_hash="$("$python_bin" - "$base_manifest_path" <<'PY'
import json
import sys
print(json.load(open(sys.argv[1], encoding="utf-8"))["sha256"])
PY
)"
actual_base_hash="$(sha256sum "$base_checkpoint_path" | awk '{print tolower($1)}')"
[[ "$recorded_base_hash" == "$actual_base_hash" ]] ||
    die "Base checkpoint hash does not match $base_manifest_path. Re-run scripts/prepare-needle-base-checkpoint.sh."
if [[ $skip_install -eq 1 ]]; then
    warn "--skip-install is a compatibility no-op; this wrapper never installs packages."
fi
command -v nvidia-smi >/dev/null 2>&1 || die "nvidia-smi is unavailable. GPU training requires a GPU exposed to WSL."
nvidia-smi >/dev/null || die "nvidia-smi could not query a GPU. Repair WSL GPU support before training."
backend="$("$python_bin" -c 'import jax; print(jax.default_backend())')" ||
    die "Could not initialize JAX from the pinned environment."
[[ "$backend" == "gpu" ]] || die "JAX selected backend '$backend', not GPU. Recreate the environment with bash scripts/bootstrap-wsl.sh --cuda."

if [[ -n "$needle_bin" ]]; then
    [[ -x "$needle_bin" ]] || die "Needle CLI was not executable: $needle_bin"
    needle_cmd=("$needle_bin")
elif [[ -x "$repo_root/.venv-needle/bin/needle" ]]; then
    needle_cmd=("$repo_root/.venv-needle/bin/needle")
else
    needle_cmd=("$python_bin" -m needle.cli)
fi

mkdir -p -- "$checkpoints_root"
if [[ "$(realpath -m "$base_checkpoint_path")" != "$(realpath -m "$run_base_checkpoint")" ]]; then
    cp -- "$base_checkpoint_path" "$run_base_checkpoint"
fi

export XLA_PYTHON_CLIENT_PREALLOCATE="${XLA_PYTHON_CLIENT_PREALLOCATE:-false}"
dataset_relative_path="$(relative_path "$repo_root" "$dataset_full_path")"
base_relative_path="$(relative_path "$repo_root" "$base_checkpoint_path")"
finetune_args=(finetune "$dataset_full_path" --epochs "$epochs" --lora-rank "$lora_rank" --lora-alpha "$lora_alpha" --lr "$learning_rate" --batch-size "$batch_size" --max-len "$max_len" --val-split "$val_split" --out checkpoints/needle_lora.pkl)
build_args=(build checkpoints/needle2.pkl --lora checkpoints/needle_lora.pkl --out "$artifact_file_name")

pushd "$run_root" >/dev/null
trap 'popd >/dev/null' EXIT
printf "Starting Needle %s run '%s'\n" "$([[ $smoke_test -eq 1 ]] && echo smoke || echo fine-tune)" "$run_name"
"${needle_cmd[@]}" "${finetune_args[@]}" 2>&1 | tee "$finetune_log_path"
[[ -f "$adapter_path" ]] || die "Needle fine-tune completed without adapter: $adapter_path"
"$python_bin" "$loss_plot_script" "$finetune_log_path" "$loss_plot_path"
[[ -f "$loss_plot_path" ]] || die "Loss plot was not created: $loss_plot_path"
"${needle_cmd[@]}" "${build_args[@]}"
[[ -f "$artifact_path" ]] || die "Needle build completed without artifact: $artifact_path"

artifact_hash="$(sha256sum "$artifact_path" | awk '{print tolower($1)}')"
provider_key="needleTuned$(printf '%s' "$run_name" | tr -cd '[:alnum:]')"
cat > "$manifest_path" <<EOF
{
  "providerKey": "$provider_key",
  "displayName": "Needle Tuned ($run_name)",
  "weightsFile": "$artifact_file_name",
  "sha256": "$artifact_hash",
  "createdUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "datasetPath": "$dataset_relative_path",
  "baseCheckpointPath": "$base_relative_path",
  "adapterPath": "$(relative_path "$repo_root" "$adapter_path")",
  "command": { "script": "scripts/needle-finetune.sh", "runName": "$run_name", "epochs": $epochs, "loraRank": $lora_rank, "loraAlpha": $lora_alpha, "learningRate": $learning_rate, "batchSize": $batch_size, "maxLen": $max_len, "valSplit": $val_split, "smokeTest": $([[ $smoke_test -eq 1 ]] && echo true || echo false) }
}
EOF

printf 'Tuned artifact: %s\nManifest: %s\nProvider key: %s\n' "$(relative_path "$repo_root" "$artifact_path")" "$(relative_path "$repo_root" "$manifest_path")" "$provider_key"
