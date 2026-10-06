# Zombie Mode (v0 prototype)

## Purpose and v0 scope

Zombies is Arena's fifth mode: solo, endless waves of EFT's own infected
against the local player, instant respawn, Arena kits, no PitFireTeam, no
loot. It does not invent a zombie system — it reactivates one EFT already
ships (the Halloween-2024 infection event) for exactly one armed raid, drives
its spawn count and pacing itself instead of the native crowd spawner, and
keeps everything else about Arena (the sacred profile, bit-identical normal
raids, Arena kits) unchanged. See `ArenaModeCatalog.ZombieId` ("ZOMBIES" on
the map screen) and `ArenaPopulationPolicy.Zombie`.

v0 is deliberately narrow. In scope: the four GENERIC infected roles
(`infectedAssault`/`infectedPmc`/`infectedCivil`/`infectedLaborant`) at
`normal`/`hard` difficulty only, one endless wave director, a HUD status
line, Arena's assist/scoreboard/corpse-cleanup plumbing extended just enough
to not misbehave under it, and the server-side clone edits the native event
needs. Out of scope is listed in its own section below — read it before
assuming a native Halloween behaviour (crowd spawns, the boss zombie,
announcer lines) is reachable here.

## The native system this mode reactivates

EFT 0.16.9.5 ships a complete "Halloween 2024" infected-bot event, off by
default on SPT and effectively unused by any other Arena mode. Every fact
below is decompile-verified against `D:\SPT41\Development\_decompiled`; file
paths are relative to that root unless marked `EFT\`.

- **Infected roles and brain.** Roles 60-64 are infected
  (`WildSpawnTypeExtension.IsInfected`, `WildSpawnTypeExtension.cs:540-547`:
  true for `infectedCivil`/`infectedLaborant`/`infectedAssault`/`infectedPmc`
  and the boss role `infectedTagilla`). `StandartBotBrain`'s role switch
  (`StandartBotBrain.cs:185-189`) routes all four generic roles to
  `InfectedLayersStrategy`.
- **Impossible throws; difficulty picks melee vs. shooting.**
  `InfectedLayersStrategy`'s constructor (`InfectedLayersStrategy.cs:77-99`)
  calls `GetZombieModeByDifficulty` (149-158), a switch with NO default case
  for three values and a `throw new ArgumentOutOfRangeException()` for
  everything else: `easy` → `Slow` (melee), `normal` → `Fast` (melee),
  `hard` → `Shooting` (draws the profile's PM pistol), `impossible` → throws
  during bot construction. This is exactly why `ZombieRoleMix` only ever
  produces `normal`/`hard` and why `BurstSpawnService.SpawnInfected` rejects
  anything else at the door (`BurstSpawnService.cs:913-918`) rather than let
  a bad draw reach this constructor.
- **The construction-order bug.** `BotsController`'s constructor passes its
  OWN `_botSpawner` field into `new BotsEventsController(...)` at
  `EFT\BotsController.cs:211` — six lines before that same field is actually
  assigned, at `EFT\BotsController.cs:236`. `BotsEventsController`'s own
  constructor (`BotsEventsController.cs:28-37`) immediately builds
  `BotHalloweenWithZombies = new BotHalloweenWithZombies(this, botsList,
  spawner, events?.Halloween2024)` at line 35 — UNCONDITIONALLY, every raid,
  whether or not the Halloween flag is ever turned on — handing it that same
  still-null spawner. `BotHalloweenWithZombies._spawner` is a public
  `[NonSerialized]` field (`BotHalloweenWithZombies.cs:67`); its own
  `Activate()` (142-158), gated on the flag and invoked from
  `BotsController.Init()` at `EFT\BotsController.cs:271` inside the same
  loading call, calls `AddBackupProfiles()` (188-211), whose body
  dereferences `_spawner.AddToTargetBackup(...)` for 4 roles × 3
  difficulties × 10 — the exact null reference the flag's first-ever flip-on
  would hit if nothing repaired the field first. `ZombieNativeEventBridge`'s
  hook 1 (below) is that repair.
- **The 498-profile preload.** `LocalGame.vmethod_1` (`EFT\LocalGame.cs:
  161-200`), the loading-time method that also starts `BotsController.Init`,
  unconditionally calls the STATIC `BotHalloweenWithZombies.
  GetProfilesOnStart()` at lines 166-169 whenever
  `Location.Events.Halloween2024.InfectionPercentage > 0` — independent of
  the `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` flag entirely, and strictly BEFORE
  `BotsController.Init()` (called at line 184, which is where the
  construction-order bug above actually lives). The native body
  (`BotHalloweenWithZombies.cs:392-407`) is a fixed list of 10
  `CountTypeBotWave` entries across all four roles and three difficulties
  (`easy`/`normal`/`hard`, including an `easy` share nothing in v0's role mix
  ever draws) totalling **498** profiles, which `BotProfileClient.
  TryLoadBotsProfilesOnStart` then requests from the server, each row capped
  at `Core.LOCAL_BOTS_COUNT` (100). This is the dump
  `ZombieNativeEventBridge`'s hook 3 replaces with Arena's own small opening
  wave.
- **The inert 8-attacker throttle.** `BotHalloweenDistributions`
  (`BotHalloweenDistributions.cs`) defines `MAX_BOT_PER_ENEMY = 8` (line 31)
  and, every 10 seconds (`AIPeriodAction`, line 39), sorts each enemy's
  attacking infected by distance and calls `InfectedLayersStrategy.
  SetCanAttack(false)` for every one past the 8 closest
  (`UpdateCanAttack`, lines 47-76). `SetCanAttack(false)` activates
  `WAIT_LOGIC` (layer priority 5, `InfectedWaitLayer`) — but
  `InfectedWaitLayer.ShallUseNow()` (`InfectedWaitLayer.cs:50-53`)
  unconditionally `return false;`, so the AI core's own layer-selection
  logic can never actually hand control to that layer. The throttle is real
  source, wired correctly, and permanently inert: an infected past the 8th
  nearest is marked "cannot attack" and attacks anyway, because the one
  mechanism meant to hold it back can never be selected. v0 does not rely on
  or work around this; `Settings.ZombieMaxAlive` already bounds total
  concurrent infected independently of per-target attacker counts.
- **Pursuit and the call relay.** `InfectedAttackLayer`'s own sighting path
  reaches `BotHalloweenWithZombies.OnSeeEnemy`/`OnGetHit` →
  `StartPursuit` (`BotHalloweenWithZombies.cs:348-385`), which opens one
  `BotHalloweenEventPursuit` per distinct target and enrolls every
  currently-infected bot. `BotHalloweenEventPursuit.CheckOnVisual` (93-116)
  grows its own call radius by `ZombieCallDeltaRadius` every
  `ZombieCallPeriodSec` up to `ZombieCallRadiusLimit` and, for every
  enrolled zombie within that radius, fires `HalloweenGameEvent.Call`, which
  `InfectedLayersStrategy.Call` (160-168) turns into
  `_owner.BotsGroup.AddPointToSearch(positionToCheck, 1000f, _owner)` — the
  exact call `ZombieSpawnService.OnZombieArrived`/`NudgeIdleZombies` also
  make directly on arrival/idle, so a freshly spawned zombie is aware of the
  player whether or not this native relay ever successfully arms this raid.
- **The crowd spawner is neutralized, not merely configured low.**
  `BotHalloweenCrowdSpawn.CalcBotsCanSpawnedPerCrowd` (182-193): with
  `InfectionPercentage >= 100` (which `ZombieLocationSettings` always writes)
  it returns `MaxCrowdAttackSpawnLimit` directly (line 190); the server
  clone fixes that to 0, so `BeginCrowdSpawn` (176-180) sets
  `spawnsRemains = 0` and the crowd spawner's own spawn loop never runs.
  `CrowdsLimit = 0` and an hour-long `CrowdCooldownPerPlayerSec` are
  belt-and-suspenders on top, in case a future build changes what a zero
  `MaxCrowdAttackSpawnLimit` does.
- **`TeleportBotNearToTarget` degenerates to self-relative jitter.**
  `InfectedLayersStrategy`'s constructor calls `GetNearestPursuitCenter`
  then `TeleportBotNearToTarget(nearestPursuitCenter)` unconditionally
  (`InfectedLayersStrategy.cs:95-96`). `GetNearestPursuitCenter`
  (`BotHalloweenWithZombies.cs:213-247`) falls back to
  `owner.Position` — the bot's OWN, just-assigned spawn position — whenever
  no `BotHalloweenEventPursuit` with `TargetAllowToSpawnCrowd` exists yet,
  which is true for every opening-wave spawn and for the whole raid whenever
  the native event never armed. `TeleportBotNearToTarget` (115-147) then
  computes `(owner.Position - point).normalized`, which is the zero vector
  when `point == owner.Position`, and relocates the bot to a semi-random
  cover point near that same degenerate direction — not toward the player at
  all. `ZombieNativeEventBridge`'s hook 5 (below) suppresses this call
  outright for a live Zombie raid so it can never defeat
  `BurstSpawnService.SpawnInfected`'s own 25-70 m spawn ring.
- **Body model: `UseSimpleAnimator`, not the role.** `LocalPlayer.cs:52`
  (and `EFT\Interactive\Corpse.cs:120` for the corpse) select
  `InGameBundles.ZOMBIE_BUNDLE_NAME` purely from
  `profile.Info.Settings.UseSimpleAnimator`, a plain bool on the profile —
  nothing in this mode sets it directly; it is carried by the infected
  profile data the server returns for `infected*` bot types
  (`SPT_Data\database\bots\types\infected*.json`), the same way every other
  field on a generated infected profile is.
- **The flag itself.** `BotInternalSettingsController.Core.
  ACTIVE_HALLOWEEN_ZOMBIES_EVENT` is a public instance field on a public
  static singleton (`BotGlobalsCoreSettings.cs:308`, default `false` — no
  initializer on a `bool`), read by `BotsEventsController.Activate()`/
  `ManualUpdate()`/`SpawnAction()` (`BotsEventsController.cs:39-57,59-69,
  71-77`) to decide whether `BotHalloweenWithZombies` ever runs at all. It
  is absent from SPT's shipped `core.json`, so it is `false` on every
  ordinary install.

## Architecture

**Server.** `ArenaLocationMutator.Mutate` calls `ZombieLocationSettings.
Apply(location)` on the already-cloned `LocationBase` (never the shared
template) only for `ArenaPopulationPolicy.Zombie`
(`ArenaLocationMutator.cs:174-179`), on top of the same quiet-clone branch
every `OwnsEntirePopulation` mode already takes (waves emptied, bosses
silenced, ABPS window closed). `ZombieLocationSettings` is one pure, static
type (`ArenaMode.Server/Services/ZombieLocationSettings.cs`) that writes
`Location.Events.Halloween2024` and raises `BotMax`/`BotMaxPvE`/
`MaxBotPerZone` to a fixed `MinimumBotCap = 60`; every constant it writes
carries the decompiled fact that makes it matter in its own doc comment
(`InfectionPercentage`/`MinInfectionPercentage` = 100 so `LocalGame.
vmethod_1`'s preload gate fires; `MaxCrowdAttackSpawnLimit`/`CrowdsLimit` = 0
to neutralize the crowd spawner; `MinSpawnDistToPlayer` = 25 kept only as a
fallback for the one case hook 5 below cannot cover; `TargetPointSearchRadiusLimit`
= 400 so `InfectedTargetLayer.ShallUseNow` stays usable on every map in
scope; `ZombieCallRadiusLimit`/`Delta`/`Period` = 400/50/1 so a sighting
reaches the whole map within 8 seconds; `DefaultInfectedLookCoeff` floors a
missing-or-zeroed map value to native 1.0 without overwriting an
intentionally tuned one; `DefaultSpawnParamRole`/`Difficulty`/`Weight` give
`BotHalloweenDistributions` a sane non-empty default it can never actually
use, since the crowd path is already dead). `Apply` never shares this block
with any other mode's clone.

**Client**, all in `ArenaMode.Client/Modes/` and `ArenaMode.Client/Bots/`:

- **`ZombieMode`** (`Modes/ZombieMode.cs`) is the `IArenaMode` shell:
  freezes `ZombieWavePolicy.Config` once at `OnMatchStart` (never re-reads
  `Settings` afterward — the same frozen-at-start convention as
  `TeamDeathmatchMode`/`RoundsMode`), drives `ZombieWavePolicy.Evaluate`
  every 0.1 s with the real clock and `ZombieSpawnService`'s real
  alive/pending counts, turns a "spawn one now" into a `ZombieRoleMix.Next`
  draw plus a `ZombieSpawnService.TryRequest` dispatch, and nudges idle
  zombies toward the player every 5 seconds independently of whether the
  native pursuit relay ever armed.
- **`ZombieWavePolicy`** (`Modes/ZombieWavePolicy.cs`) is a pure state
  machine with no EFT/Unity types (linked directly into `ArenaMode.Tests`):
  wave `n` has `WaveStartSize + WaveGrowth*(n-1)` infected; one spawn request
  goes out every `SpawnIntervalSeconds` while `alive+pending` stays under
  `min(MaxAlive, remainingToSpawn + alive)`; a wave ends only once everything
  in it has been requested AND nothing from it is alive. Two safety valves
  were added in review (see "Fixes from review" below): a
  `StuckWaveTimeoutSeconds` watchdog for a wave whose tail never clears, and
  `ReportFailedDispatch` to undo an optimistic spawn-slot commit that the
  real dispatch never actually fulfilled.
- **`ZombieRoleMix`** (`Modes/ZombieRoleMix.cs`) is the pure, deterministic
  role/difficulty draw: `infectedAssault` 50% / `infectedPmc` 25% /
  `infectedCivil` 15% / `infectedLaborant` 10%, `normal` unless an
  independent `ZombieShooterPercent` chance rolls `hard`.
- **`ZombieProfilePlan`** (`Modes/ZombieProfilePlan.cs`) is the pure
  apportionment layer: `OpeningProfiles` turns the same weight table into
  the native preload's opening-wave list (replacing the 498-profile dump),
  and `BackupTargets` is what gets registered with the native backup loader
  instead of the skipped `AddBackupProfiles` order, floored to
  `MinimumBackupCount = 4` because `BotProfileBackuploader`'s own top-up only
  fires once its deficit exceeds 3 (`BotProfileBackuploader.cs:144`,
  `num2 - 3 > 0`).
- **`ZombieSpawnService`** (`Bots/ZombieSpawnService.cs`) is the seam
  between the pure policy and the real native spawn path: `TryRequest` calls
  `BurstSpawnService.SpawnInfected`, `CountAliveInfected`/`NudgeIdleZombies`
  read `BotsController.Bots.BotOwners` directly, `OnZombieArrived` gives a
  freshly spawned bot the same group awareness `ArenaHostility.
  ReportPlayerTo` gives any other Arena bot plus the native pursuit's own
  `AddPointToSearch` call, and `RetireAllAliveInfected` administratively
  kills whatever the stuck-wave watchdog needs cleared, through
  `ArenaAdministrativeDeathGate` so it is never scored as a player kill.
  `BurstSpawnService.SpawnInfected` itself (`Bots/BurstSpawnService.cs:
  903-944`) rejects anything but the four generic infected roles and
  `normal`/`hard` difficulty at the door, then reuses `SpawnPair`'s existing
  25-70 m point ring and `SpawnBotsInZoneOnPositions` dispatch exactly like
  the ambient burst pump and `SpawnSquad` do.
- **`ZombieNativeEventBridge`** (`Bots/ZombieNativeEventBridge.cs`) is five
  independently-`TryEnable`d Harmony hooks, each failing closed internally
  (an exception inside a hook body leaves the native event off, never a
  half-armed or changed non-Zombie raid):

  | # | Target | Gate | What it does |
  |---|---|---|---|
  | 1 | `BotsEventsController.Activate()` prefix | `IsArmedZombieLoading` (+ unconditional restore check, see below) | Repairs `BotHalloweenWithZombies._spawner`/`._crowdSpawn._spawner` from the controller's own (by-now-assigned) `BotSpawner`, saves the flag's prior value, then — only if the repair succeeded — flips `ACTIVE_HALLOWEEN_ZOMBIES_EVENT` on. |
  | 2 | `BotsEventsController.Dispose()` postfix | none (acts only if hook 1 saved a value) | Restores the flag at raid teardown (`BotsController.StopGettingInfo` → `EventsController.Dispose()`, `EFT\BotsController.cs:643`). |
  | 3 | static `BotHalloweenWithZombies.GetProfilesOnStart()` prefix | `IsArmedZombieLoading` | Returns `ZombieProfilePlan.OpeningProfiles` instead of the native 498-profile list; also the one place that snapshots `ZombieWaveStartSize`/`ZombieShooterPercent` for the whole raid, since it fires before hook 1 in `LocalGame.vmethod_1`. |
  | 4 | instance `BotHalloweenWithZombies.AddBackupProfiles()` prefix | `IsArmedZombieLoading` | Skips the native 120-profile standing order; `ZombieSpawnService.RegisterBackupTargets` registers Arena's own, much smaller targets at match start instead. |
  | 5 | instance `InfectedLayersStrategy.TeleportBotNearToTarget()` prefix | `ArenaSession.IsActive` + live mode == Zombie | Skips the native ctor-time teleport outright for any bot spawned during a live Zombie raid, since it decompile-verified collapses to self-relative jitter with no pursuit established yet. |

  Hooks 1/3/4 gate on `ArenaPopulationPolicy.IsArmedZombieLoading`
  (`ArenaMode.Shared/ArenaPopulationPolicy.cs:119-124`: armed + exact map
  match + mode == `"zombie"`), the SAME loading-time exception class
  `ArenaProfilePreloadPatch` already documents in `AGENTS.md`: all three
  fire inside `LocalGame.vmethod_1`, before `BotsController.Init()` has even
  run, which is before `ArenaSession.IsActive` can have latched — so they
  cannot gate on `IsActive` the way every live-raid patch does, and instead
  gate on the same armed+map check the kit/profile preload uses. Hook 5 is
  different in kind (gates on `IsActive`, not the loading-time check)
  because it only ever matters once bots are actually spawned into a
  running raid, never during loading.

  **Flag restore paths.** The flag has three independent restore paths, not
  one: hook 2 (`Dispose()`, the normal end-of-raid path); `ArenaSession.
  OnRaidEnded`'s own safety-net call to `ZombieNativeEventBridge.
  RestoreFlag()` (idempotent — whichever of the two runs first clears the
  saved value so the second is a no-op); and, since the P0 fix below, hook
  1's own Prefix, which ALSO restores a still-saved flag from a PREVIOUS
  raid before doing anything else, the moment it observes one on ANY later
  raid's `Activate()` call — because `BotsController.Init()` calls
  `Activate()` unconditionally, every raid, Arena or not, Zombie or not.

## Settings and defaults

All in the `"Zombie"` BepInEx config section
(`ArenaMode.Client/Config/Settings.cs`), frozen at match start unless noted:

| Setting | Default | Range | Notes |
|---|---|---|---|
| `MaxAlive` | 20 | 4-56 | Ceiling kept at 56, not 60: the server clone's own `BotMax`/`MaxBotPerZone` floor (`ZombieLocationSettings.MinimumBotCap`) is a fixed 60, and the client cap this feeds (`ArenaBotCapPatch`: `MaxAlive + 2`, up to 58) must stay under it. |
| `StuckWaveTimeoutSeconds` | 100 | 0-600 | Watchdog ceiling; 0 disables it (wave then waits for every infected to die naturally, with no recovery if one is unreachable). |
| `WaveStartSize` | 8 | 1-100 | Wave 1's size. |
| `WaveGrowth` | 4 | 0-50 | Extra infected per wave over the last. |
| `IntermissionSeconds` | 10 | 0-60 | Pause between a cleared wave and the next. |
| `SpawnIntervalSeconds` | 1.0 | 0.2-10 | Seconds between individual spawn requests while filling toward `MaxAlive`. |
| `ShooterPercent` | 0 | 0-100 | Chance each spawn is `hard`/Shooting instead of `normal`/Fast melee. |
| `ReplaySeconds` | (shared default) | — | KillcamReplay duration, same per-mode binding every other mode uses. |
| `ColoredTracers` | false | — | Independent of every other mode's tracer setting and of `Effects/TeamLasers`, which Zombie does not use at all. |

## What is deliberately NOT in v0

- **No bot movement/agility boost.** Infected use whatever
  `InfectedLayersStrategy.GetZombieModeByDifficulty` and the profile's own
  movement speed give them; `BOT-MOVEMENT-REVIEW.md`'s independent
  walk/sprint controls are not wired to Zombie.
- **No announcer lines.** Wave-start uses `ArenaMatch.ReportTextCallout`
  (the same plain callout other modes use for a brief HUD line), not the
  `AnnouncerExpansion`/awards pipeline.
- **No pickups.** `ArenaMode.Client/Pickups`' allowlist is
  `mode is RoundsMode || mode is TeamDeathmatchMode`; Zombie is excluded by
  construction, matching `PICKUPS-REVIEW.md`'s documented restriction.
- **No lasers.** `ArenaTeamLaserColors` explicitly excludes Zombie from its
  solo-mode cosmetic recoloring list (see its own in-code comment) — an
  unrecognized mode id falling through to "no lasers" is the intended
  behaviour here, not an oversight.
- **No PitFireTeam.** `ArenaModeCatalog.CanSelect` still only allows
  `TacticalId` when `tacticalOnly` is set; a selected full PFT roster
  continues to force protected Tactical and excludes every other mode,
  Zombie included.
- **No corpse-lifetime tuning beyond the one exemption change.** The only
  corpse-cleanup change this branch makes is dropping the "near the player"
  distance exemption to 0 for Zombie (see the fix list below); every other
  rule (ragdoll-settled, open-loot-window, held-interactable, own-corpse
  handling) is untouched.
- **No `infectedTagilla` (the boss zombie).** `WildSpawnTypeExtension.
  IsInfected` is also true for this fifth, pure-boss role, but it needs
  `BotBossSpawn`'s `SetBoss` initialization the way `SpawnManagedBot`'s
  dedicated boss branches provide — `BurstSpawnService.SpawnInfected`
  explicitly rejects it at the door (`BurstSpawnService.cs:905-906`), and
  `RoleClassifier.Label`'s `"ZOMBIE"` case still matches it (for a
  consistent kill-feed label if it ever appears some other way) without
  treating it as reachable from this mode's own spawn path.

## Test matrix and the fault controls actually demonstrated

Offline (`dotnet test ArenaMode.Tests`, no game required): **828 passed, 2
skipped (pre-existing, environment-gated, unrelated to Zombie), 0 failed**
— confirmed by re-running the suite for this document, with the
`artifacts/zombie-nodeploy/must-not-exist` no-deploy sentinel directory
absent both before and after. `ZombieWavePolicyTests.cs`,
`ZombieRoleMixTests.cs`, `ZombieProfilePlanTests.cs` and `ZombieServerTests.cs`
are pure/production-linked and carry no EFT/Unity dependency, matching this
repo's existing `ClientRules`-linked pattern (see `ArenaMode.Tests.csproj`).

Fault controls actually demonstrated (not merely asserted — a test proven to
fail against the bug it claims to catch, per `AGENTS.md`'s own convention):

- **`AnAliveButPermanentlyUnreachableInfectedWouldFreezeTheWaveWithNoWatchdog`**
  reproduces the wave-softlock bug directly: with the watchdog disabled
  (`StuckWaveTimeoutSeconds = 0`), one alive-but-stuck infected freezes
  `Phase` at `Wave` forever, exactly as the original P0 finding described.
- **`TheStuckWaveWatchdogForceClearsAndSignalsAdministrativeRetirement`**
  confirms the fix: once armed and past the configured ceiling, `Evaluate`
  force-clears the wave and returns `ForceRetireStuckInfected = true` exactly
  once.
- **`APermanentlyFailingDispatchDoesNotCountAsSpawnedOrSilentlyEmptyTheWave`**
  reproduces the optimistic-slot-commit bug: without `ReportFailedDispatch`,
  a persistently failing dispatch still drives `SpawnedThisWave` to the
  wave's full size and the wave "completes" with zero real zombies; the test
  fails at iteration 5 without the fix and passes with it.
- **`OnlyConfirmedDispatchesCountTowardTheWaveWhenSomeFail`** — a mixed
  fail/succeed sequence confirming only confirmed dispatches ever reduce
  `remaining`.
- **`ZombieServerTests.ZombieLeavesHostilityVanillaEvenUnderFfa`** proves the
  generic FFA hostility-rewrite branch would otherwise mutate an
  `infectedAssault` `AdditionalHostilitySettings` entry, by reference-equality
  and JSON-snapshot assertion against the clone.
- **`ZombieEnablesTheNativeInfectionEventOnTheClone`**,
  **`ZombieKeepsAMapsExistingCrowdAttackSpawnParamsAndLookCoeff`** and
  **`ZombieRaisesTheCloneBotCaps`** assert concrete field values on the
  server clone (not merely "does not throw"), including that an existing
  map's own `InfectedLookCoeff`/`CrowdAttackSpawnParams` survive `Apply`
  unchanged.
- **`WaveSizeGrowsLinearlyFromTheStartSize`**,
  **`SpawningStopsAtTheConfiguredMaxAliveEvenWithManyLeftToSpawn`**,
  **`TheWaveEndsOnlyOnceEverythingIsSpawnedAndNothingIsAlive`**,
  **`IntermissionLastsExactlyItsConfiguredDurationThenAdvancesTheWave`** and
  **`ZeroLengthIntermissionAdvancesAndSpawnsInTheSameCall`** cover the wave
  arithmetic's edge cases, including the bounded (depth-2) self-recursion a
  zero-length intermission or wave would otherwise require.
- `ZombieRoleMixTests` covers determinism-by-seed, divergence-by-seed, the
  null-`Random` guard, 0%/100% shooter edge cases, and distribution within
  tolerance over 20,000 draws.
- `ZombieProfilePlanTests` covers exact apportionment (counts sum back to
  the requested total across many sizes including 0, 1, 97, 100) and the
  `MinimumBackupCount` floor.

**Known test-coverage gap, by design, not an oversight:** `ZombieWavePolicy`
is intentionally oblivious to real dispatch outcomes (it only knows
"accepted the optimistic slot" vs. "a failure was reported back"), so no
test at that layer alone exercises `ZombieMode.Tick`'s actual role/difficulty
mapping and `ZombieSpawnService.TryRequest` call together with a permanent
dispatch failure; that integration is covered at the `ZombieWavePolicy`
contract boundary (the two tests above) rather than end-to-end through
`ZombieMode`, which has no EFT-free seam to test through.

## Fixes from review (summary; see commit messages for full reasoning)

The adversarial review that followed this implementation found two P0s, four
P1s and several smaller issues; every P0/P1 and every small-and-safe P2 was
fixed, one P2 (reusing `BurstSpawnService`'s shared per-bot log lines for a
pure verbosity concern) was explicitly skipped as disproportionate without a
GPU-measured baseline first, and the P3 notes were left untouched as
instructed:

- **P0 — native flag leak across raids.** Fixed by making hook 1's own
  Prefix a third, unconditional restore path (see "Flag restore paths"
  above).
- **P0 — wave softlock on a stuck zombie.** Fixed by the
  `StuckWaveTimeoutSeconds` watchdog and `RetireAllAliveInfected`.
- **P1 — native ctor-time teleport defeating the spawn ring.** Fixed by
  hook 5; this also resolved the related P1 (the `ZombieLocationSettings`
  doc comment that previously assumed the native teleport was a genuine
  homing move).
- **P1 — scoreboard entries never trimmed.** Fixed: Zombie's
  `SynchronizeScoreboard` branch now syncs against an empty roster (still no
  per-bot allocation) instead of returning unconditionally, so
  `TrimOrdinaryHistory` can reclaim the permanent one-life entries every
  killed infected leaves behind.
- **P1 — optimistic wave-slot commit silently emptying waves.** Fixed by
  `ZombieWavePolicy.ReportFailedDispatch`.
- **P1 — corpse cleanup's 15 m exemption defeated by horde melee.** Fixed:
  `CorpseCleanupService.ActiveMinDistanceMeters()` returns 0 instead of 15
  while Zombie is the active mode; every other mode is untouched.
- **P1 (corrected from P2) — pending-request ledger FIFO mismatch.** Fixed:
  `ZombieSpawnService`'s ledger is now keyed by a per-request id instead of
  position.
- **P2s fixed:** `ZombieMaxAlive`'s range ceiling lowered from 60 to 56 (see
  Settings table); the native opening-wave snapshot
  (`ZombieNativeEventBridge.TryGetOpeningSnapshot`) is now the one
  `ZombieMode.OnMatchStart` reads, instead of a second, later `Settings`
  read that a mid-loading F12 edit could desync from; `ZombieMode.Tick`'s
  wave evaluation (and the `BotOwners` scan/HUD string rebuild it drives) is
  throttled to 0.1 s instead of every rendered frame.
- **P2 skipped:** reducing `BurstSpawnService`'s shared per-bot Info
  logging — also used by the ambient burst pump and Team Deathmatch
  placement — for a pure log-verbosity concern, without first measuring
  actual line volume on the GPU host, as the finding itself recommends.

## Considered and dismissed

None. Every finding the review raised was either confirmed and fixed,
confirmed and explicitly skipped with a stated reason (above), or left as a
lower-severity note for later (the P3 items: the stale "four modes" assertion
in `tools/NativeModeSelectionChecks/Program.cs`, `CountAliveInfected`'s
zero-on-failure convention, and this document's own existence as the
requested v0 summary). No finding was refuted as a false positive.

## Known limitations and GPU playtest checklist

Everything client-side in this branch is Harmony patches against Unity/EFT
types that cannot load outside the game — per `AGENTS.md`, "for anything
client-side, verification is still a raid." This document and the offline
test suite are NOT a claim that any of the following has been seen live;
they are decompile cross-checking and clean builds only. The first in-game
test (prefer SPT-GPU-2) must confirm, in order:

1. **Spawning works and bodies look right.** Zombies spawn from wave 1
   onward, use the zombie body/animator (not a PMC model), and land roughly
   in the 25-70 m ring around the player rather than clustering at a map's
   bot-zone spawn points.
2. **No `InfectedLayersStrategy` exception.** No `ArgumentOutOfRangeException`
   from `GetZombieModeByDifficulty` (would mean an `impossible`-difficulty
   infected reached the native constructor) and no NRE from
   `AddBackupProfiles`/the crowd spawner (would mean hook 1's repair or hook
   4's skip did not actually install this build).
3. **No teleport oddity.** A freshly spawned zombie should hold its spawn
   point, not visibly snap or relocate right after arriving — confirms hook
   5 is actually suppressing `TeleportBotNearToTarget`.
4. **Native melee/shooting behaviour lands.** `normal` infected close and
   melee; any `hard` infected (if `ShooterPercent > 0`) draw and fire a PM
   pistol at range, per the native brain's own difficulty table. The first
   GPU raid (2026-10-06) passed every item on this list EXCEPT this one —
   zombies closed and swung but never landed a hit, root-caused to SAIN's
   own global `RunToEnemyUpdate` prefix; see "Native melee is suppressed by
   SAIN's global prefix" below for the fix and what the NEXT raid must show
   (an actual health drop, a melee kill in the killfeed, the
   `ZombieMeleePatch`/`ZombieMeleeDiagnostics` log lines).
5. **The wave HUD counts down and the wave ends.** `HudStatusLine` shows
   `WAVE n | m LEFT` decrementing as zombies die, reaches `WAVE n+1 IN Xs`
   during intermission, and actually advances to wave `n+1`'s first spawn.
6. **Player death/respawn does not stall the wave.** Dying and instantly
   respawning (Zombie's `HoldRespawn = false`) must not pause or reset wave
   progression — infected should keep dying/spawning through the respawn.
7. **The flag restores after the raid.** Leave the Zombie raid, then start
   an ORDINARY (non-Zombie) raid on the same game session without
   restarting: the BepInEx log must show no `"Arena zombie native bridge"`
   lines for that second raid, and no infected should appear in it.
8. **Performance notes to record.** Frame time/ms over the raid's length,
   peak concurrent bot count (compare against the configured `MaxAlive`),
   and corpse count over time (confirms `ActiveMinDistanceMeters()`'s zero
   exemption is actually letting the overflow pass trim zombie corpses
   instead of leaving them all exempt near the player).

Also worth exercising once wave 1 is confirmed: triggering the stuck-wave
watchdog live (strand one infected somewhere unreachable and wait past
`StuckWaveTimeoutSeconds`) and a `StuckWaveTimeoutSeconds = 0` control raid
to confirm the watchdog can indeed be disabled on request.

## Native melee is suppressed by SAIN's global prefix

**What the first GPU raid found (SPT-GPU-2, 2026-10-06 12:52-13:20, build
`1e76582`).** Every item on the checklist above except #4's melee half
passed: 8 infected spawned within 7s with zombie bodies
(`UsedSimplifiedSkeleton=true`), knives in hand
(`Player.HandsController is Player+KnifeController`,
`BotWeaponManager.IsMelee=true`, `Melee.MeleeWeaponEquipped=true`,
`Melee.KnifeController` set), they closed to melee range within ~40s, the
HUD counted waves down, the stuck-wave watchdog retired them after 100s and
the next wave followed, and no Error log line appeared anywhere. But **no
zombie ever damaged the player in 25 minutes** — health stayed full the
whole raid.

**Evidence (live inspection of a zombie standing 0.79m from the player,
agent `LastReason = "atk2"` — `InfectedAttackLayer`'s `oneMeleeAttack`
decision):**

- `BotDoorOpener`: `_lastFrameDoorInteractionStatus=CanRun`,
  `NearDoor=false`, `Interacting=false`, `_currentDoorLink=null`;
  `Mover.CurrentState=Default` — so
  `OneMeleeAttackNode.UpdateNodeByBrain`'s own `DoorOpen() == CanRun` gate
  passes every time.
- `GoalEnemy`: `Distance=0.79`, `IsVisible=true`,
  `PersonalLastSeenTime` within 1s of `Time.time`, `CanShoot=false`.
  `Physical.CanMeleeHit=true`, `StateIsSuitableForHandInput=true`,
  `KnifeController.CurrentOperation=Idling`.
- `BotMeleeWeaponData._nextTryHitTime == 0.0` the whole time,
  `LastTimeEnemyHit == -100000`, `ShallEndRun=false`: the native
  `RunToEnemyUpdate` (../_decompiled/BotMeleeWeaponData.cs:132-208) never
  executed its body — lines 165-169 would have set `_nextTryHitTime` on the
  very first call.
- `AICoreController._errorsPrinted == 0`: no exception was swallowed by the
  agent loop (AICoreController.cs:49-63) — this is not a crash, it is a
  skip.
- **Root cause.** SAIN (installed; its exclusion list — fetched from the
  server into the static `BotSpawnController.StrictExclusionList` — names
  `infectedAssault`/`infectedCivil`/`infectedLaborant`/`infectedPmc`/
  `infectedTagilla` by role, SAINServerMod/Generators/DefaultBotTypes.cs:
  102-109) patches `BotMeleeWeaponData.RunToEnemyUpdate` with a PREFIX
  (SAIN-master/SAIN/Patches/FixPatches.cs, `RunToEnemyUpdatePatch`):
  <code>if (SAINEnableClass.GetSAIN(profileId, out bot) &amp;&amp;
  bot.SAINLayersActive) { ...SAIN's own version...; return false; } return
  false;</code> — the trailing, UNCONDITIONAL `return false;` is reached for
  every bot `GetSAIN` returns false for, which is every excluded bot (SAIN
  never adds it to its own `_enabledBots` set —
  SAIN/Plugin/SAINEnableClass.cs:23-68,257-272), which is every infected bot
  in this mode. Harmony's own call to the ORIGINAL method is what that
  `return false;` skips — so the native body never runs, and neither does
  SAIN's replacement branch (gated on `SAINLayersActive`, which an excluded
  bot never has).
- **Native chain that must run for a swing**, confirmed end to end against
  the decompile: `OneMeleeAttackNode.UpdateNodeByBrain`
  (OneMeleeAttackNode.cs:10-16: `if (DoorOpen()==CanRun)
  _owner.WeaponManager.Melee.RunToEnemyUpdate();`) → `RunToEnemyUpdate`
  (faces the body part, sprints, every `TRY_HIT_PERIOD_FALSE=0.1s` calls
  `TryHit` — BotMeleeWeaponData.cs:276-290: `MeleeWeaponEquipped &&
  enemy.IsVisible && Time.time-PersonalLastSeenTime<0.2 &&
  KnifeController!=null` → `KnifeController.MakeKnifeKick()` — then path
  handling via `CanRunToEnemyToHit`/`GoToByWay`/`StopMove`).
  `MakeKnifeKick` on the simplified skeleton sets the animator triggers and
  the knife-collider hit window comes from the zombie animation events
  (Player.cs:18590-18610,17776-17777).

**The fix (`ZombieMeleePatch`, `ArenaMode.Client/Bots/ZombieMeleePatch.cs`).**
Does not patch SAIN (never touch a competing mod — AGENTS.md licence
rules) and does not try to out-race SAIN's own prefix by patch-registration
ORDER (rejected by design: fragile across Harmony/BepInEx load order). A
new prefix on `OneMeleeAttackNode.UpdateNodeByBrain` itself — one call site
entirely outside `BotMeleeWeaponData`, so SAIN's patch on the melee method
is never even reached — reproduces that node's own two-line body, but
drives the TRUE native `RunToEnemyUpdate` body through a HarmonyX reverse
patch (`Harmony.CreateReversePatcher`, confirmed present via Mono.Cecil
metadata inspection of the installed `0Harmony.dll`, version `2.9.0.0`,
which copies the original method's IL into a brand-new, patch-chain-free
`MethodInfo` — SAIN's prefix is never consulted, because nothing calls
through the original PATCHED method at all). A hand-written, line-cited
fallback reimplementation
(`ZombieMeleePatch.FallbackRunToEnemyUpdate`) covers the case where the
reverse patch cannot be prepared at runtime; either path is logged once,
and if even the fallback is unreachable the prefix stays inert (returns
true — today's vanilla/SAIN-suppressed behaviour) rather than half-driving
the node. The gating decision (exact Zombie raid + infected role + the bot
can actually melee right now) is `ZombieMeleeGate.ShouldDrive`, a pure
function with no EFT/Unity types, tested the same way as
`ZombieWavePolicy`/`ZombieRoleMix`/`ZombieProfilePlan`.

Cheap diagnostics (`ZombieMeleeDiagnostics`) log the first 3 native swings
and first 3 landed hits per raid at Info from
`ZombieSpawnService.OnZombieArrived`'s own per-bot subscription, and
`ZombieMode.OnMatchEnd` reports the raid's totals in one line.

**What the NEXT GPU raid must show** to confirm the fix: the player's
health actually drops while a zombie is in melee range; a zombie death from
melee appears in the killfeed/score as an ordinary `ZOMBIE` kill; no new
Error/Exception log lines; and the Info line from `LogPathChoiceOnce`
('...driving infected RunToEnemyUpdate through a HarmonyX reverse patch...'
or '...through the hand-written fallback...') confirms which path actually
ran, plus the per-raid swing/hit counts from `ZombieMeleeDiagnostics` and
`ZombieMode`'s own end-of-raid summary line.

## Double-Activate

The same GPU raid's log showed `ZombieNativeEventBridge` log
"Activate() fired again before the previous raid's flag was restored —
leaving the saved original value alone" 77ms after its own first "armed"
line, in the SAME raid (`BotsEventsController.Activate` apparently ran
twice during one raid's loading). Decompile tracing of every link in the
native chain that reaches it —
`TarkovApplication.LocalGameMatching` → `LocalGameCreate` →
`LocalGame.Run` → `BaseLocalGame.vmethod_1` → `BotsController.Init` →
`BotsEventsController.Activate` — found exactly ONE call site at every
single link inside `Assembly-CSharp`. That rules out a native, vanilla
double-call for an ordinary raid start; the repeat happens OUTSIDE that
chain, most plausibly an SPT-side or another installed mod's own Harmony
patch re-running part of raid loading. The exact caller is not pinned down
in this pass; a stack trace captured the next time the Info line below
fires would settle it, but that is left for whoever next needs the answer
rather than chased further here.

What the evidence DOES establish is that this exact repeat is harmless: the
77ms gap is far too short for a whole new raid (new scene, new `GameWorld`)
to have loaded, so it is the SAME raid's own `GameWorld` reactivating — and
that is exactly the identity check `ActivatePatch` now uses (see
`ZombieNativeEventBridge._savedFlagGameWorld`'s own doc comment) to log Info
instead of Warning for a same-`GameWorld` repeat, while a genuinely
different, earlier raid's stuck flag still logs the original Warning. The
restore semantics themselves are completely unchanged: `_savedFlag` is
still saved exactly once per unrestored window, and `RestoreFlag` still
clears it (and the new `_savedFlagGameWorld`) together, from whichever of
`Dispose()`/`ArenaSession.OnRaidEnded`/the next raid's own `Activate()`
reaches it first.
