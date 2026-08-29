using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Kits;

/// <summary>
/// Puts one kit's primary weapon into another kit.
///
/// Gun Game needs this because its two halves come from different places: the
/// ladder says which GUN you have earned, and it says nothing else, while the
/// armour, rig and pockets stay whatever the raid handed you. That is the mode
/// as it is actually played — the gun changes, you do not. It also means a
/// ladder rung can be a bare weapon rather than twenty full loadouts, which is
/// what makes generating the ladder from the game's own weapon presets possible.
///
/// The result is a plain kit definition, so everything downstream — validation,
/// the equipment surgeon, the respawn pipeline — stays exactly as it was.
/// </summary>
internal static class KitMerge
{
    private const string PrimarySlot = "FirstPrimaryWeapon";

    /// <summary>
    /// The base kit with its primary weapon replaced. Returns the base kit
    /// unchanged when the rung carries no weapon, and the rung unchanged when
    /// there is no base kit — a caller that ends up with either still gets
    /// something buildable.
    /// </summary>
    public static KitDefinition WithPrimaryWeapon(KitDefinition baseKit, KitDefinition rung)
    {
        if (baseKit?.Items == null || baseKit.Items.Count == 0)
            return rung;

        if (rung?.Items == null || rung.Items.Count == 0)
            return baseKit;

        var rungWeapon = rung.Items.FirstOrDefault(
            n => string.Equals(n.SlotId, PrimarySlot, StringComparison.OrdinalIgnoreCase));

        if (rungWeapon == null)
            return baseKit;

        var baseRoot = baseKit.Items.FirstOrDefault(n => string.IsNullOrEmpty(n.ParentId));
        if (baseRoot == null)
            return baseKit;

        var merged = new KitDefinition
        {
            Id = baseKit.Id + "+" + rung.Id,
            Name = rung.Name ?? baseKit.Name,
            Order = rung.Order,
            Items = new List<KitItemNode>(baseKit.Items.Count + rung.Items.Count),
            Fill = new KitFill
            {
                // Both halves have a say and the smaller one wins: the rung knows
                // what a magazine for THIS weapon is, the base kit knows how many
                // its rig can actually hold. Taking the rung's number alone put
                // the old "every kit asks for three regardless" straight back —
                // and an eight-cell plate carrier holds one.
                SpareMagazines = SmallerAsk(rung.Fill?.SpareMagazines, baseKit.Fill?.SpareMagazines),
                RigContents = baseKit.Fill?.RigContents ?? new List<KitStackedItem>(),
            },
        };

        // Everything from the base kit except its own primary weapon.
        var drop = SubtreeIds(baseKit.Items, baseKit.Items.Where(
            n => string.Equals(n.SlotId, PrimarySlot, StringComparison.OrdinalIgnoreCase)));

        foreach (var node in baseKit.Items)
        {
            if (!drop.Contains(node.Id))
                merged.Items.Add(node);
        }

        // The rung's weapon subtree, hung off the base kit's equipment root.
        // Node ids are template-local and the two kits were authored apart, so
        // they are prefixed: a collision would make the builder attach one kit's
        // parts to the other's tree.
        var keep = SubtreeIds(rung.Items, new[] { rungWeapon });
        foreach (var node in rung.Items)
        {
            if (!keep.Contains(node.Id))
                continue;

            merged.Items.Add(new KitItemNode
            {
                Id = "r_" + node.Id,
                Tpl = node.Tpl,
                SlotId = node.SlotId,
                Upd = node.Upd,
                ParentId = ReferenceEquals(node, rungWeapon) ? baseRoot.Id : "r_" + node.ParentId,
            });
        }

        return merged;
    }

    /// <summary>Whichever side asked for less, or the only side that asked.</summary>
    private static int SmallerAsk(int? fromRung, int? fromKit)
    {
        if (fromRung.HasValue && fromKit.HasValue)
            return Math.Min(fromRung.Value, fromKit.Value);

        return fromRung ?? fromKit ?? 0;
    }

    /// <summary>Every node at or below the given roots, by id.</summary>
    private static HashSet<string> SubtreeIds(List<KitItemNode> all, IEnumerable<KitItemNode> roots)
    {
        var ids = new HashSet<string>(roots.Select(n => n.Id), StringComparer.Ordinal);
        if (ids.Count == 0)
            return ids;

        // The tree is small and unsorted; sweep until nothing new is claimed.
        var guard = 0;
        bool grew;
        do
        {
            grew = false;
            foreach (var node in all)
            {
                if (node.ParentId != null && ids.Contains(node.ParentId) && ids.Add(node.Id))
                    grew = true;
            }
        }
        while (grew && guard++ < 64);

        return ids;
    }
}
