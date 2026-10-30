# Arena character preview through native raid setup

Reviewed source: 2026-09-12, `feature/deathmatch-menu-preview`. This document
describes the preview change; the root HANDOFF records the final delivered
commit and DLL hashes. No gameplay video or GPU result was produced here.

User follow-up after GPU report `05e88d0` (2026-09-12): the visible character
jitter is gone. This is user-observed acceptance, separate from the report's
static side-selection screenshots. The report stopped at a cosmetic text error
before downstream screens or raids and contains no video/frame analysis. Keep
the current camera origin and posing behavior; another jitter video is only
useful if the symptom recurs. The following r2 findings supersede the earlier
downstream acceptance status.

## GPU r2 findings and lifecycle correction

Report `c5c8945` observed the same outfit, helmet and weapon on map selection,
offline settings and confirmation. The four-mode menu, finite-round gate,
custom 37-kill/75-second labels and tested panel placement passed within that
report's scope. All 18 original screenshot hashes were independently checked.
No raid started; loading, repeated navigation, match rules and raidwise profile
protection remain untested. Static images do not establish motion continuity.

The saved client log contains four isolated Unity errors at map/offline exits:
`Cannot set the parent ... while activating or deactivating the parent`.
The host's OnDisable previously called Suspend inline, which reparents the
Arena image while its ancestor is still deactivating. Unity can reject this
without throwing, so catching exceptions cannot repair the hierarchy operation.

The correction detaches before the known native screen's Close reaches
UI.Dispose/HideGameObject. The prefix on `EFT.UI.Screens/UIScreen.Close` is
restricted to map, offline, confirmation and loading screens and additionally
requires the exact current host or its containing screen. This covers the
confirmation's nested player widget and loading's inherited Close. An inactive
screen cannot use this early path during a reentrant deactivation callback.

Fallback OnDisable/OnDestroy callbacks only queue a generation-bound request.
The independent world guard drains it on Update; a new attachment drains it
before even the same-host shortcut, restoring the old native image flags before
capturing the next snapshot. An already-authorized new native model request is
preserved during this attachment drain; ordinary guard/Close drains still revoke
the old widget/account owner. This distinction was added after adversarial review
and a failing executable fast-reopen check. No owner is restored after Reset.

Destroyed Unity view/world objects now invalidate availability, so unexpected
host destruction cannot suppress native rendering with a dead Arena image.
Pending PlayerBody.Init release remains deferred until its existing finally
settles. There are no changes to geometry, camera position, animator behavior,
profile data, selection rules, gameplay, server or audio.

The new `tools/PreviewHostLifecycleChecks/Verify.ps1`, included in the existing
preview verifier, executes the production flow and lifetime state plus verbatim
availability/suspend/guard setup members against controlled native boundaries.
It counts forbidden hierarchy attempts even when production Safe catches them.
Coverage includes deferred and early cleanup, reentrant Close, repeated Accept
and Loading requests, native teammate switching, drag cleanup, stale generations,
selection loss and destruction during a pending load. Its 121 assertions and five
compiled negative controls passed. The controls reintroduce inline mutation,
missing pre-attach cleanup, request-owner loss, reentrant Close and missing
Unity-object availability checks. This is a lifecycle simulation and API
compilation, not execution of Unity rendering or native AI. Independent final
review found no further established defect. Native acceptance must resume using
`GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r3.md` and its exact hashes.

## Native owners verified in the local SPT 4.1 decompilation

Paths below are relative to `../_decompiled/`.

- `EFT.UI.Screens/ScreenManager.cs:293`: `PrepareShow` awaits
  `CloseCurrentScreen` before initializing the new screen. `TryCloseScreen`
  calls the outgoing `Screen.Close()` before `ShowPreviousScreen`. The side
  prefix can transfer its renderer before the old layout disposes it.
- `EFT.UI.Matchmaker/MatchMakerSideSelectionScreen.cs:297`: the native PMC and
  Scav models each use `PlayerModelView.Show`; Arena's third model is separately
  owned. `ArenaSideSelectionLayout.DetachPreview` clears only its Arena handle.
- `EFT.UI.Matchmaker/MatchMakerSelectionLocationScreen.cs:391` and
  `MatchmakerOfflineRaidScreen.cs:73`: these screens have no native character
  preview. The Arena image uses a display-only 180x280 lower-right slot, with
  a 24-pixel right and 100-pixel bottom inset. Map/rules/modal collision and
  ultrawide layout still require the GPU check below.
