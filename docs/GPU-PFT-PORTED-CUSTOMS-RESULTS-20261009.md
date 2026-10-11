# PFT ported-map startup and all-mode Customs — GPU2, 2026-10-09

Initial candidate: `54d9f7edf38154cc31e3fa48be0de123c1dedb2e`.
Final client candidate: `f462bfba9c4420c290674b9e0f86241528f9cf06`.
The user's failure log is `LogOutput-MASCHINE11-2026-10-09_17-53-05.log`:
Tactical Bay5 ended after waiting for 0/4 companions. See
`PFT-PORTED-MAPS-REVIEW.md` for the installed-code empty-cover dereference.

## Build and deployment

Client and server were built without automatic installation, then copied to the
development installation and GPU2 while EFT was closed and the GPU2 server stopped.
Both report `0.1.0+54d9f7edf38154cc31e3fa48be0de123c1dedb2e`:

- Client SHA256: `963250EF613A6CEFD49CEAC239891BE9EC64F33695BEE8A3218132D4789BC1CF`.
- Server SHA256: `DD7738CE012E64CDE81C4B08D9F02F7723D110FF413E707F217D897E5DD19E7B`.
- Client: zero errors, four existing nullable warnings. Server: zero errors,
  two existing analyzer warnings. Redeploy.Core and foreign mods were not built.
- GPU2 restarted as profile `SPT-GPU-2` (`6ab7db1c64f04b565cf78be9`),
  process instance `1ecabb30`; UiBridge confirmed 21/21 maschine plugins plus Core
  identical between development disk, GPU2 disk and loaded assembly.

Native follow-ups: `27fdf80` accepts the exact PFT Coop entry category,
`9aad58c` handles unavailable cover graphs, and `91db119` defers failed-start
teardown and revokes pending ambient/managed spawns. That intermediate client was staged
with zero errors/four existing warnings, then copied to both installations:
`0.1.0+91db1197c5c873cbaa68bac85174dc8eb47c3e45`, SHA256
`B18A30F2DBED402DB942C6D37D8EED633B5D9322FCAC99A7F385CAAB2F37DB8D`.
Server remains the verified `54d9f7e` binary above; no server/shared code changed
in these follow-ups, and the running server was preserved for exact recovery.

The bounded-raid retest exposed one final ordering fault: `GetLiveFollowers`
ran before deferred mode startup had bound the local PMC. `e55d6a7` defers only
this exact initially empty discovery state; existing errors and any history of
a bound leader or follower retain strict validation. Staged build: zero errors,
four existing warnings. Client deployed to both installations:
`0.1.0+e55d6a78c1fd2bbc2ec3d390e1566db2fde280f7`, SHA256
`7DA9F92589AB08B2D309B1ADC97E26AF1547D783638EDFD43CB649A2E34A060C`.
The server is unchanged. Parent baseline 158 assertions and worker 20 compiled
fault controls passed; independent lifecycle review found no P0–P2 issue.

Final integration: `21b6af6` scopes bounded initial IDs/raw creation leases to
the exact native BotCreatorClient and retires old pending placements before a
new frozen roster. `f462bfb` keeps the five exact current-round Tactical opponent
bodies readable until their roster retires, without preventing hiding/stripping
or weakening unexpected live-loss validation. Final staged client has zero
errors/four existing warnings, deployed locally and GPU2:
`0.1.0+f462bfba9c4420c290674b9e0f86241528f9cf06`, SHA256
`F43AA3C179C371310F48882A27DF0473EDBD3CD72719A33F7D67E307B50A70E8`.
Server remains `54d9f7e`. Parent rechecks: PFT234, Tactical corpse68, existing
cleanup309 and DynamicMaps61 passed. Workers rejected 24/16 respective compiled
faults; the old e55d6a7 spawn adapter fails the new repeated-raid regression.

## Offline evidence

Parent reruns on the integrated source passed:

- Unit suite: 932 passed, two pre-existing skips (934 total).
- PFT ported/bounded hooks: 147 assertions and 14 compiled fault controls.
- Customs all-mode contract: 392 assertions and three compiled fault controls.
- Area placement/respawn contracts: 423 assertions and 19 compiled fault controls.
- Native menu adapter: 190 assertions; participant/layout: 3,731; area menu: 29.
- Map admission/native installed spawn corpus: 494; existing PFT compatibility: 309;
  existing Zombie squad checks: 59.

Follow-up checks: area runtime 459 assertions/22 compiled faults; coverless
combat 150/25; actual failed-start partial 65/13; managed solo runtime 141/12
plus 17 installed native IL contracts. Parent reran each baseline, all 25 combat
faults, the 932-test unit suite and the menu/PFT/Zombie regression checks.
Independent review additionally found and resolved the detached managed-solo
lease, whose generation intentionally differs from ambient burst resets.

