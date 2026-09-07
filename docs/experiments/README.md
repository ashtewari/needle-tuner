# Experiments

`EXPERIMENT_LOG.md` is the single canonical, append-only Needle2 experiment
history. The training/evaluation scripts and the
`needle2-train-eval-loop` skill should link to this path rather than creating
another log.

Historical measurements and reproducible commands are deliberately separate:
the log records completed runs, while scripts describe how to perform a new
run. Hardware-specific observations remain scoped to the run that measured
them.

Generated model weights, checkpoints, adapters, native binaries, environments,
loss plots, and runtime reports are local-only and are not committed. Recreate
them with the repository scripts. Small manifests, checksums, and evaluation
results may be retained as provenance when they contain no secrets or
machine-specific paths.

The
[`20260905_validation_full`](provenance/20260905_validation_full/) bundle is
the sanitized release evidence for the recorded clean-room WSL setup, 30-epoch
run, exact 45-case evaluation, skill stages, and post-run tests.
