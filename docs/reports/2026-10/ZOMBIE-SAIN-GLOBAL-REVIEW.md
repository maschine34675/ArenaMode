# Zombie mode: SAIN global pair-data review

Reviewed 2026-10-09. Analysis only; no SAIN production hook, replacement DLL,
configuration change or new gameplay behavior is introduced by this review.

## Decision

Keep SAIN's global player registry, world tick, sound delivery and direction
job intact for this change. SAIN's per-bot infected exclusion does **not**
exclude infected from its global player data. There is real quadratic pair
work, including many currently unneeded infected-to-infected entries, but no
verified Arena-owned seam that removes only those entries while preserving
SAIN's full data contract. A sparse-pair implementation is a separate SAIN
design/compatibility change, not a safe extension of the current PFT fast path.

The existing repository instruction is also explicit: AGENTS.md says, "Never
patch SAIN or race its patch order." An Arena patch of SAIN's pair producer,
even an apparently small one, would cross that boundary. No such exception or
production implementation is assumed here.

## Binary and evidence binding

The installed binaries were read directly, not inferred from the reference
checkout:

| Installed artifact | ProductVersion | SHA256 |
|---|---|---|
| `D:\SPT41\BepInEx\plugins\SAIN\SAIN.dll` | `4.5.1+2d1d9257dbb3af535e9189e6181311f6355aec27` | `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489` |
| `D:\SPT41\BepInEx\plugins\pitFireTeam\pitFireTeam.dll` | `0.10.3` | `0F875F824215F68A47FFD9183FB0BDD92CBA72762E1702F458A0EE614CC6DBB9` |

Private inspection material is under
`artifacts/zombie-voice-pft-20261009/sain-analysis/` (ignored):

- `installed/`: fresh SAIN DLL decompilation with ILSpy 11.0.0.9375 and the
  installed EFT managed directory as its reference path; no decompile errors.
- `installed-pair-path.il`: raw installed IL for `OtherPlayersData` construction,
  `GetOrAddData` and removal; `PlayerTickData.Prepare/Execute/ReadData`;
  `OtherPlayerLoSData` construction; player initialization, bot admission and
  `GameWorldComponent.UpdatePlayerSoundCache`. These confirm the critical
  allocation, ordering and filter claims below.
- `pft-installed/`: installed PFT decompilation. One unrelated gestures-menu
  method failed C# reconstruction; its raw IL was separately inspected and
  retained as `PftCreateGestures.il`. It contains menu construction, not pair
  access. A full installed PFT IL symbol search found zero occurrences of
  `OtherPlayersData`, `OtherPlayerData`, `GetDistanceToPlayer`,
  `IsPlayerInRange`, `FindClosestHumanPlayer`, `OnBulletFlyBy` or
  `PlayerDistanceData`; a positive `SAIN.SAINEnableClass` control found five.
  This is a direct-symbol/string check, not proof against arbitrary third-party
  reflection or a certification of every PFT method.

The relevant control flow was compared with
`../_reference/SAIN-master/SAIN/Classes/Player/OtherPlayersData.cs`,
`Classes/BotManager/Jobs/DirectionDataJob.cs`, `Components/GameWorldComponent.cs`,
`Components/PlayerComponent.cs`, and the consumer files named below. The pair
creation, iteration and sound-routing behavior agrees for the inspected
methods. This is not a claim that the entire reference checkout matches the
installed release. Third-party source is inspection material only; no source
or DLL is copied into Arena's product.

## Producer and lifetime

1. `PlayerSpawnTracker` subscribes to native `GameWorld.OnPersonAdd` and creates
   a SAIN `PlayerComponent` for each admitted native `Player`. This registry is
   separate from the registry of SAIN-controlled bots.
2. `PlayerComponent.Init` creates `OtherPlayersData` before its later
   `BotSpawnController.AddBot` path initializes the SAIN wrapper's `BotOwner`
   and `IsAI`. Only an admitted SAIN bot later receives `BotComponent` and
   `IsSAINBot = true`. Consequently, `!IsSAINBot` during pair construction is
   not a reliable final classification as an excluded infected.
3. `OtherPlayersData` subscribes to player-added/removed events and immediately
   adds existing players. `GetOrAddData` excludes null, self and invalid/same
   profile IDs, but has no role, faction, enemy or SAIN-ownership filter.
   Each directed pair exists in `DataDictionary`, `DataHashSet` and `DataList`.
   A different body for the same profile replaces and disposes the old entry.
