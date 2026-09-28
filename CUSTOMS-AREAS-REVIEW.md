# Customs contours and unified selection — 2026-09-28

## Requested behavior

Three areas, one entry each, all independent Team Deathmatch with explicit
2v2, 3v3, 4v4 or 5v5. Bounded Deathmatch/Gun Game and Tactical/PFT are not newly
enabled: the old fixed/non-fixed entries were both TDM. Whole-map Customs remains
unavailable. Height stays unrestricted. Preview uses the exact same shared
polygons as client enclosure and server spawn filtering, without new artwork.

| Display | New wire ID | Original draft time (UTC) | Corners |
|---|---|---|---|
| Factory Center | `customs-factory-center-polygon-v2` | 17:28:27 | 40 |
| Factory Side | `customs-factory-side-polygon-v2` | 17:22:25 | 32 |
| Customs Yard | `customs-yard-polygon-v1` | 17:33:24 | 28 |

Original bytes are in `tools/CustomsAreaRefreshChecks/Fixtures/`; no smoothing,
point removal, height clamp or hand-adjustment of the user's outlines. Geometry
fingerprints are recorded at each production definition. Earlier Center rectangle,
fixed contour, team variant, Side v1 and Factory South IDs are rejected on the
wire. This prevents mixed versions negotiating different walls. `AreaSelectionValues`
migrates exact old saved Center/Side preferences during BepInEx Clamp only; its
acceptable list and menu setters still reject old IDs. Unknown/retired prototype
preferences retain the full-map fallback, with explicit native-menu preparation.

## Starts and provenance

Installed Customs `SPT_Data/database/locations/bigmap/base.json` SHA256:
`495820FDBBC2993D786599F3E28DF2AF6243D6EBE2008CDBBA35B68A85B37873`.

- Center retains its previous exact starts and explicit sniper exclusion. New
  outline admits 29 native positions: 18 PMC and 11 bot candidates. Starts: USEC
  2 PMC/5 bots, BEAR 5 PMC/5 bots; opposing minimum 35.063m.
- Side admits 34: 20 PMC and 14 bot candidates. Remove USEC marker
  `e06b071c-db66-4823-93ea-ea24471eb829`, now only 0.685m from the refined wall.
  Remaining start pools: USEC 5 PMC/7 bots, BEAR 5 PMC/6 bots; minimum opposing
  separation 28.876m. Existing 0.75m inset and 1.5m body spacing are unchanged.
- Yard's original 12 markers contain only one PMC cluster, so they cannot provide
  opposing native player starts. User explicitly authorized additional starts.
  Twenty new markers provide two compact groups, each 2 PMC alternatives and
  8 bot alternatives. The groups are 83.320m apart at their closest pair.

Yard donor IDs: USEC `f2d2c753-7e4c-4055-83da-b1c2693d4552`, BEAR
`7f751d14-f03f-406c-94d9-1600aa068835`, both native ground bot markers in
`ZoneCustoms`, core 3. Candidate offsets stay within 4m X and 6m Z, using donor Y.
They are NOT surveyed safe positions: live NavMesh, wall containment, spawn
blocking, occupancy and complete in-area paths remain decisive. No fallback
outside the contour or reduction of the selected roster is introduced.

New GUIDs are deterministic UUIDv5 of
`com.maschine.ArenaMode/customs-yard-polygon-v1/{faction}/{role}/{index}` with
the URL namespace. All originals remain unchanged. Only the exact armed Arena
clone receives new deep-copied records; nested colliders are not shared with
the donors. Donor position, core and zone are checked; missing/changed donors,
duplicate IDs or mismatched existing extras reject the clone before publication.
Final validation requires every exact extra and never reconstructs missing ones.
The usual snapshot, response identity and protected failed-start recovery remain.
No pickup layout is copied across changed boundaries or automatically added.

## Native contract verification

Installed decompilation: `EFT/BaseLocalGame.cs` calls `CreateFromScene` with the
raid's spawn records; `EFT.Game.Spawning/SpawnPointsCollection.cs` `FixSpawns`
creates unknown IDs using `SpawnPointMarker.Create`. This copies core/category/
side/pose and trigger collider, registering the marker in the named BotZone.
Subsequent `BotZone.Init` builds its spawn cache and sets the native zone reference.
BotPmc marks the new bot positions dispatchable. Existing-ID `FixParams` does NOT
update zone or core, which is why adding fresh IDs is required here.

The native Error-level line `Warning!!! 20 SpawnPointMarker will be added...`
is expected for Yard. It does not replace checking actual readiness failures.
No new client Harmony patch or native cache manipulation is needed.

## Verification and user acceptance

`tools/CustomsAreaRefreshChecks/Verify.ps1`: exact fixture comparison, independent
polygon containment, native donor/category/capacity/spacing, all three menu choices,
retired-wire rejection and compiled negative controls; includes the updated Side
geometry/triangulation/clearance harness. `Verify-Server.ps1`: full unit suite,
including Yard immutable/deep-copy/idempotence, changed-donor/collision failures
and final-result tampering without repair. One unrelated installed APBS integration
case requires its separate lab and is explicitly reported as skipped.
`Verify-Integration.ps1`: actual BepInEx bind/save/reload/migration, native-menu
transport, selection hooks, area view, map preview and existing team runtime.
These source-linked checks do not execute EFT or certify scene paths.

Independent read-only research and adversarial review found no open P0–P2 after
implementation. Offline results and delivered artifact hashes belong in HANDOFF
and the deployment receipt; don't infer a running process's version from disk.

After client AND server restart, check all three menu entries and previews, then
2v2 and 5v5, ideally both local factions. Verify own/opposing opening sides, exact
roster readiness, several respawns and protected leave/end. For Yard, especially
check furniture/doors/ground around the two new formations. Keep any concrete
failed-point/navigation diagnostic; adjust the candidate positions, never weaken
containment or replace failed points with unvalidated native random starts.
