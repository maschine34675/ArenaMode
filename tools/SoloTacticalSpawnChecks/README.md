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
full combat time after the first arrival, cancellation before native body
dispatch, and the buy-phase end waiting on a body swap still in flight (the
raid-start kit swap, or a respawn after a death in the buy phase; the pure
`SoloTacticalRoundStartPolicy` itself is covered by `ArenaMode.Tests`).

`Verify-Regression.ps1` runs five temporary mutants outside the repository: the
old 12-second timeout, a late observer-limit publication, the old whole-round
replay behavior, a buy phase that ends without waiting for the swap, and a
Hold decision the switch ignores. It then reruns the unmodified production
check. A real raid is
still required to verify Unity scheduling, bot activation, body replacement and
the visible/audio transition into combat.
