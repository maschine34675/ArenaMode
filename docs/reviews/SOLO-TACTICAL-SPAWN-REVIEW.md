# Solo Tactical opponent-start review

## Observed failure

The client log records the first Solo Tactical request at `18:19:27.579`. The
old 12-second grace window declared the squad absent at `18:19:39.864` and
replayed the whole round. That request was healthy: its five-profile spawn plan
completed at `18:19:40.307`, and its first bot arrived at `18:19:40.352`.
Consequently, the mode invalidated a cold request only 443 ms before its plan
completed and 488 ms before its first opponent appeared.

The old no-contest path returned to the buy phase. Each replay therefore ran the
round-start work again, including the player body/kit replacement, while the
late bots from the invalidated request were correctly retired. Once profile,
bundle and prefab pools were warm, a later request completed in time. This
accounts for the apparent third or fourth respawn before opponents finally
appeared. The same timing pattern is present in the archived log; bot-zone
capacity and the backend's profile-response time do not account for the delay.

## Corrected behavior

Solo Tactical now gives a dispatched opponent request the same 45-second
assembly window already used by protected 5v5. A synchronously rejected spawn
plan retries after three seconds. If a dispatched request reaches the deadline
with no arrivals, only the opponent request is renewed inside the current
round. The buy, payment, body replacement and round-start announcement are not
repeated. The existing partial-squad fallback is preserved: after the assembly
window, one or more arrivals may form the round even if a configured oversized
squad cannot be filled.

The request publishes its expected count before the asynchronous spawn call so
an already-warm task cannot invoke the observer against stale count state. Its
epoch and live-phase predicate are also passed into the burst-spawn service.
The service checks that lease after profile creation and again immediately
before ledger/native dispatch. Superseded creation data is stopped at those
safe points; callbacks from a request already owned by EFT still pass through
the observer and are retired by the epoch check. The combat timer is restarted
when the first accepted opponent arrives, so cold assembly does not consume the
configured fighting time. A survivor's asynchronous round-break body swap is
bound to the exact round number as well as the live phase.

## SPT 4.1 contract and verification boundary

The decompiled source and installed `Assembly-CSharp.dll` agree that
`BotCreationData.StopSpawn()` only cancels that creation object's token and
unsubscribes its stop handler. EFT increments its in-spawn counter and takes
ownership of positions later, inside `BotSpawner`, when
`SpawnBotsInZoneOnPositions` is dispatched. The two new cancellation checks are
before that boundary. No cancellation is attempted afterward because doing so
could strand EFT's per-point in-spawn count. Ambient Arena spawns and protected
5v5 do not pass the optional mode request predicate and retain their prior
behavior.

`tools/SoloTacticalSpawnChecks` links the production timing policy and checks
source contracts around callback order, retry isolation, timer start, body-swap
round identity and both pre-dispatch lease checks. Its regression script rejects
temporary copies containing the old 12-second timeout, late expected-count
publication and whole-round replay, then verifies production file hashes were
unchanged. The solution build validates the implementation against the locally
installed SPT 4.1 assemblies. This machine has no GPU, so actual Unity task
scheduling, bot activation and the visible transition into combat still require
an in-game test.

## Dispatch order (2026-11-13)

Found in the review of the spawn-row follow-up and recorded there as decision 9, "Known limit: solo squad
dispatch order" ([SPAWN-ROWS-FOLLOWUP-20261113.md](SPAWN-ROWS-FOLLOWUP-20261113.md)). `StartRound` called
`RequestSoloSquad(retry: false)` and only then started the body move that puts the player where the round
begins: `LiveKitRespawn.AtRoundBreak` for a survivor (round 1 included: the player is not down there), the
respawn held by `HoldRespawn` for a player who died last round. Both moves take several frames.
`BurstSpawnService.SpawnPair` picks the squad's points right after `await BotCreationData.Create`, at least
`BurstSpawnPointSelector.MinSpawnDistance` (25 m) from wherever the player stands at that moment, and with a
warm profile pool that await can complete inside the request. Whenever the profiles won that race, the points
were measured from the spot the player was leaving. The native rounds measured before the fix did not lose it
(the swap landed 0.075 s after the request, the points were picked 1.8 s after it); the code did not guarantee
the order.

### Contract

