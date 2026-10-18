using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ArenaMode.Kits;

/// <summary>
/// One detached PMC-only loadout for a protected Tactical start. The caller owns
/// the exact armed raid/profile/no-body admission and required bundle await.
/// Preparation never transfers source items; commit is a one-shot transaction.
/// No profile clone, persisted follower, live controller or corpse is involved.
/// </summary>
public static class TeamArenaEntryKit
{
    private static readonly EquipmentSlot[] ProtectedSlots =
    {
        EquipmentSlot.SecuredContainer, EquipmentSlot.Scabbard, EquipmentSlot.ArmBand,
    };
    private static readonly ConditionalWeakTable<InventoryEquipment, PreparedOwner> Prepared = new();

    public static InventoryEquipment Prepare(Profile profile, KitDefinition kit)
    {
        InventoryEquipment source = profile?.Inventory?.Equipment
                                    ?? throw new InvalidOperationException("entry kit has no source equipment");
        ItemFactory factory = Singleton<ItemFactory>.Instance
                              ?? throw new InvalidOperationException("entry kit ItemFactory is unavailable");
        Dictionary<string, KitItemNode> nodes = ValidateDefinition(kit, out KitItemNode root);
        CompoundItem oldPockets = source.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem
                                  ?? throw new InvalidOperationException("entry kit source pockets are unavailable");

        var equipment = factory.CreateItem(factory.NextId, source.TemplateId, null) as InventoryEquipment
                        ?? throw new InvalidOperationException("entry kit could not create equipment root");
        var pockets = factory.CreateItem(factory.NextId, oldPockets.TemplateId, null) as CompoundItem
                      ?? throw new InvalidOperationException("entry kit could not create source pocket template");
        Slot pocketSlot = equipment.GetSlot(EquipmentSlot.Pockets)
                          ?? throw new InvalidOperationException("entry kit equipment has no pocket slot");
        var pocketAdd = pocketSlot.AddWithoutRestrictions(pockets);
        if (pocketAdd.Failed)
            throw new InvalidOperationException("entry kit could not attach source pocket template");

        var skipped = new HashSet<string>(StringComparer.Ordinal);
        foreach (KitItemNode node in nodes.Values)
        {
            if (node.ParentId == root.Id && ProtectedSlots.Any(slot =>
                    string.Equals(slot.ToString(), node.SlotId, StringComparison.OrdinalIgnoreCase)))
                skipped.Add(node.Id);
            if (nodes.TryGetValue(node.ParentId ?? "", out KitItemNode parent)
                && parent.ParentId == root.Id
                && string.Equals(parent.SlotId, "Pockets", StringComparison.OrdinalIgnoreCase)
                && oldPockets.Slots.Any(slot => slot.IsSpecial && slot.ID == node.SlotId))
                skipped.Add(node.Id);
        }
        bool changed;
        do
        {
            changed = false;
            foreach (KitItemNode node in nodes.Values)
                if (node.ParentId != null && skipped.Contains(node.ParentId))
                    changed |= skipped.Add(node.Id);
        } while (changed);

        var created = new Dictionary<string, Item>(StringComparer.Ordinal) { [root.Id] = equipment };
        var pending = nodes.Values.Where(node => node != root && !skipped.Contains(node.Id)).ToList();
        while (pending.Count > 0)
        {
            bool attached = false;
            for (int index = pending.Count - 1; index >= 0; index--)
            {
                KitItemNode node = pending[index];
                if (!created.TryGetValue(node.ParentId, out Item parent))
                    continue;
                bool isPockets = ReferenceEquals(parent, equipment)
                                 && string.Equals(node.SlotId, "Pockets", StringComparison.OrdinalIgnoreCase);
                created.Add(node.Id, isPockets ? pockets : KitBuilder.CreateEntryNode(parent, node, factory));
                pending.RemoveAt(index);
                attached = true;
            }
            if (!attached)
                throw new InvalidOperationException("entry kit contains an unresolved item subtree");
        }

        KitBuilder.FillEntryGrids(equipment, factory, kit, created);
        ValidatePrepared(source, equipment);
        Prepared.Add(equipment, new PreparedOwner(profile, source, equipment));
        return equipment;
    }

    public static void Commit(Profile profile, InventoryEquipment expectedOldEquipment,
        InventoryEquipment preparedEquipment)
    {
        if (preparedEquipment == null || !Prepared.TryGetValue(preparedEquipment, out PreparedOwner owner)
            || owner.Attempted || !ReferenceEquals(owner.Profile, profile)
            || !ReferenceEquals(owner.Inventory, profile?.Inventory)
            || !ReferenceEquals(owner.Source, expectedOldEquipment)
            || !ReferenceEquals(profile?.Inventory?.Equipment, expectedOldEquipment))
            throw new InvalidOperationException("entry kit preparation owner changed or was already committed");
        if (!owner.Unchanged(preparedEquipment))
            throw new InvalidOperationException("entry kit prepared equipment changed before commit");

        // Check again after the caller's asynchronous loading boundary. A caller
        // cannot inject an occupied protected destination or a partial weapon.
        ValidatePrepared(expectedOldEquipment, preparedEquipment);
        owner.Attempted = true;
        try
        {
            TeamArenaLoadoutSnapshot.CommitEntryEquipment(profile, expectedOldEquipment, preparedEquipment);
        }
        finally
        {
            // The issued equipment survives the raid; its preparation must not
            // keep the discarded personal combat tree alive for that duration.
            Prepared.Remove(preparedEquipment);
        }
    }

