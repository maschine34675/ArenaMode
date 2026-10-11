# Arena default fire-mode checks

`pwsh -NoProfile -File tools/DefaultFireModeChecks/Verify.ps1` runs the linked
production implementation against minimal external engine doubles, then compiles
eleven deliberately broken copies. Each must fail a named behavioral assertion,
not merely fail compilation. Installed DLLs and production source are untouched.

Coverage: one-time published-hands catch-up, every genuine draw, supported native
full-auto only, manual selection while held, exact local live raid/body ownership,
deferred replacement and area loading, unsupported weapons, malfunction, native
subscriber invalidation, raid cleanup and shutdown.

This cannot prove Unity animation, Harmony binding or the native draw lifecycle.
Build the real client with `tools/DoubleJumpChecks/Build-Staged.ps1`; native testing
must disable the separate DefaultAutoFireMode plugin so it cannot mask the result.
Verify automatic first draw, manual semi while held, redraw, a semiauto-only weapon
and fresh respawn hands, then restore settings and audit the protected profile.
