# Needle2 experiment log

This is the canonical, append-only log for Needle2 fine-tuning and evaluation
experiments. Scripts and the `needle2-train-eval-loop` skill must refer to this
file; do not create a second experiment history. Append every successful,
regressed, and failed run with its dataset, settings, measured result, finding,
next decision, and expected result.

The entries below are historical measurements copied and curated from the
WhichBox working-tree experiment history. They are not current defaults and
are not guarantees of future runs. Results were measured on the 45-case
held-out dataset. The frozen OpenAI baseline was 93.33% intent accuracy and
71.11% parameter accuracy.

## Historical runs

| Iteration | Run | Dataset | Settings | Measured result |
|---:|---|---|---|---|
| 1 | `20260904_015940` | 300 examples | 5 epochs, rank 16/alpha 32, lr 1e-4, batch 4 | 55.56% intent / 53.33% parameter; regression |
| 2 | `20260905_014238b` | 325 examples, targeted expansion | 5 epochs, rank 16/alpha 32, lr 1e-4, batch 4 | 57.78% / 60.00%; regression |
| 3 | `20260905_lowcap` | 325 examples | 5 epochs, rank 8/alpha 16, lr 5e-5, batch 4 | **64.44% / 62.22%; best observed intent result** |
| 4 | `20260905_023254` | 325 examples | 30 epochs, rank 8/alpha 16, lr 5e-5, batch 4 | 57.78% / 64.44%; longer training regressed intent |
| 5 | `20260905_030401` | 325 examples | 12 epochs, rank 8/alpha 16, lr 5e-5, batch 4 | 57.78% / 60.00%; regression |
| 6 | `20260905_032612` | 345 examples, rebalanced | 5 epochs, rank 8/alpha 16, lr 5e-5, batch 4 | 60.00% / 57.78%; regression |
| 7 | `20260905_033826` | 345 examples, rebalanced | 5 epochs, rank 8/alpha 16, lr 5e-5, batch 4 | **64.44% / 62.22%; tied best** |
| 8 | `20260905_035100` | 391 examples, UNCLEAR oversampled | 5 epochs, rank 8/alpha 16, lr 5e-5, batch 4 | 60.00% / 64.44%; regression |
| 9 | `20260905_040219` | 345 examples | 5 epochs, rank 16/alpha 32, lr 1e-4, batch 4 | 57.78% / 60.00%; regression; loop stopped |

All runs used max length 1024 and validation split 0.1 unless stated
otherwise. The 345-row corpus and 391-row UNCLEAR-oversampled corpus are
reproducible generated artifacts documented by
`IntentEvalHarness/Dataset/artifact-manifest.json`.

## Findings

- The rank-8/rank-16 and epoch-count comparisons were noisy. Two identical
  rank-8, five-epoch runs on the 345-row corpus measured 60.00% and 64.44%
  intent accuracy, showing meaningful run-to-run variance.
- The 30- and 12-epoch runs did not improve held-out intent accuracy even
  though validation loss declined. Validation loss is therefore not a
  sufficient proxy for this held-out behavior.
- UNCLEAR remained at or below 20% intent accuracy across the tuned runs.
  Doubling UNCLEAR rows did not fix it, so further oversampling is not an
  evidence-based recommendation.
- The best measured tuned result is 64.44% intent / 62.22% parameter accuracy,
  but it remains below the frozen OpenAI baseline. Treat it as historical
  evidence, not a production quality claim.
- The loop stopped after the available dataset, epoch, capacity, and
  oversampling levers failed to exceed the best result. Architectural,
  decoding, or output-head changes were not tested by this log.

## Reproduction versus history

The table records what was measured in the source working tree. To run a new
experiment, use the current wrappers and current repository defaults, record
the exact command and environment, and append a new dated entry. Do not claim
that a current run reproduces a historical result unless it is actually
measured. Hardware, GPU/JAX nondeterminism, runtime, and loss observations are
specific to the recorded run and must not be generalized to all machines.

The current fine-tuning wrappers use the full-run defaults documented in their
help output and repository guidance. These are operational defaults, not a
quality recommendation. The historical five-epoch best run is preserved for
provenance only; it does not change those defaults.

The 45 cases are held out from training, but the loop repeatedly used their
results to choose later experiments. They therefore serve as a frozen
regression/model-selection set, not an unbiased final test set. A separately
created unseen set is required for generalization, production-readiness, or
model-superiority claims.

## Artifact policy

Generated weights, checkpoints, adapters, native binaries, local environments,
loss plots, and runtime reports are not hosted or committed. They are acquired
or generated locally using the repository scripts. The omitted local Needle
weights/checkpoints/adapters occupied approximately 2.13 GB in the source
working tree; that omission is intentional and does not invalidate the
recorded measurements.

Small provenance artifacts are retained when permitted by the extraction
inventory: see
`docs/experiments/provenance/20260905_lowcap/README.md` for the best-run
manifest, evaluation summary, results, and SHA-256 checksums. The checksum of
the omitted `.cact` weight is retained in the manifest, but the weight itself
is not.

## 2026-09-06 UTC: validation_full_20260905 (blocked preflight)

- **Dataset:** `IntentEvalHarness/Dataset/training_set.300.jsonl` (345 rows);
  the 45-case held-out dataset was not used as training input.
- **Settings:** requested quality defaults: 30 epochs, rank 16, alpha 32,
  learning rate 0.0001, batch size 4, max length 1024, validation split 0.1.
- **Measured result:** no training or tuned evaluation ran; no adapter,
  `.cact`, manifest, or candidate metrics were produced.
- **Finding:** a clean WSL 2 validation environment had Python 3.12, pinned
  GPU JAX, an NVIDIA GPU, and acquired native engine, but correctly excluded
  the local-only base checkpoint. The wrapper failed closed because
  `IntentEvalHarness/weights/base/needle2.pkl` was absent.
- **Next decision:** acquire or explicitly supply a compatible, licensed local
  base checkpoint, then run one full quality configuration and evaluate the
  exact manifest-selected artifact against base Needle and the frozen baseline.
- **Expected result:** a completed local run with log, SVG, adapter, `.cact`,
  manifest, and 45-case held-out comparison; do not claim historical metrics
  until that run completes.

## 2026-09-06 UTC: validation_full_20260905

- **Dataset:** `IntentEvalHarness/Dataset/training_set.300.jsonl` (345 rows);
  the 45-case held-out dataset was used only for evaluation.
- **Settings:** 30 epochs, LoRA rank 16, alpha 32, learning rate 0.0001, batch
  size 4, max length 1024, validation split 0.1, CUDA JAX on an RTX 3060.
- **Measured result:** completed in **27m31s (approximately 27.5 minutes)**.
  The manifest-selected tuned candidate evaluated all 45 held-out cases:
  42.22% intent accuracy,
  51.11% parameter accuracy, and 15 fallbacks. Base Needle measured 62.22%
  intent accuracy, 66.67% parameter accuracy, and 9 fallbacks. The frozen
  OpenAI baseline remains 93.33% intent and 71.11% parameter accuracy.
- **Finding:** this quality-default run regressed 20 percentage points in
  intent accuracy versus base Needle. Training loss fell, but validation loss
  plateaued near 0.502; do not use this candidate as a replacement.
- **Next decision:** investigate the tuned output-head/confidence behavior or
  a single dataset change before retraining; retain the held-out set unchanged.
- **Expected result:** a targeted change should exceed the 62.22% base intent
  accuracy on a fresh 45-case held-out evaluation before further comparison.

The sanitized validation evidence for this run is retained in
`docs/experiments/provenance/20260905_validation_full/`.
