# Player log review — 2026-10-15 evening

Read-only audit of the player's new MASCHINE11 session, against the deployed
logging-cleanup baseline `52652c9` / implementation `6dc2e59`. This is the user's
gameplay evidence, not another GPU2 automation run. No production code, settings,
profile, game process or installed file was changed by this audit. Subsequent
feature/fix work must record its own validation separately.

## Findings that matter

- **One real Arena Error is emitted after the confirmed PFT Zombie team defeat:**
  the physical follower-roster monitor still runs during the three-second result
  hold. The error text says a replacement body was registered, but the same branch
  also covers a destroyed/missing original body. The log does not prove a new
  body actually appeared. This deserves an exact terminal-lifecycle guard, not
  lowering or suppressing live-roster safety failures.
- **Five infected profile preparations returned no usable profiles.** Recovery
  continued: all 50 first-wave and all 70 second-wave infected arrival messages
  are present. This was neither a permanently short wave nor a server error.
  The underlying cause of the empty native preparation remains unproven.
- **Two genuine kit-capacity failures in Gun Game:** the M60 rung could not stow
  either 2×2 spare ammunition box; the following Enforcer respawn could not stow
  its 2×2 Grizzly. These are inventory-layout limits, not a weapon-pickup,
  examination, magazine-reach or reload-speed failure.
- Both raids reached the protected server end, which logged snapshot restoration
  and successful protected completion. There is **no server Error** in the
  matching session. A success log is not an independent before/after profile
  byte comparison, which was not performed for this user's session.
- No native damage/`BotMemory` exception recurred here. The earlier investigation
  remains open; these two raids do not establish a universal fix.

## Evidence selection and build identity

Raw snapshots, complete parsed events and their stacks remain private under the
ignored `artifacts/followups-20261015/logs/`. `analyze.py` parses actual severity
headers, not occurrences of words such as Error in Harmony method names.

| Snapshot | Bytes | Source last-write UTC | SHA256 |
| --- | ---: | --- | --- |
| `BepInEx/LogOutput-MASCHINE11-2026-10-15_19-42-06.log` | 2,281,444 | 18:06:50.597 | `174175F093C3A78D85064B6B4BCD02E89CFF9F8AA2D59D1E455CEBC3C0BDC1A9` |
| `SPT_Runtime/user/logs/spt/spt-MASCHINE11-20261015.log` | 825,809 | 18:06:53.942 | `8D799AA3D647FE04D60146BB9016328057598B97349FA4437A5660C233925090` |
| `SPT_Runtime/user/logs/spt/spt-SPT-GPU-2-20261015.log` | 1,165,769 | 17:23:48.465 | `709D514E417EB8AC7962544446F37D899A86BED156041846D3ECF6DBDFA69C4F` |

The third file was checked only to exclude the wrong host: it has no events in
the user's 17:42–18:06 UTC window. GPU2's earlier 18:41 local client run belongs
to the previous automated test, not this session.

Client line 7681 at **19:42:12.884 CEST** explicitly reports loaded Arena client
MVID **`563da9c1-363a-49eb-b83c-f390dd4e6817`**, matching the final deployed client
SHA256 `4D8E897F0E7C4A4C660AED51D373CD168CA9012FE0A0301D776B16E80DA026A8`
recorded in `HANDOFF.md`. This is a loaded MVID match, not a new process-memory
hash measurement. The server log does not expose a loaded MVID/hash; its routine
Arena events use Debug as expected, but an exact server binary identity cannot
be independently inferred from its timestamps or the current disk DLL.

Client times below are Europe/Berlin, **UTC+02:00**; server times are UTC. The
matching server window is **17:42:00–18:06:54 UTC**. Full-day server totals are
51 Warning / 0 Error, whereas only **20 Warning / 0 Error** belong to this
window. Earlier sessions must not inflate the user's counts.

## What the player actually exercised

### Customs Yard: protected Zombies with two companions

- Arm 19:45:03.048, native match start 19:45:27.698. Exact three-person team ready
  19:45:31.635. Server location clone identifies `customs-yard-polygon-v1`.
- Settings logged: MaxAlive 32, first wave 50, growth 20, spawn interval 0.2 s,
  intermission 10 s, shooter share 20%; own gear and protected PFT persistence.
  Friendly fire was blocked for this frozen raid.