- `EFT.UI.Matchmaker/MatchMakerAcceptScreen.cs:398`: the native local player
  widget is `_playerModelView`, a `MatchMakerPlayerPreview`. Its
  `Show` binds `player.VisualChanged` to `UpdateVisual`, which normally asks
  its inner `PlayerModelView` to load the profile's visual. The patch records
  this exact widget before native Show and also requires `_isMainCharacter`.
  Group-member widgets never qualify.
- `EFT.UI.Matchmaker/MatchmakerTimeHasCome.cs:268`: `ShowPlayerModel` normally
  waits for raid pools and loads its own `PlayerModelView`. The initial null
  visual request uses the already-retained Arena figure. `HandleOnPlayerClick`
  identifies the local player by account id; another member's explicit visual
  goes through the native method. Reconnection is excluded. The independent
  Arena drag handler uses native `EFT.Utilities/XCoordRotation.cs` and the
  loading widget's configured rotation speed; it is removed on detach.
- `EFT.UI/PlayerModelLoader.cs:234`: `CreatePlayerBody` creates the menu poser,
  awaits `PlayerBody.Init`, then parents it. `CreateWeapon` at line 178 sets up
  the native weapon/hand pose. `Clear` at line 289 returns the weapon prefab
  and disposes/destroys the body. A body must not be cleared under its awaited
  initialization.
- `EFT.UI/CameraImage.cs:96`: `InitCamera` cleans the previous attachment.
  `CleanUp` at line 143 disables that camera, releases the render texture and
  unsubscribes the render callbacks. Each attached image owns one such binding.

## Ownership and failure behavior

The side selection still builds detached equipment using fresh item ids. Only
the PMC faction and a copied customization map are read; no profile inventory,
health, equipment owner, raid kit, score or selection value is replaced.

Leaving the side screen with Arena selected transfers the existing renderer.
The body, camera, owned filter material and retained bundles persist once for
that setup. Moving between map, offline configuration, confirmation and loading
reparents only its image. There is no downstream `CreatePlayerBody` or second
Arena load. The isolated world is persistent until this explicit owner releases
it, including the gap between screen Close and Show.

The confirmation/loading native CameraImage is temporarily disabled; its raw
image and placeholder flags are saved and restored. The overlay itself never
blocks pointer events. A peer preview can still load its native body when the
user explicitly selects a teammate. Its native model is not rewritten into an
Arena model. Returning to the local selection shows the same owned Arena body;
the teammate body remains under the ordinary loading widget's teardown.

Every attachment has a flow settings identity, a host identity and a generation.
Late callbacks from an older use of the same screen cannot detach a newer
surface. The persistent guard is additionally bound to its renderer identity,
so deferred destruction of an older renderer cannot reset a new one.

Returning to the side screen releases the previous flow renderer and allows
the third-choice layout to construct its new owned preview. `ArenaMenuSelection.Clear`
also releases the flow, covering rejected choices, PMC/Scav selection and setup
exit. A current owned-world guard releases at gameplay activation, selection
loss or world destruction. Other native screens with no character presentation
(key access, insurance, final countdown) do not receive a new overlay.

Construction/load failure disables the owned image/world immediately. The pure
lifetime state defers loader cleanup until the single in-flight initialization
settles, then claims release once. An unavailable downstream host fails open to
the native visual method. A later load failure restores the current active
native model request only after removing Arena ownership. Cleanup of material,
view, world, retained token and loader is individually guarded so one native
cleanup exception does not skip the remaining owners.

## Jitter: source-backed candidate, no GPU conclusion

`MenuPlayerPoser.cs:392` performs native world-space IK in LateUpdate: elbow
positions are blended at line 429, grip positions at line 447, then limb solvers
update at line 461. The old Arena-only origin was `(10000,-10000,10000)`.
The spacing of representable single-precision numbers at 10000 is
0.0009765625 metres. At the new isolated origin `(128,-128,128)` the positive
axis step is 0.0000152587890625 metres, a measured 64-fold improvement. This
removes an avoidable quantization source from the hand/grip calculations.

The origin remains outside the native matchmaker cameras' short preview volume;
the owned camera retains a far plane of 25 and the WeaponPreview-only mask.
Scene/frustum separation is also checked in the GPU run: this is not proof that
every possible foreign mod's camera ignores that space. The existing ready log
records actual camera/body positions and visible renderer bounds.

