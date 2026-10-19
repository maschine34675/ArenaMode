# Deminvincibility compatibility checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/DeminvincibilityCompatChecks/Verify.ps1 -PublishedAssembly <published-2.2.0-DLL>
```

The default argument points to the separately downloaded, ignored audit artifact.
The published DLL is inspected with Cecil, never loaded/installed. All three
production adapter files are compiled into the harness. Independent boundaries
model foreign behavior; the **actual production transpiler output is emitted and
executed**, not replaced by a test-only clamp implementation. Reflection fixtures
exercise real method/type/assembly identity resolution and Harmony receipts.

The script builds separate copies for each negative control, requires the
intended failed assertion (compile failures do not count), reruns the original,
and verifies production files were not edited. Evidence is ignored under
`artifacts/deminvincibility-checks/`. This is offline contract evidence, not a
claim that native game animation/death/respawn has been tested.
