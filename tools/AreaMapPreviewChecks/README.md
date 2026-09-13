# Native area map preview: offline checks

```powershell
pwsh -NoProfile -File tools/AreaMapPreviewChecks/Verify.ps1
```

This self-contained net10 executable links the **actual production** projection,
map/boundary graphic, preview controller, selection state and shared area catalog.
It embeds the actual reviewed Customs PNG under the production resource name.
There are no mod project references, SPT SDK packages, native assembly loads,
installation writes, runtime mod builds or deployment targets. Build output and
intermediates stay under `tools/AreaMapPreviewChecks/artifacts/<run-id>/`.

The external-only doubles expose Unity's component hierarchy, RectTransform
layout arithmetic, submitted vertices/triangles, button events, deferred object
destruction and Unity-style destroyed-object null behavior. They deliberately
separate queued destruction from end-of-frame destruction, so same-frame
Close/Show cannot appear safe merely because a double deletes objects early.
The CanvasGroup double rejects duplicate components. The PNG loader validates
the header and records the actual bytes/dimensions; **it is not a PNG decoder**.
`tools/AreaMapAssets/verify-assets.mjs` independently validates the packaged image.

The checks exercise:

- Calibration corners and four named positions from the installed
  `Customs_TarkovDev.jsonc`: Big Red, Dorms, Streamer House and Factory Far Corner.
  Expected image-coordinate fractions come from its bounds and rotation, not
  the production projection method.
- Invalid/zero viewports, invalid/outside geometry, finite arithmetic under very
  large inputs, source aspect ratio, detail visibility and full-map overview.
- Actual submitted image and boundary meshes across three viewports and both
  camera choices. A synthetic concave notch distinguishes correct polygon fill
  from rectangular fill; the exact surveyed contours are also checked. Texture
  coordinates and boundary positions must coincide with the full image plane.
- Constant-width blue/halo/other-area line bands, distinct available outlines,
  detail/overview behavior, clipping-parent structure and noninteractive layers.
- Repeated Refresh reuses the same root and texture; resize recomputes layout;
  changing selected area resets the crop without recreating the texture.
- Unsupported map/mode fallback and Close restore existing native alpha and input
  flags exactly without changing native active state. Added CanvasGroups survive
  temporary fallback and are removed on Close; immediate reopen must not reuse
  one awaiting destruction. Destruction of a native group before Close must not
  leave a stale hide latch on the next screen owner.
- Closed listeners are removed and a separately retained old event callback
  cannot modify a reopened screen. Failed image loading warns once and restores
  native information, frees the allocation, and permits retry on a fresh Show.

Five source mutations are separately compiled in scratch directories: mirrored
world X, inverted texture V, reset-to-one native alpha, missing retired-owner
guard and deferred removal of the owned native CanvasGroup. Each must exit with
its specific behavioral assertion marker; a compile failure never counts as
successful fault detection. The clean baseline is rerun, and the script rejects
source/asset drift during the run. `sources-before.json`, `sources-after.json`
and `verification.log` provide the evidence binding.

This is not native rendering acceptance. GPU tests must still establish actual
landmark alignment, mask/shader clipping, font rendering, resolution behavior and
coexistence with native screen layout. Menu integration hook frequency and
normal-raid isolation are covered by the parent's separate native-hook checks.
