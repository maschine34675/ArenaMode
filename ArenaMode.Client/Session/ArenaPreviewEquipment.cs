using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Session;

/// <summary>
/// Builds a fixed, detached menu mannequin loadout. It never reads or changes a
/// profile, draws a gameplay kit, creates an inventory owner, or starts a raid.
/// ArenaCharacterPreview passes the result through PlayerVisualRepresentation to
/// its own PlayerModelLoader, retaining and releasing the preview bundles itself.
/// </summary>
internal static class ArenaPreviewEquipment
{
    private const string EquipmentTemplate = "55d7217a4bdc2d86028b456d";
    private const string EquipmentKey = "equipment";

    // Parents precede children. These are template IDs, never reusable item IDs.
    // SPT 4.1 globals preset 5af08cf886f774223c269184 supplies the M4 structure;
    // its optional carry handle is replaced by a PK-06 on the upper receiver.
    private static readonly Node[] Usec =
    {
        new("m4", "5447a9cd4bdc2dbd208b4567", EquipmentKey, "FirstPrimaryWeapon"),
        new("m4-grip", "55d4b9964bdc2d1d4e8b456e", "m4", "mod_pistol_grip"),
        new("m4-magazine", "55d4887d4bdc2d962f8b4570", "m4", "mod_magazine"),
        new("m4-upper", "55d355e64bdc2d962f8b4569", "m4", "mod_reciever"),
        new("m4-barrel", "55d3632e4bdc2d972f8b4569", "m4-upper", "mod_barrel"),
        new("m4-muzzle", "544a38634bdc2d58388b4568", "m4-barrel", "mod_muzzle"),
        new("m4-gas", "5ae30e795acfc408fb139a0b", "m4-barrel", "mod_gas_block"),
        new("m4-handguard", "5ae30db85acfc408fb139a05", "m4-upper", "mod_handguard"),
        new("m4-handguard-bottom", "637f57a68d137b27f70c4968", "m4-handguard", "mod_handguard"),
        new("m4-tube", "5649be884bdc2d79388b4577", "m4", "mod_stock"),
        new("m4-stock", "55d4ae6c4bdc2d8b2f8b456e", "m4-tube", "mod_stock_000"),
        new("m4-charge", "55d44fd14bdc2d962f8b456e", "m4", "mod_charge"),
        new("m4-sight", "57ae0171245977343c27bfcf", "m4-upper", "mod_scope"),

        // AVS with class-4 SPRTN Omega plates and its required soft armor.
        new("avs", "544a5caa4bdc2d1a388b4568", EquipmentKey, "TacticalVest"),
        new("avs-soft-front", "6570e83223c1f638ef0b0ede", "avs", "Soft_armor_front"),
        new("avs-soft-back", "6570e87c23c1f638ef0b0ee2", "avs", "Soft_armor_back"),
        new("avs-groin", "6570e90b3a5689d85f08db97", "avs", "Groin"),
        new("avs-plate-front", "656f9fa0498d1b7e3e071d98", "avs", "Front_plate"),
        new("avs-plate-back", "656f9fa0498d1b7e3e071d98", "avs", "Back_plate"),
        // Black ULACH leaves the face open and permits the ComTac II.
        new("ulach", "5b40e1525acfc4771e1c6611", EquipmentKey, "Headwear"),
        new("ulach-top", "657112234269e9a568089eac", "ulach", "Helmet_top"),
        new("ulach-back", "657112a4818110db4600aa66", "ulach", "Helmet_back"),
        new("ulach-ears", "657112ce22996eaf110881fb", "ulach", "Helmet_ears"),
        new("comtac", "5645bcc04bdc2d363b8b4572", EquipmentKey, "Earpiece")
    };

    // SPT 4.1 globals preset 5acf7db286f7743a9c7092e3 supplies the AK-74M.
    // Bastion replaces its dust cover; the original rear sight must be omitted
    // because Bastion explicitly conflicts with that part.
    private static readonly Node[] Bear =
    {
        new("ak", "5ac4cd105acfc40016339859", EquipmentKey, "FirstPrimaryWeapon"),
        new("ak-gas", "59c6633186f7740cf0493bb9", "ak", "mod_gas_block"),
        new("ak-handguard", "5648b1504bdc2d9d488b4584", "ak-gas", "mod_handguard"),
        new("ak-muzzle", "5ac7655e5acfc40016339a19", "ak", "mod_muzzle"),
        new("ak-grip", "5649ade84bdc2d1b2b8b4587", "ak", "mod_pistol_grip"),
        new("ak-bastion", "5d2c76ed48f03532f2136169", "ak", "mod_reciever"),
        new("ak-stock", "5ac50c185acfc400163398d4", "ak", "mod_stock"),
        new("ak-magazine", "55d480c04bdc2d1d4e8b456a", "ak", "mod_magazine"),
        new("ak-sight", "57ae0171245977343c27bfcf", "ak-bastion", "mod_scope"),

        // TV-110 with class-4 Global Armor plates and its required soft armor.
        new("tv110", "5c0e746986f7741453628fe5", EquipmentKey, "TacticalVest"),
        new("tv110-soft-front", "6570df294cc0d2ab1e05ed74", "tv110", "Soft_armor_front"),
        new("tv110-soft-back", "6570df9c615f54368b04fca9", "tv110", "Soft_armor_back"),
        new("tv110-plate-front", "656fa0fb498d1b7e3e071d9c", "tv110", "Front_plate"),
        new("tv110-plate-back", "656fa0fb498d1b7e3e071d9c", "tv110", "Back_plate"),
        new("6b47", "5a7c4850e899ef00150be885", EquipmentKey, "Headwear"),
        new("6b47-top", "657baaf0b7e9ca9a02045c02", "6b47", "Helmet_top"),
        new("6b47-back", "657bab6ec6f689d3a205b85f", "6b47", "Helmet_back"),
        new("6b47-ears", "657babc6f58ba5a6250107a2", "6b47", "Helmet_ears"),
        new("gssh", "5b432b965acfc47a8774094e", EquipmentKey, "Earpiece")
    };

