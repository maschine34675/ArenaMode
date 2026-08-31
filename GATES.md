# Gates: Native Arena raid selection

Scope: Add Arena alongside PMC and Scav, limit its next screen to server-enabled arena maps, and preserve ordinary raids and profile protection.

- [x] G1: The native side screen owns an explicit Arena choice; PMC, Scav and leaving the raid setup clear it without persisting a global activation switch.
  EVIDENCE: Independent source review confirmed native Random toggle reuse, controller identity on map Back, explicit PMC/Scav reset and actual root/taskbar ScreenChangeHandler cleanup; native Harmony targets installed in startup log. Visual acceptance remains G6.

- [x] G2: Arena exposes only Factory day, Labyrinth and The Lab when enabled on the server; stale selection, Factory night and Marathon cannot bypass the filter, and ordinary map selection is restored.
  EVIDENCE: Independent source review confirmed DisplayLocation filtering, OnLocationSelected/set_SelectedLocation guards, GetLocationByTime day guard, Marathon/path view restoration and unchanged shared location objects. Stock Labyrinth Enabled=false accounted for; live layout remains G6.

- [x] G3: An explicitly selected Arena raid requires server arming; neither failed negotiation nor normal PMC/Scav selection can start an unprotected Arena raid.
  CHECK: dotnet run --project tools/NativeMenuChecks/NativeMenuChecks.csproj -c Release
  EXPECT: ARENA_MENU_CHECKS_PASSED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=0f08bd63b35f/31 entries; EXPECT=matched; output-sha256=74abba4b0c8680795756f6892f9996ddfbe93a265426d86f05e58cf980ea1d77; output-bytes=42

- [x] G4: The Release solution compiles against the installed SPT 4.1 binaries and deploys successfully.
  CHECK: dotnet build ArenaMode.sln -c Release --nologo
  EXPECT: Der Buildvorgang wurde erfolgreich ausgeführt.
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=0f08bd63b35f/31 entries; EXPECT=matched; output-sha256=172d78fc1f229d8f7055ae986fda26158b0bdfb55d3609ebc6ad801aedf20808; output-bytes=1086

- [x] G5: The deployed client and server DLLs exactly match the verified Release outputs.
  CHECK: powershell -NoProfile -File tools/Verify-NativeMenuDeployment.ps1
  EXPECT: ARENA_DEPLOYMENT_VERIFIED
  EVIDENCE: exit=0; shell=C:\WINDOWS\system32\cmd.exe; cwd=D:\SPT41\Development\ArenaMode; path=0f08bd63b35f/31 entries; EXPECT=matched; output-sha256=70aec75495bd44c325f9778506428e06a42e9662d11462b8afddd57bc16508a7; output-bytes=233

- [ ] G6: In-game validation covers the three-way selection, three arena maps, back navigation, a protected Arena raid and an ordinary PMC/Scav raid.
  EVIDENCE: User confirmed this server has no GPU and cannot run the game; test launch never reached the menu, no raid started. GPU-equipped test machine required.

ABANDON: G6 User confirmed no GPU on this server; handoff for visual and raid/profile validation on a gaming PC. No further game launches here.
