# Solo Tactical spawn checks

Run from the repository root:

```powershell
dotnet run --project tools/SoloTacticalSpawnChecks/SoloTacticalSpawnChecks.csproj -c Release
```

The harness links the production timing policy and checks the Tactical/Burst
source contracts that cannot be loaded without the Unity/EFT runtime. It covers
the measured 12.728-second cold request, the full 45-second assembly window,
three-second preflight retry, exact and partial squads, synchronous observer
ordering, same-round retry isolation, an exact round-bound body-swap predicate,
full combat time after the first arrival, and cancellation before native body
dispatch.

`Verify-Regression.ps1` runs three temporary mutants outside the repository: the
old 12-second timeout, a late observer-limit publication, and the old whole-round
replay behavior. It then reruns the unmodified production check. A real raid is
still required to verify Unity scheduling, bot activation, body replacement and
the visible/audio transition into combat.
