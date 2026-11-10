using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ArenaMode.Kits;

/// <summary>
/// Handbook prices on the client (docs/reviews/CUSTOM-KITS-20261110.md): the same numbers the server's
/// KitPricer reads, from the game's own handbook, which the menu loads with the profile. The
/// indexer answers null for an unknown template (GetBasePrice would log an error for it).
/// </summary>
internal static class CustomKitPrices
{
    internal static bool Loaded
    {
        get
        {
            try { return Singleton<Handbook>.Instantiated && Singleton<Handbook>.Instance?.Items?.Length > 0; }
            catch (Exception) { return false; }
        }
    }

    internal static double? Of(string tpl)
    {
        if (string.IsNullOrEmpty(tpl) || !Singleton<Handbook>.Instantiated) return null;
        try
        {
            HandbookNode node = Singleton<Handbook>.Instance[tpl];
            return node?.Data == null ? (double?)null : node.Data.Price;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The kit's price by the shared rule, or 0 when the handbook is not loaded.</summary>
    internal static int PriceOf(KitDefinition kit) => Loaded ? CustomKitEditing.Price(kit, Lookup) : 0;

    internal static readonly Func<string, double?> Lookup = Of;
}

/// <summary>One item a slot of the editor can take.</summary>
internal sealed class CustomKitCandidate
{
    internal string Tpl;
    internal string Name;
    /// <summary>The handbook price of what a pick adds: the default build with its inserts and rounds, or the template alone.</summary>
    internal int Price;
    /// <summary>The highest armour class in it; 0 for none.</summary>
    internal int ArmourClass;
}

/// <summary>
/// The engine side of the kit editor (docs/reviews/CUSTOM-KITS-20261110.md): which templates a slot
/// takes, the item tree a pick adds (the game's default preset of the template, a weapon armed with
/// a loaded magazine and a round in every chamber, which presets lack), the ammunition a weapon takes,
/// and the rows of a live tree. Everything goes through the running game's item database and fails
/// with a reason, never with an exception into the window.
/// </summary>
internal static class CustomKitItems
{
    private static readonly Dictionary<string, List<CustomKitCandidate>> BySlot = new(StringComparer.Ordinal);
    private static readonly Dictionary<ItemTemplate, ItemTemplate[]> Children = new();

    /// <summary>Forgets the candidate lists; the kit window calls it when it opens (a content mod may differ between sessions).</summary>
    internal static void Reset()
    {
        BySlot.Clear();
        Children.Clear();
        _ammoKit = null;
        _ammoList = null;
        _ammoReason = null;
    }

    internal static IReadOnlyList<CustomKitCandidate> Candidates(string rootSlot, out string reason)
    {
        reason = null;
        if (BySlot.TryGetValue(rootSlot ?? string.Empty, out var cached)) return cached;
        var list = new List<CustomKitCandidate>();
        try
        {
            ItemFactory factory = Singleton<ItemFactory>.Instance;
            if (factory == null) { reason = "the item database is not loaded"; return list; }
            if (!(Template(factory, CustomKitStructure.EquipmentTemplate) is CompoundItemTemplate equipment)) { reason = "no equipment template"; return list; }
            Slot slot = equipment.Slots?.FirstOrDefault(s => s.ID == rootSlot);
            if (slot == null) { reason = "no slot " + rootSlot; return list; }

            HashSet<string> excluded = ExcludedSet();
            foreach (ItemTemplate template in Expand(slot.Filters))
            {
                var facts = Facts(factory, template);
                if (!CustomKitCandidates.Accept(facts, excluded)) continue;
                Summarise(factory, rootSlot, template, out int price, out int armour);
                list.Add(new CustomKitCandidate { Tpl = facts.Tpl, Name = ShortName(template), Price = price, ArmourClass = armour });
            }

            list.Sort((a, b) => a.Price != b.Price ? a.Price.CompareTo(b.Price) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            BySlot[rootSlot] = list;
        }
        catch (Exception ex)
        {
            reason = "the list could not be read: " + ex.Message;
        }

        return list;
    }

    /// <summary>
    /// The rows of the item tree a pick of <paramref name="tpl"/> adds to <paramref name="rootSlot"/>:
    /// the default preset (or the template with its locked inserts filled), a weapon loaded with
    /// <paramref name="ammoTpl"/> or its default round. The row without a parent is the tree's own root.
    /// </summary>
    internal static List<KitItemNode> BuildRows(string rootSlot, string tpl, string ammoTpl, out string reason)
    {
        reason = null;
        try
        {
            ItemFactory factory = Singleton<ItemFactory>.Instance;
            if (factory == null) { reason = "the item database is not loaded"; return null; }
            Item item = Create(factory, tpl, out reason);
            if (item == null) return null;
            if (item is Weapon weapon)
            {
                reason = Arm(factory, weapon, ammoTpl);
                if (reason != null) return null;
            }

            return Rows(item);
        }
        catch (Exception ex)
        {
            reason = "the item could not be built: " + ex.Message;
            return null;
        }
    }

    // The ammunition list of the last kit asked for: the window asks again on every redraw while the
    // list is open, and every edit is a new kit object, so one entry by reference is enough.
    private static KitDefinition _ammoKit;
    private static IReadOnlyList<CustomKitCandidate> _ammoList;
    private static string _ammoReason;

    /// <summary>The ammunition the kit's primary weapon takes in its chamber and its magazine, cheapest first.</summary>
    internal static IReadOnlyList<CustomKitCandidate> AmmoOptions(KitDefinition kit, out string reason)
    {
        if (kit != null && ReferenceEquals(kit, _ammoKit))
        {
            reason = _ammoReason;
            return _ammoList;
        }

        IReadOnlyList<CustomKitCandidate> list = ReadAmmoOptions(kit, out reason);
        _ammoKit = kit;
        _ammoList = list;
        _ammoReason = reason;
        return list;
    }

    private static IReadOnlyList<CustomKitCandidate> ReadAmmoOptions(KitDefinition kit, out string reason)
    {
        reason = null;
        var list = new List<CustomKitCandidate>();
        try
        {
            ItemFactory factory = Singleton<ItemFactory>.Instance;
            if (factory == null) { reason = "the item database is not loaded"; return list; }
            Weapon weapon = KitBuilder.BuildPrimaryWeapon(kit, 0, out _);
            if (weapon == null) { reason = "the kit has no weapon"; return list; }

            var slots = new List<Slot>(weapon.Chambers ?? Array.Empty<Slot>());
            Magazine magazine = weapon.GetCurrentMagazine();
            HashSet<string> excluded = ExcludedSet();
            foreach (ItemTemplate template in Expand(slots.Count > 0 ? slots[0].Filters : magazine?.Cartridges?.Filters))
            {
                if (!(template is AmmoTemplate)) continue;
                Item round = factory.CreateItem(factory.NextId, template._id.ToString(), null);
                if (round == null || slots.Any(s => !s.CheckCompatibility(round))) continue;
                if (magazine != null && !(magazine is CylinderMagazine) && magazine.Cartridges != null && !magazine.Cartridges.CheckCompatibility(round)) continue;
                var facts = Facts(factory, template);
                if (!CustomKitCandidates.Accept(facts, excluded)) continue;
                list.Add(new CustomKitCandidate { Tpl = facts.Tpl, Name = ShortName(template), Price = (int)Math.Round(facts.HandbookPrice ?? 0) });
            }

            list.Sort((a, b) => a.Price != b.Price ? a.Price.CompareTo(b.Price) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0) reason = "no ammunition fits this weapon";
        }
        catch (Exception ex)
        {
            reason = "the ammunition could not be read: " + ex.Message;
        }

        return list;
    }

    /// <summary>The round in the kit's primary weapon (its magazine or chamber), or null.</summary>
    internal static string CurrentAmmo(KitDefinition kit)
    {
        var gun = CustomKitEditing.RootSlot(kit, CustomKitStructure.PrimarySlot);
        if (gun == null) return null;
        var tree = CustomKitEditing.Subtree(kit.Items, gun.Id);
        return kit.Items.FirstOrDefault(n => n != null && tree.Contains(n.Id) && IsRoundSlot(n.SlotId))?.Tpl;
    }

    /// <summary>The kit with every round of its primary weapon (magazine, chambers, cylinder) made <paramref name="ammoTpl"/>.</summary>
    internal static KitDefinition WithAmmo(KitDefinition kit, string ammoTpl, out string reason)
    {
        reason = null;
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        if (factory == null) { reason = "the item database is not loaded"; return null; }
        if (!(Template(factory, ammoTpl) is AmmoTemplate ammo)) { reason = "not ammunition"; return null; }
        KitDefinition copy = CustomKitEditing.Clone(kit);
        var gun = CustomKitEditing.RootSlot(copy, CustomKitStructure.PrimarySlot);
        if (gun == null) { reason = "the kit has no weapon"; return null; }
        var tree = CustomKitEditing.Subtree(copy.Items, gun.Id);
        foreach (KitItemNode node in copy.Items)
        {
            if (node == null || !tree.Contains(node.Id) || !IsRoundSlot(node.SlotId)) continue;
            node.Tpl = ammoTpl;
            if (node.Upd?["StackObjectsCount"] is JValue count && count.Type == JTokenType.Integer && (long)count > Math.Max(1, ammo.StackMaxSize))
                node.Upd["StackObjectsCount"] = Math.Max(1, ammo.StackMaxSize);
        }

        return copy;
    }

    /// <summary>Loose rounds among the kit's supplies that its primary weapon cannot fire (another calibre).</summary>
    internal static Func<string, bool> ForeignSupplies(KitDefinition kit)
    {
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        var gun = CustomKitEditing.RootSlot(kit, CustomKitStructure.PrimarySlot);
        string calibre = factory == null || gun == null ? null : (Template(factory, gun.Tpl) as WeaponTemplate)?.ammoCaliber;
        return tpl => calibre != null && factory != null && Template(factory, tpl) is AmmoTemplate round
                      && !string.Equals(round.Caliber, calibre, StringComparison.OrdinalIgnoreCase);
    }

    internal static string ShortName(string tpl)
    {
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        ItemTemplate template = factory == null ? null : Template(factory, tpl);
        return template == null ? tpl : ShortName(template);
    }

    /// <summary>The rows of a detached item tree: ids, templates, parent and slot from the live containers, a stack count where there is a stack.</summary>
    internal static List<KitItemNode> Rows(Item root)
    {
        var rows = new List<KitItemNode>();
        foreach (Item item in root.GetAllItems())
        {
            var node = new KitItemNode { Id = item.Id, Tpl = item.TemplateId.ToString() };
            if (!ReferenceEquals(item, root))
            {
                IContainer container = item.CurrentAddress?.Container;
                node.ParentId = container?.ParentItem?.Id;
                node.SlotId = container?.ID;
                if (node.ParentId == null || node.SlotId == null)
                    throw new InvalidOperationException("an item of the build is not attached");
            }

            if (item.StackObjectsCount > 1)
                node.Upd = new JObject { ["StackObjectsCount"] = item.StackObjectsCount };
            rows.Add(node);
        }

        return rows;
    }

    internal static bool IsRoundSlot(string slotId)
        => slotId != null && (slotId == CustomKitStructure.CartridgesSlot
                              || slotId.StartsWith("patron_in_weapon", StringComparison.Ordinal)
                              || slotId.StartsWith("camora_", StringComparison.Ordinal));

    private static Item Create(ItemFactory factory, string tpl, out string reason)
    {
        reason = null;
        ItemTemplate template = Template(factory, tpl);
        if (template == null) { reason = "unknown item"; return null; }
        Item item = null;
        if (factory._itemPresets != null && factory._itemPresets.ContainsKey(template._id))
            item = factory.GetPresetItem(tpl);
        else if (!(template is WeaponTemplate))
            item = factory.CreateAndFillItem(template._id);
        if (item == null) reason = "no default build";
        return item;
    }

    /// <summary>What a preset lacks: a magazine, rounds in it (or in the cylinder) and a round in every chamber.</summary>
    private static string Arm(ItemFactory factory, Weapon weapon, string ammoTpl)
    {
        string ammoId = string.IsNullOrEmpty(ammoTpl) ? (weapon.Template as WeaponTemplate)?.defAmmo : ammoTpl;
        if (!(Template(factory, ammoId) is AmmoTemplate ammo)) return "no ammunition for this weapon";
        Item Round(int count)
        {
            Item round = factory.CreateItem(factory.NextId, ammoId, null);
            if (round != null) round.StackObjectsCount = Math.Max(1, count);
            return round;
        }

        Slot magazineSlot = weapon.GetMagazineSlot();
        Magazine magazine = weapon.GetCurrentMagazine();
        if (magazine == null && magazineSlot != null)
        {
            foreach (ItemTemplate template in Expand(magazineSlot.Filters))
            {
                if (!(template is MagazineTemplate)) continue;
                if (!(factory.CreateItem(factory.NextId, template._id.ToString(), null) is Magazine candidate)) continue;
                if (!magazineSlot.CheckCompatibility(candidate) || magazineSlot.AddWithoutRestrictions(candidate).Failed) continue;
                magazine = candidate;
                break;
            }

            if (magazine == null) return "no magazine fits this weapon";
        }

        if (magazine is CylinderMagazine cylinder)
        {
            foreach (Slot camora in cylinder.Camoras ?? Array.Empty<Slot>())
            {
                if (camora.ContainedItem != null) continue;
                Item round = Round(1);
                if (round == null || !camora.CheckCompatibility(round) || camora.AddWithoutRestrictions(round).Failed)
                    return "the ammunition does not fit the cylinder";
            }
        }
        else if (magazine != null && magazine.Cartridges != null)
        {
            // Stacks of at most the round's stack size until the magazine is full, as the game loads
            // one: a 100-round belt box takes 40 + 40 + 20 of a round that stacks to 40. The stack
            // slot itself never checks the stack size, so one stack would also go in, and a stack
            // over the round's own limit is not something the game makes.
            int perStack = Math.Max(1, ammo.StackMaxSize);
            for (int room = magazine.MaxCount - magazine.Cartridges.Count; room > 0; room = magazine.MaxCount - magazine.Cartridges.Count)
            {
                Item stack = Round(Math.Min(room, perStack));
                if (stack == null || magazine.Cartridges.Add(stack, false).Failed)
                    return "the ammunition does not fit the magazine";
            }
        }

        foreach (Slot chamber in weapon.Chambers ?? Array.Empty<Slot>())
        {
            if (chamber.ContainedItem != null) continue;
            Item round = Round(1);
            if (round == null || !chamber.CheckCompatibility(round) || chamber.AddWithoutRestrictions(round).Failed)
                return "the ammunition does not fit the chamber";
        }

        return null;
    }

    private static CustomKitCandidateFacts Facts(ItemFactory factory, ItemTemplate template)
    {
        string tpl = template._id.ToString();
        return new CustomKitCandidateFacts
        {
            Tpl = tpl,
            InternalName = template._name,
            DisplayName = FullName(template),
            HasPrefab = template.Prefab != null && !string.IsNullOrEmpty(template.Prefab.path),
            IsStationary = (template as WeaponTemplate)?.IsStationaryWeapon == true,
            IsWeapon = template is WeaponTemplate,
            HasPreset = factory._itemPresets != null && factory._itemPresets.ContainsKey(template._id),
            HandbookPrice = CustomKitPrices.Of(tpl),
        };
    }

    /// <summary>
    /// The handbook sum (each item once) and the best armour class of exactly what a pick adds: the
    /// rows <see cref="BuildRows"/> makes, so the inserts the game fills into locked slots and the
    /// rounds a weapon is armed with count, as they do in the kit's price. The template alone when
    /// it cannot be built (the pick then fails with the reason).
    /// </summary>
    private static void Summarise(ItemFactory factory, string rootSlot, ItemTemplate template, out int price, out int armour)
    {
        string tpl = template._id.ToString();
        price = (int)Math.Round(CustomKitPrices.Of(tpl) ?? 0);
        armour = (template as IArmorComponentTemplate)?.ArmorClass ?? 0;
        List<KitItemNode> rows = BuildRows(rootSlot, tpl, null, out _);
        if (rows == null) return;
        double total = 0;
        foreach (KitItemNode row in rows)
        {
            total += CustomKitPrices.Of(row.Tpl) ?? 0;
            if (Template(factory, row.Tpl) is IArmorComponentTemplate part && part.ArmorClass > armour) armour = part.ArmorClass;
        }

        price = (int)Math.Round(total);
    }

    /// <summary>Why the kit carries an item Arena does not hand out (see <see cref="CustomKitEditing.FirstExcluded"/>), or null.</summary>
    internal static string ExcludedItem(KitDefinition kit)
    {
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        KitItemNode node = CustomKitEditing.FirstExcluded(kit, tpl =>
        {
            ItemTemplate template = factory == null ? null : Template(factory, tpl);
            return template == null ? null : FullName(template);
        }, ExcludedSet());
        return node == null ? null : ShortName(node.Tpl) + " is an item Arena does not hand out";
    }

    /// <summary>Every item template the filters name, categories expanded, the excluded ones left out.</summary>
    private static IEnumerable<ItemTemplate> Expand(ItemFilter[] filters)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ItemTemplate>();
        if (filters == null) return result;
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        if (factory == null) return result;
        foreach (ItemFilter filter in filters)
        {
            if (filter?.Filter == null) continue;
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MongoID id in filter.ExcludedFilter ?? Array.Empty<MongoID>())
                if (factory.ItemTemplates.TryGetValue(id, out var node))
                    foreach (ItemTemplate child in global::JsonType.ItemTemplates.GetAllChildTemplateIds(node, Children))
                        excluded.Add(child._id.ToString());
            foreach (MongoID id in filter.Filter)
            {
                if (!factory.ItemTemplates.TryGetValue(id, out var node)) continue;
                foreach (ItemTemplate child in global::JsonType.ItemTemplates.GetAllChildTemplateIds(node, Children))
                {
                    if (child._type != NodeType.Item) continue;
                    string key = child._id.ToString();
                    if (excluded.Contains(key) || !seen.Add(key)) continue;
                    result.Add(child);
                }
            }
        }

        return result;
    }

    private static HashSet<string> ExcludedSet()
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in Session.ArenaClientConfig.ExcludedTemplates() ?? (IReadOnlyList<string>)Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(id)) excluded.Add(id.Trim());
        return excluded;
    }

    private static ItemTemplate Template(ItemFactory factory, string tpl)
    {
        if (factory == null || !CustomKitStructure.IsTemplateId(tpl)) return null;
        try { return factory.ItemTemplates.TryGetValue(new MongoID(tpl), out var template) ? template : null; }
        catch (Exception) { return null; }
    }

    private static string ShortName(ItemTemplate template)
    {
        try
        {
            string name = template.ShortNameLocalizationKey?.Localized();
            return string.IsNullOrWhiteSpace(name) ? template.Name ?? template._id.ToString() : name;
        }
        catch (Exception) { return template.Name ?? template._id.ToString(); }
    }

    private static string FullName(ItemTemplate template)
    {
        try
        {
            string name = template.NameLocalizationKey?.Localized();
            return string.IsNullOrWhiteSpace(name) ? template.Name : name;
        }
        catch (Exception) { return template.Name; }
    }
}

