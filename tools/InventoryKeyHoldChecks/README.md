# Inventory key hold checks

Run `pwsh -NoProfile -File tools/InventoryKeyHoldChecks/Verify.ps1` from the repository.
This links the production `ArenaMode.Client/Hud/InventoryKeyHold.cs` (the tap-or-hold
clock behind the hold-to-peek team table, 2026-11-08) into a console program; it never
builds or deploys a mod project and needs no game.

What it pins: the quarter-second threshold; no table before the threshold, the table from
the threshold on while the key stays down; a release before the threshold is the one tap
to forward, a release at or after it forwards nothing; a release and a cancel both clear
the pending press and hide the table; a re-press restarts the clock; a tap is reported at
most once. `Verify.ps1` runs the baseline, then nine source mutants of the production file
(threshold changed, table never or immediately shown, hold release forwarded, tap lost,
threshold release counted as a tap, release or cancel leaving state behind, re-press
keeping the old clock), each of which must fail its named assertion.

What it cannot hold: the Unity node around the clock (`ArenaInventoryKeyNode`: input-tree
position, key resolution from the EFT binding, press-type handling, forwarding to the
player owner) and the IMGUI table (`ArenaScoreboardPeek`). Those are verified in a raid;
see `docs/reviews/FORK-QUICK-WINS-20261108.md`.
