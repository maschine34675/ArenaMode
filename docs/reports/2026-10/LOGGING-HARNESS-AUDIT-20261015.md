# Logging harness compatibility audit — 2026-10-15

Scope: leaf 1.4, `tools/**`; baseline `9f29f49`. No production code, deployment, staged build scripts, or GPU state was changed by this leaf. This is source-linked/desktop evidence, not native gameplay acceptance.

## Outcome

75 existing harness projects were considered: **72 pass, 3 retain independently reproduced baseline compilation gaps**. In addition, the native-extraction AirVault wrapper passes 374 assertions, and the new real-BepInEx logging harness passes 7 assertions before and after two compiled fault controls. The ZombiePerformance wrapper passes 192 assertions and rejects all 17 compiled mutants. Unit-suite/build evidence is owned by the parent and deliberately not claimed here.

The three remaining failures are not logging regressions. Existing gameplay assertions were not removed, bypassed, or relaxed to accommodate the log cleanup.

## Fixture changes

- Existing fake loggers gained `LogDebug` with the same recording/throwing behavior as their existing `LogInfo` sink. The old gameplay/error-path observations therefore remain active; these aggregate sinks do not pretend to verify severity routing.
- Source-linked probe suites gained a small explicit `ArenaLog` stand-in, enabled for their existing diagnostic-observation scenarios. The separate actual `ArenaLog.cs`/BepInEx harness verifies default-off and runtime-switch semantics. No production logger/filter is replaced.
- Announcer, air-vault, TDM awareness/mover, and TDM search-tempo assertions now expect the exact `[dev] ` label on the same diagnostic message. Their occurrence limits, payloads, state checks, and failure tests remain intact.
- `ZombieTeamChecks` now has no-op `ZombieCrowdRuntime.Tick/Reset` host seams shared by the solo and pending-wave suites. Crowd behavior remains owned by the actual-source ZombieCrowd suite. `Tick` was already called by `ZombieMode.cs:313` at `9f29f49`; the added seam does not implement or disable crowd behavior under test.
- New Announcer checks assert native playback continues while DeveloperMode is off and no developer probe is emitted. New reload checks cover all six existing native operation families with DeveloperMode off, retaining speed and restoration assertions. Results: Announcer 332; reload 797 (18 additional assertions).

## Actual severity routing and fault controls

`tools/LoggingChecks/Program.cs` subscribes to **real BepInEx `ManualLogSource.LogEvent`** and compiles actual `ArenaMode.Client/Diagnostics/ArenaLog.cs`. Seven assertions cover: unbound settings quiet, disabled setting quiet, ordinary Warning/Error unaffected, enabled developer output at Info with `[dev]`, ordinary Debug unchanged, runtime disabling, and a missing shutdown logger.

Command: `& tools/LoggingChecks/Verify.ps1`.

The wrapper uses unique configurations and separate source copies; it never changes production source or deployed DLLs. It checks exact runtime assertion failures and rejects compiler failures as evidence:

1. Removing the developer gate (`if (true)`) fails `unbound developer mode is quiet`.
2. Forcing `DeveloperEnabled` false fails `enabled probe appears at Info with its developer label`.
3. Actual production source passes again in `finally`.

Evidence: `artifacts/logging-20261015/harness/logging-wrapper-final.log`; detailed cases: `artifacts/logging-checks/a092aca318a4412397c198b84993bc34/`. Final marker: `ARENA_LOGGING_MUTATION_CHECKS_PASSED (2 compiled mutants)`.

## Commands and extraction boundaries

All paths below are relative to this repository; logs are under `artifacts/logging-20261015/harness/` (ignored runtime evidence). `resolved-results.json` contains the final per-project exit code/log map. Earlier failed attempts are retained rather than overwritten.

Ordinary source-linked checks used `dotnet run --project tools/<suite>/<suite>.csproj -c Release -p:WarningLevel=0`. Where the suite accepts paths, arguments follow `--`: AreaTeamRuntime/ArenaPickup/PickupObjective receive `D:\SPT41`; ArenaPostRaid/ArenaUiInteraction/HitmarkerAudio receive the repository path; KitPolish Supplies receives repository path and `D:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json`. The initial sweep mistakenly passed `--nologo` to `dotnet run`; because it reaches application arguments, affected path-aware suites were rerun with their actual required arguments. Those initial errors are not counted as regressions or successful checks.

