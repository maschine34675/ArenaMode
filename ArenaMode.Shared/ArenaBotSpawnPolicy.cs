#nullable enable

using System;
using System.Linq;

namespace ArenaMode.Shared;

/// <summary>
/// Bot-only exclusions for Arena raids. The caller must already have established
/// that the current raid is Arena; ordinary raids and player spawn selection
/// never consult this policy.
/// </summary>
public static class ArenaBotSpawnPolicy
{
    public static bool IsRestrictedMap(string? mapId)
        => string.Equals(mapId, "laboratory", StringComparison.OrdinalIgnoreCase);

    public static bool IsExcludedZone(string? mapId, string? zoneName)
        => IsRestrictedMap(mapId)
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
        if (!IsRestrictedMap(mapId))
            return csv ?? string.Empty;

        return string.Join(",", (csv ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(zone => zone.Trim())
            .Where(zone => zone.Length > 0 && !IsExcludedZone(mapId, zone)));
    }
}
