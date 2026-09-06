using System.Text.Json.Nodes;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace ArenaMode.Server.Services;

/// <summary>
/// A small optional supply request on the raid's private kit catalog, not a
/// profile edit or a prediction of available inventory space. The client adds
/// these LAST, through native rig/pocket grids, after existing gear and ammo.
/// Gun Game keeps the base kit's request while its current magazine determines
/// the actual free space. Content mods are observed through the live templates.
/// </summary>
internal sealed class ArenaKitSupplies
{
    internal const string Frag = "5448be9a4bdc2dfd2f8b456a"; // RGD-5: ordinary timed frag, no impact grenade
    internal const string Flash = "5a0c27731526d80618476ac4"; // Zarya
    internal static ArenaKitSupplies? Current { get; set; }
    private readonly Dictionary<string, ThrowWeapType?> _types;
    private readonly HashSet<string> _available;

    internal ArenaKitSupplies(IEnumerable<TemplateItem> templates)
    {
        var items = templates.Where(t => t.Type == "Item").ToArray();
        _types = items.ToDictionary(t => t.Id.ToString(), t => t.Properties?.ThrowType, StringComparer.Ordinal);
        _available = items.Where(t => (t.Id.ToString() == Frag && t.Properties?.ThrowType == ThrowWeapType.frag_grenade
                                      && t.Properties.MinTimeToContactExplode < 0)
                                     || (t.Id.ToString() == Flash && t.Properties?.ThrowType == ThrowWeapType.flash_grenade))
            .Where(t => t.Properties?.Width > 0 && t.Properties.Height > 0 && t.Properties.Prefab?.Path?.Length > 0)
            .Select(t => t.Id.ToString()).ToHashSet(StringComparer.Ordinal);
    }

    internal string CreateCatalog(string source, out int added)
    {
        added = 0;
        JsonArray catalog;
        try { catalog = JsonNode.Parse(source)?.AsArray() ?? throw new InvalidOperationException("missing kit array"); }
        catch { return source; } // user-authored JSON remains exactly as it was

        foreach (var node in catalog)
        {
            // Do not turn an intentionally missing/null fill into ammunition or
            // invent a packing layout for a malformed custom kit.
            if (node is not JsonObject kit || kit["items"] is not JsonArray items
                || kit["fill"] is not JsonObject fill
                || (fill["rigContents"] is not null && fill["rigContents"] is not JsonArray)) continue;
            var contents = fill["rigContents"] as JsonArray;
            if (items.Any(i => i is not JsonObject) || contents?.Any(i => i is not JsonObject) == true) continue;
            var existing = items.Select(i => Text(i?["_tpl"]))
                .Concat((contents ?? []).Select(i => Text(i?["tpl"]))).ToArray();
            if (existing.Any(tpl => !_types.ContainsKey(tpl))) continue; // unknown content: no guessing
            bool Has(ThrowWeapType type) => existing.Any(tpl => _types[tpl] == type);
            var requests = new List<string>(2);
            if (!Has(ThrowWeapType.frag_grenade) && _available.Contains(Frag)) requests.Add(Frag);
            if (!Has(ThrowWeapType.flash_grenade) && !Has(ThrowWeapType.stun_grenade)
                && _available.Contains(Flash)) requests.Add(Flash);
            if (requests.Count == 0) continue;
            if (contents == null) fill["rigContents"] = contents = [];
            foreach (string tpl in requests)
                contents.Add(new JsonObject { ["tpl"] = tpl, ["count"] = 1, ["optionalQuickUseOnly"] = true });
            added += requests.Count;
        }
        return added == 0 ? source : catalog.ToJsonString();
    }

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}

// Last-load snapshot, like the weapon index: no global bot generation/cache or
// profile access. Missing/changed target templates simply disable that request.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 99001)]
public sealed class ArenaKitSuppliesPostLoad(TemplateTable templates, ISptLogger<ArenaKitSuppliesPostLoad> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ArenaKitSupplies.Current = new ArenaKitSupplies(templates.Items.Values);
            logger.Info("[ArenaMode] Optional kit grenade supplies ready: one timed frag and one flash, native quick-use space permitting.");
        }
        catch (Exception ex)
        {
            ArenaKitSupplies.Current = null;
            logger.Warning($"[ArenaMode] Optional grenade supplies unavailable ({ex.Message}); existing kits retained.");
        }
        return Task.CompletedTask;
    }
}
