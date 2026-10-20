# Optional-mod native checks — 2026-10-20

## Phase 1: Classic Movement, StatTrack and SVM

This section covers SPT-GPU-2 through 09:09 local time (Europe/Berlin, UTC+2).
Scenario JSON timestamps use UTC; the client log uses local time and the server
log uses UTC. Runner and GPU-host clocks are not precisely synchronized; use
host log times for the native execution order, not millisecond comparisons
with a scenario step's start timestamp. Later CLA/Deminvincibility tests and final cleanup are outside
this section.

The test target was ArenaMode commit `235cc69`, SPT 4.1.6, Classic Movement
1.1.3 and StatTrack 2.1.1. The second client process logged ArenaMode 0.1.1 with
MVID `bd1c0060-6075-4bbb-a8bc-bce666cf7b3d` at 08:52:53.173. Its startup confirmed
both the Classic Movement state/speed guards and StatTrack collection/end-merge
guards. UiBridge reports identified plugin 0.9.4 and matching deployed files.

Evidence is retained under `artifacts/native-optional-compat-20261020/`:
the `cm-st-*` scenario reports and profile captures, `svm-scav`,
`svm-ignore-after-pmc`, and `cm-st-svm-client.log` / `cm-st-svm-server.log`.
Raw profiles are ignored local evidence, not release assets.

### Observed lifecycle and shots

| Check | Native observation | Result |
|---|---|---|
| Ordinary PMC before Arena | `OldTarkovMovement.OldMovementStates.OldIdleState` was active. A later clean ordinary end reached the menu and `/stattrack/save`. | Classic Movement and ordinary persistence remained available. |
| First Arena Deathmatch, 08:59–09:00 | Three distinct local-body handles across two deaths/respawns all reported `EFT.IdlePlayerState`. The raid exited through its normal UI flow. | Arena used native movement states across replacement bodies and completed its protected end. |
| Ordinary PMC after that Arena | The saved StatTrack shot counters increased from 14 to 19. The client recorded a 300 ms fire action at 09:02:13 and subsequent ordinary extraction/save. | Ordinary collection resumed. The five-shot increase is independently visible in the saved data. |
| Arena with actual fire, 09:04–09:05 | Four AKM shots were observed as sequence 6–9; after respawn, one RSASS shot was observed as sequence 10. A second respawn produced a third distinct local body. All inspected states were `EFT.IdlePlayerState`; the in-raid StatTrack dictionary remained empty. | Five real Arena shots and two body replacements did not enter persistent weapon statistics. |
| Ordinary PMC after the second Arena, 09:06–09:07 | `OldIdleState` returned. Three MP5 shots were observed as sequence 11–13; the raid dictionary contained two entries. Normal disconnect/confirmation reached the menu. | Ordinary movement and tracking still worked after both Arena ends. |
| Ordinary Scav, 09:07–09:08 | Factory loaded and normal disconnect/confirmation returned to the menu; the server received the ordinary end. | Scav lifecycle remained available with the tested SVM preset. |

The initial fire attempts in `cm-st-pmc-before` and `cm-st-arena` explicitly
reported **NO SHOT observed**. A successful fire-command receipt alone is not a
shot. Those attempts support lifecycle/state observations only and are not
counted as StatTrack fire coverage. The later Arena report contains the actual
shot sequence/frame observations and magazine changes. The earlier ordinary
five-shot interval has a persisted counter delta and client action receipt,
rather than a separate saved per-shot scenario report.

The native controls used UiBridge fire and death actions, not direct invocation
of a foreign mod's methods. Ordinary fire tests used UiBridge infinite-ammo
support; this does not claim normal ammunition consumption. These checks cover
movement-state ownership and lifecycle restoration, not every Classic Movement
setting or movement animation.

### Persistence checks

Both captured Arena intervals were independently compared:

| Capture pair | Inventory, Health, Skills, Quests, Encyclopedia | StatTrack |
|---|---|---|
| `cm-st-before-arena` → `cm-st-after-arena` | All five fields identical | File byte-identical; shot counters 14 → 14 |
| `cm-st-before-arena-fire` → `cm-st-after-arena-fire` | All five fields identical | File byte-identical; shot counters 19 → 19 |

