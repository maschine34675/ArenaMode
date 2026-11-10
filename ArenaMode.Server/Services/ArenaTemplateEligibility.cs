using ArenaMode.Shared;

namespace ArenaMode.Server.Services;

/// <summary>
/// The server's name for <see cref="ArenaTemplateExclusions"/>, which holds the rules and their
/// reasons since the client needs them too (the custom-kit candidate lists,
/// docs/reviews/CUSTOM-KITS-20261110.md). Every member forwards; nothing here decides anything.
/// </summary>
public static class ArenaTemplateEligibility
{
    /// <inheritdoc cref="ArenaTemplateExclusions.KnownPlaceholders"/>
    public static IReadOnlyList<string> KnownPlaceholders => ArenaTemplateExclusions.KnownPlaceholders;

    /// <inheritdoc cref="ArenaTemplateExclusions.KnownBrokenTemplates"/>
    public static IReadOnlyList<string> KnownBrokenTemplates => ArenaTemplateExclusions.KnownBrokenTemplates;

    /// <inheritdoc cref="ArenaTemplateExclusions.SeededExclusions"/>
    public static IReadOnlyList<string> SeededExclusions => ArenaTemplateExclusions.SeededExclusions;

    /// <inheritdoc cref="ArenaTemplateExclusions.IsPlaceholderName"/>
    public static bool IsPlaceholderName(string? displayName) => ArenaTemplateExclusions.IsPlaceholderName(displayName);

    /// <inheritdoc cref="ArenaTemplateExclusions.ExclusionSet"/>
    public static HashSet<string> ExclusionSet(IEnumerable<string?>? configured) => ArenaTemplateExclusions.ExclusionSet(configured);

    /// <inheritdoc cref="ArenaTemplateExclusions.IsExcluded"/>
    public static bool IsExcluded(string? tpl, string? displayName, IReadOnlyCollection<string>? excluded)
        => ArenaTemplateExclusions.IsExcluded(tpl, displayName, excluded);
}