Fresh extraction/wrapper runs:

- `& tools/ArenaAirVaultChecks/Verify.ps1 -ProductionOnly`: 374 assertions, fresh native/production extraction; `airvault-wrapper-retry.log`.
- `& tools/AreaTeamRuntimeChecks/Verify.ps1 -ProductionOnly`: 459 assertions, actual `PickRespawn` freshly extracted; `area-team-wrapper-final.log`.
- `& tools/DeathmatchGroupChecks/Verify.ps1`: 83 assertions, two compiled fault controls; `deathmatch-groups-wrapper.log`. Direct use of stale generated fragments was discarded.
- `& tools/SoloPopulationChecks/Verify.ps1 -BaselineOnly`: 141 actual-source runtime checks plus 17 installed-native IL contracts; `solo-population-wrapper-final.log`. Direct compilation without its required extraction is not the supported test entry point.
- `& tools/TdmMatchChecks/Extract-RespawnGuards.ps1 -OutputPath <absolute artifacts/.../RespawnGuards.cs>`, then `dotnet run --project tools/TdmMatchChecks/TdmMatchChecks.csproj -c LoggingFinal -p:RespawnGuardSource=<same file>`: 247 assertions; `tdm-match-fresh.log`. Full unrelated TDM mutation sweep was not repeated.
- `& tools/TacticalPolishChecks/Verify.ps1 -SkipNegativeProbes` generated actual source slices but its custom `BaseIntermediateOutputPath` picked up a prior local `obj` tree and reported duplicate assembly attributes. The generated `TacticalSlice.cs` and `TacticalBuyPanel.cs` from `artifacts/tactical-polish-checks/3000eb8eaaef4e3683a75e1ed0aea1a9/production/` were then run with `dotnet run --project tools/TacticalPolishChecks/TacticalPolishChecks.csproj -c LoggingFinal -p:TacticalSlice=<absolute slice> -p:PanelSource=<absolute panel> -- all`, preserving normal SDK `obj` exclusions: 74 assertions; `tactical-polish-fresh.log`. Neither wrapper nor assertions were weakened.
- `& tools/ZombiePerformanceChecks/Verify.ps1`: 192 assertions, all 17 compiled mutants rejected; `zombie-performance-wrapper-final.log`. Its 10,000-call allocation assertion measured zero allocations in the desktop boundary fixture; this is not a Unity FPS measurement.

## Remaining baseline gaps

The current failing commands and baseline controls were executed, not inferred only from text:

| Harness | Current failure | Baseline control |
| --- | --- | --- |
| M60DefinitionChecks | `ArenaPickupSettings.cs:39` refers to `ArenaHealthPickup` absent from this fixture | `git show 9f29f49:ArenaMode.Client/Pickups/ArenaPickupSettings.cs` saved to an ignored source copy, then same project with `-c LoggingBaselineControl -p:PickupSettingsSource=<copy>`: same CS0103 |
| TdmWarmupChecks | Seven missing host APIs in the controller fixture | `ArenaController.cs` from `9f29f49`, same project with `-c LoggingBaselineControl -p:ControllerSource=<copy>`: same seven errors |
| ZombieKitWarmupChecks | Same seven missing host APIs | Same baseline-controller override: same seven errors |

Controller APIs: `ZombiePftFastPath.TickDiagnostics`, `ZombieVoiceBudget.Tick`, `ArenaSession.TickAreaStart`, `PitFireTeamAreaSpawns.Tick`, `ArenaDefaultFireMode.Tick`, `ArenaAreaRuntime.IsPending`, `ArenaMatchService.TeamStartAbortPending`. Logs: `M60DefinitionChecks.baseline-control.log`, `TdmWarmupChecks.baseline-control.log`, `ZombieKitWarmupChecks.baseline-control.log`. These suites need separate fixture maintenance; no speculative production fix was made.

No dedicated ZombieMelee harness exists in `tools/**`. Its developer-gate changes received source review below; native melee animation/contact acceptance remains outside this desktop task.

