# Area grid visibility checks

Run `pwsh -NoProfile -File tools/AreaVisibilityChecks/Verify.ps1` from the repo.

The helper compiles production-linked enclosure/policy code with Unity doubles,
rejects four independently compiled defects and runs the existing enclosure
geometry regression suite. It writes only ignored check artifacts and has no
mod project reference or deployment target. Success is
`ARENA_AREA_VISIBILITY_CHECKS_PASSED`.

See `AREA-GRID-VISIBILITY-REVIEW.md` for behavior, scope and GPU limitations.
The native renderer, shader, game lifecycle and live NavMesh are not executed.
