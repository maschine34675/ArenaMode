# Bot effects / spawn shield native checks — 2026-10-19

**Representative native acceptance completed; boundaries are recorded below.**
Host: SPT-GPU-2, canonical test profile
`6ab7db1c64f04b565cf78be9` (nickname `SPT-GPU-2`). Ignore nickname-matching
sync-conflict profile files. Parent already saved the canonical profile, 77 config
files and deployed client/Core/Killcam identities under the task's `parent/`
artifact folder. Do not replace that backup or restore unrelated synced settings.

## Preflight and native evidence

1. Read UiBridge overview/start/raid/shooting help. Check `uibridge_status` and host
   lease before anything that acts on the GPU. Parent reviews/deploys the paired
   Arena DLLs and optional temporary probe, then starts via `gpu-run start`.
2. Record candidate source revision, server/client/companion/Core/Killcam/UiBridge
   loaded MVIDs and disk SHA-256; loaded versus disk mismatch requires restart.
   Record resolution, rendering path, anti-aliasing/DLSS mode and relevant pinned
   test options. A source build is not native acceptance.
3. Only temporary godmode/infinite ammo and short controlled fixture requests may
   supplement normal gameplay. Use UiBridge's bounded real firing; do not edit
   native bot movement or create fake body/Animator copies for the visual test.
4. Each visual case keeps an actual full game frame and adjacent snapshot/log
   evidence with body/health/camera IDs and active timers. Keep full raw frames;
   optional crops are explanatory derivatives. A success on a synthetic cover
   does not replace success behind existing map cover.

## Required cases

| Case | Procedure | Acceptance / honest limit |
|---|---|---|
| F1 Factory negative | Start independent TDM on Factory with a nonzero player spawn protection setting. Observe initial admitted bots and at least one replacement spawn. Inspect the mode's frozen shield duration/list plus `IsBotSpawnProtected` readback while live. | No bot spawn shields/contours at either spawn; player protection stays enabled. A snapshot only after the usual four seconds is insufficient. |
| P1 ported positive | Start TDM on Equator, Bay5 or Air Pit with team-box negotiation. Temporarily pin shield duration to 15s if needed, then restore. Capture the real opening Live transition and a replacement bot. | Native team-box placement, real spawn protection plus narrow pale contour, no large cage. First actual shot releases protection; timer also expires without a shot. No injected spawn protection is allowed. |
| C1 natural cover | Follow a naturally protected/buffed visible bot into full existing opaque cover and a doorway/half cover; move the camera. | Visible parts retain the narrow contour, occluded body pixels are absent. Body/gear does not gain a broad halo around cover. One head ray cannot prove partial occlusion. |
| C2 controlled full cover | Use the explicit test-only probe on a current real visible bot. Record active blue/pink production buff, create `full` render-only cover and capture. | Confirm the opaque cube actually covers the whole bot and the effect is still active. No silhouette pixels through the cube. This is a synthetic depth-render test, not pickup/pathfinding proof. |
| C3 controlled partial cover | Same physical bot, `lower-half` cover while buff remains active; capture before/after moving camera. | Upper visible body contour remains; lower occluded body has no contour. Cover tracks pre-cull; if the target escapes viewport or dies, the run is invalid rather than a pass. |
| B1 both buffs | Observe/inject each production Shield and Double Damage separately, then `both` on an unbuffed exact bot; retain close visible body frames. | Blue and pink stay legible simultaneously with no cage. Real duration readback; if natural pickup overwrites a timer, label it and repeat. |
| B2 natural pickup | Observe at least one bot actually collecting an enabled ground powerup; retain pickup disappearance/log and bot effect. | Actual collection activates the correct native effect/visual. Reflected Grant calls alone do not cover this case. |
| L1 expiry/death | Let timed buffs expire on a live bot, then in a separate case kill a buffed bot with real fire. | Corresponding lease/source draw disappears; no stale contour on corpse or reused body. Other still-active buffs remain unaffected. |
| L2 camera/HUD | Capture normal first person and scoped/current camera transition where available; Arena HUD off; repeat DLSS on if supported. | Effect remains world-aligned and depth-correct, no duplicated contour/camera-buffer residue. If a camera path intentionally lacks safe depth, absence is explicitly recorded, never claimed as a visual pass. |
| L3 raid exit | Leave through normal protected Arena flow with active/expired visuals. Read menu state and merged BepInEx/server/EFT logs. | No owned effect renderer/cover/material/command-buffer left attached, normal return/profile protection, no new model/animation/render errors. Temporary helper removed after EFT exit. |
| N1 ordinary control | Enter ordinary PMC/scav or inspect unarmed admission control without injecting buffs. | No Arena bot effect owner, no new protection. Fixture must refuse a buff there. Native visual control remains separate from offline admission tests. |