- Wave 1 cleared 19:47:32.082. Two fresh companions passed adoption and the
  stable registry barrier; team ready again 19:47:36.815. First wave-2 arrival
  19:47:46.905, consistent with its ten-second intermission.
- Local death 19:49:09.403, then companion spectator. Team defeat was reported
  19:49:11.780; protected native stop 19:49:14.772. Final result: **70 own kills,
  1 death, score 11,371, best streak 70, 3.8 minutes**.
- Server 17:49:14.818 suppresses the protected PFT death-outcome persistence;
  17:49:16.031 restores the snapshot; 17:49:16.075 confirms protected end and
  persistence, skipping ordinary post-raid processing.

This is also direct evidence for the user's refill complaint: the then-current
PFT implementation rebuilds companions between waves while preserving the
surviving PMC body and consumed equipment. It is the documented old behavior,
not an exception or a failed refill transaction in this log.

### Equator: Gun Game

- Arm 19:52:18.317, match started 19:52:28.817. Initial swap plus nineteen death
  replacements account for twenty Redeploy replacement events.
- Completed one 24-rung ladder at 19:58:24.625 and continued to rung 14/24.
- Normal exit 20:02:47.338: **37 kills, 19 deaths, score 8,837, best streak 6,
  10.3 minutes**. Server snapshot restored 18:02:47.441; protected completion
  confirmed 18:02:47.481.
- Optional killcam worked repeatedly, with two missing/empty killer recordings
  correctly falling back without replay. They did not block respawn.

## Complete client Warning/Error inventory

The whole captured process has **139 Warning / 2 Error**. Grouping below covers
every header, including startup, scenes, combat and teardown; none is discarded
because its text happens to contain an exception at Warning severity.

| Signature | Count | Evidence and attribution |
| --- | ---: | --- |
| LogTools cannot create current-log symlink | 1 W | Line 2. Host privilege/alias configuration; explains the missing `LogOutput.log` alias. No elevation/config change made. |
| Optional Fika DLL serialization exclusions | 4 W | Lines 16–19: MedicRound, UIFixes and WTT optional Fika references. Pre-raid, unchanged external dependency family. |
| Harmony inherited-method targets | 4 W | DynamicMaps `BattleUIScreenShowPatch`, UIFixes `DialogWindowPatch` and `WeaponModdingScreenCursorPatch`, WTT `IsConditionDone`. Source attribution matches the earlier audit. |
| UiBridge binding address not on this PC | 1 E | Line 9067, 19:42:15.349. Host-specific automation configuration, before Arena arm. Network addresses deliberately omitted. |
| WheelCollider missing Rigidbody | 4 W | Lines 14944–14947, Customs scene load at 19:44:53.549–.551. Native asset warnings without owning stack. |
| Negative BoxCollider scale/size | 34 W | 33 on Customs load: mostly `ServerSpatialController/.../AudioTriggerArea`, one `BordersColliders/RoofBorder (2)`. One at 19:55:47.015 belongs to an AK-74 handguard's Vltor CASV rail LOD1. Arena supplies weapon variants, but the log does not establish who authored/mutated its negative scale. |
| No infected profiles after native preparation | 5 W | Two infectedPmc, three infectedAssault, 19:47:52.356–19:48:11.057; source and successful retry evidence below. |
| PFT result-row victim unavailable | 1 W | Line 15604, 19:49:11.754. PFT `SquadRaidKillReport.RecordTeammateKill` could not find a matching native victim-stat row and skipped copying it. No damage is cancelled by that warning. The missing-stat cause and possible Arena interaction are not established. |
| Physical PFT roster rejected after defeat | 1 E | Line 15608, 19:49:11.794. Direct Arena terminal-lifecycle finding below. |
| `Calling Animator.GotoState on Synchronize layer` | 19 W | Eighteen during Zombie teardown at 19:49:15.848–.866; one during Gun Game at 20:01:46.911. No managed stack or animator identity. Cannot classify the whole family as shutdown-only, harmless or conclusively external. |
| Renderer in multiple LODGroups | 4 W | Lines 15704–15707, Equator load at 19:52:17.141–.143; drink-refrigerator shadow objects. Asset hierarchy warning, no demonstrated ArenaMode mutation. |
| Intentional `RespawnApiProbe.AlwaysThrow` subscriber | 20 W | Lines 16025–18226. Installed test fixture deliberately throws `InvalidOperationException` on every replacement. Core logs it, then delivers other subscribers and completes the replacement. Not twenty spontaneous production failures. |
| Redeploy stale PerfectCulling FPS-camera sampler cleanup | 20 W | Paired with all twenty replacements. Arena invokes replacement; Redeploy owns this recovery. No associated stuck replacement in this run. |
| ArenaMapPort missing impact SoundBank | 1 W | Line 16356, 19:53:06.331, `SoundType=Books` on `shoe_sport_Single1_Physic (1)`. Owned MapPort fallback leaves that prop silent. Not an Arena announcer failure. |
| Missing script on `<null>`, `Unknown`, `extractor_smoke` | 18 W | Six of each text, 19:53:19.744–19:56:37.640. Native asset/object warning without stack; ownership of the instantiated effect is unresolved. |
| Killcam killer track empty/missing | 2 W | Lines 17389/17689, 19:57:59.426 and 19:59:21.526. Zero snapshots once, no recorded track once; replay skipped and next replacement follows. Existing bounded fallback, not a proven new recording regression. |
| M60 spare boxes do not fit | 1 W | Line 17444, 19:58:04.294. Both 2×2 reserve boxes refused by native inventory placement. |
| Enforcer Grizzly does not fit | 1 W | Line 17463, 19:58:17.782. 2×2 medical item refused during the following kit build. |

