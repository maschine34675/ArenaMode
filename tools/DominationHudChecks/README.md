# Domination HUD stacking checks

Run `pwsh -NoProfile -File tools/DominationHudChecks/Verify.ps1` from the repo.
The harness links the production layout, objective panel and powerup panel;
only Unity/TMP, the score anchor, match flags and pickup data are doubles. It
does not import mod projects or deployment targets.

The objective row keeps its existing position below the score. Its shared
52-unit height is also used to create the view. Effects and pickup notices move
below that actual row with a 16-unit gap while the row is visible; every other
mode/phase retains the old score-bottom plus 16-unit anchor. Positions refresh
before the existing powerup-text throttle. Tests inspect the created views,
their rendered height inputs, parent visibility, offsets, notices, multi-effect
height, phase/current-match changes, hidden/restored HUD and actor replacement.
Mutation checks must fail named behavioral assertions, including reintroducing
the original overlap and delaying layout until the text refresh.

The doubles do not establish native text metrics, screen clipping under extreme
user offsets or actual Unity rendering. Native screenshot acceptance is separate.
