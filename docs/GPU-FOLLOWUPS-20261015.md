# Native follow-up acceptance — 15 October 2026

SPT-GPU-2, default test profile only, Factory daytime. No fake PFT roster,
inventory-file rewrite or infinite-ammunition hook was used. Raw profiles,
teammate stores, configurations, logs and inspector evidence stay ignored under
`artifacts/followups-20261015/native/`. Native testing is separate from the
offline regression suites. All requested native acceptance checks and final
restoration below are complete, with the stated scope limits.

## Loaded builds

First process started at 20:30:19 CEST. UiBridge reported all 19 local/remote/loaded
maschine assemblies identical. Client build `0cd9a48`, SHA256
`0082D8B2DA7F62F35926F74349FB1F9C8500FC2D9E8B2C881EDB9884FA6A0629`,
MVID `dce31fc7-e3b6-4209-98f3-61a75069eee1`. KillcamReplay 0.9.13,
SHA256 `26809E10FE8C676A10A93CC298D22372FCEA9F7E6BC48FE2E61D365F58D98350`,
MVID `bf63cc87-ea36-4c24-a370-6ad72183360d`. Remote server hash verified unchanged:
`C1A37197A746958C6A82EE2D41B1A7E61E9497F9A486AE92A871F33DC81BEA3B`.
The unchanged BigBrain companion was also checked after all tests:
`5701DFF516540CD3DAA7B8BC3E42132E96E3184391EE1676928AEB918818B0F9`.

## PFT opening countdown and survivor refill — passed

One stored companion was invited through MY SQUAD → portrait context menu →
INVITE TO GROUP. The test profile was initially unequipped; its existing M4 and
AVS were equipped through native inventory actions. Four zombies per wave,
MaxAlive four, ten-second intermission, no shooters; own gear remained enabled.

- Team readiness: 20:36:07.560. First infected arrival: 20:36:18.528. No infected
  arrived during the opening ten seconds. This is the Arena wave countdown,
  independent of vanilla's shortened deployment countdown.
- PMC body Id 1 / team epoch 1 began with 29 magazine rounds plus one chambered.
  Seven real M4 shots consumed ammunition: 30 → 23; weapon durability 99.8.
  Godmode was enabled for setup; infinite ammo was explicitly disabled.
- Wave one cleared at 20:36:50.666 with the PMC alive. Native own-gear refill
  completed at 20:36:50.984. Body Id changed 1 → 7, epoch 1 → 2, weapon identity
  changed; entry ammunition 29+1 and durability 100 returned.
- PFT adopted the replacement leader and one new physical follower; stable
  registry passed, team ready at 20:36:52.790; wave two spawned at 20:37:02.895.

Evidence: `wave1initial.json`, `wave1-ammo-spent.json`, `wave2-refill.json`,
`pft-start2/`, `client-first.log`.

## Dead PMC, continued squad and terminal wipe — passed, distinct runs

The first run used a real infected melee hit after test-only health reduction.
The local PMC died at 20:37:37.575 while the follower remained alive; spectator
followed the follower. The follower subsequently lost its close-range duel.
Team defeat at 20:38:39.804 and protected exit completed without the prior
post-result physical-roster Error. There were 319 native attempted swings and
19 observed hits; the prolonged duel is not evidence that lifecycle code froze.

A second run invited two actual stored companions, with two zombies per wave.
After three-person team readiness at 20:40:54.698, the local test kill hook ran
at 20:40:54.881. This is deliberately a synthetic death trigger, distinct from
the first run's real melee death. The companions naturally cleared wave one.
The protected dead-body respawn completed at 20:42:09.950; PMC body Id 6, epoch
2, fresh M4 with 29+1 rounds, two fresh physical followers; team ready at
20:42:11.895. No fake roster or forced wave clear was involved.

The subsequent manual disconnect exposed a separate teardown Error: Stop(Left)
committed at 20:42:33.983, then a live bound follower was reported missing one
millisecond later; the match/session cleanup had not run yet. This finding was
sent to the root agent and prompted the narrow native-stop guard. It must not
be presented as a clean run or hidden by log-level filtering.

Evidence: `dead-wave2-restored.json`, `pft-dead/`, `client-first.log`.

## Additional Molotov — passed

Deathmatch, Spartan kit: three grenade items rather than two. The two originals
remained, and only the extra identity became TopPriorityGrenade. Turning the
mutator off and back on preserved exactly those same three identities.

The actual vanilla G command is `PressThrowGrenade`; UiBridge's current prose
example `ThrowGrenade` is ineffective here. `PressThrowGrenade` equipped the
bonus identity. Native `TryHighThrow`/`FinishHighThrow` threw it, and Arena bound
that exact supplied item. At 20:45:55.766 it ignited 32 floor cells for six seconds;
the captured frame visibly shows fire. The two originals remained afterwards.

A second G selected an original RGD-5. Its native throw consumed it, left the
other original, produced no additional incendiary binding/ignition, and the
following frame shows fragmentation marks and a 33-HP loss. The exact explosive
damage event was not separately traced, so those frame and identity observations
support ordinary grenade behavior without claiming a separate damage-hook test.

