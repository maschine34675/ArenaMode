using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Kits;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Session;

/// <summary>
/// The operations on the player's own kits behind the kit screen (docs/reviews/CUSTOM-KITS-20261110.md):
/// copy a kit, change a slot, change the ammunition, rename, delete. Every change is built as a new
/// kit, checked by both tiers (the structure rules and the engine probe) and saved at once; a change
/// that fails a check is refused with its reason and the stored kit stays as it was. Nothing here
/// touches the profile, the stash or the server: the kit file under BepInEx/config is the only write.
/// </summary>
internal static class ArenaKitWorkshop
{
    internal const string AmmoSlot = "ammo";

    /// <summary>The slots the editor offers, in the order of the rows.</summary>
    internal static readonly string[] Slots =
    {
        CustomKitStructure.PrimarySlot, AmmoSlot, "ArmorVest", "TacticalVest", "Headwear", "Earpiece", "Backpack",
    };

    private static readonly Dictionary<string, Item> Pictures = new(StringComparer.OrdinalIgnoreCase);

    internal static bool Locked => ArenaSession.IsActive || ArenaSession.ArmedForNextRaid;

    internal static string LockedReason => "Kits cannot be changed while a raid is starting or running.";

    internal static string Label(string slot)
    {
        switch (slot)
        {
            case CustomKitStructure.PrimarySlot: return "WEAPON";
            case AmmoSlot: return "AMMUNITION";
            case "ArmorVest": return "ARMOUR";
            case "TacticalVest": return "RIG";
            case "Headwear": return "HELMET";
            case "Earpiece": return "HEADSET";
            case "Backpack": return "BACKPACK";
            default: return slot?.ToUpperInvariant() ?? string.Empty;
        }
    }

    internal static bool Clearable(string slot)
        => slot == "ArmorVest" || slot == "Headwear" || slot == "Earpiece" || slot == "Backpack";

    /// <summary>Forgets the cached pictures; the kit screen calls it when it opens.</summary>
    internal static void Reset()
    {
        Pictures.Clear();
        CustomKitItems.Reset();
    }

