using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>
/// Display names of the fixed Team DM / Domination seats (2026-11-08, adopted from the
/// live Arena table). The first body in a seat gives the seat its name; every
/// replacement body is shown under that name in the scoreboard, the killfeed and the
/// death notices, so a team keeps the same five names all match. Display only: the
/// bot's profile is never written, and the registry is match-local (reset by
/// <see cref="TeamDeathmatchMode"/> at match start and end), so an ordinary raid never
/// sees it populated.
/// </summary>
internal static class TeamSeatNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);

    /// <summary>The seat name a replacement body must display, keyed by its profile id.</summary>
    internal static void Assign(string profileId, string name)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrWhiteSpace(name)) return;
        Names[profileId] = name;
    }

    internal static bool TryGet(string profileId, out string name)
    {
        name = null;
        return !string.IsNullOrEmpty(profileId) && Names.TryGetValue(profileId, out name);
    }

    internal static int Count => Names.Count;

    internal static void Reset() => Names.Clear();
}
