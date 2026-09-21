# Needle native engines

This directory holds the local Needle native engine binaries, one subtree per
engine generation:

- `<rid>/libneedle.{dll,so}` — the default-pinned Needle2 engine (`cactus-needle` 2.0.10).
- `<rid>/3.0.2/libneedle.{dll,so}` — the Needle3 engine (`cactus-needle` 3.0.2), stored in a
  version-suffixed subfolder so both engine generations can coexist locally without
  overwriting each other.

Create the pinned environment and fetch the native engine through the
repository scripts. The default invocation (no `-EngineVersion`/`--engine-version`)
acquires the Needle2 engine exactly as before:

```powershell
pwsh -File scripts/bootstrap-needle.ps1 -Cuda
pwsh -File scripts/acquire-needle-native.ps1
```

```bash
bash scripts/bootstrap-wsl.sh --cuda
bash scripts/acquire-needle-native.sh
```

To acquire the Needle3 engine instead, pass the pinned `3.0.2` engine version
(inference-only; no `-Cuda`/`--cuda` or training stack):

```powershell
pwsh -File scripts/bootstrap-needle.ps1 -EngineVersion 3.0.2
pwsh -File scripts/acquire-needle-native.ps1 -EngineVersion 3.0.2
```

```bash
bash scripts/bootstrap-wsl.sh --engine-version 3.0.2
bash scripts/acquire-needle-native.sh --engine-version 3.0.2
```

The scripts pin and verify the `cactus-needle` package for the selected engine
version, record acquisition provenance, and store the native binary under the
current RID (and version subfolder for non-default engine versions). No
upstream native-binary checksum is published by the documented source, so the
package wheel hash establishes acquisition provenance and the locally recorded
binary hash provides subsequent tamper detection.

The native binaries are not committed to source control (see `.gitignore`) and
must be fetched locally before Needle evaluation. Without a matching binary,
the corresponding Needle provider cannot run. OpenAI remains disabled by
default and is not an automatic fallback.

Needle2 (`needleBase`/tuned) and Needle3 (`needleV3`) cannot be loaded in the
same process — the harness enforces this with a fail-fast CLI guard rejecting
`--providers` selections that mix `needleV3` with any Needle2-family provider.
Run each engine in a separate `dotnet run` invocation.

Native binaries are local, reproducibly acquired artifacts and are not
committed. Their acquisition is not an experiment-log entry; record any
measured training or evaluation run in
[`../../docs/experiments/EXPERIMENT_LOG.md`](../../docs/experiments/EXPERIMENT_LOG.md).
See [`../../docs/RESPONSIBLE-AI.md`](../../docs/RESPONSIBLE-AI.md) for
third-party licensing and release constraints.