/// <summary>
/// The engine check of a player's own kit (docs/reviews/CUSTOM-KITS-20261110.md), the second tier
/// after <see cref="CustomKitStructure"/>: the kit is built as a detached equipment the way the game
/// would accept it, child items first so each attached subtree is whole, with every check KitBuilder
/// skips (it attaches without restrictions): slot filters, blocking and conflicting slots, conflicting
/// items, magazine capacity, required parts of every weapon and of the equipment, ammunition that the
/// chamber takes, the supplies fitting the rig and pockets as the strict entry path wants them, and
/// a handbook price for every item the kit hands out (<see cref="CustomKitEditing.FirstUnpriced"/>,
/// with the inserts the game puts into locked slots exempt: only the built tree knows which those
/// are). A pick that fails is refused in the editor; a stored kit that fails here is unavailable in
/// this session and never issued. Cached per kit object (an edit is a new object) and by content,
/// but only once the item database and the handbook are there to answer.
/// </summary>
internal static class CustomKitProbe
{
    private sealed class Result { internal string Reason; }

    private static readonly ConditionalWeakTable<KitDefinition, Result> Results = new();

    /// <summary>Null when the kit builds cleanly, otherwise the first reason it does not.</summary>
    internal static string Check(KitDefinition kit)
    {
        if (kit == null) return "no kit";
        if (Results.TryGetValue(kit, out var known)) return known.Reason;
        // Not remembered: a verdict without the database would outlive the moment it is loaded.
        if (Singleton<ItemFactory>.Instance == null || !CustomKitPrices.Loaded) return "the item database or the handbook is not loaded";
        Result result = Evaluate(kit);
        try { Results.Add(kit, result); }
        catch (ArgumentException) { }
        return result.Reason;
    }

