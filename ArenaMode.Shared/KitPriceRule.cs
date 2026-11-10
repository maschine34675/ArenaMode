#nullable enable

using System;
using System.Collections.Generic;

namespace ArenaMode.Shared;

/// <summary>One item of a kit tree as the price rule sees it: its id, template, parent and slot.</summary>
public readonly struct KitPriceNode
{
    public KitPriceNode(string? id, string? tpl, string? parentId, string? slotId)
    {
        Id = id;
        Tpl = tpl;
        ParentId = parentId;
        SlotId = slotId;
    }

    public string? Id { get; }
    public string? Tpl { get; }
    public string? ParentId { get; }
    public string? SlotId { get; }
}

/// <summary>
/// What a kit costs (docs/reviews/CUSTOM-KITS-20261110.md): the handbook price of every item
/// of its tree, each node once, the secure container and everything under it left out, the sum
/// rounded to whole thousands. The server prices the stock catalogue with it at load and after
/// a reload (KitPricer); the client prices the player's own kits with it from the game's
/// handbook, which carries the same numbers. One implementation, so the two cannot disagree.
///
/// The parts of the rule and why: the handbook sum is a number a player already reads as roubles;
/// the secure container is the arena's plumbing (the Alpha alone was two thirds of a kit's price
/// before it was left out); a node counts once whatever its stack size, as KitPricer always did,
/// and the fill block (spare magazines, rig supplies) is not priced; an item the handbook does not
/// know counts nothing. Math.Round keeps its default, banker's rounding, because that is what the
/// prices of every published catalogue were computed with.
/// </summary>
public static class KitPriceRule
{
    public const double Unit = 1000d;

    public const string SecuredContainerSlot = "SecuredContainer";

    /// <summary>The rounded price of <paramref name="nodes"/>; <paramref name="handbookPrice"/> answers null for an unknown template.</summary>
    public static double Price(IReadOnlyList<KitPriceNode> nodes, Func<string, double?> handbookPrice)
    {
        if (nodes == null) throw new ArgumentNullException(nameof(nodes));
        if (handbookPrice == null) throw new ArgumentNullException(nameof(handbookPrice));

        var skipped = SecureContainerSubtree(nodes);
        var total = 0d;
        foreach (var node in nodes)
        {
            if (node.Id != null && skipped.Contains(node.Id))
                continue;

            if (node.Tpl != null && handbookPrice(node.Tpl) is double price)
                total += price;
        }

        // Rounded to something a person reads as a price rather than as a sum:
        // 318,400 becomes 318,000.
        return Math.Round(total / Unit) * Unit;
    }

    /// <summary>
    /// The ids of every node in a slot named SecuredContainer and of everything hanging off it,
    /// however deep: found by growing the set until a pass adds nothing.
    /// </summary>
    public static HashSet<string> SecureContainerSubtree(IReadOnlyList<KitPriceNode> nodes)
    {
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (string.Equals(node.SlotId, SecuredContainerSlot, StringComparison.OrdinalIgnoreCase) && node.Id != null)
                skipped.Add(node.Id);
        }

        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var node in nodes)
            {
                if (node.Id != null && node.ParentId != null && skipped.Contains(node.ParentId) && skipped.Add(node.Id))
                    grew = true;
            }
        }

        return skipped;
    }
}