## Independent early-guard review

Reviewed the final client developer guards for gameplay side effects. No gameplay branch was found inside the newly disabled observation paths:

- Reload diagnostics only manage diagnostic weak-table state. Actual speed leases/cleanup remain outside; the new developer-off cases demonstrate this.
- Announcer plays through the native route before its output probe. The new developer-off case demonstrates playback is retained.
- TDM search tempo assigns its maximum delay before returning when developer logging is disabled. Awareness and mover detail paths only inspect state; normal refresh remains separate.
- Air-vault trace and motion-observation state are separate from animator ownership, vault movement, and restoration. Developer-off clears only the observation record.
- BotLoadBoost establishes/restores forced loading state before skipping diagnostic output. Pursuit route/decision deduplication only controls messages; genuine failure warnings remain outside the guards.
- Corpse cleanup executes before the gated expensive report; cleanup state is not conditional on DeveloperMode.
- ZombieMeleeDiagnostics gates diagnostic event subscriptions. The handlers only increment diagnostic counters/read metadata; native attack/hit event execution is not suppressed. Existing subscriptions still unsubscribe on death/unspawn, and `ConsumeTotals` still clears counters. Diagnostics become partial if DeveloperMode is enabled after some zombies already spawned; this affects observation completeness, not gameplay.

## Per-project results

The table below lists all 75 considered existing projects. AirVault and the new LoggingChecks are additional as detailed above. The three baseline gaps remain failures, not skipped or green tests.

