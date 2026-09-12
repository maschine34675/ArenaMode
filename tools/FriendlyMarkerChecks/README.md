# Friendly bot diamonds

Run from the repository:

```powershell
pwsh -NoProfile -File tools/FriendlyMarkerChecks/Verify-Contracts.ps1
pwsh -NoProfile -File tools/FriendlyMarkerChecks/Verify-Regression.ps1
```

The first command reads installed EFT/Unity metadata with Mono.Cecil (PowerShell 7),
then runs the production-linked baseline and three compiled projection faults.
The second runs all fourteen compiled faults and the .NET 10 harness linking the actual marker lifecycle, projection
policy, palette and `TacticalMode.FriendlyMarkers.cs` partial. Other Unity/EFT state
is modeled, not a working renderer. Existing TDM `TryClassifyCurrentBody` is a
collaborator here; its actual implementation is covered by `TeamGiveUpChecks`.
No plugin/server project is built or deployed. Fourteen deliberate changes in scratch
source copies must fail their intended assertions; production sources stay intact.

## Behavior and integration

- `ArenaFriendlyMarkers.Create(RectTransform)`, `Tick(ArenaMatch)`, `Dispose()`;
  root-owned ArenaHud creates/ticks/disposes it and Plugin destruction detaches HUD.
- F12 `HUD / ShowFriendlyMarkers`, default true, uses normalized FriendlyTracerColor.
- Eleven reusable 10px diamonds with a 7px colored fill and dark border; no assets,
  font dependency, input raycasts, labels, health values or offscreen arrows.
- Only current living friendly AI bodies qualify. No local human, opponents,
  unknown actors, copied profile aliases, dead bodies, ordinary raids or solo modes.
- TDM uses its owned current-body API. Protected Tactical reads exact frozen
  owner/body/profile bindings only while Live, in the same game/match, with current
  alive and ever-existed dictionaries agreeing. No PFT discovery/admission call or
  historical tracer roster is used; rebind/aftermath hides all old markers.
- Tick clears previous display. `Canvas.preWillRenderCanvases` projects the current
  head and performs fresh geometric LOS using the native FPS camera, before Unity
  rebuilds UI graphics. No camera callback or forced recursive canvas rebuild is
  used. Disposing or destroying the HUD removes the callback.
- Three capped diagnostic samples from Tick report scope, camera readiness, canvas
  callbacks and last-pass alive/friendly/head/projected/blocked/shown counts. Samples
  start after five seconds, at least ten seconds apart, even if rendering never runs.
- Limits: 128 scanned players, eleven rays/views per canvas callback, 100m range,
  0.65m minimum distance, and 2% screen-edge padding. Duplicate physical entries are
  deduplicated without retaining the bodies across frames. No positive LOS cache.
- Projection uses normalized camera coordinates and current display pixels. The
  exact camera's applied `SSAAImpl.CurrentState` identifies the render viewport
  that native DLSS/SSAA expands to full output; an absent/disabled scaler or NOSCALE
  retains normal camera viewport offsets. An unknown active state hides markers.
  Output and viewport changes are read every callback; input RT dimensions and
  requested quality ratios are not used to place HUD graphics. See
  [FRIENDLY-MARKERS-REVIEW.md](../../FRIENDLY-MARKERS-REVIEW.md).
  A disabled scaler is a defensive fallback; third-party mods that disable only
  SSAAImpl while keeping direct postprocessing propagation alive are not simulated.

## Native evidence

Read against `../_decompiled/` and installed assemblies:

- `EFT.CameraControl/CameraManager.cs`: `Exist` avoids constructing the singleton;
  `Camera` is EFT's current FPS camera; viewport/screen projection uses that camera.
- `PlayerBones.cs`, `EFT/BifacialTransform.cs`, `EFT/PlayerSpiritBase.cs` and
  `EnemyPart.cs`: `Head.position` resolves accumulated/imitated bone poses before
  falling back to the Original Transform. Reading Original directly bypasses live
  virtualized poses. Missing/non-finite anchors are hidden without guessing.
