# Protected PFT Tactical equipment choice — native verification

Status: **N1–N3 passed**, 2026-10-18, with the bounded native coverage below.
The first kit-entry attempt exposed an early map-identity guard error. The
corrected candidate passed supplied-kit entry, native death, round restoration
and protected exit. A separate exit-time roster diagnostic remains a follow-up;
it also occurred in the own-gear control and did not prevent restoration.

## Isolation and evidence

GPU2, test profile **SPT-GPU-2** only; EFT 0.16.9.5-40743, Unity
2022.3.43f1 and UiBridge 0.9.4+42bbfb38. No personal profile, companion creation,
inventory grant, server deployment or Core deployment was used. The four existing
companions were invited through MY SQUAD's native portrait context menu.

Raw evidence is retained in the ignored directory
`D:/SPT41/Development/ArenaMode-pft-tactical-kits/artifacts/pft-tactical-native/`.
It includes private profile backups and must not be published wholesale.
All 223 pre-existing Arena preferences and the test profile/PFT stores were
backed up before changes. The new startup added `Tactical/TeamOwnGear=true`;
the 224-entry baseline includes that default. Temporary diagnostics and a
60-second Tactical round limit were used; optional loading-screen kit warmup was
explicitly set to **Off** for the supplied-kit trial.

## First candidate and own-gear control

Source `3652dda30f1b2d7cfcab18c8573e1bc94761286f` was built by the parent and
deployed using the canonical client deployer. Both the Client and the new plain
BigBrain library were deployed together, after EFT exited; host SHA comparison
completed before `gpu-run start`.

- Client SHA256 `1C03329B9942A28ED9602FDA733D2AB3292A067C6A5C96A59EDFE3FC943B735C`.
- BigBrainCompat SHA256 `4B5C383BFF5436568C24582D9DF399803A300C938C4B6EE7B51C19B57A1B83A0`.
- Process `98c21ab1`, started 11:38:22 local time. Native status found 18
  maschine plugins and two libraries identical across VM, host disk and loaded
  assemblies. The pursuit layer registered and bot movement support reported
  ready through Arena's logger. This is native startup evidence for the paired
  library lifecycle, not exhaustive pursuit behavior coverage.

The actual Tactical 5v5/PFT map screen offered **Arena kits / Own gear**. Own gear
was selected by default. `menu-own.png` and
`kit-start/kit-selected-menu.png` were visually inspected at 1920×1080: the
selection stripe changes sides and the row does not overlap the objective,
respawn or area/squad information. The rule text occupies two complete lines.

An own-gear Factory control reached live 5v5 at **11:41:48.497**. The PMC kept the
same knife, secure container, pockets and dogtag IDs that were present before
entry. All four PFT companions activated. Read-only physical equipment inspection
found each companion's nine original equipped item IDs intact (36 total).
PFT additionally supplies one temporary secure container per companion; those
four generated IDs are separately identified, not represented as persistent
gear changes. No full nested-item/ammunition-state comparison is inferred from
this top-level native inspection.

The normal Escape/DISCONNECT/result-screen path returned to MainMenu. The entire
server-entry inventory was identical afterwards, **314/314 items**, and all
12 original PFT profile/equipment/settings files remained byte-identical.
See `own-equipment.json`, `own-team-gear.json`, `own-tactical-log.json`,
`own-entry-profile.json` and `own-exit-profile.json`.

## Failure found and protected recovery

The supplied-kit attempt was made with four invited companions, via a real click
on Arena kits, and optional preload **Off**. At **11:46:55.623**, CaptureRequest
rejected its loading ownership check. Read-only native inspection confirmed that
the exact loading world's `LocationId` and `MainPlayer` were both null while the
armed map was Factory and the backend profile was the correct USEC test PMC.
The game's world map field is populated later than this seam; the root corrected
the guard against the native request's location instead.

Arena cancelled the protected start. The native error dialog's OK returned to
MainMenu in 12 seconds; no force continuation, artificial match activation or
hard process termination was used. All 314 inventory items and all PFT stores
remained intact. The original HTTP start task was still retrying after the early
exception, producing later server RejectedArenaStart entries; the root also
changed this path to observe that task before cancellation. The first failure
is preserved in `kit-start-arena-log.json`, `failed-entry-inspect.json`,
`failed-start-cancel.json` and `first-server-errors.json`.

The first process also logged repeated CraftQueue Poll/ChannelInfo 404 errors.
Those began before the kit attempt; no causal attribution to Arena is made.
They remain part of the preserved multi-stream diagnostics, rather than being
hidden by a claim that the process log was completely clean.

## Final candidate, round restore and cleanup

The final candidate was **`6fc7ca452455fd079a9cf681fceac73e8c6f3409`**. Both
libraries were redeployed together after graceful EFT exit. Synchronization
completed before a clean start; process **`fe952cf9`**, started **11:55:44**, then
confirmed 18 maschine plugins and two libraries identical across VM, host disk
and loaded assemblies. No Server/Core binary was replaced.

