# Native TDM match rules checks

Run `pwsh -NoProfile -File tools/TdmMatchMenuChecks/Run-Checks.ps1` from the repo.
This standalone net10.0 executable links the unchanged production settings,
selection state, mode view and child rules view against external Unity/TMP,
BepInEx and session doubles. It never imports the mod's deploy targets.

Checks cover the actual 50-kill/600-second defaults and F12 ranges, accepted and
rejected persistence, independent zero limits, hidden non-TDM/PFT controls,
live armed/active guards, exact custom F12 labels with no invented selection,
same-mode refresh, real button callbacks, the shared mode panel footprint
(base 360x530, TDM/Domination 360x584, Deathmatch/Zombie 360x596), and close/reopen listener ownership. Thirty-six deliberate
source faults must compile and fail their corresponding behavioral assertions.
All compiler mutations and output evidence go to ignored repo `artifacts/`;
production files are not rewritten. The final run restores production inputs.

The mutators extension also links the real pickup settings and inline view/adapter.
It covers all twenty feature switches, shared F12 values, movement
master and mode/dependency restrictions, arm/active locks before UI refresh,
manual saving, unchanged laser brightness, inline state colors, close and stale callbacks.
Killcam lives in this always-visible right column and keeps its existing per-mode semantics.
The panel reserves the same 320-pixel image slot in every mode; map/preview
selection no longer controls its vertical position.
It has no legend line and no footer note: ten rows in each column run from top 52
to 8 px above the 472-pixel panel bottom, with 22px buttons and 19.2px description
cells. Shared row edges allow only 0.001px of floating-point rounding. The added
Friendly Fire control uses independent Team DM/Tactical/Zombie preferences,
defaults on/on/off, and is unavailable for solo modes. Tests cover defaults,
real callback persistence, cross-mode isolation, armed/active locks, and stale
callbacks. Three compiled faults specifically violate those admission/default
rules. Runtime freeze and damage behavior belong to FriendlyFireRuntimeChecks.

Auto Quickslots and Instant Search write the original `Kits/AutoQuickSlots` and
`Loot/InstantSearch` preferences. Arena HUD writes the separate
`HUD/CombatElements` preference; the existing `HUD/Enabled` master stays intact
and makes the mutator unavailable when disabled. Checks exercise these controls
in all six modes and solo/PFT variants, preserve individual HUD preferences,
and cover manual saving, independent config reload, F12 refresh, active/armed
locks and retired callbacks. Five new compiled faults target the three bindings,
the HUD master restriction and accidental writes to an individual hitmarker choice.

All six modes display a two-line description, then the common RESPAWN and
EQUIPMENT facts. The objective is explained once in the description. Deathmatch and Team DM keep those facts;
their compact choice bands follow below. Zombie's equipment selector replaces
only the equipment value and retains the common label. Descriptions follow
FFA/all-vs-player, solo/PFT and own-gear state; respawn text follows playable
Killcam settings without promising a respawn in solo Zombies. Zero kill/time
limits use `Off` consistently in current-value labels and buttons.

The mode panel ends at reference Y=662 (TDM/Domination Y=716, Deathmatch/Zombie Y=728); the area panel
begins twelve pixels below. No rightward/downward extension over native controls
is added. The harness checks visibility, fixed fact positions, child bounds,
stale callbacks, immediate FFA description changes and all dynamic variants.
Native `BaseDropDownBox.ToggleMenu` uses the same list-item clone technique and
the existing `HoverTooltipArea.SetMessageText/SetUnlockStatus` contract is reused.

These checks exercise ownership and layout arithmetic, not Unity rendering,
native click hit-testing, BepInEx file IO, or the runtime's frozen match policy.
The text-length assertions are copy budgets, not font metric measurements.

Domination adds a sixth playable row while removing its old inert preview, so
the seven-row footprint stays unchanged. CTF remains disabled. The real view
shows Domination's fixed objective limits and faction choice, hides TDM's kill
controls, shares the independent team preferences and rejects direct selection
callbacks if the objective companion capability is unavailable. A compiled
mutation removes this capability lock and must fail the native view assertion.

The dedicated Molotov template constant is linked from Shared, alongside the
production fire policy that uses it. PFT Zombie description checks follow the
October 15 entry-gear refill: the surviving PMC is included, while team-wipe
defeat and fallen-member restoration remain separately asserted. Reverting
only this description to the old companions-only wording must fail the check.
The description is 76 characters, within the unchanged 80-character copy budget;
the earlier 83-character refill wording exceeded it. This budget is not native
font-layout acceptance.
The extra-Molotov/G-priority summary likewise stays within the unchanged
36-character mutator budget; "extra" preserves the distinction from replacing
the existing grenades.

GPU acceptance must still inspect the largest Customs area/team-size layout,
switch TDM/other modes/PFT, and verify custom F12 values (for example 77 kills,
75 seconds). The runtime owner separately verifies that mid-raid F12 changes
affect the next match only. No server schema or settings-report change is made.
