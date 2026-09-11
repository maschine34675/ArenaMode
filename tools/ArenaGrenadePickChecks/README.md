# Arena grenade-pick checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/ArenaGrenadePickChecks -c Release
pwsh -NoProfile -File tools/ArenaGrenadePickChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaGrenadePickChecks/Verify-InstalledContracts.ps1
pwsh -NoProfile -File tools/ArenaGrenadePickChecks/Verify-ContractFaults.ps1
```

The harness compiles the production `ArenaReachPatches.cs` against a small
model of the SPT 4.1 inventory, the quick-throw view
(`FastAccessGrenadeItemView`), the G key (`PlayerInputTranslator.vmethod_1`)
and `Player.TryProceed`, written from `../_decompiled` with line references in
`StandIns.cs`. A minimal patch host binds prefixes/postfixes the way Harmony
does (`__instance`, `ref __result`, original parameters by name, `___field`
injection up the declaring type's hierarchy; postfixes on every return path,
none after the original throws) and refuses a binding Harmony would refuse at
patch time. UseItemsAnywhere 1.4.0 is modelled from its mechanism for the
user's install; nothing of it is copied.

What it checks, all through the real production patches (123 assertions):

- with the two pick patches suppressed, the model reproduces the reported
  defect: a backpack grenade drawn by number key and thrown leaves the pick
  (and the quick-slot icon) on the thrown item, and tap-G falls back to
  `SetFirstAvailableItem` every time;
- with them, the pick and icon move on exactly as the native view does for a
  rig grenade, tap-G throws again, and a rig grenade still gets one native
  call and no second one;
- the same for a grenade in a rig pouch (natively reachable, bindable only
  through Arena) and, on the UseItemsAnywhere install, in a backpack pouch, in
  both postfix orders;
- a grenade that only moved and is still reachable keeps the pick; one dropped
  out of the inventory does not, nor does a pouch dropped with the pick inside;
- the decision is `TryProceed`'s own question: with reach off, a picked
  grenade that is carried but not drawable moves the pick, and a natively
  drawable rig-pouch pick that is not bindable is still drawn;
- Begin/Failed events, another owner, a non-grenade, a non-pick grenade and a
  container without the pick leave the pick alone (these mirror the native
  handler's own filters: spec checks, not a failure seen in the game);
- the key backstop clears a pick stranded by turning the F12 setting off,
  leaves a drawable pick alone, and a tap with nothing picked logs nothing;
- ordinary raids stay native, including the real ordinary-raid variant of the
  defect that UseItemsAnywhere itself causes;
- a pick patch that cannot be enabled is reported and never stops the rest of
  the plugin from loading;
- a failing cleanup never escapes into the game's single-try event multicast
  or the input translator, lets the native method run, and is reported once
  per raid with its exception.

Assertions prefixed `model:` only confirm the stand-ins behave as the game
does; `control:` assertions run with the pick patches suppressed.

`Verify.ps1` breaks the production file fourteen ways in temporary copies
(arena gate, contained enabling, view resync, container case, reachability
test in both directions, owner test, status test, both exception guards, key
backstop, null pick, per-raid reporting); each must fail at the assertion
naming it before the unchanged file is checked again.

`Verify-InstalledContracts.ps1` pins, against the installed
`Assembly-CSharp.dll` with Mono.Cecil: the targets and that each is a single
method (`OnItemRemoved(RemoveItemEventArgs obj)`, public virtual `vmethod_1`,
only overridden in the hideout); the parameter names Harmony binds (`obj`,
`item` on both place methods); the protected `QuickSlotView.InventoryController`
behind `___InventoryController`; the pick field and what reads/writes it;
`Show` subscribing `OnItemRemoved`; the removal event's fields and its route
through `SafeRemoveItemEvent`'s single try; the throw path's discard and its
raised events; `TryProceed`'s reach refusal and fallback; `IsAtReachablePlace`'s
null-address refusal; the shipped `FastAccessSlots`,
`BindAvailableSlotsExtended` and `GrenadeThrowingSlots`; and, when installed,
UseItemsAnywhere's version, its four patches and both of its slot lists.
`Verify-ContractFaults.ps1` applies eight wrong expectations to temporary
copies of that script; each must be rejected.

Known divergences of the model, none of which a check depends on: one view is
shown (the game never subscribes the battle and inventory views at once); the
view's `ItemView` and `Item` are one `Shown`; sorting is stable where the game
uses `List.Sort`; `vmethod_1`'s hand-state early returns and random retry are
omitted (index 0 stands in for `Random.Range`); the local controller's
`GetAcceptableItemsNonAlloc` override that skips unsearched containers is not
modelled (it only narrows G's candidate list).

None of this runs Unity. The quick-slot icon, hold-G and the actual throw
still need the in-raid test in `GRENADE-PICK-REVIEW.md`.
