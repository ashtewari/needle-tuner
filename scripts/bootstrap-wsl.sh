#!/usr/bin/env bash

set -euo pipefail

readonly CACTUS_VERSION_DEFAULT="2.0.10"

usage() {
    cat <<'EOF'
Usage: bash scripts/bootstrap-wsl.sh [--cuda] [--venv <path>] [--engine-version <2.0.10|3.0.2>] [--prepare-base-checkpoint] [--help]

Creates a Python 3.12 virtual environment and installs the pinned Needle stack.
--cuda installs the pinned JAX CUDA 12 extra only after confirming that WSL can
see an NVIDIA GPU. It never installs or changes a host/WSL GPU driver.
--engine-version selects the pinned cactus-needle release: 2.0.10 (Needle2,
default, includes the JAX/flax/optax fine-tuning stack) or 3.0.2 (Needle3,
inference-only in this repo; skips the training stack and --cuda). Non-default
versions default to a version-suffixed venv (.venv-needle-<version>) so both
can coexist locally.
--prepare-base-checkpoint explicitly downloads and verifies the official ~90 MB
Needle2 base checkpoint after environment setup.
EOF
}

die() {
    printf 'ERROR: %s\n' "$1" >&2
    exit 1
}

warn() {
    printf 'WARN: %s\n' "$1" >&2
}

cuda=0
venv_path=""
engine_version="$CACTUS_VERSION_DEFAULT"
prepare_base_checkpoint=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --cuda) cuda=1; shift ;;
        --prepare-base-checkpoint) prepare_base_checkpoint=1; shift ;;
        --engine-version)
            [[ -n "${2:-}" ]] || die "Missing value for --engine-version."
            engine_version="$2"
            shift 2
            ;;
        --venv)
            [[ -n "${2:-}" ]] || die "Missing value for --venv."
            venv_path="$2"
            shift 2
            ;;
        -h|--help) usage; exit 0 ;;
        *) die "Unknown option '$1'. Use --help for usage." ;;
    esac
done

# Pinned wheel provenance per supported engine version; never accept an
# arbitrary caller-supplied URL/hash pair here. 3.0.2 (Needle3) is
# inference-only in this repo, so it skips the JAX/flax/optax training stack.
case "$engine_version" in
    2.0.10)
        cactus_wheel_url="https://files.pythonhosted.org/packages/24/ac/84d720ba744e79f3fa5483f0ea6b71f14d329fae3623dd0f74236f19c938/cactus_needle-2.0.10-py3-none-any.whl"
        cactus_wheel_sha256="f263b8e74fde5e225bb19a1c2747a3c2164272e27f1a7b948ecea3f3e1d925d9"
        requirements_cactus="requirements-cactus-needle.txt"
        requirements_needle="requirements-needle.txt"
        ;;
    3.0.2)
        cactus_wheel_url="https://files.pythonhosted.org/packages/f3/b0/7b2ac5951fc639aa113a8244530f7d236645212bc6144f55a099dd6c594f/cactus_needle-3.0.2-py3-none-any.whl"
        cactus_wheel_sha256="99200776c42b2af93325326f1030b49da6af3fa9d66e5d979b44f5e472e4e739"
        requirements_cactus="requirements-cactus-needle-v3.txt"
        requirements_needle="requirements-needle-v3.txt"
        ;;
    *)
        die "Unsupported --engine-version '$engine_version'. Supported versions: 2.0.10, 3.0.2."
        ;;
esac
is_inference_only=0
[[ "$engine_version" == "$CACTUS_VERSION_DEFAULT" ]] || is_inference_only=1

if [[ $cuda -eq 1 && $is_inference_only -eq 1 ]]; then
    die "--cuda is only supported with the default engine version ($CACTUS_VERSION_DEFAULT); $engine_version is inference-only in this repo."
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
python_bin="${PYTHON_BIN:-python3.12}"
if [[ -z "$venv_path" ]]; then
    if [[ $is_inference_only -eq 1 ]]; then
        venv_path="$repo_root/.venv-needle-$engine_version"
    else
        venv_path="$repo_root/.venv-needle"
    fi