Evidence: `molotov-before.json`, `molotov-id-checks.json`, `molotov-fire.png`,
`normal-rgd.png`, `client-first.log`. Cold-pool own-gear and a completely full rig
were not separately tested; their coverage remains offline.

## Killcam live tail — passed

The host's saved live tail was three seconds, temporarily pinned to five for a
clear half-duration test. The live static EffectiveLiveTailSeconds read 2.5.
An actual enemy PMC killed the local player; replay recorded 56 snapshots across
2.97097 seconds, matching the selected three-second Arena replay. The recording
window was not shortened. Default slow motion still applied.

Inspector samples captured FadeOutCut, FadeInLive and then LiveHold. During
LiveHold the native phase clock progressed from 0.2333358 through 2.483381;
playback ended with its final phase clock at **2.50004768 seconds**. The actual
respawn followed replay cleanup. This measures LiveHold, not the total replay
including slow motion and fades. The infected-killer run separately skipped its
replay with the existing warning that no killer track was recorded.

Evidence: `replay-timeline3.json`, `client-first.log` at 20:49:15–20:49:25. Two
earlier observation windows saw no death and are retained as such, not successes.

## Protected state and final checks

After the first three raids all twelve PFT store files were byte-identical to
their pre-test snapshots. The main profile retained all 401 item identities,
with no added or removed items. Health, skills, statistics, quests, task counters,
achievements, insurance and examination encyclopedia matched the baseline.
Only the two deliberate pre-test native equip moves differed in inventory;
the early on-disk equipped snapshot had preceded their server persistence.
The final native unequip returned M4 and AVS to their exact original stash cells
(row 16 column 1, row 38 column 1). After the server's normal save, the complete
Inventory object, including all 401 items, locations and quick-slot bindings,
matched the pre-test profile. All other protected sections listed above match.
Health values match as well; the only Health difference is its ordinary
`UpdateTime` advancing across the game restart. No profile file was overwritten.

All **304 stored configuration values** match their original values: Arena 224,
Killcam 19, PFT 61 (including entries not currently bound). Killcam tail is back
to the host's original three seconds. All twelve PFT store files remain
byte-identical after all four raids. Final status: MainMenu, no raid, no active
test hooks, control lease free, 19/19 loaded assemblies identical.

Evidence: `restoration-verification.json`, `after/`, `restore/`,
`final-status.json`. The verifier explicitly preserves the raw Health mismatch
and checks that removing only UpdateTime yields equality; it does not hide
inventory or health-value differences.

## Final manual-exit correction — passed

Only the Arena client changed for the narrow native Stop guard. A fresh process
started at 20:53:13 CEST with all 19 assemblies identical:
build `98c70c8`, SHA256
`25263FA2CE310CEE938EB684930B64C98ECE36D22E2854E52FAF11945DB95F29`,
MVID `8323d860-1771-4872-ab4c-90ff96773201`. Killcam and server/companion stayed
unchanged. The earlier feature proofs belong to the first build; this second
test specifically verifies the native-stop difference.

A stored companion was invited again through the native menu. The exact
two-person team became ready at 20:55:12.381. Manual Stop(Left) committed at
20:55:13.407, session cleanup followed at 20:55:14.443, and the game returned to
MainMenu. **Zero Errors** occurred in this second process. The earlier missing
physical-follower Error did not recur. This short retest exits during the
opening pause; survivor/dead replacements are covered by the previous runs,
not claimed to have been repeated here.

Evidence: `exit-retest/`, `client-final.log`, `final-status.json`.

## Client/server log audit

Client times are CEST; matching server times are UTC (minus two hours). Read the
live remote file contents: an open server log's cached directory timestamp did
not reflect its most recent appended lines.

The first process had **44 Warnings / 1 Error**. The Error was the manual PFT
exit just corrected. Warnings comprise four known missing optional Fika
references, four inherited Harmony target warnings, three Factory negative-scale
transit colliders, four intentional RespawnApiProbe subscriber exceptions,
four stale PerfectCulling cleanup notices, twelve teardown Animator notices,
twelve missing extractor-smoke component notices, and one unavailable infected
killer recording. Real exceptions remain visible; the probe exceptions were
test-plugin injections, not ignored native failures. No previous BotMemory
damage exception or empty infected preparation recurred in these runs.

The final process had **8 startup Warnings / 0 Errors** (the same optional-Fika
and inherited-Harmony groups), with no runtime warning from the manual-exit
test. Matching first-process server window: **16 Warnings / 0 Errors** (eight
PFT roster profile-view fallbacks, eight Fence plate-randomisation warnings).
Final process window through 20:58:26: **5 Warnings / 0 Errors** (four roster
view fallbacks, one Fence plate warning). These sources/operations are outside
Arena wave/refill execution; no Arena protected-restore failure appeared. The
PFT fallback requests occur when its roster portraits ask vanilla for their
stored teammate profiles and do not prevent native invitations/spawning.

Four checks were completed independently: loaded-build/roster ownership;
native gameplay and exact item/body identities; adverse death/exit behavior;
then persisted-state/config equality and complete client/server warning review.
This is functional evidence for this Factory test installation, not a capacity
or performance claim for large Zombie waves or every map/kit combination.
