using System.Text.Json.Nodes;
using ArenaMode.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ArenaMode.Server.Services;

/// <summary>
/// Gives every kit a price, so a mode with an economy has something to charge.
///
/// Computed here rather than written into kits.json by the generator, for two
/// reasons: the price then follows whatever the kit actually contains, including
/// a kit somebody edited or wrote by hand, and there is one implementation of
/// the rule instead of two that can disagree. A kit that already carries a price
/// keeps it — a deliberate number beats a derived one.
///
/// The number is the handbook sum in roubles, which is a currency the player
/// already has intuitions about: a kit that reads 320,000 is understood without
/// a legend. No scaling, because a scaled score would need explaining. The rule
/// itself lives in <see cref="KitPriceRule"/> (shared with the client, which prices
/// the player's own kits with it, docs/reviews/CUSTOM-KITS-20261110.md); this class
/// only applies it to the catalogue, at load and again after the config page's
/// "Reload from disk" (<see cref="Reprice"/>), which reads the unpriced kits.json.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 98000)]
public class KitPricer(TemplateTable templateTable, ISptLogger<KitPricer> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var prices = HandbookPrices(templateTable);
            if (prices == null)
                return Task.CompletedTask;

            ArenaContext.HandbookPrices = prices;

            var priced = Apply(ArenaContext.KitsJson, prices, out var count, out var cheapest, out var dearest);
            if (priced == null)
                return Task.CompletedTask;

            ArenaContext.KitsJson = priced;

            if (count > 0)
                logger.Debug($"[ArenaMode] Priced {count} kit(s): {cheapest:N0} to {dearest:N0} roubles.");
        }
        catch (Exception ex)
        {
            // A kit without a price still builds; only the buy menu is poorer for
            // it. Never worth failing the mod load over.
            logger.Warning($"[ArenaMode] Kit pricing failed, kits stay unpriced: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The handbook as one dictionary rather than a scan per item: a kit is thirty-odd
    /// templates and there are twenty kits, which is six hundred lookups against a list
    /// of thousands. Null when the database has no handbook.
    /// </summary>
    private static Dictionary<string, double>? HandbookPrices(TemplateTable templateTable)
    {
        var handbook = templateTable.Handbook?.Items;
        if (handbook == null)
            return null;

        var prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in handbook)
        {
            if (entry.Id.ToString() is { } id)
                prices[id] = entry.Price ?? 0d;
        }

        return prices;
    }

    /// <summary>
    /// Prices every kit of <paramref name="kitsJson"/> that has no price of its own with
    /// <see cref="KitPriceRule"/>. Null when the text is no array or no kit needed a price;
    /// throws on a kit whose ids are not strings, as the load always did.
    /// </summary>
    public static string? Apply(string kitsJson, IReadOnlyDictionary<string, double> prices,
        out int count, out double cheapest, out double dearest)
    {
        count = 0;
        cheapest = 0;
        dearest = 0;

        if (JsonNode.Parse(kitsJson) is not JsonArray kits)
            return null;

        Func<string, double?> lookup = tpl => prices.TryGetValue(tpl, out var price) ? price : null;
        foreach (var node in kits)
        {
            if (node is not JsonObject kit)
                continue;

            if (kit["price"] is not null)
                continue;

            var total = kit["items"] is JsonArray items ? KitPriceRule.Price(Rows(items), lookup) : 0d;

            kit["price"] = total;
            count++;
            cheapest = count == 1 ? total : Math.Min(cheapest, total);
            dearest = Math.Max(dearest, total);
        }

        return count > 0 ? kits.ToJsonString() : null;
    }

    /// <summary>
    /// For ConfigStore.Reload: the catalogue priced again with the handbook read at load,
    /// or the text as given when there is none yet or the text cannot be priced. Never
    /// throws: a reload that cannot price still reloads, and says so.
    /// </summary>
    public static string Reprice(string kitsJson)
    {
        var prices = ArenaContext.HandbookPrices;
        if (prices == null)
            return kitsJson;

        try
        {
            return Apply(kitsJson, prices, out _, out _, out _) ?? kitsJson;
        }
        catch (Exception ex)
        {
            ArenaContext.Logger?.Warning($"[ArenaMode] Kit pricing after the reload failed, kits stay unpriced: {ex.Message}");
            return kitsJson;
        }
    }

    private static List<KitPriceNode> Rows(JsonArray items)
    {
        var rows = new List<KitPriceNode>(items.Count);
        foreach (var item in items)
        {
            rows.Add(new KitPriceNode(
                item?["_id"]?.GetValue<string>(),
                item?["_tpl"]?.GetValue<string>(),
                item?["parentId"]?.GetValue<string>(),
                item?["slotId"]?.GetValue<string>()));
        }

        return rows;
    }
}