The independent review caught and resolved the old router team-size overload,
non-TDM respawn reservation gap and initial PFT callback counter exception path.
Final review found no unresolved P0–P2 issue. Offline fixtures do not establish
native navigation, loading or long-match behavior.

## Initial native evidence (54d9f7e)

Log: `LogOutput-SPT-GPU-2-2026-10-09_18-22-47.log`.
Companions test1–test4 were invited through PFT's native squad menu. Test hooks,
when used, were limited to protected Arena and switched off before leaving.

| Scenario | Evidence | Result |
|---|---|---|
| Bay5 Tactical PFT | 18:28:19.946 existing core 4 fallback; four physical active companions; 18:28:39.072 five allies versus five opponents live | Passed startup |
| Bay5 Zombies PFT | 18:31:26.815 exact five-player team ready; wave 1 running | Passed startup; combat cover errors found |
| Equator Zombies PFT | 18:33:57.065 existing core 3; 18:34:01.851 exact team ready; companions kill infected | Passed startup; combat/path errors found |
| Equator Tactical | Not offered in native Tactical selection; existing five-opponent distance-capacity guard retained | Correctly blocked |
| Customs Tactical PFT / Factory Center | 18:36:50.912 native PFT dispatch deferred; 18:37:00.705 initial team spawn not verified | Failed; exposed PFT's Coop spawn-category mutation |
| Customs Zombies PFT / Customs Yard | Not attempted on this initial candidate | Covered in final retests below |
| Customs solo/menu coverage | Not attempted on this initial candidate | Covered in later retests below |

The Bay5 Tactical test ended deliberately through protected Disconnect. At
18:29:33.824 the previously documented PFT roster observer emitted its teardown
diagnostic after native `Stop(Left)` (18:29:33.757), not during match assembly or
live play. That diagnostic is unchanged by this startup/area task.

The initial smoke also exposed two empty-cover assumptions after spawning:
PFT companion cover queries throw in `CoverFindGraphSearchData.TryAdd`, and
infected navigation on Equator throws when a failed direct NavMesh path falls
back to a cover graph. Both require a narrow follow-up, rather than calling
successful spawning full compatibility. The melee `GoalTargetBeingHitAction`
exception at 18:34:26.400 occurs after native `Stop(Left)` at 18:34:26.208;
the exact null field is not established and is a separate teardown issue.

The Customs rejection additionally demonstrated a failed-start exit hang:
native state was `Stopping`, all local profile identities matched, Arena's
abort was committed, and respawn had stood down. Calling Stop inside the
raid-start callback is now being deferred; failed/committed starts must also
block fallback population requests. The test process was closed gracefully,
with the server left running so its exact protected snapshot remains available
to the next client process's normal crash-recovery handshake. No profile files
were overwritten. Final recovery and retest results follow below.

## Final candidate recovery and native retests

Recovery/retests first used client `91db119`, server `54d9f7e`. After a fresh process,
inviting companions before the first local-start reconciliation was correctly
blocked by the old protected session; PFT's native invite screen displayed an
unhandled-response dialog. EFT was closed gracefully again. In the next process
(`ffedf4a5`, log `LogOutput-SPT-GPU-2-2026-10-09_18-54-41.log`), the first solo
Arena attempt invoked normal crash recovery: at server UTC 16:57:03.331 the
snapshot was restored, and at 16:57:03.355 protected end completed and persisted.
The immediately following start was rejected by the existing PFT finalization
cooldown; no raid was admitted. This was acknowledged and retried after settling.
An independent full-profile comparison then found only Health.UpdateTime and
Hideout.sptUpdateLastRunTimestamp changes; all twelve PFT files remained identical.
Only the deliberate mode/area menu selections differed in configuration.

| Final scenario | Evidence | Result |
|---|---|---|
| Customs Deathmatch / Factory Side | 18:59:46.824 area READY, 20 connected player/14 bot markers; match starts afterward; 9/9 active opponents by 19:00:17; player and all nine sampled positions inside contour; rival score 200 | Passed startup/combat sample; protected exit 19:00:38 |
| Customs all five mode selections (91db119) | Native Deathmatch/Gun Game/Tactical/TDM/Zombies: three areas, no whole-map option; preview changes; mode-specific participant/team controls | Passed |
| Customs Tactical PFT / Factory Center (e55d6a7) | 19:16:21.132 area READY; mode starts .247; four distinct reservations released .328; four active physical companions; 19:16:32.258 five allies versus five opponents live | Passed startup/movement observation; no actual engagement observed while PMC stayed at entry |
| Customs Zombies PFT / Customs Yard (e55d6a7) | Consecutive-raid duplicate rejection described below | Failed; final correction tested below |
| Bay5 Tactical PFT (e55d6a7) | 19:28:01.800 exact 5v5 live; native-path and no-cover guards exercised; opponent dies, then body released at 19:28:21.870 | Startup passed; live corpse-lifetime failure found and resolved in final retest below |
| Bay5 Zombies PFT (e55d6a7) | 19:30:47.312 exact five-player squad; wave 1 decreases 56 to 41, surviving companions move/fight; both coverless guards exercised for 60+ seconds | Passed startup/combat; protected leave 19:31:50.641 |
| Equator Zombies PFT (e55d6a7) | 19:34:13.062 exact five-player squad; 56 to 22 infected remaining by 00:48; all four followers still active/moving, test1/test3 kills observed; both coverless guards exercised | Passed startup/combat; protected leave 19:35:11.468 |

