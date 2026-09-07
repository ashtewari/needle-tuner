# Needle Tuned Weights

Place locally fine-tuned Needle artifacts under this directory.

Expected layout for each run:

```text
IntentEvalHarness/weights/<run-name>/
  manifest.json
  needle_tuned.cact
  checkpoints/
```

The wrappers create `needle_tuned.cact` and a manifest that names it in
`weightsFile`. Do not rely on manifest auto-discovery when evaluating a run.
Select exactly one completed run by loading its `providerKey`, `displayName`,
`weightsFile`, and `sha256` from that run's `manifest.json`; the registry
validates the selected artifact hash before loading it.

Weights, checkpoints, and adapters are generated locally and are not committed.
Record completed runs in the canonical append-only
[`../../docs/experiments/EXPERIMENT_LOG.md`](../../docs/experiments/EXPERIMENT_LOG.md).
The historical best-run manifest and evaluation results are retained under
`../../docs/experiments/provenance/20260905_lowcap/`; the corresponding
generated weights are intentionally omitted.

Recommended workflow:

1. Export the training JSONL:
   `dotnet run --project IntentEvalHarness -- --export-training-jsonl Dataset/needle-training-seed.json --output Dataset/training_set.seed.jsonl`
2. Start a fine-tune run:
   Windows: `pwsh -File scripts/needle-finetune.ps1`
   WSL/Linux: `bash scripts/needle-finetune.sh`
3. Evaluate one manifest-selected tuned provider against the frozen baseline:

   ```bash
   run_name="<run-name>"
   manifest="IntentEvalHarness/weights/$run_name/manifest.json"
   export Needle__TunedWeightsPath="$(python3 -c 'import json,sys; print("weights/" + sys.argv[2] + "/" + json.load(open(sys.argv[1]))["weightsFile"])' "$manifest" "$run_name")"
   export Needle__TunedProviderKey="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["providerKey"])' "$manifest")"
   export Needle__TunedWeightsDisplayName="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["displayName"])' "$manifest")"
   export Needle__TunedWeightsSha256="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sha256"])' "$manifest")"
   dotnet run --project IntentEvalHarness -- \
     --providers "needleBase,$Needle__TunedProviderKey" \
     --compare-baseline Baselines/openAi_frozen_20260823_full45
   ```