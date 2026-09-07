# Needle2 native engine

This directory holds the local Needle2 native engine binaries:
- `libneedle.dll` for Windows
- `libneedle.so` for Linux or WSL

Create the pinned environment and fetch the native engine through the
repository scripts:

```powershell
pwsh -File scripts/bootstrap-needle.ps1 -Cuda
pwsh -File scripts/acquire-needle-native.ps1
```

```bash
bash scripts/bootstrap-wsl.sh --cuda
bash scripts/acquire-needle-native.sh
```

The scripts pin and verify the `cactus-needle` package, record acquisition
provenance, and store the native binary under the current RID. No upstream
native-binary checksum is published by the documented source, so the package
wheel hash establishes acquisition provenance and the locally recorded binary
hash provides subsequent tamper detection.

The native binaries are not committed to source control (see `.gitignore`) and
must be fetched locally before Needle evaluation. Without a matching binary,
the Needle provider cannot run. OpenAI remains disabled by default and is not
an automatic fallback.

Native binaries are local, reproducibly acquired artifacts and are not
committed. Their acquisition is not an experiment-log entry; record any
measured training or evaluation run in
[`../../docs/experiments/EXPERIMENT_LOG.md`](../../docs/experiments/EXPERIMENT_LOG.md).
See [`../../docs/RESPONSIBLE-AI.md`](../../docs/RESPONSIBLE-AI.md) for
third-party licensing and release constraints.