The solo kit swap logged the installed `RespawnApiProbe.AlwaysThrow` test
subscriber's intentional exception. Core caught it and completed the swap;
that pre-existing external probe was not modified or removed. No Error-level
line occurred in this solo raid. After protected exit, a fresh parent comparison
again found only the two allowed profile timestamps and zero changed PFT files.

On `91db119`, Customs Tactical Center used the correct Coop start at
19:05:49.757. Discovery latched a missing leader at 19:06:03.823, just before
area READY (19:06:03.922) and mode startup (19:06:04.014). All four physical
followers arrived at 19:06:04.561–19:06:05.548, but the earlier latch ended the
match. Normal protected no-contest exit completed to MainMenu. This establishes
that the spawn-category correction worked and identifies the separate
initial-discovery correction in `e55d6a7`; it is not an injected test of the
failed-start abort coordinator.

Final `e55d6a7` restarted as instance `3247fb88`, log
`LogOutput-SPT-GPU-2-2026-10-09_19-12-22.log`; UiBridge verified all 21 maschine
plugins and Core matched development disk, host disk and loaded assembly.
Center's boundary rejected attempted out-of-contour AI destinations as intended;
this is containment evidence, not acceptance of long-match pursuit behavior.
Its protected leave committed at 19:19:50.109 and returned to MainMenu in 24 s.
The known missing-body roster diagnostic followed native Stop on that same
timestamp; it did not abort startup or live play. Parent profile comparison
again found only two bookkeeping timestamps and all twelve PFT files identical.

The following `e55d6a7` Yard/Zombies raid exposed a consecutive-raid bug:
19:22:21.996 area READY (8 player/22 bot points), then four immediate bounded
activation rejections at 19:22:22.021–.022. The combined warning mentioned safe
markers, but the earlier duplicate guards ran first. The process-wide initial
profile-ID set survived protected end and a new frozen roster; the profile-only
raw-task ledger had the same potential reuse problem. Native LocalGame creates a
new BotCreatorClient per raid, so both ledgers must instead belong to that exact
creator. Old creator/task tombstones must remain for delayed continuations.
This failed run ended protected after the 45-second roster timeout and returned
to MainMenu. It is not evidence that Yard lacks connected companion positions.

Bay5 Tactical's later live failure is separate from spawning: its dead opponent
was trimmed at 19:28:16.866 and released by corpse cleanup at 19:28:21.870.
The next Tactical roster check (.914) requires that frozen body's identity to
remain readable and therefore ended no contest. A narrow current-round body
retention fix passed the final test below; unexpected loss/replacement of living bodies
must continue to fail closed.

Bay5 Zombie's live sample had no matching Error/Exception/no-contest entry.
After its deliberate native Stop(Left), the known disappeared-follower diagnostic
occurred at 19:31:50.642, followed by `GoalTargetBeingHitAction` exceptions at
.670 and .749. These are shutdown-only observations, not resolved live-combat
faults. Protected end still completed to MainMenu with local godmode disabled.
Equator's same roster diagnostic followed Stop by 1 ms (19:35:11.469), without
the melee exception. Parent audited the raw disk log by timestamp and severity:
Center Tactical, Bay5 Zombies and Equator Zombies live windows contain no
Error/Fatal/Exception/no-contest entry. Final configuration restoration remains
separate from these intermediate comparisons.

## Combined f462bfb native follow-up

The Zombie combat samples above are explicitly bound to `e55d6a7`; the final
follow-ups change bounded creator ownership and Tactical corpse retention, not
the tested ported Zombie combat adapter. The final binary is tested below on
the affected Tactical death/cleanup path and consecutive bounded squad raids.

