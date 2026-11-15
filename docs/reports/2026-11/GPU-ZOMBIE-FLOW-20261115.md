# Zombie recovery, planning and wave variety — 2026-11-15

Implementation follows the approved order: individual recovery, native planning
budget, then native wave composition. Contract:
[Zombie flow](../../reviews/ZOMBIE-FLOW-20261115.md). No version bump, merge, push
or release is part of this task.

## Candidate binding

Branch `codex/zombie-flow-20261115`, based on
`99f9bd6908810d22da16126a07ec0851d84dee5b`. Candidate A was built without deployment
and explicitly installed as client DLL plus matching symbols on the local tree
and both GPU hosts:

- Client SHA256: `B2CA8ED2769DB5535A737B1458F1579AD27FB6B5AD1CC46A78E6B408EE57F332`.
- MVID: `dec526a1-0b99-46a6-9f24-cb61e70602ba`.
- PDB SHA256: `90C5223AEE2A2A474C4ABA649FD68029794D9C73AF7E70271997A69F074A95D9`.
- Released Core 1.0.24 stays `AA46EC8495A7EA01A671E4612CC84E08A26AF4E4B9979860038D900E941B915B`.

Source manifests before/after build, exact binaries and identity are retained in
`artifacts/zombie-flow-20261115/native/candidate-a/`. Production sources did not
change during the native runs. Server, helper, Gate and companion binaries were
not replaced. Loaded plugin/library comparisons matched on both hosts; the
temporary GPU2 fixture was explicitly identified as an additional local-only
plugin, separately hash-bound and removed before the final team run.

## Native observations

| Scenario | Result |
|---|---|
| GPU1 Factory, own gear, 8 infected | Native easy/Slow, normal/Fast and hard/Shooting observed on exact owned bodies. 279 native melee swings and 17 hits; planning peak 2, reverse copy active. |
| GPU1 Factory, Arena kit, 4 infected/wave | Eight actual gun kills advanced waves 1→2→3. Living refill replaced local bodies 2→7→12, with restored health/ammo and four arrivals per wave. Native variants remained present across waves. |
| GPU1 separate Factory TDM 2v2 | Three bots moved during a 20-second observation (path lower bounds 70.76/53.44/48.12 m). All Zombie ownership/recovery/planning counters stayed zero. |
| GPU2 Factory, 32 infected, no kills | Planning peak 2; 712 approach and 11,351 melee jobs, 38,302 deferrals, peak queue 23, oldest current wait 2 frames. Native melee continued: 3,703 swings/333 hits. Reverse copy attempted=true/dead=false. No individual replacement of the active fighting crowd. |
| GPU2 Factory, existing PFT companion, fresh process without fixture | Companion made four real kills (450 score), advancing to wave 2. Living leader 1→7 and follower 2→8 rebuilt correctly. Recovery ownership contained only infected bodies, excluding leader/follower in both waves. Planning peak 1; protected exit completed in 21 seconds. |
| GPU2 injected geometry | No valid positive recovery result: native movement escaped each enclosure before the 20-second stationary requirement. See below. |

The small wave samples demonstrate actual construction of supported variants,
not statistical agreement with the configured distribution. Planning figures
count complete native jobs, not nested NavMesh searches or CPU time. They do not
establish an FPS improvement. Native shooting-mode construction was observed;
this report does not separately claim that a shooting infected fired a bullet.

The protected team used one member of GPU2's existing four-member roster. No
companion was created or edited; no items, deaths or score were injected. The
local player had only the entry bayonet and used temporary test invulnerability.
Native companion combat cleared the wave. This proves one surviving-leader wave
rebuild, not player death/spectator, full team wipe or larger squad acceptance.
The original empty group was restored, test controls disabled and EFT closed.
That fresh process recorded no ArenaMode warnings/errors; its optional absent
QuestingBots warning and native startup/Animator diagnostics remain separate.

### Recovery test limitation

The temporary fixture creates real opaque collision geometry, optionally with
real NavMesh obstacles. It names one exact confirmed owned body and current
raid/world, checks test host/profile and isolation, expires within 120 seconds,
and removes only its geometry. It never changes bot health, AI, position,
production counters or profile data. Fixture outputs are observations, not
production success assertions.

