using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArenaMode.Server.Rewards;

internal sealed record RewardColaChanges(
    IReadOnlyDictionary<string, int> Xp, IReadOnlyDictionary<string, int> Progress,
    IReadOnlyList<string> Researched, IReadOnlyDictionary<string, int> SynergyProgress,
    IReadOnlyList<string> SynergyDone, IReadOnlyList<string> Achievements);

// A typed monotonic delta, never a foreign profile copy. The owner must bind this
// to a Zombie run and durable restoration before signing it; Merge is a detached
// editor and must run inside the separate cross-file startup recovery protocol.
internal static class RewardColaDelta
{
    internal const int MaxBytes = 131_072;
    internal const int MaxForeignBytes = 1_048_576;
    internal const int MaxXpDelta = 1_000_000;

    internal static string? Capture(string beforeJson, string afterJson)
    {
        var before = Snapshot(ParseForeign(beforeJson));
        var after = Snapshot(ParseForeign(afterJson));
        var delta = new RewardColaChanges(
            Difference(before.Xp, after.Xp, MaxXpDelta),
            Difference(before.Progress, after.Progress, int.MaxValue),
            Added(before.Researched, after.Researched),
            Difference(before.SynergyProgress, after.SynergyProgress, int.MaxValue),
            Added(before.SynergyDone, after.SynergyDone),
            Added(before.Achievements, after.Achievements));
        if (Empty(delta)) return null;
        var json = Write(delta);
        Validate(json);
        return json;
    }

    internal static void Validate(string json) => _ = Read(json);

    internal static RewardColaChanges Read(string json)
    {
        var root = ParseObject(json, MaxBytes);
        Fields(root, "schema", "catalog", "xp", "prog", "researched", "synProg", "synDone", "ach");
        if (Integer(root["schema"]) != 1 || Text(root["catalog"]) != RewardColaCatalog.Version)
            throw new RewardDataException("cola-delta-version");
        var changes = new RewardColaChanges(
            Numbers(Object(root["xp"]), id => RewardColaCatalog.Perks.Contains(id) ? MaxXpDelta : -1, positive: true),
            Numbers(Object(root["prog"]), id => RewardColaCatalog.Challenges.TryGetValue(id, out var c) ? c.Goal : -1, positive: true),
            Set(root["researched"], RewardColaCatalog.Challenges.ContainsKey),
            Numbers(Object(root["synProg"]), id => RewardColaCatalog.Synergies.GetValueOrDefault(id, -1), positive: true),
            Set(root["synDone"], RewardColaCatalog.Synergies.ContainsKey),
            Set(root["ach"], RewardColaCatalog.Achievements.ContainsKey));
        if (Empty(changes) || !string.Equals(json, Write(changes), StringComparison.Ordinal))
            throw new RewardDataException("cola-delta-empty-or-noncanonical");
        return changes;
    }

    internal static string Merge(string currentForeignJson, string deltaJson)
    {
        var delta = Read(deltaJson);
        var current = ParseForeign(currentForeignJson);
        // Refuse incompatible/malformed known subtrees instead of quietly replacing
        // them; unowned root fields and nested settings remain exactly preserved.
        _ = Snapshot(current);
        PrepareCurrentChallengeVersion(current);
        foreach (var (id, amount) in delta.Xp)
            Add(Ensure(current, "xp"), id, amount, int.MaxValue, clamp: false);
        foreach (var (id, amount) in delta.Progress)
        {
            var def = RewardColaCatalog.Challenges[id];
            if (Add(Ensure(Ensure(current, "prog"), def.Perk), id, amount, def.Goal, clamp: true) == def.Goal)
                Ensure(Ensure(current, "researched"), def.Perk)[id] = true;
        }
        foreach (var id in delta.Researched)
            Ensure(Ensure(current, "researched"), RewardColaCatalog.Challenges[id].Perk)[id] = true;
        foreach (var (id, amount) in delta.SynergyProgress)
            if (Add(Ensure(Ensure(current, "syn"), "prog"), id, amount, RewardColaCatalog.Synergies[id], clamp: true)
                == RewardColaCatalog.Synergies[id])
                Ensure(Ensure(current, "syn"), "done")[id] = true;
        foreach (var id in delta.SynergyDone) Ensure(Ensure(current, "syn"), "done")[id] = true;
        foreach (var key in delta.Achievements) Ensure(current, "ach")[key] = true;
        var result = current.ToJsonString();
        if (Encoding.UTF8.GetByteCount(result) > MaxForeignBytes) throw new RewardDataException("cola-state-too-large");
        return result;
    }

