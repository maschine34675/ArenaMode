using ArenaMode.Shared;
using System;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>What the candidate rule needs to know about one template, read from the game's item database.</summary>
internal sealed class CustomKitCandidateFacts
{
    internal string Tpl;
    /// <summary>The internal template name (<c>_name</c>), not the localized one.</summary>
    internal string InternalName;
    /// <summary>The display name as the game shows it; the placeholder marker lives here.</summary>
    internal string DisplayName;
    internal bool HasPrefab;
    internal bool IsStationary;
    internal bool IsWeapon;
    internal bool HasPreset;
    /// <summary>The handbook price; null when the handbook does not know the template.</summary>
    internal double? HandbookPrice;
}

/// <summary>
/// Which templates a player may put into an own kit (docs/reviews/CUSTOM-KITS-20261110.md). The item
/// database is not a list of finished things: the rule leaves out what the server's generators leave
/// out (the DO_NOT_USE placeholders, the seeded broken templates, the configured exclusions), what has
/// no model to show (no prefab), the stationary guns, the game's test weapon, every item without a
/// handbook price (the price rule would count it as free) and a weapon without a default preset (there
/// is no build of it to start from). Null means accepted; otherwise the reason.
/// </summary>
internal static class CustomKitCandidates
{
    internal static string Refusal(CustomKitCandidateFacts facts, IReadOnlyCollection<string> excluded)
    {
        if (facts == null || !CustomKitStructure.IsTemplateId(facts.Tpl)) return "not an item template";
        if (ArenaTemplateExclusions.IsExcluded(facts.Tpl, facts.DisplayName, excluded)) return "excluded for the arena";
        if (!facts.HasPrefab) return "no model";
        if (facts.IsStationary) return "a stationary weapon";
        if (facts.InternalName != null && facts.InternalName.StartsWith("weapon_test", StringComparison.OrdinalIgnoreCase))
            return "a test weapon";
        if (facts.HandbookPrice == null || facts.HandbookPrice <= 0) return "no handbook price";
        if (facts.IsWeapon && !facts.HasPreset) return "no default build";
        return null;
    }

    internal static bool Accept(CustomKitCandidateFacts facts, IReadOnlyCollection<string> excluded)
        => Refusal(facts, excluded) == null;
}
