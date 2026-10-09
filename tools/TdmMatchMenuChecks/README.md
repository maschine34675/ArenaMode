# Native TDM match rules checks

Run `pwsh -NoProfile -File tools/TdmMatchMenuChecks/Run-Checks.ps1` from the repo.
This standalone net10.0 executable links the unchanged production settings,
selection state, mode view and child rules view against external Unity/TMP,
BepInEx and session doubles. It never imports the mod's deploy targets.

Checks cover the actual 50-kill/600-second defaults and F12 ranges, accepted and
rejected persistence, independent zero limits, hidden non-TDM/PFT controls,
live armed/active guards, exact custom F12 labels with no invented selection,
same-mode refresh, real button callbacks, the unchanged 360x530 panel footprint
(Deathmatch 360x596), and close/reopen listener ownership. Twenty-seven deliberate
source faults must compile and fail their corresponding behavioral assertions.
All compiler mutations and output evidence go to ignored repo `artifacts/`;
production files are not rewritten. The final run restores production inputs.

The mutators extension also links the real pickup settings and inline view/adapter.
It covers all seventeen feature switches, shared F12 values, movement
master and mode/dependency restrictions, arm/active locks before UI refresh,
manual saving, unchanged laser brightness, inline state colors, close and stale callbacks.
Killcam lives in this always-visible right column and keeps its existing per-mode semantics.
The panel reserves the same 320-pixel image slot in every mode; map/preview
selection no longer controls its vertical position.
It has no legend line and no footer note: nine rows in the first column and eight
in the second run from top 52 to 8 px above the 472-pixel panel bottom. The added
Friendly Fire control uses independent Team DM/Tactical/Zombie preferences,
defaults on/on/off, and is unavailable for solo modes. Tests cover defaults,
real callback persistence, cross-mode isolation, armed/active locks, and stale
callbacks. Three compiled faults specifically violate those admission/default
rules. Runtime freeze and damage behavior belong to FriendlyFireRuntimeChecks.

All five modes now display a two-line description, then the common OBJECTIVE,
RESPAWN and EQUIPMENT facts. Deathmatch and Team DM no longer hide those facts;
their compact choice bands follow below. Zombie's equipment selector replaces
only the equipment value and retains the common label. Descriptions follow
FFA/all-vs-player, solo/PFT and own-gear state; respawn text follows playable
Killcam settings without promising a respawn in solo Zombies. Zero kill/time
limits use `Off` consistently in current-value labels and buttons.

The mode panel still ends at reference Y=698 (Deathmatch Y=764); the area panel
begins twelve pixels below. No rightward/downward extension over native controls
is added. The harness checks visibility, fixed fact positions, child bounds,
stale callbacks, immediate FFA description changes and all dynamic variants.
Native `BaseDropDownBox.ToggleMenu` uses the same list-item clone technique and
the existing `HoverTooltipArea.SetMessageText/SetUnlockStatus` contract is reused.

These checks exercise ownership and layout arithmetic, not Unity rendering,
native click hit-testing, BepInEx file IO, or the runtime's frozen match policy.
The text-length assertions are copy budgets, not font metric measurements.
GPU acceptance must still inspect the largest Customs area/team-size layout,
switch TDM/other modes/PFT, and verify custom F12 values (for example 77 kills,
75 seconds). The runtime owner separately verifies that mid-raid F12 changes
affect the next match only. No server schema or settings-report change is made.
