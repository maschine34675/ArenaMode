# Per-map participant selection

Reviewed 2026-09-21. This is the client preference and native-menu portion of the
participant contract; population, server negotiation and raid freezing are owned
by the shared policy/session integration.

## User-visible contract

The map screen shows a compact minus / total / plus row for whole-map Deathmatch,
Gun Game and independent Team Deathmatch. The heading says **you included**;
TDM also displays the symmetric format (`12 (6v6)`). A recommendation stays below
the row: Equator 4, Bay5 8, Factory 10, Labs 16, Labyrinth 8 (updated 2026-10-19). Recommendations are
defaults, not caps. Solo formats offer 2–24 total; TDM offers even totals 4–24.

The existing bounded-area team buttons and fixed Tactical/PitFireTeam presentation
are retained. No participant override is offered without an admitted selected map,
or for a bounded area or protected team format. On Deathmatch's tallest full-map
layout the area panel ends at Y886; every admitted layout remains at/before Y916.
These are layout-coordinate checks, not native screenshots.

## Persistence and ownership

`Config/ArenaParticipantSettings.cs` binds hidden BepInEx preferences in
`[Map participants]`, keyed by native map ID plus `_solo` or `_team`. Deathmatch and
Gun Game share `_solo`; independent TDM owns `_team`. Factory day/night persist
separately because the native time selector changes the actual selected map ID.
Invalid saved values read as the shared recommendation without a read-time write.
There is no migration of the old global bot-count/team-size settings.

The writer validates the shared policy and refuses active or armed sessions. It
explicitly saves to disk even when `SaveOnConfigSet` is off, restores that host
option afterward, and rolls the in-memory preference back if saving throws. The
view changes its displayed value only after the adapter pushes an acknowledged
state. This preference class does not alter the running raid's frozen total.

`ArenaParticipantSelectionState` contains the rendered map, mode, area and total.
Native participant buttons retire on a map/mode/area context change and retain
their identities during equivalent periodic refreshes. A delayed button delegate
cannot acquire replacement row ownership. The adapter independently validates the
exact Show generation, native controller and Arena flow, selected map on both
screen and RaidSettings, current mode, current saved TDM area, current eligibility
and the rendered total before saving. This also closes the interval before the
periodic updater has observed an F12/map/group change.

Native source was checked in
`../_decompiled/EFT.UI.Matchmaker/MatchMakerSelectionLocationScreen.cs`: the
`SelectedLocation` setter writes RaidSettings (line 334); `OnLocationSelected`
resolves time before that setter (lines 512–529); `UpdateSelectedTime` changes the
selected location directly (lines 533–538). Checking only the tile or a previous
updater snapshot would therefore be insufficient.

## Verification and limits

Run `pwsh -NoProfile -File tools/ParticipantMenuChecks/Verify.ps1`.

- `ParticipantMenuChecks`: actual production preference/state/view sources with
  real BepInEx `ConfigFile`; fresh defaults, independently saved maps and mode
  families, reload, bounds, active/armed rejection, read-only invalid-value
  handling, actual filesystem save refusal and rollback. Controlled Unity/TMP
  surfaces exercise real production buttons, all supported map/mode/count
  combinations, layout bounds, accepted/refused writes, stale delegates,
  replacement panels, fixed modes and min/max forced-event rejection.
- `NativeAreaHookChecks`: production native-screen patch and menu selection,
  with external screen/render/config seams; participant callbacks are tested
  against map, mode, saved area, group, map enablement, count, controller,
  Show generation, armed/active session and close changes. Existing map admission
  and area ownership checks remain included.
- `AreaTeamMenuChecks`: unchanged bounded-team behavior through the production
  view; existing 2v2–5v5 size buttons, refusal, stale delegates and layout.
- Six isolated source faults must fail their intended behavioral assertion:
  missing disk save, missing armed guard, missing active guard, stale row
  membership, stale Show generation and changed saved area. Ordinary compiler
  inputs are restored and all three checks rerun before the success marker.

Four self-review passes covered implementation, format/persistence edge cases,
stale ownership and error paths, then layout/cache/documentation polish. The
verification driver does not build or deploy either mod DLL. Real Unity prefab
rendering, fonts/scaling, pointer hit testing, Harmony detours, and actual raids
still require the restarted GPU client; these controlled doubles prove none of
those native outcomes.
