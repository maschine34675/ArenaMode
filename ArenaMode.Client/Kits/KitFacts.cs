using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ArenaMode.Kits;

/// <summary>
/// What a kit holds, read once from its own item tree for the kit browser
/// (docs/reviews/KIT-PICKER-20261110.md): the short line under a card and the rows of the detail
/// pane. Everything resolves through the live item database, like <see cref="KitSummary"/>, so
/// a kit with a modded item describes itself. Cached per catalogue object: the catalogue is
/// re-fetched every raid, and the old entries die with their kits.
/// </summary>
internal sealed class KitFacts
{
    private static readonly ConditionalWeakTable<KitDefinition, KitFacts> Cache = new();

    private KitFacts() { }

    /// <summary>Two or three facts for a card, most decisive first.</summary>
    internal string CardLine { get; private set; } = string.Empty;

    /// <summary>Label and value per line of the detail pane; a kit without the thing has no line for it.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> Rows { get; private set; }
        = Array.Empty<KeyValuePair<string, string>>();

    internal static KitFacts For(KitDefinition kit)
    {
        if (kit?.Items == null) return new KitFacts();
        return Cache.GetValue(kit, Read);
    }

    private static KitFacts Read(KitDefinition kit)
    {
        var facts = new KitFacts();
        try
        {
            var rows = new List<KeyValuePair<string, string>>(8);
            var card = new List<string>(3);

            KitItemNode weaponNode = RootInSlot(kit, "FirstPrimaryWeapon");
            string weapon = NameOf(weaponNode?.Tpl);
            if (weapon != null)
            {
                string ammo = NameOf(AmmoOf(kit, weaponNode));
                rows.Add(Row("WEAPON", ammo != null ? weapon + "  ·  " + ammo : weapon));
            }

            string sidearm = NameOf(RootInSlot(kit, "Holster")?.Tpl);
            if (sidearm != null) rows.Add(Row("SIDEARM", sidearm));

            int armourClass = 0;
            try { armourClass = KitSummary.FrontBodyClass(kit); }
            catch (Exception ex) { Plugin.ModLogger.LogDebug($"Kit facts for '{kit.Id}': armour class unavailable ({ex.Message})."); }
            string armour = NameOf(RootInSlot(kit, "ArmorVest")?.Tpl) ?? (armourClass > 0 ? NameOf(RootInSlot(kit, "TacticalVest")?.Tpl) : null);
            rows.Add(Row("ARMOUR", armourClass > 0 ? "class " + armourClass + (armour != null ? "  ·  " + armour : string.Empty) : "none"));
            card.Add(armourClass > 0 ? "armour " + armourClass : "no armour");

            KitItemNode helmetNode = RootInSlot(kit, "Headwear");
            string helmet = NameOf(helmetNode?.Tpl);
            int helmetClass = helmetNode != null ? BestClass(kit, helmetNode) : 0;
            rows.Add(Row("HELMET", helmet == null ? "none" : helmetClass > 0 ? helmet + "  ·  class " + helmetClass : helmet));
            if (helmet != null) card.Add(helmetClass > 0 ? "helmet " + helmetClass : "helmet");

            string headset = NameOf(RootInSlot(kit, "Earpiece")?.Tpl);
            rows.Add(Row("HEADSET", headset ?? "none"));
            if (headset == null) card.Add("no headset");

            int spares = kit.Fill?.SpareMagazines ?? 0;
            if (spares > 0) rows.Add(Row("MAGAZINES", spares + " spare"));

            string supplies = Supplies(kit);
            if (supplies.Length > 0) rows.Add(Row("SUPPLIES", supplies));

            string backpack = NameOf(RootInSlot(kit, "Backpack")?.Tpl);
            if (backpack != null) rows.Add(Row("BACKPACK", backpack));

            facts.Rows = rows;
            facts.CardLine = string.Join("  ·  ", card.ToArray());
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Kit facts for '{kit.Id}' failed: {ex.Message}");
        }

        return facts;
    }

    private static KeyValuePair<string, string> Row(string label, string value) => new(label, value);

    /// <summary>The node worn directly in an equipment slot, never a same-named slot deeper in the tree.</summary>
    internal static KitItemNode RootInSlot(KitDefinition kit, string slotId)
    {
        if (kit?.Items == null) return null;
        var equipment = kit.Items.FirstOrDefault(n => string.IsNullOrEmpty(n.ParentId));
        if (equipment == null) return null;
        return kit.Items.FirstOrDefault(n => n.ParentId == equipment.Id
            && string.Equals(n.SlotId, slotId, StringComparison.OrdinalIgnoreCase));
    }

    private static string AmmoOf(KitDefinition kit, KitItemNode weapon)
    {
        if (weapon == null) return null;
        var inside = Subtree(kit, weapon);
        // The loaded magazine names the round the kit fights with; a weapon fed round by round
        // has it only in the chamber.
        KitItemNode round = kit.Items.FirstOrDefault(n => inside.Contains(n.Id)
                && string.Equals(n.SlotId, "cartridges", StringComparison.OrdinalIgnoreCase))
            ?? kit.Items.FirstOrDefault(n => inside.Contains(n.Id)
                && (n.SlotId ?? string.Empty).StartsWith("patron_in_weapon", StringComparison.OrdinalIgnoreCase));
        return round?.Tpl;
    }

