# GPU menu retest: Customs area map preview r2

Released 2026-09-13. Test ID: **`area-map-preview-r2`**. Read `AGENTS.md`, the
latest `HANDOFF.md` entry and `GPU-AREA-MAP-PREVIEW-TEST-RESULTS-r1.md` first.
This is a menu-only task. Do not launch raids, patch code, rebuild, instrument
game memory, call Arena routes manually or replace DLLs during the test.

## Why the retest is needed

Commit `588090f` correctly stopped r1 at Factory East selection: the menu offered
the area but the real settings/configuration constraints rejected it. Initial
Center layout and build binding passed; the other preview cases remain untested.
The new client admits East and uses the BepInEx constraint for menu writes.
Geometry, preview graphics and server behavior are unchanged. Offline checks
reproduce the old failure and verify the fix with actual config save/reload;
they are not proof that the native button and preview now work.

## Exact binding: the client and server deliberately have different IDs

- Client source: `2c0223f643e070d392a13a492411985b743c9966`.
- Client ProductVersion: `0.1.0+2c0223f643e070d392a13a492411985b743c9966`.
- Unchanged server source: `d7cde9d115a0f775f8c1b52cc324ee8b0c319e62`.
- Server ProductVersion: `0.1.0+d7cde9d115a0f775f8c1b52cc324ee8b0c319e62`.
- Portable binding: **`GPU-AREA-MAP-PREVIEW-r2-BUILD.json`**. It contains nine
  files: four DLLs and five map/source/license files. Only Arena.Client changed.
  Later documentation commits must not trigger a rebuild or replace these IDs.

| Installed file relative to SPT | SHA256 |
|---|---|
| `BepInEx/plugins/maschine-ArenaMode.Client.dll` | `5D2443A237DB64A539EC2E5B0B2BDA7B0B583FF5925E32878E60EF31EDDB2C62` |
| `SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll` | `A694911344BE7AC9058211EAC856B6944AA1CB16EB3AB1282ECB46403391C7B7` |
| `BepInEx/plugins/maschine-Redeploy.Core.dll` | `FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C` |
| `BepInEx/plugins/maschine-Redeploy.Client.dll` | `7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46` |

Wait for the existing mirror, then run this read-only check from
`C:\SPT41\Development\ArenaMode` before launch and at the end:

```powershell
$ErrorActionPreference = 'Stop'
$testRoot = 'C:\SPT41'
$binding = Get-Content -LiteralPath GPU-AREA-MAP-PREVIEW-r2-BUILD.json -Raw | ConvertFrom-Json
foreach ($file in $binding.Files) {
    $path = Join-Path $testRoot $file.RelativePath
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256) {
        throw "Wrong installed file: $($file.RelativePath)"
    }
    if ($file.ProductVersion -and [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion -ne $file.ProductVersion) {
        throw "Wrong build ID: $($file.RelativePath)"
    }
}
Write-Output "$($binding.TestId): all $($binding.Files.Count) installed files match"
```

Stop on a missing/different file; do not rebuild to fix synchronization. **Restart
the game** after matching hashes. The server may remain the unchanged verified
r1 process; if its loaded identity cannot be established, restart it from
`C:\SPT41\SPT_Runtime`. Record hashes, ProductVersions, process start times,
resolution, actual scaling if available, and any setting changes. A current
on-disk hash alone does not identify a process started before deployment.

## Identifiers and expected behavior

Choose **ARENA → TEAM DM** (`teamdeathmatch`) and **Customs** (`bigmap`), without
selected PitFireTeam followers. PFT may remain installed.

| Visible area | ID | Teams |
|---|---|---|
| Factory Center | `customs-factory-center-v1` | Fixed 2v2 |
| Factory Center (contour) | `customs-factory-center-polygon-v1` | Fixed 2v2 |
| Customs contour (team starts) | `customs-factory-center-teams-v1` | Selectable 2v2–5v5 |
| Factory East (contour) | `customs-factory-east-polygon-v1` | Fixed 2v2 |