Hideout differed only at `sptUpdateLastRunTimestamp` in each pair. No other
Hideout field changed. StatTrack's weapon-instance and weapon-template entries
both followed **14 → 19 → 22** through the ordinary raids; these are two views
of the same shots, not additive totals. The other seven saved counters remained
unchanged. No new hit/kill-statistics acceptance is claimed by this test.

The server recorded snapshot restoration and completed protected Arena ends at
08:59:53.621 and 09:05:21.321 local time. Neither Arena end emitted
`/stattrack/save`; subsequent ordinary PMC and Scav ends did.

The later ordinary PMC Ignore capture is not another Arena equality check:
Inventory, Health, Quests and Encyclopedia remained unchanged, while three
`Skills.Common[].PointsEarnedDuringSession` fields were reset to zero
(StressResistance, Perception and Charisma). Its Hideout timestamp also advanced.
These differences occurred outside the protected Arena intervals.

### SVM admission and raid ends

The existing SVM 2.2.1 installation was an admission-negative control. At
08:47:18.548 local time, the server rejected Arena compatibility because the
adapter requires SVM 2.2.3 or newer. An ordinary PMC raid still completed.
The user subsequently chose to remove the old SVM version rather than support
it. The attempted legacy investigation was cancelled; no legacy implementation
or commit remains in the working tree.

The published SVM 2.2.3 binary was then tested, with SHA256
`7A142971404A4D84A20D855837EBED1E1E07921B05BA714C9F49B47BE7CFCADB`.
The server logged its protected-end adapter ready at 08:52:36.434 local time.
The dedicated test preset started with raid overrides disabled; the test driver
subsequently exercised SaveLoot and Ignore settings.

The SaveLoot run returned an ordinary PMC to the menu; its before/after
Inventory, Health, Skills, Quests and Encyclopedia captures were unchanged.
The first Arena lifecycle then restored its protected snapshot. The second
Arena scenario explicitly exercised the Ignore run and again restored its
snapshot after actual firing and respawns. A following ordinary PMC retained
StatTrack's additional three shots, and the ordinary Scav end also completed.
This demonstrates ordinary → Arena → ordinary operation with the tested
installation; it does not certify every SVM database option or future version.

### Test-path failures and remaining log observations

- The first extraction script hit UiBridge's existing control lease and stopped
  without taking over. Its subsequent retry reached the menu.
- Teleporting directly to the Factory Gate 3 marker later failed to trigger
  extraction. Moving outside the trigger and walking into it succeeded; the
  driver allowed an approximately 18-second extraction wait. The saved client
  log records the walk at 09:02:57, followed by the ordinary save/end. This was
  a test-path problem, not a demonstrated raid-end adapter failure.
- The client logged `Coroutine couldn't be started because the the game object
  'MenuScreen' is inactive!` at 08:58:20 and 09:06:57 during ordinary exit flows.
  Both flows subsequently returned to the menu. No stack in the saved log
  establishes which mod caused this, so it is retained as unresolved rather
  than attributed to Arena or dismissed as a clean log.
- Startup reported empty `LateToTheParty` and `Skwizzy.LootingBots` server-mod
  directories, including before the 2.2.3 run. Other retained warnings concern
  missing Fence armor slots, a trader fallback, profile item repairs, companion
  profile lookups, inherited-method Harmony hooks and Factory's negative-scale
  collider. These do not constitute new CM/StatTrack/SVM adapter exceptions.

No protected-end failure, lost ordinary tracking, or movement-state leak was
observed in these bounded checks. Final temporary-mod cleanup and later test
phases must be recorded separately.

## Phase 2: Continuous Load Ammo

This phase used the same ArenaMode build/MVID with published Continuous Load
Ammo 1.2.0. The installation manifest records DLL SHA256
`241B4F8FB5D5B99305854309B8C7E3E8486392354258971CBB0879C104430B7E`.
The client loaded CLA at 09:11:19.089 and reported Arena's controller/rebind
guards ready at 09:11:19.864. Ordinary Factory PMC, Arena Deathmatch and another
ordinary Factory PMC ran in that same client process, before its exit at 09:25.

