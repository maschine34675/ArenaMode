// Linked into the test project, which compiles with nullable enabled.
#nullable disable

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ArenaMode.Kits;

/// <summary>
/// What the stock catalogue allows a kit to carry outside its item tree, taken from the stock kits
/// themselves so a hand-edited custom kit cannot hoard what no stock kit has: the spare magazine
/// count, the rig supplies (templates, entries, counts), the secure container, the pockets, and the
/// upd values a stock kit sets (the weapon's durability, its fire mode).
/// </summary>
internal sealed class CustomKitEnvelope
{
    internal int MaxSpareMagazines;
    internal int MaxRigEntries;
    internal int MaxRigCount;
    internal double MaxDurability;
    internal readonly HashSet<string> RigTemplates = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> SecuredContainers = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> Pockets = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> FireModes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The envelope of <paramref name="stock"/>; an empty catalogue allows nothing.</summary>
    internal static CustomKitEnvelope FromStock(IEnumerable<KitDefinition> stock)
    {
        var envelope = new CustomKitEnvelope();
        if (stock == null) return envelope;
        foreach (KitDefinition kit in stock)
        {
            if (kit?.Items == null) continue;
            string root = CustomKitStructure.RootId(kit);
            foreach (KitItemNode node in kit.Items)
            {
                if (node?.Upd != null)
                {
                    if (node.Upd["Repairable"] is JObject repairable && CustomKitStructure.Number(repairable["MaxDurability"], out double max))
                        envelope.MaxDurability = Math.Max(envelope.MaxDurability, max);
                    if (node.Upd["FireMode"] is JObject fire && fire["FireMode"] is JValue mode && mode.Type == JTokenType.String)
                        envelope.FireModes.Add((string)mode);
                }

                if (node == null || root == null || !string.Equals(node.ParentId, root, StringComparison.Ordinal)) continue;
                if (string.Equals(node.SlotId, CustomKitStructure.SecuredContainerSlot, StringComparison.Ordinal) && node.Tpl != null)
                    envelope.SecuredContainers.Add(node.Tpl);
                if (string.Equals(node.SlotId, CustomKitStructure.PocketsSlot, StringComparison.Ordinal) && node.Tpl != null)
                    envelope.Pockets.Add(node.Tpl);
            }

            KitFill fill = kit.Fill;
            if (fill == null) continue;
            envelope.MaxSpareMagazines = Math.Max(envelope.MaxSpareMagazines, fill.SpareMagazines);
            int entries = 0;
            foreach (KitStackedItem entry in fill.RigContents ?? new List<KitStackedItem>())
            {
                if (entry?.Tpl == null || entry.OptionalQuickUseOnly) continue;
                entries++;
                envelope.RigTemplates.Add(entry.Tpl);
                envelope.MaxRigCount = Math.Max(envelope.MaxRigCount, entry.Count);
            }

            envelope.MaxRigEntries = Math.Max(envelope.MaxRigEntries, entries);
        }

        return envelope;
    }
}

/// <summary>
/// The structure check of a player's own kit (docs/reviews/CUSTOM-KITS-20261110.md), pure so it runs
/// offline: the first of two tiers. A kit the editor made always passes it, so a failure is either a
/// bug or a hand edit; a failing kit is kept in the file, shown as unavailable and never issued. The
/// rules mirror what the issuing paths need (TeamArenaEntryKit.ValidateDefinition for the PitFireTeam
/// entry: one root, unique ids, no orphan or cycle, one occupant per slot; upd values of the type
/// KitBuilder.ApplyUpd reads, which the strict entry path throws on) and close the price rule's holes
/// for a hand-edited file: a stack only on cartridges (the rule prices a node once), a childless
/// secure container of a stock template (its subtree is not priced), pockets of a stock template, the
/// fill block inside the stock envelope (it is not priced either), and no upd value a stock kit does
/// not set (durability above the stock weapon's, a medkit's resource: the price is of the template).
/// Template ids are lower-case hex as the database spells them: the handbook and the exclusion list
/// compare ids as text, so another spelling of a dear or excluded item would slip past both.
/// </summary>
internal static class CustomKitStructure
{
    internal const int MaxNodes = 400;
    internal const int MaxNameLength = 32;
    internal const string EquipmentTemplate = "55d7217a4bdc2d86028b456d";
    internal const string PrimarySlot = "FirstPrimaryWeapon";
    internal const string SecuredContainerSlot = "SecuredContainer";
    internal const string PocketsSlot = "Pockets";
    internal const string CartridgesSlot = "cartridges";

