# GPU menu test: Customs area map preview r1

Released for menu testing on 2026-09-13 after the user authorized deployment.
The reviewed client, server and five map files are installed and hash-verified
under `D:\SPT41`. Wait for the existing mirror and verify the files under
`C:\SPT41` before starting. This task does not authorize autonomous raid tests.

## Fixed test and build binding

- Test ID: `area-map-preview-r1`.
- Source commit: `d7cde9d115a0f775f8c1b52cc324ee8b0c319e62`.
- Arena client AND server ProductVersion:
  `0.1.0+d7cde9d115a0f775f8c1b52cc324ee8b0c319e62`.
- Exact portable binding: `GPU-AREA-MAP-PREVIEW-r1-BUILD.json`. It records the
  installed hashes for both Arena DLLs, unchanged Redeploy.Core/Client and all
  five map/source/license files. Their directory paths are relative to SPT.
- The binary was built before this documentation-only deployment commit. Later
  documentation commits are expected; do not rebuild to match the checkout.
- This is a new test binding. The older `respawn-stage0-r4` instruction remains
  bound to its old build and is not evidence for this candidate.

| Installed file relative to SPT | SHA256 |
|---|---|
| `BepInEx/plugins/maschine-ArenaMode.Client.dll` | `AB53689653C666F8B842DA30C0F4FF9C7C86B801950A8418B47B2FAA37323A17` |
| `SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll` | `A694911344BE7AC9058211EAC856B6944AA1CB16EB3AB1282ECB46403391C7B7` |
| `BepInEx/plugins/maschine-Redeploy.Core.dll` | `FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C` |
| `BepInEx/plugins/maschine-Redeploy.Client.dll` | `7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46` |

Before launch and after the menu checks, run this read-only check from
`C:\SPT41\Development\ArenaMode` and retain the output:

```powershell
$ErrorActionPreference = 'Stop'
$testRoot = 'C:\SPT41'
$binding = Get-Content -LiteralPath GPU-AREA-MAP-PREVIEW-r1-BUILD.json -Raw | ConvertFrom-Json
foreach ($file in $binding.Files) {
    $path = Join-Path $testRoot $file.RelativePath
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256) {
        throw "Wrong installed file: $($file.RelativePath)"
    }
    if ($file.ProductVersion -and [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion -ne $file.ProductVersion) {
        throw "Wrong build ID: $($file.RelativePath)"
    }
}
Write-Output "$($binding.TestId): all $($binding.Files.Count) installed files match $($binding.SourceCommit)"
```

If a file is missing or differs, do not start or rebuild: report the mismatch
and let the deployment/mirror settle. After matching hashes, **start both game
and SPT server fresh** so the running processes load this pair. Record the exact
source commit, binding file, hashes, start times, resolution and UI scaling in
the report. Never infer the running build from a checkout or a copied file alone.

Read `AGENTS.md`, `HANDOFF.md` and `AREA-MAP-PREVIEW-REVIEW.md` first. Follow the
local computer-use workflow and preserve other sessions' files and reports.
Do not change or remove another mod to make this test pass.

## Map, mode and area identifiers

Use visible mode **TEAM DM**, ID `teamdeathmatch`, and map **Customs**, ID
`bigmap`. Do not select PitFireTeam followers for this independent TDM test;
an installed PFT mod may remain installed.

| Visible area entry | Exact area ID | Team size |
|---|---|---|
| Factory Center | `customs-factory-center-v1` | Fixed 2v2 |
| Factory Center (contour) | `customs-factory-center-polygon-v1` | Fixed 2v2 |
| Customs contour (team starts) | `customs-factory-center-teams-v1` | Selectable 2v2–5v5 |
| Factory East (contour) | `customs-factory-east-polygon-v1` | Fixed 2v2 |

Fallback maps: Factory uses `factory4_day` or `factory4_night` through its native
time choice; THE LAB is `laboratory`; THE LABYRINTH is `labyrinth`. These maps
may use `full-map` (Whole map); Customs cannot. AREA and OVERVIEW change only the
preview camera and are not area/mode identifiers.

