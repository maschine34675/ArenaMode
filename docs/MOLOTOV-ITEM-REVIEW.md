# Dedicated Molotov: private template and supplies

Current integration (15 October): the private item/model is merged into the main
branch. The single client live-grant replaces the historical server kit addition;
existing RGDs remain untouched. See [current integration](MOLOTOV-NATIVE-INTEGRATION-20261015.md).
The original dated implementation/testing below is retained as history.

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
After successful ownership/safety verification, registration also adds only its
private ID to SPT's in-memory `RagfairConfig.Dynamic.Blacklist.Custom`. Existing
entries are preserved. This keeps SPT's later global flea-unlock pass from
changing the item into merchandise; no user's config file or broad setting is
changed. Missing blacklist state fails closed before cloning. Foreign-ID
collisions and failed publication do not acquire this exclusion.

Registration runs at Preload (100000), before SPT loads profiles and freezes its
item database. The kit supply snapshot remains at PostLoad + 99001. Registration
accepts only a compatible timed native donor and reads back every
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

Measured leaf result: 1,900 assertions in the registered-item path and 1,605 in
the unchanged historical supply path; nine item mutations and both historical
placement mutations compiled and failed at their intended assertions. The final
production rerun passed with `ARENA_MOLOTOV_ITEM_CHECKS_PASSED`.

Independent review: `molotov_bot_design` checked production registration/supplies,
native CustomItemService publication/override/blacklist behavior and the linked
tests; no new P0-P2 findings. Root integration still verifies session exclusion,
server build, native item usability and final profile restoration.

## Native load-order correction

The first GPU1 run on SPT 4.1.6 rejected the original late registration at
08:36:29.361 UTC: custom items must be added before profiles load. No Molotov
template reached the client, and the existing forty RGD-5 requests remained.
The old reference checkout and the compile-time 4.1.0 package did not include the
new `CustomItemService.ProfilesLoaded` guard. This was an evidence gap in the
initial delegate-only registration checks, not a client pickup/model failure.

The installed Core DLL was decompiled directly and compared with GPU1's copy;
both have SHA256 `490409F7C67480A8BF647DA67ADA8B91361330C2A9324D9E9787CD1EBAF5BF27`.
Its `SaveCallbacks` at priority 600000 sets `ProfilesLoaded`, takes the database
integrity snapshot and only then loads profiles. `CreateItemFromClone` rejects
late calls before any publication. Registration now uses the supported Preload
phase; neither the guard nor the snapshot is bypassed.

The harness additionally loads the actual installed Core in an isolated assembly
context. It executes the native late-call guard (both directly and through
`CreateItemFromClone`), compares production registration priority with native
SaveCallbacks metadata, and executes the real native override method using its
own DTOs/converters. Zero, false and empty safety overrides must still produce
the harmless template. A compiled mutation restores the old late priority and
must fail that native lifecycle check. No SPT server or profile service is started;
this does not replace the subsequent fresh-server/client GPU retest.

The corrected diff received independent `molotov_asset_feasibility` review of
the installed DI/start ordering, guard/override test and donor/isolation contract;
no open P0-P2. Final leaf rerun passed 1,900/1,605 assertions and all eleven
compiled mutations. Fresh GPU acceptance remains with the integration owner.

## Native global flea-unlock correction

The next fresh GPU1 server registered the template at 08:52:21.094 UTC, and the
client could receive and hold that exact item. Its standard Spartan kit still
contained only the two RGD-5s: the server requested forty frags, rather than sixty
supplies, before client packing. The kit file matched the local catalog and did
not author a throwable. This was not a quick-slot capacity failure.

The installed SVM preset disables the BSG flea blacklist. The installed SPT
4.1.6 `GameCallbacks`/`PostDbLoadService` path then runs
`SetAllDbItemsAsSellableOnFlea`, setting `CanSellOnRagfair=true` on every eligible
item not in its custom blacklist. The native client inspector confirmed that
exact changed value on template `a12e00000000000000000001`, with native strength
still zero. The late kit snapshot correctly refused the modified template under
its unchanged strict validation. Registration now protects its own successful
clone in the native custom blacklist before `GameCallbacks` runs. It does not
weaken validation, edit the donor or modify a template after the profile snapshot.

The production-linked harness invokes that installed bulk method on temporary
native DTO tables: a positive control without the exclusion reproduces the
mutation; production registration's exclusion then preserves the harmless
Molotov and an existing third-party exclusion, while an ordinary initially
unsellable item still becomes sellable. A compiled mutation removes only the
private exclusion and must fail this native test. Registration tests also cover
idempotence, preserving existing exclusions, foreign collisions, missing native
blacklist state and failed publication. The startup snapshot now reports whether
the Molotov is ready, missing/replaced or changed, including actual relevant
property values once rather than producing per-raid spam.

This diff received independent `molotov_asset_feasibility` review with no open
P0-P2. Final verification passed 1,921 registered-item assertions, the unchanged
1,605 historical packing assertions and all twelve compiled mutations (ten item,
two packing). The production rerun returned `ARENA_MOLOTOV_ITEM_CHECKS_PASSED`.
The test uses the real installed native service but does not start the
server or touch profiles/config files; fresh-server kit delivery remains an
integration/GPU gate.
