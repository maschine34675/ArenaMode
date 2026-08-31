# Gates: Native Arena raid selection

Scope: Add Arena alongside PMC and Scav, limit its next screen to server-enabled arena maps, and preserve ordinary raids and profile protection.

- [x] G1: The native side screen owns an explicit Arena choice; PMC, Scav and leaving the raid setup clear it without persisting a global activation switch.
  EVIDENCE: Independent source review confirmed native Random toggle reuse, controller identity on map Back, explicit PMC/Scav reset and actual root/taskbar ScreenChangeHandler cleanup; native Harmony targets installed in startup log. Visual acceptance remains G6.
  FOLLOW-UP 2026-08-31: User playtest exposed missing visibility. Actual level48 inspection found RandomToggleSpawner under inactive AlphaBlocker (CanvasGroup alpha=.3, interactable=false). ArenaSideSelectionView now activates and unlocks only this validated exclusive wrapper; independent review found no remaining issue. G1 describes implementation evidence, not a passed visual test.
  REGRESSION: dotnet run --project tools/NativeSideViewChecks -c Release; exit 0, PASS: 29 native side-view regression checks (production helper linked). Scene-based stand-ins cover inactive ancestry, opacity/input, tooltip, eligibility and unchanged ordinary controls; actual rendering remains G6.
  MUTANTS: Temporary source copies without wrapper activation/unlock and without canvas unlock failed the intended visibility and opacity/input assertions respectively. Production source was hash-verified unchanged; final normal run passed.

- [x] G2: Arena exposes only Factory day, Labyrinth and The Lab when enabled on the server; stale selection, Factory night and Marathon cannot bypass the filter, and ordinary map selection is restored.
  EVIDENCE: Independent source review confirmed DisplayLocation filtering, OnLocationSelected/set_SelectedLocation guards, GetLocationByTime day guard, Marathon/path view restoration and unchanged shared location objects. Stock Labyrinth Enabled=false accounted for; live layout remains G6.
  FOLLOW-UP (empty maps): User confirmed the side button is visible, but map selection was empty. Actual level48 component 25068 binds BOTH containers to Transform 15523. Hiding the paths container therefore hid the map buttons. The new helper hides only individual LocationPath views plus Marathon and restores their original visibility. Independent review passed; corrected live map acceptance remains G6.
  REGRESSION (empty maps): dotnet run --project tools/NativeMapViewChecks -c Release; exit 0, PASS: 40 native map-view regression checks (production helper linked). A temporary source-copy mutant hiding the entire container fails the intended map-visibility assertion. Production source SHA unchanged; normal rerun passed. Native rendering/clicks remain G6.

- [x] G3: An explicitly selected Arena raid requires server arming; neither failed negotiation nor normal PMC/Scav selection can start an unprotected Arena raid.
  CHECK: dotnet run --project tools/NativeMenuChecks/NativeMenuChecks.csproj -c Release
  EXPECT: ARENA_MENU_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=0f08bd63b35f/31 entries; EXPECT=matched; output-sha256=74abba4b0c8680795756f6892f9996ddfbe93a265426d86f05e58cf980ea1d77; output-bytes=42

- [x] G4: The Release solution compiles against the installed SPT 4.1 binaries and deploys successfully.
  CHECK: dotnet build ArenaMode.sln -c Release --nologo
  EXPECT: Der Buildvorgang wurde erfolgreich ausgeführt.
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=0f08bd63b35f/31 entries; EXPECT=matched; output-sha256=172d78fc1f229d8f7055ae986fda26158b0bdfb55d3609ebc6ad801aedf20808; output-bytes=1086
  FOLLOW-UP 2026-08-31: Visibility fix rebuilt and deployed successfully (exit 0, four existing CS8632 warnings, no errors). Server 31 tests, menu 31 assertions and announcer 17 assertions passed again.
  FOLLOW-UP (empty maps): Per-path visibility fix also builds and deploys successfully with the same four existing warnings. Server 31, menu 31, side-button 29 and announcer 17 checks pass again.

- [x] G5: The deployed client and server DLLs exactly match the verified Release outputs.
  CHECK: powershell -NoProfile -File tools/Verify-NativeMenuDeployment.ps1
  EXPECT: ARENA_DEPLOYMENT_VERIFIED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=0f08bd63b35f/31 entries; EXPECT=matched; output-sha256=70aec75495bd44c325f9778506428e06a42e9662d11462b8afddd57bc16508a7; output-bytes=233
  FOLLOW-UP 2026-08-31: ARENA_DEPLOYMENT_VERIFIED; client SHA256 FDBDD0F6B0ED90EEC4B9C50497CEB05DBC7E70D0C9045B8CA458684E69A51550, server SHA256 D1F6603F7CB15CF3FCD578CF8F24C0A83064F8E328F01637084077ED896904F5. The deployed client was also decompiled to confirm the new wrapper activation code is present.
  LATEST (empty maps): ARENA_DEPLOYMENT_VERIFIED; client SHA256 4454634119025A2903E0757B608DF43C3E69D6224A9B08960C8E58A760C6517A, server unchanged. Deployed client decompilation confirms the per-path fix is present.

- [ ] G6: In-game validation covers the three-way selection, three arena maps, back navigation, a protected Arena raid and an ordinary PMC/Scav raid.
  EVIDENCE: User confirms both the Arena side button and the corrected map selection work on the gaming PC. Full navigation, ordinary PMC/Scav and raid/profile acceptance remain open. This server has no GPU; no further game launch was attempted.

ABANDON: G6 User confirmed no GPU on this server; handoff for visual and raid/profile validation on a gaming PC. No further game launches here.
