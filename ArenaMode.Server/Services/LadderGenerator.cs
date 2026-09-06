using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Services;

/// <summary>
/// Builds the Gun Game weapon pool from the item database.
///
/// This used to be an offline script run against the shipped database file, and
/// that was wrong in a way that took a playtest to notice: content mods inject
/// their items when the SERVER loads, so a script reading the file on disk never
/// sees them. A commit message even promised that "a weapon mod widens the
/// ladder for free" — it did not, and could not.
///
/// Running here fixes that by construction. The load order puts this after the
/// content mods, so every weapon that exists at that moment is a candidate, no
/// matter which mod put it there and without this mod knowing anything about
/// that mod's format.
///
/// What it produces is the pool, not the ladder: every weapon the game can build
/// a working example of, ordered weakest to strongest. LadderPicker cuts the
/// raid's twenty-four rungs out of it.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 99000)]
public class LadderGenerator(
    TemplateTable templateTable,
    GlobalTable globalTable,
    LocaleTable localeTable,
    ISptLogger<LadderGenerator> logger)
    : IOnLoad
{
    private static readonly MongoId WeaponBase = new("5422acb9af1c889c16000029");
    private static readonly MongoId AmmoBase = new("5485a8684bdc2da71d8b4567");
    private static readonly MongoId BoltAction = new("5447b6254bdc2dc3278b4568");
    private static readonly MongoId EquipmentRoot = new("55d7217a4bdc2d86028b456d");

    /// <summary>Launchers and flares: not something to hand a player as a rung.</summary>
    private static readonly HashSet<string> ExcludedCalibres =
        ["Caliber40mmRU", "Caliber26x75", "Caliber30x29"];

    /// <summary>Below this a round cannot realistically finish a fight, and a rung you cannot leave is a dead ladder.</summary>
    private const double MinimumUsefulDamage = 20d;

    private Dictionary<MongoId, TemplateItem> _items = null!;

    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ArenaContext.WeaponVariants = CreateVariantIndex(templateTable);
            logger.Info($"[ArenaMode] Weapon variant index ready: {templateTable.Items.Count} live templates; no bot generator/config changes.");
        }
        catch (Exception ex)
        {
            ArenaContext.WeaponVariants = null;
            logger.Warning($"[ArenaMode] Weapon variants unavailable ({ex.Message}); existing builds remain unchanged.");
        }
        try
        {
            _items = templateTable.Items;
            var pool = Build();

            if (pool.Count == 0)
            {
                logger.Warning("[ArenaMode] Weapon pool came out empty — keeping config/ladder.json.");
                return Task.CompletedTask;
            }

            ArenaContext.LadderJson = pool.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            logger.Info($"[ArenaMode] Weapon pool generated: {pool.Count} weapons from the live item database.");
        }
        catch (Exception ex)
        {
            // A broken pool must not take the server down, and the shipped file
            // is a perfectly good fallback.
            logger.Error($"[ArenaMode] Weapon pool generation failed ({ex.Message}) — falling back to config/ladder.json.");
        }

        return Task.CompletedTask;
    }

    internal static ArenaWeaponVariants CreateVariantIndex(TemplateTable table, JsonSerializerOptions? serializerOptions = null)
    {
        // MongoId is a struct. Default System.Text.Json serializes it as an
        // object, silently losing every slot filter/conflict template. Use the
        // server's registered converters without creating/changing global options.
        var options = serializerOptions ?? JsonUtil.JsonSerializerOptionsNoIndent
            ?? throw new InvalidOperationException("SPT JSON converters are not initialized");
        var templates = new JsonObject();
        foreach (var (id, item) in table.Items)
            templates[id.ToString()] = new JsonObject
            {
                ["_name"] = item.Name,
                ["_parent"] = item.Parent.ToString(),
                ["_type"] = item.Type,
                ["_props"] = JsonSerializer.SerializeToNode(item.Properties, options),
            };
        return new ArenaWeaponVariants(templates);
    }

    // ---------------------------------------------------------------- helpers

    private bool DescendsFrom(MongoId tpl, MongoId ancestor)
    {
        var node = tpl;
        for (var depth = 0; depth < 12; depth++)
        {
            if (!_items.TryGetValue(node, out var item))
                return false;

            if (item.Parent == ancestor)
                return true;

            node = item.Parent;
        }

        return false;
    }

    private TemplateItemProperties? Props(MongoId tpl)
        => _items.TryGetValue(tpl, out var item) ? item.Properties : null;

    private double Price(MongoId tpl)
        => templateTable.Handbook?.Items?.FirstOrDefault(i => i.Id == tpl)?.Price ?? 0d;

    private static string NewId() => Guid.NewGuid().ToString("N")[..24];

    private Dictionary<string, string>? _names;

    /// <summary>
    /// The name the game shows, not the one the database files use internally.
    ///
    /// This is the rung title the player reads on every promotion, and reading
    /// the template's own field gives "weapon_izhmash_aks74u_545x39" where the
    /// offline generator gave "Kalashnikov AKS-74U". Falls back to the internal
    /// name, because not every template has a locale entry.
    /// </summary>
    private string NameOf(MongoId tpl)
    {
        _names ??= localeTable.Global.TryGetValue("en", out var lazy)
            ? lazy.Value as Dictionary<string, string> ?? []
            : [];

        if (_names.TryGetValue($"{tpl} Name", out var localised) && !string.IsNullOrWhiteSpace(localised))
            return localised;

        return _items.TryGetValue(tpl, out var item) ? item.Name ?? tpl.ToString() : tpl.ToString();
    }

    // ------------------------------------------------------------ ammunition

    private sealed record Round(MongoId Tpl, int Pen, double Damage, double Shots, double Score);

    private Dictionary<string, List<Round>> IndexAmmo()
    {
        var byCalibre = new Dictionary<string, List<Round>>();

        foreach (var (tpl, item) in _items)
        {
            var p = item.Properties;
            if (p?.Caliber == null || !DescendsFrom(tpl, AmmoBase))
                continue;

            var pen = p.PenetrationPower ?? 0;
            var dmg = p.Damage ?? 0;
            var shots = Math.Max(1d, p.ProjectileCount ?? 1);

            // Ranked on what a round DOES, not on its penetration. Buckshot
            // throws eight pellets and reads as "penetration 2"; sorting on
            // penetration alone put the best close-range round in the game at
            // the bottom of every shotgun's list.
            var score = pen * 2d + dmg * shots * 0.25d;

            if (!byCalibre.TryGetValue(p.Caliber, out var list))
                byCalibre[p.Caliber] = list = [];

            list.Add(new Round(tpl, pen, dmg, shots, score));
        }

        foreach (var list in byCalibre.Values)
            list.Sort((a, b) => a.Score.CompareTo(b.Score));

        return byCalibre;
    }

    /// <summary>
    /// What the magazine will actually take. Calibre alone is not a filter:
    /// grenade shrapnel carries a calibre and a high projectile count, and once
    /// projectile count entered the ranking it outranked real cartridges.
    /// </summary>
    private HashSet<MongoId>? AcceptedRounds(MongoId? magazine, MongoId weapon)
    {
        foreach (var tpl in new[] { magazine, weapon })
        {
            if (tpl == null)
                continue;

            var cartridges = Props(tpl.Value)?.Cartridges?.FirstOrDefault();
            var filter = cartridges?.Properties?.Filters?.FirstOrDefault()?.Filter;
            if (filter is { Count: > 0 })
                return filter;
        }

        return null;
    }

    // ----------------------------------------------------------------- build

    private JsonArray Build()
    {
        var ammoByCalibre = IndexAmmo();

        // One preset per weapon: several builds of the same gun would crowd a
        // power band and make a "random" ladder deal the same rifle three times.
        var presetsByWeapon = new Dictionary<MongoId, Preset>();
        foreach (var preset in globalTable.ItemPresets.Values)
        {
            var root = preset.Encyclopedia
                       ?? preset.Items?.FirstOrDefault(i => i.ParentId == null)?.Template;

            if (root == null || !DescendsFrom(root.Value, WeaponBase))
                continue;

            if (!presetsByWeapon.TryGetValue(root.Value, out var existing) || Plainer(preset, existing))
                presetsByWeapon[root.Value] = preset;
        }

        var candidates = new List<Candidate>();
        foreach (var (weapon, preset) in presetsByWeapon)
        {
            var candidate = Consider(weapon, preset, ammoByCalibre);
            if (candidate != null)
                candidates.Add(candidate);
        }

        candidates.Sort((a, b) => a.Score.CompareTo(b.Score));

        var pool = new JsonArray();
        for (var i = 0; i < candidates.Count; i++)
            pool.Add(BuildEntry(i, candidates[i]));

        return pool;
    }

    /// <summary>The plainest build wins: a named default first, then the fewest parts.</summary>
    private static bool Plainer(Preset candidate, Preset current)
    {
        var candidateDefault = candidate.Name?.Contains("default", StringComparison.OrdinalIgnoreCase) == true;
        var currentDefault = current.Name?.Contains("default", StringComparison.OrdinalIgnoreCase) == true;

        if (candidateDefault != currentDefault)
            return candidateDefault;

        return (candidate.Items?.Count ?? int.MaxValue) < (current.Items?.Count ?? int.MaxValue);
    }

    private sealed record Candidate(
        MongoId Weapon,
        Preset Preset,
        Item MagazineNode,
        MongoId Ammo,
        List<MongoId> AmmoChoices,
        int Capacity,
        double Score);

    private Candidate? Consider(MongoId weapon, Preset preset, Dictionary<string, List<Round>> ammoByCalibre)
    {
        var weaponProps = Props(weapon);
        var calibre = weaponProps?.AmmoCaliber;
        if (calibre == null || ExcludedCalibres.Contains(calibre))
            return null;

        var magazineNode = preset.Items?.FirstOrDefault(i => i.SlotId == "mod_magazine");

        var accepted = AcceptedRounds(magazineNode?.Template, weapon);
        var choice = PickAmmo(ammoByCalibre, calibre, accepted);
        if (choice == null)
            return null;

        var (round, choices) = choice.Value;
        if (round.Damage < MinimumUsefulDamage)
            return null;

        var capacity = CartridgeCapacity(magazineNode?.Template) ?? CartridgeCapacity(weapon) ?? 0;
        if (capacity <= 0)
            return null;

        var score = round.Pen * 2d + round.Damage * 0.4d + Price(weapon) / 12000d;
        return new Candidate(weapon, preset, magazineNode!, round.Tpl, choices, capacity, score);
    }

    private int? CartridgeCapacity(MongoId? tpl)
    {
        if (tpl == null)
            return null;

        var max = Props(tpl.Value)?.Cartridges?.FirstOrDefault()?.MaxCount;
        return max is > 0 ? (int)max.Value : null;
    }

    /// <summary>
    /// A few capable rounds rather than one, so the picker can vary a weapon's
    /// load between raids. Taken from the upper middle: capable, without dealing
    /// out the rarest armour-piercing round every time.
    /// </summary>
    private (Round Round, List<MongoId> Choices)? PickAmmo(
        Dictionary<string, List<Round>> byCalibre, string calibre, HashSet<MongoId>? accepted)
    {
        if (!byCalibre.TryGetValue(calibre, out var all) || all.Count == 0)
            return null;

        var pool = all;
        if (accepted != null)
        {
            var narrowed = all.Where(r => accepted.Contains(r.Tpl)).ToList();
            if (narrowed.Count > 0)
                pool = narrowed;
        }

        var top = Math.Min(pool.Count - 1, (int)(pool.Count * 0.75));
        var low = Math.Max(0, top - 2);
        return (pool[top], pool.Skip(low).Take(top - low + 1).Select(r => r.Tpl).ToList());
    }

    // ----------------------------------------------------------- entry shape

    private JsonObject BuildEntry(int order, Candidate c)
    {
        var nodes = new JsonArray();
        var rootId = NewId();
        nodes.Add(Node(rootId, EquipmentRoot, null, null));

        var presetRoot = c.Preset.Items!.First(i => i.ParentId == null);
        var remap = c.Preset.Items!.ToDictionary(i => i.Id, _ => NewId());

        foreach (var node in c.Preset.Items!)
        {
            var isRoot = ReferenceEquals(node, presetRoot) || node.ParentId == null;
            var entry = Node(
                remap[node.Id],
                node.Template,
                isRoot ? rootId : remap[node.ParentId!],
                isRoot ? "FirstPrimaryWeapon" : node.SlotId);

            nodes.Add(entry);
        }

        LoadMagazine(nodes, c);
        ChamberRound(nodes, c, remap[presetRoot.Id]);
        EnsureSight(nodes, c.Weapon);

        return new JsonObject
        {
            ["id"] = $"rung{(order + 1):000}",
            ["name"] = NameOf(c.Weapon),
            ["order"] = order,
            ["items"] = nodes,
            ["fill"] = new JsonObject
            {
                ["spareMagazines"] = c.Capacity < 20 ? 3 : 2,
                ["rigContents"] = new JsonArray(),
            },
            ["ammoChoices"] = new JsonArray(c.AmmoChoices.Select(a => (JsonNode)a.ToString()).ToArray()),
        };
    }

    private static JsonObject Node(string id, MongoId tpl, string? parentId, string? slotId)
    {
        var node = new JsonObject { ["_id"] = id, ["_tpl"] = tpl.ToString() };
        if (parentId != null)
            node["parentId"] = parentId;
        if (slotId != null)
            node["slotId"] = slotId;

        return node;
    }

    /// <summary>
    /// Presets ship empty. A revolver holds one round per chamber and never
    /// reads the cartridge stack, so a stack put there is invisible to the
    /// weapon and it arrives unloaded with nothing to say about it.
    /// </summary>
    private void LoadMagazine(JsonArray nodes, Candidate c)
    {
        var magazine = nodes.FirstOrDefault(n => n?["slotId"]?.GetValue<string>() == "mod_magazine");
        if (magazine == null)
            return;

        var magazineId = magazine["_id"]!.GetValue<string>();
        var magazineTpl = new MongoId(magazine["_tpl"]!.GetValue<string>());

        var camoras = Props(magazineTpl)?.Slots?
            .Where(s => s.Name?.StartsWith("camora_") == true)
            .Select(s => s.Name!)
            .ToList();

        if (camoras is { Count: > 0 })
        {
            foreach (var camora in camoras)
                nodes.Add(WithCount(Node(NewId(), c.Ammo, magazineId, camora), 1));

            return;
        }

        nodes.Add(WithCount(Node(NewId(), c.Ammo, magazineId, "cartridges"), c.Capacity));
    }

    private void ChamberRound(JsonArray nodes, Candidate c, string weaponNodeId)
    {
        if (Props(c.Weapon)?.Chambers?.Any() != true)
            return;

        nodes.Add(WithCount(Node(NewId(), c.Ammo, weaponNodeId, "patron_in_weapon"), 1));
    }

    private static JsonObject WithCount(JsonObject node, int count)
    {
        node["upd"] = new JsonObject { ["StackObjectsCount"] = count };
        return node;
    }

    // --------------------------------------------------------------- sights

    private static readonly string[] SightCategories =
        ["Collimator", "OpticScope", "AssaultScope", "SpecialScope", "IronSight", "Sights"];

    private bool IsSight(MongoId tpl)
    {
        var node = tpl;
        for (var depth = 0; depth < 12; depth++)
        {
            if (!_items.TryGetValue(node, out var item))
                return false;

            if (_items.TryGetValue(item.Parent, out var parent)
                && parent.Type == "Node"
                && parent.Name != null
                && SightCategories.Any(c => parent.Name.Contains(c, StringComparison.Ordinal)))
            {
                return true;
            }

            node = item.Parent;
        }

        return false;
    }

    /// <summary>
    /// A weapon you cannot aim is a rung nobody wants to reach. Some of the
    /// game's own presets ship with every sight slot empty; weapons with fixed
    /// irons have no such slot at all, which is how the two are told apart.
    /// </summary>
    private void EnsureSight(JsonArray nodes, MongoId weapon)
    {
        if (nodes.Any(n => n != null && IsSight(new MongoId(n["_tpl"]!.GetValue<string>()))))
            return;

        if (FitInto(nodes, "mod_scope") || FitInto(nodes, "mod_sight_rear"))
            return;

        // Only bolt-actions reach an optic through a rail and have no irons to
        // fall back on. Doing this generally bolted a red dot onto every SMG
        // with a spare rail — sixty weapons changed to fix one.
        if (DescendsFrom(weapon, BoltAction) && FitRail(nodes))
            FitInto(nodes, "mod_scope");
    }

    private bool FitInto(JsonArray nodes, string slotName)
    {
        foreach (var host in nodes.ToList())
        {
            var hostId = host?["_id"]?.GetValue<string>();
            if (hostId == null)
                continue;

            var slot = Props(new MongoId(host!["_tpl"]!.GetValue<string>()))?.Slots?
                .FirstOrDefault(s => s.Name == slotName);

            if (slot == null || Occupied(nodes, hostId, slotName))
                continue;

            // Materialised and tested with Any(), NOT FirstOrDefault: MongoId is
            // a STRUCT, so an empty result is twenty-four zeroes rather than
            // null. A null check would have passed it straight through, written
            // a node with a template id that does not exist, and — because this
            // then reports success — skipped the rear-sight attempt that might
            // actually have worked. The client drops the whole rung when it
            // meets an unknown template, so the weapon would silently vanish
            // from the ladder. No vanilla weapon reaches it; modded ones can,
            // which is the entire reason this generator moved here.
            var candidates = slot.Properties?.Filters?.FirstOrDefault()?.Filter?
                .Where(IsSight)
                .Where(f => Props(f)?.Slots?.Any(s => s.Required == true) != true)
                // Reflex sights first, then price: this is a close-quarters
                // arena, where magnification is a handicap.
                .OrderBy(f => IsReflex(f) ? 0 : 1)
                .ThenBy(Price)
                .ToList();

            if (candidates is not { Count: > 0 })
                continue;

            nodes.Add(Node(NewId(), candidates[0], hostId, slotName));
            return true;
        }

        return false;
    }

    private bool FitRail(JsonArray nodes)
    {
        foreach (var host in nodes.ToList())
        {
            var hostId = host?["_id"]?.GetValue<string>();
            if (hostId == null)
                continue;

            var slots = Props(new MongoId(host!["_tpl"]!.GetValue<string>()))?.Slots?
                .Where(s => s.Name?.StartsWith("mod_mount") == true);

            foreach (var slot in slots ?? [])
            {
                if (Occupied(nodes, hostId, slot.Name!))
                    continue;

                // Same struct trap as above.
                var rails = slot.Properties?.Filters?.FirstOrDefault()?.Filter?
                    .Where(f => Props(f)?.Slots?.Any(s => s.Name == "mod_scope") == true)
                    .Where(f => Props(f)?.Slots?.Any(s => s.Required == true) != true)
                    .OrderBy(Price)
                    .ToList();

                if (rails is not { Count: > 0 })
                    continue;

                nodes.Add(Node(NewId(), rails[0], hostId, slot.Name!));
                return true;
            }
        }

        return false;
    }

    private bool IsReflex(MongoId tpl)
    {
        var node = tpl;
        for (var depth = 0; depth < 12; depth++)
        {
            if (!_items.TryGetValue(node, out var item))
                return false;

            if (_items.TryGetValue(item.Parent, out var parent)
                && parent.Name?.Contains("Collimator", StringComparison.Ordinal) == true)
            {
                return true;
            }

            node = item.Parent;
        }

        return false;
    }

    private static bool Occupied(JsonArray nodes, string hostId, string slotName)
        => nodes.Any(n => n?["parentId"]?.GetValue<string>() == hostId
                          && n["slotId"]?.GetValue<string>() == slotName);
}