    private static RewardColaChanges Snapshot(JsonObject root)
    {
        if (root.TryGetPropertyValue("chv", out var version) && Integer(version) is < 0 or > 2)
            throw new RewardDataException("cola-challenge-version");
        var xp = Numbers(Optional(root, "xp"), id => RewardColaCatalog.Perks.Contains(id) ? int.MaxValue : -1);
        var prog = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var researched = new List<string>();
        foreach (var (perk, value) in Optional(root, "prog"))
        {
            if (!RewardColaCatalog.Perks.Contains(perk)) throw new RewardDataException("cola-unknown-perk");
            foreach (var entry in Numbers(Object(value), id => RewardColaCatalog.Challenges.TryGetValue(id, out var c) && c.Perk == perk ? c.Goal : -1))
                prog.Add(entry.Key, entry.Value);
        }
        foreach (var (perk, value) in Optional(root, "researched"))
        {
            if (!RewardColaCatalog.Perks.Contains(perk)) throw new RewardDataException("cola-unknown-perk");
            researched.AddRange(Flags(Object(value), id => RewardColaCatalog.Challenges.TryGetValue(id, out var c) && c.Perk == perk));
        }
        var syn = Optional(root, "syn");
        return new(xp, prog, researched.Order(StringComparer.Ordinal).ToArray(),
            Numbers(Optional(syn, "prog"), id => RewardColaCatalog.Synergies.GetValueOrDefault(id, -1)),
            Flags(Optional(syn, "done"), RewardColaCatalog.Synergies.ContainsKey),
            Flags(Optional(root, "ach"), RewardColaCatalog.Achievements.ContainsKey));
    }

    private static void PrepareCurrentChallengeVersion(JsonObject root)
    {
        if (root.TryGetPropertyValue("chv", out var version) && Integer(version) == 2) return;
        // Upstream 2.2.1 resets specific incomplete counters while migrating old
        // challenge semantics. Never perform that destructive migration for it.
        // Fresh state needs the reviewed schema marker or its next native load
        // would discard newly transferred incomplete counters (PerkService.Migrate).
        if (Optional(root, "prog").Any(p => Object(p.Value).Count != 0)
            || Optional(root, "researched").Any(p => Object(p.Value).Count != 0))
            throw new RewardDataException("cola-ordinary-challenge-migration-required");
        root["chv"] = 2;
    }

