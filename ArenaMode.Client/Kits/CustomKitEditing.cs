// Linked into the test project, which compiles with nullable enabled.
#nullable disable

using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ArenaMode.Kits;

/// <summary>
/// The editing operations of a player's own kit on its flat item tree (docs/reviews/CUSTOM-KITS-20261110.md),
/// pure so they are pinned offline. Every operation returns a NEW kit and never touches its input:
/// KitFacts and KitPreviewItems cache by kit object, and the kit of a raid is a held clone. Node
/// ids are fresh 24-hex ids; KitBuilder makes its own live ids at issue, these only have to be
/// unique inside the kit.
/// </summary>
internal static class CustomKitEditing
{
    /// <summary>A deep copy: items (upd included) and fill, nothing shared with the input.</summary>
    internal static KitDefinition Clone(KitDefinition kit)
    {
        if (kit == null) throw new ArgumentNullException(nameof(kit));
        var copy = new KitDefinition { Id = kit.Id, Name = kit.Name, Price = kit.Price, Order = kit.Order, Items = new List<KitItemNode>(), Fill = CloneFill(kit.Fill) };
        foreach (KitItemNode node in kit.Items ?? new List<KitItemNode>())
            if (node != null) copy.Items.Add(CloneNode(node));
        return copy;
    }

    /// <summary>
    /// A new custom kit from <paramref name="source"/> (a stock or custom kit): every node id fresh,
    /// the tree and fill as they are, no price and no order (both are the catalogue's business).
    /// </summary>
    internal static KitDefinition CopyOf(KitDefinition source, string id, string name, Func<string> newNodeId = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var ids = newNodeId ?? NewNodeId;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KitItemNode node in source.Items ?? new List<KitItemNode>())
            if (node?.Id != null && !map.ContainsKey(node.Id)) map[node.Id] = ids();
        var copy = new KitDefinition { Id = id, Name = name, Price = 0, Order = 0, Items = new List<KitItemNode>(), Fill = CloneFill(source.Fill) };
        foreach (KitItemNode node in source.Items ?? new List<KitItemNode>())
        {
            if (node == null) continue;
            KitItemNode clone = CloneNode(node);
            clone.Id = node.Id != null && map.TryGetValue(node.Id, out var fresh) ? fresh : ids();
            if (!string.IsNullOrEmpty(node.ParentId))
                clone.ParentId = map.TryGetValue(node.ParentId, out var parent) ? parent : node.ParentId;
            copy.Items.Add(clone);
        }

