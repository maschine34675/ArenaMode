#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Shared;

/// <summary>
/// Bot exclusions for Arena raids. Callers establish the current Arena context;
/// ordinary raids never consult this policy. Protected 5v5 also applies the
/// individual point rule to its leader, where PFT places the allied bots.
/// </summary>
public static class ArenaBotSpawnPolicy
{
    public static bool IsRestrictedMap(string? mapId)
        // Existing server callers use this for whole-zone wave restrictions.
        // A single Factory marker must not opt that map into Labs processing.
        => HasZoneExclusions(mapId);

    public static bool HasZoneExclusions(string? mapId)
        => string.Equals(mapId, "laboratory", StringComparison.OrdinalIgnoreCase);

    public static bool HasPointExclusions(string? mapId)
        => string.Equals(mapId, "factory4_day", StringComparison.OrdinalIgnoreCase)
           || string.Equals(mapId, "factory4_night", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Factory player markers Arena refuses. PFT places followers at their
    /// leader, so protected 5v5 must exclude them from the leader's choices as
    /// well; the rest of Factory's BotZone is untouched.
    ///
    /// Exact identities, never a height band or a zone: the first entry is the
    /// courtyard marker near the Woods transit, and the remaining five are one
    /// group on the y = -2.60 floor around x -6.7..-2.8 / z 14.3..18.5, reported
    /// from a playtest where the companions dropped there and stopped moving.
    /// They are excluded as a group because they are one place — the nearest two
    /// are 1.8 m apart, so excluding a single one only hands the next round the
    /// marker beside it. Coordinates are recorded in the installed-database
    /// fixture of LabsBotSpawnChecks, which fails if BSG moves any of them.
    /// </summary>
    private static readonly HashSet<string> FactoryExcludedPointIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "c671337f-e58f-4de6-a387-307d877954a3",
            "c2d01ce0-e0e4-41af-8311-44dc5cf38a93",
            "97998a75-baa4-4b03-ac1f-f86dfbf48d79",
            "ea010f6f-4860-4339-b576-09439f8f4c3a",
            "8128ea83-0d4f-4e64-b109-5acc9e477b33",
            "71c2ba1e-e226-47be-814f-d52ddd931603"
        };

    public static bool IsExcludedPoint(string? mapId, string? pointId)
        => HasPointExclusions(mapId)
           && !string.IsNullOrWhiteSpace(pointId)
           && FactoryExcludedPointIds.Contains(pointId);

    public static bool IsExcludedZone(string? mapId, string? zoneName)
        => HasZoneExclusions(mapId)
           && (string.Equals(zoneName, "BotZoneGate1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(zoneName, "BotZoneGate2", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Remove the sealed Labs extraction areas, including references from an
    /// older persisted configuration. An empty result has NO allowed named zone;
    /// callers must not interpret it as permission to choose any scene zone.
    /// Other maps retain their existing zone string unchanged.
    /// </summary>
    public static string FilterZones(string? mapId, string? csv)
    {
        if (!HasZoneExclusions(mapId))
            return csv ?? string.Empty;

        return string.Join(",", (csv ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(zone => zone.Trim())
            .Where(zone => zone.Length > 0 && !IsExcludedZone(mapId, zone)));
    }
}
