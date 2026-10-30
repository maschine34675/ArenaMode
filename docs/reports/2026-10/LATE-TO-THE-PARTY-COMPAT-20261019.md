# Late To The Party: exact weapon-pickup protection

Date: 2026-10-19. This adapter is independently authored Arena code. It neither
copies foreign implementation nor bundles foreign sources/DLLs, changes saved
LTTP settings, alters normal PMC/Scav loot policy, or pauses global door logic.

## Reviewed source and published contract

Reviewed source: `dwesterwick/SPTLateToTheParty`, commit
`6b7f370aa989dd50fa390ec361f65ac5b6fa6fd7`, version 3.1.1. The read-only audit
snapshot is `artifacts/forge-top200-20261019/leaf-3/source/late/`.
The separately downloaded published client is `LateToTheParty-Client.dll`,
assembly version 3.1.1.0, MVID `7aa6260b-5943-45e0-becd-a28c2e8de830`, SHA256
`0c1d13ae9e2924096d90be0c3b81000a1910ba8c4a2e35646922eb00f53ee0f4`.
Its BepInPlugin GUID is `com.danw.latetotheparty` and its plugin class is
`LateToTheParty.LateToThePartyPlugin`.

3.1.1 is the minimum. A newer version is attempted only after matching loaded
plugin/assembly metadata, exact managed method shapes, relevant cleanup call
edges and all four installed Harmony prefix receipts. It produces one warning
per process. This is conditional admission, not a guarantee for future releases.
An unsafe present contract rejects Arena alone through the shared optional-mod
admission provider. Optional absence leaves startup and ordinary raids available.

## Why four seams are guarded

`LootManager.FindAndDestroyLoot` enumerates discovered loot with deferred
callbacks. `LootDestructionHelpers.StartDestruction(Item)` first opens a nearby
door, then calls `DestroyViaLTTP`, invokes `OnDestroyLoot`, and finally calls
`LootManager.ConfirmItemDestruction`. Skipping only the inner deletion would
still open the door, publish a false deletion and advance LTTP's destroyed count.
The direct `DestroyViaLTTP` API also has callers independent of that wrapper.

The adapter therefore excludes exact owned sources at:

- `LootDiscoveryHelpers.ProcessFoundLooseLootItem(LootItem,double)`: keep the
  generated pickup tree out of LTTP's discovered-loot denominator.
- `LootDestructionHelpers.IsEligibleForDestruction(Item,IEnumerable<Vector3>,double)`:
  do not spend cleanup selection capacity on an already known owned pickup.
- `LootDestructionHelpers.StartDestruction(Item)`: reject stale queued work before
  its door/event/accounting effects.
- `LootDestructionHelpers.DestroyViaLTTP(Item)`: cover direct native destruction.

Ordinary calls run the unchanged original methods. The actual native direct
paths are `ItemAddress.RemoveWithoutRestrictions` for attachments/container
items and `IItemOwner.RaiseRemoveEvent` for world roots. Arena's existing
`PickupOwner.OutProcess` does not intercept either operation.
The runtime validator also inspects reachable local state-machine `MoveNext`
bodies and refuses alternate native loot deletion around the guarded seam,
including concrete owner/address implementations. It does not execute foreign
methods during validation.

## Ownership and transfer lifetime

Only a handle created by `ArenaWeaponPickup.Prepare` registers authority. That
factory already requires its exact current world, runtime generation and owning
runtime continuation after bundle preload. The compatibility lookup additionally
requires the native LocalGame/GameWorld/IBotGame reference pair. Map, template,
item ID, display name and previous-raid flags never grant protection.

Every match requires the original native LootItem, its captured PickupOwner and
root Weapon references. The queried item must be one of the original generated
references and still physically belong to that weapon's current item tree.
Thus a newly attached ordinary item, detached original part, same-ID clone or
reused pooled model does not acquire protection. Discovery also checks the exact
LootItem argument, not just its Item reference.

