using ArenaMode.Events;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using Redeploy.Core.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Kits;

/// <summary>
/// Instantiates a kit's preset-style item tree into a live equipment root via the
/// game's ItemFactory. Every node gets a FRESH id (factory.NextId) and parent
/// references are remapped — the static ids in the kit JSON are template-only
/// (instantiating them directly would corrupt the profile on the second use).
/// </summary>
public static class KitBuilder
{
    /// <summary>
    /// Applies a kit to the profile by swapping in a fresh equipment tree
    /// (EquipmentSurgeon keeps secure container / special slots) populated
    /// from the kit definition, then registers magazines as known.
    /// </summary>
    public static void ApplyToProfile(Profile profile, LocalPlayer oldBody, KitDefinition kit, bool requireCorpse)
    {
        // MUST run before ReplaceEquipment: the surgeon moves the secured container /
        // scabbard / armband into the new tree BEFORE populate — a CreateItem throw on
        // an unknown tpl mid-populate would strand those items in a detached root.
        var invalid = ValidateKit(kit);
        if (invalid.Count > 0)
            throw new InvalidOperationException(
                $"Kit '{kit.Id}' references unknown templates: {string.Join(", ", invalid)}");

        EquipmentSurgeon.ReplaceEquipment(
            profile,
            oldBody,
            (equipment, factory) => Populate(equipment, factory, kit),
            requireCorpse);

        RegisterMagazinesAsChecked(profile);
        ArenaEvents.RaiseKitApplied(kit);
    }

    /// <summary>All template ids in the kit that the item DB does not know (empty = kit is safe to build).</summary>
    public static List<string> ValidateKit(KitDefinition kit)
    {
        var invalid = new List<string>();
        var factory = Comfort.Common.Singleton<ItemFactory>.Instance;
        if (factory == null)
            return invalid;

        void Check(string tpl)
        {
            if (string.IsNullOrEmpty(tpl))
                return;

            try
            {
                if (!factory.ItemTemplates.TryGetValue(new MongoID(tpl), out _))
                    invalid.Add(tpl);
            }
            catch
            {
                invalid.Add(tpl);
            }
        }

        foreach (var node in kit.Items)
        {
            if (!string.IsNullOrEmpty(node.ParentId))
                Check(node.Tpl);
        }

        foreach (var entry in kit.Fill?.RigContents ?? new List<KitStackedItem>())
            Check(entry.Tpl);

        return invalid;
    }

    private static void Populate(InventoryEquipment equipment, ItemFactory factory, KitDefinition kit)
    {
        var created = new Dictionary<string, Item>(StringComparer.Ordinal);

        // Map the kit's root node (no parent) onto the live equipment root.
        var pending = new List<KitItemNode>();
        foreach (var node in kit.Items)
        {
            if (string.IsNullOrEmpty(node.ParentId))
                created[node.Id] = equipment;
            else
                pending.Add(node);
        }

        // Parents before children: repeatedly attach nodes whose parent already exists.
        var guard = 0;
        while (pending.Count > 0 && guard++ < 64)
        {
            var attachedAny = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var node = pending[i];
                if (!created.TryGetValue(node.ParentId, out var parent))
                    continue;

                var attached = CreateAndAttach(parent, node, factory);
                if (attached != null)
                    created[node.Id] = attached;

                // A failed attach deliberately leaves the node unmapped so its whole
                // subtree surfaces as logged orphans instead of building detached.
                pending.RemoveAt(i);
                attachedAny = true;
            }

            if (!attachedAny)
                break;
        }

        foreach (var orphan in pending)
            Plugin.ModLogger.LogWarning($"Kit '{kit.Id}': orphan node {orphan.Tpl} (parent {orphan.ParentId} missing).");

