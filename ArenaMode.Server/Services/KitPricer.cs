using System.Text.Json.Nodes;
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
/// a legend. No scaling, because a scaled score would need explaining.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 98000)]
public class KitPricer(TemplateTable templateTable, ISptLogger<KitPricer> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var priced = Price(ArenaContext.KitsJson, out var count, out var cheapest, out var dearest);
            if (priced == null)
                return Task.CompletedTask;

            ArenaContext.KitsJson = priced;

            if (count > 0)
                logger.Info($"[ArenaMode] Priced {count} kit(s): {cheapest:N0} to {dearest:N0} roubles.");
        }
        catch (Exception ex)
        {
            // A kit without a price still builds; only the buy menu is poorer for
            // it. Never worth failing the mod load over.
            logger.Warning($"[ArenaMode] Kit pricing failed, kits stay unpriced: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private string? Price(string kitsJson, out int count, out double cheapest, out double dearest)
    {
        count = 0;
        cheapest = 0;
        dearest = 0;

        if (JsonNode.Parse(kitsJson) is not JsonArray kits)
            return null;

        var handbook = templateTable.Handbook?.Items;
        if (handbook == null)
            return null;

        // One dictionary rather than a scan per item: a kit is thirty-odd
        // templates and there are twenty kits, which is six hundred lookups
        // against a list of thousands.
        var prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in handbook)
        {
            if (entry.Id.ToString() is { } id)
                prices[id] = entry.Price ?? 0d;
        }

        foreach (var node in kits)
        {
            if (node is not JsonObject kit)
                continue;

            if (kit["price"] is not null)
                continue;

            var total = 0d;
            if (kit["items"] is JsonArray items)
            {
                foreach (var item in items)
                {
                    var tpl = item?["_tpl"]?.GetValue<string>();
                    if (tpl != null && prices.TryGetValue(tpl, out var price))
                        total += price;
                }
            }

            // Rounded to something a person reads as a price rather than as a
            // sum: 318,400 becomes 318,000.
            total = Math.Round(total / 1000d) * 1000d;

            kit["price"] = total;
            count++;
            cheapest = count == 1 ? total : Math.Min(cheapest, total);
            dearest = Math.Max(dearest, total);
        }

        return count > 0 ? kits.ToJsonString() : null;
    }
}