Evidence: `cla-client.log`, `temporary-cla-installation.json` and
`temporary-cla-installed.json` under the evidence directory above. The timeline
and action receipts below were checked against the saved client log. Inventory
counts, controller/process reads and per-shot frames were direct UiBridge
observations reported by the test operator; those response payloads are not
reproduced in the client log. A successful action receipt alone is not being
used as proof that loading or firing actually occurred.

### Native operations and replacement bodies

| Check | Observation and boundary |
|---|---|
| Ordinary unload before Arena | The MP5's 20-round magazine contained 19 rounds and was moved to Pockets. `UnloadAmmo` at 09:17:27.798 followed by `ToggleInventory` at 09:17:27.829 left CLA `IsActive=true`, inventory closed and a native `UnloadMagazineProcess`; the observed magazine decreased through 5/20 to 0/20. |
| Ordinary load and cancellation | Dragging the recovered M882 ammunition onto the magazine started a native `LoadMagazineProcess`. With inventory closed, CLA remained active and 6/20 rounds were observed. `ToggleShooting`/`EndShooting` at 09:18:02 stopped the operation at 12/20. These commands are cancellation input here, not evidence of a shot. |
| Initial Arena swap | The raid-start living-body swap completed at 09:19:33, with CLA retirement and successor registration. No pending load was established at that swap, so this does not satisfy the pending-work live-kit test. |
| First two Arena deaths | The initial SCAR/Mk17 unload had already completed when inspected. A subsequent load/death attempt could not establish `IsActive` because the inspector handle had been collected. The next shotgun kit did not provide a suitable magazine; two test items were supplied inside Arena. Deaths at 09:20:40 and 09:21:38 replaced the body, but neither counts as a confirmed pending-load test. |
| Confirmed pending unload, STANAG | Native `UnloadAmmo` at 09:22:00.465 and inventory close at 09:22:00.506 were followed by separate reads confirming CLA active with a native `UnloadMagazineProcess`. The native test death at 09:22:09.311 completed physical replacement pipeline #4. |
| Confirmed pending unload, AK55 | The 30-round magazine underwent native `UnloadAmmo` at 09:22:40.914 and inventory close at 09:22:40.938. CLA active and `UnloadMagazineProcess` were again confirmed before death at 09:22:49.631 and replacement pipeline #5. |
| Hands after the final replacement | A 350 ms MCX fire action at 09:22:57 produced five observed shots: sequence 1–5 at frames 67540, 67549, 67556, 67565 and 67574. This confirms actual firing after replacement, not merely command delivery. |
| Ordinary control after Arena | After the normal Arena leave flow, the following PMC again unloaded the MP5 magazine. `UnloadAmmo` at 09:24:48.787 and inventory close at 09:24:48.809 left CLA active, inventory closed and 3/20 rounds remaining when inspected. Ordinary CLA functionality therefore remained available after Arena. |

The log independently counts **four Arena deaths**, four successful death-body
replacements and the initial living kit swap. Only the two explicitly inspected
unload cases above establish pending magazine work before death. The normal
leave flow called native `Stop(Left)` at 09:23:08.464 and returned through the
menu into the next ordinary raid. Its early `/arenamode/raid-end` response was
deferred until the native end; that response alone does not prove snapshot
restoration.

### Test settings, log findings and remaining scope

For timing, Arena `Character/MagazineHandlingSpeed` changed from 2 to 1 at
09:18:58.310 and `Character/MaxSkills` from true to false at 09:20:12.990.
UiBridge godmode prevented incidental deaths; its explicit kill action still
drove the real death/replacement pipeline. The two supplied items existed only
in the protected Arena test. Restoring these settings and removing temporary
CLA files belongs to the final cleanup record, not to this phase's acceptance.
SVM SaveLoot was inadvertently enabled before this client/server run; this CLA
sequence is not another isolated SVM SaveLoot/Ignore comparison.

