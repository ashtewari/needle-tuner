#!/usr/bin/env bash

set -euo pipefail

readonly HF_REPOSITORY_DEFAULT="Cactus-Compute/needle2"
readonly HF_FILENAME_DEFAULT="checkpoints/needle2.pkl"

usage() {
    cat <<'EOF'
Usage:
  bash scripts/prepare-needle-base-checkpoint.sh --download [options]
  bash scripts/prepare-needle-base-checkpoint.sh --source <path> --sha256 <hash> [options]

Prepare the ignored local Needle base checkpoint required by the fine-tune
wrappers. --download resolves immutable metadata from the official Hugging
Face repository (Cactus-Compute/needle2 by default), verifies its published
LFS SHA-256, and records the resolved revision. --source imports a
user-supplied file only when its expected SHA-256 is supplied and matches.

Options:
  --download                Download the official checkpoint (explicit; about 90 MB).
  --source <path>           Import a pre-acquired checkpoint.
  --sha256 <hash>           Required expected SHA-256 for --source.
  --repository <owner/name> Hugging Face repository; default: Cactus-Compute/needle2.
  --filename <path>         Repository-relative checkpoint path; default: checkpoints/needle2.pkl.
  --output <path>           Destination; default: IntentEvalHarness/weights/base/needle2.pkl.
  --python-bin <path>       Python with huggingface_hub; default: .venv-needle/bin/python.
  --force                   Replace an existing checkpoint after verification.
  --dry-run                 Print the resolved action without downloading or writing.
  -h, --help                Show this help.
EOF
}

die() { printf 'ERROR: %s\n' "$1" >&2; exit 1; }
require_value() { [[ -n "${2:-}" ]] || die "Missing value for $1."; }
is_sha256() { [[ "$1" =~ ^[[:xdigit:]]{64}$ ]]; }

mode=""
source_path=""
expected_hash=""
output_path=""
python_bin=""
hf_repository=""
hf_filename=""
force=0
dry_run=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --download)
            [[ -z "$mode" ]] || die "Choose exactly one of --download or --source."
            mode="download"; shift ;;
        --source)
            require_value "$1" "${2:-}"
            [[ -z "$mode" ]] || die "Choose exactly one of --download or --source."
            mode="source"; source_path="$2"; shift 2 ;;
        --sha256) require_value "$1" "${2:-}"; expected_hash="${2,,}"; shift 2 ;;
        --repository) require_value "$1" "${2:-}"; hf_repository="$2"; shift 2 ;;
        --filename) require_value "$1" "${2:-}"; hf_filename="$2"; shift 2 ;;
        --output) require_value "$1" "${2:-}"; output_path="$2"; shift 2 ;;
        --python-bin) require_value "$1" "${2:-}"; python_bin="$2"; shift 2 ;;
        --force) force=1; shift ;;
        --dry-run) dry_run=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) die "Unknown option '$1'. Use --help for usage." ;;
    esac
done

hf_repository="${hf_repository:-$HF_REPOSITORY_DEFAULT}"
hf_filename="${hf_filename:-$HF_FILENAME_DEFAULT}"

[[ -n "$mode" ]] || die "Choose --download or --source <path>. Use --help for usage."
if [[ "$mode" == "source" ]]; then
    is_sha256 "$expected_hash" ||
        die "--source requires a 64-character SHA-256 supplied with --sha256."
