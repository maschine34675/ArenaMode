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
        => ArenaPopulationPolicy.IsIndependentTeam(mode);
    public static bool IsSelectable(string mode, bool bounded, bool pitFireTeam)
        => !pitFireTeam && (IsSolo(mode) || !bounded && IsTeam(mode));
    public static int MinimumTotal(string mode) => IsTeam(mode) ? 4 : 2;
    public static int MaximumFor(string map, string mode)
        => ArenaTeamBoxCatalog.Applies(map, mode) ? 20 : MaximumTotal;
    public static int Step(string mode) => IsTeam(mode) ? 2 : 1;
    // Chop Shop defaulted to 6 while it had seven Bot/BotPmc rows (eight would have asked
    // for seven bots from seven rows). ArenaMapPort's rows of 2026-11-12 gave it sixteen,
    // as many as Bay5, Equator and Air Pit have, and Arena's own TeamFight on this map is
    // 5v5, so it joins the 8-group (seven bots from sixteen rows leaves nine spare). It is a
    // menu preset, not a ranking by area: Chop Shop is 1,915.6 m². A value saved per map
    // is not migrated; only a fresh configuration gets the new recommendation.
    // Equator keeps 4. That preset dates from 2026-09-21 and was never derived from the
    // number of rows (BurstMinAliveBots is 4 on every port); it is an older, smaller
    // preset of a 2,783 m² map, kept as it is until someone decides to change it. The
    // ranges stay generic (2-24 solo, 4-20 through the boxes).
    public static int DefaultTotal(string map, string mode)
        => ArenaMapCatalog.IsEquator(map) ? 4
            : (ArenaMapCatalog.IsBay5(map) || ArenaMapCatalog.IsAirPit(map) || ArenaMapCatalog.IsBowl(map)
                || ArenaMapCatalog.IsIceberg(map) || ArenaMapCatalog.IsAutoService(map)) ? 8
            : string.Equals(map, ArenaMapCatalog.LaboratoryId, StringComparison.OrdinalIgnoreCase) ? 16
            : string.Equals(map, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase) ? 8 : 10;

    // Null retains the legacy protocol except for original-box capacity-bound TDM.
    // Explicit values must never be coerced:
    // client/server acknowledge exactly the requested total before native start.
    public static bool TryResolve(string map, string mode, bool bounded, bool pitFireTeam,
        int? requested, out int? selected, out string? reason)
    {
        selected = null; reason = null;
        if (!requested.HasValue)
        {
            if (ArenaTeamBoxCatalog.Applies(map, mode))
            { reason = "Team DM with original team boxes requires an explicit participant count (4..20)."; return false; }
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
