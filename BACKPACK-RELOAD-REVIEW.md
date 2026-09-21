# Arena reload from a backpack (2026-09-21)

With `UseItemsAnywhere` enabled, Arena adds magazines directly in the worn backpack
to R/quick-reload, the magazine selector and inventory reload candidates. Native
compatibility, search/examination, sorting, animation and transactions remain in
charge. No nested containers or magazines attached to weapons are scanned.

## Mono regression and replacement

The first implementation patched the constructed generic
`InventoryController.GetReachableItemsOfTypeNonAlloc<Magazine>`. The GPU log at
18:25:08.964 demonstrates why this is unsafe: a `ThrowWeap` query from
`GrenadeInventoryCounter` entered the generated Magazine wrapper and threw
`ArrayTypeMismatchException` in the ORIGINAL native scanner, before the postfix.
The weapon panel hid itself after this failure. Ammo queries used for internal
reload and G grenade selection share that code on Mono as well. A postfix runtime
type guard cannot repair a generic context already substituted by the trampoline.
The earlier source-linked tests only called that postfix and missed the failure.

The fix never patches generic methods. Three non-generic consumers have exactly
one Magazine call replaced by a typed, non-generic helper:

- `FirearmHandsInputTranslator.ReloadExternalMagazine`: NonAlloc magazine query.
- `AmmoSelector.CG_FindAmmoForWeapon.method_0`: enumerable magazine query.
- `ItemUiContext.FindSuitableMagazine`: enumerable magazine query.

The helper runs the untouched native query first, then appends backpack candidates.
The transpiler refuses missing/duplicate callsites before changing any instruction,
preserves labels/exception boundaries, and leaves Ammo/ThrowWeap calls untouched.
Do not restore the generic detour even if a desktop-CLR harness accepts it.

## Safety

The addition requires active Arena, the exact started local game/world/raid,
living local physical body and its inventory controller. Respawn, spectating,
stand-down, bots and ordinary raids remain unchanged. Native exceptions still have
their native behavior; only the optional backpack scan is guarded.

The virtual native `GetAcceptableItemsNonAlloc` scans only Backpack, preserving
`PlayerOwnerInventoryController` unknown-item and searched-container filters.
Existing results/order are retained, additions deduplicate by reference. Body and
worn backpack are checked again before publishing, including predicate callbacks.
No shared slot arrays or inventory contents are rewritten.

No reload transaction, timing, hands state or old-magazine destination changes.
Arena reload speed and UIFixes ReloadInPlace remain downstream. Full native storage
can still make the old magazine drop; returning it to Backpack is not promised.
The HUD retains the backpack origin icon.

## Verification

`tools/BackpackReloadChecks/Verify.ps1` links the actual adapter with explicit native
boundary doubles and real Harmony instruction types. It checks non-generic targets,
exact call replacement/stack shape/metadata, typed ammo/grenade queries, native-first
collection, scope, predicates, virtual overrides and replacement during callbacks.
Six compiled faults include restoration of the unsafe generic target and disabled
callsite-drift rejection. Installed Assembly-CSharp IL pins the three exact Magazine
calls and separate G/ThrowWeap query. Measured: 51 behavior assertions, 34 native
contracts, six faults; independent review found no concrete P0-P2.

A fresh EFT process is mandatory: replacing the DLL cannot remove a detour already
installed in Mono. GPU acceptance is recorded separately after delivery; source
checks and successful builds alone do not certify the runtime patch.
