# Factory Translocator collision correction

2026-11-16, branch `codex/translocator-20261115`, based on `c37ee3e5`.
Unreleased development fix; no version bump, merge or publication.
[Feature contract](../../reviews/TRANSLOCATOR-20261115.md).

## Reproduced cause

On host 2, candidate E's first native throw at Factory basement spawn
`(-0.122,-3.860,-14.337)`, yaw 343.785/pitch 0, returned `THROW BLOCKED`:
Launches 0 / Rejections 1, no disc. Before another geometry query could overwrite
the buffers, `OverlapSphereNonAlloc` held two enabled non-trigger SphereColliders
on layer 13 (`Triggers`), radius 20, with no renderer:
`SPAWNS/PMCspawn_exp_Area_03_D Group/PMCspawn_exp_Area_03_D (3)` and `(2)`.
Their centers were `(18.631,-2.669,-14.000)` and `(18.637,-2.669,-15.659)`.
The initial eye clearance failed before any spherecast. This directly identifies
the false blocker, rather than inferring it from a forward ray.

At `(-3,-3.818,-14.337)`, yaw 90, two actual throws returned behind the player.
The forward ray met that same spawn sphere at 1.51 m, while the first real rack
was 6.15 m away. The trajectory supports the reported invisible rebound; no exact
per-substep contact was instrumented. Candidate E's capture already had a cyan
world background before the throw and is not visual acceptance evidence.

A real-wall control at `(-17.240,-3.729,-14.337)`, yaw 270, correctly refused
the throw. Its spherecast recorded actual layer 12
`Factory_basement_04_A_BALLISTIC_Concrete` at 0.202240869 m, alongside the layer 18
movement hull. Thus excluding helpers must not remove the physical wall.

## Correction and boundary

Disc overlap and sweep queries now use physical layers: Default, Player,
DoorLowPolyCollider, Terrain, HighPolyCollider, Loot, HitCollider, Deadbody,
LevelBorder and TransparentCollider. Native `LayersMaskController`, the installed
PhysicsManager and real grenade prefabs informed the choice. Default and Loot
carry real physical objects. LevelBorder intentionally remains stricter than a
native grenade: some ported arenas use it as their actual outer boundary.

Whole-map movement hulls, spawn/interaction helpers and PlayerSpirit broadphase
volumes are excluded. A bounded arena additionally queries LowPoly but accepts
only colliders owned by its existing `ArenaAreaRuntime` enclosure. Own-body
filtering also recognizes the exact owner's Spirit parent/controller without
ignoring another player's body or anonymous world geometry for a missing owner.

Launch origin/radius, swept motion, buffer-saturation refusal, controls, physics,
normal-raid gating and the separate native player-capsule destination validation
are unchanged. No profile/session/reward code changed. There is no new runtime
logging. An independent read-only review found no blocking defects.

## Installed candidate A

Only the client DLL/PDB were copied to the local installation and both GPU hosts,
after an isolated build and with all game/server processes closed. Server and
released Core remain unchanged.

| Artifact | SHA256 / identity |
|---|---|
| Client | `8710BCA3139FD87257B8F90CBF503B56CD73A02B37CE7F0D57FB1D5821D52F5E` |
| Client MVID | `e4980df0-1f25-40a9-99ab-e1cd483171db` |
| PDB | `FA32AE04D37874E600EDD594E64150F52ED0D99DA16A3D87AD4C50CBD801E208` |
| Unchanged server | `EAE730FB3379CCB0920839EB7FDB4BD2BF31753BEA319ED57748F412D146CDF1` |
| Unchanged Core 1.0.24 | `AA46EC8495A7EA01A671E4612CC84E08A26AF4E4B9979860038D900E941B915B` |
| Source manifest | `D0C7E59180CF409D7F4E7D68FB89F3644CFACD4F60DBA58313FA331124006817` |

The precommit candidate's base stamp alone does not identify this fix. All 641
source-manifest entries match; the only production delta is
`ArenaTranslocatorGeometry.cs`. The linked SessionLogic/CoreVersionGate sources
and project properties are unchanged. This is not a complete SDK/asset/reference
environment lock. Exact loaded identity and native outcomes are recorded below.

## Offline verification

The isolated client build passed with zero errors and four existing CS8632
warnings in the unchanged shared settings DTOs; it did not deploy Core or server.

The new geometry cases first failed against the original production source with
`helper volume does not block launch`. The corrected linked harness passes 91
assertions and 28 compiled fault controls, including 11 new faults covering both
query masks, own/foreign Spirit bodies, missing owners, map borders, Default/Loot
and the bounded enclosure exception. Tests filter fixtures using the mask actually
passed to the physics call; positive solids must block both overlap and sweep.
These fixtures do not simulate Unity's physical scene or Transform hierarchy.

