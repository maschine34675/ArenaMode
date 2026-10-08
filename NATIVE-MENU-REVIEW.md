# Arena native menu settings — 2026-09-07

Historical review of the first native dropdown presentation. The later visible
sidebar and compact mode facts are documented in [MODE-SIDEBAR-REVIEW.md](MODE-SIDEBAR-REVIEW.md).

The map screen now uses an EFT raid-settings dropdown in an Arena settings group
instead of five flat, orange-highlighted mode buttons. The selected mode has a
larger description underneath. The native popup opens upward so Next and Back
remain visible. The side-selection description now correctly names five modes.

The later character-choice row improvement is recorded separately in
`CHARACTER-SELECTION-REVIEW.md`; this document records the map-settings work.

## Implementation and boundaries

- Clone only the closed, owned `RaidSettingsWindow._aiDifficultyDropdown`
  subtree through `MenuUI.MatchmakerOfflineRaidScreen`. Preserve EFT's sprites,
  font/material, hover behavior and selection sounds. Never show/apply the
  original raid-settings window or alter shared visual assets.
- Use the existing screen canvas and its scaling. The 620 x 126 settings group
  sits above navigation; Factory conditions move with it and restore their
  original position on close. The popup is configured before native initialization.
- Native dropdowns temporarily reparent their open panel into ContextMenuArea.
  Unsubscribe the Arena callback and close the dropdown before destroying its
  group so no popup is left on another screen.
- The production-linked selection state owns stable ids, accepted writes and
  Tactical-only PFT restrictions. Rejected or throwing writes restore the visible
  saved choice. No raid-start calls, protocol changes or additional settings.

Player count and difficulty belong in this group when their behavior is
implemented. Do not add placeholder controls that appear to change a match.
Counts are currently mode-specific (Team DM, solo Tactical, fixed PFT 5v5);
there is no uniform participant-count or easy/normal/hard setting with consistent
Arena semantics. Their future implementation needs an explicit per-match policy.

## Sources and licensing

Native contracts were checked in `../_decompiled/EFT.UI/DropDownBox.cs`,
`BaseDropDownBox.cs`, `MenuUI.cs`, `Diz.Binding/BindableEvent.cs` and
`EFT.UI.Matchmaker/RaidSettingsWindow.cs`.

Local technique references:

- `../_reference/UIFixes-main/src/Patches/FleaPrevSearchPatches.cs` (MIT):
  native control reuse and subscription lifetime.
- `../_reference/SPT-PitFireTeam-beta-0.10.0/client/Components/SquadControlMenuUi.Settings.cs`
  and `Patches/OtherPlayerProfileScreenPatch.ProfileOptions.cs`: native option
  rows and dropdowns, inspected only as technique under the project license rules.

No third-party mod code, DLLs, UI bundles or new dependency were copied.

## Validation

- Client Release build and deployment succeeded: zero errors, four existing
  CS8632 warnings in the unchanged shared settings model. The referenced
  Redeploy.Core was also built/deployed from its unchanged source.
- 123 production-linked catalog/selection-state assertions passed. Removing the
  selection guard from an isolated source copy failed the intended PFT
  unauthorized-selection assertion. No mutant was deployed.
- Independent adversarial review found no actionable functional issue in the
  state, native lifecycle, popup ownership or selection callback contracts.
- Native menu run on the GPU host: all five modes and descriptions, Factory
  day/night, Labs, Labyrinth, Back with the dropdown open, return to PMC and
  repeated Arena entry passed. No raid was started.
- A second game launch verified the final upward popup, Team DM to Tactical
  selection and Back with the list open. Next and Back remained unobstructed;
  no popup remained on character selection. The record contains 19 original
  1920 x 945 captures: 01-15 from the first menu run, 16-19 from the final build.
- Neither run logged an Arena warning/error or a client error. The installed
  client hash matches the Release output; game, launcher and owned test server
  were stopped after inspection. Syncthing remains enabled under the user's
  disposable-test-data policy.
- Actual PFT roster rendering, other resolutions and gameplay remain outside
  this menu check; the user performs raid tests.

