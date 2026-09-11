# Grenade pick after widened reach (2026-09-11)

Pre-existing defect in Arena's reach feature (`ArenaMode.Client/Kits/ArenaReachPatches.cs`,
F12 Kits / `UseItemsAnywhere`, default on), found by the adversarial review of the
weapon-panel grenade row (see RAID-INFO-PANELS-REVIEW.md) and fixed here. Client only.

## The defect, verified against ../_decompiled

1. Reach makes `IsAtBindablePlace` and `IsAtReachablePlace` true for any worn,
   examined, whole quick-usable item, so a grenade outside the rig/pocket top level
   can be bound to a number key and drawn with it (`Player.SetQuickSlotItem` ->
   `TryProceed`, Player.cs:31417-31435, 31560-31666).
2. Drawing ANY grenade makes it the G key's pick: `FastAccessGrenadeItemView`
   implements `ISetInHandsHandler` and writes `Equipment.TopPriorityGrenade`
   (FastAccessGrenadeItemView.cs:199-209; field InventoryEquipment.cs:76).
3. The throw discards it: `BaseGrenadeHandsController.ThrowGrenade` ->
   `Player.ThrowGrenade` -> `ItemManipulator.Discard`, then Begin and Succeed remove
   events with `From` = its old address and the item already addressless
   (Player.cs:15672-15682, 31952-31967; DiscardResult/RemoveResult/ContainerRemoveResult).
4. The view only moves the pick on when the removed grenade's parent is the item in a
   `GrenadeThrowingSlots` slot (FastAccessGrenadeItemView.cs:160-197) — TacticalVest
   and Pockets natively (InventoryEquipment.cs:124-142). `ThrowWeap` is a plain `Item`
   (ThrowWeap.cs:9), so the TacticalVest/CompoundItem branch never applies. The pick
   keeps naming the thrown item.
5. Tap-G (`PlayerInputTranslator.vmethod_1`, PlayerInputTranslator.cs:496-542) uses
   the pick whenever its candidate list is non-empty. `SetItemInHands` passes, since
   `Item.CheckAction(null)` returns a default (succeeded) `Option` for a null address
   (Item.cs:629-634, Option.cs:13-16); `TryProceed` then finds it unreachable
   (InventoryController.cs:448-453; Arena's postfix refuses too, `WornAndWhole` needs
   an address) and falls back to `SetFirstAvailableItem` (Player.cs:31583-31587).
   G silently never throws, with grenades in the rig, until hold-G sets a new pick
   (InventoryScreenQuickAccessPanel.cs:270-275). The quick slot keeps showing the
   thrown grenade.

Which grenades are affected depends on the install:

| Grenade location | Vanilla slot lists + Arena reach | User's install (UseItemsAnywhere 1.4.0) |
|---|---|---|
| Rig / pockets top level | native bookkeeping | native bookkeeping |
| Backpack top level | **affected** | native (the mod adds Backpack to `GrenadeThrowingSlots`) |
| Pouch nested in rig (natively reachable, bindable only via reach) | **affected** | **affected** |
| Pouch nested in backpack | **affected** | **affected** |

On the user's install the mod itself binds nested grenades in ordinary raids too, so
the same stale pick exists there without Arena. That is the mod's behaviour and is
deliberately left alone: ordinary raids stay bit-identical.

## The fix

Two patches in the reach feature, both gated on `ArenaSession.IsActive` alone.
Not on the setting as well: F12 is live, and without the UseItemsAnywhere mod a
backpack pick made while reach was on is stranded the moment reach is turned off.
What they do is never worse than native: they only act on a pick G cannot draw, whose
only native outcome is the `SetFirstAvailableItem` fallback, and replace that with the
game's own next pick or its own no-pick branch. Most such picks are reach's; a few are
native bookkeeping gaps (an unexamined rig grenade the view lists anyway, a dropped
backpack's grenades left in the view's list under the mod) that inside the arena now
take the same route.

