# Zombie retreat-planning assessment

2026-10-09, source baseline `65dfb56` / installed client `415c5aa`. This is a
read-only assessment of native `BotDogFight`, separate from the pending-spawn
wave fix. It introduces no AI skip, settings change or supported-population
increase. Native measurements, if collected after this source review, belong in
the dated delivery report; the previous captures cannot isolate this subsystem.

## Recommendation

**Measured decision: keep native retreat planning unchanged.** The temporary
GPU2 probe subsequently measured only 0.00356–0.00360 ms/frame for all
`ManualUpdate` calls at 56 infected, including successful close-range checks
for both melee and shooting infected. Across the capture, 56 retreat checks
(54 successful) took 1.0192 ms in total; no `Fight` call was observed. These
overlapping instrumented times are not a demonstrated saving. See
`GPU-ZOMBIE-WAVE-RETREAT-RESULTS-20261009.md` for identities and limits.

Before measurement, the source review found a
plausible narrow opportunity while a generic infected is executing its own
attack layer, but **not** evidence for disabling all retreat/cover processing.
Successful retreat planning normally changes state once and then stops doing
path searches until the enemy leaves close range. The repeated expensive case
is an unsuccessful check, retried once per second. An expensive per-frame
admission guard could cost more than the mostly cheap original update.

The measurement used a temporary, observational GPU-only probe of
`BotDogFight.ManualUpdate`, `CheckSpaceForRetreat` and `Fight`, split by actual
infected brain mode and current layer. Do not turn this into a performance claim
from aggregate FPS alone. The measured cost is negligible here, so no permanent
compatibility seam is added.

## Pass 1: native producers and actual frequency

`../_decompiled/EFT/BotOwner.cs:1025-1066` calls `DogFight.ManualUpdate()` for
every alive Active bot whose standby state is not paused. The call is not
restricted to ordinary human bots. Its surrounding work includes hearing/vision,
aiming, motion, doors, medicine, memory and weapons; the entire owner update is
not a proxy for retreat cost.

`../_decompiled/BotDogFight.cs:85-137` has these states:

| State before update | Native work |
|---|---|
| No enemy | Set `none`, return. |
| Recently left cover with the native flag set | Set `dogFight`, return. |
| `none` | If an enemy is visible and within `DOG_FIGHT_IN`, perform one retreat check; success sets `dogFight`, failure sets `shootFromPlace`. |
| `dogFight` | Compare distance with `DOG_FIGHT_OUT`; no path search here. |
| `shootFromPlace` | Retry retreat at most once per second; success returns to `none`; also clear when the enemy leaves `DOG_FIGHT_OUT`. |

The installed server database has `DOG_FIGHT_IN = 3` and `DOG_FIGHT_OUT = 6`
for every difficulty of the four generic roles
(`D:/SPT41/SPT_Runtime/SPT_Data/database/bots/types/infected*.json`, for example
`infectedassault.json:555-556`). These are database values, not a readback of
post-modification runtime settings. A shooting infected standing 10 m from a
player would normally never enter the retreat-check branch at all; a useful
shooting probe must include a closer target, or record its actual live thresholds.

`CheckSpaceForRetreat` (`BotDogFight.cs:38-73`) first excludes a nearby door,
then may do two `NavMesh.SamplePosition` calls, one `NavMesh.CalculatePath`,
read the completed path corners and check path length. A door rejection or
failed sample does not incur all that work. `_pathCached` is reused; one must
not claim that every call allocates a whole new NavMeshPath.

`Fight` (`BotDogFight.cs:138-170`) is the actual movement consumer. At most once
per second it checks a retreat path and issues `GoToPoint`; otherwise it shoots
in place or pursues its enemy. `DogFightNode.UpdateNodeByBrain`
(`../_decompiled/DogFightNode.cs:19-49`) calls it for the `dogFight` decision.
Counting `ManualUpdate` calls alone would not prove that a bot retreated, or
even that a NavMesh query happened.

## Pass 2: infected decisions and consumers

`../_decompiled/StandartBotBrain.cs:185-194` gives the four generic infected
roles `InfectedLayersStrategy`. Infected Tagilla instead receives the ordinary
Tagilla boss strategy and must be excluded. Difficulty maps to Slow/easy,
Fast/normal and Shooting/hard (`InfectedLayersStrategy.cs:149-158`). Arena's
`BurstSpawnService.SpawnInfected` admits only the four generic roles and
normal/hard (`ArenaMode.Client/Bots/BurstSpawnService.cs:898-926`).

`InfectedLayersStrategy.cs:77-99` registers wait, fight requests, peaceful
requests, attack, target search and patrol layers. It adds no ordinary human
combat layer. Its event priorities are disabled (`:181-184`). Nevertheless,
the active layer is not necessarily the attack layer, and mods can add or
replace layers. Role or difficulty alone is insufficient evidence for a skip.

`../_decompiled/InfectedAttackLayer.cs:46-159` selects:

- Fast: `runToEnemyZigZag` or `oneMeleeAttack`, plus a timed `holdPosition` after
  an attack.
- Shooting: `shootFromPlace` when a visible, shootable target is within 15 m;
  otherwise `attackMoving`, or `goToEnemyZigZag` if it cannot shoot.

The attack layer does not select `dogFight`. Its explicit end handlers for
zigzag movement, shooting, attack movement and hold do not read DogFightState
(`:116-134`, `:168-200`, `:231-252`). The melee handler delegates to
`BaseLogicLayer.EndOneMeleeAttack` (`:202-218`), but that base implementation
also reads only target/melee data (`../_decompiled/BaseLogicLayer.cs:447-463`).
The matching native nodes do not call `DogFight.Fight`: `OneMeleeAttackNode`,
`RunToEnemyZigZag`, `GoToEnemyZigZag`, `ShootFromPlace`, `AttackMoving`.