The first fixture version used `SimpleCharacterController.detectCollisions`, a
native getter that deliberately throws. The fixture was corrected to inspect
the actual enabled main collider and collision mask. A central carved enclosure
then allowed native NavMesh correction to move the bot outside; ordinary walls
without carving also failed because `BotMoverImpostor` reapplies requested motion
along its stored path. Finally, a wall-only carved ring preserved the original
NavMesh island: real paths were partial for roughly 1–3.5 seconds, then the native
controller moved the body outside at about 4 seconds and paths became complete.

All failed attempts and traces are retained. None meets the stationary proof,
and none was counted as a passed recovery test. **Actual native retirement,
replacement arrival and no-credit accounting for a persistent stuck body remain
unverified.** Production-linked fault tests cover these paths; a naturally stuck
native case or a separately validated confinement fixture is still needed.
The complete-path/physical-obstruction branch likewise has source tests only.

### Known exit diagnostic

During GPU2's 32-infected run, `Stop(Left)` occurred at 16:22:25.306. Four native
`BotMemory.GoalTargetBeingHitAction` null exceptions followed 243–526 ms later;
none occurred before Stop. The native MVID/offset and stack match the previously
documented [October 9 teardown defect](../2026-10/GPU-MODE-COPY-MAP-ACCESS-RESULTS-20261009.md#open-native-observation-melee-teardown-callback).
`BotWeaponManager.Dispose` clears `Melee` while a target hit subscription can
still receive a pending native kick callback. No new budget/recovery frame was
involved. Protected exit completed. No broad exception suppression was added.

GPU1 recorded no ArenaMode warnings/errors across its three raids. Other
sources' startup warnings, Factory marker/transit diagnostics, JetMaterial and
Animator teardown messages are retained separately, not described as clean logs.

## Verification and preservation

Focused production-linked recovery, planning and composition checks passed
independent parent reruns, including compiled fault rejection and the installed
native IL contract. Composition checks include 0%/100% shooters, future-wave
profile coverage and actual Mode/Burst admission wiring. The complete automatic
affected selection ran all 70 selected harness steps plus the xunit suite without
deployment: **71 passed, 0 failed, 0 cached**. Xunit: **3,918 passed, 3 skipped**;
the skips are the existing two environment-dependent reward baselines and the
installed APBS clone-pipeline check. Focused faults and native scenarios are
separate evidence, not added to those xunit counts.

Native evidence stays under `artifacts/zombie-flow-20261115/`: `native/`,
`native-host1/`, `native-team/` and the final verification log. Focused composition
self-check evidence is in `variety/`; parent gate fingerprints are in the local
`.unlazy/zombie-flow-20261115/` ledger.
Seven complete profiles per installation were archived before startup; PFT
files, config bytes and unchanged installation hashes were preserved separately.
GPU1 login added four ordinary `/spt/mods` registrations (ArenaMode, Redeploy,
WTT common library and content backport), documented before its first raid.
Subsequent gameplay comparisons use the actual entry baseline and permit only
the two existing Health/Hideout bookkeeping timestamps. No profile was repaired
or overwritten, and the four pre-existing conflict copies were retained.

The independent post-test audit passed on all three installations, and was
repeated after restoring Syncthing's original unpaused state. Final snapshot:
`native/after-20261115-163624/verification.json`. All 21 complete profile files
match the expected natural merge of each test host's verified profile, allowing
only the two bookkeeping timestamps above. No new profile conflict copies were
created. All 61 PFT files per installation and all archived configuration bytes
match their originals, including the server's cached client settings. Unchanged
installation hashes match; the only intended client changes are candidate A's
DLL and matching PDB. The temporary fixture and exact whitelist allowance are
outside the game tree. Both games are closed and test controls are off.

Syncthing restoration readbacks are retained in `native/sync/`. The surrounding
development tree may continue transferring test artifacts; the full game-data
preservation comparison above is independent of that transfer status.

Acceptance remains partial solely for positive native individual recovery:
the bounded fixture attempts ended without proving retirement, replacement
arrival and unscored accounting in the game. That result was explicitly retained
as a required follow-up, not converted into a successful gate.
