using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArenaMode.Server;
using ArenaMode.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

/// <summary>
/// The installed vanilla item database as typed server objects, and the Gun Game
/// pool LadderGenerator builds from it.
///
/// This is NOT the live pool. It has no handbook (so the cartridge rule is
/// skipped and prices are 0) and no English locale (so rung names fall back to
/// the internal template names): the fixture exists to compare two builds of the
/// generator over identical input, never to say what a player's server serves.
/// </summary>
internal static class WeaponPoolFixture
{
    internal static readonly MongoId WeaponBase = new("5422acb9af1c889c16000029");

    /// <summary>The database directory (the folder that holds globals.json and templates/), or null without a game install.</summary>
    internal static string? DatabaseDirectory
        => RepoFiles.ItemDatabase is { } items ? System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(items)) : null;

    internal static string ItemsPath => System.IO.Path.Combine(DatabaseDirectory!, "templates", "items.json");

    internal static string GlobalsPath => System.IO.Path.Combine(DatabaseDirectory!, "globals.json");

    /// <summary>The same converters as the live JsonUtil, in local options: tests must not touch the server's static configuration.</summary>
    internal static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters())
            options.Converters.Add(converter);
        return options;
    }

    internal static Dictionary<MongoId, TemplateItem> LoadTemplates()
    {
        var typed = JsonSerializer.Deserialize<Dictionary<string, TemplateItem>>(File.ReadAllText(ItemsPath), Options())!;
        return typed.ToDictionary(x => new MongoId(x.Key), x => x.Value);
    }

    /// <summary>Only the ItemPresets property of globals.json, keyed like the server table.</summary>
    internal static Dictionary<MongoId, Preset> LoadPresets()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(GlobalsPath));
        var typed = document.RootElement.GetProperty("ItemPresets").Deserialize<Dictionary<string, Preset>>(Options())!;
        return typed.ToDictionary(x => new MongoId(x.Key), x => x.Value);
    }

    internal static TemplateTable Templates(Dictionary<MongoId, TemplateItem> items) => new()
    {
        Items = items,
        Character = null!, CustomisationStorage = null!, Prestige = null!, Quests = null!,
        RepeatableQuests = null!, Handbook = null!, Customization = null!, Dialogue = null!,
        Profiles = null!, Prices = null!, DefaultEquipmentPresets = null!, Achievements = null!,
        CustomAchievements = null!, LocationServices = null!,
    };

    internal static GlobalTable Globals(Dictionary<MongoId, Preset> presets) => new()
    {
        Configuration = null!,
        LocationInfection = null!,
        BotPresets = null!,
        BotWeaponScatterings = null!,
        ItemPresets = presets,
    };

    internal static LocaleTable EmptyLocales() => new()
    {
        Global = new Dictionary<string, LazyLoad<GlobalLocaleDictionary>>(),
        Menu = new Dictionary<string, Dictionary<string, object>>(),
        Languages = new Dictionary<string, string>(),
    };

    /// <summary>What one generator run left behind: the pool JSON and everything it logged, with the logger method.</summary>
    internal sealed record Run(string Json, List<(string Level, string Text)> Log)
    {
        internal JsonArray Pool => JsonNode.Parse(Json)!.AsArray();

        internal int Count(string level, string contains)
            => Log.Count(l => l.Level == level && l.Text.Contains(contains, StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs the generator once and returns what it left in ArenaContext.LadderJson. The context statics the
    /// generator writes are restored afterwards; the caller holds the serial collection. The logger record is
    /// cleared first, so the Log of the result is this run's alone.
    /// </summary>
    internal static async Task<Run> GenerateAsync(
        Dictionary<MongoId, TemplateItem> items, Dictionary<MongoId, Preset> presets, bool guard = true)
    {
        var ladderBefore = ArenaContext.LadderJson;
        var configBefore = ArenaContext.Config;
        var variantsBefore = ArenaContext.WeaponVariants;
        try
        {
            const string shipped = "[{\"id\":\"shipped\"}]";
            LadderGeneratorTests.QuietLogger.Clear();
            ArenaContext.LadderJson = shipped;
            ArenaContext.Config = new ArenaModeConfig();
            var logger = LadderGeneratorTests.QuietLogger.Create<LadderGenerator>();
            var generator = new LadderGenerator(Templates(items), Globals(presets), EmptyLocales(), logger) { GuardPresets = guard };
            await generator.OnLoadAsync();
            return new Run(ArenaContext.LadderJson, [.. LadderGeneratorTests.QuietLogger.Entries]);
        }
        finally
        {
            ArenaContext.LadderJson = ladderBefore;
            ArenaContext.Config = configBefore;
            ArenaContext.WeaponVariants = variantsBefore;
        }
    }

    /// <summary>True when the template is the weapon base or sits (up to twelve levels) below it, like LadderGenerator.DescendsFrom.</summary>
    internal static bool IsWeapon(IReadOnlyDictionary<MongoId, TemplateItem> items, MongoId tpl)
    {
        var node = tpl;
        for (var depth = 0; depth < 12; depth++)
        {
            if (!items.TryGetValue(node, out var item))
                return false;
            if (item.Parent == WeaponBase)
                return true;
            node = item.Parent;
        }

        return false;
    }

    /// <summary>The root template LadderGenerator keys a preset by: its Encyclopedia, else its parentless node.</summary>
    internal static MongoId? RootOf(Preset preset)
        => preset.Encyclopedia ?? preset.Items?.FirstOrDefault(i => i.ParentId == null)?.Template;

    /// <summary>
    /// Node ids are a fresh random value per run (the generator's NewId), so two pools can never be equal as
    /// text. Every "_id" and "parentId" is replaced by the index of its first appearance within its entry; the
    /// rest (names, order, ids, templates, slots, fill, ammoChoices) is kept as is.
    /// </summary>
    internal static string Normalise(string poolJson)
    {
        var pool = JsonNode.Parse(poolJson)!.AsArray();
        foreach (var entry in pool)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            string Map(string id)
            {
                if (!seen.TryGetValue(id, out var mapped))
                    seen[id] = mapped = "n" + seen.Count;
                return mapped;
            }

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

        return pool.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    internal static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static string Sha256File(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
