# Native TDM match rules checks

Run `pwsh -NoProfile -File tools/TdmMatchMenuChecks/Run-Checks.ps1` from the repo.
This standalone net10.0 executable links the unchanged production settings,
selection state, mode view and child rules view against external Unity/TMP,
BepInEx and session doubles. It never imports the mod's deploy targets.

Checks cover the actual 50-kill/600-second defaults and F12 ranges, accepted and
rejected persistence, independent zero limits, hidden non-TDM/PFT controls,
live armed/active guards, exact custom F12 labels with no invented selection,
same-mode refresh, real button callbacks, preserved non-TDM layout, the existing
360x488 panel footprint, and close/reopen listener ownership. Fourteen deliberate
source faults must compile and fail their corresponding behavioral assertions.
All compiler mutations and output evidence go to ignored repo `artifacts/`;
production files are not rewritten. The final run restores production inputs.

The mutators extension also links the real pickup settings and inline view/adapter.
It covers all eight existing feature switches, shared F12 values, movement
master and mode/dependency restrictions, arm/active locks before UI refresh,
manual saving, unchanged laser brightness, inline state colors, close and stale callbacks.
Killcam lives in this always-visible right column and keeps its existing per-mode semantics.
The panel reserves the same 320-pixel image slot in every mode; map/preview
selection no longer controls its vertical position.

The rules occupy the old fact-row region only for independent Team DM. The mode
panel still ends at reference Y=656; the untouched area panel begins at Y=668.
No rightward or downward extension over native map/bottom controls is added.
Native `BaseDropDownBox.ToggleMenu` uses the same list-item clone technique and
the existing `HoverTooltipArea.SetMessageText/SetUnlockStatus` contract is reused.

These checks exercise ownership and layout arithmetic, not Unity rendering,
native click hit-testing, BepInEx file IO, or the runtime's frozen match policy.
GPU acceptance must still inspect the largest Customs area/team-size layout,
switch TDM/other modes/PFT, and verify custom F12 values (for example 77 kills,
75 seconds). The runtime owner separately verifies that mid-raid F12 changes
affect the next match only. No server schema or settings-report change is made.
