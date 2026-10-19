# AI Limit integration checks

`pwsh -File tools/AiLimitCompatChecks/Verify.ps1` compiles the actual adapter,
scope and reflection contract against small EFT/BepInEx/Harmony boundary doubles.
It exercises ordinary PMC, Scav, hideout, pending/confirmed starts, exact active
worlds, same-map follow-up raids, later limiter enables, changed plugin APIs,
partial patching, missing native fields, runtime read failures and shutdown.
Seven separately compiled mutants must fail their intended behavior assertions.

For the release-specific metadata check, download AI Limit **1.9.2** separately
from its upstream release, extract outside the live game, then supply:

```powershell
pwsh -File tools/AiLimitCompatChecks/Verify.ps1 -PublishedAssembly 'D:\path\dvize.AILimit.dll'
```

Mono.Cecil reads the DLL; no third-party plugin is loaded or executed. This checks
the GUID/version, component/method signatures and the actual raid-start forwarder
against the released binary, independently of the GitHub source snapshot.

No test deploys a DLL, edits user settings, or runs EFT. Prefix execution is a
managed test double, so these checks do not certify Mono Harmony ordering,
native AI behavior, PFT transitions or round/respawn acceptance. Evidence goes
to the ignored `artifacts/ai-limit-checks/` directory.
