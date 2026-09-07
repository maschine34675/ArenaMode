# Arena mode sidebar — 2026-09-07

The map screen exposes all five Arena modes in a left-side list. Three short
label/value rows replace the selected mode's paragraph: rules, respawn and
equipment. The panel uses the existing native canvas and leaves vertical room
for future implemented match options. No player-count/difficulty placeholders
or new gameplay settings are introduced.

This supersedes the dropdown presentation documented in `NATIVE-MENU-REVIEW.md`.
The stable selection state and settings writer remain unchanged.

## Native implementation

- Only `RaidSettingsWindow._aiDifficultyDropdown._elementTemplate` is cloned.
  This Unity `Button` is the exact prefab used by `BaseDropDownBox.ToggleMenu`.
  The owned rows retain native graphics, typography and hover behavior, with a
  small persistent selection marker. Clicks use the native dropdown-select sound.
- Each clone receives a fresh click event before activation. No whole dropdown,
  raid-settings window, toggle spawner or native event owner is cloned; no popup
  is reparented into `ContextMenuArea`.
- The existing `ArenaModeSelectionState` still validates indices/PFT eligibility
  and changes the accepted selection only after `Settings.TrySelectGameMode`
  succeeds. Rejected/throwing writes cannot move the persistent marker or facts.
- PFT disables the other four buttons and their native CanvasGroups; tooltips
  explain the Tactical restriction. The existing state guard independently
  rejects forbidden selections. The equipment fact becomes `PMC entry gear`.
- Close clears the selection/callbacks, hides and destroys the owned panel.
  Individual cleanup calls are protected so they cannot stop native navigation.
  Native `HoverTooltipArea.OnDisable` closes a hovered tooltip on panel teardown.
- Native map positions, click targets and right-hand location information remain
  unchanged. The previous Factory-conditions lift is removed; time selection
  uses its original native position, with no extra `LocationConditionsPanel.Set`.

Sources checked against this host's `../_decompiled`: `EFT.UI/BaseDropDownBox.cs`,
`DropDownBox.cs`, `HoverTooltipArea.cs`, `LocationButton.cs` and
`EFT.UI.Matchmaker/LocationConditionsPanel.cs`. No external mod code or assets
were copied. The prior native-control reference research still applies.

## Compact copy

| Mode | Rules | Respawn | Equipment |
| --- | --- | --- | --- |
| Endless | Deathmatch | Instant | Arena kits |
| Rounds | Kill target / time limit | Instant | Arena kits |
| Gun Game | Weapon progression | Instant | Weapon ladder |
| Tactical | Elimination rounds | Next round | Buy / retain kit |
| Team DM | USEC vs BEAR | Instant | Arena kits |
| PFT Tactical | Elimination rounds | Next round | PMC entry gear |

`Arena kits` covers both RandomKit and fixed SelectedKit. Solo Tactical retains
the carried Arena kit; its ordinary buy path does not imply importing arbitrary
PMC equipment. Gun Game uses the weapon ladder, with the existing kit fallback
if the ladder is unavailable. Rounds ends each round at a local kill target or
time limit, so the older `Score-limited` catalog description was corrected.
No new overall winner/participant-count promises are made.

Contracts were checked in `KitCatalog`, `RoundsMode`, `GunGameMode`,
`TacticalMode`, `TeamDeathmatchMode` and `EndlessDeathmatchMode`.

## Validation

- 123 existing production-linked mode-selection/catalog checks pass, including
  initial repair, accepted/rejected/throwing writes and PFT restrictions. These
  checks do not render the new native buttons or validate their geometry.
- Independent adversarial review found no actionable functional issue in native
  row ownership, callbacks, saved-state markers, PFT gating or tooltip cleanup.
- GPU observations and their exact scope follow below. No raid was started;
  the user performs actual raid tests.

## GPU menu run and installed identity

Reviewed on `WIN-Q470OL2A14A` at `C:\SPT41`, on branch
`feature/arena-native-menu-options`, starting from
`70a65c04257e5513072449479e184477899b9d68`. Captures on 2026-09-07 are
**1920 x 945** with native UI scale **0.875** (logical height 1080).
These window captures have a ratio of approximately 2.03:1; they are not a true
16:9 visual test. Client log times are local **MESZ / UTC+02:00**, while the
screenshot manifest records UTC timestamps.

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client, Release and installed copy | `67B55E0A532BFACE875AF556D6760B1AD454698AB9FE4B4125A8A99350390986` |
| ArenaMode.Server, unchanged | `FCE6C394DF9184E81FE6D43B0D236380DE71E3BB110708497E2471D395B2DBE8` |
| Redeploy.Core, unchanged | `F923B0D709908F75969B3101E9F8290CD2BB06DF457F64B1FFF8876A7BBFEA00` |

The client build succeeded with zero errors and four existing `CS8632` warnings
in `ArenaMode.Shared/ClientSettingsModels.cs`. The production-linked selection
check output records 123 passing assertions. The saved final client log contains
no error-level entries or Arena warnings.

Observed menu results:

- All five native mode buttons were visible together and selectable. Each mode
  showed its three compact facts without text clipping. The selected marker
  remained visible after the pointer moved away (screenshots 02-06).
- Factory day and night were selectable; the time controls stayed in their
  original native position and remained accessible (02 and 07).
- Labs and Labyrinth remained clickable and displayed their native location
  information alongside the sidebar (08-09). The log recorded three visible
  map buttons with the shared container, both before and after reopening.
- Back removed the sidebar. Selecting PMC then showed the ordinary map screen
  without an Arena sidebar (10-11).
- Entering Arena again produced exactly one sidebar with Tactical still selected
  (12). The final reopened map view is logged at **14:41:55.397**; the associated
  capture is **14:41:56.121** local time.

**Not GPU-tested:** the disabled four-row PFT presentation and its hover tooltip,
nor a true 16:9 resolution. PFT selection restrictions are covered by the 123
state/catalog checks and source review; those do not establish visual behavior.
No claims about scoring, respawn, match equipment or raid integration follow
from this menu-only pass.

Private artifacts on the GPU host are under
`C:\Users\Administrator\Documents\Codex\2026-09-07\lies-agents-md-und-handoff-md`:

- `outputs/Arena-Modusauswahl/Vergleich.html` and its `screenshots/` folder contain
  the comparison gallery and 12 original images. `01-vorher.png` is a historical
  dropdown baseline; only 02-12 show this sidebar build. `manifest.json` records
  the image provenance, capture times, dimensions and build identities.
- `work/arena-mode-sidebar/final-client.log`, `build.log` and
  `selection-checks.log` retain the final runtime, build and assertion outputs.

The repository contains this compact report, not profiles or raw runtime logs.
