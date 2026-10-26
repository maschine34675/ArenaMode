# Domination admission checks

Run from the repository root:

```powershell
dotnet run --project tools/DominationAdmissionChecks/DominationAdmissionChecks.csproj
```

This standalone net10 harness compiles the real shared map/population/participant
rules, native selection state, virtual-side lease and conditional-map lifecycle.
It checks Factory day/night admission, rejected maps/areas/PFT, retained existing
mode behavior, count validation, selected-side isolation and native-view switch
back. A small explicitly labelled source-contract section checks that dependency
and reward rejection remain before roster freeze/server arm/reward begin.

No mod build, deployment or game process is involved. Native menu layout,
objective behavior and gameplay acceptance remain separate integration gates.
