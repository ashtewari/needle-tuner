# Best-run provenance

This directory retains small, non-secret provenance for the historical
`20260905_lowcap`/`20260905_033826` low-capacity runs. The generated model
weights, checkpoints, and adapters are intentionally omitted.

- `best-run-manifest.json`: settings and SHA-256 for the omitted `.cact` weight.
- `evaluation-summary.json`: the 45-case evaluation summary.
- `results.csv`: per-case evaluation results.
- `checksums.sha256`: checksums for the retained files.

The run measured 64.44% intent accuracy and 62.22% parameter accuracy. This is
historical provenance, not a current default or a promise that another machine
will produce the same result.
