#!/usr/bin/env bash

set -euo pipefail

readonly CACTUS_VERSION_DEFAULT="2.0.10"

usage() {
    cat <<'EOF'
Usage: bash scripts/acquire-needle-native.sh [--rid <linux-x64|linux-arm64>] [--engine-version <2.0.10|3.0.2>] [--force] [--dry-run]

Fetches the platform-native Needle engine through the verified, pinned
cactus-needle CLI. The engine is stored under IntentEvalHarness/native/<rid>
for the default engine version, or IntentEvalHarness/native/<rid>/<engine-version>
for a non-default pinned version (for example Needle3's 3.0.2 engine), so
both engine generations can be acquired locally without overwriting each other.
EOF
}

die() {
    printf 'ERROR: %s\n' "$1" >&2
    exit 1
}

rid="auto"
engine_version="$CACTUS_VERSION_DEFAULT"
force=0
dry_run=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --rid)
            [[ -n "${2:-}" ]] || die "Missing value for --rid."
            rid="$2"
            shift 2
            ;;
        --engine-version)
            [[ -n "${2:-}" ]] || die "Missing value for --engine-version."
            engine_version="$2"
            shift 2
            ;;
        --force) force=1; shift ;;
        --dry-run) dry_run=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) die "Unknown option '$1'. Use --help for usage." ;;
    esac
done

# Pinned wheel provenance per supported engine version; never accept an
# arbitrary caller-supplied URL/hash pair here.
case "$engine_version" in
    2.0.10)
        cactus_wheel_url="https://files.pythonhosted.org/packages/24/ac/84d720ba744e79f3fa5483f0ea6b71f14d329fae3623dd0f74236f19c938/cactus_needle-2.0.10-py3-none-any.whl"
        cactus_wheel_sha256="f263b8e74fde5e225bb19a1c2747a3c2164272e27f1a7b948ecea3f3e1d925d9"
        ;;
    3.0.2)
        cactus_wheel_url="https://files.pythonhosted.org/packages/f3/b0/7b2ac5951fc639aa113a8244530f7d236645212bc6144f55a099dd6c594f/cactus_needle-3.0.2-py3-none-any.whl"
        cactus_wheel_sha256="99200776c42b2af93325326f1030b49da6af3fa9d66e5d979b44f5e472e4e739"
        ;;
    *)
        die "Unsupported --engine-version '$engine_version'. Supported versions: 2.0.10, 3.0.2."
        ;;
esac

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
native_root="$repo_root/IntentEvalHarness/native"
if [[ "$engine_version" == "$CACTUS_VERSION_DEFAULT" ]]; then
    venv_root="$repo_root/.venv-needle"
else
    venv_root="$repo_root/.venv-needle-$engine_version"
fi
python_bin="$venv_root/bin/python"
bootstrap_manifest="$venv_root/.needle-bootstrap.json"

if [[ "$rid" == "auto" ]]; then
    case "$(uname -m)" in
        x86_64) rid="linux-x64" ;;
        aarch64|arm64) rid="linux-arm64" ;;
        *) die "Unsupported Linux architecture: $(uname -m)" ;;
    esac
fi
[[ "$rid" == "linux-x64" || "$rid" == "linux-arm64" ]] ||
    die "The Bash acquisition script supports linux-x64 and linux-arm64 only."

library_name="libneedle.so"
if [[ "$engine_version" == "$CACTUS_VERSION_DEFAULT" ]]; then
    destination_dir="$native_root/$rid"
else
    destination_dir="$native_root/$rid/$engine_version"
fi
destination="$destination_dir/$library_name"
provenance="$destination_dir/acquisition.json"

if [[ $dry_run -eq 1 ]]; then
    printf 'Would use RID: %s\n' "$rid"
    printf 'Would require verified cactus-needle %s from %s\n' "$engine_version" "$cactus_wheel_url"
    printf "Would run the official 'needle fetch --out' command and install %s\n" "$destination"
    exit 0
fi

if [[ -f "$destination" && $force -eq 0 ]]; then
    [[ -f "$provenance" ]] ||
        die "Existing native engine has no provenance manifest: $destination. Re-run with --force to reacquire it."
    expected_hash="$("$python_bin" - "$provenance" <<'PY'
import json
import sys
print(json.load(open(sys.argv[1], encoding="utf-8"))["sha256"])
PY
)"
    actual_hash="$(sha256sum "$destination" | awk '{print tolower($1)}')"
    [[ "$expected_hash" == "$actual_hash" ]] ||
        die "Existing native engine hash does not match its provenance manifest. Re-run with --force to reacquire it."
    printf 'Verified existing native engine: %s\n' "$destination"
    exit 0
fi

[[ -x "$python_bin" ]] ||
    die "Pinned Python environment not found at $python_bin. Run bash scripts/bootstrap-wsl.sh first."
[[ -f "$bootstrap_manifest" ]] ||
    die "Cannot verify package provenance: $bootstrap_manifest is missing. Recreate the environment with scripts/bootstrap-wsl.sh."

"$python_bin" - "$bootstrap_manifest" "$engine_version" "$cactus_wheel_url" "$cactus_wheel_sha256" <<'PY'
import json
import sys

actual = json.load(open(sys.argv[1], encoding="utf-8")).get("cactusNeedle", {})
expected = {"version": sys.argv[2], "wheelUrl": sys.argv[3], "wheelSha256": sys.argv[4]}
if actual != expected:
    raise SystemExit("Bootstrap provenance does not match the pinned official package.")
PY

installed_version="$("$python_bin" -c "import importlib.metadata as m; print(m.version('cactus-needle'))")"
[[ "$installed_version" == "$engine_version" ]] ||
    die "Expected cactus-needle $engine_version, found '$installed_version'. Recreate the environment with scripts/bootstrap-wsl.sh."

stage="$native_root/.acquire-$$"
cleanup() { rm -rf -- "$stage"; }
trap cleanup EXIT
mkdir -p -- "$stage"

if [[ -x "$venv_root/bin/needle" ]]; then
    "$venv_root/bin/needle" fetch --out "$stage"
else
    "$python_bin" -m needle.cli fetch --out "$stage"
fi

fetched="$(find "$stage" -type f -name "$library_name" -print -quit)"
[[ -n "$fetched" ]] ||
    die "Official cactus-needle fetch did not produce $library_name for $rid. Verify that this RID is supported by the current official package."

mkdir -p -- "$destination_dir"
cp -- "$fetched" "$destination"
hash="$(sha256sum "$destination" | awk '{print tolower($1)}')"
"$python_bin" - "$provenance" "$rid" "$library_name" "$hash" "$engine_version" "$cactus_wheel_url" "$cactus_wheel_sha256" <<'PY'
import json
import pathlib
import sys

pathlib.Path(sys.argv[1]).write_text(json.dumps({
    "schemaVersion": 1,
    "rid": sys.argv[2],
    "library": sys.argv[3],
    "sha256": sys.argv[4],
    "source": {
        "command": "needle fetch --out",
        "package": "cactus-needle",
        "version": sys.argv[5],
        "wheelUrl": sys.argv[6],
        "wheelSha256": sys.argv[7],
    },
    "binaryChecksumPublished": False,
    "verification": "No published native-binary checksum was available in the documented official source. Provenance is verified through the pinned official PyPI wheel hash; the local binary SHA-256 is recorded for subsequent tamper detection.",
}, indent=2) + "\n", encoding="utf-8")
PY

printf 'Native engine acquired: %s\n' "$destination"
