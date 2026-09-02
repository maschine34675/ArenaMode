# Team Arena expansion checks

Offline checks for the spectator policy and its narrow integration contracts.
They link the production policy source and inspect the camera lifecycle wiring,
without loading EFT or optional camera mods.

```powershell
dotnet run --project tools/TeamArenaExpansionChecks -c Release
```