| Harness | Result | Evidence |
| --- | --- | --- |
| AnnouncerChecks | ARENA_ANNOUNCER_CHECKS_PASSED (332 assertions) | `AnnouncerChecks.final.log` |
| AreaConfigChecks | AREA CONFIG PRODUCTION CHECKS PASSED (259 assertions; real BepInEx bind/save/reload) | `AreaConfigChecks_AreaConfigChecks.retry.log` |
| AreaConnectivityChecks | AREA_CONNECTIVITY_CHECKS_PASSED: 630 assertions | `AreaConnectivityChecks_AreaConnectivityChecks.after-api.log` |
| AreaTeamRuntimeChecks | ARENA_TEAM_RUNTIME_CHECKS_PASSED | `area-team-wrapper-final.log` |
| ArenaAirControlChecks | ARENA_AIR_CONTROL_CHECKS_PASSED (155 assertions) | `ArenaAirControlChecks_ArenaAirControlChecks.after-api.log` |
| ArenaExitChecks | ARENA_EXIT_CHECKS_PASSED (118 assertions) | `ArenaExitChecks_ArenaExitChecks.after-api.log` |
| ArenaInputChecks | ARENA_INPUT_CHECKS_PASSED (78 assertions) | `ArenaInputChecks_ArenaInputChecks.after-api.log` |
| ArenaKillcamChecks | ARENA KILLCAM CONTRACTS PASSED: 170 assertions | `ArenaKillcamChecks_ArenaKillcamChecks.after-api.log` |
| ArenaLabyrinthAccessChecks | ARENA_LABYRINTH_ACCESS_CHECKS_PASSED (50 assertions) | `ArenaLabyrinthAccessChecks_ArenaLabyrinthAccessChecks.after-api.log` |
| ArenaLightingChecks | ARENA_LIGHTING_CHECKS_PASSED (267 assertions) | `ArenaLightingChecks_ArenaLightingChecks.after-api.log` |
| ArenaMagazineChecks | ARENA_MAGAZINE_CHECKS_PASSED (75 assertions) | `ArenaMagazineChecks_ArenaMagazineChecks.after-api.log` |
| ArenaMedicalChecks | ARENA_MEDICAL_CHECKS_PASSED (74 assertions) | `ArenaMedicalChecks_ArenaMedicalChecks.after-api.log` |
| ArenaPickupChecks | ARENA_PICKUP_BEHAVIOR_PASSED: 410 assertions; source-linked runtime/state/layout/geometry; scratch C:\Users\Administrator\AppData\Local\Temp\ArenaPickupChecks-2ff7a2d3c10b4c6f86dc68edc6f83e73 | `ArenaPickupChecks_ArenaPickupChecks.retry.log` |
| ArenaPostRaidChecks | ARENA_POSTRAID_PRODUCTION_CHECKS_PASSED: 55 assertions/contracts | `ArenaPostRaidChecks_ArenaPostRaidChecks.retry.log` |
| ArenaReloadChecks | ARENA_RELOAD_CHECKS_PASSED (797 assertions) | `ArenaReloadChecks.final.log` |
| ArenaUiInteractionChecks | ARENA_EXIT_CHECKS_PASSED: 65 assertions | `ArenaUiInteractionChecks_ArenaUiInteractionChecks.retry.log` |
| ArenaWeaponPickupChecks | ARENA WEAPON ADAPTER PASSED: 155 assertions | `ArenaWeaponPickupChecks_ArenaWeaponPickupChecks.after-api.log` |
| ArenaWeaponPickupChecks/Definition | BASELINE GAP (compile; see below) | `ArenaWeaponPickupChecks_Definition_M60DefinitionChecks.after-api.log` |
| BotAgilityChecks | BOT_AGILITY_BEHAVIOR_PASSED (309 assertions) | `BotAgilityChecks_BotAgilityChecks.after-api.log` |
| BotLifecycleSafetyChecks | ARENA_BOT_LIFECYCLE_CHECKS_PASSED (4315 assertions) | `BotLifecycleSafetyChecks_BotLifecycleSafetyChecks.after-api.log` |
| BotMovementChecks | BOT_MOVEMENT_BEHAVIOR_PASSED (131 assertions) | `BotMovementChecks_BotMovementChecks.after-api.log` |
| BotTempoChecks | BOT_TEMPO_BEHAVIOR_PASSED (149 assertions) | `BotTempoChecks_BotTempoChecks.after-api.log` |
| BunnyhopChecks | ARENA_BUNNYHOP_CHECKS_PASSED (76 assertions) | `BunnyhopChecks_BunnyhopChecks.after-api.log` |
| CorpseCleanupChecks | CORPSE_CLEANUP_CHECKS_PASSED (309 assertions) | `CorpseCleanupChecks_CorpseCleanupChecks.after-api.log` |
| DeathmatchGroupChecks | DEATHMATCH_GROUP_CHECKS_PASSED | `deathmatch-groups-wrapper.log` |
| DeathmatchMenuChecks | DEATHMATCH_MENU_STATE_PASSED: 35 assertions | `DeathmatchMenuChecks.final.log` |
| DefaultFireModeChecks | ARENA_DEFAULT_FIRE_MODE_CHECKS_PASSED: 46 assertions | `DefaultFireModeChecks_DefaultFireModeChecks.after-api.log` |
| DoubleJumpChecks | ARENA_DOUBLE_JUMP_CONTRACTS_PASSED (293 assertions; production source with contract doubles, no live Unity/Harmony claim) | `DoubleJumpChecks_DoubleJumpChecks.retry.log` |
| DynamicMapCorpseChecks | ARENA_DYNAMICMAP_CORPSE_BEHAVIOR_PASSED (61 assertions) | `DynamicMapCorpseChecks_DynamicMapCorpseChecks.after-api.log` |
| EquatorTeamSpawnChecks | EQUATOR_TEAM_SPAWN_CHECKS_PASSED (100 assertions) | `EquatorTeamSpawnChecks_EquatorTeamSpawnChecks.after-api.log` |
| FriendlyFireRuntimeChecks | FRIENDLY_FIRE_RUNTIME_PASSED 229 assertions | `FriendlyFireRuntimeChecks_FriendlyFireRuntimeChecks.after-api.log` |
| FriendlyMarkerChecks | ARENA_FRIENDLY_MARKER_CHECKS_PASSED (150 assertions) | `FriendlyMarkerChecks_FriendlyMarkerChecks.after-api.log` |
| GcThresholdChecks | ARENA_GC_THRESHOLD_BASELINE_PASSED checks=35 | `GcThresholdChecks_GcThresholdChecks.after-api.log` |
| GpuAcceptanceCompatChecks | GPU_ACCEPTANCE_COMPAT_CONTRACTS_PASSED: 47 | `GpuAcceptanceCompatChecks_GpuAcceptanceCompatChecks.after-api.log` |
| HitmarkerAudioChecks | HITMARKER_PRODUCTION_CHECKS_PASSED (54 assertions) | `HitmarkerAudioChecks_HitmarkerAudioChecks.retry.log` |
| KitPolishChecks/Supplies | KIT_SUPPLIES_ASSERTIONS_PASSED: 1605 | `KitPolishChecks_Supplies_SuppliesChecks.retry.log` |
| LabsBotSpawnChecks | LABS_BOT_SPAWN_CHECKS_PASSED (1105 assertions) | `LabsBotSpawnChecks_LabsBotSpawnChecks.after-api.log` |
| LabyrinthAlarmChecks | ARENA_LABYRINTH_ALARM_CHECKS_PASSED (91 assertions) | `LabyrinthAlarmChecks_LabyrinthAlarmChecks.after-api.log` |
| LabyrinthTrapChecks | ARENA_LABYRINTH_TRAP_CHECKS_PASSED (384 assertions) | `LabyrinthTrapChecks_LabyrinthTrapChecks.after-api.log` |
| MolotovChecks | ARENA_MOLOTOV_PRODUCTION_PASSED: 60 assertions | `MolotovChecks_MolotovChecks.after-api.log` |
| NativeAreaHookChecks | NATIVE_AREA_HOOK_CHECKS_PASSED (195 assertions) | `NativeAreaHookChecks_NativeAreaHookChecks.after-api.log` |
| NativeArenaHudChecks | NATIVE_ARENA_HUD_PRODUCTION_PASSED (44 assertions) | `NativeArenaHudChecks_NativeArenaHudChecks.after-api.log` |
| NativeMenuChecks | ARENA_MENU_CHECKS_PASSED (640 assertions) | `NativeMenuChecks_NativeMenuChecks.after-api.log` |
| NativeModeSelectionChecks | PASS: 160 Arena mode-selection state checks (production state and catalog linked). | `NativeModeSelectionChecks.final.log` |
| OpticPreloadChecks | ARENA_OPTIC_PRELOAD_CONTRACTS_PASSED assertions=73 | `OpticPreloadChecks_OpticPreloadChecks.after-api.log` |
| ParticipantMenuChecks | PARTICIPANT_SOURCE_CHECKS_PASSED (3730 assertions) | `ParticipantMenuChecks_ParticipantMenuChecks.after-api.log` |
| PftPortedMapChecks | PFT_PORTED_MAP_CHECKS_PASSED: 234 assertions; production resolver and native-hook binding, no game acceptance claimed. | `PftPortedMapChecks_PftPortedMapChecks.after-api.log` |
| PickupObjectiveChecks | ARENA_PICKUP_BEHAVIOR_PASSED: 545 assertions; source-linked runtime/state/layout/geometry; scratch C:\Users\Administrator\AppData\Local\Temp\ArenaPickupChecks-6edeb0b4f2bb4dc182b71daae5cae5d9 | `PickupObjectiveChecks_PickupObjectiveChecks.retry.log` |
| PitFireTeamChecks | PITFIRETEAM_COMPAT_CHECKS_PASSED (340 assertions) | `PitFireTeamChecks.final.log` |
| PitFireTeamRoundLifecycleChecks | PITFIRETEAM_ROUND_LIFECYCLE_CHECKS_PASSED (500 assertions) | `PitFireTeamRoundLifecycleChecks.final.log` |
| PoolPrewarmChecks | ARENA_POOL_PREWARM_BASELINE_PASSED checks=44 | `PoolPrewarmChecks_PoolPrewarmChecks.after-api.log` |
| PortedCoverChecks | PORTED_COVER_CHECKS_PASSED: 150 assertions; linked production gates/native branch model, not game acceptance. | `PortedCoverChecks_PortedCoverChecks.after-api.log` |
| RaidInfoPanelChecks | ARENA_INFO_PANEL_PRODUCTION_PASSED (368 assertions) | `RaidInfoPanelChecks_RaidInfoPanelChecks.after-api.log` |
| RoleLabelChecks | ARENA_ROLE_LABEL_CONTRACTS_PASSED: 174 assertions | `RoleLabelChecks_RoleLabelChecks.after-api.log` |
| SainPersonalityChecks | ARENA_SAIN_PERSONALITY_CHECKS_PASSED (176 assertions) | `SainPersonalityChecks_SainPersonalityChecks.after-api.log` |
| SoloPopulationChecks | SOLO POPULATION CHECKS PASSED | `solo-population-wrapper-final.log` |
| SurgicalWalkChecks | SURGICAL_WALK_CHECKS_PASSED (91 assertions) | `SurgicalWalkChecks_SurgicalWalkChecks.after-api.log` |
| TacticalCorpseChecks | TACTICAL_CORPSE_CHECKS_PASSED: 68 assertions; actual cleanup and retention sources, native-shaped boundary doubles | `TacticalCorpseChecks_TacticalCorpseChecks.after-api.log` |
| TacticalPolishChecks | TACTICAL POLISH CHECKS PASSED (74 assertions) | `tactical-polish-fresh.log` |
| TdmAwarenessChecks | TDM_AWARENESS_CONTRACTS_PASSED: 620 assertions; production Relations and hearing bridge linked, no GPU claim. | `TdmAwarenessChecks.final.log` |
| TdmMatchChecks | TDM_MATCH_POLICY_EXIT_PASSED (247 assertions) | `tdm-match-fresh.log` |
| TdmMatchMenuChecks | TDM_MATCH_MENU_CHECKS_PASSED (2494 assertions) | `TdmMatchMenuChecks_TdmMatchMenuChecks.after-api.log` |
| TdmSearchTempoChecks | TDM_SEARCH_TEMPO_CONTRACTS_PASSED: 104 assertions; linked production hooks and native pair guards, no AI simulation. | `TdmSearchTempoChecks_TdmSearchTempoChecks.retry.log` |
| TdmWarmupChecks | BASELINE GAP (compile; see below) | `TdmWarmupChecks_TdmWarmupChecks.after-api.log` |
| TeamArenaExpansionChecks | TEAM_ARENA_EXPANSION_CHECKS_PASSED (892 assertions) | `TeamArenaExpansionChecks.final.log` |
| TeamDeathmatchRuntimeChecks | ARENA_TDM_RUNTIME_CHECKS_PASSED | `TeamDeathmatchRuntimeChecks_TeamDeathmatchRuntimeChecks.after-api.log` |
| TeamGiveUpChecks | ARENA_TEAM_GIVE_UP_CHECKS_PASSED | `TeamGiveUpChecks_TeamGiveUpChecks.after-api.log` |
| ZombieCrowdChecks | ZOMBIE_CROWD_CHECKS_PASSED (88 assertions) | `ZombieCrowdChecks_ZombieCrowdChecks.after-api.log` |
| ZombieKitWarmupChecks | BASELINE GAP (compile; see below) | `ZombieKitWarmupChecks_ZombieKitWarmupChecks.after-api.log` |
| ZombiePerformanceChecks | ZOMBIE_PERFORMANCE_MUTATION_CHECKS_PASSED 17 compiled mutants; evidence: D:\SPT41\Development\ArenaMode\artifacts\zombie-performance-checks\769f8294940149fd93eab6d77753416d | `zombie-performance-wrapper-final.log` |
| ZombiePftFastPathChecks | ZOMBIE_PFT_FAST_PATH_CHECKS_PASSED 190 assertions; actual source linked; native Mono detour requires game proof. | `ZombiePftFastPathChecks_ZombiePftFastPathChecks.after-api.log` |
| ZombieSoloChecks | ZOMBIE_SOLO_CHECKS_PASSED 85 assertions | `ZombieSoloChecks.final.log` |
| ZombieTeamChecks | ZOMBIE_TEAM_CHECKS_PASSED 112 assertions | `ZombieTeamChecks.final.log` |
| ZombieVoiceBudgetChecks | ZOMBIE_VOICE_BUDGET_CHECKS_PASSED assertions=325 | `ZombieVoiceBudgetChecks_ZombieVoiceBudgetChecks.after-api.log` |
| ZombieWavePendingChecks | ZOMBIE_WAVE_PENDING_CHECKS_PASSED 49 assertions | `ZombieWavePendingChecks.final.log` |
