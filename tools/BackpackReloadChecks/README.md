# Backpack reload checks

Run `pwsh -NoProfile -File tools/BackpackReloadChecks/Verify.ps1` from the repo.
The actual adapter is source-linked with explicit native boundary doubles and
installed Harmony instruction types. No generic native method may be detoured.

Cases cover the three non-generic callsites, exact typed call replacement,
stack shape, branch labels/exception blocks, drift rejection, native ammo/grenade
queries, native-first magazine collection, order, deduplication, predicates,
virtual search filters, direct versus nested storage, local identity and
body/backpack replacement during callbacks. Six compiled faults must fail,
including restoration of the unsafe generic detour. Installed IL verifies exact
Magazine callsites and the separate ThrowWeap query used by G.

The runner writes isolated evidence and verifies source/installed files remain
unchanged. It does not install Harmony patches or run Unity/Mono. See
BACKPACK-RELOAD-REVIEW.md and the separate GPU result record.
