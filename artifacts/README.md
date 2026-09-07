# Local artifacts

This ignored directory is for reproducible local derived material that does not
belong in committed datasets, manifests, or documentation.

Do not use this directory for experiment history. The canonical append-only log
is [`../docs/experiments/EXPERIMENT_LOG.md`](../docs/experiments/EXPERIMENT_LOG.md).
Generated weights, checkpoints, adapters, native binaries, environments, and
runtime reports remain local-only; retain only small provenance artifacts when
they are explicitly curated under `docs/experiments/provenance/`.
