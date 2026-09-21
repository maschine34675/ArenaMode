# Backpack reload offline checks

Run `pwsh -NoProfile -File tools/BackpackReloadChecks/Verify.ps1` from the repo.
It source-links the actual Arena adapter, sharing explicit native boundary doubles
with ArenaInfiniteAmmoChecks. Cases cover order, deduplication, predicates, virtual
search/examination restrictions, direct versus nested storage, unrelated generic
queries, exact local ownership and callback replacement/exception handling.
Four compiled faults must be rejected. Installed IL confirms R and menu call paths.

The runner writes isolated build/evidence directories and verifies that source and
installed files remain unchanged. It does not deploy or execute the actual generic
Harmony patch on Mono. See BACKPACK-RELOAD-REVIEW.md for GPU acceptance checks and
the unchanged native old-magazine destination behavior.
