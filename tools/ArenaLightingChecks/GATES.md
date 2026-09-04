# Gates: Arena lighting director

Scope: Add a Labs-only, Arena-only runtime lighting layer for round countdown, time pressure, and Tactical results without editing map bundles or leaking into ordinary raids.

- [x] L0: this ledger states outcomes that can fail
  CHECK: node C:\Users\Administrator\.codex\skills\unlazy\scripts\gate-lint.mjs GATES.md
  EXPECT: LINT OK
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode-lighting-director\tools\ArenaLightingChecks; path=71fb9274b043/32 entries; EXPECT=matched; output-sha256=fe47f9c473b56f89afafdaab2eaf87348041ef7149caded95f87290772a69d0f; output-bytes=150

- [x] L1: the production policy and runtime remain off outside the exact supported Arena context, prioritize result and countdown states correctly, reuse team colors, avoid duplicate lights, and clean up every owned object
  CHECK: dotnet run --project ArenaLightingChecks.csproj -c Release
  EXPECT: ARENA_LIGHTING_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode-lighting-director\tools\ArenaLightingChecks; path=71fb9274b043/32 entries; EXPECT=matched; output-sha256=41a10e32749bc335e2e1eff51e633b8a8d9c05ad283a61f88c74e0e749b98bf0; output-bytes=47

- [x] L2: the deployment-free Release build and every directly affected Arena regression suite pass together
  CHECK: powershell -NoProfile -ExecutionPolicy Bypass -File Verify-Regression.ps1
  EXPECT: ARENA_LIGHTING_REGRESSION_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode-lighting-director\tools\ArenaLightingChecks; path=71fb9274b043/32 entries; EXPECT=matched; output-sha256=cfc98df49cce0c0ca6956f689a8955eca2c067a041f39f33a254690ae4ce800c; output-bytes=2668

- [x] L3: adversarial source review confirms exact Arena and GameWorld ownership, safe external-lighting-mod behavior, bounded per-frame work, and teardown after failure, raid end, stale-world replacement, and plugin shutdown
  EVIDENCE: Reviewed production and linked harness sources on 2026-09-04. The review corrected an invalid-duration NaN path and prevented a premature Tactical flash during buy/staging; the final design owns one transient light, performs no scene enumeration, suppresses itself for the verified Blackout/Going Dark plugin GUIDs by default, and has explicit failure, raid-end, stale-world, and shutdown cleanup.

- [ ] L4: a GPU raid on Labs visually confirms readable countdown pulses, restrained red time pressure, team-colored Tactical results, spectator-camera visibility, and no ordinary-raid effect
  EVIDENCE: unavailable on this host

ABANDON: L4 This server has no GPU, so visual rendering and live raid behavior require the user's later gaming-PC playtest.
