# Team Arena expansion checks

Offline checks for protected 5v5 expansion policies and integration contracts.
They exercise best-of-N scoring and spectator policy directly, then inspect the
camera lifecycle, post-death pursuit, raid-leased opponent spawning, exact PFT
rebuild sequence, prepared PMC respawn authorization, and damage guards without
loading EFT or optional mods.

```powershell
dotnet run --project tools/TeamArenaExpansionChecks -c Release
```
