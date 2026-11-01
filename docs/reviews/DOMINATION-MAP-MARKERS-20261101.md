# Domination objectives on DynamicMaps

Date: 2026-11-01. User choice: only during Domination, with live possession.

## Behavior and ownership

Optional DynamicMaps shows the current round's compact objective IDs on its map
and shared minimap view: neutral light grey, USEC blue, BEAR red, contested yellow.
Equator and Bay5 retain their original `X`; Air Pit and Bowl have A/B; Iceberg and
Factory have A/B/C. The round's verified floors are authoritative, including
Factory's randomly selected layout. No duplicated coordinates, new settings,
map-package changes, external assets or mandatory dependency are introduced.

`DominationRuntime` owns one `DynamicMapsDominationMarkers` instance after complete
placement. Each existing 0.2-second objective sample updates it **after** advancing
the rules. `ObjectiveHudVisible && IsSearchActive` supplies exact raid/match and
Live-phase admission; the same sample reaching the score/time limit clears the
overlay before the result hold. Respawns retain the round and its objectives.
Native stop, failure and normal end dispose the same instance. No gameplay,
objective placement, profile, scoring, bot or saved configuration state is written.

## Optional API and UI lifecycle

Reviewed DynamicMaps 1.2.1, product
`1.2.1+99200c60f084d1f64491dee93ceb09351bb8a2cd`, SHA256
`c41507eb1edb6d6c8101314fedf9eb52c874c980e757cbc8348f084d2231b1b8`.
The adapter binds public exact signatures and uses a soft dependency already
declared by ArenaMode. Absent DynamicMaps is silent; an incompatible API disables
only this overlay. Future versions must still pass the API checks and are not
claimed tested by this baseline.

- `MapView.LoadMap` postfix discovers new views; `UnloadMap` prefix removes only
  owned markers through native `RemoveMapMarker`. Existing inactive views are
  discovered once because a retained map can reopen without another LoadMap call.
- Location names must match the current raid. Native projection receives world
  `(x,z,y)` and owns rotation, zoom and floor visibility. The map's own markers and
  global category/opacity settings remain unchanged.
- `MapMarkerDef` uses an owned category. Its backdrop is a solid native UI quad
  rotated into a diamond (the native dot's large transparent border was unreadable
  in the first GPU test). No new texture or sprite asset is needed. Its ID label
  is centred and visible without hovering. Ownership repaint is followed by the
  native position/layer callback, preserving hidden floors.
- The instance owns its hook ID and exact marker handles. Late disposal of a
  previous round cannot remove a successor's markers. Destroyed views are pruned.
- Optional binding, rendering and removal errors cannot abort a match. Cleanup
  attempts native removal; even fallback hiding is guarded against an already
  invalidated Unity object. One warning per adapter bounds failures.

The DynamicMaps source was read to verify contracts; no code, DLL or artwork was
copied into ArenaMode.

## Verification

`tools/DominationMapMarkerChecks/Verify.ps1` exercises the production adapter with
external Unity/DynamicMaps/Harmony boundaries doubled, deliberate source defects
and installed-DLL metadata checks. `DominationRuntimeChecks` verifies the actual
runtime-to-overlay data and cleanup. Native rendering is a separate GPU-1 gate;
its result is appended after the fresh-client check.
