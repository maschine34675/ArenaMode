# Corpse cleanup: paired native inventory notifications

The long-raid report in `LONG-RAID-REVIEW-2026-09-05.md` identified 5,640
`No parent inventory ... RemoveItemEventArgs` diagnostics matching Arena's
649 trimmed corpses. These are missing inventory-activity notifications, not
evidence of lost player gear or a failed profile restore. The original log
snapshot has since rotated; the report records the historical evidence.

## Correction

`CorpseCleanupService.StripGear` now sends the complete successful native
removal result through `Begin`, then `Succeed` in a `finally` block. Physical
removal already happened before this notification pair. A native refusal
emits neither event; a Begin observer failure still attempts the completion
needed to settle the activity and update the reactive slot view.

Before removal, an item with any existing activity on its corpse controller
is skipped. Native completion matches only event type and item, not event ID;
without this exclusion it could settle another operation's earlier Begin.
Unrelated idle equipment remains eligible. Busy gear stays on the trimmed
corpse and is not retried, an intentional safety tradeoff.

No inventories are rolled back, active-event lists cleared, events swallowed,
or corpse/player objects destroyed. The complete result retains resize and
search-state notifications. The existing Arena-only, teammate, open-loot,
own-corpse, distance, settled-ragdoll, held-weapon and secure-container
exclusions are unchanged.

## Native evidence

Verified against installed SPT 4.1 source in `../_decompiled/`:

- `EFT.InventoryLogic/ItemManipulator.cs:2123-2185` and `Slot.cs:983-995`:
  unrestricted removal mutates the slot and item address before returning.
- `EFT.InventoryLogic/DestroyResult.cs:98-108` and
  `ApplySortItemsPositionResult.cs:40-56`: native immediate removals issue
  Begin/Succeed after mutation. `ChangeItemsOperation` uses the same ordering.
- `EFT.InventoryLogic/ItemController.cs:1733-1748`: ProcessActivity adds Begin
  or removes the first matching type/item activity, logging if none exists.
  `SelectEvents` at 984-993 and `HasForeignEvents` at 1106-1110 explain why
  the same-owner busy-item exclusion is necessary.
- `EFT.InventoryLogic/RemoveResult.cs:81-89`: forward the full result for both
  statuses, including resize and successful search cleanup.
- `ContainerRemoveResult.cs:12-15` and `ItemAddress.cs:51-57`: use the retained
  original slot address after item detachment.
- `SlotItemAddress.cs:39-46` and `Slot.cs:727-730`: only Succeed updates the
  reactive slot value. `EFT/PlayerBody.cs:201-222,305-327` then cancels pending
  model work, unskins and returns the previous model to its pool. Begin does
  not duplicate that model release.

## Verification and limits

The checks link the entire production service and invoke its public Tick.
Contract fakes deliberately separate slot mutation, activity matching and
reactive model release. The old Succeed-only behavior is a positive control:
it produces the diagnostic while still releasing the model, proving that
the test does not confuse logging noise with a resource leak.

Coverage includes normal success, repeated sweeps, protected gear, teammates,
normal raids, current looting, active ragdolls, native refusal, failures before
and after mutation, and notification exceptions. Four source-copy mutations
omit Begin, omit completion, remove the busy-item guard, or remove the finally;
each must fail its intended assertion. Production files remain unchanged.

This does not recover every possible native observer failure. A raw
`Slot.OnAddOrRemoveItem` callback can throw after detachment but before a result
is returned. A persistent success-notification observer failure can prevent
the later slot-view update. These paths remain logged without falsely saying
the item stayed attached; no speculative retry or rollback is added.

Independent native/code review reported no unresolved P0-P2 findings. Local
tests cannot prove Unity pooling or FPS improvements. After restarting the
client, verify many kills with corpse stripping enabled: the original per-item
activity diagnostics should disappear, bodies/gear should still trim normally,
and raid exit/profile restoration should complete. This server has no GPU;
the live check remains an explicit handoff in `tools/CorpseCleanupChecks/GATES.md`.
