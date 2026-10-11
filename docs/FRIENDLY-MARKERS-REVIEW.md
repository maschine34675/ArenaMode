# Friendly markers: native output projection

Date: 2026-09-12. Scope: correct teammate diamond displacement with DLSS. No change
to team admission, profile/match state, LOS, marker styling or render callbacks.

## Native diagnosis

The HUD is a `ScreenSpaceOverlay` canvas (`ArenaHud.Build`) with a 1920x1080
reference scaler. Its screen-to-local conversion expects final display pixels.
Previously the marker passed `Camera.WorldToScreenPoint` directly into that canvas.
The game camera can instead project into a smaller native rendering viewport.

Installed `Unity.Postprocessing.Runtime.dll`, SHA256
`B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548`, was inspected
read-only with `ilspycmd -t SSAA` and `-t SSAAImpl`. No game source was copied into
the repository. Relevant installed methods:

- `SSAAImpl.ApplySwitch`: first resets Camera.rect to full; UPSCALE changes it to
  `(0, 0, ratio, ratio)`, while DOWNSCALE allocates a larger input render texture.
  The private `CurrentState` enum records NOSCALE, UPSCALE or DOWNSCALE.
- `SSAAImpl.GetInputWidth/Height` read the current RT or camera pixel dimensions.
  `GetOutputWidth/Height` return Screen.width/height. `RenderImage` maps the input
  across the full output viewport, including the fallback when DLSS is unavailable.
- `SSAA.RemapToHiRes` explicitly distinguishes input and output pixels. Its
  arithmetic is evidence of the mismatch, not code copied into this mod.
- `SSAA.Switch` updates the wrapper's ratio before `SSAAImpl.OnPostRender` applies
  the requested switch. The wrapper ratio is therefore unsuitable for deciding
  the currently rendered output mapping during a settings transition.
- `../_decompiled/EFT.CameraControl/CameraManager.cs`, `SetAntiAliasing`, selects
  DLSS's optimal resampling factor and calls SSAA.Switch. `CameraManager.Camera`
  remains the current gameplay camera. The reference EnemyMarkers mod uses world
  text geometry; no code or assets from it are adopted.

Multiplying normalized viewport coordinates by Camera.rect alone would still
shrink markers under DLSS: that rect is the input crop which native SSAA expands.

## Correction and ownership

The existing normalized `WorldToViewportPoint` result is mapped to display pixels.
For the exact current camera's active SSAAImpl in applied UPSCALE/DOWNSCALE, the
output rectangle is full screen. NOSCALE or an absent/disabled component preserves
the actual camera rect, including offset and partial clipping. An unknown active
state or invalid dimensions produces no marker instead of a guessed location.
The private state field is resolved once; the component and state are read from
the exact current camera on each canvas pass, never a stale manager scaler cache.

The mapping reads current Screen dimensions and viewport each pass. It does not
cache a quality factor, use input RT dimensions, change the camera, change graphics
settings, disable temporal jitter, or require a new hook. Canvas conversion still
applies UI scale before adding the unchanged 30 HUD pixel head offset.

All existing scope/body/phase guards, physical-head LOS, bounded pools/scans/rays,
and `Canvas.preWillRenderCanvases` ownership remain intact. Normal raids never
enter this projection path. Hidden/invalid frames clear old geometry; disposal
removes the callback. The parent owns the additional non-copying client reference
to the game's Unity.Postprocessing.Runtime assembly.

## Verification and limits

`tools/FriendlyMarkerChecks/Verify-Contracts.ps1` reads the installed native field,
enum, viewport, output-size and prior UI/body/LOS contracts. It then compiles the
actual production marker lifecycle/policy with narrow Unity/EFT I/O collaborators,
including the actual Tactical marker partial. The baseline passes **148 assertions**;
three compiled projection defects must fail their specific assertions. The full
`Verify-Regression.ps1` preserves all prior controls and rejects **14 compiled
defects** before rerunning the baseline. Compilation failures are never accepted
as negative-control success. Source copies are temporary; no mod project is built.

Projection coverage includes reduced DLSS rects and quality changes, supersampling
with the RT attached/detached, pending enable/disable versus applied native state,
disabled/unknown scaler state, resolution changes, viewport offsets/clipping,
canvas scale, raised head anchor, camera replacement, and invalid output clearing.
The input-pixel camera collaborator positively reproduces the reported half-size
displacement. Production math is linked, not reimplemented in the test harness.

Four-pass self-review found and avoided the naive Camera.rect scaling error and
the wrapper requested-ratio race. Independent review and parent re-verification
of the installed contracts, baseline and projection defects passed before integration.

This is source/native-contract evidence, not rendered GPU acceptance. A later GPU
test must compare DLSS off/on and quality/resolution changes with a visible moving
teammate in TDM and Tactical spectator view, check off-center/crouched/prone heads,
walls/death/rebind, and a following normal raid. Subpixel temporal jitter, native
frame timing, every collision mesh and external camera modifications are not
simulated. Disabled-component handling is a defensive normal-viewport fallback:
native SSAA.Switch enables the component, and no supported native disabling path
was found. A third-party mod that disables SSAAImpl but keeps SSAAPropagator's
direct RenderImage calls alive is outside this verified state contract; disabled
alone does not prove that external propagation stopped.

Independent review by the corpse-cleanup worker confirmed the applied-state
mapping, requested-ratio timing and scope/body/LOS/lifecycle preservation without
an established production defect. It identified the disabled propagation boundary
above, which is now explicit. The integrated staged build is recorded in the handoff.

Current r3 tests, installed DLLs and deployment files were untouched;
only the isolated helper was compiled during implementation and review.