    private static IReadOnlyDictionary<string, int> Numbers(JsonObject obj, Func<string, int> maximum, bool positive = false)
    {
        var values = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, node) in obj)
        {
            int bound = maximum(key);
            long value = Integer(node);
            if (bound < 0 || value < (positive ? 1 : 0) || value > bound)
                throw new RewardDataException("cola-unknown-or-out-of-range-progress");
            values.Add(key, (int)value);
        }
        return values;
    }

    private static IReadOnlyList<string> Flags(JsonObject obj, Func<string, bool> known)
    {
        var values = new List<string>();
        foreach (var (key, node) in obj)
        {
            if (!known(key) || node is not JsonValue value || !value.TryGetValue<bool>(out var flag))
                throw new RewardDataException("cola-invalid-completion");
            if (flag) values.Add(key);
        }
        return values.Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyDictionary<string, int> Difference(IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after, int maximum)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal))
        {
            long change = (long)after.GetValueOrDefault(key) - before.GetValueOrDefault(key);
            if (change < 0 || change > maximum) throw new RewardDataException("cola-regressed-or-excessive-progress");
            if (change > 0) result.Add(key, (int)change);
        }
        return result;
    }

    private static IReadOnlyList<string> Added(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        if (before.Except(after, StringComparer.Ordinal).Any()) throw new RewardDataException("cola-regressed-completion");
        return after.Except(before, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool Empty(RewardColaChanges d) => d.Xp.Count + d.Progress.Count + d.Researched.Count
        + d.SynergyProgress.Count + d.SynergyDone.Count + d.Achievements.Count == 0;

    private static string Write(RewardColaChanges d) => new JsonObject
    {
        ["schema"] = 1, ["catalog"] = RewardColaCatalog.Version,
        ["xp"] = Map(d.Xp), ["prog"] = Map(d.Progress), ["researched"] = Array(d.Researched),
        ["synProg"] = Map(d.SynergyProgress), ["synDone"] = Array(d.SynergyDone), ["ach"] = Array(d.Achievements)
    }.ToJsonString();

    private static JsonObject Map(IReadOnlyDictionary<string, int> values)
    {
        var result = new JsonObject();
        foreach (var (key, value) in values.OrderBy(p => p.Key, StringComparer.Ordinal)) result[key] = value;
        return result;
    }

    internal static JsonArray Array(IEnumerable<string> values) => new(values.Order(StringComparer.Ordinal)
        .Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    internal static IReadOnlyList<string> Set(JsonNode? node, Func<string, bool> known)
    {
        if (node is not JsonArray arr) throw new RewardDataException("cola-expected-array");
        var result = arr.Select(Text).ToArray();
        if (result.Any(v => !known(v)) || result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new RewardDataException("cola-unknown-or-duplicate-key");
        return result;
    }

    private static int Add(JsonObject obj, string id, int delta, int maximum, bool clamp)
    {
        long before = obj.TryGetPropertyValue(id, out var node) ? Integer(node) : 0;
        long value = before + delta;
        if (!clamp && value > maximum) throw new RewardDataException("cola-progress-overflow");
        int merged = (int)Math.Min(maximum, value);
        obj[id] = merged;
        return merged;
    }

    private static JsonObject ParseForeign(string json) => ParseObject(string.IsNullOrWhiteSpace(json) ? "{}" : json, MaxForeignBytes);
    private static JsonObject Optional(JsonObject obj, string key) => obj.TryGetPropertyValue(key, out var value) ? Object(value) : new();
    private static JsonObject Ensure(JsonObject obj, string key)
    {
        if (!obj.ContainsKey(key)) obj[key] = new JsonObject();
        return Object(obj[key]);
    }

    internal static JsonObject ParseObject(string json, int maxBytes)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > maxBytes) throw new RewardDataException("cola-json-too-large");
        try
        {
            using var parsed = JsonDocument.Parse(json);
            RejectDuplicates(parsed.RootElement);
            return Object(JsonNode.Parse(json));
        }
        catch (JsonException) { throw new RewardDataException("cola-invalid-json"); }
        catch (ArgumentException) { throw new RewardDataException("cola-invalid-json"); }
    }

    private static void RejectDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new RewardDataException("cola-duplicate-property");
                RejectDuplicates(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var value in node.EnumerateArray()) RejectDuplicates(value);
    }

    internal static void Fields(JsonObject obj, params string[] names)
    {
        if (obj.Count != names.Length || names.Any(n => !obj.ContainsKey(n))) throw new RewardDataException("cola-unexpected-fields");
    }
    internal static JsonObject Object(JsonNode? value) => value as JsonObject ?? throw new RewardDataException("cola-expected-object");
    internal static string Text(JsonNode? value) => value is JsonValue node && node.TryGetValue<string>(out var text)
        ? text : throw new RewardDataException("cola-expected-string");
    internal static long Integer(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var wide)) return wide;
            if (value.TryGetValue<int>(out var narrow)) return narrow;
        }
        throw new RewardDataException("cola-expected-integer");
    }
}
