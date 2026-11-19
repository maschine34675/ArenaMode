using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils.Json;

// The two production files this harness compiles (ArenaPresetGuard.cs, LadderGenerator.cs) are replaced by
// mutants one at a time (Verify.ps1); every mutant must fail the assertion it names. The checks have names, a
// failing check prints "FAIL: <name>" and the exit code is 1. The generator part runs over the installed
// vanilla database (SPT_DATABASE): a missing database is a failure here, never a skip.

namespace ArenaMode.Server
{
    // The harness compiles LadderGenerator without the rest of the server, so the two statics it touches are
    // stood in for here with the members it uses.
    public class ArenaModeConfig
    {
        public List<string> ExcludedTemplates { get; set; } = new(ArenaTemplateEligibility.SeededExclusions);
    }

    public static class ArenaContext
    {
        public static ArenaModeConfig Config { get; set; } = new();
        public static ArenaWeaponVariants? WeaponVariants { get; set; }
        public static string LadderJson { get; set; } = "[]";
    }
}

public class HarnessLogger : DispatchProxy
{
    public static readonly List<(string Level, string Text)> Entries = [];
    public static bool ThrowOnWarning;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        // Only the left-out report: other Warnings of the load (the variant index has no JSON options here) are not under test.
        if (targetMethod?.Name == "Warning" && ThrowOnWarning && args?[0] is string warning && warning.Contains("Weapon pool: ", StringComparison.Ordinal))
            throw new InvalidOperationException("the logger refused the warning");

        if (args is { Length: > 0 } && args[0] is string text)
            Entries.Add((targetMethod?.Name ?? "?", text));

        return targetMethod?.ReturnType == typeof(void) ? null
            : targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
    }
}


