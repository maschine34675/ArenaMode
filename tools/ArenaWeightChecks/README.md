# Arena carry-weight checks

Offline regression harness for the Arena-only, local-player weight-limit scaling
and its apply/restore lifecycle. It compiles the production patch, tuning and
runtime files against small SPT stand-ins, then rejects mutations that leak into
ordinary raids, omit a weight range or miss a respawned `Physical` instance.

```powershell
dotnet run --project tools/ArenaWeightChecks -c Release
pwsh -NoProfile -File tools/ArenaWeightChecks/Verify-Regression.ps1
```
