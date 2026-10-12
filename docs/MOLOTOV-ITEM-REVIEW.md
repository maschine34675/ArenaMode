# Dedicated Molotov: private template and supplies

Date: 2026-10-12. This review covers the server item and kit path, not native
model, fire damage, bot escape or audio acceptance. See the separate combat and
model reviews and the final integration/GPU report for those boundaries.

## Contract

The fixed private template `a12e00000000000000000001` clones RGD-5's schema and
native prefab references. The original `5448be9a4bdc2dfd2f8b456a` item is not
modified. The new template has zero native strength, blast radii and fragment
count; concussion, flash and armor-blast vectors are zero. Native blast effects,
fuze sound and tripwire planting are disabled. A finite fuse remains for cleanup.
`ThrowWeap.IsDummy` therefore makes the native `ExplosionSharedMethods.Explosion`
damage path return immediately even without the Arena client. The client also
suppresses only this exact template's native explosion.

SPT's `CustomItemService.CreateItemFromClone` supplies the item database entry,
base-class cache and localized labels. Handbook, flea-price and weapon-shelf
insertion are explicitly disabled; SPT consequently also adds the ID to its PMC
loot blacklist. Nothing adds trader stock, loot-generation entries or profile
items. The item is examined by default, avoiding a persistent examination write.

Registration runs at PostLoad + 97999, before pricing and the existing kit supply
snapshot. It accepts only a compatible timed native donor and reads back every
safety property after SPT publishes the clone. Only that exact successful object
may enter the supply snapshot. A foreign occupied ID is left untouched. Repeated
registration of the same verified object is idempotent. A rejected, throwing or
partially completed new registration removes its own newly inserted template and
does not authorize supplies. SPT may retain harmless locale/cache metadata after
a late partial failure; this does not restore the removed item or give a profile
access to it.

## Kit and session boundaries

Existing optional two-RGD-5 supplies remain unchanged. A standard kit without
authored throwables can additionally request one Molotov, last in the supply
list. Any authored throwable—including smoke and flash—opts out of the Molotov;
the historical two-frag rule is otherwise preserved. Unknown/malformed content,
empty item lists and missing/unsafe registration do not acquire a Molotov.
Applying the transformation twice adds nothing further.

`includeMolotov:false` preserves the two ordinary frags without adding a bottle.
The session caller uses it for Zombies, which the ground-fire runtime does not
support. The existing protected PitFireTeam gear guard bypasses the supply
transformation entirely. Ordinary/unarmed sessions still receive the authored
catalog. Only the private armed/started raid snapshot is enriched.

Native `KitBuilder` packs optional supplies after existing gear and ammunition,
only in TacticalVest/Pockets, never in Backpack or protected containers. No room
means skip; nothing is displaced or repacked. The new template participates in
the existing bundle preload. Gun Game retains base-kit supplies across weapon
merges but weapon promotions never replenish them. Solo Tactical receives the
item through its purchased complete kit; there is no new standalone purchase,
new free starting kit or protected-team gear edit.

## Evidence and limits

`tools/MolotovItemChecks/Verify.ps1` links production registration, supplies,
KitBuilder, KitMerge and preload code against real SPT 4.1.0 DTOs and both real
twenty-kit catalogs. It checks safe definition/read-back, donor preservation,
foreign collisions, repeated registration, partial errors, unsafe publication,
authored throwables, malformed/unknown kits, optional quick-slot placement,
preloading and Gun Game behavior. It also executes the historical supplies
regression and its two compiled backpack/getter mutations. Item mutations cover
global exposure, safety verification, partial rollback, foreign ID adoption,
unregistered supplies, authored throwables, unsupported modes and placement flag.

The SPT publication delegate and Unity inventory boundary are test doubles; the
actual SPT DTO/API contract and native implementation were independently read.
These checks do not claim a running SPT server, actual grenade hands/model or
in-raid acceptance. No test script builds the mod project or deploys a DLL.

Measured leaf result: 1,893 assertions in the registered-item path and 1,605 in
the unchanged historical supply path; eight item mutations and both historical
placement mutations compiled and failed at their intended assertions. The final
production rerun passed with `ARENA_MOLOTOV_ITEM_CHECKS_PASSED`.

Independent review: `molotov_bot_design` checked production registration/supplies,
native CustomItemService publication/override/blacklist behavior and the linked
tests; no new P0-P2 findings. Root integration still verifies session exclusion,
server build, native item usability and final profile restoration.
