using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaMode.Kits;

/// <summary>What the card presenter needs of a kit; free of game types so the rules can be checked offline.</summary>
internal sealed class ArenaKitCardSource
{
    internal string Id;
    internal string Name;
    internal int Price;
    internal int Order;
    /// <summary>One of the player's own kits (docs/reviews/CUSTOM-KITS-20261110.md).</summary>
    internal bool Custom;
    /// <summary>Why this card cannot be picked or bought here; null when it can.</summary>
    internal string Blocked;
}

/// <summary>One card of the kit browser as it is drawn: a kit, or the random draw.</summary>
internal sealed class ArenaKitCard
{
    /// <summary>The kit id; null on the random card.</summary>
    internal string Id;
    internal string Title;
    internal string PriceText;
    /// <summary>The short state word on the card ("SELECTED", "BUYING", "WEARING"), or empty.</summary>
    internal string Tag;
    internal bool Selected;
    internal bool Favorite;
    /// <summary>False when a click must do nothing: the menu is locked, or the kit is not affordable.</summary>
    internal bool Enabled;
    internal bool IsRandom;
    /// <summary>True when the card may carry a favourite mark: a kit whose id the favourite list can store.</summary>
    internal bool CanFavorite;
    internal bool Custom;
    /// <summary>The reason a card is greyed out beyond the purse or the lock (a custom kit above the cap, unusable here); null otherwise.</summary>
    internal string Blocked;
}

/// <summary>
/// Turns the catalogue and the player's state into the cards of the kit browser
/// (docs/reviews/KIT-PICKER-20261110.md). Two users: the menu picker, where a card chooses the
/// kit of the base draw, and solo Tactical's buy screen, where a card is a purchase. Cheapest
/// first in both, so the list reads as a ladder; the views only draw what this returns.
/// </summary>
internal static class ArenaKitCards
{
    internal const string RandomTitle = "RANDOM EACH LIFE";
    internal const string SelectedTag = "SELECTED";
    internal const string BuyingTag = "BUYING";
    internal const string WearingTag = "WEARING";
    internal const string CustomTag = "CUSTOM";

    internal static List<ArenaKitCard> ForPicker(IReadOnlyList<ArenaKitCardSource> kits, ArenaKitChoice choice,
        IEnumerable<string> favorites, bool favoritesOnly, bool locked)
    {
        var cards = new List<ArenaKitCard>();
        if (!favoritesOnly)
            cards.Add(new ArenaKitCard
            {
                Id = null, IsRandom = true, Title = RandomTitle, PriceText = string.Empty,
                Selected = choice.Random, Tag = choice.Random ? SelectedTag : string.Empty, Enabled = !locked,
            });

        foreach (ArenaKitCardSource kit in Sorted(kits))
        {
            bool favorite = ArenaKitFavorites.Contains(favorites, kit.Id);
            if (favoritesOnly && !favorite) continue;
            bool selected = choice.Selects(kit.Id);
            cards.Add(new ArenaKitCard
            {
                Id = kit.Id, Title = TitleOf(kit), PriceText = Money(kit.Price), Favorite = favorite,
                Selected = selected, Tag = selected ? SelectedTag : string.Empty,
                // A hand-written kits.json may use an id the choice cannot store (spaces,
                // commas, too long): the card stays visible, F12's Fixed Kit ID still takes it.
                Enabled = !locked && ArenaKitFavorites.IsKitId(kit.Id),
                // Favourites stay open while the picker is locked (display only), so this is
                // not Enabled; an id the list cannot store gets no mark at all.
                CanFavorite = ArenaKitFavorites.IsKitId(kit.Id),
            });
            Mark(cards[cards.Count - 1], kit);
        }

        return cards;
    }

    /// <summary>
    /// The buy screen: a kit that costs more than the purse stays visible and cannot be bought,
    /// the kit already chosen for the round cannot be bought twice, and the kit on the body is
    /// named so that buying nothing is a readable choice.
    /// </summary>
    internal static List<ArenaKitCard> ForShop(IReadOnlyList<ArenaKitCardSource> kits, int money,
        string boughtId, string wornId)
    {
        var cards = new List<ArenaKitCard>();
        foreach (ArenaKitCardSource kit in Sorted(kits))
        {
            bool bought = Same(kit.Id, boughtId);
            bool worn = Same(kit.Id, wornId);
            bool affordable = kit.Price <= money;
            cards.Add(new ArenaKitCard
            {
                Id = kit.Id, Title = TitleOf(kit),
                PriceText = affordable ? Money(kit.Price) : Money(kit.Price) + "  (short)",
                Selected = bought,
                Tag = bought ? BuyingTag : worn ? WearingTag : string.Empty,
                Enabled = affordable && !bought,
            });
            Mark(cards[cards.Count - 1], kit); // a custom kit the shop cannot sell is greyed too
        }

        return cards;
    }

    /// <summary>The one line above the picker's cards that says what the next raid will hand out.</summary>
    internal static string ChoiceLine(ArenaKitChoice choice, IReadOnlyList<ArenaKitCardSource> kits)
    {
        if (choice.Random) return "A random kit for every life";
        if (choice.KitId.Length == 0) return "The first kit of the catalogue, every life";
        if (kits != null)
            foreach (ArenaKitCardSource kit in kits)
                if (kit != null && choice.Selects(kit.Id))
                    return kit.Blocked == null ? TitleOf(kit) + ", every life"
                        : TitleOf(kit) + ": " + kit.Blocked + "; the first buildable kit is used";
        return "'" + choice.KitId + "' is not in the catalogue: the first buildable kit is used";
    }

    internal static string Money(int roubles)
        => roubles > 0 ? roubles.ToString("N0", CultureInfo.InvariantCulture) + " ₽" : "free";

    /// <summary>A purse, which is never "free": an empty one reads 0 ₽.</summary>
    internal static string Purse(int roubles)
        => Math.Max(0, roubles).ToString("N0", CultureInfo.InvariantCulture) + " ₽";

    /// <summary>
    /// A custom kit's card: tagged CUSTOM when no state tag is shown, and a blocked kit greyed out
    /// with its reason in the price line (it keeps a SELECTED or BUYING tag, so a stored choice
    /// that the cap now refuses is still visible).
    /// </summary>
    private static void Mark(ArenaKitCard card, ArenaKitCardSource kit)
    {
        card.Custom = kit.Custom;
        if (kit.Custom && string.IsNullOrEmpty(card.Tag)) card.Tag = CustomTag;
        if (kit.Blocked == null) return;
        card.Blocked = kit.Blocked;
        card.Enabled = false;
        card.PriceText = (card.PriceText ?? string.Empty) + "  \u00b7  " + kit.Blocked;
    }

    internal static string TitleOf(ArenaKitCardSource kit)
        => string.IsNullOrWhiteSpace(kit?.Name) ? kit?.Id ?? string.Empty : kit.Name.Trim();

    private static List<ArenaKitCardSource> Sorted(IReadOnlyList<ArenaKitCardSource> kits)
    {
        var sorted = new List<ArenaKitCardSource>();
        if (kits == null) return sorted;
        foreach (ArenaKitCardSource kit in kits)
            if (kit != null && !string.IsNullOrWhiteSpace(kit.Id)) sorted.Add(kit);
        // List.Sort is unstable; the comparison is total (price, authored order, id), so the
        // order does not depend on how the server happened to list the kits.
        sorted.Sort((a, b) =>
        {
            int byPrice = a.Price.CompareTo(b.Price);
            if (byPrice != 0) return byPrice;
            int byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });
        return sorted;
    }

    private static bool Same(string a, string b)
        => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
           && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
