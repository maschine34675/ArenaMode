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

Implementation pending; separate change/commit from K1.
