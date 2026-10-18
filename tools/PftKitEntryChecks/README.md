# Protected Tactical PMC entry kit checks

Run `pwsh -NoProfile -File tools/PftKitEntryChecks/Verify.ps1` from the repository.
`-SkipFaults` runs only the behavior suite. No game, profile, settings or installed
DLL is modified. All build products and compiled fault copies stay under ignored
`obj`, `bin` and `artifacts/pft-entry-checks/<run>/`.

The executable links the **real** `TeamArenaEntryKit`, `KitBuilder` and kit model
sources. `Extract-Transaction.ps1` copies exact production method blocks from
`TeamArenaLoadoutSnapshot`: entry admission, commit, protected transfer plan,
forward/reverse transfers, metadata snapshot/remapping/rollback and exceptions.
Only the native field accessor bootstrap is substituted by CLR field-ref delegates.
There is no independent transaction implementation in the doubles. The full client
must still compile against the installed native EFT assemblies separately.

The boundary doubles provide mutable inventory slots/grids, fresh IDs and injected
native failures. They do not claim native packing, animation, controller, PFT or
live profile restoration acceptance. Synthetic fixtures cover complete and broken
trees, private-subtree omission, original pocket/special-slot retention, unchanged
source inventory during preparation, invalid `upd`, missing vital parts, wrong
owners, altered prepared trees, one-shot use, short spare-magazine capacity,
required supplies, exact retained identities, checked magazines/chambers, scoped
examination and both transfer and late sanitation/weight failures.

The runner first requires the positive suite, then recompiles production copies
with explicit faults and requires a behavioral failure (compiler failures do not
count). It rebuilds and reruns the unmodified baseline last. Each successful run
writes source hashes, bounded transcripts and a manifest for parent re-verification.

Implementation passes: the ordinary kit builder retains its original defaults;
strict construction is an entry-only seam. The expert review checked EFT's real
`Item.CurrentAddress`, `CompoundItem.MissingVitalParts`, `Slot` add/remove results
and `Profile.Examined` null semantics. Adversarial review added prepared-tree
revalidation, rollback of newly learned templates, and removal of consumed weak-
table owners so issued equipment does not retain the old combat equipment tree.