| Binary | Final SHA256 |
| --- | --- |
| ArenaMode.Client | `F0D46F531B0A1D112D2A94758DC29793113AF29252F7F19E363E16B71B8550A0` |
| ArenaMode.BigBrainCompat | `D933C9BEF702CDCE101A46FE8C49BBB61A3FAC759E0AB30CB8D55A1F9D765CF9` |
| ArenaMode.Server, unchanged | `BA468562C633445CC668D709748861456102F1DAFEF5DA61457B732C3348845A` |
| Redeploy.Core, unchanged | `6319028DF7A828999EE2B4CCC8B4ECD8213CE22529EA07A34A1ABA61F4148577` |

Four existing companions were invited again through the native UI. The real
Tactical map screen selected **Arena kits**, recorded in
`kit-retest/kit-selected-menu.png`; layout and selected state were visually
reviewed. Optional loading-screen kit preloading remained **Off**. The native
scenario completed all 14 steps without a forced transition or inventory grant.

The **Reaper** kit was issued at **11:57:14.986**, before first-body creation and
match start at **11:57:29.263**. The first PMC already held its RD-704; there was
no initial respawn/body swap. Round 1 became live at **11:57:46.133** with 5v5.
Native inventory inspection displayed 18 usable/equipped items. This count does
not include all recursive weapon attachments. Knife and secure-container IDs
were retained, and the original pockets template was preserved. Each companion
retained all nine original top-level equipment IDs and templates: **36/36**.
The four additional temporary PFT secure containers are distinguished from that
persistent equipment. See `kit-equipment.json`, `kit-team-gear-comparison.json`
and `kit-live-log.json`.

The supported UiBridge kill hook then triggered a real native PMC death. This
is deterministic death-lifecycle coverage, not proof of death from an enemy
bullet. The player entered spectator mode, the companions continued fighting,
and the timed round ended at **11:58:46.204**. During the next-round transition,
all nine old bot bodies left the registries, the entry kit was prepared before
PMC respawn release, the replacement PMC was bound, and PFT adopted its fresh
leader/followers. Round 2 became live at **11:59:06.270**, again with 5v5.

`round-comparison.json` confirms a fresh PMC body and 18 displayed items whose
templates, placement, loaded ammunition, condition and quickslot data match the
first kit when IDs are excluded. Sixteen item IDs were newly issued; knife and
secure container retained their original IDs. The four companions had fresh
physical bodies with the same ten top-level slot templates each (nine persistent
equipment slots plus their temporary container). This does not assert unchanged
combat ammunition or every nested attachment on the companion bodies.

The restored RD-704 fired **two native shots** through the normal trigger path.
`restored-kit-live.png` shows the live second round, teammates and the weapon
at **28/30 + 1** after firing. No infinite-ammunition hook or item grant was used.

The normal leave flow returned to MainMenu. `verification.json` records:

- Complete server-entry `Inventory`, including fast-panel bindings, identical:
  **314/314 items**, with no issued kit left behind.
- Skills, Stats, Quests, TaskConditionCounters, Achievements, InsuredItems and
  Encyclopedia identical. Health values identical; only native health update
  timestamps are excluded from that comparison.
- All **12 PFT profile/equipment/settings files** byte-identical to the backups.
- All **224 Arena settings** restored with no differences, including the new
  `Tactical/TeamOwnGear=true` default and the previous preload/round-time values.
- Native checked-magazine/chamber collections empty as before. FastAccess has
  zero bound items and four bound equipment cells, matching the baseline counts.
- Server and Core hashes unchanged.

`final-status.json` confirms MainMenu, no active raid, godmode off, no control
lease, and the matching loaded binaries. GPU2 was released for other work.

## Log follow-ups and coverage limits

The final client export contains 176 HTTP NotFound errors from the existing
CraftQueue polling problem and one Arena roster error. The server export
explicitly bounded to the final process contains only CraftQueue Poll/ChannelInfo
errors (224 at capture), with **no repeated Arena local-start rejection**.
`final-server-errors.json` also contains earlier-session history and is not used
to infer errors in the corrected run; use `final-server-session-errors.json`.

At **11:59:39.953**, immediately after the explicit native `Stop(Left)`, the
roster observer reported that a living PFT body disappeared and emitted a
no-contest result during teardown. The same ordering is present in the earlier
own-gear control at **11:43:07.837–11:43:07.912**. This is therefore not specific
to kit selection, but should be cleaned up separately; no claim is made that it
predates this branch. Both exits completed and restored the protected profile.

The 34 EFT trace errors are preserved in `final-eft-errors.json`: ten
`JetMaterial == null`, four menu/loading animator NREs before kit issuance,
eight spawn-marker fix entries, one Transit diagnostic, four PlayerBody disposal
warnings at initial match startup, and seven missing UtilityOperationEvent
entries. No unverified causal attribution to this change is made. The test is
not represented as an error-free process log. Specifically, the four NREs at
**11:56:58** pass through `PlayerModelLoader.CreateWeapon` and
`PlayerModelView.Show`: this is an unresolved preview-model finding. The actual
Confirmation/Deploying model was not captured and visually evaluated in this
run, so neither a visible defect nor a correct preview is inferred from the
successful raid entry. Menu layout and the restored in-raid weapon have separate
visual evidence.

Native coverage is one Factory own-gear control on `3652dda`, and one supplied
Reaper-kit Factory match on `6fc7ca4` with four existing companions and one
death/round transition. Other maps, all kits, one-to-three companion rosters,
long matches, manual loot transfers and enemy-inflicted PMC death were not
exercised here. Offline gates and their mutation controls are reported separately
by the implementation owner.
