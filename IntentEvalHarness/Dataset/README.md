# Needle Training Dataset

This directory now has two distinct roles:

- `intent-eval-dataset.json`: the 45-case held-out regression set used for harness evaluation. Keep this file out of Needle fine-tuning input.
- `needle-training-seed.json`: a separate seed training corpus for Needle fine-tuning preparation. It is biased toward the currently weak areas: `UPDATE_ITEM`, `UNCLEAR`, `SEARCH_ITEM`, `VIEW_INVENTORY`, and `MANAGE_BOX` boundary cases.

Recommended workflow:

1. Keep `intent-eval-dataset.json` frozen for held-out comparison against the OpenAI baseline.
2. Expand `needle-training-seed.json` as you gather more production-like prompts.
3. Export JSONL with:
   `dotnet run --project IntentEvalHarness -- --export-training-jsonl Dataset/needle-training-seed.json --output Dataset/training_set.seed.jsonl`
4. Use the generated `training_set.seed.summary.json` to verify class balance and abstain coverage.

Current authored seed corpus counts:

- `SEARCH_ITEM`: 18
- `ADD_ITEM`: 12
- `UPDATE_ITEM`: 27
- `DELETE_ITEM`: 12
- `MANAGE_BOX`: 21
- `VIEW_INVENTORY`: 20
- `UPLOAD_PHOTO`: 14
- `GENERAL_HELP`: 13
- `UNCLEAR`: 30

Total seed examples: `167`

`needle-training-seed.300.json` is a deterministic 345-example expansion. The
standard JSONL exports retain source order. The `UNCLEAR`-oversampled JSONL has 391
rows: each of its 46 `UNCLEAR` rows appears twice, adjacently, and every other row
appears once. See `artifact-manifest.json` for artifact provenance and regeneration
contracts. Regenerate all derived artifacts with
`pwsh -NoProfile -File scripts/generate-training-datasets.ps1`; the historical
`generate_needle_training_seed_300.ps1` command is a compatibility shim.

The JSONL exporter uses the current Needle tool schema from `NeedleIntentClient`, and
it emits abstain rows for `UNCLEAR` with `answers: []`.