### PFT error after the already determined result

The ordered client events at lines 15604–15610 are:

1. PFT cannot copy one victim-stat row (19:49:11.754).
2. Arena cancels the follower spawn lease and releases the spectator
   (19:49:11.779–.780).
3. Arena explicitly confirms defeat because every team member is dead
   (19:49:11.780).
4. The global physical-roster monitor emits its replacement-body error
   (19:49:11.794), **14 ms after the terminal result**.

At the reviewed baseline, `ZombieTeamRuntime.EndSurvival` sets its phase to
Ended, holds respawn and schedules `EndTeamRaid` three seconds later.
`PitFireTeamCompat.BeginFinalizing` is only called by that later exit.
`ArenaController.Update` still runs `PitFireTeamCompat.Tick`; its
`LiveTeamArena && !RoundTransitionActive` condition calls `GetLiveFollowers`.
The check in `TryGetLiveFollowers` around lines 1207–1219 treats either a
different ever-existed player **or null/destroyed original/registry player** as
the same replacement-body violation. The log records no actual new follower
activation after the defeat, and provides no object identities for this check.

This is a source-backed mismatch between terminal-mode lifetime and the global
monitor, not proof of an unauthorized new live teammate. The match already had
its legitimate result and the server protected end succeeded. A fix should stop
live roster discovery only for the exact terminal raid, preserve already latched
errors and persistence suppression, and retain strict live/staging/rebind
replacement checks. The finding was handed to the owner of the current Zombie
changes; this audit does not itself claim the fix or its acceptance.

### Five empty preparations, with full wave population

`BurstSpawnService` logs only after `BotCreationData.Create` returns null/empty
while the captured raid and request still match. That does **not** say the HTTP
server returned an empty JSON response. Native `BotCreationData.Create` delegates
to the creator/profile cache and can retain no usable profile; no matching
generation exception or HTTP failure appears here.

`ZombieSpawnService.OnDispatchFinished` returns preparations that never reached
native dispatch to the failed-dispatch ledger; `ZombieMode.Tick` restores those
wave slots before clearance decisions. Captured arrivals count **50 + 70**, not
50 + 65. The next infected arrives 0.461 s after the first warning; the last
failed infectedAssault preparation is followed by a successful infectedAssault
arrival 0.237 s later. Successful subsequent arrivals are not proof that each
one was the very same request ID, which is not in these messages.

Keep this warning and the original lower-level evidence. A future diagnosis
would need the exact profile-provider/cache result and request identity before
blaming SPT generation, APBS, SAIN, the network or hardware.

### Kit capacity is real, not a logging change

`WeaponSwapService.HandOutSpares` submits every spare to
`KitBuilder.AddToAnyGridOf`; zero placed logs Warning while a partial reserve
logs Info. The player reached M60 rung 24 at 19:58:03.513; both 2×2 Assault Boxes
were refused, leaving the loaded weapon without those reserves.

