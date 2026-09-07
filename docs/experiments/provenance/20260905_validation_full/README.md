# Clean-room validation evidence

This tracked bundle is a sanitized release record for the recorded WSL
validation run. It contains no weights, checkpoints, native binaries,
usernames, drive-letter paths, `/home`, `/mnt`, secrets, raw logs, or verbose
transcripts.

Contents:

- `validation-summary.md` — commands/categories, timestamps, duration, metrics,
  fallback counts, limitations, and evidence references.
- `source-evidence.md` — sanitized factual basis and links to published target
  evidence used to curate this record; it does not depend on team-workspace
  files.
- `manifest.json` — scope, retained files, and artifact provenance.
- `checksums.sha256` — SHA-256 for every retained bundle file except this
  checksum file itself.

The 30-epoch training interval is recorded as **27m31s** (approximately
27.5 minutes), from `00:36:57.347Z` through `01:04:28.564Z`. The candidate
is not promotable: it scored below base Needle on the repeated 45-case
regression/model-selection set.
