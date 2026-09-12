#nullable enable

using System;

namespace ArenaMode.Shared;

/// <summary>
/// Per-arm bot relation contract. Legacy modes retain the server's configured
/// hostility; the new Deathmatch mode must explicitly negotiate its menu choice.
/// No missing/unknown value may silently become a different advertised rule.
/// </summary>
public static class DeathmatchSelection
{
    public const string ModeId = "deathmatch";
    public const string FreeForAll = "ffa";
    public const string AllVsPlayer = "all_vs_player";

    public static bool IsDeathmatch(string? mode)
        => string.Equals(mode, ModeId, StringComparison.OrdinalIgnoreCase);

    public static bool TryResolve(string? mode, string? requested, out string? frozen, out string reason)
    {
        frozen = null;
        reason = string.Empty;
        if (!IsDeathmatch(mode))
        {
            if (requested == null)
                return true;
            reason = "Deathmatch hostility is only valid for Deathmatch";
            return false;
        }

        if (!string.Equals(requested, FreeForAll, StringComparison.Ordinal)
            && !string.Equals(requested, AllVsPlayer, StringComparison.Ordinal))
        {
            reason = "Deathmatch requires an explicit ffa or all_vs_player hostility choice";
            return false;
        }

        frozen = requested;
        return true;
    }
}
