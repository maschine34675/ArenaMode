using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Services;

/// <summary>
/// Arena-owned, bounded additions to existing builds. The index is a private snapshot
/// of the server's loaded templates, not a bot generator or an editable global pool.
/// Authored nodes, ammo, prices and equipment are retained except four explicitly
/// approved plain dust covers replaced atomically with a usable optic. A transaction is
/// committed only after the entire resulting weapon passes the native slot rules.
/// </summary>
public sealed class ArenaWeaponVariants
{
    private const string Weapon = "5422acb9af1c889c16000029";
    private const string Mount = "55818b224bdc2dde698b456f";
    private const string Collimator = "55818ad54bdc2ddc698b4569";
    private const string CompactCollimator = "55818acf4bdc2dde698b456b";
    private const string AssaultScope = "55818add4bdc2d5b648b456f";
    private const string OpticScope = "55818ae44bdc2dde698b456c";
    private const string SpecialScope = "55818aeb4bdc2ddc698b456a";
    private const string Grip = "55818af64bdc2d5b648b4570";
    private const string Muzzle = "5448fe394bdc2d0d028b456c";
    private const string Silencer = "550aa4cd4bdc2dd8348b456c";
    private const string Laser = "55818b0e4bdc2dde698b456e";
    private const string Tactical = "55818b164bdc2ddc698b456c";
    private const int SearchBudget = 4096;
    private const int MaximumChain = 4;
    private const int MaximumWeaponNodes = 180;
    // EFT 4.1 JsonTypes.TypeTable keys for Item/CompoundItem, weapons, ammo and
    // weapon mods. An arbitrary content-mod Node is NOT a native CLR category.
    // Verify.ps1 checks these data IDs against the installed/decompiled contract.
    private static readonly HashSet<string> NativeFilterCategories = new(StringComparer.Ordinal)
    {
        "54009119af1c881c07000029", "566162e44bdc2d3f298b4573", "5448fe124bdc2da5018b4567",
        "550aa4154bdc2dd8348b456b", "55818afb4bdc2dde698b456d", "55818b084bdc2d5b648b4571",
        "55818af64bdc2d5b648b4570", "56ea9461d2720b67698b456f", "55818b0e4bdc2dde698b456e",
        "5448fe394bdc2d0d028b456c", "550aa4af4bdc2dd4348b456e", "550aa4bf4bdc2dd6348b456b",
        "550aa4dd4bdc2dc9348b4569", "550ad14d4bdc2dd5348b456c", "550aa4cd4bdc2dd8348b456c",
        "55818b1d4bdc2d5b648b4572", "5448fe7a4bdc2d6f028b456b", "55818add4bdc2d5b648b456f",
        "55818ad54bdc2ddc698b4569", "55818acf4bdc2dde698b456b", "55818ac54bdc2d5b648b456e",
        "55818ae44bdc2dde698b456c", "55818aeb4bdc2ddc698b456a", "55818b164bdc2ddc698b456c",
        "5a74651486f7744e73386dd1", "55802f3e4bdc2de7118b4584", "55818a6f4bdc2db9688b456b",
        "55818b014bdc2ddc698b456b", "5448bc234bdc2d3c308b4569", "610720f290b75a49ff2e5e25",
        "627a137bf21bc425b06ab944", "55818b224bdc2dde698b456f", "55818a604bdc2db5418b457e",
        "55818a594bdc2db9688b456a", "55802f4a4bdc2ddb688b4569", "555ef6e44bdc2de9068b457e",
        "55818a104bdc2db9688b4569", "55818a684bdc2ddd698b456d", "55818a304bdc2db5418b457d",
        "5422acb9af1c889c16000029", "5447b5fc4bdc2d87278b4567", "5447b5f14bdc2d61278b4567",
        "5447bedf4bdc2d87278b4568", "67446d4f04141c10630604e7", "5447bed64bdc2d97278b4568",
        "5447b6194bdc2d67278b4567", "5447b5cf4bdc2d65278b4567", "5447b6094bdc2dc3278b4567",
        "617f1ef5e8b54b0998387733", "5447b5e04bdc2d62278b4567", "5447b6254bdc2dc3278b4568",
        "5447bee84bdc2dc3278b4569", "5485a8684bdc2da71d8b4567",
    };
    // The only non-additive exception: plain AKM/AK74/VPO/Vityaz dust covers.
    // No children, no authored Upd and no existing optic; replacement+optic is
    // one validated transaction. No generic receiver/handguard rebuilding.
    private static readonly HashSet<string> PlainCovers = new(StringComparer.Ordinal)
        { "59d6507c86f7741b846413a2", "5649af094bdc2df8348b4586", "59e6449086f7746c9f75e822", "59985a8086f77414ec448d1a" };
    private static readonly HashSet<string> RailedCovers = new(StringComparer.Ordinal)
        { "5d2c76ed48f03532f2136169", "5d2c770c48f0354b4a07c100", "5d2c772c48f0355d95672c25", "59985a6c86f77414ec448d17" };
    private readonly Dictionary<string, Definition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _ancestors = new(StringComparer.Ordinal);
    private readonly Dictionary<Slot, Definition[]> _candidates = new();
    private readonly Dictionary<Slot, bool> _primaryScopeSockets = new();
    private readonly object _gate = new();