internal static class Program
{
    private static int _failures;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
            return;
        _failures++;
        Console.WriteLine($"FAIL: {name}{(detail.Length > 0 ? " (" + detail + ")" : "")}");
    }

    /// <summary>A check that cannot be evaluated because the code under test threw is a failed check, named.</summary>
    private static void Check(string name, Func<bool> test)
    {
        try
        {
            Check(name, test());
        }
        catch (Exception ex)
        {
            Check(name, false, "threw " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // ------------------------------------------------------------------- hand-made database

    private static readonly MongoId Weapon = new("a00000000000000000000001");
    private static readonly MongoId Barrel = new("a00000000000000000000002");
    private static readonly MongoId Muzzle = new("a00000000000000000000003");
    private static readonly MongoId Magazine = new("a00000000000000000000004");
    private static readonly MongoId Ammo = new("a00000000000000000000005");
    private static readonly MongoId Other = new("a00000000000000000000006");
    private static readonly MongoId Unknown = new("ffffffffffffffffffffffff");

    private const string P1 = "b00000000000000000000001", P2 = "b00000000000000000000002", P3 = "b00000000000000000000003",
        P4 = "b00000000000000000000004", P5 = "b00000000000000000000005", P6 = "b00000000000000000000006",
        P7 = "b00000000000000000000007", P8 = "b00000000000000000000008", P9 = "b00000000000000000000009";

    private static Slot S(string name, bool required = false) => new() { Name = name, Required = required };

    private static TemplateItem T(MongoId id, IEnumerable<Slot>? slots = null, IEnumerable<Slot>? chambers = null, IEnumerable<Slot>? cartridges = null) => new()
    {
        Id = id,
        Name = "t",
        Parent = new MongoId("5422acb9af1c889c16000029"),
        Properties = new TemplateItemProperties { Slots = slots ?? [], Chambers = chambers ?? [], Cartridges = cartridges ?? [] },
    };

    private static Dictionary<MongoId, TemplateItem> SmallDatabase() => new()
    {
        [Weapon] = T(Weapon, [S("mod_barrel", true), S("mod_stock"), S("mod_magazine")], [S("patron_in_weapon")]),
        [Barrel] = T(Barrel, [S("mod_muzzle", true)]),
        [Muzzle] = T(Muzzle),
        [Magazine] = T(Magazine, cartridges: [S("cartridges")]),
        [Ammo] = T(Ammo),
        [Other] = T(Other),
    };

    private static Item I(string id, MongoId tpl, string? parent, string? slot)
        => new() { Id = new MongoId(id), Template = tpl, ParentId = parent, SlotId = slot };

    private static Preset Pre(MongoId? encyclopedia, params Item[] items)
        => new() { Id = new MongoId("c00000000000000000000001"), Name = "p", Parent = new MongoId("c00000000000000000000002"), Items = [.. items], Encyclopedia = encyclopedia };

    private static Item[] Complete() =>
    [
        I(P1, Weapon, null, null),
        I(P2, Barrel, P1, "mod_barrel"),
        I(P3, Muzzle, P2, "mod_muzzle"),
        I(P4, Magazine, P1, "mod_magazine"),
        I(P5, Ammo, P1, "patron_in_weapon"),
        I(P6, Ammo, P4, "cartridges"),
        I(P7, Ammo, P4, "cartridges"),
    ];

    private static string? Problem(Preset preset) => ArenaPresetGuard.Problem(preset, SmallDatabase());

    private static void GuardChecks()
    {
        Check("complete preset accepted", () => Problem(Pre(null, Complete())) == null);
        Check("repeated stack slot accepted", () => Problem(Pre(null, Complete())) == null && Complete().Count(i => i.SlotId == "cartridges") == 2);
        Check("chamber slot accepted", () => Problem(Pre(null, Complete())) == null && Complete().Any(i => i.SlotId == "patron_in_weapon"));
        Check("matching encyclopedia accepted", () => Problem(Pre(Weapon, Complete())) == null);
        Check("upper-case parent id accepted", () => Problem(Pre(null, Complete().Select(i => i.ParentId == null ? i : I(i.Id.ToString(), i.Template, i.ParentId.ToUpperInvariant(), i.SlotId)).ToArray())) == null);

        Check("null element rejected", () => ArenaPresetGuard.Problem(new Preset { Id = default, Name = "p", Parent = default, Items = [I(P1, Weapon, null, null), null!] }, SmallDatabase()) == "no-parts");
        Check("empty list rejected", () => Problem(Pre(null)) == "no-parts");
        Check("no root rejected", () => Problem(Pre(null, I(P1, Weapon, P1, "mod_barrel"))) == "no-root");
        Check("second root rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Other, null, null)])) == "several-roots");
        Check("empty parent id is not a root", () => Problem(Pre(null, [.. Complete(), I(P8, Other, "", "mod_stock")])) == $"bad-parent:{Other}");
        Check("duplicate id rejected", () => Problem(Pre(null, [.. Complete(), I(P2, Other, P1, "mod_stock")])) == $"duplicate-id:{P2}");
        Check("bad parent id rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Other, "zz", "mod_stock")])) == $"bad-parent:{Other}");
        Check("child without slot rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Other, P1, null)])) == $"no-slot:{Other}");
        Check("orphan rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Other, P9, "mod_stock")])) == $"orphan:{Other}");
        Check("cycle island rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Other, P9, "mod_stock"), I(P9, Other, P8, "mod_stock")])) == $"cycle:{Other}");
        Check("root mismatch rejected", () => Problem(Pre(Barrel, Complete())) == $"root-mismatch:{Barrel}!={Weapon}");
        Check("unknown template rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Unknown, P1, "mod_stock")])) == $"unknown-template:{Unknown}");
        Check("missing slot rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Other, P1, "mod_gone")])) == $"slot-missing:mod_gone on {Weapon}");
        Check("slot id differing in case only rejected", () => Problem(Pre(null, [.. Complete(), I(P8, Other, P1, "MOD_STOCK")])) == $"slot-missing:MOD_STOCK on {Weapon}");
        Check("required slot of root rejected", () => Problem(Pre(null, I(P1, Weapon, null, null))) == $"required-empty:mod_barrel on {Weapon}");
        Check("required slot of deeper part rejected", () => Problem(Pre(null, Complete().Where(i => i.Id.ToString() != P3).ToArray())) == $"required-empty:mod_muzzle on {Barrel}");
    }

    // ---------------------------------------------------------------- vanilla database and generator

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters())
            options.Converters.Add(converter);
        return options;
    }

    private sealed record Run(string Json, List<(string Level, string Text)> Log)
    {
        public JsonArray Pool => JsonNode.Parse(Json)!.AsArray();

        public int Count(string level, string contains) => Log.Count(l => l.Level == level && l.Text.Contains(contains, StringComparison.Ordinal));

        /// <summary>Per-entry Debug lines of one left-out preset, optionally with the exact reason it ends with.</summary>
        public int Reported(string name, string? reason = null)
            => Log.Count(l => l.Level == "Debug" && l.Text.Contains($"preset '{name}'", StringComparison.Ordinal)
                              && (reason == null || l.Text.EndsWith("left out: " + reason, StringComparison.Ordinal)));
    }

    private static async Task<Run> Generate(Dictionary<MongoId, TemplateItem> items, Dictionary<MongoId, Preset> presets, bool guard = true, bool throwOnWarning = false)
    {
        HarnessLogger.Entries.Clear();
        HarnessLogger.ThrowOnWarning = throwOnWarning;
        ArenaContext.LadderJson = "[{\"id\":\"shipped\"}]";
        ArenaContext.Config = new ArenaModeConfig();
        var templates = new TemplateTable
        {
            Items = items,
            Character = null!, CustomisationStorage = null!, Prestige = null!, Quests = null!,
            RepeatableQuests = null!, Handbook = null!, Customization = null!, Dialogue = null!,
            Profiles = null!, Prices = null!, DefaultEquipmentPresets = null!, Achievements = null!,
            CustomAchievements = null!, LocationServices = null!,
        };
        var globals = new GlobalTable { Configuration = null!, LocationInfection = null!, BotPresets = null!, BotWeaponScatterings = null!, ItemPresets = presets };
        var locales = new LocaleTable
        {
            Global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>>(),
            Menu = new Dictionary<string, Dictionary<string, object>>(),
            Languages = new Dictionary<string, string>(),
        };
        var logger = DispatchProxy.Create<ISptLogger<LadderGenerator>, HarnessLogger>();
        await new LadderGenerator(templates, globals, locales, logger) { GuardPresets = guard }.OnLoadAsync();
        HarnessLogger.ThrowOnWarning = false;
        return new Run(ArenaContext.LadderJson, [.. HarnessLogger.Entries]);
    }

    private static string Normalise(string poolJson)
    {
        var pool = JsonNode.Parse(poolJson)!.AsArray();
        foreach (var entry in pool)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            string Map(string id) => seen.TryGetValue(id, out var m) ? m : seen[id] = "n" + seen.Count;
            if (entry?["items"] is not JsonArray nodes)
                continue;
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (node["_id"] is JsonValue id && id.TryGetValue<string>(out var own))
                    node["_id"] = Map(own);
                if (node["parentId"] is JsonValue parent && parent.TryGetValue<string>(out var up))
                    node["parentId"] = Map(up);
            }
        }

        return pool.ToJsonString();
    }

    private static string WeaponOf(JsonNode? entry)
        => entry!["items"]!.AsArray().First(n => n!["slotId"]?.GetValue<string>() == "FirstPrimaryWeapon")!["_tpl"]!.GetValue<string>();

    private static bool IsWeapon(Dictionary<MongoId, TemplateItem> items, MongoId tpl)
    {
        var node = tpl;
        for (var depth = 0; depth < 12; depth++)
        {
            if (!items.TryGetValue(node, out var item))
                return false;
            if (item.Parent == new MongoId("5422acb9af1c889c16000029"))
                return true;
            node = item.Parent;
        }

        return false;
    }

    private static MongoId? RootOf(Preset p) => p.Encyclopedia ?? p.Items?.FirstOrDefault(i => i.ParentId == null)?.Template;

    private sealed class ThrowingSlots : IEnumerable<Slot>
    {
        public IEnumerator<Slot> GetEnumerator() => throw new InvalidOperationException("this template cannot be read");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static async Task GeneratorChecks(string database)
    {
        var options = Options();
        var typedItems = JsonSerializer.Deserialize<Dictionary<string, TemplateItem>>(File.ReadAllText(System.IO.Path.Combine(database, "templates", "items.json")), options)!;
        var items = typedItems.ToDictionary(x => new MongoId(x.Key), x => x.Value);
        using var globals = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(database, "globals.json")));
        var vanilla = globals.RootElement.GetProperty("ItemPresets").Deserialize<Dictionary<string, Preset>>(options)!
            .ToDictionary(x => new MongoId(x.Key), x => x.Value);

        var baseline = await Generate(items, vanilla);
        Check("vanilla pool is generated", baseline.Pool.Count >= 100, $"{baseline.Pool.Count} entries");
        Check("no Warning when nothing is left out", baseline.Count("Warning", "left out") == 0);
        var unguarded = await Generate(items, vanilla, guard: false);
        Check("vanilla pool identical with and without the guard", Normalise(baseline.Json) == Normalise(unguarded.Json));
        var pooled = baseline.Pool.Select(WeaponOf).ToHashSet();

        // The weapon: several vanilla presets, none named "default", and in the pool. The injected preset IS
        // named "default" (so Plainer takes it) and holds a part whose parent is in no node.
        var group = vanilla.Values
            .Where(p => RootOf(p) is { } r && IsWeapon(items, r))
            .GroupBy(p => RootOf(p)!.Value)
            .Where(g => g.Count() >= 2 && pooled.Contains(g.Key.ToString())
                        && g.All(p => p.Name?.Contains("default", StringComparison.OrdinalIgnoreCase) != true))
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .First();
        var source = group.OrderBy(p => p.Id.ToString(), StringComparer.Ordinal).First();
        var broken = new List<Item>(source.Items)
        {
            new() { Id = new MongoId("d00000000000000000000001"), Template = source.Items[0].Template, ParentId = "cccccccccccccccccccccccc", SlotId = "mod_nowhere" },
        };
        var injected = new Preset { Id = new MongoId("d00000000000000000000002"), Name = "Injected default", Parent = source.Parent, Items = broken, Encyclopedia = group.Key };
        var withBroken = new Dictionary<MongoId, Preset>(vanilla) { [injected.Id] = injected };

        // Precondition of the numbering mutant: a skipped candidate leaves a hole only when a later rung exists.
        var injectedAt = baseline.Pool.Select(WeaponOf).ToList().IndexOf(group.Key.ToString());
        Check("injected weapon is not the last rung of the baseline pool", injectedAt >= 0 && injectedAt < baseline.Pool.Count - 1, $"index {injectedAt} of {baseline.Pool.Count}");

        var guarded = await Generate(items, withBroken);
        Check("broken default preset costs nothing with the guard",
            guarded.Count("Error", "generation failed") == 0 && Normalise(guarded.Json) == Normalise(baseline.Json),
            $"{guarded.Pool.Count} entries against {baseline.Pool.Count}");
        Check("broken preset reported once with its reason",
            guarded.Count("Warning", "left out") == 1 && guarded.Count("Warning", "'Injected default'") == 1 && guarded.Count("Warning", "orphan:") == 1);

        var isolated = await Generate(items, withBroken, guard: false);
        Check("build isolation keeps the pool",
            isolated.Count("Error", "generation failed") == 0 && isolated.Pool.Count == baseline.Pool.Count - 1
            && isolated.Count("Warning", "build-threw:KeyNotFoundException") == 1,
            $"{isolated.Pool.Count} entries, errors {isolated.Count("Error", "generation failed")}");
        Check("numbering stays dense after a skipped candidate",
            isolated.Pool.Count > 0
            && isolated.Pool.Select((e, i) => e!["order"]!.GetValue<int>() == i && e["id"]!.GetValue<string>() == $"rung{i + 1:000}").All(x => x));

        var failingReport = await Generate(items, withBroken, throwOnWarning: true);
        Check("failing report keeps the pool",
            failingReport.Pool.Count == baseline.Pool.Count && failingReport.Count("Error", "") == 0
            && failingReport.Count("Debug", "left-out report failed") == 1);

        // Null elements: an armour-like preset (no Encyclopedia) is nobody's business, a weapon preset (known by
        // its Encyclopedia) is reported and costs only itself.
        var holes = new Dictionary<MongoId, Preset>(vanilla)
        {
            [new MongoId("d00000000000000000000003")] = new Preset { Id = new MongoId("d00000000000000000000003"), Name = "Armour with a hole", Parent = new MongoId("d00000000000000000000009"), Items = [null!] },
            // A null preset: the root lookup throws before any weapon is known, so the net contains it silently.
            [new MongoId("d00000000000000000000002")] = null!,
            [new MongoId("d00000000000000000000004")] = new Preset { Id = new MongoId("d00000000000000000000004"), Name = "Weapon with a hole", Parent = new MongoId("d00000000000000000000009"), Items = [null!], Encyclopedia = group.Key },
            [new MongoId("d00000000000000000000005")] = new Preset { Id = new MongoId("d00000000000000000000005"), Name = "Hole before root", Parent = new MongoId("d00000000000000000000006"), Items = [null!, new Item { Id = new MongoId("d00000000000000000000006"), Template = group.Key }] },
            [new MongoId("d00000000000000000000007")] = new Preset { Id = new MongoId("d00000000000000000000007"), Name = "No root part", Parent = new MongoId("d00000000000000000000008"), Items = [new Item { Id = new MongoId("d00000000000000000000008"), Template = group.Key, ParentId = "d00000000000000000000008", SlotId = "mod_barrel" }] },
        };
        var holed = await Generate(items, holes);
        Check("null elements contained and only a weapon reported",
            holed.Count("Error", "generation failed") == 0 && holed.Count("Warning", "left out") == 1
            && holed.Reported("Weapon with a hole") == 1 && holed.Count("Warning", "Armour with a hole") == 0 && holed.Count("Debug", "Armour with a hole") == 0
            && Normalise(holed.Json) == Normalise(baseline.Json));
        // The reasons are read from the per-entry Debug lines: the Warning shows only the first three entries.
        Check("null element of a weapon preset named no-parts", holed.Reported("Weapon with a hole", "no-parts") == 1);
        Check("null element before the root named no-parts", holed.Reported("Hole before root", "no-parts") == 1);
        Check("preset without a root part named no-root", holed.Reported("No root part", "no-root") == 1);

        // A candidate whose build throws inside Consider (a template that cannot be read): the other weapons stay.
        var magazine = new MongoId(baseline.Pool[0]!["items"]!.AsArray().First(n => n!["slotId"]?.GetValue<string>() == "mod_magazine")!["_tpl"]!.GetValue<string>());
        var original = items[magazine].Properties!.Cartridges;
        items[magazine].Properties!.Cartridges = new ThrowingSlots();
        try
        {
            var consider = await Generate(items, vanilla);
            // A weapon preset with a cartridge child under that magazine: the GUARD has to read the unreadable template
            // (SlotNames), so it throws for this one preset, which is what the per-preset net is for.
            var reading = new Preset
            {
                Id = new MongoId("d00000000000000000000010"), Name = "Reads the bad magazine", Parent = new MongoId("d00000000000000000000011"), Encyclopedia = group.Key,
                Items =
                [
                    new Item { Id = new MongoId("d00000000000000000000011"), Template = group.Key },
                    new Item { Id = new MongoId("d00000000000000000000012"), Template = magazine, ParentId = "d00000000000000000000011", SlotId = "mod_magazine" },
                    new Item { Id = new MongoId("d00000000000000000000013"), Template = magazine, ParentId = "d00000000000000000000012", SlotId = "cartridges" },
                ],
            };
            var reads = await Generate(items, new Dictionary<MongoId, Preset>(vanilla) { [reading.Id] = reading });
            Check("consider isolation keeps the pool",
                consider.Count("Error", "generation failed") == 0 && consider.Count("Debug", "consider-threw") == 1
                && consider.Pool.Count > 0 && consider.Pool.Count < baseline.Pool.Count,
                $"{consider.Pool.Count} entries against {baseline.Pool.Count}, errors {consider.Count("Error", "generation failed")}");
            // The same unreadable template also makes the guard throw for every preset with cartridge children under
            // that magazine: those presets are reported as preset-threw and the loop goes on.
            Check("preset loop isolation keeps the pool",
                reads.Count("Error", "generation failed") == 0 && reads.Reported("Reads the bad magazine", null) == 1 && reads.Count("Debug", "preset-threw:InvalidOperationException") == 1 && reads.Pool.Count > 0,
                $"{reads.Count("Debug", "preset-threw:")} preset-threw lines, {reads.Pool.Count} entries");
        }
        finally
        {
            items[magazine].Properties!.Cartridges = original;
        }
    }

    private static async Task<int> Main()
    {
        GuardChecks();

        var database = Environment.GetEnvironmentVariable("SPT_DATABASE");
        if (string.IsNullOrEmpty(database) || !File.Exists(System.IO.Path.Combine(database, "templates", "items.json")))
            Check("SPT_DATABASE names the installed database (a skip is not a pass)", false, database ?? "unset");
        else
            await GeneratorChecks(database);

        if (_failures == 0)
            Console.WriteLine("WEAPON_POOL_GUARD_PRODUCTION_PASSED");
        return _failures == 0 ? 0 : 1;
    }
}
