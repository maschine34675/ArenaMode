# Arena UI asset checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/ArenaUiAssetChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaUiAssetChecks/Verify-Integration.ps1
```

The first runner verifies all 17 asset SHA256 values, compiles the production
loader, icon policy, pooled killfeed, hitmarker and scoreboard presentation against narrow
Unity/TMP/IMGUI doubles, and rejects twelve compiled regressions. It restores and
reruns the unmodified inputs after the negative controls. It writes isolated
logs and illustrative SVGs under `artifacts/ui-asset-checks/<id>`.

The integration runner executes the existing server and UI regression checks,
then a staged client build. Neither runner deploys installed DLLs.

The doubles model resource ownership, calls, rectangles and approximate font
metrics, not Unity's actual PNG decoder, fonts, frame lifecycle or GPU output.
Preview SVGs are not screenshots; the killfeed preview omits TMP colour markup.
Native acceptance cases and asset distribution restrictions are documented in
`ARENA-UI-ASSETS-REVIEW.md` and `ArenaMode.Client/Assets/Ui/NOTICE.md`.
Both native bleeding causes are checked with retained prior headshot metadata;
all pooled rows must clear that decoration and restore neutral weapon colour on
their next direct bullet kill. Combat metadata is not modified by presentation.
