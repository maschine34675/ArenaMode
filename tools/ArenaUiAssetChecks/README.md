# Arena UI asset checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/ArenaUiAssetChecks/Verify.ps1
# Optional: additionally inspect a staged client DLL, without loading/executing it.
pwsh -NoProfile -File tools/ArenaUiAssetChecks/Verify.ps1 -ClientAssembly '<stage>/maschine-ArenaMode.Client.dll'
pwsh -NoProfile -File tools/ArenaUiAssetChecks/Verify-Integration.ps1
```

The first runner needs Python 3.9+ (standard library only) and .NET 10. It decodes
all 18 real RGBA PNGs independently of Unity doubles: chunk CRCs, bounded inflated
data, fixed dimensions, visible white pixels, transparent surroundings and alpha
edges/gradient. It verifies manifest/source provenance, the complete MIT notice,
current hashes, exact file/resource sets and exclusion of all eighteen retired
game-art fingerprints. Eight invalid-input controls verify these rejection paths.
The fingerprint JSON contains hashes only, never retired pixels.

It compiles the production loader, categorical icon policy, pooled killfeed,
hitmarker and scoreboard presentation against narrow Unity/TMP/IMGUI doubles,
and rejects fifteen compiled regressions. It restores and
reruns the unmodified inputs after the negative controls. It writes isolated
logs and illustrative SVGs under `artifacts/ui-asset-checks/<id>`.

With `-ClientAssembly`, PE metadata is read without executing the assembly. Its
exact 21 UI resources (18 images and three attribution files) must match the
validated current sources; the three readable `ArenaMode/Ui` notices beside the
DLL must match the embedded copies. The normal product build retains the
existing suppressible asset-deploy boundary for these notices.

The integration runner executes the existing server and UI regression checks,
then a staged client build. Neither runner deploys installed DLLs.

The doubles model resource ownership, calls, rectangles and approximate font
metrics, not Unity's actual PNG decoder, fonts, frame lifecycle or GPU output.
They deliberately provide no pretend pixel colors. Real neutral pixels are
proved by the independent PNG audit, not a stub-generated blue test pattern.
Preview SVGs are not screenshots; the killfeed preview omits TMP colour markup.
Current artwork provenance/license is in `ArenaMode.Client/Assets/Ui/NOTICE.md`;
native acceptance and remaining limits are recorded separately in `docs/HANDOFF.md`.
Both native bleeding causes are checked with retained prior headshot metadata;
all pooled rows must clear that decoration and restore neutral weapon colour on
their next direct bullet kill. Combat metadata is not modified by presentation.
Barbed-wire notices reuse the same pooled rows but carry no attacker, weapon or
distance. Their categorical icon, escaped victim text, cleared headshot, and return
to ordinary kill presentation are exercised separately from death capture.
