# Incendiary prototype checks

From the repository root:

```powershell
pwsh -NoProfile -File tools/MolotovChecks/Verify.ps1
pwsh -NoProfile -File tools/MolotovChecks/Build-Staged.ps1
```

The first command links the real fire policy/runtime/supply/geometry and BigBrain
escape layer against explicit Unity/EFT boundary doubles. It runs 89 assertions
and thirteen separately compiled mutants, each required to fail its intended assertion.
Supply checks retain authored grenade identities, grant exactly one extra, preserve
manual grenade selection, refuse full/busy/forbidden inventory, wait for native
resource preload, and reject stale completions or duplicate grants after event
failures and off/on toggles. The preloader and native inventory remain boundary
doubles; the real native call path is documented in the supply review.
No installed files are changed. Logs and mutant builds go to ignored `artifacts`.
Physics geometry, animator, Harmony dispatch, audio and actual bot movement still
require native acceptance; see `docs/MOLOTOV-SUPPLY-20261015.md` and the historical
`docs/MOLOTOV-PROTOTYPE-REVIEW.md`.

The second reuses the established no-deploy client/companion build, validates
installed client/server/core/companion hashes before/after, and emits a source
and artifact manifest. It does not deploy or certify an already-running game.
