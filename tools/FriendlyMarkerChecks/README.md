# Friendly bot diamonds

Run from the repository:

```powershell
pwsh -NoProfile -File tools/FriendlyMarkerChecks/Verify-Contracts.ps1
pwsh -NoProfile -File tools/FriendlyMarkerChecks/Verify-Regression.ps1
```

The first command reads installed EFT/Unity metadata with Mono.Cecil (PowerShell 7).
The second runs a .NET 10 harness linking the actual marker lifecycle, projection
policy, palette and `TacticalMode.FriendlyMarkers.cs` partial. Other Unity/EFT state
is modeled, not a working renderer. Existing TDM `TryClassifyCurrentBody` is a
collaborator here; its actual implementation is covered by `TeamGiveUpChecks`.
No plugin/server project is built or deployed. Eight deliberate changes in scratch
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
- Tick clears previous display. Only the current FPS camera's pre-cull callback
  projects the current head and performs fresh geometric LOS; this follows native
  camera positioning and the spectator's onBeforeRender pose. Secondary cameras
  cannot add markers. Disposing or destroying the HUD removes the callback.
- Limits: 128 scanned players, eleven rays/views per camera callback, 100m range,
  0.65m minimum distance, and 2% screen-edge padding. Duplicate physical entries are
  deduplicated without retaining the bodies across frames. No positive LOS cache.

## Native evidence

Read against `../_decompiled/` and installed assemblies:

- `EFT.CameraControl/CameraManager.cs`: `Exist` avoids constructing the singleton;
  `Camera` is EFT's current FPS camera. Unity `Camera.onPreCull` supplies that exact
  camera after its pose is set; viewport/screen projection uses that camera.
- `PlayerBones.cs`, `EFT/BifacialTransform.cs`: `PlayerBones.Head.Original` is the
  physical head Transform. Missing/non-finite anchors are hidden without guessing.
- `LayersMaskController.cs`: HighPolyWithTerrain covers HighPolyCollider + Terrain;
  LowPolyColliderLayerMask adds geometry proxies; PlayerStaticDoorMask is specifically
  DoorLowPolyCollider, not player colliders; TransparentLayerMask adds glass blockers.
  Trigger volumes are ignored. Head LOS is checked before the 20px UI offset, so an
  above-cover icon position cannot expose a head concealed by a wall.
- `EFT/GameWorld.cs`: current alive and ever-existed dictionaries return physical
  Player objects; a reused profile ID is not sufficient ownership evidence.
- `TacticalMode.cs`: frozen follower arrays are bound before Phase.Live and replaced
  during guarded Rebinding. The new partial only reads those fields; it neither
  modifies match state nor asks PitFireTeam to discover/register followers.

## Four passes and limits

1. Native source/assembly pass established current camera, physical head, mask and
   follower binding contracts before implementation.
2. Production-linked pass covers 99 assertions across TDM/PFT eligibility, walls,
   camera changes, geometry/range/projection, identical IDs, dead/missing/rebound
   actors, normal raids, setting changes, pooling and callback disposal.
3. Adversarial pass rejects eight regressions: enemy visibility, through-wall marker,
   dead marker, rear-camera marker, callback leak, PFT transition leak, PFT body alias
   and PFT live-world alias. No production source is edited by the test runner.
4. Independently read root integration (Settings, ArenaHud and Plugin.OnDestroy).
   Independently reviewed and re-ran the root assembly-banner checks as well; no
   open correctness findings. Parent performs independent review/reverification of
   this leaf before release.

GPU handoff remains required: ordinary first person and protected-team spectator,
standing/crouching/prone heads, movement behind walls/closed doors and glass, head
hidden behind cover, quick camera/target changes, bot death/replacement, round rebind,
UI scaling and a following normal raid. Physics LOS is conservative geometry
visibility, not a simulation of lighting, smoke, fog or postprocessing visibility.
No renderer, live PFT/Unity callback ordering or every map collision mesh can be
proven by the modeled harness.
