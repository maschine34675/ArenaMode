# TDM preparation and submission review (2026-09-08)

Scope: `TeamDeathmatchPopulation`, `TeamDeathmatchMode`, the explicit-faction
branch of `BurstSpawnService`, and their policy/runtime/async integration tests.
Normal Arena/Tactical spawn selection, area geometry, native path guards, team
capacities, and the independent PreActive activation deadline are unchanged.
Root owns optional catalogue warmup deferral and final build/delivery.

## Recorded defect

The GPU retest used `e2859e8`; report commit `55164e4`. Evidence is under
`artifacts/gpu-acceptance/2026-09-08_125900-retest/logs/`:

| Test / client.log lines | Match start | Area READY | Full active roster | Elapsed |
| --- | --- | --- | --- | --- |
| `04-after-usec-5v5`, 12952–13049 | 13:23:21.481 | 13:23:22.422 | 13:25:15.809 | 114.328 s |
| `15-after-bear-5v5`, 12949–13063 | 16:21:03.879 | 16:21:04.766 | 16:28:06.977 | 423.098 s |
| `17-after-bear-4v4`, 13630–13723 | 16:49:59.889 | 16:50:00.889 | 16:53:48.236 | 228.347 s |

All three area preparations reached and freshly verified all 28 markers in
under about one second. These initial roster windows contain no failed area
reservation/admission or retired activation. Once a spawn plan exists, bodies
arrive within approximately 20–40 ms and the final readiness announcement follows
the last body by about one second. Existing admission ages cover the entire
request; the old log cannot precisely split synchronous preflight, backend work,
asset loading and pool creation. The new stage diagnostics make the first three
client boundaries measurable without pretending to instrument inside EFT.

USEC 5v5 uses a fixed kit and has no initial timeout. Its nine accepted requests
take 7.3–40.8 seconds each, serially within each faction. BEAR 4v4 request #7 is
logged `loading:46s:expired` at 16:52:17.490 and is not discarded until
16:53:26.622. Replacement #8 then arrives at 16:53:47.205. BEAR 5v5 discards five
completed preparations before dispatch, at 16:23:44.857, 16:24:43.733,
16:25:02.402, 16:26:18.948, and 16:27:41.452. The timeout state explicitly shows
`loading`, not uncertain native arrivals; already admitted bodies are Active.

The old 45-second ticket included profile/pool preparation. Unreturned loads
correctly kept their faction single-flight, but their successful late results
were then rejected and replaced with new work. Native activation was never
started for these discarded preparations. Concurrent optional kit catalogue
loading is a plausible amplifier in the BEAR tests, not the sole proven cause:
the fixed-kit USEC test is also slow. Two concurrent Spartan preparations finish
at 16:21:17.215 and 16:21:18.899 with the same 35 cold templates. Server batch
cache timing messages are not full HTTP or client asset-load timings.

## Implemented ownership

1. A Preparing ticket owns one unresolved preparation per faction. At 45 seconds
   it contributes to the bounded slow-spawn diagnostic, while remaining valid.
   Its separate 300-second watchdog is an explicit operational ceiling.
2. The real async burst method awaits `BotCreationData.Create`, checks its exact
   raid/request lease, and creates a fresh real area reservation. Only immediately
   before native submission does a one-shot callback recheck the current match,
   exact ticket and actual faction occupancy, and record the submission time.
   A final lease check also catches synchronous drift inside that callback.
3. Native arrival has its own 45-second deadline from submission. A callback
   during Preparing cannot admit a body. Repeated transition calls cannot restart
   the clock, and synchronous arrival may settle before terminal dispatch return.
   The existing three-ticket native uncertainty bound per faction is preserved.
4. A physical PreActive body occupies its slot immediately and retains the
   independent existing 45-second activation deadline. Admission still verifies
   faction, group, identity, capacity and the area reservation.
