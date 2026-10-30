# Protected container consumption during 5v5

## Report and evidence (2026-09-05)

The client log contains two failed round-4 restores with the same message:
`1 originally protected items left or disappeared from protected slots`.

| Failure | Killcam playback ended | Result |
|---|---|---|
| 11:07:35.098 | 11:07:35.213 | Replay was still active |
| 11:34:31.286 | 11:34:00.614 | Replay had ended about 31 seconds earlier |

The second occurrence rules out a running replay as a necessary cause of this
validation failure. The user confirmed completely consuming an item from the
secure container during these matches. The old log records only a missing-item
count, so it cannot establish the exact item.

The original rule required every protected descendant's original ID to remain
present throughout the match. This also rejected legitimate consumption. Local
EFT source confirms that `ActiveHealthController.MedEffect` and
`MedEffectHelper.RemoveItem` remove the last medicine, food or stim item after
use. `Meds` includes `Stimulator`; checking only `MedKitComponent` would miss
single-use stims. Magazine loading can also remove a complete source ammunition
stack's ID through `ItemManipulator.Merge`.

## Restore behavior

At entry capture, only already-protected `Meds`, `FoodDrink` and `Ammo`
descendants are classified as expendable. Actual protected slot roots are
always excluded from this exception. The immutable snapshot still excludes all
protected items from its reconstruction data.

A classified ID may be absent on a later restore only if it is absent from the
entire current equipment graph. An item moved into a rig or pocket still fails
closed, as do missing container roots or nonconsumable protected items,
uncaptured items entering protection and ordinary snapshot items moved into
protected slots. Absence does not itself prove consumption; this narrow rule
also safely leaves a removed known consumable absent rather than recreating it.

Prepare and commit both evaluate the rule. Commit additionally requires the
exact inventory/equipment objects and unchanged protected ID set recorded at
prepare. Fresh IDs, ordinary item reconstruction, protected-item transfer,
rollback, round-epoch ownership and the server's raid/profile protections retain
their existing behavior. Nothing replenishes the consumed protected item.

## Verification

- The loadout harness covers exhausted medicine/stim/food/ammo, moved-out
  consumables, missing roots/nonconsumables, absent equipment evidence, invalid
  classification and a later round with fresh ordinary item IDs.
- Restoring the old unconditional missing-ID rejection makes the new consumed
  medicine assertion fail with the exact reported error.
- Independent adversarial review checks both prepare and commit and native EFT
  consumption semantics. Release compilation and affected 5v5 regression checks
  are recorded in the repository gates.

The GPU test remains: consume a complete medicine or stim from the secure
container, die, and continue into the following 5v5 round. The round should start
with the entry combat loadout and the remaining current protected contents.
