# Native Arena selection

Implemented 2026-08-31 for the installed SPT 4.1 client. Runtime acceptance is still open; see `GATES.md`.

## Player flow

Choose **Arena** on the native PMC/Scav screen. The existing, unused third native toggle is reused, including its group, animation and layout. Arena remains a PMC raid internally because that is the post-raid path protected by the server.

The next screen offers **Factory (day), Labyrinth and The Lab**, subject to the client allowlist and server Arena configuration. Other locations and Marathon are hidden. Factory cannot switch to its unsupported night variant. Labyrinth is exposed only in this Arena view even when the ordinary game location has `Enabled=false`. Native map locks are not changed globally; the existing Arena keycard waiver remains in use.

The same map screen now contains an Arena-only mode bar for **Endless, Rounds, Gun Game and Tactical**. It writes the existing `Match/GameMode` entry, saves it, and reports the client-owned value to the server settings mirror. F12 remains an alternate editor for that entry and for detailed rules and kits, but it is no longer required to choose the mode. A complete PitFireTeam roster enters the screen in Tactical and cannot select the three solo modes; final raid negotiation independently keeps the existing strict Tactical-only check.

Returning from the map screen preserves Arena and the chosen mode. Selecting PMC or Scav, leaving setup through the main menu/taskbar, or beginning a new setup clears the Arena claim and restores the previous ordinary map/time/transition/key selection. The old persistent `General/Enabled` setting no longer controls raid activation.

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

## Follow-up: third button invisible in the user's playtest

The user's 2026-08-31 test reached the side-selection screen but showed only PMC and Scav. The supplied client log registered the Arena menu patches without an initialization error. The original implementation enabled `_randomButton.gameObject`, but source review alone had missed its serialized parent state.

Read-only inspection of the actual EFT 0.16.9.5.40743 files (SPT 4.1.3, Unity 2022.3.43f1) found:

| Object in `EscapeFromTarkov_Data/level48` | Serialized state |
| --- | --- |
| Side screen component 25165 on GameObject 364 | `_randomButton` references component 25177. |
| RandomToggleSpawner, GameObject 385 | Under `AlphaBlocker`; its own `_canvasGroup` reference is null. Vanilla `Show` hides this spawner. |
| AlphaBlocker, GameObject 368 | Inactive; direct child of the screen; its only child is RandomToggleSpawner. |
| Wrapper CanvasGroup 23550 | Alpha 0.3, interactable false, blocksRaycasts true. |
| Wrapper HoverTooltipArea 25204 | Enabled, with the obsolete message `Not available in alpha`. |

The shared toggle prefab in `sharedassets44.assets` (component 8925, GameObject 1051) is active and is the same prefab used by PMC and Scav. Activating only RandomToggleSpawner can therefore never show the button while AlphaBlocker remains inactive. Merely activating AlphaBlocker would still leave the button dim and unclickable.

`ArenaSideSelectionView` now validates that the wrapper belongs exclusively to Arena, enables both wrapper and spawner, sets its CanvasGroup to full opacity and the current solo availability, and disables the obsolete tooltip. It retains native placement, ToggleGroup and listeners. It never unlocks ancestors or the Scav cooldown group. Unexpected hierarchy fails with a logged error and hides only Arena; it does not modify the ordinary sides. No scene asset is modified or distributed. Each menu opening logs `Arena side button: active=..., interactable=..., wrapper=...` for the next live test.

An independent adversarial review found no remaining correction to make in the helper or its integration. Release build and installed-file hash verification succeeded; server tests (31), selection/negotiation assertions (31) and announcer assertions (17) passed. The installed client SHA-256 is `FDBDD0F6B0ED90EEC4B9C50497CEB05DBC7E70D0C9045B8CA458684E69A51550`; the server is unchanged at `D1F6603F7CB15CF3FCD578CF8F24C0A83064F8E328F01637084077ED896904F5`.