## Menu checks

1. Open ARENA, select TEAM DM and Customs, without PFT followers. The left area panel offers
   four entries, including **Factory East (contour)**. Record one full-screen
   image with the new area selected. All area rows, native map controls,
   time selection and Back/Next/Ready controls must remain unobscured, with
   their native availability/interactivity unchanged. A button legitimately
   disabled or absent by vanilla is not itself a preview failure.
2. Check AREA for both measured contours. The selected polygon is blue, its
   interior map is brighter than its surroundings, and the full contour fits
   the preview. Buildings, roads and boundaries remain aligned when switching
   to OVERVIEW. Use visible map landmarks; do not treat an attractive image as
   proof of geometric alignment. Record both views of Factory East and the
   center contour.
3. OVERVIEW shows the whole Customs ground floor. Other available geometries
   are grey. The center contour/team-start variants share the same footprint
   and must not create two offset or thick duplicate boundaries. The legacy
   rectangle remains a distinct available outline. Return to AREA: neighboring
   grey contours disappear and the selected contour is framed again.
4. Toggle AREA/OVERVIEW repeatedly, then wait at least ten seconds. The camera
   choice must persist during periodic menu refresh. Changing the selected
   area starts in AREA view. Camera buttons must not change area/team size/mode.
   Factory East stays fixed **2v2**; the center team-start variant retains its
   existing 2v2–5v5 selection. Do not launch a raid to check this menu behavior.
5. Move forward to the next screen and return without starting the raid. Repeat
   ten open/back cycles, including switching between the two contours. The
   preview must appear only once, with no flicker or growing duplicated text,
   controls or artwork. The selected settings must stay consistent with the
   existing menu behavior. In particular, camera toggles never select an area.
6. Switch to Factory, Labs and Labyrinth: the Customs artwork is absent and
   native map information is restored. Return to Customs; the preview works
   again. Switching to those other maps normally selects `full-map`; returning
   to Customs can therefore choose Factory Center (the rectangle) as its valid
   default. This existing fallback is expected, not loss of the preview state.
   Reselect Factory East explicitly for its checks. Return to character selection and inspect ordinary PMC and Scav:
   neither must display the Arena preview or leave native information invisible.
7. Inspect at the actual test resolution and, if supported by the UI without
   restarting, at 1920x1080 and a smaller supported resolution. Record each
   exact resolution/scaling. Look for cut-off attribution/captions, distorted
   image aspect, unclipped map content, overlapping buttons and unreadable fonts.
   Restore the user's original display setting afterward. Mark unavailable
   resolutions as untested, not passed.

## Evidence and failure handling

Save full-screen screenshots and a short clip of AREA/OVERVIEW plus a back/return
cycle. Review the clip for flicker and alignment; screenshots alone cannot prove
temporal stability. If recording is unavailable, document that limitation.
Read only the log interval belonging to this build/test and report counts for:

- `Arena area map preview unavailable`;
- duplicate CanvasGroup or other component errors;
- missing texture, shader/material, MissingReference or NullReference failures;
- lost native controls, invisible map information or menu exceptions.

Preserve unrelated known log errors separately with timestamps and stacks. Stop
on a reproducible new menu failure and capture the screen, exact steps and log.
Do not patch, rebuild or deploy to continue the same acceptance run. Write a
dated report with a pass/fail/untested row for each check, linked evidence, exact
build binding and limitations. Commit the report/evidence according to the repo
workflow; do not include unrelated modifications. No raid/profile claim follows
from this menu test.

## Separate user-owned raid acceptance

The user later tests Customs / Factory East / independent TDM 2v2: initial and
respawn positions inside the eleven-point contour, both teams' ability to reach
combat, no stuck spawns or boundary escape, unlimited height as intended, match
end/leave and unchanged protected profile. Capture candidate/path failures from
the log rather than weakening readiness checks. The menu's polygon image and
the database candidate counts cannot certify any of these runtime outcomes.