This supports the hypothesis that the *prepared DogFight state* is unused
while this exact native attack layer and these decisions own the bot. It does
not prove that all state can safely be removed for the entire bot lifetime:

- `BaseLogicLayer.ShallStartDogFight` reads the state (`:93-96`). Generic end
  handlers consume it for prone shooting, shooting from place, go-to-enemy,
  attack movement and run-to-cover (`:271`, `:348`, `:586`, `:596`, `:654`).
- The infected request layers inherit ordinary request implementations.
  Cover requests and inherited end handlers remain possible; a future layer
  switch can expose stale state even if the prior attack layer ignored it.
- A shooting infected's `attackMoving` node still performs actual cover
  selection (`../_decompiled/AttackMoving.cs:89-149`). Removing cover planning,
  `BotAttackManager`, the movement controller or danger handling would alter
  real shooting behavior. That is outside this candidate.
- PFT explicitly reads and writes follower DogFightState, including
  `../_reference/SPT-PitFireTeam-beta-0.10.3/client/BigBrain/FollowerCombatCommon.cs:9863`
  and follower/boss components. PFT teammates and every human/boss brain must
  stay untouched; installed SAIN's separate DogFight actions are also not the
  native subsystem under review.

## Pass 3: available runtime cost evidence

The previous same-day 56-infected Factory captures are under
`artifacts/zombie-perf-20261009/{baseline,final}-profiler/`.
They do **not** contain a row for native `BotDogFight.ManualUpdate`,
`CheckSpaceForRetreat` or `Fight`. The curated native list confirms that
limitation (`../ModProfiler/Profiling/Instrumenter.cs:464-483`). A zero search
result here means uninstrumented, not zero execution or zero cost.

For scale only, the full 30-second windows measured the parent method:

| Capture CSV | Owner calls in window | Entire BotOwner update, ms/frame |
|---|---:|---:|
| baseline `modprofiler-20261009-134553.csv` | 137,592 | 0.90759 |
| baseline `modprofiler-20261009-134623.csv` | 141,008 | 0.93345 |
| final `modprofiler-20261009-140014.csv` | 145,264 | 0.72975 |
| final `modprofiler-20261009-140044.csv` | 150,080 | 1.06240 |

These are `calls_delta` and `ms_per_frame_delta`, not cumulative snapshots.
Each window has 56 owner updates per measured frame. Retreat is only a subset
of this parent time. Differing approach/melee convergence and other bot work
prevent treating the differences as retreat savings. The earlier run used
Fast infected only; it says nothing conclusive about Shooting infected.

## Pass 4: bounded measurement and candidate safety

A temporary probe can classify calls without per-event reflection or string
formatting. The actual decompiled public members are:

| Datum | Read-only access |
|---|---|
| Owner | `BotDogFight._owner` inherited from `BotData` |
| Role and difficulty | `owner.Profile.Info.Settings.Role/BotDifficulty` |
| Actual strategy | `owner.Brain.BaseBrain` and exact `InfectedLayersStrategy` type |
| Actual Slow/Fast/Shooting mode | `strategy.CurrentZombieMode`, after validating `_attackLayer` |
| Current layer | `strategy.CurLayer`; compare identity with `strategy._attackLayer` and exact layer type |
| Current decision | `owner.Brain.LastDecision` nullable enum; do not call name/ToString helpers |
| Planner state | `DogFightState`, `_pursuitInProgress`, `_posibleCheckCanGo` |
| Check success | `CheckSpaceForRetreat`'s original Boolean return value |

Use existing exact active Zombie raid/world/body admission as the model, never
just `IsInfected`. A read-only probe must return no Boolean prefix decision,
write no native state, swallow no native exception and retain no bot/world
references after raid end. Time the original call after classification and
before aggregation. Log only aggregates (for example every 30 s) with elapsed
seconds/frames, method calls/time, successful retreat checks, current mode,
attack-layer fraction, actual DogFight calls and exception counts. A Finalizer
must return the incoming exception unchanged.

`CheckSpaceForRetreat` can be nested inside ManualUpdate or Fight, or called
directly by `ShallStartCauseHavePlace`; do not sum all three as disjoint CPU
time or subtract every check from the parent. Timestamp instrumentation has an overhead
floor; a very small result is a reason to stop, not to claim a tiny FPS gain.
Measure both (a) approach into close melee and failed-retreat geometry and
(b) mixed/shooting infected at close and farther range. Compare same population,
map, camera and protection conditions; the pending-wave fix does not require
changing those AI settings. Remove the probe after collecting evidence.

If a later measurement proves enough repeated unused work, consider only a
candidate restricted to the exact native attack strategy/layer, known harmless
decision set and current live generic-infected body. Keep current dogFight
actions, request/custom/boss/PFT layers and missing/stale objects native. Its
design still needs an explicit state-lifetime argument and native transition
tests: returning false from a retreat check changes state, while skipping
ManualUpdate preserves possibly stale state. Neither is automatically an
equivalent optimization. Do not convert this assessment into a role-wide
ManualUpdate skip, a global NavMesh change, or a higher zombie cap.

Source/consumer/frequency review is complete in four passes. Parent review and
any new native measurements determine final delivery acceptance; this document
alone does not certify gameplay or a performance benefit.