Private visual report and original screenshots:
`C:\Users\Administrator\Documents\Codex\2026-09-07\lies-agents-md-und-handoff-md\outputs\Arena-UI-Ueberarbeitung\`.
Build and run logs are in the sibling `work\arena-native-menu\` directory.

## 2026-10-08: "ARENA ZONE" instead of "PVE ZONE"

Offline-verified only (harness plus source pins); Harmony binding and the
rendered label were not seen in the game.

The SPT locale key `ESessionMode/Pve` reads "PVE ZONE". Two native paths write it
(decompile `D:\SPT41\Development\_decompiled`, EFT 0.16.9.5):

- `EFT.UI.Screens/MatchmakerEftScreen.cs:17`, the abstract generic base of every
  matchmaker screen: `Show(controller)` sets
  `_subCaption.text = controller.SessionMode.LocalizedEnum()` (public field
  `_subCaption`, line 11). `EftMatchmakerScreenController.cs:20-22` exposes only
  `MatchmakerPlayersController` and `SessionMode`, no `RaidSettings`; each screen's
  own controller does (`RaidSettings` at `MatchMakerSelectionLocationScreen.cs:23`,
  `MatchMakerSideSelectionScreen.cs:34`, `MatchMakerAcceptScreen.cs:41`).
- `EFT.UI.Matchmaker/MatchmakerTimeHasCome.cs:219`, the "DEPLOYING TO LOCATION"
  screen, inside `Show(IEftSession, RaidSettings, MatchmakerPlayersController)`
  (line 209).

`ArenaSessionModeCaptionPatch` (new, enabled from `Plugin` right after
`ArenaLocationSelectionPatch`) overwrites the label with the pure
`ArenaSessionModeCaption.Resolve(true, ...)` = "ARENA ZONE" in postfixes, so the
native text is written first and replaced after. The generic base is deliberately
NOT patched (AGENTS.md: a patch on a method declared on a generic type is a
trap); every target is a non-generic override or method of the concrete screen:

| Screen | Patched method (decompile) | Gate |
|---|---|---|
| Select location | `MatchMakerSelectionLocationScreen.ShowInternal` (`:409`, finishes `Show(controller)` `:391`) | `ArenaMenuSelection.IsSelectedFor(raidSettings)` |
| Accept (every Arena flow while PitFireTeam is installed, solo or team; every flow with `SkipPreRaidScreens` off) | `MatchMakerAcceptScreen.Show(MatchmakerAcceptScreenController)` (`:321`) | `ArenaMenuSelection.IsSelectedFor(controller.RaidSettings)` |
| Deploying to location | `MatchmakerTimeHasCome.Show(TimeHasComeScreenController)` (`:204`, calls the overload at `:209`) | `!controller.IsReconnecting` and (`IsSelectedFor(controller.RaidSettings)` or `ArenaSession.ArmedForNextRaid`) |
| Side selection | `ArenaSideSelectionPatch`: `ShowPatch.Postfix` and `SelectPatch.Postfix` (`MatchMakerSideSelectionScreen.Show` `:258`, `SetSelectedSide` `:448`) | `ArenaMenuSelection.IsSelected`, behind the PitFireTeam squad-screen early return |

The deploying screen patches the controller override instead of the three-argument
overload, because only the controller tells a reconnect to a
running ordinary raid (`ReconnectTimeHasComeScreenController`, `:20`) apart, and
`ArenaPreviewFlowPatch.LoadingShowPatch` already patches that same method.

Which screens an Arena flow shows depends on `SkipPreRaidScreensPatch` (config
`General / SkipPreRaidScreens`, default on). With it on, a solo Arena raid goes
from the map straight to the loading screen, past Game Settings
(`MatchmakerOfflineRaidScreen`), Insurance (`MatchmakerInsuranceScreen`) and the
accept screen, but its confirmation half hands back to the native path whenever
PitFireTeam is installed (`SkipConfirmationPatch`: `PitFireTeamCompat.IsInstalled`,
solo or team), so with PitFireTeam the accept screen IS part of every Arena flow
and is patched. Known gap, deliberately not patched: with `SkipPreRaidScreens` off
Game Settings and Insurance (and the accept screen without PitFireTeam) also
appear inside the Arena flow, and `MatchmakerOfflineRaidScreen`,
`MatchmakerInsuranceScreen`, `MatchmakerMapPointsScreen` and the key screen keep
the native "PVE ZONE" there. All of them derive from `MatchmakerEftScreen<,>` and
could take the same `IsSelectedFor(controller.RaidSettings)`-gated postfix if that
mixed caption is ever unwanted; the requested screens are the side, location,
deploying and (with PitFireTeam) accept screens. On the side screen the ARENA
toggle is the native third (Random) toggle: switching it on shows "ARENA ZONE",
any other side restores the native label, and a restored selection (back from the
map) shows it again.

Every patch is gated on the Arena flow, never throws (`Apply` catches and leaves
the native text) and touches nothing otherwise, so normal PMC/SCAV flows are
byte-identical. The native text a patch replaced is remembered per label
(`ConditionalWeakTable`, inside the Unity-free `ArenaSessionModeCaptionLedger`) so
only a label this patch overwrote is ever restored.
Offline: `tools/NativeMenuChecks` (`CaptionChecks.cs`) compiles and EXECUTES the
pure `Resolve` and the whole set/restore ledger against a stand-in label (an
ordinary flow is never touched, Arena writes once, off restores exactly the native
text in any language, a fresh native rewrite is not clobbered, re-arming after one
remembers the new text, labels are independent), and pins by source text the
gates of the four Harmony targets, each as ONE complete `if (condition)
Apply(...)` statement so a negated group or a changed operator is rejected, plus
the direct-write ban and the Plugin wiring. `Verify-SessionModeCaption.ps1` holds
21 mutants (including the negated and operator-flipped deploying gate, the
flipped ledger branch, a dropped remember step and a missing forget), each
rejected by its named assertion. Not covered: Harmony resolution of the four
targets (all four are already patched elsewhere in the mod with the same
`AccessTools` calls), the reconnect case, and what the label looks like.
