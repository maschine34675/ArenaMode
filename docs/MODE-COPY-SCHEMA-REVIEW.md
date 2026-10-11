# Mode descriptions and visible rule schema

2026-10-09. Presentation only; no match rules, settings, persistence, admission or
ordinary-raid behavior changed. Native visual acceptance belongs to the delivery
report, not to the offline checks recorded here.

## Problem and common layout

The catalog already limited text length, but Deathmatch and Team DM hid all three
facts behind their settings. Team DM additionally hard-coded `Instant respawn`
even with a selected, available Killcam. Allowed native dropdown rows have no
hover description, so the old catalog descriptions were not actually visible.

Every mode now shows the same sequence:

1. A short, wrapped description in a dedicated two-line block.
2. `OBJECTIVE`, `RESPAWN`, `EQUIPMENT`, at the same position and typography.
3. Any existing mode-specific round, kill, time and opponent choices below.

The equipment choice in solo Zombies replaces only the EQUIPMENT value; its
label stays aligned with every other mode. It offers the existing Arena kits /
Own gear options, without another setting or a duplicated heading. Protected
Tactical/Zombies still use automatic PMC entry gear and hide those controls.

The mode panel retains its existing 360 x 530 footprint, or 360 x 596 in
Deathmatch. Five mode rows use 34-pixel buttons with a 36-pixel pitch. Description
top/height are 284/42; the common facts sit at 330, 360 and 390 with 30-pixel
heights. Rule controls start at 432: four 39-pixel bands for Deathmatch, two for
Team DM. The area panel therefore stays in its accepted position; this change
does not move the right-hand map preview, mutators or native bottom controls.

## Mode and variant truth

| Mode | Objective | Respawn | Equipment | Description explains |
|---|---|---|---|---|
| Deathmatch | Most kills | Instant / After killcam | Arena kits | FFA or all opponents against the player; limits end rounds |
| Gun Game | Kill to advance | Instant / After killcam | Weapon ladder | Each kill advances the weapon; the ladder repeats |
| Tactical solo | Round elimination | Next round | Buy / retain kit | Eliminate the enemy squad; PFT + four allies enables 5v5 |
| Tactical PFT | Round elimination | Next round | PMC entry gear | Eliminate the enemy squad; fallen allies return next round |
| Team DM | Most team kills | Instant / After killcam | Arena kits | USEC vs BEAR, ranked by team kills |
| Zombies solo | Survive the waves | None | Arena kits / Own gear | First death ends the match |
| Zombies PFT | Survive the waves | Next wave | PMC entry gear | Fallen allies return after cleared waves; a team wipe ends the match |

The Tactical row continues to show its actual `1vN` squad-size setting or PFT
`5v5`; the Zombie row identifies the PFT squad variant. The protected Zombie
description deliberately does not say every squad member resets: a living PMC
keeps their body/current condition at a wave transition.

`After killcam` appears only for an instant-respawn mode with a selected replay
and an available replay provider. Solo Zombies always displays `None`, including
with Killcam enabled. Existing next-round/next-wave holds remain unchanged.

Deathmatch's opponent control refreshes the description immediately on accepted
selection, as well as on regular synchronization. Zero kill/time limits now use
`Off` in both current-value labels and option buttons; endless round count stays
`Endless`, because that setting means repeating rounds, not disabling them.
The invalid finite-round/no-limit combination remains disabled by its existing
state policy. Custom F12 limit values still display exactly without inventing a
selected preset.

## Offline verification and boundaries

- `dotnet run --project tools/NativeModeSelectionChecks -c Release`: production
  catalog and selection state, 157 assertions.
- `tools/NativeModeSelectionChecks/Verify-CatalogSchema.ps1`: five independently
  compiled catalog faults rejected, then production rerun and hash verification.
- `dotnet run --project tools/TdmMatchMenuChecks -c Release`: actual production
  view/state/settings against external Unity/TMP/BepInEx doubles, 1324 assertions.
  Covers visibility (including DM/TDM), common positions, separate description
  bounds, control containment, inline equipment bounds, FFA text updates,
  zero-limit wording, custom values, all loadout/team/replay variants, armed/active
  locks and retired callbacks.
- `tools/TdmMatchMenuChecks/Run-Checks.ps1`: baseline and final production runs
  plus 24 compiled faults, including hidden common facts, invisible/unwrapped
  descriptions and misreported TDM Killcam behavior. Evidence stays in ignored
  `artifacts/tdm-match-menu-checks/`.

The harness measures text values, visibility and rectangle arithmetic, not TMP
glyph metrics, Unity rendering or actual pointer hit testing. The short-string
limits are a copy budget. Native acceptance should inspect each mode at 1080,
especially the four-choice Customs area panel under Team DM, the longest
description (PFT Zombies), both equipment buttons, and selected/off/unavailable
Killcam. No raid needs to be started merely to verify these presentation changes.

## Review passes

Implementation included the common visible schema rather than only catalog
copy. Domain reread checked Gun Game's repeating ladder, Tactical squad/gear and
Zombie cleared-wave/first-death behavior against their runtime owners. Defect
hunting added visibility controls and delayed-respawn regressions, then removed
the two-pixel overlap between adjacent fact rectangles. Polish retained the
accepted outer footprint, removed duplicate notes and aligned zero-limit terms.
Final native font/layout validation and independent review are recorded by the
parent delivery, separately from this offline evidence.
