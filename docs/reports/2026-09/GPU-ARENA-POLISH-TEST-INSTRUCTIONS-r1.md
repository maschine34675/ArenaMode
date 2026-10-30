# GPU checks: area catalogue, Tactical startup and killfeed offsets r1

Test ID: **`arena-polish-r1`**. Read `AGENTS.md` and the newest `HANDOFF.md` entry.
The Codex instance on the GPU machine owns **menu checks only**. Actual raids
in the separate checklist below remain user-run. Do not rebuild, patch, inject
game memory, call Arena routes manually or alter profiles to obtain a pass.
Historical area-map r1/r2 reports and evidence remain unchanged.

## Installation and process binding

Use `GPU-ARENA-POLISH-r1-BUILD.json` as the complete portable nine-file binding.
Client AND server source: `699df574a062705c8be3242953aba0599b63667f`.
Both Arena ProductVersions: `0.1.0+699df574a062705c8be3242953aba0599b63667f`.
Redeploy and the five map/source/license assets are unchanged.
Later documentation commits do not change these binary IDs.

Wait for the existing D: to C: mirror. Run this read-only check from
`C:\SPT41\Development\ArenaMode` before starting and after testing:

```powershell
$ErrorActionPreference = 'Stop'
$testRoot = 'C:\SPT41'
$binding = Get-Content -LiteralPath GPU-ARENA-POLISH-r1-BUILD.json -Raw | ConvertFrom-Json
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

Stop on mismatch; do not rebuild to compensate. **Restart both game and SPT
server after the hashes match.** This change updates their shared area catalogue;
an old process is not acceptable even if its on-disk DLL has been replaced.
Record process start times and startup logs, resolution/scaling, original
settings and pre/post file checks. Exact build identity comes from the DLL
ProductVersions and hashes above; the client startup log only prints `0.1.0`.
Snapshot the relevant configuration before
using native controls, and restore the user's values afterward.

## Native menu checks for the GPU Codex instance

Choose ARENA, TEAM DM (`teamdeathmatch`), Customs (`bigmap`), without selected
PFT followers. PFT may stay installed. Never press the final raid-start button.

| Visible Customs area | Stable ID | Expected team-size control |
|---|---|---|
| Factory Center (contour) | `customs-factory-center-polygon-v1` | Fixed 2v2 |
| Customs contour (team starts) | `customs-factory-center-teams-v1` | 2v2, 3v3, 4v4, 5v5 |
| Factory Side (team starts) | `customs-factory-east-polygon-v1` | 2v2, 3v3, 4v4, 5v5 |

The old **Factory Center** rectangle (`customs-factory-center-v1`) and
**Factory South** (`factory-south-v1`) must not be offered. The measured Center
contour and its team-start variant intentionally remain. Factory Side replaces
the former Factory East label without changing its eleven-point contour.

1. **Catalogue and selection.** Exactly the three Customs choices above appear.
   From Center, select Factory Side with one click; verify stripe, title and
   outline immediately and after 15 seconds. Confirm the saved area ID read-only
   and the log `Arena play area selected in map screen: customs-factory-east-polygon-v1.`
   Click all three rows; hover alone is not selection.
2. **Team sizes.** On Factory Side choose every offered size from 2v2 to 5v5.
   Verify the native selected value, leave to the next setup screen and return,
   then wait 15 seconds. Repeat on Center team starts. Center contour must still
   show fixed 2v2. Do not manually arm a raid as a substitute for the menu.
3. **Preview and navigation.** For both measured contours test AREA and OVERVIEW,
   registration with roads/buildings and complete contour framing. The retired
   rectangle must not remain as an extra grey outline. Shared Center geometry
   should not be drawn twice. Camera-only changes must not change area/team
   settings. Complete ten Next/Back cycles; no duplicate roots, obscured vanilla
   buttons, stuck input or artwork left on unrelated screens.
4. **Factory and normal menus.** Factory day/night offer full-map with no South
   area. THE LAB and THE LABYRINTH retain native map information. Returning to
   Customs after full-map may select the first Center contour; explicitly
   reselect Side. Ordinary PMC/Scav must have no Arena map preview/controls.
5. **HUD configuration visibility.** In F12/HUD locate `KillfeedOffsetX` and
   `KillfeedOffsetY`, both zero by default. Positive X means right, positive Y
   down, in scaled 1920x1080 units. Do not claim visual in-raid movement from
   finding these controls. Preserve any user-customized values.
6. **Display and evidence.** Check 1920x1080 and one smaller supported window or
   resolution, recording actual sizes. Capture each relevant state. Unsupported
   recording means framewise flicker remains untested; ordinary screenshots are
   enough for static layout checks. Restore display settings afterward.

Do not deliberately write retired IDs into the user's config: migration/rejection
is already tested offline with actual BepInEx persistence and the server service.
If a retired value happens to be present initially, preserve evidence of its
natural fallback: acceptable-value clamping gives full-map; native Customs
proposes a valid contour before starting, while Factory keeps full-map.

## Separate user-run raid checklist

These cases require real gameplay and are **not automatically authorized for the
GPU Codex instance**. Record each as untested until the user runs it.

- **Factory Side:** independent TDM 2v2, then 5v5; check opposing starts, complete
  rosters, reachable respawns after deaths on both factions, visible boundary
  near the edge and no escape/stuck bots. The offline survey verifies native
  coordinates and spacing, not scene connectivity, cover or balance.
- **Tactical opening:** solo 1v5, no first-round purchase: explicitly labelled
  cheapest valid free starter, no random expensive kit. Then buy another kit:
  exactly one charge and correct equipment. Browsing alone must not charge.
  Survivors keep/resupply their gear; after death the existing affordable
  automatic purchase remains. Test a later round as well.
- **Tactical readiness/audio:** during initial opponent loading the assembly
  notice stays visible and Round Start is silent. Cue once when the expected
  physical opponents are Active (or after the documented partial-ready timeout);
  combat time starts there. An early kill while later bots load must not allow
  a second First Blood or reset its kill chain when the start cue arrives.
  A zero-opponent retry must not charge again or play the cue.
- **Tactical Escape:** during countdown, open the native menu with Esc both
  while alive and during a held post-death buy phase. Buy overlay must close;
  native settings/resume/leave must work. Countdown waits for the menu. Resume
  must not fire a held click, lose hands or bypass death protection. Test leaving
  through the normal protected flow and verify ordinary profile restoration.
- **PFT regression:** protected Tactical 5v5 retains entry gear, all followers
  and opponents are ready before its existing cue, next round and protected
  exit still work. No free-starter assignment or kit replacement on this path.
- **Killfeed offsets:** with visible entries change X by +100 and Y by +50.
  The complete feed moves right/down together without resetting row lifetimes
  or losing killer/victim/distance/team colors. Check negative values and return
  to original values. Score-panel offsets must remain independent. Check after
  respawn/next round and ensure no Arena feed in normal menus/raids.

## Reporting and stop conditions

Read only the new client/server log interval. Separate existing startup warnings
from new menu exceptions, duplicate components, Tactical startup failures,
readiness retries and native input errors. On a reproducible new failure or hash
change, capture steps/screens/log and stop; mark later cases untested rather
than patching and continuing under the same build binding.

Write `GPU-ARENA-POLISH-TEST-RESULTS-r1.md`: pass/fail/untested for each case,
exact nine-file pre/post hashes, four DLL ProductVersions, fresh-process proof,
actual display sizes, linked original evidence and restored settings. Return to
the main menu and commit only the report/evidence on current `master`; the old
feature branches were merged/deleted, so do not recreate or force-push them.
No result file or screenshot may be presented as a raid pass without a real raid.
