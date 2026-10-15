# Client logging audit — 2026-10-15

Baseline: `9f29f49`. Scope: the client runtime directories, `Plugin.cs` and
`ArenaController.cs`; `Config/**` and `Diagnostics/**` belong to the coordinating
review. This document records semantic source review, not a native gameplay test.
The coordinating report records build, regression checks and delivery separately.

## Severity contract

- Info: plugin/build identity; optional integration availability; selected rules,
  live round/wave starts, outcomes and recovery from a player-visible failure;
  explicit export completion; explanations for missing equipment or unavailable UI.
- Debug: hook setup, successful asset loading, profile/body identities, spawn
  requests, placement, lifecycle transitions, loadout replacement, camera ownership,
  audio queue policy and aggregate implementation counters.
- DeveloperMode: detailed movement/audio/renderer/AI/corpse probes and per-shot,
  per-contact or per-jump traces. `ArenaLog.Developer` also checks the master switch.
  It intentionally emits labelled Info only after explicit opt-in, so these traces
  remain usable without enabling all BepInEx Debug output.
- Warning/Error: real inability to perform an action, invalid identity or contract,
  failed protection/restore/cleanup, unsafe placement, missing assets, degraded
  behavior, no-contest and failed native calls. Existing suppression/rate limits
  remain; there is no global logger filter and no filtering of other mods.

## Source-site counts

Counts exclude comments, generated files, Config and Diagnostics. They count call
sites, not messages per raid. All **814 original Info/Warning/Error call sites** in
the owned source were reviewed. The five existing Debug sites were also checked.

| Level | Before | After |
|---|---:|---:|
| Info | 306 | 43 |
| Debug | 5 | 237 |
| Developer helper | 0 | 40 |
| Warning | 437 | 432 |
| Error | 71 | 71 |

The totals change because mixed-purpose methods now choose a severity by reason:
announcer skips, arming cancellation and Zombie team endings. All 71 original
Error sites remain errors. Safety warnings are not behind DeveloperMode.

## Exact remaining Info inventory

Paths below are relative to `ArenaMode.Client/`; line references describe this
reviewed revision and can move with later edits.

| File:line | Message / reason to retain |
|---|---|
| `Plugin.cs:148` | Plugin/Core version and loaded module MVID; identifies the actual running client. |
| `Areas/ArenaAreaCapture.cs:151` | User-requested outline export path; file saved, not activated. |
| `Pickups/ArenaPickupCapture.cs:77` | User-requested pickup draft export path. |
| `Compat/PitFireTeamCompat.cs:1673` | Supported PFT version found; optional squad modes available. |
| `Effects/ArenaLightingDirector.cs:93` | Another mod owns map lighting; explains why Arena lighting is absent. |
| `Hud/InfoPanels/LegacyPanelSuppression.cs:34` | Retired standalone RaidInfoPanels still installed; explains duplicate-HUD prevention. |
| `Combat/WeaponSwapService.cs:497` | Some spare magazines did not fit; explains reduced reserve. Zero reserve remains Warning. |
| `Kits/KitBuilder.cs:531` | Some spare magazines did not fit in the issued kit. Zero reserve remains Warning. |
| `Kits/KitBuilder.cs:599` | Optional grenade unavailable in live database; explains missing item. |
| `Kits/KitBuilder.cs:605` | Optional grenade did not fit usable slots; explains missing item. |
| `Modes/ArenaMatchService.cs:140` | Match started and selected mode. |
| `Modes/ArenaMatchService.cs:190` | Final kills, deaths, score, best streak and duration. |
| `Modes/ArenaMatchService.Abort.cs:124` | Failed-start protected exit accepted; protection remains active through teardown. |
| `Modes/GunGameMode.cs:179` | Weapon ladder unavailable; playable kit-catalog fallback selected. |
| `Modes/GunGameMode.cs:455` | Ladder completed. |
| `Modes/GunGameMode.cs:561` | Final tier and completed ladder count. |
| `Modes/RoundsMode.cs:253` | Deathmatch round result and score. |
| `Modes/RoundsMode.cs:324` | Deathmatch round live. |
| `Modes/TacticalMode.cs:492` | PFT team size / rounds-to-win and protected entry loadout. |
| `Modes/TacticalMode.cs:505` | Solo Tactical starting money. |
| `Modes/TacticalMode.cs:741` | Solo Tactical round live and opponent count. |
| `Modes/TacticalMode.cs:751` | Normal time-limit loss, formerly Warning. |
| `Modes/TacticalMode.cs:951` | Tactical final score and money. |
| `Modes/TacticalMode.cs:1116` | PFT Tactical round live; selected allies versus five enemies. |
| `Modes/TacticalMode.cs:1931` | Normal PFT round result; no-contest remains Warning. |
| `Modes/TacticalMode.cs:2684` | PFT match result and protected exit outcome. |
| `Modes/TacticalMode.cs:2955` | Solo Tactical round win/loss, match score and money. |
| `Modes/TeamDeathmatchMatchFlow.cs:79` | TDM final result, faction score and played time. |
| `Modes/TeamDeathmatchMode.cs:98` | TDM team size and local faction. |
| `Modes/TeamDeathmatchMode.cs:139` | Roster readable after prior warning; refill resumes. Technical state dump removed. |
| `Modes/TeamDeathmatchMode.cs:161` | Delayed requests settled after prior warning; refill resumes. Technical state dump removed. |
| `Modes/TeamDeathmatchMode.cs:176` | TDM teams ready. |
| `Modes/ZombieMode.cs:289` | Wave size/growth, alive cap and selected survival rules. |
| `Modes/ZombieMode.cs:502` | Solo death ends survival without respawn. |
| `Modes/ZombieTeamRuntime.cs:118` | PFT companion count and protected own-gear start. |
| `Modes/ZombieTeamRuntime.cs:179` | PFT wave team ready. |
| `Modes/ZombieTeamRuntime.cs:219` | Cleared wave and companion restoration before next wave. |
| `Modes/ZombieTeamRuntime.cs:430` | Normal team defeat; no-contest now Warning. |
| `Session/ArenaSession.cs:218` | Selected map/mode armed. |
| `Session/ArenaSession.cs:220` | Friendly-fire rule frozen for this raid. |
| `Session/ArenaSession.cs:478` | PFT own gear/container retained and protected. |
| `Session/ArenaSession.cs:490` | Solo Zombie own gear/container retained and protected. |
| `Session/ArenaSideSelectionPatch.cs:91` | Arena disabled with eligibility reason, explaining unavailable menu choice. |

