# Native model controls — GPU2, 19 October 2026

Both preview families reproduce in a fresh ordinary PMC/PFT process without
ArenaMode.Client, its BigBrain companion, Killcam or Redeploy.Client. This is
a positive control: those client components are not necessary causes of this
reproduction. It is not a mod-free comparison, and does not claim that every
historical model/animation family has the same cause.

No candidate binary was built or deployed by this leaf. No production code was
changed. The parent separately owns candidate repair verification.

## Process identity and scope

All times are host local, UTC+02:00. Test profile was SPT-GPU-2,
`6ab7db1c64f04b565cf78be9`; no personal profile was used. EFT was
0.16.9.5.40743, Unity 2022.3.43f1, UiBridge 0.9.4+42bbfb38.

| Control | Process | UiBridge start | Loaded state |
| --- | --- | --- | --- |
| Ordinary PMC, clients present | `30eaad04` | 09:55:34 | 42 plugins / 6 libraries matched VM, host disk and loaded identity. ArenaMode, Killcam and Redeploy client/Core patches present. |
| Ordinary PMC, clients isolated | `726752c5` | 10:15:27 | 39 plugins / 5 libraries matched. No ArenaMode, Killcam or Redeploy.Client plugin; no ArenaMode, Killcam or Redeploy.Core/Client Harmony patch entry. |

The first process and raid were started by the parent, who verified
ArenaSession inactive/unarmed. This leaf took over that running ordinary raid,
then owned all subsequent GPU2 actions. Both raids used native PMC Factory day,
bots=None, and the same four PFT companions invited in MY SQUAD. The recipe
reported the boss option still checked; it was not changed. The first parent
run had godmode enabled, which ended on leaving. The isolated run used no
godmode, infinite ammunition, kill, movement, weapon-grant or fire hook.

The stronger control was necessary because Arena creates its third mannequin
even before an ordinary PMC selection. Unselecting Arena alone does not remove
that shared-pool exposure. Four exact files were temporarily renamed away from
their `.dll` names:

- `BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll`
- `BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.BigBrainCompat.dll`
- `BepInEx/plugins/maschine-Killcam.dll`
- `BepInEx/plugins/maschine-Redeploy.Client.dll`

Each was backed up and hashed first, with matching VM, GPU2 and GPU1 bytes.
GPU1 was confirmed not running EFT and both hosts were reserved against our
deployments. The renamed files were verified absent as DLLs on GPU2 before
the fresh start. Shared Redeploy.Core remained on disk; its normal drivers
were absent and no Core Harmony patches appeared. Arena server, ArenaMapPort,
PFT 0.10.3 and other installed mods stayed present. Thus this excludes those
four client files as necessary triggers, not every possible server/data mod.

## Positive observations

| Family | Ordinary PMC with clients | Fresh isolated PMC |
| --- | --- | --- |
| Weapon preview NRE | Four EFT records at 09:59:44.353–.365; original trace lines 1719, 1726, 1733, 1740 | Four EFT records at 10:18:11.974–.992; trace lines 1719, 1729, 1739, 1749 |
| Undisposed PlayerBody | Four EFT records at 10:00:25.959; trace lines 1785–1788 | Four EFT records at 10:18:52.095; trace lines 1797–1800 |

There were zero Error records in the isolated process's merged pre-squad
snapshot. The errors appeared after the relevant preview/loading exposure.
Both processes preceded any Arena raid, replay or player respawn in their
respective cold session. The repeated records are source records, not proof
of four distinct failing weapons.

With the Arena Parent hook absent, the isolated NRE exposed a more precise
native stack:

```text
AnimationSystem.UnityAnimatorWrapper.GetParameter [0x0000e]
ObjectInHandsAnimator.SetAnimatorGetter [0x0003d]
FirearmsAnimator.SetAnimatorGetter
WeaponPrefab.Parent [0x00124]
WeaponPrefab.Init
EFT.UI.PlayerModelLoader.CreateWeapon
EFT.UI.PlayerModelLoader.Load
EFT.UI.PlayerModelView.Show(PlayerVisualRepresentation)
```

`../_decompiled/AnimationSystem/UnityAnimatorWrapper.cs:534–541` obtains the
parameter from `_parametersCache` and reads its name/type.
`../_decompiled/ObjectInHandsAnimator.cs:94–105` loops through native animator
parameters. This narrows the observed failure beyond the historical patched
Parent stack. It does not identify the exact null cache/member or weapon
asset; these were not sampled. No parameter or animator fix is inferred.

During isolated loading the read-only inspector found one inactive
`EFT.UI.Matchmaker.MatchMakerGroupPreview` at
`Menu UI/UI/MatchMaker AcceptScreen/PreviewsPanel/GroupPreview`, scene
DontDestroyOnLoad. Reading its `list_0` then timed out because the main thread
was loading. Consequently this run does not claim a measured four-body
pre-destruction list or `_active` values. The historical four owner contexts,
exact installed PFT/native disposal chain, and the new independent positive
control support the upstream lifecycle attribution documented in
[the source audit](MODEL-PREVIEW-INVESTIGATION-20261019.md).

