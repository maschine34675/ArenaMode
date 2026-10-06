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
   *Confirmed 2026-10-06 (raid 1, `1e76582`): 8 infected spawned within 7 s,
   zombie bodies (`UsedSimplifiedSkeleton=true`), 25-70 m ring, BotZone.*
2. **No `InfectedLayersStrategy` exception.** No `ArgumentOutOfRangeException`
   from `GetZombieModeByDifficulty` (would mean an `impossible`-difficulty
   infected reached the native constructor) and no NRE from
   `AddBackupProfiles`/the crowd spawner (would mean hook 1's repair or hook
   4's skip did not actually install this build).
   *Confirmed 2026-10-06 (raids 1 and 2): no Error lines in either raid.*
3. **No teleport oddity.** A freshly spawned zombie should hold its spawn
   point, not visibly snap or relocate right after arriving — confirms hook
   5 is actually suppressing `TeleportBotNearToTarget`.
   *2026-10-06: the first-raid note in the section below lists this item as
   passed; the results document records no separate observation of it.*
4. **Native melee/shooting behaviour lands.** `normal` infected close and
   melee; any `hard` infected (if `ShooterPercent > 0`) draw and fire a PM
   pistol at range, per the native brain's own difficulty table. The first
   GPU raid (2026-10-06) passed every item on this list EXCEPT this one —
   zombies closed and swung but never landed a hit, root-caused to SAIN's
   own global `RunToEnemyUpdate` prefix; see "Native melee is suppressed by
   SAIN's global prefix" below for the fix and what the NEXT raid must show
   (an actual health drop, a melee kill in the killfeed, the
   `ZombieMeleePatch`/`ZombieMeleeDiagnostics` log lines).
   *Melee half confirmed 2026-10-06 (raid 2, `0c6fab3`): first native swing
   14:12:25 at 0.7 m via the HarmonyX reverse patch, first landed hit
   14:12:25.8, player health dropped, killfeed "ZOMBIE -> USEC . 1 m .
   Infectious strike", 2618 swings / 178 hits over the raid. The ranged half
   (`ShooterPercent > 0`) is still untested.*
5. **The wave HUD counts down and the wave ends.** `HudStatusLine` shows
   `WAVE n | m LEFT` decrementing as zombies die, reaches `WAVE n+1 IN Xs`
   during intermission, and actually advances to wave `n+1`'s first spawn.
   *Partly confirmed 2026-10-06: the HUD showed "WAVE 1 | 8 LEFT | 00:13" in
   raid 1 and counted down per kill in raid 2 ("WAVE 2 | 10 LEFT" after two
   kills), and later waves followed the 10 s pause; a wave ending through kills
   was NOT seen (the watchdog ended every wave).*
6. **Player death/respawn does not stall the wave.** Dying and instantly
   respawning (Zombie's `HoldRespawn = false`) must not pause or reset wave
   progression — infected should keep dying/spawning through the respawn.
   *Confirmed 2026-10-06 (raid 2): death #1 at 14:12:48, respawn pipeline
   0.20 s, 4 s spawn protection, the wave kept running (HUD "1 DEATHS ... WAVE
   1 | 8 LEFT").*
7. **The flag restores after the raid.** Leave the Zombie raid, then start
   an ORDINARY (non-Zombie) raid on the same game session without
   restarting: the BepInEx log must show no `"Arena zombie native bridge"`
   lines for that second raid, and no infected should appear in it.
   *Confirmed 2026-10-06: the flag read `false` after raids 1 and 2; the
   control raid at 14:16:40 (ordinary PMC, Factory, straight after raid 2)
   logged no Zombie/bridge lines and no errors, its bots ran SAIN's Combat
   Layer.*
8. **Performance notes to record.** Frame time/ms over the raid's length,
   peak concurrent bot count (compare against the configured `MaxAlive`),
   and corpse count over time (confirms `ActiveMinDistanceMeters()`'s zero
   exemption is actually letting the overflow pass trim zombie corpses
   instead of leaving them all exempt near the player).
   *Partly confirmed 2026-10-06 (raid 2, 12 zombies): 120 fps (8.3 ms mean),
   RAM 11.2 GB, managed 2.3 GB. Not yet measured: MaxAlive 40-56, corpse count
   over time, any raid longer than 3.9 min.*

Also worth exercising once wave 1 is confirmed: triggering the stuck-wave
watchdog live (strand one infected somewhere unreachable and wait past
`StuckWaveTimeoutSeconds`) and a `StuckWaveTimeoutSeconds = 0` control raid
to confirm the watchdog can indeed be disabled on request.
*2026-10-06: the watchdog fired under real conditions (raid 1 waves 1-4 after
100 s; raid 2 at 14:14:03 and 14:16:05) but only because the tester did not
kill the wave, not on a stranded infected; neither the stranded-zombie case
nor the `StuckWaveTimeoutSeconds = 0` control has been run.*

Open items after the two GPU-2 raids of 2026-10-06 (details in
`GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`):

- **Watchdog semantics.** 100 s without the wave ending is too short for real
  players: a running wave is cleared in the middle of the fight. Proposal: count
  the timer only while no infected has died for 100 s (progress instead of
  duration), or raise the default to 240 s. Then re-run a raid in which a wave
  ends through kills.
- A second `Activate()` per raid with no known caller (see "Double-Activate").
- Performance and corpses at `MaxAlive` 40-56.
- Ranged infected (`ShooterPercent > 0`), Labs/Labyrinth, the boss zombie and
  PitFireTeam remain out of scope for now.

## Native melee is suppressed by SAIN's global prefix

**What the first GPU raid found (SPT-GPU-2, 2026-10-06 12:52-13:11, build
`1e76582`).** Every item on the checklist above except #4's melee half
passed: 8 infected spawned within 7s with zombie bodies
(`UsedSimplifiedSkeleton=true`), knives in hand
(`Player.HandsController is Player+KnifeController`,
`BotWeaponManager.IsMelee=true`, `Melee.MeleeWeaponEquipped=true`,
`Melee.KnifeController` set), they closed to melee range within ~40s, the
HUD counted waves down, the stuck-wave watchdog retired them after 100s and
the next wave followed, and no Error log line appeared anywhere. But **no
zombie ever damaged the player in 19 minutes** — health stayed full the
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

## v1 (2026-10-06 evening): progress-based watchdog, continuous waves, refill, own gear

Driven by the user's own post-playtest feedback on v0 ("funktioniert gut") — four
requests, implemented as four stages on `feature/zombie-prototype` plus one
same-evening review-fix pass. Commits, oldest first:
`452d87a` `7588923` `7bdd868` `66fb9aa` `a54201a` `40503d1` `3a16528` `a6879df`
`1203438` `3c0a528` `c088b5d`.

### 1. Watchdog: progress, not duration

**Problem (found in the first GPU raid, see "Native melee..." above).**
`StuckAliveSince` was a pure wall-clock timer from the moment the tail state
("everything of the wave spawned, at least one still alive") began, never reset
by a kill in between — so a wave the player was steadily clearing one infected at
a time still got force-cleared once total tail duration crossed
`StuckWaveTimeoutSeconds`, exactly as raids 1 and 2 showed.

**New semantics.** "Stuck" now means "fully spawned AND no infected has died for
`StuckWaveTimeoutSeconds`" — matching the setting's own name. The clock restarts
on every infected death, not merely once at tail entry. The setting itself is
unchanged: name, 100 s default, 0-600 s range; only its description in
`Settings.cs` was reworded to document the progress-based semantics.

**How a death is detected (and why not through the kill/scoring pipeline).**
`ZombieSpawnService.ConfirmedArrivals` (`Bots/ZombieSpawnService.cs`) is a new
monotonic lifetime counter, incremented in `OnZombieArrived` immediately after
the bot-alive check — deliberately before the player-alive early return, so it
can never undercount relative to `CountAliveInfected`'s own definition during
Zombie's instant-respawn window. `ZombieWavePolicy.Evaluate` (`Modes/
ZombieWavePolicy.cs`) gained a sixth parameter, `int arrivedTotal = 0` (defaulted
so every pre-existing non-watchdog call/test is unaffected), nets it against the
previous call's baseline (`State.LastArrivedTotal`) to get
`arrivedSinceLastCall`, and only then asks whether `alive` fell short of what new
arrivals alone would explain — a raw "did alive drop" check is not enough by
itself, because a request dispatched before the tail began can resolve into a
confirmed arrival during the tail (alive rising for a reason unrelated to
dying), and a death and a late arrival can land in the same call and net to zero
visible change. Every self-recursive call inside `Evaluate` forwards the SAME
`arrivedTotal` it received rather than letting it default, so a synthetic
re-entrant call can never corrupt the real baseline. `ZombieMode.Tick` reads
`ZombieSpawnService.ConfirmedArrivals` each wave-evaluation tick and passes it
through.

Two designs were weighed for the death signal itself (see `ZombieWavePolicy`'s
own doc comment, "How a death is detected, and why not the obvious way"): wiring
it through the existing kill-capture/scoring pipeline (`ZombieMode.OnKill`), or
deriving it purely from alive/arrived counts inside the pure policy layer.
Chosen: the latter — `ArenaKill` is only raised when EFT names a credited
aggressor, and an infected that dies with none identified (environmental damage,
a fall) is routed to `ArenaUncreditedDeathPatch`, which explicitly skips any
non-local-player victim, so that class of death would silently fail to reset the
watchdog if wired through `OnKill`. Staying inside the pure layer also keeps the
watchdog independent of any future change to scoring/kill-feed filtering.

**Fault control.** Reverted the tail-branch re-arm condition from
`if (NaN || anyDeathSinceLastCall)` back to the old `if (NaN)`-only check;
`TheStuckWaveWatchdogDoesNotFireWhileInfectedKeepDying`,
`TheStuckWaveWatchdogStillFiresWhenDeathsActuallyStop` and
`AMaskedDeathCoincidingWithALateArrivalStillResetsTheStuckTimer` all failed
(red) with the exact expected symptom — the watchdog firing early because a
death never restarted the clock; restored, full suite green again.

**Review fix, same evening (`c088b5d`).** The bigger-wave defaults below made a
watchdog-cleared tail able to hold the full `MaxAlive = 20` at once (vs. at most
`WaveStartSize = 8` under the old defaults), so one firing of
`RetireAllAliveInfected` could burst up to 20 per-bot `LogWarning` lines in a
single frame. Changed to one `LogInfo` summary line
("administratively retired N infected…") after the loop; the per-bot
`LogWarning` stays, but only on the genuinely exceptional failure-to-retire
path. Log-shape only, no behaviour change.

**Saturated waves arm the watchdog too (2026-10-06, same day).** The
progress fix above still only ARMED once `remainingToSpawn == 0` — the wave
fully spawned. A GPU raid the same day (18:04-18:10, wave 1 of 30 after
`7588923` raised `WaveStartSize` to 30 with `MaxAlive` unchanged at 20) hit
exactly the gap that leaves open: 20 infected alive, 10 still to spawn, a
player who could not land a kill, no infected death for over four minutes —
and the watchdog never fired at all, because the wave was never "fully
spawned". In v0 the same kind of stall resolved after 100s, only because
`WaveStartSize` (8) sat below `MaxAlive`, so every wave reached the
fully-spawned tail while still comfortably under the cap; the bigger-wave
change below is what exposed this. The real failure condition was never
"fully spawned", it is SATURATED: no spawn is possible right now, either
because everything has already been requested, or because `alive + pending`
already fills the policy's own target
(`min(MaxAlive, remainingToSpawn + alive)`, the same formula the ordinary
continuous-refill spawn decision already used) — both are the same failure
from the watchdog's point of view, since only an infected death can free the
room for the next spawn. `ZombieWavePolicy.Evaluate` now arms/re-arms on
saturation, using the exact same progress-not-duration clock as before. A
firing while `remainingToSpawn > 0` clears only the alive infected and stays
in `Phase.Wave` — the wave is not over, only unstuck — so the remainder
resumes spawning in the very same call, exactly as an ordinary death would
have let it; only a firing at the fully-spawned tail still ends the wave as
before. A wave still filling up (`alive + pending` below target) is never
saturated and must never arm this.

**Fault control.** Three new tests, each with an injected fault that turns
it red before the corresponding fix and green after it is restored:
(a) `TheStuckWaveWatchdogArmsWhenTheWaveIsSaturatedEvenWithSpawnsStillLeft`
reproduces the GPU raid directly (30-wave, `MaxAlive` 20, 20 alive/10 left,
zero deaths) — reverting the arming condition from `saturated` back to the
old `remainingToSpawn == 0` alone made it fail (the 90s ceiling crossed with
`ForceRetireStuckInfected` never turning true). (b)
`TheStuckWaveWatchdogKeepsResettingWhileSaturatedAndInfectedKeepDying` reuses
the masked-death pattern (one infected dies the same call a different one
arrives, so `alive` never visibly dips) but for the mid-wave/capped case —
reverting the re-arm condition from `NaN || anyDeathSinceLastCall` back to
`NaN`-only made it fail inside the loop, because nothing else resets the
clock while the wave stays pinned at the cap. (c)
`AWaveStillFillingUpDoesNotArmTheWatchdogEvenForALongTime` sits at 5/30
alive (`MaxAlive` 20) for 1000s with no deaths and confirms the watchdog
never arms and the wave keeps trying to spawn — the boundary the `saturated`
condition must respect.

### 2. Continuous, much bigger waves

**New defaults (`Settings.cs`, section `"Zombie"`):**

| Setting | Old | New | Range |
|---|---|---|---|
| `WaveStartSize` | 8 | **30** | 1-200 (was 1-100) |
| `WaveGrowth` | 4 | **10** | 0-100 (was 0-50) |
| `SpawnIntervalSeconds` | 1.0 | **0.75** | 0.2-10 (unchanged) |
| `MaxAlive` | 20 | 20 (unchanged, as decided) | 4-56 (unchanged) |

`ZombieWavePolicy.Evaluate`'s continuous-refill math already implemented "many
more zombies, not necessarily at once, but without pause, bounded by `MaxAlive`"
with no formula change needed: `target = min(MaxAlive, remainingToSpawn +
alive)`; one spawn request goes out every `SpawnIntervalSeconds` while
`alive + pending < target`. Raising the defaults alone was enough to deliver
item 2 of the request.

**The one real throttle risk a bigger wave 1 exposes.**
`ZombieProfilePlan.BackupTargets` was registered with the native backup loader
(`BotProfileBackuploader`) at a flat `MinimumBackupCount = 4` regardless of wave
size. With `infectedAssault` weighted 50% of the role mix, a 30-strong wave 1
draws roughly 15 of that role alone against a cache of 4; `BotProfileBackuploader`
only tops up once its deficit exceeds 3, one outstanding fetch at a time
(decompile-verified), so the cache would empty almost immediately into
`BotProfileClient.CreateProfile`'s synchronous 3-profile-at-a-time miss path —
visibly stalling spawns even though `ZombieWavePolicy` itself never pauses.
`ZombieProfilePlan.BackupCountForWaveStartSize(int waveStartSize)` scales the
per-(role, difficulty) backup target by the heaviest role weight's share of
`waveStartSize`, floored at `MinimumBackupCount` and ceilinged at
`MaxBackupCountPerPair` (a new `const int = 100`, i.e. `Core.LOCAL_BOTS_COUNT`,
the same native per-request cap the opening preload already respects by
construction). `ZombieMode.OnMatchStart` now calls
`ZombieProfilePlan.BackupTargets(_shooterPercent,
ZombieProfilePlan.BackupCountForWaveStartSize(_config.WaveStartSize))` instead
of the old implicit-default call.

**Opening preload.** `ZombieNativeEventBridge`'s `GetProfilesOnStart` hook
already covers at least `MaxAlive` for wave 1 by construction — it sizes to the
full `WaveStartSize`, and the new `30 > 20` holds with room to spare (the old
`8 < 20` did not need to, and now does not need to either).

**Tests added:** `WaveSize`'s arithmetic at the new defaults; a 30-zombie wave
at `MaxAlive = 20` ramping to exactly 20 alive and then refilling one per
`SpawnIntervalSeconds` as each kill opens a slot, continuously, until all 30
have gone out; `BackupCountForWaveStartSize`'s scaling, floor, ceiling and
monotonicity.

### 3. Refill between waves (ammo, meds, grenades)

Implemented as two layers, per `AGENTS.md`'s pattern elsewhere in this mode:

- **`ZombieRefillPolicy`** (`Modes/ZombieRefillPolicy.cs`) — pure, EFT/Unity-free
  WHEN layer, linked into `ArenaMode.Tests`. Fires exactly once per
  `Wave`→`Intermission` transition (edge-detected from the `Phase` the caller
  already reads every `WaveEvaluateIntervalSeconds`), only while the caller
  reports the player eligible (alive, not mid-death/respawn). A transiently
  ineligible player is not a permanent skip — Zombie's instant-respawn pipeline
  can still be finishing exactly when the wave's last infected dies (~0.2 s on
  the GPU host, per the GPU results document) — so the same intermission keeps
  asking on later calls, bounded by a retry budget (`RefillMaxAttempts = 20` in
  `ZombieMode`, a private const, at the existing 0.1 s evaluate cadence ≈ 2 s of
  real-attempt headroom) that only real attempts (a reported "did not start")
  consume; an ineligible tick costs nothing. `ReportStarted` resolves the
  intermission for good, mirroring `ZombieWavePolicy`'s own optimistic-commit
  convention (dispatched == resolved).
- **`ZombieLoadoutRefill`** (`Modes/ZombieLoadoutRefill.cs`) — the thin EFT-facing
  adapter. Writes **no new inventory-mutation code**: it re-applies the
  player's loadout through the SAME live-body respawn path Gun Game/Tactical
  already use between rounds.
  - **Random-kit mode:** `TryRefill` re-applies the player's CURRENT
    `KitDefinition` through `Respawn.LiveKitRespawn.AtRoundBreak` — the exact
    path `TacticalMode.StartRound` already uses between rounds. Zombie sets no
    `KitCatalog.KitProvider`, so `AtRoundBreak`'s internal `GetSelectedKit()`
    would otherwise hand back a fresh random draw; `ZombieMode` tracks "the kit
    currently applied" via the existing `ArenaEvents.OnKitApplied` event
    (subscribed in `OnMatchStart`, unsubscribed in `OnMatchEnd`) and
    `ZombieLoadoutRefill` installs that kit as a one-shot `KitProvider` for the
    exact width of `AtRoundBreak`'s single synchronous kit-capture call, then
    restores whatever was there before. A later ordinary mid-wave death respawn
    is unaffected and still draws a fresh random kit exactly as v0 does.
  - **Own-gear mode (item 4):** `TryRefillOwnGear` is `TryRefill`'s sibling —
    restores `ZombieOwnGearLoadout`'s captured entry loadout through a new
    `LiveKitRespawn.AtRoundBreakOwnGear` (which needed `AtRoundBreak`'s shared
    `SwapRoutine` machinery to grow an optional `equipmentOverride` delegate,
    since the kit-only `AtRoundBreak` hard-refuses when
    `KitCatalog.GetSelectedKit()` returns null and had no parameter for
    substituting a `TeamArenaLoadoutSnapshot`-style restore).
  - Both log exactly one Info line per successful refill, only from the
    `onComplete` callback once the whole swap is confirmed (not merely the one
    `EquipmentStrategy` step inside it):
    `"Zombie refill after wave {n}: kit '{name}' re-applied."` (random-kit) or
    `"Zombie refill after wave {n}: own-gear entry loadout re-applied (epoch {N})."`
    (own-gear). `TryRefill`/`TryRefillOwnGear` never throw into `Tick` — caught,
    logged once via `LogWarning`, reported as "did not start" so the policy's
    retry budget handles it.
  - `ZombieMode.Tick` computes `playerEligible` from `HealthController.IsAlive`
    plus `!ArenaRespawnDriver.{IsRespawnInProgress, StandDown}` and branches the
    refill call on the frozen `_ownGear` field; `ZombieRefillPolicy` itself (the
    WHEN) needed no change between the two modes.

**Accepted superset, documented, not fixed.** Re-applying a kit (or the entry
loadout) through `LiveKitRespawn.AtRoundBreak`/`AtRoundBreakOwnGear` also
restores armor/weapon **durability** and performs `AtRoundBreak`'s own short
`NearPositionSpawnSelector` relocation — both are accepted supersets of "just
refill ammo/consumables" inherited from reusing this already-tested path as-is,
rather than writing a bespoke in-place inventory edit. `ZombieLoadoutRefill`'s
own class doc comment argues this explicitly.

**Doc-comment correction (`3c0a528`, review).** The class doc comment had
claimed a failed restore is "a fatal, raid-ending abort" for both callers of the
underlying `TeamArenaLoadoutSnapshot` primitive. True for
`ArenaRespawnDriver.RespawnOwnGearPlayer` (a death respawn —
`AbortOwnGearRespawn` sets `StandDown` and schedules an abort), but **not** for
`ZombieLoadoutRefill.TryRefillOwnGear`: the `InvalidOperationException` it
throws from inside the `EquipmentStrategy` closure on a failed `TryRestore` is
caught by `RespawnEngine.RunRespawn`'s own failure callback and handled by
`LiveKitRespawn.SwapRoutine`'s ordinary non-fatal recovery (`RecoverOldBody` +
reporting "did not start") — that one resupply is skipped and the raid
continues on the player's current body. The comment now states the asymmetry
explicitly: a poisoned snapshot keeps silently failing every later
intermission's refill through the non-fatal path until the player's next real
death hits the same poisoned snapshot through the OTHER caller, which then IS
fatal.

**Fault controls (`7bdd868`):** dropping `playerEligible` from
`ShouldAttempt`'s expression failed `RetriesOnLaterTicksWhilePlayerIsIneligible
ThenFiresOnceEligible` and `TheRefillPolicyNeverFiresWhileThePlayerIsIneligible`
exactly as expected; making `ReportStarted` a no-op failed
`FiresExactlyOnceOnEnteringIntermissionWhilePlayerIsEligible` and
`TheRefillPolicyDoesNotRefireAfterAlreadyStartedThisIntermission` exactly as
expected. Both restored, full suite green again.

### 4. Own gear: bring your own protected equipment

As an alternative to random Arena kits, the player can keep their own
equipment, protected exactly the way Tactical's protected PitFireTeam path
protects a PMC's entry loadout — reusing that mechanism rather than writing new
profile-protection code.

- **Setting.** `Settings.ZombieOwnGear` (bool, default `false`, section
  `"Zombie"`) — a new F12 toggle.
- **Gate.** `ZombieOwnGearGate.IsSelectedFor(mode, ownGearSelection)`
  (`Modes/ZombieOwnGearGate.cs`) is the one pure place comparing the frozen F12
  value against `ArenaPopulationPolicy.Zombie` (`OrdinalIgnoreCase`, matching
  `KitPreloadPlan`'s own mode-comparison convention) — no EFT/Unity types, so it
  links directly into `ArenaMode.Tests` like `ZombieWavePolicy`/`ZombieRoleMix`/
  `ZombieMeleeGate`. Used identically for the loading-screen check
  (`ZombieOwnGearArmed`, against `ArmedMode`) and the live-raid check
  (`ZombieOwnGearActive`, against `ActiveMode`), so the two can never disagree.
- **Frozen at arm.** `ArenaSession.ZombieOwnGearSelection` freezes
  `Settings.ZombieOwnGear.Value` the exact moment `GunGameLadderSelection`
  already freezes its own F12 setting (`MarkArmed`) — a mid-loading F12 edit
  must not desync the loading-screen preload from what the live raid actually
  does. Cleared alongside `GunGameLadderSelection` on every
  arming-cleared/raid-rejected/raid-ended path.
- **Entry-loadout snapshot.** `ZombieOwnGearLoadout` (`Kits/
  ZombieOwnGearLoadout.cs`) wraps the EXISTING
  `TeamArenaLoadoutSnapshot` capture/arm/prepare/commit primitive Tactical's
  protected-team path already uses and tests — one snapshot per raid, one
  strictly increasing epoch per restore. Unlike Tactical's multi-frame
  round-transition staging (needed because `PitFireTeam`'s round-transition can
  hold the respawn pipeline open across several frames while four teammates
  reassemble), every Zombie restore arms, prepares AND commits in one
  synchronous call, because Zombie has no such hold (`HoldRespawn = false`, no
  round-transition state machine) — the dead/old body's `Profile` is still
  exactly the one `TryCaptureEntryLoadout` saw at the point `RespawnEngine`
  invokes `EquipmentStrategy` (confirmed against the decompiled
  `RespawnEngine.RunRespawn`'s own call ordering). A failed prepare or commit
  permanently poisons the snapshot's restore gate by design (same contract
  Tactical already lives with); both later callers treat that as fatal for a
  death respawn (see the doc-comment correction above for the refill's own,
  non-fatal, exception).
- **Raid start.** `ArenaSession.OnRaidStarted`'s kit-swap block gets an
  own-gear branch alongside the existing PitFireTeam one: captures the entry
  loadout (`ZombieOwnGearLoadout.TryCaptureEntryLoadout`) instead of calling
  `LiveKitRespawn.AtRaidStart`, and schedules a failed-start abort if the
  capture itself fails, mirroring how a rejected raid-start token is already
  handled at this same call site — never silently falling through to a random
  kit.
- **Loading-screen preload skipped.** `KitBundlePreloader.BeginRaidStart` gets
  an early return shaped like its existing PitFireTeam/
  `CompatibilityTeamArenaActive` check, gated on `ZombieOwnGearArmed` (the
  loading-time exception class `AGENTS.md` already documents for this file) —
  a whole-catalogue preload plan would otherwise still warm pools for a raid
  that hands out no kit at all. (Deliberately NOT routed through
  `KitPreloadPlan.OpeningKit`'s existing `protectedTeamRaid` parameter alone,
  because that would still let a whole-catalogue-scope plan warm the rest of
  the catalogue — the same reason the production PitFireTeam check already
  returns early instead of threading through that parameter.)
- **Secure container.** `ArenaSecureContainer.LeavesContentsAtHome` gets a third
  condition alongside `EmptySecureContainer` and `!HasFrozenTeamRoster`: own
  gear has no medical ladder to outclass and emptying the container would
  destroy the player's own real supplies, the same way it already would for a
  frozen PitFireTeam roster.
- **Death respawn.** `ArenaRespawnDriver.RespawnRoutine` gets a new branch,
  checked FIRST — before the frozen-PitFireTeam branch and unconditionally
  ahead of the ordinary `KitCatalog.GetSelectedKit()` call — so a random-kit
  respawn can never reach a player who asked to keep their own gear. The new
  `RespawnOwnGearPlayer` coroutine mirrors `RespawnProtectedTeamPlayer`'s shape
  (same spawn protection, same failure handling via a new
  `AbortOwnGearRespawn`).
- **Wave-intermission refill.** Covered in section 3 above
  (`TryRefillOwnGear`/`AtRoundBreakOwnGear`).
- **Menu exposure.** Mirrors Tactical's equivalent the way the task asked:
  Tactical has no literal toggle for its own-gear path (it is automatic, driven
  by a complete PitFireTeam roster), so this reuses the SAME mutator-panel
  mechanism that already mode-gates other per-mode options (Pickups to
  Deathmatch/Team DM, Lasers to Team DM/Tactical 5v5) rather than inventing new
  UI: a new `ArenaMutator.OwnGear` row ("OWN GEAR" / "Bring your own protected
  equipment.", unavailable outside Zombie: "Available in Zombies."). No
  `ArenaMutatorsView` code change was needed — it already builds one row per
  `Enum.GetValues(typeof(ArenaMutator))` entry and lays out two columns of
  `(count + 1) / 2` rows; 15→16 mutators keeps `rowsPerColumn` at 8 either way
  (verified by arithmetic, not confirmed in-game). `ArenaModeSelectionView.
  Refresh`'s EQUIPMENT fact text gets the same special case its `TacticalOnly`
  branch already has ("PMC entry gear"): the ZOMBIES row shows "Own gear"
  instead of the catalog's default "Arena kits" once `Settings.ZombieOwnGear`
  is on.

**Fault control (`a54201a`):** temporarily changed `ZombieOwnGearGate`'s
`StringComparison.OrdinalIgnoreCase` to `StringComparison.Ordinal`;
`TheComparisonIsCaseAndWhitespaceInsensitive` failed exactly as expected
(an uppercase `"ZOMBIE"` mode id stopped matching); reverted, full suite green.

### Review fix: the stale cached local-player reference (`1203438`)

**Bug (`confirmed` by the orchestrator's own review pass after stages A-C).**
`ZombieMode._local` was refreshed only by the real death-respawn event
(`OnLocalRespawn`, raised from `ArenaRespawnDriver`'s three death call sites) —
but the raid-start kit swap AND the wave-intermission refill's own live-body
swap (`LiveKitRespawn.AtRaidStart`/`AtRoundBreak`/`AtRoundBreakOwnGear`, all
through the shared `SwapRoutine`) replace the live body too and destroy the old
one (`ArenaRetiredBodyEffects.TryDestroy`) WITHOUT ever raising that event.
`EFT.Player : MonoBehaviour`, so Unity's overridden `==`/`!=` makes
`_local != null` read false forever for the destroyed body on the very next
tick — silently, no log line, since `ZombieRefillPolicy.Evaluate` simply stops
returning `ShouldAttempt = true`. In random-kit mode this broke the refill from
before wave 1 even started (`OnMatchStart` captures the pre-swap body strictly
before `ArenaSession`'s own `AtRaidStart` call destroys it). In own-gear mode
wave 1's refill fired correctly, but that very refill's own
`AtRoundBreakOwnGear` call then invalidated `_local` for every later
intermission. Net effect either way: at most one resupply for the whole raid
instead of one per cleared wave — exactly the "zwischen den Wellen" behaviour
the user asked for, silently only half-delivered.

**Fix.** Mirrors `TeamDeathmatchMode.Tick`'s already-established pattern for
this exact class of staleness: poll `_world.MainPlayer` and heal `_local` (via
the existing `OnLocalRespawn`) when it has changed, guarded on
`!ArenaRespawnDriver.IsRespawnInProgress` so the heal only fires once a
replacement body is fully wired, never mid-swap (that flag spans the whole
`LiveKitRespawn.SwapRoutine` pipeline too, via the shared
`BeginPipelineUse`/`EndPipelineUse`). Placed right before computing
`playerEligible`, inside the existing 0.1 s wave-evaluate throttle.

No new unit test: the bug is Unity-specific (`MonoBehaviour`'s overridden
null-equality on a destroyed object), which a desktop-CLR xunit test cannot
reproduce — the same "client-side, verification is still a raid" limitation
this document's own "Known test-coverage gap" note and `AGENTS.md` already
document for `ZombieMode.Tick`'s EFT-facing glue. **The next GPU raid must show
a refill firing after EVERY cleared wave, not just the first.**

### Test totals after v1

Offline (`dotnet test ArenaMode.Tests`, no-deploy sentinel confirmed absent
before and after): **852 passed, 2 skipped (pre-existing, environment-gated,
unrelated to Zombie), 0 failed, 854 total** — re-confirmed for this document.
Clean Release builds of `ArenaMode.Client` and `ArenaMode.Server` with the same
no-deploy properties: 0 errors. `ZombieWavePolicyTests.cs`,
`ZombieRefillPolicyTests.cs`, `ZombieOwnGearGateTests.cs` and
`ZombieProfilePlanTests.cs` carry the new coverage; all remain pure/
production-linked with no EFT/Unity dependency.

### Updated GPU playtest checklist (next raid)

None of v1 has been seen live. Per `AGENTS.md`, "for anything client-side,
verification is still a raid" — everything above is decompile cross-checking,
offline tests and clean builds only. The next raid (prefer SPT-GPU-2) must
confirm, in order:

1. **Watchdog stays quiet while the player is actively killing.** Fight a wave
   down through kills at a pace slower than `StuckWaveTimeoutSeconds` between
   individual kills; the watchdog must NOT force-clear it. Then stop killing
   entirely for `StuckWaveTimeoutSeconds` with infected still alive and confirm
   it still fires (the watchdog is not disabled, only re-armed by progress).
2. **A wave actually ends through kills**, not the watchdog — the first time
   this mode has shown that live (both GPU-2 raids on 2026-10-06 had every wave
   end via the watchdog instead).
3. **A 30-zombie wave 1 keeps feeding at `MaxAlive = 20`** with no visible
   stall: alive count ramps to 20 quickly and a kill is followed by a fresh
   spawn within about `SpawnIntervalSeconds`, not a multi-second
   `BotProfileClient` synchronous-miss pause. No `ArgumentOutOfRangeException`
   or profile-starvation warning.
4. **The intermission refill restores magazines/meds/grenades after wave 1**,
   both modes: (a) random-kit — consume some ammo/meds/grenades during wave 1,
   survive to the intermission, confirm the log line `"Zombie refill after
   wave 1: kit '...' re-applied."` and that the inventory is topped up (and
   that durability/position also changed, per the accepted superset); (b)
   repeat for wave 2's intermission and confirm the SAME refill fires again
   (this is exactly what `1203438` fixes — the prior build would have silently
   stopped refilling after wave 1).
5. **Own gear end to end.** Arm Zombie with `OwnGear` on (F12 or the new OWN
   GEAR mutator row); confirm the map screen's EQUIPMENT fact shows "Own gear";
   start a raid and confirm the BepInEx log shows the raid-start kit swap was
   skipped and the secure container was NOT emptied; die once and confirm the
   respawn restores the real loadout (log: "Arena Zombie own-gear respawn
   completed (epoch 1)…"); survive to an intermission and confirm the log line
   `"Zombie refill after wave N: own-gear entry loadout re-applied (epoch
   N)."` with ammo/meds/grenades topped up; end the raid and confirm the
   profile afterward is identical to before arming (the sacred-profile
   guarantee, same as every other mode).
6. **Regression check.** A normal (OwnGear off) Zombie raid and an ordinary
   non-Zombie raid are both completely unaffected by all of v1.

Also still open from the v0 checklist and not yet retried: performance/corpses
at `MaxAlive` 40-56, a deliberately stranded infected for the watchdog itself,
`ShooterPercent > 0` (ranged infected), the second `Activate()`'s caller, Labs/
Labyrinth, the boss zombie, PitFireTeam.