        return copy;
    }

    /// <summary>The same kit under another name.</summary>
    internal static KitDefinition Renamed(KitDefinition kit, string name)
    {
        KitDefinition copy = Clone(kit);
        copy.Name = name;
        return copy;
    }

    /// <summary>The ids of <paramref name="id"/> and everything below it, however deep.</summary>
    internal static HashSet<string> Subtree(IEnumerable<KitItemNode> items, string id)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(id)) return set;
        set.Add(id);
        var list = new List<KitItemNode>(items ?? Array.Empty<KitItemNode>());
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (KitItemNode node in list)
                if (node?.Id != null && node.ParentId != null && set.Contains(node.ParentId) && set.Add(node.Id))
                    grew = true;
        }

        return set;
    }

    /// <summary>The item directly under the root in <paramref name="slotId"/>, or null.</summary>
    internal static KitItemNode RootSlot(KitDefinition kit, string slotId)
    {
        string root = CustomKitStructure.RootId(kit);
        if (root == null) return null;
        foreach (KitItemNode node in kit.Items)
            if (node != null && string.Equals(node.ParentId, root, StringComparison.Ordinal)
                && string.Equals(node.SlotId, slotId, StringComparison.Ordinal))
                return node;
        return null;
    }

    /// <summary>
    /// The kit with the item in root slot <paramref name="slotId"/> (and everything below it) replaced
    /// by <paramref name="subtree"/>: the rows of one item tree whose own root is the row without a
    /// parent. The new rows get fresh ids, their root goes under the kit root in that slot; nothing
    /// else of the kit changes. An empty subtree clears the slot.
    /// </summary>
    internal static KitDefinition ReplaceRootSlot(KitDefinition kit, string slotId, IReadOnlyList<KitItemNode> subtree, Func<string> newNodeId = null)
    {
        if (kit == null) throw new ArgumentNullException(nameof(kit));
        string root = CustomKitStructure.RootId(kit) ?? throw new InvalidOperationException("The kit has no single root.");
        var ids = newNodeId ?? NewNodeId;
        KitDefinition copy = Clone(kit);
        KitItemNode old = RootSlot(copy, slotId);
        if (old != null)
        {
            var gone = Subtree(copy.Items, old.Id);
            copy.Items.RemoveAll(node => node?.Id != null && gone.Contains(node.Id));
        }

        if (subtree == null || subtree.Count == 0) return copy;

        string subtreeRoot = null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KitItemNode node in subtree)
        {
            if (node?.Id == null) throw new ArgumentException("A subtree row has no id.", nameof(subtree));
            if (map.ContainsKey(node.Id)) throw new ArgumentException("A subtree row id is used twice.", nameof(subtree));
            map[node.Id] = ids();
            if (string.IsNullOrEmpty(node.ParentId))
            {
                if (subtreeRoot != null) throw new ArgumentException("The subtree has two roots.", nameof(subtree));
                subtreeRoot = node.Id;
            }
        }

        if (subtreeRoot == null) throw new ArgumentException("The subtree has no root.", nameof(subtree));
        foreach (KitItemNode node in subtree)
        {
            KitItemNode clone = CloneNode(node);
            clone.Id = map[node.Id];
            if (node.Id == subtreeRoot)
            {
                clone.ParentId = root;
                clone.SlotId = slotId;
            }
            else
            {
                if (!map.TryGetValue(node.ParentId, out var parent))
                    throw new ArgumentException("A subtree row has a parent outside the subtree.", nameof(subtree));
                clone.ParentId = parent;
            }

            copy.Items.Add(clone);
        }

        return copy;
    }

    /// <summary>The kit without the rig supplies <paramref name="drop"/> names (loose ammunition of a calibre the kit no longer fires).</summary>
    internal static KitDefinition WithoutSupplies(KitDefinition kit, Func<string, bool> drop)
    {
        KitDefinition copy = Clone(kit);
        if (drop != null) copy.Fill.RigContents.RemoveAll(entry => entry?.Tpl != null && drop(entry.Tpl));
        return copy;
    }

    /// <summary>The rows the price rule reads, in kit order.</summary>
    internal static List<KitPriceNode> PriceRows(KitDefinition kit)
    {
        var rows = new List<KitPriceNode>(kit?.Items?.Count ?? 0);
        foreach (KitItemNode node in kit?.Items ?? new List<KitItemNode>())
            if (node != null) rows.Add(new KitPriceNode(node.Id, node.Tpl, node.ParentId, node.SlotId));
        return rows;
    }

    /// <summary>The kit's price by the shared rule, in whole roubles; 0 when the sum is 0.</summary>
    internal static int Price(KitDefinition kit, Func<string, double?> handbookPrice)
    {
        double price = KitPriceRule.Price(PriceRows(kit), handbookPrice);
        return price <= 0 ? 0 : price >= int.MaxValue ? int.MaxValue : (int)price;
    }

    /// <summary>
    /// The first item the price rule would count as free although it is part of what the kit hands
    /// out: every node needs a handbook price above 0 except the equipment root, the secure container
    /// with everything under it and the pockets (the arena's plumbing, pinned to stock templates by
    /// the structure check), and the items <paramref name="forced"/> names by node id (the inserts the
    /// game itself puts into a locked slot, some of which the handbook does not list). Null when
    /// every item is priced. Without this rule a hand-edited kit could carry an unlisted item free.
    /// </summary>
    internal static KitItemNode FirstUnpriced(KitDefinition kit, Func<string, double?> handbookPrice, Func<string, bool> forced)
    {
        if (kit?.Items == null || handbookPrice == null) return null;
        string root = CustomKitStructure.RootId(kit);
        HashSet<string> plumbing = KitPriceRule.SecureContainerSubtree(PriceRows(kit));
        foreach (KitItemNode node in kit.Items)
        {
            if (node?.Id == null || string.Equals(node.Id, root, StringComparison.Ordinal) || plumbing.Contains(node.Id)) continue;
            if (string.Equals(node.ParentId, root, StringComparison.Ordinal) && string.Equals(node.SlotId, CustomKitStructure.PocketsSlot, StringComparison.Ordinal)) continue;
            if (forced != null && forced(node.Id)) continue;
            if (!(handbookPrice(node.Tpl ?? string.Empty) is double price) || !(price > 0)) return node;
        }

        return null;
    }

    /// <summary>
    /// The first item Arena does not hand out (ArenaTemplateExclusions: the configured list, the seeded
    /// broken templates, the game's placeholder marker in <paramref name="displayName"/>), or null. The
    /// editor never offers such an item; this is for a stored kit, which a hand edit or a list that
    /// grew since the kit was built could make carry one.
    /// </summary>
    internal static KitItemNode FirstExcluded(KitDefinition kit, Func<string, string> displayName, IReadOnlyCollection<string> excluded)
    {
        if (kit?.Items == null) return null;
        foreach (KitItemNode node in kit.Items)
            if (node?.Tpl != null && ArenaTemplateExclusions.IsExcluded(node.Tpl, displayName?.Invoke(node.Tpl), excluded))
                return node;
        return null;
    }

    internal static string NewNodeId()
    {
        var bytes = new byte[12];
        using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(bytes);
        var chars = new char[24];
        for (int index = 0; index < 12; index++)
        {
            chars[2 * index] = Hex(bytes[index] >> 4);
            chars[2 * index + 1] = Hex(bytes[index] & 15);
        }

        return new string(chars);
    }

    private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);

    private static KitItemNode CloneNode(KitItemNode node)
        => new KitItemNode { Id = node.Id, Tpl = node.Tpl, ParentId = node.ParentId, SlotId = node.SlotId, Upd = node.Upd == null ? null : (JObject)node.Upd.DeepClone() };

    private static KitFill CloneFill(KitFill fill)
    {
        var copy = new KitFill { SpareMagazines = fill?.SpareMagazines ?? new KitFill().SpareMagazines, RigContents = new List<KitStackedItem>() };
        foreach (KitStackedItem entry in fill?.RigContents ?? new List<KitStackedItem>())
            if (entry != null) copy.RigContents.Add(new KitStackedItem { Tpl = entry.Tpl, Count = entry.Count, OptionalQuickUseOnly = entry.OptionalQuickUseOnly });
        return copy;
    }
}
