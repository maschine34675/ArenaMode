# Second Customs contour: Factory East

The production preset `customs-factory-east-polygon-v1` promotes the user's
`area-draft-20260913-090513-276a2a3d6c424846bd202992faf489b4.json`. Its original,
unmodified bytes are retained in `Fixtures/`.

- Original file SHA256: `2F5046F1E1C739D292B3B300C1D0686BFDE420718D4600AFB9E2CEE6EC33A7A0`.
- Captured geometry SHA256: `bddf25bdaec03bce8a140cbd7fe0dd73ddc58ef58672ca5ce30078f1f1a7ad1e`.
- Exact 11 XZ vertices; X 466.07547..670.6625, Z -167.768631..-25.097086;
  approximately 21,084 square metres. No smoothing, rectangular approximation,
  snapping to the older contour, or vertical limit.
- Fixed independent Team Deathmatch 2v2. No PitFireTeam, adjustable team size,
  inferred faction starts, automatic draft loading, or full-map Customs support.
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

These counts are **database candidates**, not evidence that every marker is
usable in a running scene. Existing runtime spawn blocking, carving, complete
in-area path checks and fail-closed readiness remain unchanged. This fixed-2v2
preset uses the existing direct complete-path readiness; the reserved team-start
connectivity graph is not newly enabled here. The first GPU raid must validate entry, both factions,
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
marker admission, supported/unsupported protocol combinations, team-size rules,
old selections and preview proposal identity. Four separately compiled source
mutations must fail for their designated assertions: moved vertex, restored
height clamp, non-TDM admission and missing new menu entry. A compile failure is
never accepted as a caught behavioral fault. The final clean baseline is rerun.
Successful completion ends with `CUSTOMS EAST CHECKS PASSED`.

Implementation passes completed: exact draft promotion; expert reread against
shared/client/server admission and native category masks; defect hunt including
source mutation controls; polish of provenance, evidence boundaries and menu
preview semantics. Parent independently reviews the integration and commits it.
