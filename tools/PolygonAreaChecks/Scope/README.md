# Capture scope checks

Run from the repository root, with no game-project build or deployment:

```powershell
dotnet run --project tools/PolygonAreaChecks/Scope/Scope.csproj -c Release -p:SPTPath=D:\SPT41
```

Success emits `ARENA_POLYGON_SCOPE_PASSED` plus the assertion count. Any assertion
or uncaught callback exception returns a nonzero exit code without that token.
`ProductionCaptureSource` can point at a temporary mutated controller source for
negative controls; the default links the current production source directly.

The harness invokes the actual controller's private `Update`, `OnGUI`, disable
and destroy callbacks. It links the actual draft, file exporter and shared map /
polygon rules. Explicit input frames exercise shortcut modifiers and precedence.
Valid exports use the actual file writer under a fresh harness-only temporary
plugin directory; repeated exports preserve the first file. Cleanup resolves and
checks that temporary target before deleting it. No installed game/profile/config
files are read or changed (except reading the Newtonsoft.Json assembly).

Coverage includes dormant normal raids/off, exact game/world/body mismatches,
missing/dead bodies, respawn/spectator/menu suspension, same-world draft retention
after body replacement, scope/setting lifecycle resets, nearest eligible aim hits,
Arena-owned versus foreign colliders, explicit export, fault latching and GUI
state restoration. Positive controls use a live own body and valid triangle so
guard-removal mutants cannot pass merely because the draft was already invalid.

The Unity/EFT/BepInEx/session doubles are deliberately limited contracts. Ray hits,
player ancestry, key frames and exact-session identities are supplied by the host;
they do not prove real Unity physics ordering, native ownership registration,
input delivery, visibility, actual game lifecycle or rendering. GPU scene checks
remain separate. The independent draft/export suite covers detailed data rules;
this suite covers the controller's admission and side-effect boundaries.
