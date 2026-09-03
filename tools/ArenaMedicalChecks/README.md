# Arena medical timing checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/ArenaMedicalChecks -c Release
pwsh -NoProfile -File tools/ArenaMedicalChecks/Verify-Regression.ps1
```

The harness compiles the production medical timing and Harmony patch against
small stand-ins. It verifies the exact SPT 4.1 hook signatures, Arena/local-PMC
gates, proportional effect timing, non-stacking multi-limb animation speed and
cleanup at controller destruction, both raid boundaries and plugin shutdown.
The still-installed Harmony postfix is inert after plugin shutdown.

Food, ordinary raids, bots, dead players, foreign health controllers, items not
in the player's hands, invalid timing and a 1x setting all remain untouched.
The source contract also rejects shared item-template, global-health-setting or
skill mutation.

The mutation script removes the active-Arena gate and independently breaks
progress scaling in temporary source copies. Both mutants must fail before the
unchanged production sources are checked again. It never replaces a repository
file. Native animation/effect synchronization and visible timing still require
a real client test.

Design references: [BlackHawk-QuickMeds](https://github.com/black-hawk95/BlackHawk-QuickMeds)
and [MedicalAttention](https://github.com/jbs4bmx/MedicalAttention), both MIT.
No source is bundled; the production behavior is derived from and checked
against the local SPT 4.1 game sources.