    private sealed record Filter(string[] Include, string[] Exclude);
    private sealed record Slot(string Name, bool Required, bool Stack, double Capacity, Filter[] Filters);
    private sealed record Definition(string Id, string Parent, string Kind, string Name, string Prefab,
        bool Quest, double Weight, double Ergo, double Recoil, double Zoom,
        Slot[] Slots, string[] Conflicts);
    private sealed record Step(Slot Slot, Definition Item);
    private sealed record Choice(string ParentId, List<Step> Steps, double Weight);
    public sealed record EntryReport(string Id, bool HadOptic, bool HasOptic, int Added, string Detail);

    public ArenaWeaponVariants(JsonObject templates)
    {
        // Retain only immutable values used by this rule layer; no JsonNode or
        // TemplateTable reference can be mutated later through the index.
        foreach (var pair in templates)
        {
            if (pair.Value is not JsonObject item || item["_props"] is not JsonObject props) continue;
            var slots = new List<Slot>();
            foreach (string collection in new[] { "Slots", "Chambers", "Cartridges" })
                foreach (var slot in props[collection] as JsonArray ?? new JsonArray())
                {
                    if (slot is not JsonObject s || Text(s, "_name").Length == 0) continue;
                    var filters = new List<Filter>();
                    foreach (var f in s["_props"]?["filters"] as JsonArray ?? new JsonArray())
                        filters.Add(new Filter(Strings(f?["Filter"]), Strings(f?["ExcludedFilter"])));
                    slots.Add(new Slot(Text(s, "_name"), Flag(s, "_required"), collection == "Cartridges",
                        Number(s["_max_count"]), filters.ToArray()));
                }
            var zooms = (props["Zooms"] as JsonArray ?? new JsonArray())
                .OfType<JsonArray>().SelectMany(x => x).Select(Number).ToArray();
            _definitions[pair.Key] = new Definition(pair.Key, Text(item, "_parent"), Text(item, "_type"), Text(item, "_name"),
                props["Prefab"]?["path"]?.GetValue<string>() ?? "", Flag(props, "QuestItem"),
                Number(props["Weight"]), Number(props["Ergonomics"]), Number(props["Recoil"]),
                zooms.Length == 0 ? 1 : zooms.Max(), slots.ToArray(), Strings(props["ConflictingItems"]));
        }
        foreach (var item in _definitions.Values)
        {
            var ancestors = new HashSet<string>(StringComparer.Ordinal);
            string id = item.Id;
            for (int depth = 0; depth < 32 && ancestors.Add(id); depth++)
            {
                if (!_definitions.TryGetValue(id, out var parent)) break;
                id = parent.Parent;
            }
            _ancestors[item.Id] = ancestors;
        }
    }