        FillGrids(equipment, factory, kit, created);
    }

    private static Item CreateAndAttach(Item parent, KitItemNode node, ItemFactory factory)
    {
        // Occupied target slot (Pockets from the surgeon, transferred SecuredContainer/
        // Scabbard/ArmBand): reuse the live item as mapping target for the node's
        // children instead of fighting over the slot.
        var existing = FindOccupied(parent, node.SlotId);
        if (existing != null)
            return existing;

        var item = factory.CreateItem(factory.NextId, node.Tpl, null);
        ApplyUpd(item, node.Upd);

        // Magazine ammo lives in a StackSlot, which is not a Slot.
        if (parent is Magazine magazine && node.SlotId == "cartridges")
        {
            var stackResult = magazine.Cartridges.Add(item, false);
            if (stackResult.Failed)
            {
                Plugin.ModLogger.LogWarning($"Kit: stack add {node.Tpl} into cartridges failed: {stackResult.Error}");
                return null;
            }

            return item;
        }

        var slot = FindSlot(parent, node.SlotId);
        if (slot != null)
        {
            var addResult = slot.AddWithoutRestrictions(item);
            if (addResult.Failed)
            {
                Plugin.ModLogger.LogWarning($"Kit: add {node.Tpl} into {node.SlotId} failed: {addResult.Error}");
                return null;
            }

            return item;
        }

        // Grid target (e.g. slotId "main" inside a rig/backpack) — exact id only, a
        // typo'd slotId must not silently reroute into the first grid.
        if (parent is CompoundItem grids)
        {
            var grid = grids.Grids?.FirstOrDefault(g => g.ID == node.SlotId);
            if (grid != null)
            {
                var addResult = grid.AddAnywhere(item, EErrorHandlingType.Ignore);
                if (addResult.Failed)
                {
                    Plugin.ModLogger.LogWarning($"Kit: grid add {node.Tpl} failed: {addResult.Error}");
                    return null;
                }

                return item;
            }
        }

        Plugin.ModLogger.LogWarning($"Kit: no slot '{node.SlotId}' on {parent.TemplateId} for {node.Tpl}.");
        return null;
    }

    private static Slot FindSlot(Item parent, string slotId)
    {
        switch (parent)
        {
            case InventoryEquipment equipment when Enum.TryParse<EquipmentSlot>(slotId, true, out var equipmentSlot):
                return equipment.GetSlot(equipmentSlot);
            case CompoundItem compound:
                return compound.AllSlots.FirstOrDefault(s => s.ID == slotId);
            default:
                return null;
        }
    }

    private static Item FindOccupied(Item parent, string slotId)
    {
        if (slotId == "cartridges")
            return null;

        return FindSlot(parent, slotId)?.ContainedItem;
    }

    private static void ApplyUpd(Item item, JObject upd)
    {
        if (upd == null)
            return;

        try
        {
            var stack = upd["StackObjectsCount"]?.Value<int>();
            if (stack.HasValue)
                item.StackObjectsCount = stack.Value;

            if (upd["Repairable"] is JObject repairable)
            {
                var component = item.GetItemComponent<RepairableComponent>();
                if (component != null)
                {
                    component.MaxDurability = repairable["MaxDurability"]?.Value<float>() ?? component.MaxDurability;
                    component.Durability = repairable["Durability"]?.Value<float>() ?? component.Durability;
                }
            }

            if (upd["FireMode"] is JObject fireMode)
            {
                var component = item.GetItemComponent<FireModeComponent>();
                var modeName = fireMode["FireMode"]?.Value<string>();
                if (component != null && modeName != null && Enum.TryParse<Weapon.EFireMode>(modeName, true, out var mode))
                    component.SetFireMode(mode);
            }

            if (upd["Foldable"] is JObject foldable)
            {
                var component = item.GetItemComponent<FoldableComponent>();
                if (component != null)
                    component.Folded = foldable["Folded"]?.Value<bool>() ?? component.Folded;
            }

            if (upd["MedKit"] is JObject medkit)
            {
                var component = item.GetItemComponent<MedKitComponent>();
                if (component != null)
                    component.HpResource = medkit["HpResource"]?.Value<float>() ?? component.HpResource;
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Kit: applying upd to {item.TemplateId} failed: {ex.Message}");
        }
    }

    private static void FillGrids(InventoryEquipment equipment, ItemFactory factory, KitDefinition kit, Dictionary<string, Item> created)
    {
        if (kit.Fill == null)
            return;

        var containers = GridContainers(equipment);

        // Spare loaded magazines cloned from the primary weapon's magazine.
        var template = created.Values.OfType<Magazine>().FirstOrDefault(m => m.Cartridges.Items.Any());
        if (template != null && kit.Fill.SpareMagazines > 0)
        {
            var ammoTpl = template.Cartridges.Items.First().TemplateId;
            for (var i = 0; i < kit.Fill.SpareMagazines; i++)
            {
                var magazine = factory.CreateItem(factory.NextId, template.TemplateId, null) as Magazine;
                if (magazine == null)
                    break;

                var ammo = factory.CreateItem(factory.NextId, ammoTpl, null);
                ammo.StackObjectsCount = magazine.MaxCount;
                var loadResult = magazine.Cartridges.Add(ammo, false);
                if (loadResult.Failed)
                    Plugin.ModLogger.LogWarning($"Kit: loading spare magazine failed: {loadResult.Error}");

                if (!AddToAnyGrid(containers, magazine))
                {
                    Plugin.ModLogger.LogWarning("Kit: no grid space for spare magazine.");
                    break;
                }
            }
        }

        foreach (var entry in kit.Fill.RigContents ?? Enumerable.Empty<KitStackedItem>())
        {
            for (var i = 0; i < Math.Max(1, entry.Count); i++)
            {
                var item = factory.CreateItem(factory.NextId, entry.Tpl, null);
                if (!AddToAnyGrid(containers, item))
                {
                    Plugin.ModLogger.LogWarning($"Kit: no grid space for {entry.Tpl}.");
                    break;
                }
            }
        }
    }

    private static List<CompoundItem> GridContainers(InventoryEquipment equipment)
    {
        var containers = new List<CompoundItem>();
        foreach (var slot in new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets, EquipmentSlot.Backpack })
        {
            if (equipment.GetSlot(slot)?.ContainedItem is CompoundItem compound && compound.Grids?.Length > 0)
                containers.Add(compound);
        }

        return containers;
    }

    private static bool AddToAnyGrid(List<CompoundItem> containers, Item item)
    {
        foreach (var container in containers)
        {
            foreach (var grid in container.Grids)
            {
                if (!grid.AddAnywhere(item, EErrorHandlingType.Ignore).Failed)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Register kit magazines/chambers as inspected so ammo counts display normally.
    /// Runs AFTER the surgeon's sanitize (which only prunes stale entries).
    /// </summary>
    private static void RegisterMagazinesAsChecked(Profile profile)
    {
        try
        {
            foreach (var item in profile.Inventory.GetPlayerItems(EPlayerItems.Equipment))
            {
                switch (item)
                {
                    case Magazine magazine:
                        // Value semantics are the mag-check SKILL LEVEL (display precision),
                        // not the ammo count — a high constant always shows exact counts.
                        profile.CheckedMagazines[magazine.Id] = 60;
                        break;
                    case Weapon weapon:
                        // CheckedChambers is keyed by WEAPON id (Profile.CheckChamber),
                        // not by the chambered round's id.
                        if (weapon.Chambers.Any(c => c.ContainedItem != null)
                            && !profile.CheckedChambers.Contains(weapon.Id))
                        {
                            profile.CheckedChambers.Add(weapon.Id);
                        }

                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Kit: magazine registration failed: {ex.Message}");
        }
    }
}
