# Automatic Arena weapon pickup checks

`pwsh -NoProfile -File tools/ArenaWeaponPickupChecks/Verify.ps1` runs the real
`ArenaMode.Client/Pickups/ArenaWeaponPickup.cs` against controlled native boundary
models, compiles that same file against installed EFT/Unity assemblies, checks the
installed native IL, and verifies five deliberately broken production variants.
The projects do not build or deploy ArenaMode or Redeploy. Generated evidence is
under this directory's ignored `artifacts/` path.

The source-linked `Definition` check validates the pickup-only M60 against installed
item templates: attachment slot filters, required slots, 100 M993 rounds in legal
stacks, fresh mutable definitions and inclusion of the weapon root by the actual
preload planner. It needs no profile. To additionally compare the original named
preset without modifying it, supply `-PresetSourceProfile <absolute-profile-path>`;
the opt-in check binds its before/after SHA256 and normalized attachment tree.
Native creation, ownership and fresh item IDs still belong to the existing builder
and adapter; the definition check is not a native ItemFactory execution.

The adapter accepts only the exact live local player with a native
`Player.SinglePlayerInventoryController`. It uses a complete loaded kit weapon,
preloads before prefab creation and submits a regular simulated native Move into
an empty compatible primary/secondary/holster slot. It excludes `ActiveSlot`,
including an empty active slot, because native inbound processing draws into it.
It never calls the weapon swap service, selects hands, removes carried equipment,
uses unrestricted inventory mutations, or collects unrelated world loot.

Native discard bookkeeping can enumerate `ItemsCount(item, 0, count)` even for a
lossless transfer. Reject only entries with positive `NumberToDestroy`, not any
nonempty enumeration. The boundary model reproduces this native entry; a fault
control restores the original blanket rejection and must fail the first safe take.
Empty hands and an empty active slot are covered without allowing automatic draw.
Refusal messages distinguish full/incompatible slots, native veto and actual loss.

The Arena-only source owner rechecks player body, inventory, hands, world, round,
caller scope and destination immediately before native transfer. Native single
player execution yields after submission, so checking only at submission is not
sufficient. Final success additionally requires both native success and exact
destination ownership. False with an unavailable handle is terminal for that
point in the current round. Pending/uncertain work must never time out into a
fresh pickup: a late native operation could otherwise duplicate the weapon.

`Handle.Visual` and `Handle.Collider` identify the original pooled loot model,
including while native inbound processing is pending. The caller may position
and rotate it, but must not destroy it or put it beneath a parent it destroys.
Dispose immediately deactivates a pending model and defers pooling until native
rollback or success resolves ownership. The native removal event clears our
reference before that pooled object can be reused. Native callbacks may still
finish after disposal; callers must use their own entry/generation checks.

Native sources inspected in `../_decompiled/` (outside the ArenaMode repository):

- `EFT/GameWorld.cs`: `SpawnLootItem`, `CreateLootWithRigidbody`, `DestroyLoot`.
- `EFT/InteractionContextHelper.cs`: `GetAvailableInteractionState`, `smethod_10`.
- `EFT/Player.cs`: `SinglePlayerInventoryController.ExecuteAsync`,
  `PlayerInventoryController.InProcess`, `TrySetInHands`.
- `EFT.InventoryLogic/ItemController.cs`: source-owner `OutProcess` dispatch,
  `RunNetworkTransaction` and `Execute`.
- `EFT.InventoryLogic/ItemManipulator.cs`: `Move` simulation and rollback.
- `EFT.InventoryLogic/ChangeDiscardLimitsResult.cs`: zero-destroy bookkeeping
  constructor and nonempty `ItemsToDestroy` iterator, checked against installed IL.
- `EFT.InventoryLogic.Operations/MoveOperation.cs`: outbound check, move, inbound.
- `EFT.InventoryLogic.Operations/AbstractAsyncOperation.cs`: completed outbound
  callbacks continue synchronously; finalization emits success or rolls back.
- `EFT.Interactive/LootItem.cs`: `Init`, `StopPhysics`, `RemoveLootItem`, `Kill`.

Offline evidence does not certify the Unity prefab/collider appearance, native
weapon animation or hands behavior, pickup audio, or actual raid collection.
Those remain GPU acceptance checks in the combined feature handoff. AI weapon
collection is intentionally excluded from this first slice.
