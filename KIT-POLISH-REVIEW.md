# Kit polish review

## Front armor display (K1)

`KitSummary.FrontBodyClass` reads equipped ArmorVest/TacticalVest roots and the
actual installed armor children, never the slot's allowed-item list. Native
ArmorComponent.GetBothArmorPlates intersects the part mask and ArmorSlot mask;
that intersection must include a chest collider. A universal class-6 plate in a
side/back slot therefore cannot replace the front class. With no front plate,
only physical soft armor covering frontal RibcageUp contributes (the part AND
the installed ArmorSlot for children); a bare carrier and a plate stored in a
grid do not protect the front. Integral thorax armor remains supported. Helmet
summary and kit contents/prices are unchanged.

Native evidence: `../_decompiled/EFT.InventoryLogic/ArmorComponent.cs:155-238`,
`SlotSerializer.cs:25-31`, `Slot.cs:678-725`, and `../_decompiled/EArmorPlateCollider.cs`.
The offline harness links the actual KitSummary and DTO source with explicit
boundary stand-ins; real DB armor filters/colliders are projected using the
native ArmorSlot discriminator. It checks the real catalog plus front4/side6,
missing front, incompatible coverage, storage, orphan and integral cases. The
negative control restores the old max-class call in the actual buy summary.
Stand-ins are not a live Unity test; the server has no GPU.

Root validation passed: 52 production-linked assertions, all 20 shipped plus
20 installed kit summaries, and the deliberate old-max-class mutation rejected
at the intended buy-summary assertion. Real client Release compiled with zero
errors (four pre-existing Shared warnings). Independent review found no open
P0-P2 findings. These are offline/build results, not a GPU raid observation.

## Optional grenade supplies (K2)

Each eligible Arena kit receives a request for one RGD-5 and one Zarya. A timed
frag provides a limited way to flush cover; one flash gives a nonlethal entry
option without filling the inventory with explosives. A pre-existing frag or
flash/stun of any live template satisfies that type instead of adding another.
The exact RGD-5 must still be a normal timed frag (negative contact-explosion
time); missing or changed target templates disable that request. Neither authored
kit files, prices, items, ammunition counts, nor user supplies are rewritten.

`ArenaKitSuppliesPostLoad` captures actual native TemplateItem properties after
content load. It does not deserialize IDs with default JSON options, generate
bots, change APBS settings, or touch profiles. The service appends small optional
requests to a parsed copy of the kit catalog. Unknown/malformed authored contents
fail closed; missing/null fill is retained rather than inventing its ammo policy.
The regular Equipment root is confirmed Type=Item in the installed database.

`ArenaSessionService` freezes those requests with weapon variants in the exact
session's Armed catalog, before the client's loading-screen preload. They remain
stable through native start/reloads and are cleared with existing catalog state.
Ordinary sessions never receive this transformed catalog; protected PFT 5v5 is
explicitly excluded, so its PMC entry loadout gains no Arena grenades.

`KitBuilder` handles optional entries after spare ammo AND all authored fill,
using the native `InventoryEquipment.GrenadeThrowingSlots` (TacticalVest/Pockets).
Only actual ThrowWeap items qualify; each optional entry is limited to one item.
Every attempt calls existing native `Grid.AddAnywhere` with restrictions enabled.
No replacement, repacking, forced slot insertion, or backpack fallback occurs.
No space/filter refusal is an expected Info skip, while existing supply failures
keep their warnings. Thus an unusual rig or a Gun Game drum can legitimately
leave only one or no optional grenades; the server does not promise free cells.

Existing `KitBundlePreloader.ColdTemplates` already traverses every RigContents
template, including the new marker. `KitMerge` retains the base RigContents list
without mutation. A normal kit rebuild after respawn supplies grenades again;
`BuildPrimaryWeapon` during a live Gun Game promotion never does.

Native evidence: `../_decompiled/EFT.InventoryLogic/InventoryEquipment.cs:122-140`
defines the quick-throw list; `Grid.cs:511-514,922-943` retains filters, occupancy
and dimensions/rotation. `KitBuilder.Populate/FillGrids`, `KitMerge`,
`KitBundlePreloader`, and the actual server service are source-linked into the
separate Supplies harness. Its boundary doubles model item creation, finite grid
occupancy, and real DB inclusion/exclusion filters using the local native
TypeTable IDs. They are deliberately not claimed to simulate Unity/prefab models,
all native inventory events, equipment surgery, or the entire skill/inventory
system. Production always delegates final placement to the actual native grid.

Checks cover both real 20-kit catalogs, original item/ammo counts, exact optional
requests and quick-use placement, full/filtered grids, backpack exclusion, ammo
and authored fill priority, existing item identity, non-throwable rejection,
preload and Gun Game stability/no promotion refill. A deliberate mutation
restores backpack fallback in the actual builder and must fail its specific
fixture. Actual server xunit checks cover typed live template projection,
idempotency/custom grenades/fallbacks and Armed/native-start/protected/session
lifecycle. `Verify.ps1` runs Armor then `Verify-Supplies.ps1` sequentially.

Root's complete kits-G1 passed (exit 0, expected marker), including the Armor
recheck, Supplies production harness, deliberately restored backpack-fallback
mutation rejected at its intended assertion, and all nine actual server test
cases. Both shipped and installed catalogs produced exactly 40 requests for
20 kits; the real DB layout oracle placed both grenades in quick-use grids for
each kit without reducing original item/ammo counts. Independent review found
no open P0-P2 findings. No GPU raid result is claimed; final build/deployment
and commits remain root's serialized integration responsibility.