After the next death, the log names the Enforcer kit immediately before the
Grizzly refusal. `KitBuilder.FillGrids` places spare ammunition first, followed
by authored medical fill and then optional quick-use grenades. Native placement
reported no suitable free cell in the existing tactical backpack. The captured
log does not record the complete grid layout, so the specific blocking items
and optimal repack are not established. This is a separate real loadout-supply
limitation worth testing with the actual randomized rung and kit combination.
Changing the warning severity would not restore the missing supplies.

## Server review and remaining noise

In the matching 17:42:00–18:06:54 UTC window:

| Signature | Count | Assessment |
| --- | ---: | --- |
| ProfileController account preview fallback | 11 W | 17:42:49–17:43:41, before either Arena arm. Account lookup for profile/companion preview; no Arena inventory mutation established. |
| Fence missing Front_plate slot | 5 W | Native trader assortment generation. |
| Fence missing Back_plate slot | 4 W | Same family. Donor template/mod not identified by these generated item IDs. |
| Error / Fatal / Critical | 0 | No matching server-side failure or unsuccessful protected-end entry. |

There are 64 SPT middleware Info entries for `/arenamode/client-settings` in this
window. Those are still emitted by `SptLoggerMiddleware`, not the Arena logger.
The client has 8,256 Info entries: HarmonyX contributes 6,000, ModulePatch 1,256,
KillcamReplay 242 and RespawnApiProbe 176. Global `LogLevels=All` also exposes
technical Debug messages. This audit changes neither global logger levels nor
another mod's logging. Arena client Info itself consists of 18 player/support
events, and there are no `[dev]` events in this captured session.

Zombie announcer silence has an explicit non-error explanation: at attachment,
`enabled=False`, 41/41 clips ready, volume 0.7. All 61 callouts were skipped as
disabled. In Gun Game it was enabled: 15 play requests, zero skipped, including
three cues. Those logs prove playback requests, not acoustic output at speakers.

## Regression comparison and review method

The review used four passes: (1) exact host/build/time selection and immutable
snapshot hashes; (2) complete header/stack inventory with grouped counts;
(3) baseline Arena/native/PFT source tracing and server correlation; (4)
adversarial checks against overclaiming causality, intact result/protection logs,
and the previous logging audit/native report.

- The previous expected-stop warning fix remains effective: the normal PFT
  terminal stop is now Debug, not a new unexpected-respawn Warning.
- The PFT terminal physical-roster Error is a newly observed case, not a
  reclassification of that stop warning and not evidence the match failed to end.
- The historical `BotMemory.GoalTargetBeingHitAction` damage NRE is absent here,
  but remains unresolved. No damage exceptions were hidden by this review.
- AirPit's 630-decal shutdown burst is absent because these raids used Customs
  and Equator. This is **not** acceptance of an AirPit/ArenaMapPort repair.
- Animator warnings recur, including one during Gun Game. Their owner still
  needs native context rather than a blanket filter.
- Two successful protected-end log entries do not certify all profile fields;
  no original user profile snapshot was available to this read-only audit.
- No FPS, memory-safety, long-session or all-mode regression guarantee is made
  from these log observations.

Related reports: [earlier warning/error audit](LOGGING-ERROR-AUDIT-20261015.md),
[GPU logging acceptance](GPU-LOGGING-TEST-20261015.md),
[protected Zombie contract](ZOMBIE-PITFIRETEAM-REVIEW.md),
[logging policy](LOGGING.md).

## Independent source review of the proposed follow-ups

After completing the evidence audit, this reviewer inspected the parent's
terminal-monitor change and KillcamReplay commit `97c6f4e` without editing either.

- The PFT monitor still drains pending raw player completions unconditionally.
  It skips only its obsolete live follower poll when the current mode is Zombie
  and `TeamResultOwnsCurrentRaid` proves a completed team plus the exact current
  game, world and match. Solo, stale/other raids, live combat, initial assembly
  and rebinding do not gain this exemption. Existing latched problems and
  persistence protection are untouched. No actionable source-review issue found.
- KillcamReplay uses `EffectiveLiveTailSeconds` for both the LiveHold cutoff and
  the extra-delay bound. Its multiplier is 0.5 only while Arena's actual session
  is active; the saved duration, recorded playback, slow-motion calculations,
  fades and ordinary raids remain unchanged. The existing compatibility lookup
  fails back to ordinary behavior if Arena cannot be resolved. No actionable
  source-review issue found.

This source review is not build or native acceptance of either new change.
Those results belong to the implementation/test report, not the user's earlier
log snapshot above.