No animator speed, update mode, culling mode, blend weights or IK implementation
was changed. Native idle breathing/weapon handling remains. Camera/world/body
root positions are not rewritten every frame; only deliberate loading-screen
drag rotates the owned model. Texture resizes are handled by CameraImage's
normal size-change path. The code evidence alone does not decide whether the
reported artifact was IK quantization, ordinary animation, camera movement,
temporal aliasing, compression or a combination.

## Offline evidence and review passes

`pwsh -NoProfile -File tools/ArenaCharacterPreviewChecks/Verify.ps1` runs the
production lifetime/host state (51 assertions), compiles the actual renderer,
flow and Harmony hooks against installed game/Unity assemblies, and checks five
compiled production faults. The faults are release during body Init, repeated
load admission, double release, stale-host callbacks and other-flow admission.
Each must reach a failing assertion; compilation failure is not accepted as a
rejected defect. Global-model hooks, profile writes and animator changes also
have positive source controls. The hook project stubs only the mod integration
boundary and has no deployment target. It does not execute Unity.

Four leaf passes: complete implementation; review against native loaders,
screen transitions and member previews; defect hunt of deferred disposal,
generation checks, saved native flags and thrown partial transfer; polish of
placement, drag cleanup, frame provenance and test-output isolation. The parent
must independently review and re-run the leaf gate before integration acceptance.

## Optional GPU capture if jitter recurs

The user's jitter acceptance remains valid; this diagnostic protocol is optional
and must not delay the outstanding r3 navigation/loading and bounded raid tests.

1. Record the actual delivered client hash, source commit, game restart time,
   resolution, display scaling, VSync/frame cap and capture frame rate. The
   capture must come from that restarted process. Preserve the original video
   and its hash; do not reuse a previously accepted gameplay result for this DLL.
2. With the cursor stationary away from the figure, capture one 8–10 second
   side-screen clip: allow two seconds to settle, then keep both native PMC and
   Arena models visible for at least six seconds. Repeat with the same settings
   on the pre-change build only if an unchanged real baseline recording already
   exists or the test owner intentionally runs that build. Otherwise label the
   result post-change-only; do not invent a before clip. USEC and BEAR each need
   one short stationary sample if both factions are being accepted.
3. Follow Arena through map, offline settings if visited, confirmation and
   loading. Keep a short stationary sample on confirmation/loading. Verify the
   same Arena outfit, no PMC kit flash, no duplicate body, no white image,
   complete figure and no collision with location details or the Deathmatch
   rules panel. Use one deliberate drag only after the stationary segment.
4. Check Arena -> map -> back -> PMC, then Scav and a normal setup; repeat one
   fast forward/back while the spinner is visible. Cancel loading back to menu
   and reopen. If an existing protected team is available, click one teammate
   then the local player in loading and confirm both retain the correct models.
   Do not create an unrelated squad or raid solely to satisfy this optional case.
5. Extract six consecutive frames from an actual video, for example:

   ```powershell
   pwsh -NoProfile -File tools/ArenaCharacterPreviewChecks/ReviewFrames.ps1 -VideoPath 'D:\captures\arena-preview-after.mp4' -StartSeconds 2.0 -FrameCount 6
   ```

   The helper requires ffmpeg/ffprobe, writes original-size decoded PNGs and a
   `frames.json` containing source SHA256, exact source frame indices and
   presentation timestamps read from the recording. It rejects missing frames
   and an existing output directory. It does not interpolate, synthesize or
   infer timestamps from an assumed frame rate. Keep all six frames and compare
   them at the same scale alongside their timestamps.
6. In adjacent frames, compare a fixed UI edge, the feet/body root, helmet edge,
   wrist/grip and weapon sight. Coherent slow bone movement with stable UI/root
   is animation. A whole-model displacement relative to the UI suggests root,
   rotation or projection movement. Rapid isolated wrist/weapon discontinuities
   suggest posing/IK; edge-only changes with stable geometry suggest rendering
   or video compression. These are diagnostic interpretations, not automatic
   verdicts. Report the observed frame numbers/times and affected feature.
7. Preserve client log lines covering each ready message, cancellations and
   any `Arena preview`/native loader exception. Check that no owned camera,
   material or mannequin remains after returning to ordinary UI/starting the
   raid. A clean build or a script's source-state tests do not certify this.

Navigation/loading acceptance requires native observations from the delivered
build. If jitter recurs, keep the original clip
and precise frame evidence; do not tune animator speeds from a description.
