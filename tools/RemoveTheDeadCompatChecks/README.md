# Remove The Dead compatibility checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/RemoveTheDeadCompatChecks/Verify.ps1 -PublishedAssembly '<downloaded 4.1.3 RemoveTheDead.dll>'
```

The argument defaults to the ignored reference DLL under
`artifacts/compat-audit-20261019b/remove-the-dead/release/BepInEx/plugins/RemoveTheDead.dll`.
Download the official release separately; this check does not install or execute it.
Its exact published hash and metadata must match. See
[the compatibility note](../../docs/REMOVE-THE-DEAD-COMPAT-20261019.md) for provenance.

The console project links the three production compatibility sources and shared
Arena lifecycle protocol. Authored BepInEx/Harmony/Unity/EFT doubles provide the
boundaries. Reflection.Emit creates a plugin with a real managed call graph,
metadata, iterator and destructive counter. Manual cleanup, the automatic
iterator and the F12 route invoke the actual production prefix. This models the
Harmony bool-prefix contract; it does not emulate Mono detouring or Unity object
destruction. Cecil reads the published DLL as metadata only.

Coverage includes optional absence and late discovery; checked newer versions;
wrong GUID/assembly/version/base/signatures and cleanup routes; direct and helper
cleanup bypasses; exact native-world and bot-game ownership; server-confirmed
early Arena; active/ending Arena; missing or unreadable settings; ordinary
PMC/Scav/hideout and the next same-map raid; partial installation, missing or
removed patch receipts, runtime faults and failed unpatch. Foreign config and
timer sentinels remain unchanged.

`Verify.ps1` makes isolated copies of production sources. It runs the unchanged
baseline, 13 separately compiled faults and the unchanged baseline again. Each
fault must compile and fail its intended behavioral assertion. Production sources
must remain unchanged. Evidence is written to an ignored, unique directory under
`artifacts/remove-the-dead-checks`; only a fully green run prints
`REMOVE_THE_DEAD_COMPAT_VERIFIED`. No deployment or broad solution build occurs.

These checks establish offline contract and scope behavior, not in-game
acceptance with an installed third-party mod. Native acceptance still needs an
Arena auto/manual attempt with retained Tactical/PFT bodies and a nearby weapon
pickup, followed by a normal raid in the same game process.
