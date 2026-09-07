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