For short natural shields, use a real multi-frame capture around Live instead of
assuming a late single screenshot observed the effect. The helper `status` exposes
current bots but does not prove the earlier timer was ever positive. If no native
pickup can be observed within a bounded test, record B2 as unverified rather than
making bots collect by changing production gates.

## Temporary fixture protocol

See `tools/BotEffectNativeChecks/README.md`. The helper builds independently and
does not deploy. Its explicit `buff` creates at most a 15-second real production
buff on an exact existing live AI body; no existing powerup may be overwritten.
Its collider-free cover is an owned opaque renderer, updated only for the exact
camera/body and removed on expiry/death/world/health/camera change. It does not
freeze, teleport, reanimate or alter native materials on bots. Parent reviewed the
helper before granting exclusive GPU2 control and explicit probe deployment.

Commands and their verification must be separate tool turns. Every command gets
a fresh ID and produces its own JSON, including rejection reasons. Existing-buff
rejection, invalid body/cover placement, inactive shader or a renderer with zero
draw commands invalidate a proposed visual positive; none is proof of occlusion.

## Executed result — 19 October 2026

Production source candidate `612e494` (scope `62cbc2d`, renderer `f23b25b`),
unchanged throughout all three EFT processes. Client SHA-256:
`0BD56ABE20E1A49A03A2F32A17F78E7550A50A96484CA0108D1090A85AEF0960`;
companion `35BE9A371DB5F2076AF487AF6F17486C075756563918A34B6B3E232375F3C409`.
The client logged loaded MVID `fca87726-4a1a-4846-9606-fca83d376dc6`.
UiBridge reported all 19 compared plugins and two libraries identical between
VM build, host disk and loaded process. Server/Core/Killcam remained unchanged;
parent independently saved the final binary comparison.

Temporary probe SHA-256 used for the final native captures:
`28171760CF97168476954E0906E89E74F523DAC665EFD0D97D0DF517A85F95A0`,
build MVID `c7c94c40-64b8-40e0-9681-d6181cbb024b`. A separate runtime MVID read
for the helper itself was not captured. Earlier probe iterations and their failed
captures are preserved, not counted as positive controls. All final accepted
four-stage sequences use this last probe and the same production DLLs. After
cleanup, trimming trailing blank lines and recompiling the helper produced
`7AA2E748F7F1819B93DFE11E27355CE1A6878B27412594621D9917628B625666`
(zero warnings/errors); that formatting-only helper rebuild was not redeployed.

Sessions: `13ea1b23` (12:49:45), `2849e793` (13:02:37), `18257178`
(13:14:30), host local UTC+02. UiBridge `0.9.4+42bbfb38`, EFT
`0.16.9.5-40743`, Unity `2022.3.43f1`, 1920×1080 borderless. Baseline
TAA High, DLSS off; the final Bay5 tests switched to DLSS Quality through the
native graphics menu. Actual input/depth dimensions became 1280×720 and camera
rect `(0,0,0.6666667,0.6666667)`. No inferred resolution substitution was used.

Evidence paths below are relative to
`artifacts/bot-effects-20261019/native/`. Source JSON and full framebuffer PNGs
remain together. Parent independently reviewed the principal image sequences,
identity readbacks and cleanup comparison.