    /// <summary>The slots a custom kit may fill under its root: the ones the stock kits use.</summary>
    internal static readonly IReadOnlyList<string> RootSlots = new[]
    {
        PrimarySlot, "ArmorVest", "TacticalVest", "Headwear", "Earpiece", "Backpack", PocketsSlot, SecuredContainerSlot,
    };

    /// <summary>
    /// The upd keys a custom kit may carry: the ones the stock kits use, each with the value rule of
    /// <see cref="UpdProblem"/>. KitBuilder also applies MedKit, which no stock kit sets; a resource
    /// written there would be healing the price does not count, so it is refused.
    /// </summary>
    internal static readonly IReadOnlyList<string> UpdKeys = new[] { "StackObjectsCount", "Repairable", "FireMode", "Foldable" };

    /// <summary>Every problem of <paramref name="kit"/>; empty when it is structurally sound.</summary>
    internal static List<string> Validate(KitDefinition kit, CustomKitEnvelope envelope)
    {
        var problems = new List<string>();
        if (kit == null) { problems.Add("no kit"); return problems; }
        if (envelope == null) { problems.Add("no stock catalogue to compare with"); return problems; }
        if (!ArenaCustomKitId.IsWellFormed(kit.Id)) problems.Add("id is not a custom kit id");
        string name = kit.Name ?? string.Empty;
        if (name.Length == 0 || !string.Equals(name, SanitizeName(name), StringComparison.Ordinal))
            problems.Add("name is empty or carries characters a kit name may not");

        var items = kit.Items;
        if (items == null || items.Count == 0) { problems.Add("no items"); return problems; }
        if (items.Count > MaxNodes) { problems.Add("more than " + MaxNodes + " items"); return problems; }

        var byId = new Dictionary<string, KitItemNode>(StringComparer.Ordinal);
        string root = null;
        int roots = 0;
        foreach (KitItemNode node in items)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Id)) { problems.Add("an item without an id"); continue; }
            if (!IsTemplateId(node.Tpl)) problems.Add("item " + node.Id + " has no valid template");
            if (byId.ContainsKey(node.Id)) { problems.Add("item id " + node.Id + " is used twice"); continue; }
            byId[node.Id] = node;
            if (string.IsNullOrEmpty(node.ParentId)) { roots++; root = node.Id; }
        }

        if (roots != 1) { problems.Add("not exactly one root"); return problems; }
        if (!string.Equals(byId[root].Tpl, EquipmentTemplate, StringComparison.OrdinalIgnoreCase))
            problems.Add("the root is not the equipment template");

        var occupied = new HashSet<string>(StringComparer.Ordinal);
        var children = new Dictionary<string, int>(StringComparer.Ordinal);
        int primaries = 0;
        foreach (KitItemNode node in byId.Values)
        {
            if (ReferenceEquals(node, byId[root])) continue;
            if (string.IsNullOrEmpty(node.ParentId) || !byId.ContainsKey(node.ParentId)) { problems.Add("item " + node.Id + " has a missing parent"); continue; }
            if (string.IsNullOrWhiteSpace(node.SlotId)) { problems.Add("item " + node.Id + " has no slot"); continue; }
            // A magazine's cartridges are a stack slot: it holds several stacks (a 100-round box of
            // a round that stacks to 40 is three), as TeamArenaEntryKit.ValidateDefinition allows.
            if (!string.Equals(node.SlotId, CartridgesSlot, StringComparison.Ordinal) && !occupied.Add(node.ParentId + "\u0001" + node.SlotId))
                problems.Add("slot " + node.SlotId + " holds two items");
            children[node.ParentId] = children.TryGetValue(node.ParentId, out var count) ? count + 1 : 1;
            if (Cycles(node, byId)) { problems.Add("item " + node.Id + " is its own ancestor"); continue; }

            if (string.Equals(node.ParentId, root, StringComparison.Ordinal))
            {
                if (!Contains(RootSlots, node.SlotId)) problems.Add("slot " + node.SlotId + " is not a kit slot");
                if (string.Equals(node.SlotId, PrimarySlot, StringComparison.Ordinal)) primaries++;
                if (string.Equals(node.SlotId, SecuredContainerSlot, StringComparison.Ordinal) && !envelope.SecuredContainers.Contains(node.Tpl ?? string.Empty))
                    problems.Add("the secure container is not a stock one");
                if (string.Equals(node.SlotId, PocketsSlot, StringComparison.Ordinal) && !envelope.Pockets.Contains(node.Tpl ?? string.Empty))
                    problems.Add("the pockets are not stock ones");
            }

            if (node.Upd != null)
            {
                bool weapon = string.Equals(node.ParentId, root, StringComparison.Ordinal) && string.Equals(node.SlotId, PrimarySlot, StringComparison.Ordinal);
                string problem = UpdProblem(node, weapon, envelope);
                if (problem != null) problems.Add("item " + node.Id + " " + problem);
            }
        }

        if (primaries != 1) problems.Add("not exactly one primary weapon");
        foreach (KitItemNode node in byId.Values)
            if (string.Equals(node.ParentId, root, StringComparison.Ordinal)
                && string.Equals(node.SlotId, SecuredContainerSlot, StringComparison.Ordinal)
                && children.ContainsKey(node.Id))
                problems.Add("the secure container is not empty");

        KitFill fill = kit.Fill ?? new KitFill();
        if (fill.SpareMagazines < 0 || fill.SpareMagazines > envelope.MaxSpareMagazines)
            problems.Add("more spare magazines than any stock kit");
        var rig = fill.RigContents ?? new List<KitStackedItem>();
        if (rig.Count > envelope.MaxRigEntries) problems.Add("more supplies than any stock kit");
        foreach (KitStackedItem entry in rig)
        {
            if (entry == null || entry.OptionalQuickUseOnly || !envelope.RigTemplates.Contains(entry.Tpl ?? string.Empty)
                || entry.Count < 1 || entry.Count > envelope.MaxRigCount)
            {
                problems.Add("a supply no stock kit carries");
                break;
            }
        }

        return problems;
    }

    /// <summary>
    /// The name a kit is stored under: control characters and the rich-text brackets the kit cards
    /// would render dropped, runs of white space collapsed, trimmed, cut to 32 characters.
    /// </summary>
    internal static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var builder = new StringBuilder(name.Length);
        bool space = false;
        foreach (char c in name)
        {
            if (char.IsControl(c) || c == '<' || c == '>') continue;
            if (char.IsWhiteSpace(c)) { space = builder.Length > 0; continue; }
            if (space) { builder.Append(' '); space = false; }
            builder.Append(c);
        }

        string result = builder.ToString();
        return result.Length > MaxNameLength ? result.Substring(0, MaxNameLength).TrimEnd() : result;
    }

    /// <summary>The id of the node without a parent, or null when there is none or more than one.</summary>
    internal static string RootId(KitDefinition kit)
    {
        string root = null;
        if (kit?.Items == null) return null;
        foreach (KitItemNode node in kit.Items)
        {
            if (node == null || !string.IsNullOrEmpty(node.ParentId)) continue;
            if (root != null) return null;
            root = node.Id;
        }

        return root;
    }

    /// <summary>Twenty-four lower-case hex digits, the database's own spelling (see the class summary for why not upper case).</summary>
    internal static bool IsTemplateId(string tpl)
    {
        if (tpl == null || tpl.Length != 24) return false;
        foreach (char c in tpl)
            if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
        return true;
    }

    /// <summary>
    /// Why the node's upd is not one a custom kit may carry, or null. Each key has the value type
    /// KitBuilder.ApplyUpd reads (a wrong type throws there, and the strict PitFireTeam entry rethrows
    /// it into the raid start) and stays inside what a stock kit sets: a whole stack count of at least
    /// one, more than one only on cartridges; a durability only on the primary weapon, no higher than
    /// the stock weapons'; a fire mode a stock kit sets; a fold state as true or false.
    /// </summary>
    internal static string UpdProblem(KitItemNode node, bool primaryWeapon, CustomKitEnvelope envelope)
    {
        if (node?.Upd == null) return null;
        foreach (JProperty property in node.Upd.Properties())
        {
            switch (property.Name)
            {
                case "StackObjectsCount":
                    if (!Whole(property.Value, out long count) || count < 1) return "has a stack count that is not a whole number above 0";
                    if (count != 1 && !string.Equals(node.SlotId, CartridgesSlot, StringComparison.Ordinal)) return "is a stack outside a magazine";
                    break;
                case "Repairable":
                    if (!primaryWeapon || !(property.Value is JObject repairable) || !Only(repairable, "Durability", "MaxDurability")
                        || !Number(repairable["Durability"], out double durability) || !Number(repairable["MaxDurability"], out double maximum)
                        || durability <= 0 || durability > maximum || maximum > envelope.MaxDurability)
                        return "carries a durability no stock kit has";
                    break;
                case "FireMode":
                    if (!(property.Value is JObject fire) || !Only(fire, "FireMode") || !(fire["FireMode"] is JValue mode)
                        || mode.Type != JTokenType.String || !envelope.FireModes.Contains((string)mode))
                        return "carries a fire mode no stock kit sets";
                    break;
                case "Foldable":
                    if (!(property.Value is JObject fold) || !Only(fold, "Folded") || fold["Folded"]?.Type != JTokenType.Boolean)
                        return "carries a fold state that is not true or false";
                    break;
                default:
                    return "carries upd " + property.Name;
            }
        }

        return null;
    }

    /// <summary>A finite number (whole or not); never throws.</summary>
    internal static bool Number(JToken token, out double value)
    {
        value = 0;
        if (!(token is JValue json) || (json.Type != JTokenType.Integer && json.Type != JTokenType.Float)) return false;
        try { value = Convert.ToDouble(json.Value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is FormatException) { return false; }
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool Whole(JToken token, out long value)
    {
        value = 0;
        if (!(token is JValue json) || json.Type != JTokenType.Integer) return false;
        try { value = Convert.ToInt64(json.Value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is FormatException) { return false; }
        return true;
    }

    private static bool Only(JObject value, params string[] keys)
    {
        foreach (JProperty property in value.Properties())
            if (Array.IndexOf(keys, property.Name) < 0) return false;
        return true;
    }

    private static bool Cycles(KitItemNode node, Dictionary<string, KitItemNode> byId)
    {
        string current = node.ParentId;
        for (int depth = 0; depth <= byId.Count; depth++)
        {
            if (string.IsNullOrEmpty(current)) return false;
            if (string.Equals(current, node.Id, StringComparison.Ordinal)) return true;
            if (!byId.TryGetValue(current, out var parent)) return false;
            current = parent.ParentId;
        }

        return true;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (string entry in list)
            if (string.Equals(entry, value, StringComparison.Ordinal)) return true;
        return false;
    }
}