4. Each `OtherPlayerData` creates distance data and `OtherPlayerLoSData`. The
   latter allocates two persistent native raycast arrays and managed collider
   data even when that pair never becomes a SAIN enemy. This establishes
   allocation opportunity; it does not measure allocation bytes or savings.
5. `DirectionDataJobLoop` traverses the global `AlivePlayerArray`, prepares all
   non-null wrappers with pair data, schedules a job, yields a frame, reads its
   output and then yields again. `PlayerTickData.Prepare` allocates a temporary
   direction array sized to the entire owner's `DataList` and retains an
   aligned list of the pair objects. `ReadData` writes through those retained
   references and disposes the temporary array. It does not consult current
   role/exclusion status at readback.

For a settled registry containing one wrapper per living player, the theoretical
directed pair count is `N * (N - 1)`. With 56 infected and one human, this is
3,192 pairs, of which 3,080 are infected-to-infected; preserving all cross pairs
leaves 112. With 56 infected, one human and four PFT companions, it is 3,660,
of which the same 3,080 are infected-to-infected; 580 remain. These are
combinatorial examples, **not measured registry sizes or CPU savings**. A body
swap, late classification or another registered actor must be counted live.

## Consumers that must survive

| Consumer in installed SAIN | Pair direction and consequence |
|---|---|
| `EnemyListController` / `Enemy` | `GetOrAddData(enemyComponent)` provides the SAIN bot-to-enemy record. `Enemy.UpdateDistAndDirection` uses its distance/direction and writes the native `EnemyInfo` mirror. Keep every PFT/SAIN companion-to-infected pair. |
| `GameWorldComponent.UpdatePlayerSoundCache` | For each active sound source, enumerates its pair hash set and forwards cached sounds only to active `IsSAINBot` listeners. Uses pair distance and the 100 m footstep / 500 m gunfire prefilters. Keep infected-to-PFT/SAIN pairs; excluded infected are still sound sources. |
| `GameWorldComponent.TrackBullet` | Enumerates the shooter's dictionary, selects any active AI in the forward direction, uses target position and raises `RegisterFlyBy`. Its target filter is **not** restricted to SAIN bots. A generic infected-to-infected pair can therefore still be read here. |
| `HearingInput.OnBulletFlyBy` | A SAIN listener subscribes to the public fly-by event, checks/adds the source as enemy and reads listener-to-source distance. Both shooter/listener directions matter for cross pairs. No native-infected SAIN hearing subscriber was found, but the event is public. |
| `GrenadeController` | Uses thrower-to-other distance to select nearby SAIN bots for grenade tracking. Keep human/infected/companion-to-SAIN listener data when such an action exists. |
| `SAINAILimit` / `PlayerSpawnTracker.FindClosestHumanPlayer` | Sorts an owner's pair list and finds the nearest human. Removing whole infected/player registry rows or human pairs changes the available data. |
| `SAINBotTalkClass`, `GroupTalk`, `BotSquadContainer`, `LightDetectionClass` | Human-distance talk gating, nearby squad checks and flashlight investigation also read pair distance. Generic public `GetDistanceToPlayer` returns `float.MaxValue` when an entry is missing; omission changes that API's meaning. |

PFT 0.10.3 does not need a direct pair-table reference to depend on these data.
Its installed `SainGoalEnemyBridge.TrySyncEnemyState` resolves SAIN's bot and
calls `EnemyController.CheckAddEnemy` and enemy-position updates. The installed
PFT core also hooks SAIN perception/aim behavior. Therefore "no PFT pair API
symbols" cannot justify deleting companion-related pairs. No separate PFT
SAIN-addon DLL was present in the inspected installation; a future addon or
another mod is an additional consumer audit, not covered by that absence.

Native infected sensing is a separate path: inspected
`../_decompiled/BotHearingSensor.cs` subscribes to
`GlobalEventDispatcher.OnSoundPlayed` and computes native distance/relationships;
`InfectedLookData` reads native group search points and enemy memory. Neither
uses SAIN's pair table. That supports reducing *proven unused* SAIN work, but
does not make native/global sound delivery dispensable.

The installed SAIN `PlayerTalkPatch` on `Player.Say` forwards excluded AI
phrases to `BotHearingClass.PlayerTalked`. That creates a conversation sound and
raises the `PlayerTalk` event. SAIN's separate `BotHearingClass.PlayAISound`
entry also schedules native bot sound delivery after 0.1 seconds. Suppressing
the world tick, removing infected wrappers, or dropping phrase calls would
alter perception and events. A playback-only voice budget must leave those
paths intact.

