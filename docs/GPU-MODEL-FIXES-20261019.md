# Native candidate acceptance — 19 October 2026

The reviewed diagnostic identity and script-free replay-body candidates passed
the bounded GPU2 checks below. This is not a claim that the native model-error
families are repaired or that every equipment/LOD combination renders correctly.
The original errors remained visible. No code, build or deployment was performed
by this native leaf; the parent supplied the reviewed deployment.

## Process and artifact identity

Host: SPT-GPU-2, `192.168.178.109`, install `C:\SPT41`.
Profile: `SPT-GPU-2`, `6ab7db1c64f04b565cf78be9`.
EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`, UiBridge
`0.9.4+42bbfb38e6ea3536b579db4312880ebda1f7b0cf`.
Fresh process `b734328f` started UiBridge at **10:33:44 CEST**. Both initial
and final status reported **43 plugins and 6 libraries identical across VM,
host disk and loaded process**. GPU2 disk hashes matched the frozen parent
manifest before launch and after shutdown. Parent commits: Arena `61a8c9a`,
Killcam `78e3fa3`.

| Candidate | SHA-256 | MVID |
| --- | --- | --- |
| ArenaMode.Client | `5F19FF29738F6D1F56186AE586CECE82EB256EF473587C065607F499FBE193E8` | `3d30e9e3-3ee6-4084-9208-5dd9b895d5b2` |
| ArenaMode.BigBrainCompat | `3823A50CB39E4E23BFF7174B33862B7119FE61FF4317B652440E72F458CFA79E` | `fcd2899f-86db-4983-8d8b-2dc8cb7cdf70` |
| Killcam | `128E7C99917E7FE8F7C27FE9A22D73835857B3569088C532969D56A29CF2D300` | `6e37e296-46d6-4558-8dc8-241a5bdf46a5` |

Redeploy.Core remained `218967C32C58F83C9BA2044888F12F595655D752172049DE57FA9C51FD4B231C`.
There was no Core/server deployment. The foreign `RAID_REVIEW.dll`, introduced
after the earlier isolated baseline, was present and loaded here (6 Harmony
methods). Its unchanged SHA-256 was
`8BBCA8235A13A34E4FF2B1B4672E5AAEA3BB95D4F744B9993B691FE11F37E69A`.
It was neither removed nor modified. This is an installed-mod-stack acceptance,
not an isolation comparison against the earlier 39-plugin process.

Private evidence root is
`artifacts/model-errors-20261019/candidate/`. Identity records:
`start-status.json`, `final-menu-status.json`, `final-plugin-hashes.json`, and
the parent's `../parent/frozen-builds.json`.

## Actual death replay, visual ownership and respawn

Factory Deathmatch used four participants, all-versus-player, endless rounds,
no kill/time limit, and Killcam enabled. Five actual bot-attributed deaths ran
the death-replay path, not the debug replay shortcut. The first two were normal
combat deaths after navmesh walking. Later visibility attempts used temporary
nonfatal health reduction and checked teleport/walk positioning; no kill hook,
animation mutation, synthetic native exception or observer invocation was used.
Godmode was briefly enabled to position a later attempt, then disabled before
death. No infinite-ammo hook was used.

DeveloperMode was enabled. Deathmatch replay duration was temporarily 10 s;
Killcam slow motion was 5 s at factor 0.1, providing time for read-only inspection.
All settings were ledgered and restored. The longer window explains the long
wall-clock playback; it does not represent default replay timing.

The first replay, killed by VolcanoKevin at 10:36:21.144, started at
10:36:21.930 with 182 snapshots spanning 9.97 s and 182 bone snapshots. It mapped
**139/139 transforms**, copied 32 victim-weapon meshes, and ended normally at
10:37:23.497. Its entire descendant-component inventory was read in two pages:

| Component | Count |
| --- | ---: |
| Transform | 250 |
| MeshFilter | 44 |
| MeshRenderer | 44 |
| SkinnedMeshRenderer | 13 |
| LODGroup | 11 |
| Total descendants | 362 |

No other component type appeared. A separate MonoBehaviour search returned zero.
The holder itself is outside the descendant query. The sampled shirt skin's
48 bone slots and rootBone all pointed inside `KillcamPuppet/`; the second
replay's 139 mapped animation transforms likewise pointed inside that hierarchy.
Eleven LOD groups existed under owned nodes. Their internal renderer arrays
were not independently read: UiBridge's read-only field/property inspector
does not invoke Unity `GetLODs`. No claim of all native LOD transitions is made.

In the second death replay, `_puppet` was the non-null
`Killcam.Replay.GhostPuppet` handle `17a7b0ae:387`, while `_ghost` was null.
Its clone root and weapon pivot were owned by `KillcamPuppet`. Two sampled poses
moved the root from approximately `(-13.491, 1.044, 43.011)` to
`(-19.637, 1.163, 42.415)`; weapon X changed from `-13.720` to `-19.868`.
The actual recording logged 21.12 m root-track spread. This is runtime recorded
movement, not merely an object-count or quiet-log assertion.

The early replay screenshots showed obstructed/off-axis killer views and are
**not** evidence of invisible models. The fifth replay provided a usable view:
`dm-replay5-a.png`, **game frame 89218**, HUD **-0.2 s**, killer Arokar. It shows
the dressed victim, face, black ULACH helmet, arms/hands, rifle and legs, partly
occluded by the stair rail. The parent independently inspected the original
image and accepted this bounded appearance check. No recoloring, image edit,
camera override or visibility mutation was applied to create the evidence.

![Actual fifth death replay, frame 89218](../artifacts/model-errors-20261019/candidate/dm-replay5-a.png)

The same replay's visibility inspection covered 15 body/headgear renderers:
head, clothing, pants, armor, backpack, headset and helmet were enabled,
`forceRenderingOff=False`, active in hierarchy, layer 23, shadows On. The separate
bare `Hands_USEC` renderer was disabled with shadows Off; the pictured clothed
arms/hands were visible. Bounds getters were read, but returned struct handles;
this report does not claim a numeric bounds or frustum analysis. See
`replay5-visibility.json`. These are measured properties, not a general renderer
repair or proof for different equipment.

After replay completion, scene searches returned **zero KillcamPuppet objects**
and `_puppet` became null. The first respawn fired a real SKS shot, frame 20024,
11 rounds before and 10 after. After the fifth replay another actual SKS shot
was observed at frame 97726, again 11 to 10 rounds. Both occurred on a usable,
alive replacement player. Cleanup was checked again in the final main menu.
Evidence: `replay1-inspection.json`, `replay1-cleanup-shot.json`,
`replay2-inspection.json`, `replay3-inspection.json`, `replay5-visibility.json`,
`dm-final-cleanup.json`, and `pft-rounds-replay.json` (complete Killcam log).

## Genuine diagnostic identity and PFT opportunities

Before death, the live Arena player's `PlayerBody._playerProfileID` was the
empty string, `_active=True`, and owner ProfileId was
`6ab7db1c64f04b565cf78be9`. The ever-existed dictionary, alive dictionary and
RegisteredPlayers list all contained the same live player handle. ArenaSession
was active in deathmatch. `live-identity.json` records these read-only facts.

More decisively, **14 genuine utility timeout diagnostic records** appeared:
six in Deathmatch and eight in PFT Tactical. No fault or observer was manually
invoked. For example, at **10:37:02.106**, native CheckMagazine timed out on
Bot3's AUG after 2.500996 s. The diagnostic reported:

`bodyProfile=none; ownerProfile=6ad5d640bd2be335cc1cbbb3;
ownerBodyMatch=True; lookupSource=owner; currentWorldBody=same;
aliveWorldBody=same; registeredBody=True`.

It also reported current hands/operation/item/animator association, a live
undisposed controller, two consumers including that controller, EmitOnDemand,
zero queued events, 44 bound behaviours, initialized/enabled Unity animator,
Normal update, CullUpdateTransforms, speed 1, and sampled layer states. Other
records covered MP155, MP5K, ADAR, MP7, Mossberg and AKS-74UB. These real faults
validate the corrected empty-body-ID attribution and availability of useful
fault-time context. They do not establish who caused the missing animation
event or what happened during the preceding 2.5 s.

PFT Tactical used the same four temporary companions and protected own PMC
gear. The player carried the original knife-only equipment; no item was given.
RoundSeconds was temporarily 60 and RoundsToWin 2. Round 1 went live at
**10:51:35.492**, then drew at 10:52:35.558. At 10:52:41.554 all nine prior
bot bodies had left active registries; the leader wrapper was rebuilt, four
fresh follower profiles/body instances passed the stable registry barrier and
were adopted. Round 2 went live at **10:52:54.081**. Genuine utility faults
occurred before and after that rebind.

During round 2, positioning near an opponent led to a genuine death by
TorturedChunk at **10:53:45.373**. The death replay used 56 snapshots over
2.95 s, mapped 139 bones and copied the held knife mesh. It ended normally at
10:53:53.309. Round 2 then timed out with four allies/four enemies alive; the
match ended 0-0 after two rounds with native status Killed. This proves one
complete PFT rebind and a subsequent real death/replay, not a third round or
post-death next-round replacement. The DM runs independently cover respawn.

Ordinary-PMC exclusion is protected by the unchanged admission contract and
offline controls; no additional candidate ordinary-PMC raid or fabricated probe
was performed. The earlier all-client-absent native control remains separate
evidence in `GPU-MODEL-CONTROLS-20261019.md`.

## Complete visible logs and unresolved signals

The final read includes all **197/197** warning/error records, with no truncated
page and no cut-off entries: 163 native EFT Errors, 23 BepInEx Warnings and
11 server Warnings. There were zero BepInEx Errors. Three complete raw files
were archived under `candidate/raw/`:

- `LogOutput-SPT-GPU-2-2026-10-19_10-33-36.log` (17,766 lines).
- `2026.10.19_8-33-48_0.16.9.5.40743 traces.log` (2,203 lines).
- `spt-SPT-GPU-2-20261019.log` (3,726 lines; merged export filters this process's run).

Native errors include **35 utility timeouts, 21 JetMaterial messages,
four undisposed menu bodies and one Parent NRE**. Other native counts are
72 item-node messages, 16 map-spawn markers/summaries, two transit messages,
10 duplicate event-consumer messages and two AI-cover messages. No Puppet
creation/fallback error was emitted. The original native errors were not muted.

The diagnostic export has **37/37** complete Info records: 14 utility,
19 JetMaterial and four undisposed-body contexts. Admission and diagnostic budgets explain
why this is fewer than native errors; the native errors themselves remain.
JetMaterial first appeared at 10:34:36.940, before any death replay, and admitted
contexts point to inactive native weapon-pool instances. The four menu-body
errors at 10:51:21.409–.412 and Parent NRE at 10:50:59.219 remain consistent
with the independently reproduced PFT/native preview families; this candidate
did not claim to repair them.

At 10:47:27.716, native creation during the fifth DM respawn logged ten identical
`Attempting to set events consumer ... already set` errors. This is an unresolved
rebind signal, not proof of two competing emitters or a new puppet defect. The
native setter warns for any existing non-null consumer, including the same
instance, then overwrites it; parent source review addresses that distinction.
The inspected visual hierarchy had no animator/emitter/script component.
Twelve missing-script warnings for unknown/extractor_smoke objects appeared
at the PFT asset spawn, 10:51:33–34, before the PFT death replay. These findings
were retained and reported, not classified away merely by timing.

`complete-warnings.json`, `complete-native-diagnostics.json` and
`log-summary.json` preserve full texts/counts/provenance. No guaranteed M32,
Benelli, hash-zero or null-hands exposure occurred; absence cannot accept those
families. The test does not prove every shader, material block, LOD transition,
headgear asset or source-restoration failure branch. Native acceptance is
bounded to the measured models, lifecycles and genuine diagnostic opportunities.

## Profile evidence and explicit restoration

Actual post-DM and post-PFT profile/store files were copied **before** any manual
restoration, including another profile copy after EFT/server shutdown. Against
the preserved original 314-item profile, both runs kept Inventory, Skills,
Stats, Quests, TaskConditionCounters, Achievements, InsuredItems and Encyclopedia
structurally equal. Health differed only at `UpdateTime`, from `1792360117` to
`1792398836`; HP, maxima, effects, energy, hydration and temperature all remained
equal. Whole-profile byte invariance is not claimed. All **12 PFT store files
were byte-identical** after DM and after PFT, before restoration.

Evidence: `post-dm-profile.json`, `post-pft-profile.json`,
`after-stop-profile-before-restore.json`, both `*-profile-comparison.json`,
both `*-health-detail.json`, `dm-pft-comparison.json`,
`pft-stores-comparison.json`, and the original `before/` copies.

Both setting ledgers were restored in reverse order, **4 + 4 resets, zero
conflicts/missing entries**. The temporary party was cleared to local-only in
MainMenu. Final status showed no raid, no active godmode/infinite ammo/tracking
or walking; final puppet reference was null and scene search empty. EFT quit
gracefully, its bridge was unreachable for 5 s, and the server then stopped.

With both stopped, the original profile, 12 PFT stores and five backed-up
configs (ArenaMode, Killcam, Redeploy, PFT, UiBridge) were explicitly restored
to VM and GPU2. **36/36 file comparisons passed.** This exact-byte cleanup is
separate from the measured gameplay invariants above. Candidate DLLs were
retained as the parent's deployment; foreign RAID_REVIEW and Core were unchanged.
`restoration-verification.json` records the provenance, and the final process
check reported **server not running / game not running**. GPU2 was relinquished;
no later build/deployment/native action belongs to this leaf.

## Four-pass review and gate result

1. Identity/ownership: matched the frozen candidate to the fresh loaded process,
   retained foreign-plugin provenance, read all owned visual components and
   sampled native skin ownership; did not confuse fallback with puppet success.
2. Runtime/visual: five real DM deaths, measured recorded motion, visible dressed
   body/headgear, deferred cleanup, usable respawn shots, and two PFT rounds with
   actual rebind/death/replay. Parent independently inspected the final image.
3. Adversarial evidence: all native errors remain, genuine utility timeouts
   validate ownership, earlier occluded views are not failures, unknown LOD/asset
   behavior remains unknown, and no manufactured fault substitutes for evidence.
4. Final state: untruncated merged/raw logs, pre-restoration profile comparisons,
   timestamp-only Health difference, twelve byte-identical PFT stores, explicit
   exact cleanup and stopped GPU2 process/server.

Leaf 3.2: **V1 met, V2 met; 2 met / 0 unmet / 0 abandoned**, within the stated
native acceptance boundaries. Parent integration/release decisions are separate.