- **`PickAfterRemovalPatch`**, postfix on `FastAccessGrenadeItemView.OnItemRemoved`.
  After the native handler, on a Succeed removal by the view's own controller of the
  pick itself or of a container still holding it (a dropped backpack, a pouch dragged
  into loot — their event names the container): if `IsAtReachablePlace` (the question
  `TryProceed` asks) is now false for the pick, call the view's own
  `SetNewTopPriorityGrenade` — the same next pick and quick-slot icon the game
  produces for a rig grenade. A pick that only moved out of an untracked place and is
  still reachable keeps the pick (a rig grenade moved away loses it natively, first).
  The controller comes from `QuickSlotView`'s protected field through Harmony
  injection (`___InventoryController`). Everything is inside a try: the game raises
  this event through ONE try around the whole multicast
  (`ItemController.SafeRemoveItemEvent`, ItemController.cs:781-790), so a throw would
  silence every later subscriber.
- **`PickBeforeTapThrowPatch`**, void prefix on `PlayerInputTranslator.vmethod_1`.
  Clears a pick `TryProceed` would refuse, so the native no-pick branch runs (random
  candidate, which then becomes the pick and icon through `OnSetInHands`; nothing with
  an empty list). Catches what the view could not fix: reach turned off after a pick,
  a removal while no quick-slot view listened, or a replacement the view took from a
  stale entry in its own list. `HideoutPlayerInputTranslator` is the only override and
  is not patched.
- Both are enabled through `EnablePickPatch`, which catches and reports a failure:
  `Plugin.Awake` enables every patch in one unguarded sequence, so a drifted target
  must not stop the rest of the plugin from loading. Without them the defect returns,
  nothing worse.

Unchanged: what reach allows, bindings, hold-G, the view's private list, the slot
lists, bots (no `PlayerInputTranslator`), ordinary raids, the profile
(`TopPriorityGrenade` is a runtime field, not serialized). A failure is logged once per
raid with its exception ("Arena grenade pick cleanup failed"); what remains is the
game's own handling.

Known residuals, all P3 and rescued by the key backstop at the next tap: after the
backstop clears a pick, the quick slot keeps the old icon until a grenade is drawn
(normally by that same tap), because only the view writes its icon; the view's own
list can hand the removal cleanup a stale replacement (the backstop clears it); the
backstop's no-pick branch chooses randomly where the view would choose by type.

## Verification

`tools/ArenaGrenadePickChecks` (see its README) compiles the production file against
a model of the native view, key, `TryProceed` and inventory, with a Harmony-like
binder that refuses bindings Harmony would: 123 assertions. With the two pick patches
suppressed the model reproduces the report exactly (stale pick and icon, repeated
fallback). `Verify.ps1` rejects fourteen source faults, each at the assertion naming
it (arena gate, contained enabling, view resync, container case, reachability test in
both directions, owner and status tests, both exception guards, key backstop, null
pick, per-raid reporting). `Verify-InstalledContracts.ps1` pins the targets, the
parameter names Harmony binds, the injected protected field, the pick's readers and
writers, the event route, the throw path, the null-address refusal, the three shipped
slot lists and the installed UseItemsAnywhere 1.4.0 (version, patches, slot lists)
against the installed Assembly-CSharp (SHA256 `EE25CEE1…`);
`Verify-ContractFaults.ps1` shows eight wrong expectations are each rejected. Staged
client build: zero errors, the four existing CS8632 warnings.

## Adversarial review

Three independent lenses (native correctness, isolation/safety, harness fidelity)
found no P0 or P1.

- Confirmed: the chain above; ordinary raids, profile and bots untouched; no other
  installed mod patches these targets; both targets are single, non-inlinable,
  delegate-safe; the battle and inventory quick-slot views are never subscribed at
  once; re-entrancy matches native; moves/swaps raise Succeed with the destination
  address; grenades never stack; respawn builds a fresh view list and equipment; no
  legitimate (drawable) pick is ever cleared.
- P2, fixed: a pick patch failing to enable would have aborted the rest of
  `Plugin.Awake` — now contained (`EnablePickPatch`).
- P2, fixed: the reachability decision itself was untested (a substitute test kept
  every check green); two scenarios now pin it in both directions.