    private static int BestClass(KitDefinition kit, KitItemNode root)
    {
        var inside = Subtree(kit, root);
        int best = 0;
        foreach (KitItemNode node in kit.Items)
            if (inside.Contains(node.Id) && Template(node.Tpl) is IArmorComponentTemplate armour)
                best = Math.Max(best, armour.ArmorClass);
        return best;
    }

    private static HashSet<string> Subtree(KitDefinition kit, KitItemNode root)
    {
        var inside = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (KitItemNode node in kit.Items)
                if (node.ParentId != null && inside.Contains(node.ParentId) && inside.Add(node.Id)) grew = true;
        }

        return inside;
    }

    private static string Supplies(KitDefinition kit)
    {
        var parts = new List<string>();
        foreach (KitStackedItem entry in kit.Fill?.RigContents ?? new List<KitStackedItem>())
        {
            string name = NameOf(entry?.Tpl);
            if (name == null) continue;
            parts.Add(entry.Count > 1 ? name + " ×" + entry.Count : name);
        }

        return string.Join(", ", parts.ToArray());
    }

    private static string NameOf(string tpl)
    {
        ItemTemplate template = Template(tpl);
        if (template == null) return null;
        try
        {
            string name = template.ShortNameLocalizationKey?.Localized();
            return string.IsNullOrWhiteSpace(name) ? template.Name : name;
        }
        catch { return null; }
    }

    private static ItemTemplate Template(string tpl)
    {
        if (string.IsNullOrEmpty(tpl)) return null;
        try
        {
            var factory = Comfort.Common.Singleton<ItemFactory>.Instance;
            return factory != null && factory.ItemTemplates.TryGetValue(new MongoID(tpl), out var template) ? template : null;
        }
        catch { return null; }
    }
}

/// <summary>
/// Detached items of a kit for the kit browser's pictures: the primary weapon with its
/// attachments and the worn pieces, built from the kit tree with fresh ids and never put on a
/// body, a profile or an inventory. The game renders their icons itself
/// (<c>ItemViewFactory.LoadItemIcon</c>); nothing here is drawn from files of ours. Built on
/// first use and kept per catalogue object; a piece that cannot be built is left out.
/// </summary>
internal sealed class KitPreviewItems
{
    private static readonly ConditionalWeakTable<KitDefinition, KitPreviewItems> Cache = new();
    private static readonly string[] GearSlots = { "ArmorVest", "TacticalVest", "Headwear", "Earpiece", "Holster", "Backpack" };

    private KitPreviewItems() { }

    internal Item Weapon { get; private set; }
    internal IReadOnlyList<Item> Gear { get; private set; } = Array.Empty<Item>();

    internal static KitPreviewItems For(KitDefinition kit)
    {
        if (kit?.Items == null) return new KitPreviewItems();
        return Cache.GetValue(kit, Build);
    }

    private static KitPreviewItems Build(KitDefinition kit)
    {
        var items = new KitPreviewItems();
        // An unknown template would throw halfway through a tree; such a kit has no pictures,
        // exactly as it has no body in a raid.
        if (KitBuilder.ValidateKit(kit).Count > 0) return items;
        try { items.Weapon = KitBuilder.BuildPrimaryWeapon(kit, 0, out _); }
        catch (Exception ex) { Plugin.ModLogger.LogDebug($"Kit preview for '{kit.Id}': weapon unavailable ({ex.Message})."); }

        var gear = new List<Item>(GearSlots.Length);
        var factory = Comfort.Common.Singleton<ItemFactory>.Instance;
        if (factory != null)
            foreach (string slot in GearSlots)
            {
                KitItemNode root = KitFacts.RootInSlot(kit, slot);
                if (root == null) continue;
                try
                {
                    Item piece = BuildTree(kit, root, factory);
                    if (piece != null) gear.Add(piece);
                }
                catch (Exception ex) { Plugin.ModLogger.LogDebug($"Kit preview for '{kit.Id}': {slot} unavailable ({ex.Message})."); }
            }

        items.Gear = gear;
        return items;
    }

    private static Item BuildTree(KitDefinition kit, KitItemNode root, ItemFactory factory)
    {
        Item top = factory.CreateItem(factory.NextId, root.Tpl, null);
        if (top == null) return null;
        var created = new Dictionary<string, Item>(StringComparer.Ordinal) { [root.Id] = top };
        var pending = kit.Items.Where(n => !ReferenceEquals(n, root) && !string.IsNullOrEmpty(n.ParentId)).ToList();
        for (int guard = 0; pending.Count > 0 && guard < 64; guard++)
        {
            bool attached = false;
            for (int index = pending.Count - 1; index >= 0; index--)
            {
                KitItemNode node = pending[index];
                if (!created.TryGetValue(node.ParentId, out Item parent)) continue;
                pending.RemoveAt(index);
                attached = true;
                // A part that will not attach (a plate the carrier refuses) is only missing from the picture.
                try { created[node.Id] = KitBuilder.CreateEntryNode(parent, node, factory); }
                catch (Exception) { }
            }

            if (!attached) break;
        }

        return top;
    }
}
