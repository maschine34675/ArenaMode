# Native area team-size menu and negotiation — 2026-09-08

Scope: the new `customs-factory-center-teams-v1` preset gains native 2v2, 3v3,
4v4 and 5v5 buttons. Legacy area presets retain fixed 2v2; whole-map TDM retains
its separate configured 2..12 size; protected PFT Tactical remains fixed 5v5.
No ordinary PMC/Scav settings, native database locations or profile protection
paths are changed by the picker.

## Behavior and ownership

- `Settings.AreaTeamSize` / F12 `Team Deathmatch/AreaPlayersPerTeam` is a distinct
  persisted integer, default 2 and constrained to 2..5. Its exact writer rejects
  unsupported values and reports failures without fabricating a selected state.
  The new area ID is included in the existing accepted area setting values.
- `ArenaAreaSelectionState` is immutable. It exposes three Customs area choices,
  the selected count and four size options only for the new team-start preset.
  An invalid count stays visibly unselected; it is never clamped into a final
  request. Default map-area repair still occurs only inside native setup, as
  before; the final request must already contain a valid exact choice.
- `ArenaMenuSelection.TrySelectAreaTeamSize` checks the same settings owner,
  current map, selected exact area, live eligibility and displayed count options
  before writing. Native callbacks additionally retain the screen controller and
  Show generation. Retired area rows, map changes, controller changes, denied
  persistence and Close/Show replacement cannot update the current selection.
- The actual view reuses the native dropdown item prefab. Three compact area
  rows plus four horizontal size buttons end at **Y=882** in the existing
  1080-unit reference layout, below the mode panel ending at 656 and above the
  previously reserved native bottom-control region. Whole-map/PFT views have no
  size buttons. Rows/listeners survive equivalent periodic refreshes; removed
  rows are immediately deactivated and their listeners detached.

The native contract was reread in
`D:\SPT41\Development\_decompiled\EFT.UI.Matchmaker\MatchMakerSelectionLocationScreen.cs`:
the `SelectedLocation` setter writes `_raidSettings.SelectedLocation`, updates
date/time, then exposes Ready/Next. `OnLocationSelected` and direct setters remain
guarded by the existing map preparation prefixes. No new native patch target is
needed for the size row. `EftClientBackendSession.LocalRaidStarted` remains the
existing async endpoint guarded by a synchronous prefix before server start.

## Frozen transport contract

After settings synchronization and refreshed server availability, the existing
final menu validation rejects a missing/invalid new-area size. The local-start
prefix resolves the count once through shared `TryResolveTeamSize` and verifies
`ArenaAreaInitialSpawnPatch.IsReady` before any server arm. The captured integer
is passed both to `PostRaidStart` and `ArenaAreaRuntime.Arm`; later F12 changes
cannot alter the in-flight negotiated match.

`PostRaidStart(map,mode,pitFireTeamTeam,areaId,int? areaTeamSize=null)` validates the
shared area/count contract before setting pending state or sending HTTP. The
optional JSON field `areaTeamSize` is sent only for the new area. A successful
acknowledgement must contain the same exact integer for that area, in addition to
the existing exact area and arm token. Missing/null, wrong counts, floats,
strings and booleans do not count as an acknowledgement. Legacy responses may
omit the size, return null or return integer zero; a nonzero override is rejected.

A response mismatch still retains the pending arm token: it is not evidence that
the server remained idle. Existing token-bound cancellation/reconciliation and
profile protection remain in control. Normal lifecycle requests still use their
existing path and do not acquire an area-size override.

## Verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/AreaTeamMenuChecks/Verify.ps1 -SptRoot D:\SPT41
```

The final run passed:

| Check | Observation |
|---|---|
| Production-linked native selection, local-start and HTTP checks | 397 assertions |
| Production-linked native map hooks/controller callbacks | 71 assertions |
| Production area view/state with external Unity/TMP doubles | 21 assertions |
| Wrong-size ACK deliberately accepted | Expected behavioral failure |
| Frozen size deliberately dropped before runtime Arm | Expected behavioral failure |
| Area view deliberately restricted to only two rows | Expected behavioral failure |
| Retired size-row ownership guard deliberately removed | Expected behavioral failure |
| All original compiler inputs restored and rerun | Passed |

Marker: `ARENA_TEAM_MENU_CHECKS_PASSED`. Evidence is retained in ignored
`tools/AreaTeamMenuChecks/artifacts/`. Fault controls change temporary compiler
inputs only. The script excludes that directory from source discovery and forces
ordinary-input rebuilds after negative controls so a cached mutant cannot become
the final reported result. No mod project, deployment target or game is started.

Cases cover supported 2..5 values, immutable proposals, rejection without silent
final repair, wrong/old owners and maps, failed setting writes, invalid ACK types,
exact cancellation after ACK rejection, missing initial-placement patch,
synchronization-before-freeze, post-arm F12 drift, omission for legacy requests,
three-row layout bounds, button identity reuse and closed/replaced row callbacks.
Existing PFT, full-map, legacy, ordinary-side and native map/time cases remain in
the same passing harnesses.

These checks are not Unity rendering, Harmony detour or real raid evidence.
`Settings.cs` writes were source-reviewed; the root integration owns staged full
client/server compilation and independent adversarial review. UI scaling/font
appearance and actual assembly at every selected size still require the user's
GPU process. Restart client and server after the integrated change is delivered.

Suggested user checks: select Customs → **Customs contour (team starts)**, verify
three visible area choices plus four size buttons without overlap, choose 2v2 and
5v5 in separate starts, and verify both local factions reach their corresponding
starting side with the announced count. Switch back to the legacy contour and to
Factory whole-map: the size row must disappear and their previous size behavior
must remain intact. No autonomous raid was performed.

Independent review by tdm_exit_review (2026-09-08): no openP0–P2. Reviewed production changes, SettingsSync, native SelectedLocation/time/Ready ordering, strict integer acknowledgment and exact cancellation identity, owner/map/area/Show-generation guards and the tests/mutants. Internal geometry title0..36, areas36..124, sizes132..160, summary166..188, hint190..210 at panelY668 gives bottom882. This arithmetic and the standalone view harness are not GPU/font/Canvas scaling proof. Root independently reran397+71+21 assertions and4 named fault controls successfully.