    /// <summary>A new custom kit copied from <paramref name="source"/>; its id in <paramref name="id"/>, or the reason it was not made.</summary>
    internal static string Copy(KitDefinition source, IReadOnlyList<KitDefinition> stock, CustomKitView view, out string id)
    {
        id = null;
        if (Locked) return LockedReason;
        if (source == null) return "no kit to copy";
        if (view != null && view.ReadOnly) return view.Banner ?? "the kit file is read-only";
        if (view != null && view.Entries.Count >= CustomKitStore.MaxKits) return "at most " + CustomKitStore.MaxKits + " kits of your own";

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pickups.ArenaPickupWeapons.M60Id };
        foreach (KitDefinition kit in stock ?? Array.Empty<KitDefinition>()) if (kit?.Id != null) taken.Add(kit.Id);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CustomKitEntry entry in view?.Entries ?? new List<CustomKitEntry>())
        {
            if (entry.Id != null) taken.Add(entry.Id);
            if (entry.Kit?.Name != null) names.Add(entry.Kit.Name);
        }

        string newId = ArenaCustomKitId.NewId(taken.Contains);
        KitDefinition copy = CustomKitEditing.CopyOf(source, newId, NewName(source, names));
        // A stock kit as the raid serves it may carry the arena's optional grenades; an own kit
        // carries exactly what it was built with.
        copy.Fill.RigContents.RemoveAll(entry => entry == null || entry.OptionalQuickUseOnly);
        string refused = Check(copy, stock);
        if (refused != null) return refused;

        DateTime now = DateTime.UtcNow;
        string failed = CustomKitStore.Mutate(CustomKits.FilePath, records =>
        {
            if (records.Count >= CustomKitStore.MaxKits) return "at most " + CustomKitStore.MaxKits + " kits of your own";
            records.Add(CustomKitStore.ToRecord(copy, now));
            return null;
        });
        if (failed != null) return failed;
        id = newId;
        Plugin.ModLogger.LogDebug($"Arena custom kit '{copy.Name}' ({newId}) copied from '{source.Id}'.");
        return null;
    }

    /// <summary>Checks <paramref name="changed"/> by both tiers and stores it in place of the kit with its id.</summary>
    internal static string Apply(KitDefinition changed, IReadOnlyList<KitDefinition> stock)
    {
        if (Locked) return LockedReason;
        string refused = Check(changed, stock);
        if (refused != null) return refused;
        DateTime now = DateTime.UtcNow;
        return CustomKitStore.Mutate(CustomKits.FilePath, records =>
        {
            int index = CustomKitStore.IndexOf(records, changed.Id);
            if (index < 0) return "the kit is no longer in your kit file";
            records[index] = CustomKitStore.ToRecord(changed, now, records[index]);
            return null;
        });
    }

    internal static string Delete(string id)
    {
        if (Locked) return LockedReason;
        string failed = CustomKitStore.Mutate(CustomKits.FilePath, records =>
        {
            int index = CustomKitStore.IndexOf(records, id);
            if (index < 0) return "the kit is no longer in your kit file";
            records.RemoveAt(index);
            return null;
        });
        if (failed != null) return failed;

        // The stored choice and the favourite must not point at a kit that is gone.
        try
        {
            if (KitCatalog.CurrentChoice.Selects(id))
            {
                Settings.TrySelectKit(true, null);
                if (Settings.SelectedKit != null) Settings.SelectedKit.Value = string.Empty;
            }

            if (ArenaKitFavorites.Contains(ArenaKitFavorites.Parse(Settings.FavoriteKits?.Value), id))
                Settings.TryToggleFavoriteKit(id);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena custom kit deleted, but the choice could not be reset: {ex.Message}");
        }

        return null;
    }

    /// <summary>The kit with <paramref name="tpl"/> in <paramref name="slot"/> (as its default build), or null and the reason.</summary>
    internal static KitDefinition WithItem(KitDefinition kit, string slot, string tpl, out string reason)
    {
        reason = null;
        if (slot == AmmoSlot) return CustomKitItems.WithAmmo(kit, tpl, out reason);
        string keepAmmo = slot == CustomKitStructure.PrimarySlot ? CustomKitItems.CurrentAmmo(kit) : null;
        var rows = CustomKitItems.BuildRows(slot, tpl, keepAmmo, out reason);
        if (rows == null && keepAmmo != null)
            rows = CustomKitItems.BuildRows(slot, tpl, null, out reason); // the old round does not fit: the weapon's own
        if (rows == null) return null;
        KitDefinition changed = CustomKitEditing.ReplaceRootSlot(kit, slot, rows);
        if (slot == CustomKitStructure.PrimarySlot)
            changed = CustomKitEditing.WithoutSupplies(changed, CustomKitItems.ForeignSupplies(changed));
        return changed;
    }

    internal static KitDefinition Without(KitDefinition kit, string slot) => CustomKitEditing.ReplaceRootSlot(kit, slot, null);

    internal static KitDefinition Renamed(KitDefinition kit, string name, out string reason)
    {
        reason = null;
        string clean = CustomKitStructure.SanitizeName(name);
        if (clean.Length == 0) { reason = "a kit needs a name"; return null; }
        return CustomKitEditing.Renamed(kit, clean);
    }

    /// <summary>The editor rows of <paramref name="kit"/>.</summary>
    internal static List<ArenaKitEditorRow> Rows(KitDefinition kit, string activeSlot)
    {
        var rows = new List<ArenaKitEditorRow>();
        foreach (string slot in Slots)
        {
            var row = new ArenaKitEditorRow { Slot = slot, Label = Label(slot), CanChange = true, CanClear = Clearable(slot), Active = slot == activeSlot };
            if (slot == AmmoSlot)
            {
                string ammo = CustomKitItems.CurrentAmmo(kit);
                row.ItemName = ammo == null ? "none" : CustomKitItems.ShortName(ammo);
                double? price = ammo == null ? null : CustomKitPrices.Of(ammo);
                row.Detail = price == null ? string.Empty : ArenaKitCards.Money((int)Math.Round(price.Value)) + " a round";
                if (ammo != null) row.Picture = () => Picture(ammo);
                rows.Add(row);
                continue;
            }

            KitItemNode node = CustomKitEditing.RootSlot(kit, slot);
            if (node == null)
            {
                row.ItemName = "none";
                row.CanClear = false;
                rows.Add(row);
                continue;
            }

            row.ItemName = CustomKitItems.ShortName(node.Tpl);
            var tree = CustomKitEditing.Subtree(kit.Items, node.Id);
            double sum = kit.Items.Where(item => item != null && tree.Contains(item.Id)).Sum(item => CustomKitPrices.Of(item.Tpl) ?? 0d);
            int armour = ArmourClass(kit.Items.Where(item => item != null && tree.Contains(item.Id)).Select(item => item.Tpl));
            row.Detail = (armour > 0 ? "class " + armour + "  ·  " : string.Empty) + ArenaKitCards.Money((int)Math.Round(sum));
            string tpl = node.Tpl;
            if (slot == CustomKitStructure.PrimarySlot) row.Picture = () => KitPreviewItems.For(kit).Weapon;
            else row.Picture = () => Picture(tpl);
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The pick list of <paramref name="slot"/>, or null and the reason.</summary>
    internal static List<ArenaKitEditorChoice> Choices(KitDefinition kit, string slot, out string reason)
    {
        IReadOnlyList<CustomKitCandidate> candidates = slot == AmmoSlot
            ? CustomKitItems.AmmoOptions(kit, out reason)
            : CustomKitItems.Candidates(slot, out reason);
        string current = slot == AmmoSlot ? CustomKitItems.CurrentAmmo(kit) : CustomKitEditing.RootSlot(kit, slot)?.Tpl;
        var choices = new List<ArenaKitEditorChoice>(candidates.Count);
        foreach (CustomKitCandidate candidate in candidates)
        {
            string tpl = candidate.Tpl;
            choices.Add(new ArenaKitEditorChoice
            {
                Key = tpl,
                Name = candidate.Name,
                Detail = (candidate.ArmourClass > 0 ? "class " + candidate.ArmourClass + "  ·  " : string.Empty)
                         + ArenaKitCards.Money(candidate.Price) + (slot == AmmoSlot ? " a round" : string.Empty),
                Picture = () => Picture(tpl),
                Current = string.Equals(tpl, current, StringComparison.OrdinalIgnoreCase),
            });
        }

        return choices;
    }

    /// <summary>A detached item to take a picture of: the default build of the template, or the template alone.</summary>
    internal static Item Picture(string tpl)
    {
        if (string.IsNullOrEmpty(tpl)) return null;
        if (Pictures.TryGetValue(tpl, out var known)) return known;
        Item item = null;
        try
        {
            ItemFactory factory = Singleton<ItemFactory>.Instance;
            if (factory != null)
            {
                var id = new MongoID(tpl);
                item = factory._itemPresets != null && factory._itemPresets.ContainsKey(id)
                    ? factory.GetPresetItem(tpl)
                    : factory.CreateItem(factory.NextId, tpl, null);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogDebug($"Arena kit editor: no picture of {tpl}: {ex.Message}");
        }

        Pictures[tpl] = item;
        return item;
    }

    private static string Check(KitDefinition kit, IReadOnlyList<KitDefinition> stock)
    {
        var problems = CustomKitStructure.Validate(kit, CustomKitEnvelope.FromStock(stock));
        if (problems.Count > 0) return problems[0];
        string excluded = CustomKitItems.ExcludedItem(kit);
        if (excluded != null) return excluded;
        if (!CustomKitPrices.Loaded) return "the game's handbook is not loaded, so the kit cannot be priced";
        if (CustomKitPrices.PriceOf(kit) <= 0) return "the kit has no price";
        return CustomKitProbe.Check(kit);
    }

    private static string NewName(KitDefinition source, HashSet<string> taken)
    {
        string stem = CustomKitStructure.SanitizeName((string.IsNullOrWhiteSpace(source.Name) ? source.Id : source.Name.Trim()));
        if (stem.Length > CustomKitStructure.MaxNameLength - 8) stem = stem.Substring(0, CustomKitStructure.MaxNameLength - 8).TrimEnd();
        string name = CustomKitStructure.SanitizeName(stem + " copy");
        for (int number = 2; taken.Contains(name) && number < 100; number++)
            name = CustomKitStructure.SanitizeName(stem + " copy " + number);
        return name.Length == 0 ? "My kit" : name;
    }

    private static int ArmourClass(IEnumerable<string> templates)
    {
        int best = 0;
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        if (factory == null) return 0;
        foreach (string tpl in templates)
        {
            if (!CustomKitStructure.IsTemplateId(tpl)) continue;
            try
            {
                if (factory.ItemTemplates.TryGetValue(new MongoID(tpl), out var template) && template is IArmorComponentTemplate armour && armour.ArmorClass > best)
                    best = armour.ArmorClass;
            }
            catch (Exception) { }
        }

        return best;
    }
}