| Scenario | Evidence | Result |
|---|---|---|
| Bay5 Tactical after opponent death / cleanup interval | 19:44:14.602 5v5 live; 19:45:00.230 natural round-1 victory; fresh four followers adopted 19:45:07.943 and round 2 live 19:45:18.901; corpse pressure trims 3/releases 2 old bodies while the exact dead current-round Bot18 remains readable through sweep 36 | Passed live combat, round rebuild and corpse-retention regression |
| Customs Tactical / Factory Center | Area READY 19:51:01.410; four distinct companion reservations; 19:51:16.639 5v5 live; all nine sampled bots inside the 40-corner contour | Passed startup and containment sample; protected Stop 19:51:34.947, session end 19:51:36.015 |
| Next protected raid: Customs Zombies / Customs Yard | Same process; area READY 19:54:09.465 (8 player/22 bot points); exact five-player squad ready 19:54:11.030; native infected fight; PMC death 19:54:53.029 enters spectator with surviving companions | Passed repeated-raid startup/combat; natural team defeat 19:55:28.490 and protected session end 19:55:32.587 |

For the corpse-pressure check only, Performance/MaxCorpses was changed from 10
to 0 and then corrected to 1: zero means keep all, so that interval is not
counted as a cleanup test. At 19:46:15.172 three corpses were trimmed; at
19:46:20.178 two old bodies were released. Native inspection linked the retained
Bot18/profile `6ac92818a4e5770e1c3b61d3` to frozen enemy index 4: dead health,
completed trim at sweep 17, no release failure, no Own/Protected flag. It remained
readable through current sweep 36 and round 2 continued 4v4 without Error or
no-contest. The original limit 10 was restored and read back; local godmode was
switched off before protected leave. Native Stop(Left) at 19:48:29.246 and session
end at 19:48:30.336 returned to MainMenu in 21 seconds. Final process identity is
`26d7f5b2`, log `LogOutput-SPT-GPU-2-2026-10-09_19-40-33.log`.

Center Tactical and Yard Zombies used the same final process with no restart
between them. The four persisted companion identities were accepted in both
raids, resolving the previous process-wide duplicate rejection. Yard reached
61 live actors (PMC, four companions and 56 infected); natural combat produced
197 native melee swing attempts and 46 landed hits before the whole squad died.
All 19 sampled Yard bodies (four followers and 15 infected during wave assembly)
were inside the actual 28-corner contour. Two unsafe navigation samples were
rejected; the successful start did not weaken that check. These are rounded
position samples, not continuous trajectory proof.
The player entered spectator before that defeat, with no respawn. There was no
Error/Exception or readiness timeout during this Yard run. Its protected end
followed natural elimination, not a forced kill or manually completed wave.

## Preservation and limits

Before testing, the entire test profile, all twelve PFT persistence files, relevant
configuration and installed artifact hashes were captured in
`artifacts/pft-ported-customs-20261009`. Final comparison after the protected
Yard defeat and again after graceful process exit passed:

- Whole profile differs only at `characters.pmc.Health.UpdateTime` and
  `characters.pmc.Hideout.sptUpdateLastRunTimestamp`.
- All twelve PFT files are byte-identical; no profile or companion backup was
  manually copied over the live files.
- ArenaMode and ModProfiler configuration values match the original snapshot.
  Mode/area selections and MaxCorpses were restored; godmode is off.
- Final client/server match the exact hashes above on development and GPU2.
  Core, UiBridge, PFT and SAIN main DLL match the initial hashes. All eight
  inspected file pairs match between hosts. SAIN.Preset.Shared has pair equality
  only; no separate initial hash was captured for that auxiliary DLL.
- No new probe remains. The installed pre-existing RespawnApiProbe was preserved.
- GPU2 EFT was closed gracefully from MainMenu; server PID 3612 remains running.
  No push was made; unrelated untracked Molotov assets were preserved.

The parent independently audited all three final loaded-client raid windows:
zero Error/Fatal/Exception/no-contest entries before their intended endings.
The known Center teardown error immediately after Stop serves as a positive
control for the log filter; it is not a live failure or a fixed diagnostic.
Evidence: `parent-final-live-log-audit.json`, `preservation.json`,
`final-artifacts.json`, `native-tactical-corpse-retest.*` and
`native-customs-f462bfb.*`. The final raw log copy is
`final-loaded-client-log.txt`, SHA256
`C9CAC78DBB6C5118DD2B71AFD0937248C419C953D1848B7D7FCF0D3CF182BDCC`.

Equator Tactical is still intentionally unavailable: from the atrium there
are only four native opponent markers at least 25 m away, while Tactical
requires five and a surviving player may remain there for the next round.
This is a separate verified capacity blocker, not a remaining PFT spawn failure.

These results do not certify every mode/area through a complete match, every
movement trajectory, a successful Zombie wave rebuild, or long-match performance.
All-five-mode Customs UI coverage is bound to `91db119`, ported Zombie combat
to `e55d6a7`; their relevant menu/combat source is unchanged in the final build.
Final `f462bfb` directly covers the changed bounded creator ledger and Tactical
body lifetime. Forced failed-start Stop retry remains controlled-harness evidence;
an ordinary PMC raid was not replayed in this test session. Known shutdown-only
roster and occasional melee diagnostics are explicitly not claimed fixed here.