The isolated raid reached BattleUI. Native Disconnect was initiated about
17 seconds into that live phase after the decisive records were captured;
the leave recipe returned to MainMenu in 22 seconds. The original takeover
raid was also left normally (28-second result-screen sequence). Healing was
explicitly skipped because the original test data would be restored with
the server stopped. No additional speculative asset runs were performed.

## Logs and limits

Private evidence is under `artifacts/model-errors-20261019/native/`:

- `allmods-takeover-status.json`, `isolated-start-status.json`,
  `isolated-raid-status.json` and `isolated-final-menu.json` record actual
  process/build/patch/UI state. Final menu had one occupied local party slot
  and four empty slots; temporary invitations were no longer active.
- `pmc-cold-final-warnings.json` and `isolated-complete-warnings.json` contain
  the complete merged Warning/Error selections for their explicit sessions:
  respectively **38/38** and **36/36** records, `truncated=false`, no cut-off
  entries. The three original streams are archived in `cold-allmods-raw/`
  and `isolated-raw/`; source file and line remain attached to each selection.
- Both selections have 18 EFT Error records, one BepInEx Error and ten
  BepInEx Warnings; server Warnings differ, nine versus seven. Besides the
  two tested families, the EFT entries include ordinary spawn/transit output
  and an inactive MenuScreen coroutine error during native leave. No claim
  is made that leaving produced a clean log.
- `control-summary.json` independently recounts the selected records.
  `isolated-loading-errors.json` captures the NRE before raid startup;
  `isolated-raid-errors.json` captures the four body errors before leave.

No suitable CheckMagazine, M32, Benelli or hash-zero-event comparison was
attempted. Absence of those families here is not evidence of a repair or an
own-mod exclusion; the relevant active AI/assets/operations were not assured.
The two positive preview controls do not accept the later Killcam or diagnostic
changes. A new candidate still requires its own loaded-identity and gameplay
verification.

## Restoration, provenance and unrelated arrival

Ordinary PMC raids are not Arena-protected transactions. The original profile
had 314 items; the first post-raid snapshot also had 314, but Inventory, Skills,
Stats and Health changed. Quests, TaskConditionCounters, Achievements,
InsuredItems and Encyclopedia compared equal. Eight of the twelve PFT store
files were byte-identical after that first raid; four changed. The isolated
post-raid profile shows the same equal/changed section classification.

Both actual post-raid profile/PFT states were saved before any overwrite.
The original profile and twelve original PFT files were explicitly restored
before the isolated comparison, then again at the end, each time with EFT
closed and GPU2's server stopped. These restorations are test cleanup, not
evidence of profile invariance. `first-pmc-restoration.json`, both profile
comparison reports and the saved post-raid files make that distinction visible.

Final verification confirmed:

- Four temporarily isolated files restored to exact original hashes on VM,
  GPU2 and GPU1; no temporary disabled filename remained at those paths.
- All **52 preexisting GPU2 plugin-directory DLLs** matched their original
  hashes. Their manifests are retained; unrelated DLLs were not overwritten.
- Original GPU2 profile SHA256
  `54BCCAC55A3D1A6485FFE4B41BDC923C858967D1F914CA5CE86FB490F3760FAC`
  restored exactly; all twelve PFT store files matched original byte hashes.
- Six relevant configuration files matched their originals. They had also
  been byte-identical before restoration; no logging level, DeveloperMode,
  Killcam, Redeploy or PFT configuration edit was introduced by these controls.
- Native MainMenu/empty-party state was captured before graceful quit.
  At handoff both GPU hosts had no EFT process; GPU2 server was stopped.
  No live test hook or control operation remained.

An unrelated new `BepInEx/plugins/RAID_REVIEW.dll` arrived during this work,
131584 bytes, file timestamp 10:20:23, SHA256
`8BBCA8235A13A34E4FF2B1B4672E5AAEA3BB95D4F744B9993B691FE11F37E69A`.
It was absent from the starting manifest and was deliberately left untouched
after notifying the parent. The positive control process started at 10:15:27,
and its loaded-state snapshots do not list Raid Review; the file arrived after
the decisive observations. Therefore it does not invalidate this cold control.
The whole plugin directory is not claimed identical: there is this one unowned
addition, which the next candidate process may load.

`plugin-restoration.json`, `final-restoration.json` and
`restoration-verification.json` preserve exact hashes and the extra-file
exception. These files, raw logs and profiles are private evidence, not release
assets. GPU ownership was handed back before further candidate work.

## Review and gates

Four passes: complete paired exposure/evidence; check actual loaded identities
and original-versus-restored state; challenge preselection mannequin, passive
Core/server scope, duplicate records and the unrelated DLL; recount original
selections and verify exact restoration with bounded native coverage.

Leaf N1/N2: **2 met, 0 unmet, 0 abandoned**. These gates certify causal controls
and their cleanup, not gameplay acceptance of the separately prepared fixes.