    // The verdict by content as well: the kit window reads the file again per opening and after
    // every edit, each time into new objects, and neither the item database nor the handbook
    // changes in a process once loaded.
    private static readonly Dictionary<string, string> ByContent = new(StringComparer.Ordinal);
    private const int MaxRemembered = 256;

    private static Result Evaluate(KitDefinition kit)
    {
        string key = null;
        try { key = Newtonsoft.Json.JsonConvert.SerializeObject(new object[] { kit.Items, kit.Fill }); }
        catch (Exception) { key = null; }
        if (key != null && ByContent.TryGetValue(key, out var known)) return new Result { Reason = known };

        string reason;
        try { reason = Run(kit); }
        catch (Exception ex) { reason = "the kit could not be checked: " + ex.Message; }
        if (key != null)
        {
            if (ByContent.Count >= MaxRemembered) ByContent.Clear();
            ByContent[key] = reason;
        }

        return new Result { Reason = reason };
    }

    private static string Run(KitDefinition kit)
    {
        ItemFactory factory = Singleton<ItemFactory>.Instance;
        if (factory == null) return "the item database is not loaded";
        var unknown = KitBuilder.ValidateKit(kit);
        if (unknown.Count > 0) return "unknown items: " + string.Join(", ", unknown.Take(3).ToArray());
        string root = CustomKitStructure.RootId(kit);
        if (root == null) return "no single root";

        if (!(factory.CreateItem(factory.NextId, CustomKitStructure.EquipmentTemplate, null) is InventoryEquipment equipment))
            return "no equipment";
        var byId = new Dictionary<string, KitItemNode>(StringComparer.Ordinal);
        var created = new Dictionary<string, Item>(StringComparer.Ordinal) { [root] = equipment };
        foreach (KitItemNode node in kit.Items)
        {
            if (node == null || node.Id == root) continue;
            byId[node.Id] = node;
            Item item = factory.CreateItem(factory.NextId, node.Tpl, null);
            if (item == null) return "item " + node.Tpl + " could not be created";
            if (node.Upd?["StackObjectsCount"] is JValue count && count.Type == JTokenType.Integer)
                item.StackObjectsCount = (int)Math.Max(1, Math.Min(int.MaxValue, (long)count));
            created[node.Id] = item;
        }

        int Depth(KitItemNode node)
        {
            int depth = 0;
            for (string parent = node.ParentId; parent != null && parent != root && byId.TryGetValue(parent, out var up) && depth <= byId.Count; parent = up.ParentId)
                depth++;
            return depth;
        }

        var forced = new HashSet<string>(StringComparer.Ordinal);
        foreach (KitItemNode node in byId.Values.OrderByDescending(Depth))
        {
            if (!created.TryGetValue(node.ParentId ?? string.Empty, out var parent)) return "item " + node.Tpl + " has no parent";
            string problem = Attach(parent, created[node.Id], node.SlotId, out bool locked);
            if (problem != null) return CustomKitItems.ShortName(node.Tpl) + ": " + problem;
            if (locked) forced.Add(node.Id);
        }

        Slot missing = equipment.MissingVitalParts.FirstOrDefault();
        if (missing != null) return "the kit has nothing in its required slot " + missing.ID;
        foreach (Weapon weapon in equipment.GetAllItems().OfType<Weapon>())
        {
            Slot part = weapon.MissingVitalParts.FirstOrDefault();
            if (part != null) return weapon.ShortName.Localized() + " lacks a required part (" + part.ID + ")";
            Magazine magazine = weapon.GetCurrentMagazine();
            if (magazine != null && !(magazine is CylinderMagazine) && magazine.Cartridges?.Count > 0
                && weapon.Chambers?.Length > 0 && !magazine.IsAmmoCompatible(weapon.Chambers))
                return weapon.ShortName.Localized() + ": the magazine's rounds do not fit the chamber";
        }

        KitItemNode free = CustomKitEditing.FirstUnpriced(kit, CustomKitPrices.Lookup, forced.Contains);
        if (free != null) return CustomKitItems.ShortName(free.Tpl) + " has no handbook price, so the kit's price would not count it";

        try
        {
            KitBuilder.FillEntryGrids(equipment, factory, kit, created);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        return null;
    }

    /// <summary>Attaches with every check the game makes; <paramref name="locked"/> tells whether the slot was a locked one.</summary>
    private static string Attach(Item parent, Item item, string slotId, out bool locked)
    {
        locked = false;
        if (parent is Magazine magazine && slotId == CustomKitStructure.CartridgesSlot)
        {
            if (magazine.Cartridges == null) return "the magazine takes no loose rounds";
            var stacked = magazine.Cartridges.Add(item, false);
            return stacked.Failed ? "does not fit the magazine (" + stacked.Error + ")" : null;
        }

        Slot slot = parent is InventoryEquipment equipment && Enum.TryParse<EquipmentSlot>(slotId, true, out var equipmentSlot)
            ? equipment.GetSlot(equipmentSlot)
            : (parent as CompoundItem)?.AllSlots?.FirstOrDefault(candidate => candidate.ID == slotId);
        if (slot == null)
        {
            var grid = (parent as CompoundItem)?.Grids?.FirstOrDefault(candidate => candidate.ID == slotId);
            if (grid == null) return "no slot " + slotId;
            var placed = grid.AddAnywhere(item, EErrorHandlingType.Ignore);
            return placed.Failed ? "does not fit " + slotId : null;
        }

        if (slot.ContainedItem != null) return slotId + " is already filled";
        if (!slot.CheckCompatibility(item)) return "does not fit " + slotId;
        if (slot.BlockerSlots.Count > 0 || slot.GetConflictingSlot(item).Any(other => other.ContainedItem != null))
            return slotId + " is blocked by another item";
        var conflicts = slot.CheckConflictingItems(item);
        if (conflicts.Failed) return "conflicts with another part (" + conflicts.Error + ")";
        var added = slot.AddWithoutRestrictions(item);
        if (added.Failed) return "could not be put into " + slotId + " (" + added.Error + ")";
        locked = slot.Locked;
        return null;
    }
}
