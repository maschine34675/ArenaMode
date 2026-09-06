using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Kits;

/// <summary>
/// What a kit actually contains, in the few lines a buyer needs.
///
/// The buy menu used to offer twenty names and twenty prices, which is not
/// enough to choose with: the names are invented and the price only says which
/// is dearer, not what the extra money buys. This reads the kit's own item tree
/// and reports the four things that decide a round — the gun, the body armour,
/// the helmet, and whether you can hear.
///
/// Everything is resolved through the live item database, so a kit carrying a
/// modded weapon describes itself correctly with no table to maintain here.
/// </summary>
internal static class KitSummary
{
    private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Forget the cached lines — the catalogue is re-fetched every raid.</summary>
    public static void Reset() => Cache.Clear();

    /// <summary>One short line per kit, cached because the menu redraws every frame.</summary>
    public static string For(KitDefinition kit)
    {
        if (kit?.Id == null)
            return string.Empty;

        if (Cache.TryGetValue(kit.Id, out var cached))
            return cached;

        var line = Build(kit);
        Cache[kit.Id] = line;
        return line;
    }

    private static string Build(KitDefinition kit)
    {
        try
        {
            var parts = new List<string>(4);

            var weapon = NameOf(TplInSlot(kit, "FirstPrimaryWeapon"));
            if (weapon != null)
                parts.Add(weapon);

            // Front protection is what the buyer compares. A class-6 SIDE plate
            // must not advertise a carrier whose installed front plate is class 4.
            var armour = FrontBodyClass(kit);
            if (armour > 0)
                parts.Add($"armour {armour}");

            var helmetClass = BestClass(kit, "Headwear");
            var helmet = NameOf(TplInSlot(kit, "Headwear"));
            if (helmet != null)
                parts.Add(helmetClass > 0 ? $"{helmet} ({helmetClass})" : helmet);

            var ears = NameOf(TplInSlot(kit, "Earpiece"));
            parts.Add(ears ?? "no headset");

            var spares = kit.Fill?.SpareMagazines ?? 0;
            if (spares > 0)
                parts.Add($"{spares} spare mag{(spares == 1 ? string.Empty : "s")}");

            return string.Join("  ·  ", parts.ToArray());
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Kit summary for '{kit.Id}' failed: {ex.Message}");
            return string.Empty;
        }
    }

    private static string TplInSlot(KitDefinition kit, string slotId)
        => kit.Items?.FirstOrDefault(n => string.Equals(n.SlotId, slotId, StringComparison.OrdinalIgnoreCase))?.Tpl;

    internal static int FrontBodyClass(KitDefinition kit)
    {
        if (kit?.Items == null) return 0;
        const EArmorPlateCollider chest = EArmorPlateCollider.Plate_Granit_SAPI_chest
                                         | EArmorPlateCollider.Plate_Korund_chest;
        var equipmentIds = new HashSet<string>(kit.Items.Where(n => string.IsNullOrEmpty(n.ParentId))
            .Select(n => n.Id), StringComparer.Ordinal);
        int frontPlate = 0, softFront = 0;
        foreach (var root in kit.Items.Where(n => n.ParentId != null && equipmentIds.Contains(n.ParentId)
                     && (string.Equals(n.SlotId, "ArmorVest", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(n.SlotId, "TacticalVest", StringComparison.OrdinalIgnoreCase))))
        {
            var rootTemplate = Template(root.Tpl);
            if (rootTemplate is IArmorComponentTemplate integral && integral.ArmorPlateColliders == 0
                && integral.ArmorColliders?.Contains(EBodyPartColliderType.RibcageUp) == true)
                softFront = Math.Max(softFront, integral.ArmorClass);

            if (!(rootTemplate is CompoundItemTemplate compound)) continue;
            foreach (var node in kit.Items.Where(n => n.ParentId == root.Id))
            {
                // Native ArmorComponent intersects the installed part's coverage
                // with its ArmorSlot. Looking at the part alone counts universal
                // plates in back/side slots, or spare plates stored in a rig grid.
                var slot = compound.Slots?.FirstOrDefault(s => s.ID == node.SlotId) as ArmorSlot;
                if (slot == null || !(Template(node.Tpl) is IArmorComponentTemplate armour)) continue;
                if ((armour.ArmorPlateColliders & slot.ArmorPlateColliderMask & chest) != 0)
                    frontPlate = Math.Max(frontPlate, armour.ArmorClass);
                else if (armour.ArmorPlateColliders == 0
                         && armour.ArmorColliders?.Contains(EBodyPartColliderType.RibcageUp) == true
                         && slot.ArmorColliders?.Contains(EBodyPartColliderType.RibcageUp) == true)
                    softFront = Math.Max(softFront, armour.ArmorClass);
            }
        }
        return frontPlate > 0 ? frontPlate : softFront;
    }

    /// <summary>
    /// The best armour class anywhere under the given slots, plates included.
    ///
    /// Walking the subtree rather than reading the base item, because that is
    /// where the protection lives: the same lesson the kit generator had to
    /// learn twice, once when bare plate carriers scored as class 6 and once
    /// when a helmet scored for a face shield it was about to lose.
    /// </summary>
    private static int BestClass(KitDefinition kit, params string[] slotIds)
    {
        var roots = kit.Items
            .Where(n => slotIds.Any(slot => string.Equals(n.SlotId, slot, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Id)
            .ToList();

        if (roots.Count == 0)
            return 0;

        // Everything hanging off those roots, however deep.
        var inside = new HashSet<string>(roots, StringComparer.Ordinal);
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var node in kit.Items)
            {
                if (node.ParentId != null && inside.Contains(node.ParentId) && inside.Add(node.Id))
                    grew = true;
            }
        }

        var best = 0;
        foreach (var node in kit.Items)
        {
            if (inside.Contains(node.Id))
                best = Math.Max(best, ArmorClass(node.Tpl));
        }

        return best;
    }

    private static int ArmorClass(string tpl)
    {
        var template = Template(tpl);
        if (template == null)
            return 0;

        try
        {
            // Through the interface, because the class lives on several unrelated
            // template types — ArmoredEquipmentTemplate for helmets and plates,
            // VestTemplate for armoured rigs — and all of them implement this.
            return template is IArmorComponentTemplate armour ? armour.ArmorClass : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string NameOf(string tpl)
    {
        var template = Template(tpl);
        if (template == null)
            return null;

        try
        {
            var name = template.ShortNameLocalizationKey?.Localized();
            return string.IsNullOrWhiteSpace(name) ? template.Name : name;
        }
        catch
        {
            return null;
        }
    }

    private static ItemTemplate Template(string tpl)
    {
        if (string.IsNullOrEmpty(tpl))
            return null;

        try
        {
            var factory = Comfort.Common.Singleton<ItemFactory>.Instance;
            return factory != null && factory.ItemTemplates.TryGetValue(new MongoID(tpl), out var template)
                ? template
                : null;
        }
        catch
        {
            return null;
        }
    }
}