`dotnet run --project tools/NativeSideViewChecks -c Release` also passes 29 focused checks. It compiles the real view helper with Unity/EFT stand-ins and a fixture based on the inspected scene. Coverage includes the inactive parent, disabled CanvasGroup, obsolete tooltip, solo availability, repeated opening, unchanged PMC/Scav controls and safe rejection of shared or unknown hierarchy. These checks verify the helper's state changes, not actual Unity rendering or native event delivery.

Mutation verification used temporary source copies, never the deployed source: removing wrapper activation and canvas unlocking fails the visibility check; retaining activation but removing canvas unlocking fails the opacity/input check. The production source hash was unchanged, and the final normal run passed again.

Visual acceptance remains pending a fresh user test with this DLL. No game was launched on the GPU-less server during this diagnosis.

## Follow-up: Arena map screen empty

The next user test confirmed the Arena side button is visible, but no maps appeared on the following screen. The updated log confirms a visible/interactable side button and successful Arena configuration requests. The installed client allowlist and server configuration enable Factory day, Labyrinth and The Lab.

Actual scene inspection found the cause in `level48`: MatchMakerSelectionLocationScreen component 25068 on GameObject 305 has **both** `_locationButtonsContainer` and `_locationPathsContainer` pointing at Transform 15523 / GameObject 269 (`Paths Container`). The former postfix disabled that shared container to remove transit lines, also hiding every map button. The path template (component 25098 / GameObject 276) is a separate sibling outside the shared container.

`ArenaLocationSelectionView` now hides only individual generated `LocationPath` GameObjects and Marathon. It preserves each original `activeSelf` value, restores before another `ShowInternal` and before native `Close`, and skips views that Unity has already destroyed. The shared container and map buttons are never toggled. The existing map filter, selection guards and both profile protection layers are unchanged. A diagnostic logs native button count, visible-in-hierarchy count and whether the two containers are shared.

Independent adversarial review found no remaining issue in this helper or its integration. The Release solution builds with four existing CS8632 warnings and no errors. The 31 server tests, 31 menu/negotiation assertions, 29 side-button assertions and 17 announcer assertions pass again. Build and installed client SHA-256 match at `4454634119025A2903E0757B608DF43C3E69D6224A9B08960C8E58A760C6517A`; the server hash is unchanged. The installed client was also decompiled to verify that it contains the per-path fix.

`dotnet run --project tools/NativeMapViewChecks -c Release` passes 40 additional checks against the linked production helper with Unity/EFT stand-ins. They cover the shared-container regression, unchanged map buttons/template/ancestors, exact restoration of active and inactive views, reopening, new or destroyed path views, duplicate references and separate-container layouts. A temporary source-copy mutant that hides the entire container fails the map-visibility assertion; the production source hash stays unchanged and the final normal run passes. The test code was inspected independently of its author. These checks do not execute native map filtering, click listeners or Unity rendering.

The user's side-button confirmation is a passed visual check for that button only. The corrected map screen and subsequent raid/profile flow still need a game restart and live retest; no game was launched on this server.

Subsequent user confirmation: the corrected map selection works on the gaming PC.
Both reported visibility regressions are therefore confirmed fixed by the user.
This does not close the broader navigation, ordinary-raid and profile-integrity
acceptance checks above.

## Follow-up: native Arena mode picker

The mode picker is created only from the Arena-gated location-screen postfix and
is destroyed before every screen rebuild or close. Its fixed bottom-center strip
uses the native navigation-button font and the free space recorded in the installed
`level48` layout, above the Back/Next row and below the closed Arena time panel.
Ordinary PMC/Scav screens never create it. Failure to construct the optional view
is logged and leaves the native map screen usable.

`tools/NativeModeSelectionChecks` links the production catalog and Unity view. Its
checks cover all four registry ids and labels, selection propagation, descriptions,
font fallback, exact placement, re-entry without duplicates, teardown, rejected
config writes and the Tactical-only PitFireTeam state. `tools/PitFireTeamChecks`
also proves that mode-pending side-screen admission relaxes only the old mode value;
invalid rosters, Fika/co-op and the final non-Tactical start remain rejected.

The offline checks and Release build cannot prove final rendering or pointer input.
That visual acceptance remains for the GPU-equipped client.
