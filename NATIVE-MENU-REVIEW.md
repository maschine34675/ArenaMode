# Native Arena selection

Implemented 2026-08-31 for the installed SPT 4.1 client. Runtime acceptance is still open; see `GATES.md`.

## Player flow

Choose **Arena** on the native PMC/Scav screen. The existing, unused third native toggle is reused, including its group, animation and layout. Arena remains a PMC raid internally because that is the post-raid path protected by the server.

The next screen offers **Factory (day), Labyrinth and The Lab**, subject to the client allowlist and server Arena configuration. Other locations and Marathon are hidden. Factory cannot switch to its unsupported night variant. Labyrinth is exposed only in this Arena view even when the ordinary game location has `Enabled=false`. Native map locks are not changed globally; the existing Arena keycard waiver remains in use.

Returning from the map screen preserves Arena. Selecting PMC or Scav, leaving setup through the main menu/taskbar, or beginning a new setup clears it and restores the previous ordinary map/time/transition/key selection. The old persistent `General/Enabled` setting no longer controls raid activation. F12 still configures Arena's rules, kits and submode; this change does not introduce a new submode picker.

## Safety review

Two independent read-only review passes compared the implementation with the complete decompiled game source. Findings fixed during review:

- Use the native hidden Random toggle rather than cloning an initialized UI spawner. Translate its selection to PMC without removing native listeners or changing the game's side enum.
- Preserve selection by side-controller identity when returning from the map screen; do not clear it in the screen's `Close`, which also runs on forward navigation.
- Observe actual screen changes, since returning to an existing root does not call `ShowMenuScreen`.
- Guard both map selection and the selected-location setter; group/level refreshes can otherwise restore Ground Zero after a null selection.
- Filter the Arena view independently of vanilla `Enabled`, which excludes stock Labyrinth. No shared location objects are mutated.
- Suppress the ordinary PMC health warnings while Arena is selected; vanilla Update/hover would restore them every frame.
- Require server acknowledgement before the original local-raid start request. Rejected/failed Arena negotiation returns a faulted task through vanilla's error/menu-return path, which reloads the server profile. Clear the legacy `InGameStatus.InRaid` flag because that error path never calls `OnGameEnd`.
- Keep uncertain server arming pending after lost/malformed responses. Failed cancellation blocks subsequent normal PMC and Scav starts until acknowledgement succeeds.
- Reconcile server state before the first local raid of each client process, including normal raids. This covers a server arm surviving a client restart. Only that initial reconciliation adds a request to an otherwise ordinary raid; normal gameplay/profile processing is unchanged.
- Recheck solo/non-coop eligibility, selected map and refreshed server configuration at negotiation time, after remote settings synchronization.

The server's snapshot/restore and post-raid skip implementations were not changed or weakened. No third-party mod source or assets were copied.

## Verification and limitations

`tools/NativeMenuChecks` compiles the production selection, HTTP-client and negotiation files with explicit stand-ins for the game/BepInEx/transport. Its 31 assertions include normal selection restoration, controller isolation, map/Scav rejection, lost announcement and cancellation responses, recovery, client-restart reconciliation, joining a party after selection, missing server mod (the installed SPT HTTP wrapper throws on 404), transport/500 failures and malformed ping responses. These checks do not exercise Unity rendering, actual Harmony invocation or real profile persistence.

Run:

```powershell
dotnet run --project tools/NativeMenuChecks/NativeMenuChecks.csproj -c Release
dotnet build ArenaMode.sln -c Release --nologo
powershell -NoProfile -File tools/Verify-NativeMenuDeployment.ps1
```

The runtime attempt loaded the native menu Harmony patches successfully, but the existing `spt41dev` profile remained at **Loading profile data...** before reaching the main menu. No raid was started. The client log showed no Arena initialization error. The user subsequently confirmed that this server has no GPU and cannot run the game. Further game-launch attempts were stopped; visual/gameplay acceptance requires a GPU-equipped machine. The final negotiation hardening was verified offline and is included in the final build, not in that earlier startup attempt.

Required live acceptance: restart the game with the final DLL; verify Arena/PMC/Scav selection, all three Arena maps, Factory day, map Back, taskbar/main-menu return, normal PMC/Scav maps and Lab key requirements; then compare the profile before/after a short Arena raid and confirm an ordinary raid still processes normally. Do not treat the source review and fault-injection harness as completion of this live check.