elif [[ -n "$expected_hash" ]]; then
    die "--sha256 is only used with --source; official downloads verify published metadata."
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
resolve_path() { [[ "$2" = /* ]] && realpath -m "$2" || realpath -m "$1/$2"; }

output_path="$(resolve_path "$repo_root" "${output_path:-IntentEvalHarness/weights/base/needle2.pkl}")"
output_dir="$(dirname -- "$output_path")"
manifest_path="$output_dir/base-checkpoint.manifest.json"
python_bin="${python_bin:-$repo_root/.venv-needle/bin/python}"

if [[ $dry_run -eq 1 ]]; then
    if [[ "$mode" == "download" ]]; then
        printf 'Would download %s/%s using %s, resolve an immutable revision, verify the published LFS SHA-256, and write %s\n' "$hf_repository" "$hf_filename" "$python_bin" "$output_path"
    else
        printf 'Would verify and import %s to %s with SHA-256 %s\n' "$(resolve_path "$repo_root" "$source_path")" "$output_path" "$expected_hash"
    fi
    exit 0
fi

if [[ -f "$output_path" && $force -eq 0 ]]; then
    [[ -f "$manifest_path" ]] ||
        die "Existing checkpoint lacks provenance: $output_path. Re-run with --force to replace it."
    command -v python3 >/dev/null 2>&1 ||
        die "python3 is required to verify existing checkpoint provenance."
    recorded_hash="$(python3 - "$manifest_path" <<'PY'
import json
import sys

try:
    manifest = json.load(open(sys.argv[1], encoding="utf-8"))
    value = manifest["sha256"]
except (OSError, ValueError, KeyError, TypeError) as error:
    raise SystemExit(f"Cannot read checkpoint provenance manifest: {error}")

if not isinstance(value, str):
    raise SystemExit("Checkpoint provenance manifest SHA-256 is not a string.")
print(value.lower())
PY
)" || die "Existing checkpoint provenance manifest is invalid: $manifest_path. Re-run with --force to replace it."
    is_sha256 "$recorded_hash" ||
        die "Existing checkpoint provenance manifest has an invalid SHA-256: $manifest_path. Re-run with --force to replace it."
    actual_hash="$(sha256sum "$output_path" | awk '{print tolower($1)}')"
    [[ "$recorded_hash" == "$actual_hash" ]] ||
        die "Existing checkpoint hash does not match $manifest_path. Re-run with --force to replace it."
    printf 'Verified existing base checkpoint: %s\n' "$output_path"
    exit 0
fi

mkdir -p -- "$output_dir"
stage="$output_dir/.prepare-base-$$"
cleanup() { rm -rf -- "$stage"; }
trap cleanup EXIT
mkdir -p -- "$stage"

if [[ "$mode" == "download" ]]; then
    [[ -x "$python_bin" ]] ||
        die "Pinned Python environment was not found: $python_bin. Run bash scripts/bootstrap-wsl.sh first."

    metadata_path="$stage/official-metadata.json"
    downloaded=0
    for attempt in 1 2 3; do
        if "$python_bin" - "$stage" "$metadata_path" "$hf_repository" "$hf_filename" <<'PY'
import hashlib
import json
import pathlib
import re
import shutil
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
PY
        then
            downloaded=1
            break
        fi
        if [[ $attempt -lt 3 ]]; then
            delay=$((2 ** attempt))
            printf 'WARN: official checkpoint metadata/download attempt %s failed; retrying in %ss.\n' "$attempt" "$delay" >&2
            sleep "$delay"
        fi
    done
    [[ $downloaded -eq 1 ]] ||
        die "Official checkpoint acquisition failed after three attempts. Check network access to https://huggingface.co/$hf_repository and retry."

    candidate="$stage/$hf_filename"
    [[ -f "$candidate" ]] || die "Official download completed without $hf_filename."
    cp -- "$candidate" "$output_path"
    "$python_bin" - "$metadata_path" "$manifest_path" "$output_path" <<'PY'
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
PY
else
    source_path="$(resolve_path "$repo_root" "$source_path")"
    [[ -f "$source_path" ]] || die "User-supplied checkpoint was not found: $source_path"
    actual_hash="$(sha256sum "$source_path" | awk '{print tolower($1)}')"
    [[ "$actual_hash" == "$expected_hash" ]] ||
        die "User-supplied checkpoint SHA-256 mismatch: expected $expected_hash, got $actual_hash."
    cp -- "$source_path" "$output_path"
    copied_hash="$(sha256sum "$output_path" | awk '{print tolower($1)}')"
    [[ "$copied_hash" == "$expected_hash" ]] ||
        die "Copied checkpoint SHA-256 mismatch: expected $expected_hash, got $copied_hash."
    cat > "$manifest_path" <<EOF
{
  "schemaVersion": 1,
  "preparedUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "source": "user-supplied-import",
  "sourceFileName": "$(basename -- "$source_path")",
  "localFile": "$(basename -- "$output_path")",
  "sha256": "$copied_hash",
  "bytes": $(wc -c < "$output_path")
}
EOF
fi

actual_hash="$(sha256sum "$output_path" | awk '{print tolower($1)}')"
printf 'Prepared verified base checkpoint: %s\nSHA-256: %s\nManifest: %s\n' "$output_path" "$actual_hash" "$manifest_path"
