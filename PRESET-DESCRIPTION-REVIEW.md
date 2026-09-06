# Preset description regression

## Cause

The front-armor change in `9d3bb6c` asked every equipped vest for its native
`IArmorComponentTemplate.ArmorPlateColliders` before checking whether the vest
itself had any integral armor. Native `VestTemplate` iterates its optional
`armorPlateColliders` backing array without a null check. The corresponding
`ArmoredEquipmentTemplate` getter has a null-to-zero guard instead.

All 20 shipped kit rigs use the native VestTemplate category
`5448e5284bdc2dcb718b4567`, have armorClass 0 and omit that array. Consequently,
the new summary computation threw, its outer catch returned an empty string,
and `For()` cached that empty result for the kit. The buy panel still calls the
summary and displays nonempty text; its layout was not the regression.

The earlier armor harness projected every template into a null-safe
ArmorTemplate. This erased the concrete VestTemplate behavior and hid the bug.
The installed Assembly-CSharp getter IL and local JsonTypes template discriminator
now independently validate that boundary. A briefly synchronized old client log
also showed at least ten matching `Kit summary ... failed` NRE messages around
20:57:53.712–.714. The log was subsequently replaced by another startup, so no
complete historical error count is claimed.

## Fix

- Check a vest's positive integral class and actual front thorax coverage first.
- Read VestTemplate's optional raw plate array safely, treating null as no
  plate mask without changing the shared database. This also supports a legacy
  integral soft vest with positive armor class and no plate array.
- Preserve the installed front-part AND ArmorSlot coverage calculation. Side and
  rear plates still cannot inflate the advertised front class.
- Isolate unexpected armor metadata errors to the armor section. Weapon, helmet,
  headset and spare-magazine text remain visible, with one diagnostic per cached
  kit until the existing catalog reset. No fake armor class is substituted.

The UI, kit items, economy, profiles and normal raids are unchanged.

## Verification

The corrected boundary double now represents VestTemplate separately and
deliberately throws for a missing array through its native-style getter. This is
a modeled boundary checked against installed IL, not execution of Unity.

The production-linked harness checks full `KitSummary.For()` results for both
real 20-kit catalogs, not only the armor helper. It resets the cache between
catalogs because their IDs are identical, and requires both files to exist.
It checks weapon, front armor, helmet, headset and spare magazines, plus positive
legacy soft armor, nonzero masks, a deliberately broken armor getter, partial
description caching and reset. The original side/back/soft fixtures remain.

Root observed **303 assertions passed** after the independent review caught and
corrected the initial missing between-catalog cache reset. Two targeted runtime
mutations reject an unsafe Vest getter and loss of unrelated description
sections. The existing old-max-armor and backpack-fallback negative controls,
1,562 supply assertions and nine actual supply server tests also passed. The
client Release build succeeded against the installed game with zero errors and
four existing nullable-context warnings. Independent adversarial review found
no open P0–P2 findings after the cache correction.

`tools/KitPolishChecks/Verify-Descriptions.ps1` is the combined native-contract and
regression oracle. Final combined builds/deployment are recorded separately in
PRESET-OPTICS-REVIEW.md. There is no GPU on this host: visible menu rendering and
the rest of the actual raid still need the user's playtest after restart.
