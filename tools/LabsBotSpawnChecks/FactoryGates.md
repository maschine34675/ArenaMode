# Factory unsafe-point regression gates

OWNS: tools/LabsBotSpawnChecks/**

Scope: Test-only production-linked verification of the exact Factory unsafe point, guarded bot planning/dispatch and protected Tactical scene views. Root owns production and all game builds, deployment, documentation outside this harness and commits.

Four passes completed: (1) native collection declarations and existing harness read before adding fixtures, (2) root contract refinements incorporated for Labs-only server classification, exact world/map checks and armed-mode precedence, (3) six independent source mutants prove the intended failure boundaries, (4) docs and mutation isolation re-read, whitespace checked and complete baseline/mutation gates rerun. Both installed database fixtures ran (145 original to 144 retained on day and night); no fixture skip on this host. Root performs independent production/harness review and rerun. Live placement, Unity/Harmony execution and PFT follower behavior still require the user GPU raid and are not claimed by these tests.

- [x] G1: Production-linked checks reject the exact Factory day/night point at bot boundaries, preserve safe batch capacity and retain original scene objects outside protected team selection.
  CHECK: dotnet run --project tools/LabsBotSpawnChecks/LabsBotSpawnChecks.csproj -c Release
  EXPECT: LABS_BOT_SPAWN_CHECKS_PASSED
  CWD: .
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=145a6a34b272/32 entries; EXPECT=matched; output-sha256=e3691f110020c8b3f5b53e5058201ac8c5863cd339fd4fbe439d95c7892e5369; output-bytes=203

- [x] G2: Deliberate point-policy, selection/capacity, team-view, world and mode-precedence defects fail their intended gameplay assertions; unchanged production then passes.
  CHECK: pwsh -NoProfile -File tools/LabsBotSpawnChecks/Verify-FactoryRegression.ps1
  EXPECT: FACTORY_BOT_SPAWN_REGRESSION_VERIFIED
  CWD: .
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=145a6a34b272/32 entries; EXPECT=matched; output-sha256=318616df613ba4631a8ca7eb05f2e5264287bf480f02447ac27d55dea5adee5f; output-bytes=833
