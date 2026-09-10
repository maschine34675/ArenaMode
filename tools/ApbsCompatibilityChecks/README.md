# APBS compatibility checks

From the repository root, use PowerShell 7:

```powershell
pwsh -NoProfile -File tools/ApbsCompatibilityChecks/Verify.ps1
pwsh -NoProfile -File tools/ApbsCompatibilityChecks/Verify-Contracts.ps1
pwsh -NoProfile -File tools/ApbsCompatibilityChecks/Verify-Faults.ps1
```

`-SptRoot C:\SPT41` is supported; otherwise the root is derived from the repository
location. Requires the user's installed APBS 2.4.1, SPT 4.1, BepInEx's Mono.Cecil
and .NET 10. No APBS binaries or source are bundled. Outside this explicit check,
the installed integration test skips unless `ARENA_APBS_TEST_ROOT` is supplied.

The first command runs all server tests, redirects build deployment, requires
the installed APBS test to pass and verifies installed Arena, Redeploy and APBS
DLLs/config remain byte-identical. It does not start the server or migrate config.
The second reads installed method contracts/clone order and existing armbands.
The third copies source to ignored artifacts, verifies a pristine control, then
removes session validation there and requires the same-token rearm test to fail.
Production source and the installation stay unchanged. Logs/TRX/hash receipts
live under `artifacts/apbs-compat/`.

See `APBS-COMPATIBILITY-REVIEW.md` for scope and the remaining user raid checks.