5. Request phase clocks use `Time.realtimeSinceStartup`. The preparation watchdog
   runs before the simulation tick throttle and is also checked by detached
   current/terminal callbacks. A completion just after 300 seconds cannot remove
   its ticket and silently enable another preparation before the next tick.
6. Watchdog failure uses the existing terminal TDM failure path. It prevents all
   further refills and stale admissions, reports one failure, and does not claim
   cancellation of an unreturned engine task. It does not introduce an automatic
   raid exit; ordinary exit/teardown remains available. The old completed task is
   stopped before native dispatch whenever it eventually returns.

`InitialRosterReady` is true only after the initial full Active roster belongs
to the current exact live match. Root consumes this for optional warmup. It is
false for a replacement Current/world, native stop, failed mode or match end.

Diagnostics use process-local request numbers and faction names. They record
request preparation start, preflight duration/refusal, the entire awaited
`BotCreationData.Create` duration, fresh placement duration/refusal, submission
preparation duration and native arrival age. These timings do not distinguish
backend serialization from asset/pool work inside `Create`.

## Native source verification

Read-only source under `../_decompiled` confirms:

- `BotCreationData.cs:64,141`: `Create` constructs its own data internally and awaits
  `IBotCreator.GenerateProfile` before returning it. The caller cannot cancel that
  hidden data object during the await. `StopSpawn` cancels the data's own token.
- `BotCreatorClient.cs:211` delegates to `BotProfileClient.CreateProfile`.
- `BotProfileClient.cs:162–205`: backend load precedes a cancellation check; the
  native asset/pool await receives `ObjectsFactory.DefaultCancellationToken`,
  not the spawner token. A request timeout cannot prove native work ended.
- `EFT/ClientBackendSession.cs:1886`: backend `LoadBots` has no per-request
  cancellation-token argument. `EFT/BotSpawner.cs:629,737` owns a raid-wide token;
  cancelling it would affect unrelated spawner work and is not used here.
- `EFT/ObjectsFactory.cs:715–776`: the awaited task loads retained bundle jobs and
  then initializes/fills pools. Under `JobYieldPriority.Low` it returns without
  awaiting pool completion. Low is therefore not a safe drop-in preloader fix.
- `EFT/ObjectsFactory.cs:203–267`: requests may expand existing pools; already
  loaded bundles do not imply an instantly prepared new bot.
- `Diz.Jobs/JobScheduler.cs:140–225,270`: pool continuations share frame/loop
  budgets. Both General and Low yield asynchronously; this change does not force
  global scheduler execution, change native priorities or cancel shared pools.

## Verification and limits

The policy harness links actual population/dispatch/scoreboard code. Runtime
fixtures link the actual mode, population and relations. The additional
`tools/TeamDeathmatchRuntimeChecks/BurstIntegration/Run.ps1` extracts the complete
production `SpawnFactionBot`, async `SpawnPair`, and request-guard methods without
rewriting them, then compiles them with controlled native-boundary stand-ins and
the actual population and dispatch lease. A controllable TaskCompletionSource
exercises the real await and callback ordering, including already-completed
tasks, stale/overdue preparation, full occupancy, unavailable placement, missing
or throwing transition callbacks, synchronous observers, proven prefix rejection,
uncertain native exceptions, late arrivals and duplicate callbacks.

Targeted fault controls require their intended assertion to fail, not merely a
compile error. Existing runtime stop, friendship, score and capacity checks remain.
Final command evidence is recorded in
`tools/TeamDeathmatchRuntimeChecks/PREPARATION-GATES.md`.

Four self-review passes covered complete implementation, native cancellation and
phase semantics, race/fault injection and integration, then diagnostic/evidence
polish. The parent independently reviewed the stable population/mode, full Burst
callback lifecycle and extracted checks and reported no remaining P0–P2 issue.
Final integration verification and artifact delivery belong to the parent.
No leaf commit, full client build, deployment or live GPU test is claimed here.
The remaining GPU check is repeated cold USEC/BEAR 4v4/5v5 assembly and refill with
the new phase logs; these offline tests cannot promise a specific loading speed.
