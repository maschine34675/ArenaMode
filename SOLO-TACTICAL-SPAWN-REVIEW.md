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
