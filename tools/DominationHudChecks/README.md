# Integrated Domination score checks

Run `pwsh -NoProfile -File tools/DominationHudChecks/Verify.ps1` from the repo.
The harness links the production score panel, objective row, layout and powerup panel;
only Unity/TMP, config, match flags/score data and pickup data are doubles. It
does not import mod projects or deployment targets.

The 36-unit objective row is a child of the actual score view with no independent
background. The one score surface grows from 84 to 120 units only while objectives
are visible. Effects and pickup notices follow that complete height with a 16-unit
gap; every other mode/phase retains the original score size and pickup position.
Geometry and row visibility refresh before score/powerup text throttles. Tests
inspect the created hierarchy, surfaces, one-to-three-point text, actual height
inputs, offsets, notices, phase/current-match changes, hidden/restored HUD and actor replacement.
Mutation checks must fail named behavioral assertions, including reintroducing
the original overlap, restoring a separate card, detaching the row, failing to
extend the shared surface, and delaying layout until the text refresh.

The doubles do not establish native text metrics, screen clipping under extreme
user offsets or actual Unity rendering. Native screenshot acceptance is separate.
