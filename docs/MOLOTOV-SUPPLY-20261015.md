# Additional Molotov supply — 2026-10-15

This supersedes the supply/conversion rule in the September 30 prototype review.
The dedicated-item integration later that evening is recorded in
[MOLOTOV-NATIVE-INTEGRATION-20261015.md](MOLOTOV-NATIVE-INTEGRATION-20261015.md).

## Player behavior

With the Molotov mutator enabled and its matching BigBrain companion available,
the exact local player receives **one additional** incendiary in free rig/pocket
space once their equipment is live. Existing RGD-5s and every other item keep
their native identity, placement and ordinary behavior. Only the added item's
exact process-local identity creates fire. G initially prefers that item; a later
manual grenade selection is never overwritten by our ticks.

The supplied item is now the private Arena Molotov, with its own name, bottle
model and icon. Its native blast is always harmless, including when the mutator
is off. Disabling retires pending owned fire/projectiles safely, without deleting
inventory items. Ordinary RGD-5s are never a fallback or conversion target.

Supply is once per equipment tree, not once per tick or grenade throw. Spending,
dropping, toggling off/on or merely advancing the round counter cannot mint more.
A newly equipped kit or restored own-gear tree receives its own one-item supply.
Full/incompatible rig and pockets skip the bonus and report this at Debug; nothing
is evicted, repacked, replaced, pushed into a secure container or put in a backpack.
The attempt is not repeated on that same equipment tree after freeing a cell.
Ordinary raids, bots and the still-unsupported Zombie mode receive nothing.

## Native inventory and ownership

The runtime already requires the exact confirmed Arena world, live game, open
combat round, current match, local living body and matching hands controller.
Supply additionally waits through respawn, weapon swapping and native inventory
busy state, and accepts only `Player.SinglePlayerInventoryController`.

The grant occurs after match setup captured protected own-gear entry snapshots.
Consequently later own-gear restores exclude the temporary bonus and receive one
new bonus on their fresh equipment, rather than copying and adding it repeatedly.
The registered private template is separate from ordinary items. No profile
persistence path, kit catalog or protection rule is changed by the live grant.

Before granting, the exact confirmed raid must carry the server's private-item
capability and its native client template must pass a harmlessness check. A private-item-only `KitBundlePreloader.Preload` request reuses the
existing per-template resource cache. Both native factories must be present;
the read-only `IsTemplateWarmed` query confirms the subsequent pool preparation.
A warm-cache entry alone is not template validation. This matters for
own-gear entry with no RGD-5: adding an unpooled item can otherwise break native
hands-controller creation. Work is hosted on the local game, never a replaceable
body. Exact world/match generation checks reject stale asynchronous completions.
Weak world/match references and a conditional-weak equipment ledger do not retain
retired worlds or bodies simply because an equipment tree once got a bonus.

The placement sequence follows installed native code, not unrestricted grid edits:

1. `Grid.FindFreeSpace` and `CreateItemAddress` select a compatible free cell.
2. `ItemManipulator.Add(..., simulate:true)` and `CanExecute` preserve native
   transfer, locked-item, count, shape, filter and action restrictions.
3. Synchronous checked `ItemManipulator.Add` physically adds only the new item.
4. `AddResult.RaiseEvents(Begin/Succeed)` follows the exact recipe in
   `ItemController.AddAndRaiseEvents` (`../_decompiled/EFT.InventoryLogic/ItemController.cs:1655`).
5. Set `InventoryEquipment.TopPriorityGrenade` once, if examined and reachable.
   Native `PlayerInputTranslator.vmethod_1` reads it directly for G
   (`../_decompiled/EFT/PlayerInputTranslator.cs:509`). Native selection/removal
   events and the existing Arena grenade-pick guard continue to own subsequent
   selection. No generic inventory Harmony detour or new input patch is added.

The ledger records the attempt before mutation and the granted identity before
notifications. Even a third-party notification exception after physical addition
cannot duplicate the item on a retry. An exceptional grant fails safely through
the existing warning path; ordinary RGD-5s never become fallback incendiaries.

## Verification and limits

Four passes: native design/call-path inspection; implementation and linked-source
behavior checks; negative controls and lifetime/resource review; parent integration
review/build plus separate native acceptance. The initial self-review identified
and corrected missing donor-pool preparation before the final implementation.

`tools/MolotovChecks/Verify.ps1` now exercises 89 assertions and thirteen compiled
negative controls. Existing fire ownership, normalization, no-stacking, geometry,
posthumous attribution and teardown checks remain. New checks cover existing
grenade preservation, additional-only identity, G preference/manual selection,
full/busy/refused native inventory, off/on and spending, new equipment, foreign
event exceptions, missing/failed/pending resource preparation and stale world work.

These are desktop boundary doubles, not Unity inventory/animator acceptance.
Final stable-source evidence: `artifacts/molotov-checks/a7948d9c93994c1fb46c94ee895828ce`;
both production runs report 89 assertions and all thirteen negative controls
compile and fail at their designated assertions. Scoped `git diff --check` passes.
The parent task records the final staged build, installed hashes, fresh game load
and native test outcome separately. This leaf does not deploy or commit.

Native test priorities: start with two native RGD-5s, observe three total, tap G
and verify ground fire, then throw the remaining two and verify native blasts.
Repeat with own gear containing no RGD-5, a fresh loadout/respawn and a full rig;
confirm manual hold-G selection remains authoritative and the protected profile
returns unchanged after leaving.

The subsequent [GPU2 follow-up report](GPU-FOLLOWUPS-20261015.md) verifies the
three-item kit, G selecting the extra incendiary, original grenade preservation
and ordinary behavior, and no duplicate on an off/on toggle. Cold-pool own gear
and full-rig handling remain covered by the offline boundary checks rather than
a separately repeated native scenario.