No CLA controller, delayed-completion or respawn exception appears in the saved
log. The known inactive-`MenuScreen` coroutine error recurred at 09:18:24.592 and
09:25:02.228 during ordinary exit flows, without an attribution stack. Both
flows continued. Startup retained the existing Fika serialization exclusions,
inherited-method Harmony warnings and Factory collider warnings. Developer
diagnostics also reported missing muzzle-jet materials and a headset custom-type
check on inactive pooled objects; these do not establish a new CLA failure.

The respawn diagnostics additionally show rising subscriber/job counts across
the four deaths. These counters need separate lifecycle interpretation; this
run alone does not establish a leak or a cleanup failure.

This is bounded native acceptance of ordinary load/unload/cancel, continuation
with the inventory closed, two pending-unload death replacements and the
ordinary control afterwards. It does **not** certify the 800 ms newer-operation
race, a pending-work living kit swap, preset/MultiSelect cancellation, the CLA
quick-load hotkey, a measured movement-limit reset or PFT/replay transitions.
No production change was justified by the CLA observations in this phase.

### CLA followup during the first Deminvincibility process

The next client process kept CLA installed alongside Deminvincibility and SVM;
its evidence is `demi-onehp-client.log`. Deathmatch was configured for three
two-minute rounds. It reached round 3, then exited manually; this was not a
completed three-round match.

The living transition from round 1 to round 2 completed replacement pipeline
#4 at 09:31:24.513. Direct inspection showed local `Player.Id` changing from
7 to 8 and CLA's `_player.Id` matching 8. A subsequent RSASS fire action at
09:31:55 produced one observed shot. The living round 2-to-3 transition completed
pipeline #5 at 09:33:39.585, followed by one observed SKS shot at 09:33:59.
These are additional native living-body rebind and usable-hands observations.

Neither attempt established pending CLA work across the living replacement.
The first unload had completed by the CLA completion diagnostic at
09:31:15.969, over eight seconds before the swap. Before the second swap,
ammunition was dragged onto a magazine with roughly six seconds remaining;
the first `IsActive` inspection arrived only after replacement. The countdown
and a drag input cannot substitute for a confirmed pre-swap active process.
The pending-work living-swap case and the other untested cases above remain open.

## Phase 3: Deminvincibility — 1 HP Mode

This section covers the fresh client process beginning at 09:26, still on
ArenaMode MVID `bd1c0060-6075-4bbb-a8bc-bce666cf7b3d`. Deminvincibility 2.2.0 loaded
at 09:26:30.380; its Arena adapter was ready at 09:26:36.641. CLA 1.2.0 and SVM
remained installed. The manifest records Demi DLL SHA256
`F34E19DF4257F6D35C202B39599F613C76458FAE8D27912042F37B18CED07C4A`.

Evidence is `demi-onehp-client.log`, the temporary-Demi installation manifests,
and `demi-onehp-after/report.json`. As in the CLA section, exact health values,
body IDs and observed shot counts came from direct UiBridge responses; the
saved client log independently records hook receipts, the guard paths, give-up,
replacement pipelines and ordinary return.

All 43 loaded plugins matched the installed binaries. The operator inspected
Demi's nine configuration entries. For this case,
`1. Health / 1 HP Mode` was enabled and
`2. Death / Enable second chance protection?` remained disabled. Damage remained
at the existing 99% setting; other health preferences were not changed.
These are temporary test selections, not new defaults or a completed final
settings-restoration check.