Factory uses `factory4_day`/`factory4_night`; THE LAB is `laboratory`; THE
LABYRINTH is `labyrinth`. Those maps can use `full-map`; Customs cannot.
Switching through a different map normally writes `full-map`, so returning to
Customs can legitimately choose the Center rectangle. Reselect East explicitly
after such a transition. AREA/OVERVIEW change only the preview camera.

## Checks, in order

1. **Selection regression first.** Click Factory East once. Verify the selection
   stripe, title `CUSTOMS — Factory East (contour)` and polygon all change;
   hover highlighting alone does not count. Capture immediately and after at
   least 15 seconds. Check for the exact log success marker
   `Arena play area selected in map screen: customs-factory-east-polygon-v1.`
   The saved BepInEx `ExperimentalArea` value should also be that ID while East
   is selected; inspect it read-only, without writing test settings to disk.
   Stop on a reproduced failure. Do not use F12 as a substitute for the native click.
2. **All four area choices.** Click Center contour, team starts and East again;
   verify title, stripe and outline follow each choice. Only team starts exposes
   2v2–5v5; other bounded choices remain fixed 2v2. Check the original Center
   rectangle too. Verify every offered row has an effect.
3. **Detail and overview.** For both measured contours, AREA fits the complete
   outline with context. The interior is bright, the outside dim, the selected
   boundary blue. OVERVIEW shows all Customs with other geometries grey. The
   shared Center/team-start footprint is not drawn twice. Buildings/roads and
   polygon remain registered through the camera change. AREA hides neighboring
   grey boundaries again. Capture each contour in both views.
4. **Refresh and settings.** Toggle cameras repeatedly, then wait 15 seconds.
   Same-area refresh retains the camera; choosing another area resets to AREA.
   Camera controls must not change the chosen area, mode or team size. Test the
   offered team sizes on the team-start variant, then return to fixed-2v2 East.
5. **Navigation and cleanup.** Go to the following setup screen and Back without
   starting a raid. Repeat ten open/back cycles, including selecting both
   contours. No duplicate roots/buttons/text, lost input or preview remaining
   on the following screen. Native map/time/Back/Next/Ready controls remain
   unobscured with their normal availability; a vanilla-disabled Ready is not a
   failure. Record cycles performed, not merely that Back worked once.
6. **Fallback and ordinary menus.** Switch to Factory, THE LAB and THE LABYRINTH:
   no Customs artwork, native information restored. Return to Customs, applying
   the expected full-map fallback above. Then inspect ordinary PMC and Scav
   menus: no Arena preview or invisible native information. Do not start raids.
7. **Display sizes.** Capture the actual resolution and, if safely supported,
   1920x1080 and one smaller supported resolution. Check clipping, aspect,
   captions, attribution, fonts and control overlap. Record actual settings and
   restore them afterward; an unavailable resolution is untested, not passed.

## Evidence, stop conditions and cleanup

Use the documented native computer-use surface. Capture full-screen images with
timestamps and a short clip only if recording is supported. The r1 interface
provided screenshots only: a timed before/after sequence is acceptable evidence
for stable states, but is **not** a frame-by-frame or flicker/video pass. Mark
that temporal acceptance as untested if no appropriate capture is available;
do not abandon otherwise executable menu cases merely for lack of recording.

Read only this run's log interval. Count and preserve preview-unavailable,
duplicate-component, missing-texture/material, MissingReference, NullReference
and menu exceptions. The prior r1 startup warnings are documented in its report;
keep unrelated warnings separate rather than assigning them to this change.
Stop on a reproducible new menu failure, softlock, new exception or hash change,
capture steps/screens/log and leave later checks untested. No patch or rebuild
to continue the same run.

Restore the user's original mode/settings/display values, document any expected
map-fallback value change, and return to the main menu. No profile edits. Write
`GPU-AREA-MAP-PREVIEW-TEST-RESULTS-r2.md` with a pass/fail/untested row per case,
exact mixed client/server binding, linked original evidence and limitations.
Commit only that report/evidence. Keep r1 evidence immutable.

Actual Factory East raid entry, spawn connectivity, respawns, enclosure and
profile acceptance remain separate user-owned tests, not part of this task.