1. `StartRound` sends no request and shows no "assembling" toast. It sets `_squadRequested = 0` (so neither the
   assembly check nor the HUD notice reads the previous round's count) and hands the request to
   `SoloTacticalSquadDispatch.Open(round, startBodyMove)`, which holds it FIRST and then starts the move
   (`StartSoloBodyMove`): a survivor's `AtRoundBreak` gets the gate's callback as its `onComplete` (it used to
   get `null`); for a player who died, the move is the held respawn `StartRound` has just released, in flight
   while `ArenaRespawnDriver.IsRespawnInProgress` holds, and its landing is reported by
   `OnLocalRespawn`'s solo branch (`OnSoloLocalRespawn`).
2. The request is released exactly once per round, and only for the round it was opened for, by whichever comes
   first: the move landed (the swap's `onComplete(true)`, the respawn's `OnLocalRespawn`); the move reported that
   it did not land (`onComplete(false)`: stood down, refused by the engine, failed); no move was in flight (the
   swap refused synchronously, no `LocalGame`, a held respawn whose routine has already ended, a start that
   threw); the cap, `SoloTacticalSquadDispatch.BodyMoveCapSeconds` = `SoloTacticalSpawnPolicy.AssemblySeconds`
   (45 s) after the round start, checked every frame by `TickLive`. The hold is in place before the move starts,
   because a move can report inside its own start; a second report, a late report from an earlier round and a
   report after `EndRound`, `OnMatchEnd` or `OnMatchStart` (all three cancel) release nothing.
3. The release calls `DispatchOpeningSquad`, which sends the request only into its own live round, logs why at
   Debug (Warning for the cap, with "requesting the squad from where the player stands now"), calls
   `RequestSoloSquad(retry: false)` (which publishes `_squadRequested` and the 45 s `_squadDeadlineAt` from that
   moment) and then shows "Round N — assembling N opponents". The 45-second assembly window therefore counts
   from the real request, not from the round start. Both releases arrive through callers that swallow
   exceptions (`LiveKitRespawn`'s report, `ArenaMatch.RaiseSafe`), so a request that throws is caught there,
   logged as a Warning and retried after `SoloTacticalSpawnPolicy.PlanFailureRetrySeconds` (3 s) like a
   rejected plan (finding of the adversarial review).
4. While the request is held, `TickLive` returns before the assembly evaluation, the retry and the combat timer;
   nothing is asked for yet, so nothing can be retried or time out.
5. The destination is not pre-resolved: `ArenaSpawnSelector` draws the Player row at random inside
   `RespawnEngine.RunRespawn`. The position the points are measured from is `BurstSpawnService.GetPlayerPosition`,
   `GameWorld.MainPlayer`, which Redeploy sets to the new body inside `RunRespawn`
   (`Redeploy.Core/Engine/RespawnEngine.cs`, `Singleton<GameWorld>.Instance.MainPlayer = newPlayer`), before
   `LiveKitRespawn.SwapRoutine` reports and before `ArenaRespawnDriver` publishes the respawn event.
6. The Debug line "Arena burst spawn plan: ... selected N from the player at (x, y, z)." names that position, the
   native evidence of the order.

PitFireTeam Tactical (`RequestTeamEnemies`) is a different path and unchanged.

### Limits

- A move that lands only after the cap: the request goes out from where the player stands at the cap, the old
  race for that one case. Nothing measured comes near it (a warm swap lands in 0.075 s; the slowest legitimate
  path is a cold kit's bundles, 22 s on the slow machine, before the engine's own 30 s body-creation deadline).
- A held respawn whose routine ends without publishing a body (its own failure paths, most of which end the raid)
  waits out the cap before the squad is requested; a refused or failed survivor swap reports and does not wait.
  The swap's own exits without a report are the ones that find the raid no longer active or `StandDown` set, that
  is, the raid is ending.
- While the request is held nothing says "assembling": the toast and the assembly notice (which needs
  `_squadRequested > 0`) come with the request. Normally that is the fraction of a second the swap takes; a dead
  player's respawn adds the killcam and the bought kit's bundles; only a move that never reports makes it 45 s.
- The guarantee is the 25 m floor at planning time, measured from the position the player landed on. Bots move
  once they are active, so a distance read after "round N live" can be below 25 m with a correct plan.

### Verified offline

- `tools/SoloTacticalSpawnChecks`: the gate itself (hold, every release, exactly once, round binding, cancel,
  re-entrancy, a start that throws, the cap no longer than the assembly window); the dispatch order simulated on
  the installed ArenaMapPort rows of all six ports with the production selector (every row as the starting
  position, every Player row at least 40 m away as the destination, swaps that land at once, after 0.075 s and
  after 2.5 s, held respawns, refusals, failures, a move that never reports and one that lands after the cap,
  every rotation of the selector's cursor); and the source contract of the wiring (`StartRound`,
  `StartSoloBodyMove`, `OnSoloLocalRespawn`, `DispatchOpeningSquad`, `TickLive`, `EndRound`, `OnMatchEnd`, the
  constructor). The simulation reads from the production `StartRound` whether it requests the squad itself or
  hands the request to the gate. Against the fixed source: 3,550 simulated round starts, closest squad point
  25.1 m from the post-move position. Against the order before the fix (`a1e33b87`, run by
  `Verify-Regression.ps1`) the assertion "every squad point is at least 25 m (BurstSpawnPointSelector.MinSpawnDistance)
  from the post-move position" fails: of the round starts that land the player on a new row, a selection with a
  point closer than 25 m to the new row is possible in 210 of 210 on Chop Shop (100 %), 240 of 275 on Bay5 (87 %),
  175 of 235 on Equator (74 %), 90 of 320 on Iceberg (28 %), 50 of 330 on Air Pit (15 %) and none on Bowl; the
  closest example is an Air Pit squad point 9.7 m from the new row. These count a start as failing when any
  rotation of the round-robin cursor yields such a point, so they are upper bounds next to the reviewer's
  single-draw figures in the follow-up. `Verify-Regression.ps1` rejects thirteen mutants, eight of them new: the
  pre-fix source, a request reinserted before the move, a landing `OnLocalRespawn` no longer reports, a swap
  whose callback is `null` again, a `TickLive` that does not ask the gate, a gate that releases at `Open`, a
  cap 15 s longer than the assembly window and a gate that releases a second time.
- `tools/TacticalPolishChecks` runs the extracted production bodies of `StartRound`, `StartSoloBodyMove`,
  `DispatchOpeningSquad`, `OnSoloLocalRespawn`, `TickLive` and `RequestSoloSquad` (part `order`): no request,
  count or toast before the swap lands; the landing sends the request with its count and toast; the 45 s window
  counts from the real request (no retry at 45.9 s after a request at 1 s, a retry at 46 s); a failed, a refused
  and a never-reporting swap (the cap at 45 s); a dead player's request held for the respawn and released by its
  landing, or at once when that routine has already ended; nothing sent after the round or match ended, or by a
  late landing from an earlier round; a request that throws announces nothing and is retried after 3 s, not every
  frame. Three probes must fail there: the actual `StartRound` of `a1e33b87`, a
  `TickLive` that ignores the held request and an `OnSoloLocalRespawn` that does not report.
