#nullable enable
using System;

namespace ArenaMode.Shared;

/// <summary>Total concurrent participants, including one reserved human slot.</summary>
public static class ArenaParticipantPolicy
{
    public const int MaximumTotal = 24;
    public static bool IsSolo(string mode)
        => string.Equals(mode, "deathmatch", StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, "gungame", StringComparison.OrdinalIgnoreCase);
    public static bool IsTeam(string mode)
        => string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase);
    public static bool IsSelectable(string mode, bool bounded, bool pitFireTeam)
        => !pitFireTeam && (IsSolo(mode) || !bounded && IsTeam(mode));
    public static int MinimumTotal(string mode) => IsTeam(mode) ? 4 : 2;
    public static int MaximumFor(string map, string mode)
        => ArenaMapCatalog.IsEquator(map) && IsTeam(mode) ? 20 : MaximumTotal;
    public static int Step(string mode) => IsTeam(mode) ? 2 : 1;
    public static int DefaultTotal(string map, string mode)
        => ArenaMapCatalog.IsEquator(map) ? 4 : (ArenaMapCatalog.IsBay5(map) || ArenaMapCatalog.IsAirPit(map)) ? 8
            : string.Equals(map, ArenaMapCatalog.LaboratoryId, StringComparison.OrdinalIgnoreCase) ? 16
            : string.Equals(map, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase) ? 12 : 10;

    // Null retains the legacy protocol except for Equator's capacity-bound TDM.
    // Explicit values must never be coerced:
    // client/server acknowledge exactly the requested total before native start.
    public static bool TryResolve(string map, string mode, bool bounded, bool pitFireTeam,
        int? requested, out int? selected, out string? reason)
    {
        selected = null; reason = null;
        if (!requested.HasValue)
        {
            if (ArenaMapCatalog.IsEquator(map) && IsTeam(mode))
            { reason = "Equator Team DM requires an explicit participant count (4..20)."; return false; }
            return true;
        }
        if (!ArenaMapCatalog.IsSupportedMap(map) || !IsSelectable(mode, bounded, pitFireTeam))
        { reason = "This Arena format does not support a participant override."; return false; }
        if (requested.Value < MinimumTotal(mode) || requested.Value > MaximumFor(map, mode)
            || IsTeam(mode) && requested.Value % 2 != 0)
        { reason = IsTeam(mode) ? $"Select an even 4..{MaximumFor(map, mode)} for Team DM (you included)." : "Select 2..24 participants (you included)."; return false; }
        selected = requested.Value;
        return true;
    }
}