| Case | State | Evidence and observed result |
|---|---|---|
| Probe compile | PASS, offline | Standalone native references, no deploy targets or production dependency; zero warnings/errors. |
| F1 Factory negative | PASS | `factory-negative-initial.json`, `factory-negative-replacements.json`: frozen bot protection 0, protected registry empty, all initial and distinct replacement bots unprotected while global player setting remained 15. A natural pickup shield on another body remained active, so pickup protection was not globally suppressed. |
| P1 ported positive | PASS for initial/replacement scope | `equator-opening-live.json` and `bay5-tdm-initial.json`: all three initial bots protected, frozen duration 15. `bay5-tdm-replacements3.json`: new viniHNS body `-1714470`, profile `6ad5fee24d08e42ffc7fd657`, protected=true and contour flag Spawn. The earlier roster had different bodies; two other replacements had already released protection. No injected spawn shield. A separate close-up pale-only raster/color test was not obtained. |
| B1 + C2/C3, TAA | PASS | `bay5-sequence1.json`, `06f485c78596462c911f2f8e5f566870-*.png`, frames 4490/4502/4514/4526: real moving Fatheals, explicitly injected Shield+DoubleDamage, both timers remain above 14s. Blue/pink narrow body contour, lower body disappears behind half cover, no contour through full opaque cover, contour returns after removal. Synthetic cover, not a natural pickup claim. |
| C1/B2 natural gameplay | PASS, representative | `equator-sanote-buff.json` refuses injection because Sanote is already buffed; `equator-sanote-buff.png` shows the natural pink upper-body contour, followed by `equator-sanote-natural.json` with DD9.44s and `equator-sanote-natural.png` behind existing wooden cover without a revealed silhouette. The 12:53:46.049 pickup log records natural DoubleDamage collection. `dm-natural-beforecover.json` additionally records a natural Shield29s+DD2.94s on the sole DM opponent, with no successful injected grant in that case. |
| DLSS Quality + HUD setting | PASS, representative | `bay5-dlss-sequence2.json`, `c786c9eb48b64704aa3b33fb371c9096-*.png`: centered moving body and both colors correctly aligned, half/full cover depth-correct at native 1280×720 input. HUD Enabled=false removes the ordinary Arena panels, but the score bar remains by its existing separate behavior; bot effect remains. |
| Off-center DLSS | PASS with image-quality limitation | `bay5-dlss-offcenter2.json`, `757930ab8e5744c28f2c920874e2ccf3-*.png`: body around x1280 of the 1920-pixel display, contour attached to visible head/shoulders; the foreground weapon occludes lower pixels, full test cover removes all contours, removal restores them. Vanilla hit/suppression blur affects the whole world image. This does not certify every DLSS preset or magnified scope camera. |
| L1 expiry | PASS | `bay5-expired.json`: same Fatheals body `-1327526` alive after the earlier 15s grant, all buff timers zero, contour owner absent. Later natural effects can create a new owner normally. |
| L1 death before expiry | PASS | `bay5-akiw-final-grant.json`: Akiw body `-1706170`, DD15s at frame125858 / 13:34:33. Actual VPO-209 firing kills him by headshot (`bay5-akiw-death-status.json`). `bay5-akiw-final-post.json`, frame126574 / 13:34:39, already excludes that body; unrelated new Extrenz body `-1758052` retains its own Spawn flag. This six-second interval is shorter than the injected timer. |
| L3 exit | PASS with inspection limit | All raids left through normal protected disconnect/results/menu flow. Menu inspector found zero ArenaBotEffectRenderer; final probe snapshots already have Cover=null. The second inspector query used BotEffectNative rather than the actual ArenaEffectTestOpaqueCover name, so it does not independently certify zero cover objects. Native godmode/infinite-ammo hooks ended with the raid. EFT then quit gracefully and UiBridge confirmed unreachable for five seconds. |
| N1 / alternate cameras | NOT RUN natively | No ordinary PMC/scav control, killcam/spectator transition, magnified scope, every upscaler preset or every map was added to this bounded run. Production admission/ownership regression checks remain separate evidence. |

### Invalid attempts retained

Opening Equator stills and `d97c596bf13e4fe9a8b42d81026549c9-*.png` have the
target/test cube behind existing map geometry and cannot prove a visible positive
or synthetic occlusion. Several explicit grants were correctly rejected because
the target died or already carried an effect. `bay5-dlss-sequence.json` and
`bay5-dlss-offcenter.json` timed out waiting for a representative clear view.
Neither is counted. The earlier Fatheals kill was later than its injected timer;
it proves removal/replacement only, not death while buffed. The later Akiw case
is the valid timed death proof. `Ok=true` on a sequence is never its acceptance
criterion by itself.

### Logs and restoration

Full merged Warning/Error exports are `session1/2/3-merged-warnings.json`; raw
BepInEx/server/EFT sources are under `raw-logs/`. After filtering each export to
its own process window, the counts are 164, 43 and 157. There are no matching
ArenaMode/Redeploy/Killcam/bot-effect errors, shader/render-texture failures,
NREs or missing-reference exceptions in those filtered exports. Existing EFT
`JetMaterial == null`, item-node/handler diagnostics, missing extractor_smoke
scripts, map/season/spatial-audio messages and SPT Fence/profile lookup warnings
remain. This is not a globally clean log or proof that all earlier model issues
were fixed. See `log-review.json` and the parent's independent session3 review.

The known temporary settings were restored only to their recorded before-values:
SpawnProtectionSeconds 4, DeveloperMode false, HUD Enabled true, DM FFA true,
TDM limits 50 kills/600 seconds and the five remembered participant counts.
UiBridge correctly refused the hidden participant keys; after EFT had stopped,
only those exact menu-owned keys were restored from the parent's baseline.
The failed HUD config-pin attempt is retained in `hud-ledger.json`; the later
successful native config action and actual HUD-off images are the executed test.
DLSSMode/DLSSEnabled were restored to Off/false, without rewriting other graphics
options. All 77 config files and Graphics.ini are finally **byte-identical** to
their respective before-copies (`cleanup-final.json`, `restored-config-hashes.json`).

Only the exact temporary helper DLL was removed on VM, GPU2 and the inert
Syncthing copy on GPU1. Its exact VM Syncthing temporary DLL was also removed;
no generic sync cleanup was performed. Its per-request files were archived under
`raw-fixture-json/` and removed from the owned C:\Temp fixture directory.
Production pair remains deployed. Parent independently compared the profile:
inventory (314 items), XP, skills, quests, trader info, stats, encyclopedia and
Info are identical; only routine Health.UpdateTime/Hideout timestamps advanced.
No raw profile backup was restored while the SPT server was running.