    /// <summary>Same input/index/seed gives the same detached output. No RNG, profile or bot state is shared.</summary>
    public string CreateCatalog(string json, int seed, out IReadOnlyList<EntryReport> reports)
    {
        var report = new List<EntryReport>();
        reports = report;
        lock (_gate)
        {
            JsonArray? catalog;
            try { catalog = JsonNode.Parse(json) as JsonArray; }
            catch (Exception) { report.Add(new("catalog", false, false, 0, "invalid-json; unchanged")); return json; }
            if (catalog == null) return json;
            var random = new Random(seed);
            foreach (var entry in catalog.OfType<JsonObject>())
            {
                string id = Text(entry, "id");
                string problem = "items-missing";
                if (entry["items"] is not JsonArray original || !TryValidate(original, out problem))
                { report.Add(new(id, false, false, 0, "base-invalid:" + (entry["items"] == null ? "items-missing" : problem))); continue; }
                var items = (JsonArray)original.DeepClone();
                bool hadOptic = HasCategory(items, "optic");
                int count = items.Count;
                double originalWeight = WeaponNodes(items).Sum(x => _definitions[Text(x, "_tpl")].Weight);
                var notes = new List<string>();
                foreach (string category in new[] { "optic", "grip", "muzzle", "laser" })
                {
                    if (HasCategory(items, category)) continue; // Respect authored equipment.
                    int chance = category switch { "grip" => 75, "muzzle" => 55, "laser" => 60, _ => 100 };
                    if (random.Next(100) >= chance) continue;
                    if (TryAdd(items, category, random, seed, id, originalWeight, out var upgraded, out string reason))
                        items = upgraded;
                    else if (category == "optic" && TryReplacePlainCover(items, random, seed, id, originalWeight,
                                 out upgraded, out string replacement))
                    { items = upgraded; notes.Add(replacement); }
                    else notes.Add(category + ":" + reason);
                }
                if (TryValidate(items, out problem)) entry["items"] = items;
                else { items = original; notes.Add("final-rejected:" + problem); }
                report.Add(new(id, hadOptic, HasCategory(items, "optic"), items.Count - count,
                    notes.Count == 0 ? "validated" : string.Join(",", notes)));
            }
            return catalog.ToJsonString();
        }
    }

    public static string Summarize(string label, IReadOnlyList<EntryReport> report)
        => $"[ArenaMode] {label} weapon variants: {report.Count} builds, optics {report.Count(x => x.HadOptic)} -> "
           + $"{report.Count(x => x.HasOptic)}, {report.Count(x => x.Added > 0)} upgraded; "
           + "unchanged/no-optic: " + string.Join("; ", report.Where(x => !x.HasOptic || x.Detail.StartsWith("base-invalid", StringComparison.Ordinal))
               .Take(180).Select(x => x.Id + "=" + x.Detail));