The source belongs either to the current generation or an unsettled submitted
transfer. Native Move changes the weapon's address before final InProcess and
RemoveItemEvent completion. That temporary exact destination controller is
captured when the transfer is submitted, so LTTP cannot delete the original
source/attachments in this interval. Disposal and generation rollover cannot
open a gap while the original request remains pending. The existing OutProcess
validation still refuses queued stale/disposed transfers; no transfer policy is
weakened and no timeout rearms ambiguous submissions.

Successful source removal or confirmed carried ownership revokes the entry.
A later ordinary player drop receives no exemption. Rollback/final refusal plus
retired cleanup also releases the source. Native source release remains Arena's
own `GameWorld.DestroyLoot` call and is never intercepted. Weak registry entries
are pruned on registration and explicitly removed on consumption/source release,
even if LTTP is absent. Pending ambiguous requests retain only their original
same-world quarantine until settlement; a subsequent native world cannot inherit
it. Reads use native CurrentAddress where an absent address is legitimate,
because native Item.Parent throws in that case.

## Verification and review

`pwsh -NoProfile -File tools/LateToThePartyCompatChecks/Verify.ps1` is the leaf
oracle. It compiles production-linked behavior against authored native/Harmony
boundaries, checks the separately published DLL as metadata, rejects twenty
compiled negative controls at their expected assertions, reruns clean production,
and checks that its production inputs did not change during the run.

Measured leaf result: exit 0 and `LATE_TO_THE_PARTY_COMPAT_VERIFIED`, with
203 assertions before and after all 20 compiled negative controls. Evidence:
`artifacts/late-to-the-party-checks/8e44e0238e3c497bab780a12a071611a/`.
The first fault-matrix attempt exposed a harness identity collision: a deferred
fixture was rejected for test-assembly loading rather than its IL deletion path.
Unique fixture assembly revisions and an explicit expected rejection reason
removed that false positive; the final matrix includes the corrected control.
The four inspected source files' Git blob hashes also match the pinned
`tree.json`: LootManager, LootDestructionHelpers, LootDiscoveryHelpers and ModInfo.

Four passes: implement all four seams and source ownership; review native
owner/address and pending transfer semantics; independently inspect bypasses,
which added deferred state-machine and concrete-owner validation; polish lifecycle
registry retirement and run the full fault matrix. The independent reader found
no further concrete ownership defect after those corrections. Runtime and
published metadata evidence is separate from actual native acceptance.

Native references checked: `../_decompiled/EFT.Interactive/LootItem.cs`
(Init captures ItemOwner, successful root RemoveLootItem destroys the source,
Kill returns the pooled object), `../_decompiled/EFT/GameWorld.cs`
(RegisterLoot/DestroyLoot unregister the native lists),
`../_decompiled/EFT.InventoryLogic/Item.cs` (CurrentAddress/Parent/Owner),
`../_decompiled/EFT.InventoryLogic/ItemAddress.cs`, `OwnerItself.cs`,
`IItemOwner.cs`, and `ItemController.cs` (concrete remove-event dispatch).

## Native acceptance boundary

A native test should retain the published plugin unmodified, record loaded Arena
build/MVID, and observe the bounded Debug line
`Arena Late To The Party: owned weapon source excluded from foreign cleanup.`
while LTTP naturally scans a real pickup. Inspect the full loaded M60 tree,
collect it through the real inactive-slot transaction, and confirm its ordinary
subsequent drop has no retained source protection. Let LTTP remove ordinary
eligible loot beyond its age/distance gates, then run an ordinary raid on the
same map and confirm its normal policy. Verify clean round/raid exit and restore
all temporary test payload/configuration changes.

Discovery exclusion can prove the natural primary path but does not itself
exercise a pre-existing queued/direct deletion request, transfer awaiting native
completion, a delayed rollback or pooled-object reuse. Offline coverage of those
cases is not native acceptance. This leaf performs no client deployment or game
test; the root integration records build/deployment/native evidence separately.
