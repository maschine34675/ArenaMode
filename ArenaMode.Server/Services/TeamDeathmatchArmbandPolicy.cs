using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace ArenaMode.Server.Services;

/// <summary>One generated bot's final faction marker, never a shared template or player inventory.</summary>
public static class TeamDeathmatchArmbandPolicy
{
    // Native SPT forceArmband convention, independent of APBS's weighted pools.
    public const string UsecBlue = "5b3f3af486f774679e752c1f";
    public const string BearRed = "5b3f3ade86f7746b6b790d8e";

    public static bool IsPmcRequest(BotGenerationDetails? details)
        => details is { IsPmc: true, IsPlayerScav: false }
            && string.IsNullOrEmpty(details.EventRole)
            && ((Equal(details.Role, "pmcUSEC") && Equal(details.Side, "Usec"))
                || (Equal(details.Role, "pmcBEAR") && Equal(details.Side, "Bear")));

    public static BotBase? Apply(BotBase? bot, BotGenerationDetails? details)
    {
        if (!IsPmcRequest(details)
            || bot?.Info?.Settings == null
            || bot.Id is not MongoId botId || botId.IsEmpty
            || !Equal(bot.Info.Settings.Role, details!.Role)
            || !Equal(bot.Info.Side, details.Side)
            || bot.Inventory?.Items == null
            || bot.Inventory.Equipment is not MongoId equipment || equipment.IsEmpty)
            return bot;

        var source = bot.Inventory.Items;
        // Missing/duplicate IDs are corrupt generation input; do not invent a
        // second root or try to repair arbitrary gear as part of identification.
        if (source.Any(item => item == null || item.Id.IsEmpty)
            || source.Select(item => item.Id).Distinct().Count() != source.Count
            || source.Count(item => item.Id == equipment) != 1)
            return bot;

        var parent = equipment.ToString();
        var template = new MongoId(Equal(details.Side, "Usec") ? UsecBlue : BearRed);
        var bands = source.Where(item => item.ParentId == parent && item.SlotId == "ArmBand").ToList();
        var removed = bands.Select(item => item.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        // Some third-party items use ArmBand as a container slot. Replacing
        // such a bot-only item must remove its descendants too, not orphan them
        // beneath an ordinary cloth band. Other equipment/loot stays identical.
        bool expanded;
        do
        {
            expanded = false;
            foreach (var item in source)
                if (item.ParentId != null && removed.Contains(item.ParentId))
                    expanded |= removed.Add(item.Id.ToString());
        } while (expanded);

        // A malformed parent cycle must never remove the inventory root.
        if (removed.Contains(parent))
            return bot;
        if (bands.Count == 1 && removed.Count == 1 && bands[0].Template == template)
            return bot;

        var items = source.Where(item => !removed.Contains(item.Id.ToString())).ToList();
        items.Add(new Item
        {
            Id = new MongoId(),
            Template = template,
            ParentId = parent,
            SlotId = "ArmBand",
        });

        // Preserve aliases held by generators/caches. Only this return value's
        // container and list change; no existing Item or Upd is edited in place.
        return bot with { Inventory = bot.Inventory with { Items = items } };
    }

    private static bool Equal(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
