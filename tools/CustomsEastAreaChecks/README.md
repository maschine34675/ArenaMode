# Current Factory Side check (2026-09-28)

This harness now checks the refined 32-corner September 28 fixture under
`../CustomsAreaRefreshChecks/Fixtures/`, wire ID `customs-factory-side-polygon-v2`.
The old USEC bot marker `e06b071c-db66-4823-93ea-ea24471eb829` no longer clears
the boundary: 34 native candidates (20 PMC/14 bot), opening pools 5/7 USEC and
5/6 BEAR. See `../../CUSTOMS-AREAS-REVIEW.md`. The original fixture is retained
for provenance; the older evidence below does not describe current geometry.

# Second Customs contour: Factory Side team starts

The production preset `customs-factory-east-polygon-v1` promotes the user's
`area-draft-20260913-090513-276a2a3d6c424846bd202992faf489b4.json`. Its original,
unmodified bytes are retained in `Fixtures/`.

- Original file SHA256: `2F5046F1E1C739D292B3B300C1D0686BFDE420718D4600AFB9E2CEE6EC33A7A0`.
- Captured geometry SHA256: `bddf25bdaec03bce8a140cbd7fe0dd73ddc58ef58672ca5ce30078f1f1a7ad1e`.
- Exact 11 XZ vertices; X 466.07547..670.6625, Z -167.768631..-25.097086;
  approximately 21,084 square metres. No smoothing, rectangular approximation,
  snapping to the older contour, or vertical limit.
- Independent Team Deathmatch with explicit 2v2, 3v3, 4v4 or 5v5 selection and
  exact native faction starts. No PitFireTeam, automatic draft loading or full-map
  Customs support. The original draft's 2v2 value is preserved in its raw fixture.
- Displayed as `Factory Side (team starts)`, from native `ZoneFactorySide`.
  The previous East label equated positive world X with east without evidence:
  map U = `(698-X)/1070` puts this contour to the left of Center. The new label
  deliberately avoids an unverified compass claim.
- The stable `customs-factory-east-polygon-v1` selection is retained. Old fixed
  requests with absent/zero team size now fail closed; older fixed-only servers
  reject the new explicit 2..5 override. Geometry itself has not changed.
- Legacy Factory South and Customs Center rectangle are no longer offered or
  admitted by the shared resolver. Their definitions survive only as historical
  geometry fixtures. Center contour (fixed 2v2) and Center team starts remain.
- `ArenaAreaSelectionState.MapId` identifies the preview's map.
  `SelectedArea` resolves only its already offered proposal and is null for a
  full-map or unavailable proposal. It neither saves nor arms a selection.

## Native spawn evidence

Read-only survey of `SPT_Runtime/SPT_Data/database/locations/bigmap/base.json`,
SHA256 `495820FDBBC2993D786599F3E28DF2AF6243D6EBE2008CDBBA35B68A85B37873`:

- 35 geometrically accepted, distinct physical positions.
- 20 PMC markers usable by either USEC or BEAR.
- 15 native Bot/BotPmc markers, all in `ZoneFactorySide`.
- The `ZoneSnipeFactory` marker `6e771bc5-ce82-4ab8-88e2-d50dedf4e34e` and
  checkpoint/`ZoneBlockPost` markers are horizontally outside this contour;
  there are no additional guessed identity exclusions.
- Five PMC markers at Y -0.3499999 remain candidates. The draft's old spawn-height
  hints do not override the user's unlimited-height decision.

USEC starts on the left of the displayed map (larger X) with five native PMC
alternatives and eight bot markers. BEAR starts on the right (smaller X), with
five PMC alternatives and six bot markers. Exact production-selected identities
are checked against the original database, not hand-generated positions. The
minimum simultaneous same-side spacing is 1.885m / 1.710m and the closest
opposing pair is 28.876m, preserving the existing 1.5m / 25m requirements.
The central marker `ae0c5e1d-71cc-44a2-86ae-4c45e224a1ff` is retained for later
respawns but not opening starts: it would violate opposing separation. The start
pools are broad sectors, not a promise of compact formations, cover or balance.

These counts are **database candidates**, not evidence that every marker is
usable in a running scene. Existing runtime spawn blocking, carving, complete
in-area path checks and fail-closed readiness remain unchanged. This preset now
uses the existing reserved faction-start connectivity graph and later
enemy-aware respawns. The first GPU raid must validate entry at 2v2 and 5v5, both factions,
continuous respawns, boundary behavior and eventual end/leave. If connectivity
fails, diagnose the concrete marker/route rather than weakening those checks.

## Verification

From the repository root:

```powershell
pwsh -NoProfile -File tools/CustomsEastAreaChecks/Verify.ps1
```

The optional `-SptRoot C:\SPT41` selects the other installation. This standalone
net10 executable links the production geometry, catalog, menu proposal and
related policies. The only host seam is the existing Gun Game constant fixture;
no Unity rendering or native navigation is simulated. There are no mod project
references, SPT SDK targets, build hooks, deployment, or installed-file writes.
All build products and transcripts stay under this harness's ignored
`artifacts/<run-id>/` directory.

Checks compare the exact draft vertices and fingerprint, independent 2D
containment/clearance sampling, triangle coverage, unrestricted height, native
marker admission, supported/unsupported protocol combinations, explicit team-size
rules, retired-ID rejection, preview proposal identity, native start categories,
physical uniqueness and simultaneous/opposing spacing. Nine separately compiled
source mutations must fail at designated assertions: moved vertex, height clamp,
non-TDM admission, missing menu entry, missing native start, wrong start category,
too-close opponents, re-admitted retired area and Center-only team-size picker.
A modified scratch native corpus additionally proves that collapsed simultaneous
spacing is rejected; the installed database is never changed. A compile failure is
never accepted as a caught behavioral fault. The final clean baseline is rerun.
Successful completion ends with `CUSTOMS EAST CHECKS PASSED`.

Implementation passes completed: catalog/menu change; expert reread against
shared/client/server admission, actual calibration and native category masks;
defect hunt including source mutation controls; polish of provenance, evidence
boundaries and saved-selection integration notes. Parent independently reviews
the integration and commits it.
