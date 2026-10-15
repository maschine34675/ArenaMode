using ArenaMode.Kits;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Pickups;

/// <summary>One Arena-owned, loaded weapon. Native inventory rules decide whether it fits.</summary>
internal static class ArenaWeaponPickup
{
    public static IEnumerator Prepare(GameWorld world, KitDefinition kit, Vector3 position,
        Quaternion rotation, Func<bool> isCurrent, Action<Handle> completed)
    {
        int generation = ArenaPickupRuntime.Generation;
        if (!Current(world, generation, isCurrent) || kit == null)
        {
            completed?.Invoke(null);
            yield break;
        }

        // Bundle loading precedes even the loot prefab factory, not just drawing.
        yield return KitBundlePreloader.Preload(kit);
        Handle handle = null;
        Weapon weapon = null;
        PickupOwner owner = null;
        GameObject visual = null;
        LootItem loot = null;
        try
        {
            if (Current(world, generation, isCurrent))
            {
                weapon = KitBuilder.BuildPrimaryWeapon(kit, 0, out _);
                if (weapon != null && !weapon.MissingVitalParts.Any()
                    && weapon.GetAllItems().OfType<Ammo>().Any(ammo => ammo.StackObjectsCount > 0))
                {
                    owner = new PickupOwner(weapon);
                    visual = Singleton<ObjectsFactory>.Instance?.CreateLootPrefab(weapon, ECameraType.Default);
                    if (visual != null)
                    {
                        visual.transform.SetPositionAndRotation(position, rotation);
                        visual.SetActive(true);
                        loot = world.CreateLootWithRigidbody(visual, weapon, weapon.ShortName,
                            false, null, out var collider, false, true);
                        if (loot != null)
                        {
                            // StopPhysics returns the temporary rigidbody and keeps the native
                            // loot bounding collider. The arena owns the fixed placement.
                            loot.StopPhysics();
                            handle = new Handle(world, generation, isCurrent, weapon, owner, loot, collider);
                            visual = null;
                            loot = null;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena pickup: weapon preparation failed ({ex.Message}).");
        }
        finally
        {
            // Creation can throw after native Init registered the item. Returning
            // only its prefab would then leave a stale entry in GameWorld's loot map.
            if (loot == null && visual != null)
            {
                var partial = visual.GetComponent<LootItem>();
                if (partial != null && ReferenceEquals(partial.Item, weapon)
                    && ReferenceEquals(partial.ItemOwner, owner)) loot = partial;
            }
            if (loot != null) world.DestroyLoot(loot);
            else if (visual != null) AssetPoolObject.ReturnToPool(visual);
        }
        completed?.Invoke(handle);
    }

    private static bool Current(GameWorld world, int generation, Func<bool> isCurrent)
        => world != null && ReferenceEquals(Singleton<GameWorld>.Instance, world)
            && generation == ArenaPickupRuntime.Generation && isCurrent != null && isCurrent();

    internal sealed class Handle : IDisposable
    {
        private static readonly EquipmentSlot[] WeaponSlots =
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster
        };

        private readonly GameWorld _world;
        private readonly int _generation;
        private readonly Func<bool> _isCurrent;
        private readonly Weapon _weapon;
        private readonly Item[] _generatedItems;
        private readonly PickupOwner _owner;
        private LootItem _loot;
        private readonly BoxCollider _collider;
        private bool _disposed;
        private bool _consumed;
        private bool _uncertain;

        internal Handle(GameWorld world, int generation, Func<bool> isCurrent, Weapon weapon,
            PickupOwner owner, LootItem loot, BoxCollider collider)
        {
            _world = world;
            _generation = generation;
            _isCurrent = isCurrent;
            _weapon = weapon;
            _generatedItems = weapon.GetAllItems().ToArray();
            _owner = owner;
            _loot = loot;
            _collider = collider;
            // LootItem subscribes first and returns its model to the pool on success.
            // Forget that reference immediately; it may already represent another item.
            _owner.RemoveItemEvent += Removed;
        }

        // Keep identifying the source model during native InProcess, when the
        // item already moved but the final removal event has not pooled it yet.
        public GameObject Visual => !_disposed && !_consumed && _loot != null ? _loot.gameObject : null;
        public BoxCollider Collider => Visual != null ? _collider : null;
        public bool IsPending { get; private set; }
        public bool IsAvailable => !_disposed && !_consumed && !_uncertain && OwnsSource();
        public string LastRefusal { get; private set; }

        /// <summary>
        /// True means submitted, not collected. A pending transaction is never timed out
        /// into a reusable pickup: EFT may complete it later. Only its final callback
        /// settles this instance. A thrown/unknown submission is quarantined until reset.
        /// </summary>
        public bool TryTake(Player player, Func<bool> isCurrent, Action<bool> completed)
        {
            LastRefusal = "pickup or player is no longer ready";
            if (!IsAvailable || IsPending || !Eligible(player, isCurrent)) return false;
            LastRefusal = "hands or inventory are busy";
            var inventory = player.InventoryController;
            if (!(inventory is Player.SinglePlayerInventoryController)
                || inventory.IsChangingWeapon || player.HandsController == null
                || !player.HandsController.SupportPickup()) return false;

            var equipment = inventory.Inventory?.Equipment;
            if (equipment == null) return false;
            // Freeze knowledge to the original generated tree, not later attached loot.
            // This survives native transfer/draw but expires with this body/round/raid.
            ArenaPickupExamination.Remember(_generatedItems, player, _world, _generation);
            int free = 0, rejected = 0, blocked = 0, destructive = 0;
            var refusals = new List<string>(WeaponSlots.Length);
            foreach (var slotName in WeaponSlots)
            {
                var slot = equipment.GetSlot(slotName);
                // Native Player.TrySetInHands draws automatically into ActiveSlot.
                // Even an empty active slot therefore cannot be an automatic pickup target.
                if (slot == null || slot.ContainedItem != null || ReferenceEquals(slot, player.ActiveSlot)) continue;
                free++;
                var plan = ItemManipulator.Move(_weapon, slot.CreateItemAddress(), inventory, simulate: true);
                if (plan.Failed)
                {
                    rejected++;
                    refusals.Add($"{slotName}={DescribeError(plan.Error)}");
                    continue;
                }
                if (!inventory.CanExecute(plan.Value))
                {
                    blocked++;
                    refusals.Add($"{slotName}=CanExecute:false");
                    continue;
                }
                // Native ChangeDiscardLimitsResult also enumerates preserved items
                // as ItemsCount(item, 0, count). Only a positive loss is destructive.
                if (plan.Value.ItemsToDestroy?.Any(count => count.NumberToDestroy > 0) == true)
                { destructive++; refusals.Add($"{slotName}=would discard items"); continue; }

                var hands = player.HandsController;
                IsPending = true;
                LastRefusal = "native transfer did not complete";
                bool settled = false;
                // SinglePlayerInventoryController.ExecuteAsync first yields a frame.
                // This source-owner hook runs AFTER that yield and before native Move.
                // Its synchronous result keeps check -> move -> native InProcess together.
                _owner.AllowTransfer = (executor, item, from, to) => !settled && IsPending
                    && IsAvailable && Eligible(player, isCurrent)
                    && ReferenceEquals(player.InventoryController, inventory)
                    && ReferenceEquals(player.HandsController, hands) && !inventory.IsChangingWeapon
                    && ReferenceEquals(executor, inventory) && ReferenceEquals(item, _weapon)
                    && ReferenceEquals(from.GetOwner(), _owner) && ReferenceEquals(to.Container, slot)
                    && ReferenceEquals(to.GetOwner(), inventory) && slot.ContainedItem == null
                    && !ReferenceEquals(player.ActiveSlot, slot);

                try
                {
                    inventory.RunNetworkTransaction(plan.Value, result =>
                    {
                        if (settled) return;
                        settled = true;
                        IsPending = false;
                        _owner.AllowTransfer = null;
                        bool carried = ReferenceEquals(slot.ContainedItem, _weapon)
                            && ReferenceEquals(_weapon.Parent?.GetOwner(), inventory);
                        // A final refusal without ownership is retryable. Any ambiguous
                        // result that already owns the weapon must never produce a clone.
                        _consumed |= carried;
                        bool success = result != null && result.Succeed && carried
                            && Eligible(player, isCurrent) && ReferenceEquals(player.InventoryController, inventory);
                        if (!success)
                            Plugin.ModLogger.LogDebug($"Arena pickup weapon transfer refused: nativeSuccess={result?.Succeed == true}, carried={carried}, slot={slotName}.");
                        if (success)
                        {
                            try { KitBuilder.RegisterAsChecked(player.Profile, _weapon.GetAllItems()); }
                            catch (Exception ex)
                            {
                                Plugin.ModLogger.LogWarning($"Arena pickup: magazine display registration failed ({ex.Message}).");
                            }
                        }
                        if (_disposed) ReleaseSourceVisual();
                        completed?.Invoke(success);
                    });
                }
                catch (Exception ex)
                {
                    // Submission may have thrown after queuing. Fail closed without
                    // releasing the reservation or pretending that it can be retried.
                    _uncertain = true;
                    _owner.AllowTransfer = null;
                    Plugin.ModLogger.LogWarning($"Arena pickup: native weapon transfer is uncertain ({ex.Message}).");
                }
                return true;
            }
            LastRefusal = free == 0 ? "free an inactive weapon slot"
                : destructive > 0 ? "transfer would discard items"
                : blocked > 0 ? "inventory rejected the transfer"
                : "native inventory refused the weapon (see log)";
            // A failed Move is not necessarily a slot filter mismatch: native
            // examination, search, locking and other restrictions fail here too.
            // Preserve the actual error without weakening any transfer checks.
            Plugin.ModLogger.LogDebug($"Arena pickup weapon not taken: {LastRefusal}; free={free}, planRejected={rejected}, blocked={blocked}, destructive={destructive}; reasons=[{string.Join("; ", refusals)}].");
            return false;
        }

        private static string DescribeError(object error)
        {
            if (error == null) return "unknown native error";
            string type = error.GetType().Name;
            // Diagnostics must not turn an ordinary refusal into a thrown pickup
            // operation if an error's formatter accesses a retired native object.
            try
            {
                string detail = (error.ToString() ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                return type + ": " + (detail.Length > 512 ? detail.Substring(0, 512) : detail);
            }
            catch { return type + " (description unavailable)"; }
        }

        private bool Eligible(Player player, Func<bool> isCurrent)
            => !_disposed && Current(_world, _generation, _isCurrent) && isCurrent != null && isCurrent()
                && player != null && !player.IsAI && ReferenceEquals(_world.MainPlayer, player)
                && ArenaPickupRuntime.IsLiveActor(player);

        private bool OwnsSource() => ReferenceEquals(_weapon.Parent?.GetOwner(), _owner);

        private void Removed(RemoveItemEventArgs args)
        {
            if (args.Status != CommandStatus.Succeed || !ReferenceEquals(args.Item, _weapon)
                || !(args.From is OwnerItself)) return;
            _consumed = true;
            _loot = null;
            _owner.RemoveItemEvent -= Removed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.AllowTransfer = null;
            // A retired pending request must leave no visible/interactable obstacle
            // while native completion still owns its eventual pool cleanup.
            if (_loot != null) _loot.gameObject.SetActive(false);
            // A queued operation's source hook will refuse it after disposal.
            // A move already inside native InProcess owns its own completion;
            // retain the source visual until rollback/success determines its owner.
            if (!IsPending) ReleaseSourceVisual();
        }

        private void ReleaseSourceVisual()
        {
            _owner.RemoveItemEvent -= Removed;
            var loot = _loot;
            _loot = null;
            // This is only the original source model, never an equipment/hands
            // model. A failed final callback may nevertheless leave the item carried;
            // its stale source representation must still be unregistered and pooled.
            if (loot != null && ReferenceEquals(loot.Item, _weapon)
                && ReferenceEquals(loot.ItemOwner, _owner) && _world != null)
                _world.DestroyLoot(loot);
        }
    }

    /// <summary>Only this arena item can cross this hook; ordinary loot is untouched.</summary>
    internal sealed class PickupOwner : ItemController
    {
        internal Func<ItemController, Item, ItemAddress, ItemAddress, bool> AllowTransfer;
        internal PickupOwner(Weapon weapon) : base(weapon, weapon.Id, weapon.ShortName) { }

        public override void OutProcess(ItemController executor, Item item, ItemAddress from,
            ItemAddress to, IInventoryOperation operation, Callback callback)
        {
            bool allowed = false;
            try { allowed = AllowTransfer?.Invoke(executor, item, from, to) == true; }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning($"Arena pickup: ownership check failed ({ex.Message}).");
            }
            callback(allowed ? (IResult)new SuccessfulResult() : new FailedResult("Arena pickup is no longer available."));
        }
    }
}