- P3, fixed: a container dropped with the pick inside was missed; the failure log
  was process-wide and message-only (now per raid, full exception); comments
  overstated the `IsActive`-only argument and the tracked slot list; no check that
  normal play logs nothing; only one of the two exception guards was mutated; the
  installed contracts left slot lists, `item`, single-method targets, the null-address
  refusal, the event route and the mod's lists unpinned; the contract faults had no
  script; the README named the wrong document.
- P3, documented as residuals above: icon staleness on the backstop path, stale
  entries in the view's own list, random vs. by-type choice. The Begin/owner filter
  checks mirror native filters and are spec checks, not failures seen in the game.

## In-raid acceptance (GPU)

None of the above runs Unity. Test on a separate test profile, Arena active, F12
Kits `UseItemsAnywhere` on (default); watch the G quick slot at the bottom.

On the user's install UseItemsAnywhere already tracks the backpack's top level, so
the original report only reproduces there with the mod disabled (game closed, move
`BepInEx\plugins\UseItemsFromAnywhere.dll` out, restore it afterwards) or with a
grenade in a container nested in the rig/backpack.

1. **Reported path.** Put a grenade in the backpack's top level (mod disabled) or in
   a nested container; keep at least one grenade in the rig. Bind the backpack
   grenade to a number key, draw it with that key, throw it. Expect: the G quick slot
   switches to a rig grenade immediately. Tap G: a rig grenade comes out and can be
   thrown. Repeat tap G until the rig is empty: every tap throws.
2. **Moved, still reachable (mod disabled).** Bind and draw a backpack grenade, put
   it away, move it to another backpack cell. Tap G: that grenade comes out. (With
   the mod the backpack is native bookkeeping, which moves the pick on instead.)
3. **Dropped.** Bind and draw a backpack grenade, put it away, drop it on the ground.
   The G slot switches to a rig grenade; tap G throws a rig grenade. If a container
   holding a grenade is available: pick that grenade the same way, then drop the
   container (or the backpack, Z) — same expectation.
4. **Setting off (mod disabled).** Bind and draw a backpack grenade, put it away,
   turn F12 Kits `UseItemsAnywhere` off. Tap G: a rig grenade comes out (not the
   weapon swap). Turn the setting back on.
5. **Unchanged natives.** Tap G with only rig grenades; hold G and choose a grenade;
   draw a rig grenade by number key and throw it — all as before.
6. **Ordinary PMC raid** afterwards: G, hold-G and number keys behave as before.
   With the mod installed, a nested-pouch grenade thrown by number key still blocks
   tap G there — that is the mod's own behaviour, unchanged by Arena.
7. `BepInEx\LogOutput.log` shows `Enabled patch PickAfterRemovalPatch` and
   `Enabled patch PickBeforeTapThrowPatch` at startup, and no "Arena grenade pick
   cleanup failed" or "Arena grenade pick patch not enabled" line.

## Delivery

**Current combined delivery (2026-09-11):** the first GPU panel job completed.
Source `998cef5` includes the unchanged grenade-picker implementation plus both
panel/native-UI repairs. Client delivered with SHA256
`ED74F678CB303D775035FA6A4192387C167214C32B3A6FE6DD4CEE251D5CBEE1`, receipt
`artifacts/panel-native-ui-fix-delivery/07f8e2b9a54f4fe0a308780659959878/receipt.json`.
The r2 panel job pins that combined build. Native grenade gameplay still needs
GPU acceptance. Do not replace it with the older initially staged DLL below.

Historical staging: not delivered with the implementation, as the client (`D927D5B9…`, from
`18267c8`) is pinned by the prepared GPU panel job (GPU-PANELS-TEST-INSTRUCTIONS.md),
which forbids a DLL swap during its run. Staged instead from the clean
implementation commit `3f34f86` (zero errors, the four existing CS8632 warnings):
client SHA256 `FF07FE124952C9D81A958284C8DEA8E83AB303B4243A0D6BCCA336E88A171EA0`,
manifest `artifacts/double-jump-build/2307d55300984d3886b2406aecd9c2d3/manifest.json`;
installed client, server and Redeploy.Core stayed unchanged at that stage.
Now superseded by the combined delivery above. Client only: game restart, no
server change.