fi

if ! grep -qiE '(microsoft|wsl)' /proc/sys/kernel/osrelease 2>/dev/null; then
    die "This is the WSL bootstrap. Run it from WSL 2; it does not configure WSL or system drivers."
fi

command -v "$python_bin" >/dev/null 2>&1 ||
    die "Python 3.12 was not found as '$python_bin'. Install python3.12 and python3.12-venv in WSL, then retry."

"$python_bin" -c 'import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 12) else 1)' ||
    die "'$python_bin' is not Python 3.12."

if [[ $cuda -eq 1 ]]; then
    command -v nvidia-smi >/dev/null 2>&1 ||
        die "CUDA was requested but nvidia-smi is unavailable in WSL. Install/update the NVIDIA WSL driver on the Windows host, then retry; this script will not install drivers."
    nvidia-smi >/dev/null ||
        die "CUDA was requested but WSL cannot query an NVIDIA GPU. Repair host WSL GPU support, then retry."
else
    if ! command -v nvidia-smi >/dev/null 2>&1 || ! nvidia-smi >/dev/null 2>&1; then
        warn "No NVIDIA GPU is visible in WSL. Installing the CPU-compatible pinned stack; rerun with --cuda after host GPU support is available."
    fi
fi

if [[ ! -x "$venv_path/bin/python" ]]; then
    "$python_bin" -m venv "$venv_path"
fi

venv_python="$venv_path/bin/python"
"$venv_python" -m pip install --upgrade pip
"$venv_python" -m pip install --require-hashes --no-deps \
    --requirement "$script_dir/$requirements_cactus"
"$venv_python" -m pip install --requirement "$script_dir/$requirements_needle"

if [[ $cuda -eq 1 ]]; then
    "$venv_python" -m pip install "jax[cuda12]==0.11.1"
fi

"$venv_python" -m pip check
if [[ $is_inference_only -eq 1 ]]; then
    "$venv_python" - "$engine_version" <<'PY'
import importlib.metadata as metadata
import sys

expected = {"cactus-needle": sys.argv[1]}
actual = {name: metadata.version(name) for name in expected}
if actual != expected:
    raise SystemExit(f"Pin verification failed: {actual}")
print("Pinned packages verified:", ", ".join(f"{name}={version}" for name, version in actual.items()))
PY
else
    "$venv_python" - "$engine_version" <<'PY'
import importlib.metadata as metadata
import sys

expected = {
    "cactus-needle": sys.argv[1],
    "jax": "0.11.1",
    "jaxlib": "0.11.1",
    "flax": "0.12.9",
    "optax": "0.2.8",
}
actual = {name: metadata.version(name) for name in expected}
if actual != expected:
    raise SystemExit(f"Pin verification failed: {actual}")
print("Pinned packages verified:", ", ".join(f"{name}={version}" for name, version in actual.items()))
PY
fi

"$venv_python" - "$venv_path/.needle-bootstrap.json" "$engine_version" "$cactus_wheel_url" "$cactus_wheel_sha256" <<'PY'
import json
import pathlib
import sys

pathlib.Path(sys.argv[1]).write_text(json.dumps({
    "cactusNeedle": {
        "version": sys.argv[2],
        "wheelUrl": sys.argv[3],
        "wheelSha256": sys.argv[4],
    }
}, indent=2) + "\n", encoding="utf-8")
PY

if [[ $is_inference_only -eq 0 ]]; then
    "$venv_python" - <<'PY'
import jax
print("JAX version:", jax.__version__)
print("Backend:", jax.default_backend())
print("Devices:", jax.devices())
PY
fi

if [[ $prepare_base_checkpoint -eq 1 ]]; then
    bash "$script_dir/prepare-needle-base-checkpoint.sh" --download --python-bin "$venv_python"
fi

printf 'Needle environment ready: %s\n' "$venv_path"