## Developer collection boundaries

The master switch is checked before expensive diagnostic-only work in:

- area connectivity/formation diagnostics: candidate enumeration, sampled paths,
  connected-marker details; real placement/navigation validation is unchanged;
- TDM awareness snapshots: optional SAIN reflection, mover/path snapshots and
  per-bot diagnostic tables; hearing/awareness actions still run;
- Tactical pursuit route/decision traces: diagnostic key/description creation;
  awareness failure and actual stalled-movement warnings remain visible;
- mantle animator traces and motion observations: optional callback details,
  position/capsule samples, read-only observation objects; native movement remains;
- reload observations: diagnostic timers/event sets and animator readback, separate
  from the real animation-speed leases and their cleanup;
- corpse cleanup reports: the diagnostic report scan is skipped, while the actual
  damage-source, aggressor, native lifetime and release checks always run;
- announcer output-state snapshot/reflection after playback; cache creation,
  clip validation/readback and actual playback remain required regardless;
- melee probe subscriptions: only DeveloperMode admits new diagnostic subscribers;
  existing subscribers retain normal death/unspawn cleanup. Totals are explicitly
  developer observations, not guaranteed whole-raid counts if toggled mid-raid;
- marker watchdog snapshots, input traces, per-shot akimbo, per-contact fire,
  friendly-fire proof logging and per-jump physics detail;
- BotLoadBoost diagnostic payload: checked only after its real scheduler lease is
  acquired; release always restores the scheduler before optional output.

Simple implementation counters at 30/60-second intervals remain Debug (zombie
crowd/performance/voices, bot tempo/movement). They do not perform expensive
diagnostic scene scans. Low-cost identities already available for necessary
gameplay checks may still be formatted before the helper's final switch check.

## Reviewed warning changes

- Announcer `disabled`, `volume_zero`, `listener_paused`, `loading`,
  `round_result_pending`, `superseded_callout`, `lower_priority_callout`,
  `countdown_not_currently_playable`, `result_already_queued`, `stale_callout`,
  `queued_callout_canceled` and queued-result cancellation are Debug. These are
  intentional mute/priority/round policies, not broken audio. Missing/invalid
  clips, rebuild/play/output failures, failed PCM capture and failed loads retain
  Warning. Unknown future reasons also retain Warning.
- Arming cancellation is Debug only for the exact known reasons `left raid setup`
  and `new raid negotiation started`. Failed scene loads, negotiation and native
  starts retain Warning; stale-latch/profile/token guards retain their severity.
- Successfully rejecting unowned native/excluded spawn batches is Debug. Actual
  unusable points, wrong faction/count, missing profiles, failed dispatch,
  arrival validation and inability to retire a body retain Warning/Error.
- Successfully retiring a late/rejected spawned body is Debug; the failure to
  retire remains Error. Rejection's originating failure remains visible.
- A pursuit route rejected while another target can be tried is Debug. Actual
  stalled movement, failed identity/awareness, missing ownership and exceptions
  retain Warning with existing per-actor/epoch throttling.
- A native stop after `StandDown` was already set by the protected result/leave
  coordinator is Debug. An unexpected stop interrupting an active respawn
  retains its Warning; setting `StandDown` and all cleanup remain unconditional.
- Solo Tactical time-limit loss is Info. A PFT Zombie no-contest now has Warning,
  while ordinary team defeat remains Info.

All other warning/error sites were retained: optional integration contract
failures; failed HUD/audio/assets; item/kit/native inventory errors; placement,
roster, round-start and respawn failures; profile/server restoration; cleanup and
native event restoration. A successful fallback does not automatically make a
real failed operation harmless or suitable for suppression.

## Validation boundary

`git diff --check -- ArenaMode.Client` passed (existing Git line-ending notices
are not whitespace errors). This leaf did not build, deploy, run raids, modify
tests, or commit. The coordinating agent owns executable checks and independent
review, especially distinguishing diagnostic early returns from gameplay paths.
