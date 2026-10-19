# Continuous Healing compatibility checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/ContinuousHealingCompatChecks/Verify.ps1
```

Supply `-PublishedAssembly <path>` if the separately downloaded official 1.6.3
DLL is elsewhere. The known SHA256 and metadata are inspected with Mono.Cecil;
the foreign DLL is never executed, installed or bundled. Local .NET 10 and the
existing SPT `BepInEx/core/Mono.Cecil.dll` are required.

The executable compiles the three real production compatibility files against
controlled boundary types and independent foreign method-shape fixtures. There
is no production-project reference or deployment target. It runs ordinary/Arena
body ownership, retirement, current-local pass-through, native fallback,
metadata and installation-fault controls. `Verify.ps1` copies production source
to a fresh ignored artifact folder and compiles fourteen intentional defects;
each must fail its named behavioral assertion, then the unmodified control must
pass again. Source snapshots must remain unchanged throughout verification.

Success token: `CONTINUOUS_HEALING_COMPAT_VERIFIED`. The script reports the
evidence directory with individual build/run logs. See
`docs/CONTINUOUS-HEALING-COMPAT-20261019.md` for source contracts and the separate
native test boundary.
