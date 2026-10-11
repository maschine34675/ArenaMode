# GPU acceptance lifecycle compatibility — 2026-09-08

Scope: the two ancillary errors recorded in `GPU-ACCEPTANCE-2026-09-08.md`.
The navigation/readiness correction is separate. These repairs are limited to
the exact active Arena game/world; no graphics setting, profile, ordinary-raid
behavior, PFT squad state or global exception handling is changed.

## Main-camera grass motion vectors

The preserved Legacy client log reports four color/depth size mismatches:
11:23:46.916, 11:24:54.739, 11:25:40.030 and 11:26:12.551 (local time). Every
one follows an Arena initial kit-body replacement or respawn completion by
approximately 0.02–0.20 seconds. The color surface is `GrassMotionVectorsRT`
(1114 x 548); the depth surface is the new full-size 1920 x 945 camera buffer.
This is the **main** motion-vector target, not the separate optic target.

Verified native source under `Development/_decompiled`:

- `GPUInstancer/GPUInstancerManager.cs:541`: `Update` checks motion-vector
  texture dimensions against its cached camera/SSAA first.
- The same method reacquires a destroyed camera only at line 574, **after**
  that size check, then draws with shared static motion-vector resources.
- `InitializeCameraData` at line 805 binds the current `CameraManager` camera
  and attaches those shared command buffers. `ClearMotionVectorData` at line
  525 combines the motion-vector color targets with the camera's depth target.
- `GPUInstancerCameraData.cs:43` refreshes the cached camera and SSAA together.
- `EFT.CameraControl/CameraManager.cs:1196` destroys the previous FPS camera
  during reset. `Redeploy.Core/View/CameraRebindHelper.cs:60–62` invokes this
  replacement during both initial kit changes and respawns.

The installed `Unity.Postprocessing.Runtime.dll` was also read with ILSpy:
`SSAA.GetInputWidth/Height` falls back to screen dimensions before its `_impl`
is bound; `SSAAImpl.GetInputWidth/Height` reads the current camera/RT size.
`SSAAImpl.OnPostRender` commits the upscale rectangle through `ApplySwitch`.
Together these expose a concrete first-frame ordering hazard: the native
manager can attach the old smaller textures to a new full-size camera before
its next update catches up. The logged errors have no stack trace, so this
does not prove that every possible instance of that Unity message has this
cause or exclude a contribution from another camera mod.

Repair: `ArenaGpuAcceptanceCompat` prefixes the exact native manager `Update`
and performs its existing camera initialization earlier **only** when:

- game/world/session identity matches the active Arena raid;
- it is a main-camera manager whose previously assigned Unity camera has
  actually been destroyed (not a never-assigned or still-live foreign camera);
- the live native player camera controller belongs to the current main player;
- `CameraManager` has a live active replacement camera.

The original `Update` then performs its own resolution check and command-buffer
work. No SSAA/DLSS preference or render texture is manipulated by Arena. In
particular, the repair does **not** clear the shared buffer per manager: that
would discard draw commands appended by earlier grass/tree managers.
Optic managers, foreign cameras, loading/ending games and normal raids retain
the original behavior. Failures in the optional preflight leave native Update
enabled and produce one diagnostic instead of hiding Unity errors.

## PFT inactive MenuScreen warmup

The recorded 11:26:42.390 error has no stack. A matching concrete scheduling
defect was verified against the **installed `pitFireTeam.dll`**, not merely an
assumed reference version. The reference source confirms the same structure:

- `client/Components/SquadControlMenuUi.cs:519` attaches its component directly
  to the `MenuScreen` object.
- `SyncButtonVisibility` at line 630 checks the child button's `activeSelf`,
  then requests settings warmup even when its parent screen is inactive.
- `client/Components/SquadControlMenuUi.Settings.cs:303` starts that coroutine
  on the component without checking `isActiveAndEnabled`.
- PFT patches native `ChangeToReconnectionAvailable` and
  `ChangeScreenInGameStatus`; native `RaidMainMenuScreenController.ShowAction`
  invokes those while preparing the menu. `ScreenManager.OnShowStart` runs
  this preparation before the actual screen's `Show` call.
- Vanilla `MenuScreen.Show` itself uses `Utils.WaitFrames`, which **already**
  checks `isActiveAndEnabled` (`Utils.cs:859`). Blaming or replacing that wait
  is not justified by the inspected source.

Repair: optionally resolve only assembly `pitFireTeam`, exact type
`pitTeam.Components.SquadControlMenuUi`, and the declared private instance
`void QueueRaidSettingsWarmup()` method. Skip that one warmup request only
when its host is inactive during the exact Arena raid. No foreign source,
method body or private field is copied/modified; no PFT DLL is bundled.

Retry remains available: the skipped request does not fill PFT's pending
coroutine field. After native `MenuScreen.ShowGameObject`, PFT's Show postfix
runs `Initialize -> SyncButtonVisibility` again on the active host. In addition,
the explicit settings-button path calls its synchronous settings builder.
The guard neither suppresses that builder nor changes menu visibility. Missing
or changed optional methods are reported without disabling the independent
grass-camera repair or protected team integration.

## Verification and remaining acceptance

Run the standalone, non-deploying checks:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/GpuAcceptanceCompatChecks/Verify.ps1
```

The completed run passed **47 behavioral assertions and eight named source
mutants**, with the final `GPU_ACCEPTANCE_COMPAT_CHECKS_PASSED` marker. The root
agent independently reviewed both repairs against the native lifecycle and
requested the added direct inactive-host negative control; no P0–P2 finding
remained in that review.

The harness compiles the production compatibility file verbatim. Its native
order seam explicitly reproduces the old first-frame color/depth mismatch,
checks correction before the first replacement render, the subsequent native
upscale change, and preservation of both grass/tree command contributions.
It also checks ordinary/foreign/ending raids, wrong bodies, optics, live and
unassigned cameras, optional absence, exact method shapes, and inactive-to-active
warmup retry. It is not a Unity renderer, Harmony detour or actual PFT UI test.
Eight source mutants must fail their named behavioral assertions, including
missing early rebind, normal-raid leakage, optic/foreign-camera takeover, wrong
body ownership, original inactive-host scheduling and permanent warmup suppression.

Root integrated Enable/Shutdown in the plugin and independently reviewed the
native ordering, Arena ownership, optional PFT retry and unchanged shared draw
commands: no unresolved P0-P2. The full staged client compiles against installed
APIs (four existing nullable warnings). Root also reran all 47 checks and eight
controls: `tools/GpuAcceptanceCompatChecks/artifacts/72ac5c19d795404ca57cb5a2a44d4dbe/`.

Delivery evidence is recorded in `HANDOFF.md`. Recheck the actual GPU raid with
upscaling enabled, initial kit swap, several respawns, and Escape/settings menu
opening. Preserve raw logs and inspect the visual outcome. A successful build
or these behavioral checks cannot establish live rendering success.