## What is dispensable, and why it is not removed here

Within the inspected SAIN implementation, the ordinary sound distributor skips
non-SAIN infected listeners before it reads their distance. Enemy calculations,
talk gating and squad helpers belong to SAIN bots. Thus a pair whose two bodies
are confirmed generic native infected and remain non-SAIN has little useful
SAIN work. No installed PFT direct consumer of that pair was found. This is the
credible optimization candidate, not a proven zero-reader contract: bullet
tracking still traverses such pairs and exposes a public event.

There is no existing role-aware exclusion callback between SAIN registration
and its global pair generator. The apparent small patches have material gaps:

- Returning null from `GetOrAddData` changes a public accessor used for
  on-demand enemy creation, and initialization runs before wrapper ownership
  has settled. A guard using only `IsAI`/`IsSAINBot` either misclassifies early
  players or lets the initial quadratic entries through.
- Removing entries after classification must preserve all three collections,
  body-replacement events, persistent-array lifetime, and already-prepared job
  references. Removing/disposal alone does not retire the aligned snapshot
  retained by `PlayerTickData` across the frame boundary.
- Filtering only `PlayerTickData.Prepare` would require replacing or modifying
  SAIN's allocation/count/index mapping. Leaving the original public records
  behind but no longer updating them exposes stale/default distance data.
- Omitting complete infected rows loses infected-to-companion audio and
  shoot/grenade source data. Omitting infected columns loses SAIN/PFT enemy
  perception. Disabling `WorldTickPatch` also loses transforms, equipment,
  sound-cache processing and bot-controller updates unrelated to the matrix.

Accordingly, no production skip is recommended in this task. An upstream
SAIN-owned sparse design could retain all human/companion/SAIN cross pairs,
make non-SAIN-to-non-SAIN data lazy, and create fresh data on any explicit
lookup. It must explicitly define fly-by delivery, unsettled ownership,
promotion to SAIN ownership, body replacement, pending jobs and disposal. An
Arena integration would need a documented producer API or a separately
authorized exact-version compatibility design; it should not rewrite SAIN's
job/coroutine or mutate its shared collections opportunistically.

A smaller upstream candidate is an early return in `UpdatePlayerSoundCache`
when the source's `AISoundCachedEvents.Count` is zero: the current code still
walks possible listeners even though its inner event loop has no work. This
does not require removing players or pair records. It remains only a proposal:
the empty-cache frequency and avoided cost were not measured, and an Arena
prefix on SAIN is outside the current boundary.

## Measured cost and next evidence

The earlier 56-infected solo Factory captures are available under
`artifacts/zombie-perf-20261009/`. Their 30-second CSV **delta** windows give:

| Window ending (local CEST) | WorldTickPatch, inclusive ms/frame | DirectionDataJobLoop, inclusive ms/frame |
|---|---:|---:|
| Baseline 13:45:53 | 0.14919 | 0.16120 |
| Baseline 13:46:23 | 0.13936 | 0.15213 |
| Final 14:00:14 | 0.18601 | 0.13893 |
| Final 14:00:44 | 0.13813 | 0.13681 |

These values were recalculated by reading the existing CSV columns. The
initial zero-frame exports and final partial tail windows are excluded from
this table. `WorldTickPatch` includes required player/bot work and sound
distribution; the coroutine timer covers its preparation/scheduling/readback
segments, not an isolated infected-pair arithmetic benchmark. Neither row is
the saving from a pair filter. Pair-count reductions cannot be applied as a
percentage to either inclusive timer. These captures also have no PFT
companions and no experimental SAIN variant.

The next useful investigation, if desired, is a behavior-preserving capture of
live pair counts, settled ownership, empty sound-cache frequency, pair
preparation/readback cost and allocations, first solo and then with four PFT
companions. Any later sparse implementation needs native checks for companion
acquisition/hearing, shooting infected and fly-bys, nearby sound/phrase events,
late spawns, deaths, body swaps, wave restoration, ordinary raids and teardown.
Only a source-bound A/B can establish an actual saving. None is claimed here.

## Review completion

Four passes completed: producer/consumer inventory; installed-binary and native
cross-check; adversarial review of early ownership, indirect PFT consumers and
pending job lifetime; final evidence/measurement wording audit. This leaf's
deliverable is the bounded recommendation and its evidence. No client build,
deployment or new GPU acceptance is part of this analysis leaf.
