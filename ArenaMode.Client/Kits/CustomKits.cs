using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ArenaMode.Kits;

/// <summary>One of the player's own kits as this session sees it.</summary>
internal sealed class CustomKitEntry
{
    /// <summary>The kit with its price and order filled in; null for a record that cannot be read as a kit.</summary>
    internal KitDefinition Kit;
    internal string Id;
    internal CustomKitState State;
    /// <summary>Why the kit is not ready; null when it is.</summary>
    internal string Reason;
    internal int Price;
}

/// <summary>
/// The player's own kits read, priced and checked for this session (docs/reviews/CUSTOM-KITS-20261110.md).
/// The kit window builds one per opening and after every edit; a raid holds the one of its first
/// question (<see cref="KitCatalog.CustomView"/>), with deep copies of the kits, so the file changing
/// during a raid changes nothing in it.
/// </summary>
internal sealed class CustomKitView
{
    internal readonly List<CustomKitEntry> Entries = new();
    internal readonly List<KitDefinition> Ready = new();
    internal int StockCap;
    /// <summary>False when the stock catalogue was not loaded: a raid does not hold such a view.</summary>
    internal bool StockLoaded;
    internal string Banner;
    internal bool ReadOnly;

    internal static readonly CustomKitView Empty = new();

    internal CustomKitEntry Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (CustomKitEntry entry in Entries)
            if (entry.Kit != null && string.Equals(entry.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) return entry;
        return null;
    }
}

/// <summary>Where the kit file lives and how a view of it is made.</summary>
internal static class CustomKits
{
    internal const string FileName = "com.maschine.ArenaMode.custom-kits.json";

    /// <summary>The order every custom kit sorts behind the stock kits with the same price.</summary>
    internal const int OrderBase = 100000;

    internal static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, FileName);

    /// <summary>
    /// Reads the file and decides each kit's state: a structure problem (tier 1), an id a stock kit
    /// or an earlier custom kit already uses, an item Arena does not hand out, no price (no handbook,
    /// or a sum of 0), an engine problem (tier 2, which also wants a price for every item). Ready
    /// kits are deep copies with price and order filled in.
    /// </summary>
    internal static CustomKitView Load(IReadOnlyList<KitDefinition> stock)
    {
        var view = new CustomKitView { StockLoaded = stock != null };
        IReadOnlyList<KitDefinition> catalogue = stock ?? Array.Empty<KitDefinition>();
        view.StockCap = ArenaCustomKitPolicy.StockCap(catalogue.Where(kit => kit != null).Select(StockPrice));

        CustomKitFile file;
        try { file = CustomKitStore.Load(FilePath); }
        catch (Exception ex)
        {
            view.Banner = "Your kit file could not be read: " + ex.Message;
            view.ReadOnly = true;
            return view;
        }

        view.Banner = file.Banner;
        view.ReadOnly = file.ReadOnly;
        var envelope = CustomKitEnvelope.FromStock(catalogue);
        var taken = new HashSet<string>(catalogue.Where(kit => kit?.Id != null).Select(kit => kit.Id), StringComparer.OrdinalIgnoreCase);
        var records = file.Records.OrderBy(record => record.SavedUtc).ThenBy(record => record.Kit?.Id, StringComparer.Ordinal).ToList();
        bool priced = CustomKitPrices.Loaded;
        foreach (CustomKitRecord record in records)
        {
            var entry = new CustomKitEntry { Id = record.Kit?.Id, State = CustomKitState.Unavailable };
            view.Entries.Add(entry);
            if (record.Kit == null)
            {
                entry.Reason = record.Problem ?? "an unreadable kit record";
                continue;
            }

            KitDefinition kit = CustomKitEditing.Clone(record.Kit);
            entry.Kit = kit;
            if (!taken.Add(kit.Id))
            {
                entry.Reason = "its id is taken by another kit";
                continue;
            }

            var problems = CustomKitStructure.Validate(kit, envelope);
            if (problems.Count > 0)
            {
                entry.Reason = problems[0];
                continue;
            }

            // Not part of the cached engine check: the exclusion list is the server's config, which
            // can change while the game runs.
            entry.Reason = CustomKitItems.ExcludedItem(kit);
            if (entry.Reason != null) continue;

            entry.Price = priced ? CustomKitEditing.Price(kit, CustomKitPrices.Lookup) : 0;
            kit.Price = entry.Price;
            kit.Order = OrderBase + view.Entries.Count;
            if (entry.Price <= 0)
            {
                entry.State = CustomKitState.NoPrice;
                continue;
            }

            entry.Reason = CustomKitProbe.Check(kit);
            if (entry.Reason != null) continue;
            entry.State = CustomKitState.Ready;
            view.Ready.Add(kit);
        }

        return view;
    }

    /// <summary>A stock kit's price: the served one, or the shared rule over its items when the server sent 0 (a reload before the fix).</summary>
    internal static int StockPrice(KitDefinition kit)
        => kit.Price > 0 ? kit.Price : CustomKitPrices.PriceOf(kit);
}
