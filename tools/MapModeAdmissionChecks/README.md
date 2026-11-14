# Map/mode admission checks

Run `pwsh -NoProfile -File tools/MapModeAdmissionChecks/Verify.ps1 -SptRoot D:\SPT41`.
This compiles the real shared catalog/participant policy and client spawn planner
with minimal native vector/marker boundaries. It requires the installed `base.json`
of the six ported maps (Equator, Bay5, Air Pit, Bowl, Iceberg, Chop Shop) and of
Customs; it fails rather than silently skipping absent data.
No game process, deployment, settings or profiles are changed.

Checks cover supported mode pairs, the five distant rows a Tactical squad needs
(Equator and Iceberg have them everywhere, Chop Shop's yard centre does not), the
PFT Zombie exclusion on Equator and Chop Shop (solo remains available), bounded all-mode
ownership, exact frozen totals/team sizes, retired/wrong-map
contours and unidentified legacy populations. Installed opening markers are
planned by the production selector; safe initial starts are not the same as
Tactical multi-round capacity, which is why the centres are pinned separately
(`EQUATOR-CENTER`, `ICEBERG-CENTER`, `CHOPSHOP-YARD-CENTER`). Fifteen compiled
faults must each fail at their intended assertion, after the positive baseline.

Installed-data pins of the spawn rows (ArenaMapPort 0.6.0, measured 2026-11-12 on the build 0.5.0+160523d):
`SPAWN-ROWS-DATA` (the base.json of Equator, Iceberg and Chop Shop exists and has at
least 16, 17 and 16 distinct Bot/BotPmc rows; on the published 0.5.0, with a missing
file or with a stub file without spawn rows it fails with a
sentence that names the needed build, because the Tactical admissions of 2026-11-13
were decided on those rows and a red pin on older data is not an ArenaMode defect),
`EQUATOR-GRID` and `ICEBERG-GRID` (the minimum of the production selector's
`CountUsable` over the bounding box of the map's Bot rows, 1 m steps, y of the nearest
row, is at least five; today 6 and 9), `CHOPSHOP-GRID` (the same minimum is 1 to 4,
the evidence of the PitFireTeam refusal beside `CHOPSHOP-YARD-CENTER`; today 3) and
`EXIT-NAMES` (`ArenaTeamBoxCatalog`'s `BoothExits` equal the installed `exits[].Name`
of each of the six ported maps as a set, in its own loop over the six formations
because Bowl has no row in the mode matrix; only this pin notices a rename on
ArenaMapPort's side). `SPAWN-ROWS-ZONE` says that the Bot rows of the three maps lie
in one zone (the grid and the evidence pins read it), and `DATA-FILE` names a
missing installed file of any other map. The three newest faults raise the 25 m floor to 30 m (dies at
`EQUATOR-GRID`) and rename an Iceberg and a Bowl exit literal (both die at
`EXIT-NAMES`); the harness
project takes the production team-box catalogue and selector through
`ProductionTeamBoxCatalogSource` and `ProductionBurstSelectorSource` for them.

This is database/rule evidence, not NavMesh, physics or live round acceptance.
