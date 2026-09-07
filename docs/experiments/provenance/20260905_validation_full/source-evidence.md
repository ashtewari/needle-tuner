# Evidence basis

This self-contained bundle retains sanitized factual excerpts rather than raw
transcripts, machine logs, or team-workspace records. No unpublished path is
required to interpret or verify its claims.

## Sanitized factual record

- One explicit WSL run used the documented 30-epoch settings and completed
  from `00:36:57.347Z` through `01:04:28.564Z`, a duration of **27m31s**.
- Its exact manifest-selected 45-case evaluation recorded the candidate at
  19/45 intent, 23/45 parameters, and 15 fallbacks; base Needle at 28/45,
  30/45, and 9; and the frozen OpenAI reference at 42/45, 32/45, and 0.
- The omitted candidate artifact's recorded SHA-256 is
  `8518c4774ebb41573021ab86f8ab6e5dc06422ac730b9ebbd30c866b26b03295`.
  It is manifest/log provenance only because the generated artifact is not
  published.
- The historical WSL post-training verification passed **32/32** .NET tests.
  A later reviewer-fix verification passed **36/36** tests after repository
  changes; it did not run during the historical training execution and did not
  produce new training or evaluation metrics.

## Published target references

| Repository-relative reference | Used for |
|---|---|
| `docs/experiments/EXPERIMENT_LOG.md` | Canonical append-only experiment history |
| `IntentEvalHarness/integrity.manifest.json` | Dataset and frozen-baseline integrity contract |
| `IntentEvalHarness/Baselines/openAi_frozen_20260823_full45/` | Committed 45-case frozen OpenAI baseline evidence |

Generated weights, checkpoints, adapters, native binaries, local environments,
runtime reports, raw logs, usernames, and ephemeral run names are deliberately
not published.
