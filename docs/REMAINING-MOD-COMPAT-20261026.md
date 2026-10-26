# Remaining optional-mod compatibility work

Status: **bounded native acceptance and policy update, 2026-10-26**. This records the
follow-up to [the seven-mod review](SEVEN-MOD-COMPAT-20261025.md) and the earlier
[Fast Grenade Throw review](FOUR-MOD-COMPAT-20261025.md). It does not certify
every option, map or combination. A subsequent native
Labyrinth test exposed an Amand initial-scene blocker; Amand remains unlisted
pending a separately justified solution. Fast Grenade Throw, Quick Cancel,
HUD Breakout and PiP Disabler received exact reviewed-version whitelist entries
after Arena and standalone tests. PiP's corrected path passed two scoped Arena
deaths and one scoped standalone death.
Temporary test mods, overrides and profile changes have been removed/restored;
the tested production binaries and updated whitelist are deployed.

## Reviewed releases

| Mod | Plugin identity | Version | Current result |
|---|---|---|---|
| [Fast Grenade Throw](https://sp-mod.com/mod/2783/fast-grenade-throw) | `com.vultify.fastgrenadethrow` | 2.0.0 | Whitelisted after actual Arena Molotov/RGD throws and standalone RGD control. |
| [Amand's Graphics](https://github.com/ArchangelWTF/AmandsGraphics/releases/tag/v1.8.0) | `com.Amanda.Graphics` | 1.8.0 | Replacement-camera correction passed two Factory Arena replacements; native Labyrinth initial activation failed before replacement. Keep unlisted. |
| [HUD Breakout](https://github.com/Niharkanta1/SPT.HudBreakout/releases/tag/1.2.0) | `com.deadwolf.hudbreakout` | 1.2.0 | Whitelisted after Arena/standalone physical-body continuity tests. No body adapter needed; presentation/semantic limits remain. |
| [Manimal's Quick Cancel](https://sp-mod.com/mod/2969/manimals-quick-cancel) | `com.manimal.quickcancel` | 2.0.0 | Whitelisted after real Arena/standalone cancellation after replacement. The rare pending-death race has offline coverage only. |
| [Picture in Picture Disabler](https://sp-mod.com/mod/2667/picture-in-picture-disabler) | `com.fiodor.pipdisabler` | 2.1.0 | Whitelisted after corrected scoped-death tests in Factory Arena and standalone Redeploy. |

The exact released DLLs used for source/metadata review and staging were:

| DLL | SHA-256 |
|---|---|
| `FastGrenadeThrow.dll` | `1CF1FA49DDA94E9936053043E171A5EE1C2AAB6A9C6A696A7B532235A8D83AE4` |
| `AmandsGraphics.dll` | `0BAD292DA16386DE8A737C5C627016030A343256A3670823ABEE805439CB75BD` |
| `HudBreakout.dll` | `317BAD15E91848B6ACEF387CF9EF3F48C9E0A4AED63328E03155BFE194560A34` |
| `QuickCancelClient.dll` | `3C6C87112A7C0739C505A7F204DB2E3F13932DFDC8C568519E34B8BF685463BC` |
| `PiP-Disabler.dll` | `12712B3FDE8BC5C24031B2BFB2B54FF9383582E72F2608148CD391E2E4B83B44` |

Fontaine FOV Fix 4.1.0 was staged as an existing integration/control, DLL SHA-256
`025A7816CAAF0756050F58202459C76D1392349D890038424BB465619B549B4F`.
Plugin versions, rather than assembly versions, identify these releases:
HUD Breakout's assembly is `0.0.0.0`, PiP Disabler's is `1.0.0.0`.
Foreign packages remain temporary test inputs, not bundled Arena assets.

## Implementation boundaries

### Fast Grenade Throw

FGT's private grenade finder selected the first effective candidate instead of
honoring the native priority grenade. Earlier gameplay demonstrated an RGD-5
being selected instead of Arena's Molotov. Its direct Unity key polling also
bypassed Arena's normal input blocker.

`ArenaMode.Client/Compat/FastGrenadeThrow` now composes the selection with the
current, physical Arena player. A preferred item must be examined, worn, at its
current inventory address and present in the actual effective candidate list.
The throw entry respects Arena panels, respawn/spectator/stand-down states and
weapon swaps. Ordinary PMC/Scav behavior remains foreign-owned; no inventory,
configuration or grenade template is rewritten. No general respawn adapter was
added for FGT because its existing recovery did not require one.

### Shared body changes

Amand, Quick Cancel and PiP corrections live in **Redeploy.Core 1.0.15**, so both
standalone Redeploy and Arena use the same physical-body boundaries. Their source
contracts and limits are documented in
[the Core report](../../Redeploy/docs/REMAINING-BODY-COMPAT-20261026.md).
Build success is separate from acceptance under each driver.

Arena's only additional PiP-specific runtime integration is
`Compat/PipPreloadCompat.cs`. It respects the existing exact
`KitOpticPreloadGuard` marker for a cold kit role-model. The marker remains valid
through foreign `OnDisable` postfixes and is removed by the finalizer, which
preserves the original exception. Mounted optics, unrelated objects and expired
requests are not suppressed. This prevents Arena's own preload models from
taking PiP's live reticle state; it does not give Arena ownership of every optic.

## Native evidence available at this checkpoint

Evidence is kept locally under `artifacts/compat-remaining-20261026/native/`:
`observations.md`, `baseline-bepinex.log`, `candidate2-bepinex.log`,
`candidate2-off-hud.png`, action snapshots and the staged-file inventory.
These are development evidence, not release-package contents.

The baseline was Arena source `a6c5d75` with Core 1.0.14, process `0d9fb70d`.
Two actual Factory Arena replacements reproduced Amand's stale camera cache:
its `Player` matched the living replacement, but `FPSCamera`, `FPSCameraCamera`
and `FPSCameraPrismEffects` were destroyed Unity objects. The current native
`FPS Camera` and its PrismEffects were enabled and alive. Absence of an Amand
exception was therefore not a passing result.

Candidate 2 used process **`2e6e5641`**, started at 09:28:30 local on SPT-GPU-2.
The log records SPT 4.1.6, ArenaMode 0.3.0, Redeploy.Client 1.0.2 and
Redeploy.Core 1.0.15.0; Core was built from `fa72287` plus the pending changes.
The Arena client reported MVID `53903de8-9d6f-446b-b73a-cee90f615cfc`.
The test operator verified loaded files against the host disk at startup, but
did not archive a separate Candidate 2 Core SHA-256. Do not substitute the
older `candidate-deployment.json` hash or a later rebuilt DLL's hash for it.
**PiP Disabler was deliberately disabled for Candidate 2.**

| Exercise | Observed result |
|---|---|
| First replacement | Body/camera `-1410414/-1410560` became `-1503770/-1503916`; Amand matched the new body/camera and remained ON. |
| Second replacement | After a native Insert toggle to OFF, body/camera became `-1578012/-1578158`; Amand matched the new body/camera and remained OFF. |
| HUD continuity | HUD Breakout followed both physical replacement bodies; native arm damage changed health from 440 to 390, and replacement restored 440 before the next injury. |
| Actual fire | Three real MCX shots changed the magazine display to 27/30. The inspected screenshot shows that value and 390/440 health. |
| Grenade selection | FGT threw the priority Molotov (three grenades to two), then an underhand RGD (two to one). F8 while inventory was open consumed neither remaining RGD. |
| Quick Cancel | With medical-use speed set to 1 for the test, `SelectFastSlot4`, then `QuickReloadWeapon` about 700 ms later, dropped the medkit (five medical items to four), restored the held firearm and allowed three actual shots. Pending-controller count was zero afterward. |

Earlier cancellation attempts at approximately 150 ms were too early and do not
prove interruption. A completed cancel followed by a later death also does not
prove the rare pending-throw/death race. That race remains an offline test case.
Runtime key changes (FGT overhand F9, underhand F8) were test controls, not new
defaults. F11 also opened ModProfiler; the operator paused/closed it without
saving a setting.

No compatibility error was reported during the two Candidate 2 replacements.
The log is **not globally error-free**: at 09:38:27.678, after Arena leave was
committed, `BotFirstAid.CG_ApplyToSelf.method_1` raised a null reference from
`MedsInHandsOperation.Finish/OnEffectRemoved`. The available stack identifies a
bot healing callback during raid exit, not its causal owner. This remains a
separate unresolved observation; it is not evidence against Amand or proof that
Quick Cancel caused it.

### Candidate 3: Amand Labyrinth blocker

Process `6533c0c32fa74cc6928ab3a7dd9cd4ac` loaded the archived Candidate 3
Core DLL SHA-256
`C437D1523F48498DC733D4A21902E86DB9AEC47A595A5C221ADCD4401D6BD8AF`
and Arena client SHA-256
`56D2A63CD54FAF7AD2BCD02711C331FBD5E519A301B772387046CDDD1782253A`.
These hashes match the saved binaries and `candidate3-deployment.json`;
`candidate3-loaded.json` records the process. The separate
`candidate3-labyrinth-bepinex.log` shows:

- 09:41:33.699: Amand's own `PrismEffectsPatch.OnEnableAsync` called native
  `ActivateAmandsGraphics`, which raised a null reference during initial spawn.
- Its observed scene fell back to `default`, `levelSettings` was null and
  GraphicsMode remained false.
- 09:41:37.872: Arena's initial kit replacement was refused by Core because
  Amand had not established the required body/camera/scene baseline. The player
  was left without the intended kit. This is a failed acceptance gate.

The released binary confirms the source path: no Labyrinth scene entry exists;
unknown scenes use `!settings`. At IL `02b1` it calls `GameObject.Find`, followed
immediately at `02b6` by `GetComponent<LevelSettings>` without a null check.
The initial native exception precedes Core preparation and does not require a
replacement body. An ordinary PMC control was not executed here, so its outcome
is a source-based expectation rather than an additional native pass/failure.

Bypassing Core's validation would accept partially initialized foreign state;
replaying activation would repeat the same scene lookup. No global setting,
fake scene object or blanket foreign reset was added. The safe current outcome
is to leave Amand unlisted and retain the concrete map blocker. Factory's
successful ON/OFF replacement results remain valid within their tested scope.
Labyrinth exit also logged a separate destroyed-Transform exception in
`TriggerZone.MoveInsideRoutine` at 09:42:50.016; its causal owner is unresolved.

### Candidate 3: corrected PiP path in Factory

The same process then entered Factory. Two initial unscoped deaths do **not**
count as PiP acceptance. Two subsequent deaths with the active optic actually
confirmed before death passed: body/camera `-1775842/-1775988` became
`-1805414/-1805560`, then `-1815380/-1815526`. Old bodies remained present;
PiP's active/last optic references cleared and its owned weapon-cache count
returned to zero after each replacement. A fresh native-installed Specter
activated on the replacement; inspected `candidate3-pip-after-active.png`
shows its centered magnified reticle before the second scoped death. No new
compatibility error accompanied those replacements. Amand and HUD Breakout
also followed the new bodies. This supports the corrected Arena path.

### Standalone Redeploy control

Process `242cfdb6` ran Factory with the Arena client DLL absent and the same
archived Candidate 3 Core. The first death occurred with an M700 optic active
(PiP optic `-1400132`, one cache). Body/camera `-1401980/-1402128` became
`-1562518/-1562664`, with the old body retained and PiP active/last references
cleared and owned caches at zero. A second, unscoped death changed them to
`-1631674/-1631820`; Amand and HUD Breakout followed both new bodies. This second
death proves general continuity, not another scoped PiP case.

Quick Cancel dropped Analgin before death and again on the replacement using
native item grant/bind/use and QuickReload after approximately one second.
An actual FGT underhand RGD throw succeeded at 09:53:43. No error accompanied
the replacements. HUD Breakout displayed the native `SHORTNAME` placeholder
with empty hands, an appearance issue rather than stale-body telemetry. Amand
retained live corpse references in `backLens`/`sightComponent`; optional optic
DOF was not enabled or accepted by this test. Exact startup evidence is in
`standalone-loaded.json`; the coordinating session retains the action record.

The four reviewed entries above are now in `assets/arena-whitelist.json`.
Amand remains unlisted due to the separate Labyrinth failure. This is an
admission decision for the tested releases, not a promise about future versions.

## Offline checks and limits

- Fast Grenade Throw: 87 assertions across five modes, four compiling mutants
  rejected at their intended runtime assertion, and an actual EFT API compile.
- Quick Cancel: 52 assertions across five modes, four compiling mutants rejected,
  and an actual EFT API compile. This proves bounded controller cleanup in the
  harness, not that the native timing race was hit.
- Amand: 187 assertions across ten modes, five compiling mutants rejected, and
  a real-EFT compile with zero warnings/errors. Both final leaf and root runs
  report the same counts.
- PiP: the current leaf report records 92 assertions using production Core and
  Arena preload classes with real Harmony, released metadata checks and an
  actual EFT API compile. Corrective provenance tests include a detached
  post-death weapon and a negative bot-root control. Arena and standalone native
  retests passed within the scopes described above.
- Full suites after integration: ArenaMode 3,213 passed with one known skip;
  Redeploy 283 passed. These are offline suite results, separate from gameplay.

HUD Breakout already polls `GameWorld.MainPlayer`; no speculative adapter was
added. Both HUDs can display the same information, as the inspected screenshot
demonstrates. Foreign hit sounds/killfeeds, raw damage numbers, administrative
bot removals and Arena shield/assist semantics are not made identical by body
continuity. Its grenade count uses rig/pockets rather than Arena's expanded
backpack reach, and its spectator HUD still describes the local player rather
than the observed bot. Users retain control of each HUD/audio option.

Amand's default Factory camera path has bounded evidence; Labyrinth initial
activation has a concrete failure above. Optional depth-of-field/health effects,
NVG/thermal rendering, every killcam/spectator phase and other maps are not
certified by this run. Neither the Factory acceptance nor
the suite results erase the separately recorded raid-exit errors.

## Deployment and cleanup

The final deployment keeps the exact Candidate 3 DLLs tested above, plus the
new external whitelist. Core SHA-256 is `C437D1523F48498DC733D4A21902E86DB9AEC47A595A5C221ADCD4401D6BD8AF`;
Arena client is `56D2A63CD54FAF7AD2BCD02711C331FBD5E519A301B772387046CDDD1782253A`;
Redeploy client is `31102063E9426967C584B653C59E426D59A1E09396F311A32AAA06ADB08CB8BA`.
The subsequent Arena rebuild passed; an independent Cecil comparison found
identical normalized IL, metadata and all 41 resources (26,681 records), with
a different build identifier. The tested artifact was retained for deployment.

The final files match on the development installation and SPT-GPU-2. The cleanup
verified backups and hashes before removing 161 temporary package/probe files,
restored the two existing Arena/Redeploy configs, removed the test-only policy
and foreign configs, restored the GPU2 test profile and verified all eight other
canonical profile copies unchanged. Synchronization exclusions were restored last.
GPU2 reports no running game, server or launcher. No server mod binary, published
version, GitHub release or remote was changed by this task.

Standalone raid exit also logged `Coroutine couldn't be started ... MenuScreen
is inactive`. Like the other exit-only observations, this was not a replacement
failure and has not been causally attributed to these adapters.
