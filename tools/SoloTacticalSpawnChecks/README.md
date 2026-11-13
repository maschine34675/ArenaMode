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
full combat time only after physical Active readiness, no premature fight cue,
one logical award-round reset without another reset at readiness, cancellation before native body
dispatch, and the buy-phase end waiting on a body swap still in flight (the
raid-start kit swap, or a respawn after a death in the buy phase; the pure
`SoloTacticalRoundStartPolicy` itself is covered by `ArenaMode.Tests`).

Dispatch order (docs/reviews/SOLO-TACTICAL-SPAWN-REVIEW.md, "Dispatch order"):
it also links the production gate `SoloTacticalSquadDispatch`, the production
`BurstSpawnPointSelector` and `ArenaBotSpawnPolicy` (with the native doubles of
`NativeDoubles.cs`) and checks that a round's squad request is held until the
body move lands, released exactly once on every way the move can end, bound to
its round and capped at the 45-second assembly window. `Program.DispatchOrder.cs`
then simulates round starts on the installed ArenaMapPort rows of the six ports
(`--spt-root`, default `D:\SPT41`; Chop Shop and Equator must be installed) and
asserts that every squad point is at least 25 m from the position the player
landed on. Which order it simulates is read from the production `StartRound`:
one that requests the squad itself is the order before 2026-11-13 and fails.

`Verify-Regression.ps1` runs twelve temporary mutants outside the repository: the
old 12-second timeout, a late observer-limit publication, the old whole-round
replay behavior, a buy phase that ends without waiting for the swap, a
Hold decision the switch ignores, the actual `TacticalMode.cs` of `a1e33b87`
(the order before the fix), a squad request reinserted before the body move, an
`OnSoloLocalRespawn` that does not report the landing, a round-break swap whose
callback is `null` again, a `TickLive` that does not ask the gate, a gate that
releases at `Open` and a cap longer than the assembly window. It then reruns the
unmodified production check. A real raid is
still required to verify Unity scheduling, bot activation, body replacement and
the visible/audio transition into combat.
