# Arena reload from a backpack (2026-09-21)

With the existing `UseItemsAnywhere` setting enabled, Arena adds compatible
magazines directly in the worn backpack to the native reload candidates. R,
quick reload, magazine selection and inventory reload keep their native behavior.
No manual configuration change is required if the existing setting is on.
Nested pouches and magazines attached to weapons are not scanned recursively.

## Native seam and safety

The postfix targets the concrete `InventoryController.GetReachableItemsOfTypeNonAlloc<Magazine>`
query. `FirearmHandsInputTranslator.ReloadExternalMagazine` calls it directly;
the generic wrapper is used by `AmmoSelector.CG_FindAmmoForWeapon.method_0` and
`ItemUiContext.FindSuitableMagazine`. See the installed decompiled files under
`../_decompiled/EFT` and `EFT.InventoryLogic/InventoryController.cs:882`.

The extension requires active Arena, the exact started local game/world/raid,
living local physical body and that body's inventory controller. Respawn,
spectating, stand-down, bots and ordinary raids remain unchanged. The postfix
accepts only a mutable `IList<Magazine>` and a compatible native predicate, guarding
shared generic Mono implementations against unrelated item queries.

The virtual native `GetAcceptableItemsNonAlloc` scans only the backpack slot.
This preserves compatibility predicates and native overrides for search/examination
(`Player.PlayerOwnerInventoryController.GetAcceptableItemsNonAlloc`, Player.cs:22345).
Existing results and order are retained, additions deduplicate by reference.
After scanning, the local identity and worn backpack are checked again before
publishing results, including callbacks from predicates.

No reload transaction, timing, hands state or old-magazine destination changes.
The existing Arena reload speed and UIFixes ReloadInPlace remain downstream.
If native rig/pocket storage is full, the old magazine may still be dropped;
this feature does not promise to return it to the backpack. The HUD keeps the
backpack origin icon; its setting description now explains that these magazines
can also be reloadable in Arena.

## Verification and pending gameplay checks

`tools/BackpackReloadChecks/Verify.ps1` source-links the actual adapter with explicit
native boundary doubles, validates the installed Assembly-CSharp IL, and rejects
compiled faults for ordinary-raid leakage, duplicate results, lost predicates and
publication after body replacement. It does not run Harmony on Unity/Mono.
All 30 behavior assertions, 19 installed native contracts and four fault controls
passed; independent review found no concrete P0–P2. Existing reload-speed checks
(779 assertions/11 faults) and grenade reach checks (123/14) also remain green.

After restarting EFT: test R with the only compatible magazine directly in the
backpack; compare a compatible rig magazine, full pockets, empty magazine, an
incompatible magazine, magazine-selection menu and inventory reload. Verify the
magazine moves exactly once, old-magazine behavior remains native, and respawn,
leaving Arena, `UseItemsAnywhere=false` and ordinary PMC raids disable the addition.
The generic Mono hook and actual reload animation require that GPU check.