| Check | Native observation | Scope |
|---|---|---|
| Ordinary PMC before Arena | `health Head=0` at 09:28:14.929 left the head at 1/35 HP. A native kill hook at 09:28:18.187 left the player alive. | Demi's ordinary 1 HP/death protection remained active. |
| Arena head-damage probe | `health Head=0` at 09:29:31.597 showed a rounded 0/35 while the player remained alive. | The retained 99% damage setting means this is not proof of an exactly zero-health head or a failed lethal transition. |
| Arena guard paths | Developer messages recorded the local lethal-damage, limb-destruction and death guards at 09:29:31.592, 09:29:37.996 and 09:29:40.415 respectively. | These establish that the corresponding adapter paths ran; they are not a full destroyed-limb/medical acceptance test. |
| Arena native death | The kill hook at 09:29:40.431 reached death and a replacement body; pipeline #2 completed at 09:29:43.345. | Demi did not prevent Arena's death/respawn path. |
| Arena give-up | The real Backspace input produced `Arena: player gave up — respawning` at 09:30:02.194, death #2 and successful pipeline #3. | The native give-up feature remained usable with 1 HP Mode enabled. |
| Living round changes | Round 1→2 and 2→3 completed living replacements. CLA's owner was explicitly matched to the new body at the first swap; actual RSASS/SKS firing was observed after the respective swaps. | Combined-mod body lifecycle worked in these two timed transitions; the direct CLA owner-ID check covers the first one. |
| Ordinary PMC after Arena | At 09:35:36, the repeated head-health and kill controls again left the player alive with 1/35 head HP; ordinary exit returned to the menu. | Ordinary protection remained available in the same process after Arena. |

The `demi-onehp-after` scenario report correctly remains **failed**: after
successfully leaving Arena and starting the ordinary PMC raid, its first health
action hit the existing test operator's control lease (`409 conflict`). This is
a driver-coordination failure, not a refused game health operation. The operator
then performed the health, kill and player-state controls manually and left the
raid; the successful hook receipts appear at 09:35:36.763 and 09:35:36.813.
The manual continuation is recorded separately rather than changing the failed
scenario's result.

No Demi/CLA adapter exception or failed replacement appears in this log.
The inactive-`MenuScreen` coroutine error recurred at 09:28:26.497 and
09:35:47.584 on ordinary exits. At 09:29:34, missing-script warnings, including
`extractor_smoke`, accompanied Mossberg model-pool activity. Their lack of an
attribution stack prevents assigning them to the compatibility adapters.
The existing collider/Harmony/serialization warnings and pooled-model
diagnostics remain distinct from the health checks.

At 09:36:18, 1 HP Mode was disabled and Second Chance enabled in preparation
for a separate fresh-process test. That configuration change is **not**
Second Chance acceptance. Its ordinary protection, debounce, Arena behavior,
solo-Zombie terminal death, PFT/replay transitions and final cleanup must be
reported from their own observations. This phase establishes only the bounded
1 HP/give-up/lifecycle checks above; it does not close the complete Demi matrix.

## Phase 4: PFT Zombies and early terminal-exit regression

The fresh process `89bb2d99` used Second Chance, with 1 HP Mode disabled.
CLA and SVM remained installed. Factory PFT Zombies used one invited companion,
one zombie in the opening wave, zero wave growth and a four-zombie live cap.
These were test values, not new defaults.

The direct observations are saved in `pft-demi-direct-observations.json`; the
client receipts are in `demi-secondchance-pft-before-recovery.log`.

- Wave 1 became ready at 09:38:29. Four actual MP5 shots and a zombie headshot
  kill were observed. UiBridge then ended its fire hook because it classified
  the PFT companion as another human. The guard was not bypassed; only the four
  shots already recorded count as fire evidence.
- The surviving PMC's entry gear was refilled at 09:38:51.288. The local body
  changed from ID 1 to ID 4, and CLA's registered player also had ID 4.
  The refreshed team was ready at 09:38:53.
- A native local kill at 09:39:21.164 entered the team's held-death state.
  Live spectating of the companion began at 09:39:21.193. The companion cleared
  the wave, and the next team was ready at 09:39:26.052. The replacement local
  body and CLA owner both had ID 7; health was 440 and the entry MP5 was restored.
- Demi's `HasSecondChance` remained true and `IsDebouncing` false after the
  Arena death. The ordinary protection had not been consumed.
- The server completed the protected end at 09:40:01.362.

This covers living and dead-player PFT wave replacements and live spectating.
It does not prove killer-camera replay, pending magazine work during these
specific swaps, or a separate Tactical PFT match.

