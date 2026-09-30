# Incendiary prototype checks

From the repository root:

```powershell
pwsh -NoProfile -File tools/MolotovChecks/Verify.ps1
pwsh -NoProfile -File tools/MolotovChecks/Build-Staged.ps1
```

The first command links the real fire policy/runtime/geometry and BigBrain escape
layer against explicit Unity/EFT boundary doubles. It runs 56 assertions and
seven separately compiled mutants, each required to fail its intended assertion.
No installed files are changed. Logs and mutant builds go to ignored `artifacts`.
Physics geometry, animator, Harmony dispatch, audio and actual bot movement still
require native acceptance; see `MOLOTOV-PROTOTYPE-REVIEW.md`.

The second reuses the established no-deploy client/companion build, validates
installed client/server/core/companion hashes before/after, and emits a source
and artifact manifest. It does not deploy or certify an already-running game.