    private bool TryAdd(JsonArray items, string category, Random random, int seed, string entry, double originalWeight,
        out JsonArray output, out string reason, string? requiredParent = null)
    {
        output = items;
        var weapon = WeaponNodes(items);
        var choices = new List<Choice>();
        int budget = SearchBudget;
        foreach (var parent in requiredParent == null ? weapon : Descendants(weapon, requiredParent))
        {
            if (!_definitions.TryGetValue(Text(parent, "_tpl"), out var definition)) continue;
            foreach (var slot in definition.Slots)
            {
                if (!EligibleSlot(slot.Name, category) || Occupied(weapon, Text(parent, "_id"), slot.Name)) continue;
                if (category == "optic" && !CanStartOpticChain(weapon, parent, slot)) continue;
                FindChains(slot, category, new List<Step>(), new HashSet<string>(), choices, Text(parent, "_id"), ref budget);
            }
        }
        // Reject known conflicts before weighting, rather than letting one
        // impossible optic consume the bounded draw through dozens of adapters.
        // In particular the stock M4 rear sight excludes several tall optics.
        var existingTpls = weapon.Select(x => Text(x, "_tpl")).ToHashSet(StringComparer.Ordinal);
        var existingConflicts = weapon.SelectMany(x => _definitions[Text(x, "_tpl")].Conflicts).ToHashSet(StringComparer.Ordinal);
        choices.RemoveAll(choice => choice.Steps.Any(step => existingConflicts.Contains(step.Item.Id)
            || step.Item.Conflicts.Any(conflict => existingTpls.Contains(conflict)
                || choice.Steps.Any(other => !ReferenceEquals(other, step) && other.Item.Id == conflict))));
        reason = budget <= 0 ? "search-budget/no-valid-chain" : "no-compatible-free-chain";
        // Select the attachment first, then one of its mounting paths. Otherwise
        // an optic supported by many equivalent adapters would get accidental extra weight.
        int attempts = 0;
        while (choices.Count > 0 && attempts++ < 48)
        {
            var groups = choices.GroupBy(x => x.Steps[^1].Item.Id).ToArray();
            var chosen = Weighted(groups, x => x.First().Weight, random);
            var path = chosen.ElementAt(random.Next(chosen.Count()));
            choices.Remove(path);
            var candidate = (JsonArray)items.DeepClone();
            string parent = path.ParentId;
            foreach (var step in path.Steps)
            {
                int salt = candidate.Count;
                string id;
                do { id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{entry}:{category}:{salt++}")))[..24].ToLowerInvariant(); }
                while (candidate.Any(x => Text(x, "_id") == id));
                candidate.Add(new JsonObject { ["_id"] = id, ["_tpl"] = step.Item.Id, ["parentId"] = parent, ["slotId"] = step.Slot.Name });
                parent = id;
            }
            if (WeaponNodes(candidate).Sum(x => _definitions[Text(x, "_tpl")].Weight)
                - originalWeight > 1.0)
            { reason = "attachment-weight-limit"; continue; }
            if (TryValidate(candidate, out reason)) { output = candidate; return true; }
        }
        if (attempts >= 48) reason = "candidate-budget:" + reason;
        return false;
    }

    private bool TryReplacePlainCover(JsonArray items, Random random, int seed, string entry, double originalWeight,
        out JsonArray output, out string detail)
    {
        output = items; detail = "";
        if (HasCategory(items, "optic")) return false;
        var weapon = WeaponNodes(items);
        foreach (var node in weapon.Where(x => Text(x, "slotId") == "mod_reciever" && PlainCovers.Contains(Text(x, "_tpl"))))
        {
            string id = Text(node, "_id");
            if (node["upd"] != null || weapon.Any(x => Text(x, "parentId") == id)) continue;
            var parent = weapon.FirstOrDefault(x => Text(x, "_id") == Text(node, "parentId"));
            if (parent == null) continue;
            var slot = _definitions[Text(parent, "_tpl")].Slots.FirstOrDefault(x => x.Name == "mod_reciever");
            if (slot == null) continue;
            var covers = Candidates(slot).Where(x => RailedCovers.Contains(x.Id) && SafeAddition(x)).ToList();
            while (covers.Count > 0)
            {
                int pick = random.Next(covers.Count);
                var cover = covers[pick]; covers.RemoveAt(pick);
                var trial = (JsonArray)items.DeepClone();
                trial.Single(x => Text(x, "_id") == id)!["_tpl"] = cover.Id;
                if (!TryValidate(trial, out _)) continue;
                if (!TryAdd(trial, "optic", random, seed, entry, originalWeight, out var completed, out _, id)) continue;
                if (!Descendants(WeaponNodes(completed), id).Any(x => _definitions.TryGetValue(Text(x, "_tpl"), out var optic) && IsOptic(optic))
                    || !TryValidate(completed, out _)) continue;
                output = completed;
                detail = "stock-cover:" + Text(node, "_tpl") + "->" + cover.Id;
                return true;
            }
        }
        return false;
    }

    private void FindChains(Slot slot, string category, List<Step> path, HashSet<string> visited,
        List<Choice> choices, string parent, ref int budget)
    {
        if (path.Count >= MaximumChain || budget <= 0) return;
        foreach (var item in Candidates(slot))
        {
            if (--budget < 0) return;
            if (!SafeAddition(item) || !visited.Add(item.Id)) continue;
            path.Add(new Step(slot, item));
            if (Matches(item, category) && item.Slots.All(x => !x.Required))
                choices.Add(new Choice(parent, new List<Step>(path), Weight(item, category)));
            else if (IsA(item.Id, Mount) || (category == "muzzle" && IsA(item.Id, Muzzle)))
            {
                foreach (var child in item.Slots.Where(x => EligibleSlot(x.Name, category)))
                {
                    // A new scope ring is empty. Its reflex rail/cap is a
                    // secondary route, even when the actual scope slot is not
                    // marked required by EFT. Do not spend the search/draw
                    // budget on a standalone reflex sight above an empty ring.
                    if (category == "optic" && !CanUseOpticBranch(item, child, null, null)) continue;
                    // A path must satisfy every required sibling too. Complex
                    // multi-child adapters are intentionally outside this first-stage generator.
                    if (item.Slots.Any(x => x.Required && !ReferenceEquals(x, child))) continue;
                    FindChains(child, category, path, visited, choices, parent, ref budget);
                }
            }
            path.RemoveAt(path.Count - 1);
            visited.Remove(item.Id);
        }
    }

    private bool CanStartOpticChain(List<JsonObject> weapon, JsonObject parent, Slot branch)
    {
        // The start can be an authored ring cap several levels below the ring.
        // Inspect every mount ancestor, not just the reflex sight's immediate
        // parent. This is a generation policy, never a new native validity rule
        // that could reject/remove authored equipment.
        for (int depth = 0; depth < MaximumWeaponNodes; depth++)
        {
            var definition = _definitions[Text(parent, "_tpl")];
            if (!CanUseOpticBranch(definition, branch, weapon, Text(parent, "_id"))) return false;
            var ancestor = weapon.FirstOrDefault(x => Text(x, "_id") == Text(parent, "parentId"));
            if (ancestor == null) return true;
            var ancestorSlot = _definitions[Text(ancestor, "_tpl")].Slots.FirstOrDefault(x => x.Name == Text(parent, "slotId"));
            if (ancestorSlot == null) return false;
            parent = ancestor;
            branch = ancestorSlot;
        }
        return false;
    }

    private bool CanUseOpticBranch(Definition parent, Slot branch, List<JsonObject>? existing, string? parentId)
    {
        if (!IsA(parent.Id, Mount) || IsPrimaryScopeSocket(branch)) return true;
        var primary = parent.Slots.Where(IsPrimaryScopeSocket).ToArray();
        if (primary.Length == 0) return true; // ordinary rail, direct/offset reflex mount
        return existing != null && primary.Any(socket => existing.Any(node => Text(node, "parentId") == parentId
            && Text(node, "slotId") == socket.Name && IsPrimaryScopeItem(_definitions[Text(node, "_tpl")])));
    }

    private bool IsPrimaryScopeSocket(Slot slot)
    {
        if (_primaryScopeSockets.TryGetValue(slot, out bool found)) return found;
        // Use the native compatibility filters, not English names, ring sizes
        // or a denylist. A general-purpose rail also accepts reflex sights or
        // adapters; a primary ring socket accepts only actual OpticScope items.
        // Do not classify AssaultScope/prism adapters as rings. Include scopes
        // above the Arena zoom limit here: that limit must not turn an empty
        // long-range ring into a valid standalone reflex mount.
        var accepted = slot.Name.StartsWith("mod_scope", StringComparison.Ordinal)
            ? Candidates(slot).Where(x => x.Kind == "Item").ToArray() : [];
        return _primaryScopeSockets[slot] = accepted.Length > 0 && accepted.All(IsPrimaryScopeItem);
    }

    private bool IsPrimaryScopeItem(Definition item)
        => IsA(item.Id, OpticScope);

    private static T Weighted<T>(IReadOnlyList<T> pool, Func<T, double> weight, Random random)
    {
        double pick = random.NextDouble() * pool.Sum(weight);
        foreach (var item in pool) { pick -= weight(item); if (pick < 0) return item; }
        return pool[^1];
    }

    private double Weight(Definition item, string category)
        => category == "optic" ? (IsA(item.Id, Collimator) || IsA(item.Id, CompactCollimator) ? 12 : 2)
           : category == "grip" ? Math.Clamp(4 + item.Ergo - item.Recoil / 2, 1, 14)
           : category == "muzzle" ? Math.Clamp(4 - item.Recoil / 4 - item.Weight * 3, 1, 12) : 5;

    private bool SafeAddition(Definition item)
        => item.Kind == "Item" && !item.Quest && item.Prefab.Length > 0 && item.Weight <= 0.7
           && !IsA(item.Id, SpecialScope)
           && !item.Name.Contains("thermal", StringComparison.OrdinalIgnoreCase)
           && !item.Name.Contains("nightvision", StringComparison.OrdinalIgnoreCase)
           && !item.Name.Contains("rangefinder", StringComparison.OrdinalIgnoreCase);

    private bool Matches(Definition item, string category) => category switch
    {
        "optic" => IsOptic(item) && item.Zoom <= 4,
        "grip" => IsA(item.Id, Grip),
        "muzzle" => IsA(item.Id, Muzzle),
        "laser" => IsA(item.Id, Laser) || IsA(item.Id, Tactical),
        _ => false,
    };

    private bool IsOptic(Definition item)
        => IsA(item.Id, Collimator) || IsA(item.Id, CompactCollimator)
           || IsA(item.Id, AssaultScope) || IsA(item.Id, OpticScope) || IsA(item.Id, SpecialScope);

    private bool HasCategory(JsonArray items, string category)
        => WeaponNodes(items).Any(x => _definitions.TryGetValue(Text(x, "_tpl"), out var d)
            && (category == "optic" ? IsOptic(d) : category == "muzzle" ? IsA(d.Id, Silencer) : Matches(d, category)));

    private static bool EligibleSlot(string slot, string category)
        => category switch
        {
            "optic" => slot.StartsWith("mod_scope", StringComparison.Ordinal) || slot.StartsWith("mod_mount", StringComparison.Ordinal),
            "grip" => slot.StartsWith("mod_foregrip", StringComparison.Ordinal) || slot.StartsWith("mod_mount", StringComparison.Ordinal),
            "laser" => slot.StartsWith("mod_tactical", StringComparison.Ordinal) || slot.StartsWith("mod_mount", StringComparison.Ordinal),
            "muzzle" => slot.StartsWith("mod_muzzle", StringComparison.Ordinal),
            _ => false,
        };

    private Definition[] Candidates(Slot slot)
    {
        if (_candidates.TryGetValue(slot, out var found)) return found;
        // Weapon slot whitelists almost always name concrete templates. Avoid
        // a full database scan per mount slot; broad category filters still work.
        IEnumerable<Definition> pool = _definitions.Values;
        if (slot.Filters.Length > 0 && slot.Filters[0].Include.All(x => !_definitions.TryGetValue(x, out var d) || d.Kind == "Item"))
            pool = slot.Filters[0].Include.Distinct(StringComparer.Ordinal).Where(_definitions.ContainsKey).Select(x => _definitions[x]);
        return _candidates[slot] = pool.Where(x => Allowed(slot, x.Id))
            .OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    private bool Allowed(Slot slot, string tpl)
        // EFT ItemFilterExtension.CheckItem: every filter record must match.
        // No records is unrestricted; an empty inclusion record rejects everything.
        => slot.Filters.All(f => f.Include.Any(x => FilterMatches(tpl, x)) && !f.Exclude.Any(x => FilterMatches(tpl, x)));

    private bool FilterMatches(string tpl, string filter)
        => tpl == filter || (NativeFilterCategories.Contains(filter) && IsA(tpl, filter));

    private bool IsA(string tpl, string ancestor)
        => tpl == ancestor || (_ancestors.TryGetValue(tpl, out var ancestors) && ancestors.Contains(ancestor));

    /// <summary>Audits every weapon child, not merely the first node with required slots.</summary>
    public bool TryValidate(JsonArray items, out string problem)
    {
        problem = "";
        var ids = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in items)
        {
            if (node is not JsonObject obj || Text(obj, "_id").Length == 0 || !ids.TryAdd(Text(obj, "_id"), obj))
                return Reject("duplicate-or-missing-id", out problem);
            if (!_definitions.ContainsKey(Text(obj, "_tpl"))) return Reject("missing-template:" + Text(obj, "_tpl"), out problem);
        }
        if (items.Count(x => Text(x, "parentId").Length == 0) != 1) return Reject("root-count", out problem);
        foreach (var node in ids.Values)
        {
            var seen = new HashSet<string>();
            string parent = Text(node, "parentId");
            while (parent.Length > 0)
            {
                if (!seen.Add(parent) || !ids.TryGetValue(parent, out var ancestor)) return Reject("orphan-or-cycle", out problem);
                parent = Text(ancestor, "parentId");
            }
        }
        var primary = ids.Values.Where(x => Text(x, "slotId") == "FirstPrimaryWeapon").ToArray();
        if (primary.Length != 1 || !IsA(Text(primary[0], "_tpl"), Weapon)) return Reject("primary-weapon", out problem);
        var weapon = WeaponNodes(items);
        if (weapon.Count > MaximumWeaponNodes) return Reject("weapon-size-limit", out problem);
        var occupied = new HashSet<string>();
        foreach (var node in weapon)
        {
            var definition = _definitions[Text(node, "_tpl")];
            string id = Text(node, "_id");
            if (!ReferenceEquals(node, primary[0]))
            {
                string parentId = Text(node, "parentId"), slotName = Text(node, "slotId");
                var parent = _definitions[Text(ids[parentId], "_tpl")];
                var slot = parent.Slots.FirstOrDefault(x => x.Name == slotName);
                if (slot == null || !Allowed(slot, definition.Id)) return Reject("slot-filter:" + slotName, out problem);
                if (!slot.Stack && !occupied.Add(parentId + ":" + slotName)) return Reject("duplicate-slot:" + slotName, out problem);
                if (slot.Stack)
                {
                    double amount = weapon.Where(x => Text(x, "parentId") == parentId && Text(x, "slotId") == slotName)
                        .Sum(x => x["upd"]?["StackObjectsCount"] is { } n ? Number(n) : 1);
                    if (amount <= 0 || amount > slot.Capacity) return Reject("cartridge-capacity", out problem);
                }
                // Native ItemFilterExtension applies exclusion records to the
                // complete subtree, not just the item mounted directly in the slot.
                var descendants = Descendants(weapon, id);
                if (descendants.Any(x => slot.Filters.Any(f => f.Exclude.Any(e => FilterMatches(Text(x, "_tpl"), e)))))
                    return Reject("excluded-child:" + slotName, out problem);
            }
            foreach (var slot in definition.Slots.Where(x => x.Required))
                if (!Occupied(weapon, id, slot.Name)) return Reject("required-slot:" + slot.Name, out problem);
            foreach (var other in weapon)
                if (!ReferenceEquals(node, other) && definition.Conflicts.Contains(Text(other, "_tpl"), StringComparer.Ordinal))
                    return Reject("conflict:" + definition.Id + ":" + Text(other, "_tpl"), out problem);
        }
        return true;
    }

    private static List<JsonObject> WeaponNodes(JsonArray items)
    {
        var nodes = items.OfType<JsonObject>().ToArray();
        var ids = nodes.Where(x => Text(x, "slotId") == "FirstPrimaryWeapon").Select(x => Text(x, "_id")).ToHashSet();
        bool changed;
        do { changed = false; foreach (var item in nodes) if (ids.Contains(Text(item, "parentId"))) changed |= ids.Add(Text(item, "_id")); } while (changed);
        return nodes.Where(x => ids.Contains(Text(x, "_id"))).ToList();
    }

    private static IEnumerable<JsonObject> Descendants(IEnumerable<JsonObject> nodes, string root)
    {
        var all = nodes.ToArray();
        var ids = new HashSet<string> { root };
        bool changed;
        do { changed = false; foreach (var node in all) if (ids.Contains(Text(node, "parentId"))) changed |= ids.Add(Text(node, "_id")); } while (changed);
        return all.Where(x => ids.Contains(Text(x, "_id")));
    }

    private static bool Occupied(IEnumerable<JsonObject> nodes, string parent, string slot)
        => nodes.Any(x => Text(x, "parentId") == parent && Text(x, "slotId") == slot);
    private static bool Reject(string value, out string problem) { problem = value; return false; }
    private static string Text(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? "";
    private static bool Flag(JsonNode? node, string key) => node?[key]?.GetValue<bool>() == true;
    private static double Number(JsonNode? value) => value == null ? 0 : value.GetValue<double>();
    private static string[] Strings(JsonNode? value) => (value as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToArray() ?? [];
}