- Installed `UnityEngine.UI.dll`: `Graphic.OnDisable` calls `CanvasRenderer.Clear`
  and disables rebuild participation; `OnEnable` only calls `SetAllDirty`.
  `CanvasUpdateRegistry` subscribes `PerformUpdate` to `willRenderCanvases`.
  Installed `UnityEngine.UIModule.dll`: `ForceUpdateCanvases` explicitly invokes
  `SendPreWillRenderCanvases` before `SendWillRenderCanvases`. We subscribe to the
  earlier event and never call ForceUpdateCanvases. See also Unity's
  [callback documentation](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Canvas-preWillRenderCanvases.html).
- `LayersMaskController.cs`: HighPolyWithTerrain covers HighPolyCollider + Terrain;
  LowPolyColliderLayerMask adds geometry proxies; PlayerStaticDoorMask is specifically
  DoorLowPolyCollider, not player colliders; TransparentLayerMask adds glass blockers.
  Trigger volumes are ignored. Head LOS is checked before the 30px UI offset, so an
  above-cover icon position cannot expose a head concealed by a wall.
- `EFT/GameWorld.cs`: current alive and ever-existed dictionaries return physical
  Player objects; a reused profile ID is not sufficient ownership evidence.
- `TacticalMode.cs`: frozen follower arrays are bound before Phase.Live and replaced
  during guarded Rebinding. The new partial only reads those fields; it neither
  modifies match state nor asks PitFireTeam to discover/register followers.

## Four passes and limits

1. User playtest on 2026-09-06 found no visible diamonds despite enabled settings
   and admitted friendly TDM bodies in the log. The old tests asserted GameObject
   activation, not rebuilt geometry. Native inspection identified late activation
   and direct Original-head reads as concrete implementation defects; their exact
   contribution in that raid is not observable in the old log.
2. The initial 114 production-linked assertions model active state separately from
   rebuilt geometry. An independent positive control reproduces active-but-empty
   graphics over repeated old-order frames; the new callback must have geometry
   ready in the same UI rebuild even without camera-cull callbacks. Virtual head
   poses override stale Original coordinates. Existing ownership/LOS/lifecycle,
   pooling and normal-raid checks remain, plus capped/no-callback diagnostics.
3. The initial eleven temporary mutants include old late camera activation, stale Original
   head reads and uncapped logging, alongside the eight prior ownership/visibility
   failures. Each must fail its intended assertion, never merely compilation.
4. Independent native/render audit reviewed implementation, harness fidelity and
   root lifecycle hooks. Parent reruns installed contracts and regressions before
  Release deployment. This remains model/native-contract proof, not GPU output.

On 2026-09-12 the DLSS correction adds 34 assertions (148 total) and three compiled
faults (14 total). It covers native reduced input rects, varying render ratios,
supersampling with an attached/detached RT, requested versus applied scale state,
disabled/unknown states, changing resolution, camera viewport offsets and partial
clipping, canvas scaling, the unchanged 30 HUD pixel offset, exact camera replacement,
and clearing already-visible geometry when projection inputs become invalid.
The three new faults reintroduce input-pixel placement, discard ordinary viewport
offsets or ignore applied scaling. Each must compile and fail the intended assertion.
Native GPU DLSS/FSR output and animation remain pending; these checks use modeled
camera projection and canvas conversion, not a running Unity renderer.

Technique comparison: `../_reference/enemymarkers-master` uses world `TextMesh`,
coroutine updates and native `MainParts[head].Position`, rather than toggling UI
images in camera pre-cull. Its last-known/ghost markers are not adopted. Its README
declares MIT but the supplied tree has no complete license file; no code, binary
or asset from it is copied or bundled. Arena keeps its original pooled UI diamonds.

The user confirmed visible diamonds after the render fix. The requested follow-up
raises the marker from 20 to 30 HUD pixels above the projected head; line-of-sight
still targets the head itself, so this does not reveal a teammate behind cover.

Further GPU coverage remains required: ordinary first person and protected-team spectator,
standing/crouching/prone heads, movement behind walls/closed doors and glass, head
hidden behind cover, quick camera/target changes, bot death/replacement, round rebind,
UI scaling and a following normal raid. Physics LOS is conservative geometry
visibility, not a simulation of lighting, smoke, fog or postprocessing visibility.
No renderer, live PFT/Unity callback ordering or every map collision mesh can be
proven by the modeled harness.
