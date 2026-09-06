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

    /// <summary>
    /// Builds ONLY the kit's primary weapon, detached from any inventory, plus a
    /// few loaded spares to go with it.
    ///
    /// Gun Game promotes the player on every kill, and rebuilding the whole body
    /// for each promotion would mean a black screen after every fight. So the
    /// weapon is grown on its own from the same kit tree — same nodes, same
    /// attachments, same chambered round — and handed to the live player.
    /// Nodes outside the weapon simply never find a parent and are ignored.
    /// </summary>
    public static Weapon BuildPrimaryWeapon(KitDefinition kit, int spareMagazines, out List<Item> spares)
    {
        spares = new List<Item>();

        var factory = Comfort.Common.Singleton<ItemFactory>.Instance;
        if (factory == null || kit?.Items == null)
            return null;

        var rootNode = kit.Items.FirstOrDefault(
            n => string.Equals(n.SlotId, "FirstPrimaryWeapon", StringComparison.OrdinalIgnoreCase));

        if (rootNode == null)
        {
            Plugin.ModLogger.LogWarning($"Kit '{kit.Id}' has no primary weapon to hand out.");
            return null;
        }

        var weapon = factory.CreateItem(factory.NextId, rootNode.Tpl, null) as Weapon;
        if (weapon == null)
        {
            Plugin.ModLogger.LogWarning($"Kit '{kit.Id}': {rootNode.Tpl} is not a weapon.");
            return null;
        }

        ApplyUpd(weapon, rootNode.Upd);

        var created = new Dictionary<string, Item>(StringComparer.Ordinal) { [rootNode.Id] = weapon };
        var pending = kit.Items
            .Where(n => !ReferenceEquals(n, rootNode) && !string.IsNullOrEmpty(n.ParentId))
            .ToList();

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

                pending.RemoveAt(i);
                attachedAny = true;
            }

            // What is left belongs to the rest of the kit — armour, rig, meds.
            if (!attachedAny)
                break;
        }

        BuildSpareAmmo(factory, created, spareMagazines, spares);
        return weapon;
    }

    /// <summary>
    /// Reload supply for a handed-out weapon, cloned from its own magazine — the
    /// same fixed-magazine distinction the full kit builder makes, because a
    /// spare magazine for a weapon that is fed round by round is dead weight.
    /// </summary>
    private static void BuildSpareAmmo(ItemFactory factory, Dictionary<string, Item> created, int count, List<Item> spares)
    {
        if (count <= 0)
            return;

        var template = created.Values.OfType<Magazine>().FirstOrDefault(m => LoadedRound(m) != null);
        if (template == null)
            return;

        var ammoTpl = LoadedRound(template).TemplateId;

        if (template.ReloadMagType == Weapon.EReloadMode.InternalMagazine)
        {
            for (var i = 0; i < count; i++)
            {
                var ammo = factory.CreateItem(factory.NextId, ammoTpl, null);
                ammo.StackObjectsCount = Math.Max(template.MaxCount, ammo.StackMaxSize);
                spares.Add(ammo);
            }

            return;
        }

        for (var i = 0; i < count; i++)
        {
            var magazine = factory.CreateItem(factory.NextId, template.TemplateId, null) as Magazine;
            if (magazine == null)
                break;

            var ammo = factory.CreateItem(factory.NextId, ammoTpl, null);
            ammo.StackObjectsCount = Math.Max(1, Math.Min(magazine.MaxCount, ammo.StackMaxSize));

            var loadResult = magazine.Cartridges.Add(ammo, false);
            if (loadResult.Failed)
                Plugin.ModLogger.LogWarning($"Kit: loading a handed-out magazine failed: {loadResult.Error}");

            spares.Add(magazine);
        }
    }

    /// <summary>The rig/pockets/backpack of a live inventory, for callers outside the builder.</summary>
    public static List<CompoundItem> GridContainersOf(InventoryEquipment equipment)
        => equipment == null ? new List<CompoundItem>() : GridContainers(equipment);

    /// <summary>Places an item in the first grid cell that will take it.</summary>
    public static bool AddToAnyGridOf(List<CompoundItem> containers, Item item)
        => AddToAnyGridOf(containers, item, out _);

    /// <summary>As above, and says why not when nothing took it.</summary>
    public static bool AddToAnyGridOf(List<CompoundItem> containers, Item item, out string reason)
    {
        reason = null;
        return containers != null && item != null && AddToAnyGrid(containers, item, out reason);
    }

    /// <summary>Item name and cell size, for callers writing their own message.</summary>
    public static string DescribeItem(Item item) => Describe(item);

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
            // The stack slot rejects anything over the remaining capacity, and a
            // rejected load is quiet: the magazine stays empty, and the later
            // "find a loaded magazine" probe then skips the spare ammo entirely.
            // One mistyped round count in a kit would leave an empty gun with a
            // single chambered round — clamp instead.
            var room = magazine.MaxCount - magazine.Cartridges.Count;
            if (room > 0 && item.StackObjectsCount > room)
                item.StackObjectsCount = room;

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

    /// <summary>
    /// A round from a loaded magazine, whichever way it holds them. Ordinary
    /// magazines keep one stack; a revolver cylinder keeps a round per camora
    /// slot and its cartridge stack stays empty — probing only the stack would
    /// decide a loaded revolver has no ammunition and hand out no reserve.
    /// </summary>
    private static Item LoadedRound(Magazine magazine)
    {
        var stacked = magazine.Cartridges?.Items?.FirstOrDefault();
        if (stacked != null)
            return stacked;

        return magazine.AllSlots?
            .Select(slot => slot.ContainedItem)
            .FirstOrDefault(item => item != null);
    }

    private static void FillSpareMagazines(List<CompoundItem> containers, ItemFactory factory, Magazine template,
        string ammoTpl, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var magazine = factory.CreateItem(factory.NextId, template.TemplateId, null) as Magazine;
            if (magazine == null)
            {
                Plugin.ModLogger.LogWarning($"Kit: could not create a spare of {template.TemplateId}.");
                break;
            }

            var ammo = factory.CreateItem(factory.NextId, ammoTpl, null);
            ammo.StackObjectsCount = Math.Max(1, Math.Min(magazine.MaxCount, ammo.StackMaxSize));
            var loadResult = magazine.Cartridges.Add(ammo, false);
            if (loadResult.Failed)
                Plugin.ModLogger.LogWarning($"Kit: loading spare magazine failed: {loadResult.Error}");

            if (!AddToAnyGrid(containers, magazine, out var reason))
            {
                // Info when some fit, warning only when none did. A belt-fed
                // weapon carrying a 2x2 box genuinely cannot stow a second one in
                // a rig made of 1x2 cells; that is the loadout being honest about
                // itself, not something going wrong. Ending up with NO reserve at
                // all is the case worth raising a voice over.
                var message = $"Kit: {i} of {count} spare magazines fit — {Describe(magazine)} fits no "
                              + $"remaining cell ({reason})";

                if (i > 0)
                    Plugin.ModLogger.LogInfo(message + ".");
                else
                    Plugin.ModLogger.LogWarning(message + " — this loadout has no reserve at all.");

                break;
            }
        }
    }

    /// <summary>
    /// Loose rounds for fixed-magazine weapons — FULL stacks. Such a weapon is
    /// fed round by round, so handing out one magazine worth per stack would be
    /// a rounding error of a reserve.
    /// </summary>
    private static void FillLooseAmmo(List<CompoundItem> containers, ItemFactory factory, string ammoTpl,
        int roundsPerStack, int stacks)
    {
        for (var i = 0; i < stacks; i++)
        {
            var ammo = factory.CreateItem(factory.NextId, ammoTpl, null);
            ammo.StackObjectsCount = Math.Max(roundsPerStack, ammo.StackMaxSize);

            if (!AddToAnyGrid(containers, ammo, out var reason))
            {
                Plugin.ModLogger.LogWarning(
                    $"Kit: only {i} of {stacks} ammunition stacks fit - {Describe(ammo)} was refused: {reason}");
                break;
            }
        }
    }

    private static void FillGrids(InventoryEquipment equipment, ItemFactory factory, KitDefinition kit, Dictionary<string, Item> created)
    {
        if (kit.Fill == null)
            return;

        var containers = GridContainers(equipment);

        // Reload supply, cloned from the primary weapon's magazine.
        var template = created.Values.OfType<Magazine>().FirstOrDefault(m => LoadedRound(m) != null);
        if (template != null && kit.Fill.SpareMagazines > 0)
        {
            var ammoTpl = LoadedRound(template).TemplateId;

            // A weapon with a FIXED magazine is reloaded round by round, so spare
            // magazines are worse than useless there: they cannot be swapped in,
            // they clutter the rig, and the reload has nothing to draw from. Those
            // kits get loose ammunition instead — one stack per magazine asked for.
            if (template.ReloadMagType == Weapon.EReloadMode.InternalMagazine)
                FillLooseAmmo(containers, factory, ammoTpl, template.MaxCount, kit.Fill.SpareMagazines);
            else
                FillSpareMagazines(containers, factory, template, ammoTpl, kit.Fill.SpareMagazines);
        }

        // Optional Arena grenades never evict/repack authored supplies. The
        // native quick-throw container list excludes the backpack, whose free
        // space does not make a grenade available to the grenade key.
        var grenadeContainers = equipment.GrenadeThrowingSlots
            .Select(slot => slot?.ContainedItem).OfType<CompoundItem>()
            .Where(container => container.Grids?.Length > 0).ToList();
        foreach (var entry in (kit.Fill.RigContents ?? Enumerable.Empty<KitStackedItem>())
                     .OrderBy(entry => entry.OptionalQuickUseOnly))
        {
            for (var i = 0; i < (entry.OptionalQuickUseOnly ? 1 : Math.Max(1, entry.Count)); i++)
            {
                var item = factory.CreateItem(factory.NextId, entry.Tpl, null);
                if (entry.OptionalQuickUseOnly && !(item is ThrowWeap))
                {
                    Plugin.ModLogger.LogInfo($"Kit: optional grenade {entry.Tpl} is not a throwable in the live database; skipped.");
                    break;
                }
                if (!AddToAnyGrid(entry.OptionalQuickUseOnly ? grenadeContainers : containers, item, out var reason))
                {
                    if (entry.OptionalQuickUseOnly)
                        Plugin.ModLogger.LogInfo($"Kit: optional grenade {Describe(item)} skipped; no compatible quick-use space ({reason}).");
                    else
                        Plugin.ModLogger.LogWarning($"Kit: {Describe(item)} did not fit - {reason}");
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
        => AddToAnyGrid(containers, item, out _);

    /// <summary>
    /// Places an item in the first grid cell that will take it, and says what it
    /// was that nothing would take.
    ///
    /// "No grid space" turned out to be literally true, and the information
    /// actually missing was the item's SIZE. In Gun Game the spare is cloned
    /// from the RUNG's weapon, not the base kit's, and eight of the 139 pool
    /// weapons feed from a 2x2 belt box or drum - RPD/RPDN, PKM/PKP, M60E4/E6
    /// and the Saiga-12K drum. The rigs are built from 1x2 and 1x1 cells and the
    /// pockets are 1x1, so the only grid in the whole loadout that can hold one
    /// is the backpack's single 3x3, which holds exactly one.
    ///
    /// Note that `reason` cannot tell a full grid from a rejecting filter:
    /// Grid.FindFreeSpace returns null for both and AddInternal turns a null
    /// location into NoFreeSpaceError either way. Describe()'s cell size is what
    /// carries the answer.
    /// </summary>
    private static bool AddToAnyGrid(List<CompoundItem> containers, Item item, out string reason)
    {
        reason = containers == null || containers.Count == 0
            ? "the kit has no rig, pockets or backpack to put it in"
            : null;

        if (containers == null)
            return false;

        foreach (var container in containers)
        {
            foreach (var grid in container.Grids)
            {
                var result = grid.AddAnywhere(item, EErrorHandlingType.Ignore);
                if (!result.Failed)
                    return true;

                // Last one wins: the containers are tried best-first, so the final
                // refusal is the one from the least suitable place — but any of
                // them names the kind of refusal, which is what is missing today.
                reason = result.Error?.ToString() ?? reason;
            }
        }

        return false;
    }

    /// <summary>What an item is called and how big it is, for a message a human reads.</summary>
    private static string Describe(Item item)
    {
        if (item == null)
            return "(null)";

        try
        {
            var size = item.CalculateCellSize();
            return $"{item.ShortName?.Localized() ?? item.TemplateId.ToString()} ({size.X}x{size.Y})";
        }
        catch
        {
            return item.TemplateId.ToString();
        }
    }

    /// <summary>
    /// Register kit magazines/chambers as inspected so ammo counts display normally.
    /// Runs AFTER the surgeon's sanitize (which only prunes stale entries).
    /// </summary>
    public static void RegisterMagazinesAsChecked(Profile profile)
    {
        try
        {
            RegisterAsChecked(profile, profile.Inventory.GetPlayerItems(EPlayerItems.Equipment));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Kit: magazine registration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The same registration for a NAMED set of items.
    ///
    /// Mid-raid the difference matters: sweeping the whole inventory would also
    /// reveal the round count of every magazine the player looted off a corpse
    /// and never inspected — information the game deliberately withheld. Only
    /// what this mod handed out is ours to declare inspected.
    /// </summary>
    public static void RegisterAsChecked(Profile profile, IEnumerable<Item> items)
    {
        if (profile == null || items == null)
            return;

        foreach (var item in items)
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
}