    /// <summary>
    /// Every call owns a fresh tree, including its root and repeated plates.
    /// Empty magazines/chambers are intentional: this is menu equipment only.
    /// A changed database contract throws rather than borrowing real equipment.
    /// </summary>
    public static InventoryEquipment Build(EPlayerSide side)
    {
        var nodes = side switch
        {
            EPlayerSide.Usec => Usec,
            EPlayerSide.Bear => Bear,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side,
                "Arena preview equipment requires a PMC faction.")
        };
        var factory = Comfort.Common.Singleton<ItemFactory>.Instance
            ?? throw new InvalidOperationException("Arena preview item factory is unavailable.");
        var equipment = Create(factory, EquipmentTemplate) as InventoryEquipment
            ?? throw new InvalidOperationException("Arena preview equipment root has changed type.");
        var items = new Dictionary<string, Item>(StringComparer.Ordinal)
        {
            [EquipmentKey] = equipment
        };

        foreach (var node in nodes)
        {
            if (!items.ContainsKey(node.Parent))
                throw new InvalidOperationException($"Arena preview parent '{node.Parent}' must precede '{node.Key}'.");
            items.Add(node.Key, Create(factory, node.Template));
        }

        // Populate children first so each completed weapon/armor subtree is
        // checked as a whole when attached to the detached equipment root.
        for (var index = nodes.Length - 1; index >= 0; index--)
        {
            var node = nodes[index];
            Attach(items[node.Parent], items[node.Key], node.Slot);
        }

        var missing = equipment.MissingVitalParts.FirstOrDefault();
        if (missing != null)
            throw new InvalidOperationException($"Arena preview is missing required slot '{missing.ID}'.");

        return equipment;
    }

    private static Item Create(ItemFactory factory, string template)
    {
        if (!factory.ItemTemplates.ContainsKey(new MongoID(template)))
            throw new InvalidOperationException($"Arena preview template '{template}' is unavailable.");

        return factory.CreateItem(factory.NextId, template, null)
            ?? throw new InvalidOperationException($"Arena preview template '{template}' produced no item.");
    }

    private static void Attach(Item parent, Item item, string slotId)
    {
        // Match only the declared direct parent, never a similarly named nested slot.
        var slot = (parent as CompoundItem)?.Slots.FirstOrDefault(candidate => candidate.ID == slotId)
            ?? throw new InvalidOperationException($"Arena preview parent '{parent.TemplateId}' has no slot '{slotId}'.");
        if (slot.ContainedItem != null || item.CurrentAddress != null || !slot.CheckCompatibility(item))
            throw new InvalidOperationException($"Arena preview item '{item.TemplateId}' cannot enter slot '{slotId}'.");
        if (slot.BlockerSlots.Count > 0 || slot.GetConflictingSlot(item).Any(other => other.ContainedItem != null))
            throw new InvalidOperationException($"Arena preview slot '{slotId}' is blocked.");
        var conflicts = slot.CheckConflictingItems(item);
        if (conflicts.Failed)
            throw new InvalidOperationException($"Arena preview item conflict in slot '{slotId}': {conflicts.Error}");

        // Required helmet/soft-armor inserts use locked slots. As in EFT's
        // ItemFactory.CreateAndFillItem, assemble those via AddWithoutRestrictions
        // after checking the actual item constraints above. Native slot-change
        // callbacks still run, but this tree has no owner or bound model yet:
        // do not RaiseEvents against a profile/controller.
        var added = slot.AddWithoutRestrictions(item);
        if (added.Failed)
            throw new InvalidOperationException($"Arena preview could not fill slot '{slotId}': {added.Error}");
    }

    private sealed class Node
    {
        public Node(string key, string template, string parent, string slot)
        {
            Key = key;
            Template = template;
            Parent = parent;
            Slot = slot;
        }

        public string Key { get; }
        public string Template { get; }
        public string Parent { get; }
        public string Slot { get; }
    }
}