    private static Dictionary<string, KitItemNode> ValidateDefinition(KitDefinition kit, out KitItemNode root)
    {
        if (kit?.Items == null || kit.Items.Count == 0 || kit.Items.Count > 2048
            || kit.Items.Any(node => node == null || string.IsNullOrWhiteSpace(node.Id)
                || string.IsNullOrWhiteSpace(node.Tpl)))
            throw new InvalidOperationException("entry kit item graph is missing or malformed");
        var nodes = new Dictionary<string, KitItemNode>(StringComparer.Ordinal);
        foreach (KitItemNode node in kit.Items)
            if (!nodes.TryAdd(node.Id, node))
                throw new InvalidOperationException("entry kit contains duplicate item IDs");
        KitItemNode[] roots = kit.Items.Where(node => string.IsNullOrEmpty(node.ParentId)).ToArray();
        if (roots.Length != 1)
            throw new InvalidOperationException("entry kit must have exactly one equipment root");
        root = roots[0];
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KitItemNode node in kit.Items.Where(node => node != roots[0]))
        {
            if (string.IsNullOrWhiteSpace(node.SlotId))
                throw new InvalidOperationException("entry kit contains an unnamed destination");
            // Grid contents may share a grid ID. Slot collisions are rejected by
            // CreateEntryNode, while the root's one-slot-per-name rule is known now.
            if (node.ParentId == root.Id && !addresses.Add(node.SlotId))
                throw new InvalidOperationException("entry kit contains duplicate equipment slots");
            var chain = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            KitItemNode cursor = node;
            while (cursor != root)
            {
                if (cursor.ParentId == null || !nodes.TryGetValue(cursor.ParentId, out cursor)
                    || !chain.Add(cursor.Id))
                    throw new InvalidOperationException("entry kit contains an orphan or cyclic subtree");
            }
        }
        if (kit.Fill?.RigContents?.Any(entry => entry == null || string.IsNullOrWhiteSpace(entry.Tpl)) == true)
            throw new InvalidOperationException("entry kit contains an invalid supply");
        return nodes;
    }

    private static void ValidatePrepared(InventoryEquipment source, InventoryEquipment equipment)
    {
        if (ReferenceEquals(source, equipment) || equipment.CurrentAddress != null)
            throw new InvalidOperationException("entry kit root is not detached");
        CompoundItem oldPockets = source.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem;
        CompoundItem pockets = equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem;
        if (oldPockets == null || pockets == null || pockets.TemplateId != oldPockets.TemplateId)
            throw new InvalidOperationException("entry kit changed the pocket template");
        foreach (EquipmentSlot slot in ProtectedSlots)
            if (equipment.GetSlot(slot) == null || equipment.GetSlot(slot).ContainedItem != null)
                throw new InvalidOperationException("entry kit protected destination is occupied or missing");
        foreach (Slot special in oldPockets.Slots.Where(slot => slot.IsSpecial))
        {
            Slot destination = pockets.Slots.FirstOrDefault(slot => slot.ID == special.ID);
            if (destination == null || !destination.IsSpecial || destination.ContainedItem != null)
                throw new InvalidOperationException("entry kit special destination changed or is occupied");
        }

        Item[] items = equipment.GetAllItems().ToArray();
        var sourceIds = new HashSet<string>(source.GetAllItems().Select(item => item.Id.ToString()), StringComparer.Ordinal);
        var issuedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Item item in items)
            if (item == null || item.StackObjectsCount <= 0
                || !issuedIds.Add(item.Id.ToString()) || sourceIds.Contains(item.Id.ToString()))
                throw new InvalidOperationException("entry kit contains reused item identities");
        Weapon[] weapons = new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster }
            .Select(slot => equipment.GetSlot(slot)?.ContainedItem).OfType<Weapon>().ToArray();
        if (weapons.Length == 0 || weapons.SelectMany(weapon => weapon.GetAllItems())
                .OfType<CompoundItem>().Any(part => part.MissingVitalParts.Any()))
            throw new InvalidOperationException("entry kit has no usable weapon or a weapon is missing vital parts");
    }

    private sealed class PreparedOwner
    {
        internal PreparedOwner(Profile profile, InventoryEquipment source, InventoryEquipment equipment)
        {
            Profile = profile;
            Inventory = profile.Inventory;
            Source = source;
            _items = equipment.GetAllItems().ToArray();
            _addresses = _items.Select(item => (object)item.CurrentAddress).ToArray();
        }
        internal readonly Profile Profile;
        internal readonly EFT.InventoryLogic.Inventory Inventory;
        internal readonly InventoryEquipment Source;
        internal bool Attempted;
        private readonly Item[] _items;
        private readonly object[] _addresses;
        internal bool Unchanged(InventoryEquipment equipment)
        {
            Item[] current = equipment.GetAllItems().ToArray();
            return current.Length == _items.Length
                   && current.Select((item, index) => ReferenceEquals(item, _items[index])
                       && ReferenceEquals(item.CurrentAddress, _addresses[index])).All(same => same);
        }
    }
}