The full xunit suite passed: 3,929 tests, three existing environment-dependent
skips, zero failures. Automatic affected selection for the code/test change ran
52 harness groups plus that suite: 53 passed, zero failed, no cached verdicts.
The final documentation index additionally selected `VerificationRunnerChecks`,
bringing the complete final set to 53 harness groups plus xunit. Its full repeat
passed 37 cases and 47 deliberate faults. The first attempt had stopped on a
temporary Git worktree creation failure (`commondir`) before the intended
`stale-write-time` assertion; that attempt is preserved and was not counted as a
pass. No production or verification-tool code was changed for the repeat.

The runner's installed-file guards passed. Several results were intentionally
not cached because configuration write-times synchronized while it ran; the
independent final configuration comparison confirms all 235 files byte-identical
to the original settings. Production/test logic remained frozen throughout;
only result documentation was completed afterward.

## Corrected native comparison

Host 2's fresh process `9965eb22` loaded candidate A with matching loaded/disk/local
MVID and remote disk hash. Factory DAY, Deathmatch FFA, two participants,
endless rounds with kill/time limits off; the server supplied the real device.
No injected item or scene-collider changes were used.

| Case | Candidate A result |
|---|---|
| Original spawn x/z and view, still inside the same helper sphere | First actual throw succeeds, Launches 1 / Rejections 0. The settled foot height differs by 0.039 m from E, far within the 20 m sphere. |
| First resting target has insufficient player clearance | Native secondary returns `target-obstructed`; player position and exact disc destination remain unchanged. |
| Same outside position, yaw 90, ray still meets the helper at 1.51 m | Disc passes that boundary and rests at `(3.553,-3.797,-10.909)`; E returned behind the thrower near x=-4.12. |
| Secondary at the clear second destination | Native Translocator succeeds, consumes the disc; player settles at `(3.553,-3.840,-10.909)`. This is distinct from UiBridge positioning used to set up comparisons. |
| Same physical wall x/z/view | `THROW BLOCKED` remains correct. Immediate spherecast Hits[0] identifies the actual HighPoly concrete at 0.202216089 m. |

The live disc mask was 1619106561. The same-position screenshot and 120-frame
capture show the rendered Factory world and visible disc. Capture ended at its
requested frame limit, with zero reported missed frames/readback errors; it is
not a recording of the entire raid. The old cyan background did not recur, but
its cause was not investigated and is not attributed to this fix.

Zero Arena warnings/errors occurred from before raid entry through MainMenu.
Godmode and held input were released; native raid exit, results screens and
game exit completed. Game and server are OFF, matching their original state.
Host 1 was not started. This run covers the listed Factory cases, not every map,
dynamic door, live opposing body or movement combination. Those additional layer
and ownership cases retain source/harness evidence only.

## Data and configuration audit

Fresh baselines were taken with synchronization paused on all three hosts. Whole
profile JSON, all existing conflict copies, PitFireTeam data, every BepInEx config
and the installed Arena payload were compared. Original Arena settings and two
DynamicMaps resolution-range comment rewrites were restored byte-exactly.

The first strict audit found a legitimate pre-existing reward on a different
profile from the selected test profile. An independent read-only audit traced
the signed input back to November 15, 21:33:10 UTC, before this task. The normal
server start paid exactly 1,000 RUB plus 100 XP for one eligible kill; the second
start reported AlreadyPaid. Receipt `a4c726a56bf94687846c25f7267c5e6e:9`, journal,
acknowledgement, signature, payload and exact XP/mail/ledger changes agree.
Reversing only that payment in memory reproduces the complete original JSON.
This is the documented startup reward exception to profile preservation, not
raid-result leakage. No live profile was edited or rolled back.

The task comparator accepts only that independently reviewed complete before/
after SHA256 pair for that one profile; it does not ignore arbitrary XP/mail
paths. Every other current profile differs only in the permitted Health/Hideout
bookkeeping timestamps; all seven pre-existing conflict copies are unchanged.
Normal server backup rotation is recorded separately. The pre-sync full audit
passed for all three installations: 373 protected/config/payload files per host,
with only the intended client DLL/PDB replaced and the documented reward retained.

The full comparison passed again after synchronization and after all offline
checks. Synchronization is restored to its original unpaused state on all three
machines; final status reads idle, zero needed items/bytes and zero errors.
No new profile conflict copies appeared. Both games and servers remain closed.

Evidence: gitignored `artifacts/translocator-collision-20261116/`, including
`native/host2-before/REPORT.md`, baseline/config/profile snapshots, candidate
manifest and identity, offline logs and deployment readbacks.