### Discovered Arena exit bug

A subsequent solo-Zombie test killed the player at 09:41:36.133, about 102 ms
after the mode started. Native Stop committed at 09:41:36.385, but the game
remained in the dead raid for more than 60 seconds and sent no protected end.
Source inspection identified the delayed native raid-intro fade replacing the
GameEnd coroutine as the likely cause; that internal callback was not directly
recorded in the original stalled run. The corrected retest below records the
guard and successful end. This is the terminal-result version of the already
documented failed-start intro race, not a demonstrated CLA, SVM or Demi adapter
failure.

EFT was gracefully closed while the same SPT server was kept running. On the
next arm, the retained snapshot was restored and its protected end persisted
at 09:58:50.352. No profile edits, fabricated raid-end request or identity
bypass were used.

Commit `5780942` adds an exact terminal game/world/profile/arm-epoch lease to
the existing intro guard. Only a committed native Stopping/Stopped transition
can suppress that one obsolete intro callback. Normal fades, native Stop and
GameEnd remain unchanged. Raid end and new-raid recovery clear the lease.
See [the implementation and offline controls](FAILED-START-INTRO-REVIEW.md).

The production-linked suites passed 287 TDM, 145 intro/failed-start and 85
solo-Zombie assertions, plus 27/25/13 compiled negative controls respectively.
Independent review and separate baseline reruns passed. The commit-bound client
build had zero errors and the four existing CS8632 warnings; it was installed
on both roots with SHA256
`8A5C9A9852E2496095E838C70201E900ACB49FB0BEC91BA3BB90E4DF867455BD`.
Server and companion remained at the previously tested `235cc69` build.

### Native terminal-exit and Second Chance retest

Process `e1923519` loaded the corrected client; UiBridge compared its loaded
module with both installed files. The scenario report is
`terminal-zombie-retest-5780942/report.json`. All 21 driver steps completed.
The scenario has zero automatic expectations, so acceptance is based on the
recorded values and independent log observations, not merely its green status.

| Case | Observation |
|---|---|
| Death directly after startup | Mode start 09:59:07.078; Stop committed 09:59:07.519; the obsolete intro was skipped at 09:59:07.610. The server restored/persisted the protected end at 09:59:08.650. The raid unloaded and returned to the menu. |
| Death after 15 seconds | Stop committed at 10:00:10.728; protected end persisted at 10:00:11.861; normal result/menu flow completed. |
| Ordinary PMC in the same process | `HasSecondChance` was still true. The client recorded native kill at 10:01:13.092; it left the player alive. After the runner's one-second wait, Second Chance was false and `IsDebouncing` false; the player remained alive at full 440 HP. Normal disconnect/results reached the menu. |

The two Arena deaths therefore neither consumed the foreign ordinary Second
Chance nor prevented native protected exit. The ordinary control exercised its
real consumption and debounce completion. It did not time that debounce across
a world change, and it does not certify every lethal combat damage type.

### Additional living-round probe and log limitation

The same corrected process ran another three-round Deathmatch from 10:02.
Two living round changes and an additional native death/respawn completed.
A final 400-ms fire action produced one actual Saiga-9 shot before the protected
exit, persisted at 10:07:45.172.

An unload was started near the first round boundary, but the following inspector
handles had already been collected and the kit was replaced. A second attempt
missed the desired timing window. Neither is counted as proof that pending
magazine work overlapped the living replacement. The earlier two individually
confirmed pending-unload deaths remain the evidence for interrupted work.

The early fixed Zombie exit also produced seven
`PerfectCullingCrossSceneGroup.UpdateSwitchQueues` NullReferenceExceptions
between 09:59:10.622 and .767, during scene teardown after the protected end.
They did not recur at the later Zombie death. The checked earlier logs did not
contain this stack, so it is not labelled a known harmless vanilla error.
Native culling accesses settings, bake groups and renderer proxies without
complete teardown/null checks; the stack has no source line identifying which
object was absent. Arena does not patch those queue methods. No respawn or
Core stale-camera/sampler eviction preceded the burst. An indirect timing effect
of an extremely early exit remains possible, but a faulty Arena resource release
has not been established. A broader culling patch is not justified by this
single teardown burst.

