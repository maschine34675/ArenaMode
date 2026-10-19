# Bot effects / spawn shield native checks — 2026-10-19

**Preparation only; no GPU acceptance is claimed by this document yet.**
Parent deployment, loaded build identity and actual results must be appended
before any case below is marked passed. Host: SPT-GPU-2, canonical test profile
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
freeze, teleport, reanimate or alter native materials on bots. Parent alone owns
installation/start; the leaf has performed **no GPU mutation during preparation**.

Commands and their verification must be separate tool turns. Every command gets
a fresh ID and produces its own JSON, including rejection reasons. Existing-buff
rejection, invalid body/cover placement, inactive shader or a renderer with zero
draw commands invalidate a proposed visual positive; none is proof of occlusion.

## Result ledger (fill with executed evidence only)

| Case | State | Candidate / evidence |
|---|---|---|
| Standalone probe compile | PASS (offline only) | .NET native references; no deploy targets, no production dependency. Exact final build/hash recorded by parent before use. |
| F1, P1, C1–C3, B1–B2, L1–L3, N1 | NOT RUN | Awaiting parent review/deployment; no generated screenshots or gameplay claims. |

