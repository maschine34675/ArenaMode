# Late To The Party compatibility checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/LateToThePartyCompatChecks/Verify.ps1
```

The default published assembly is the separately downloaded 3.1.1 client under
`artifacts/health-loot-compat-20261019/releases/late-to-the-party/extracted/`.
Use `-PublishedAssembly` for another location containing those exact published
bytes. The harness reads that DLL through Mono.Cecil; it never loads, installs,
or executes foreign code. No foreign source or binary is included here.

The executable links the production adapter, contract, world scope and full
`ArenaWeaponPickup` implementation. Independently authored boundaries model
native inventory submission, source ownership, deferred move/final result,
model pooling and Harmony prefix dispatch. Fixtures retain the published method
signatures and relevant IL edges; concrete and deferred deletion bypasses must
be refused even when all expected edges still exist.

The script first runs the real source, then compiles twenty independently mutated
copies into an ignored evidence folder, and finally reruns the real source.
Each negative control must compile and fail its specific assertion. Compiler
errors, unrelated test failures and unexpectedly passing mutants fail the gate.
The final source comparison refuses a mixed-version verification run.

Coverage includes exact original root/generated attachments, ordinary items,
same-id different instances, detached/new attachments, changed native source
owner/root, foreign worlds/games, initial discovery, queued cleanup, deletion
side effects/accounting, source-owner transfer gates, native InProcess, disposal
while pending, ambiguous submissions, generation retirement, collection and later
ordinary drops, lifecycle cleanup without the optional mod, absent/old/new/drifted
plugins, partial patching, missing receipts and shutdown after unpatch failure.

These checks do not emulate Unity's native object lifetime or Mono detours.
They are offline behavioral/contract evidence; native discovery, real pickup
collection, ordinary loot cleanup and real delayed/retiring operations require
separate gameplay evidence. See the dated compatibility document.
