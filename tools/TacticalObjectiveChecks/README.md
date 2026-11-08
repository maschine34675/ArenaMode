# Tactical objective checks

Run `pwsh -NoProfile -File tools/TacticalObjectiveChecks/Verify.ps1` from the repository. This
links the production `ArenaMode.Client/Modes/TacticalObjectivePolicy.cs` (the capture point's
pure clock) and `TacticalObjectiveLanes.cs` (the opening lanes' pure geometry) of
`docs/reviews/TACTICAL-OBJECTIVE-20261108.md` into a console program; it never builds or deploys a
mod project and needs no game.

What it pins: the setting clamps (appear 0 to 300 s, hold 3 to 60 s); an inert policy before the
round; the appear delay and its countdown text; a clean hold at the configured rate that
completes exactly once and stays terminal; a contested point pausing without a reset; an empty
point resetting; a change of holder restarting; time that runs backwards or is NaN changing
nothing; a step over thirty seconds awarding nothing; negative counts as nobody; the status
texts. Lanes: one per bot, across the middle, centred, evenly spaced, the same set whichever group
is named first, perpendicular on a diagonal axis, spacing floored at 6 m and capped at 18 m, none without both centroids, a default
axis for coincident centroids. `Verify.ps1` runs twelve source mutants, each of which must fail
its named assertion.

What it cannot hold: the Unity side (occupancy of real bodies, NavMesh sampling of the lanes,
the view, the door reset, the bridge feed) in `TacticalMode.Objective.cs`; those are verified in
a raid.