The final combined server window, 09:26–10:08 local time, contains no errors,
Arena compatibility warnings or rejected protected ends. Its six restores
include the recovery of the interrupted early-Zombie raid, not six uninterrupted
successful raids. Thirty SPT warnings concern Fence plate slots, profile-value
fallbacks and a missing trader. The corrected client has eight Unity errors:
the seven culling exceptions above and one inactive-MenuScreen coroutine error
in the subsequent ordinary Second Chance control. The additional Deathmatch has
no errors. Three AI CheckMagazine utility-event timeouts and eight missing-jet-
material diagnostics in inactive pooled weapon clones remain separate
observations; no cause in these adapters was established.

## Cleanup and final installation

After the protected ends, the MP5 was returned through the native inventory UI
to its original stash position, x=6/y=19, horizontal. The saved backend profile
confirmed that position and `slotId=hideout`. No profile JSON was overwritten.
The PFT menu confirmed an empty invited group; its four existing roster members
were retained. EFT then quit gracefully and the server stopped normally.

Both installations were cleaned with exact ownership manifests and SHA256
checks, archiving all affected files before removal:

- Classic Movement and StatTrack had already been removed at 09:09, including
  their four owned client/server directories, generated statistics and reviewed
  synchronization copies.
- CLA: two reviewed GPU synchronization copies, then six payload/config files
  across both roots. Its generated preset file did not exist.
- Deminvincibility: four payload/config files across both roots.
- SVM: two reviewed preset synchronization copies, then 43 files across the two
  complete SVM installations. Per the user's instruction, SVM 2.2.1 was **not**
  restored. All 40 original backup files, including the original presets, remain
  in ignored evidence storage.
- Eleven known Arena test settings were restored individually to their original
  values; every other setting and its formatting were preserved.

Cleanup receipts and archives are under `artifacts/native-optional-compat-20261020/`,
with timestamps 10:10:43–10:10:53 local time. No unrelated mod, shared plugin
directory, saved profile or existing empty LTTP/LootingBots directory was
included in deletion ownership.

The independent final file verification and root rerun each passed **100 checks**.
Both roots have no owned test-mod payload/config remnants; all Arena values
match the recorded baseline, not only the eleven changed keys. Recorded
Core/ContinuousHealing/UiBridge DLL hashes and Killcam config bytes match their
pretest captures. There was no original Killcam DLL hash or separate original
Core/CH/UiBridge config capture, so those are not claimed as byte-verified
restorations.

The clean restart reached MainMenu in process `10f3cd21`, started 10:11:53:
all 41 plugins and six libraries matched local files, GPU files and loaded MVIDs.
Client MVID is `19bee480-efc1-4368-8072-6cefc773c209`; its startup log contains
no errors. ARENA remained selectable and reached the map screen with the
restored Gun Game preference. This last check did not start another raid.
The clean server still reports the pre-existing, file-empty `LateToTheParty`
and `Skwizzy.LootingBots` directories as having no assemblies. Those directories
were deliberately preserved and are not temporary DLLs left by this test.

Evidence: `clean-startup-observations.json`, `clean-startup-client.log`,
`clean-startup-server.log`, and `final-native-cleanup-verification-root.json`.
After this check EFT quit gracefully, SPT stopped normally, and the host helper
confirmed **game not running / server not running**. No push or release was made.

### Remaining native coverage boundaries

The measured Factory runs establish the lifecycle combinations described above.
They are not blanket certification of every foreign setting or a future version.
CLA's new-load-within-800-ms race, confirmed pending work during a **living**
replacement, real preset/MultiSelect cancellation, quick-load hotkey and measured
movement-limit restoration remain without conclusive native evidence. Demi's
debounce spanning a world change and all individual lethal damage types were not
exhaustively exercised. PFT coverage used one companion in Zombies; separate
Tactical, larger squads and killer replay are not implied by live spectating.
The corresponding deterministic offline controls remain separate evidence.
