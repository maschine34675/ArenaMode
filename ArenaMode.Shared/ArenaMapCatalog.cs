using System;

namespace ArenaMode.Shared;

/// <summary>
/// Arena's supported native-map contract. Factory night is a time variant of
/// the Factory-day tile: EFT deliberately hides its own tile and swaps the
/// selected location between day/CURR and night/PAST through the time panel.
/// </summary>
public static class ArenaMapCatalog
{
    public const string FactoryDayId = "factory4_day";
    public const string FactoryNightId = "factory4_night";
    public const string LabyrinthId = "labyrinth";
    public const string LaboratoryId = "laboratory";

    public const string LegacyDefaultClientAllowlist =
        FactoryDayId + "," + LabyrinthId + "," + LaboratoryId;

    public const string DefaultClientAllowlist =
        FactoryDayId + "," + FactoryNightId + "," + LabyrinthId + "," + LaboratoryId;

    public static bool IsSupportedMap(string locationId)
        => string.Equals(locationId, FactoryDayId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, FactoryNightId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, LabyrinthId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, LaboratoryId, StringComparison.OrdinalIgnoreCase);

    public static bool IsFactoryVariant(string locationId)
        => string.Equals(locationId, FactoryDayId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, FactoryNightId, StringComparison.OrdinalIgnoreCase);

    public static bool IsFactoryNight(string locationId)
        => string.Equals(locationId, FactoryNightId, StringComparison.OrdinalIgnoreCase);

    public static bool AllowlistContains(string allowlist, string locationId)
    {
        if (!IsSupportedMap(locationId) || string.IsNullOrWhiteSpace(allowlist))
            return false;

        foreach (var entry in allowlist.Split(','))
        {
            if (string.Equals(entry.Trim(), locationId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// BepInEx preserves an existing value when a Bind default changes. Upgrade
    /// only the exact former default; any edited allowlist remains user-owned.
    /// </summary>
    public static string UpgradeLegacyClientAllowlist(string allowlist)
        => string.Equals(allowlist, LegacyDefaultClientAllowlist, StringComparison.Ordinal)
            ? DefaultClientAllowlist
            : allowlist;

    public static bool AnyMapAvailable(Func<string, bool> isAvailable)
        => isAvailable != null
           && (isAvailable(FactoryDayId)
               || isAvailable(FactoryNightId)
               || isAvailable(LabyrinthId)
               || isAvailable(LaboratoryId));

    /// <summary>
    /// EFT represents both Factory variants with the day tile. Keep the night
    /// database entry out of the generated tile list while making the day tile
    /// visible if either variant is available.
    /// </summary>
    public static bool ShouldDisplayNativeLocation(string locationId, Func<string, bool> isAvailable)
    {
        if (!IsSupportedMap(locationId) || isAvailable == null)
            return false;

        if (IsFactoryNight(locationId))
            return false;

        if (string.Equals(locationId, FactoryDayId, StringComparison.OrdinalIgnoreCase))
            return isAvailable(FactoryDayId) || isAvailable(FactoryNightId);

        return isAvailable(locationId);
    }

    /// <summary>The Factory-day tile remains clickable when only night is enabled.</summary>
    public static bool CanSelectNativeLocation(string locationId, Func<string, bool> isAvailable)
    {
        if (!IsSupportedMap(locationId) || isAvailable == null)
            return false;

        if (string.Equals(locationId, FactoryDayId, StringComparison.OrdinalIgnoreCase))
            return isAvailable(FactoryDayId) || isAvailable(FactoryNightId);

        return isAvailable(locationId);
    }

    /// <summary>
    /// Preserve the requested Factory time when it is enabled; otherwise choose
    /// the only enabled variant. False is the harmless result when neither exists.
    /// </summary>
    public static bool ShouldSelectFactoryNight(
        bool requestedNight,
        bool dayAvailable,
        bool nightAvailable)
    {
        if (requestedNight && nightAvailable)
            return true;

        if (!requestedNight && dayAvailable)
            return false;

        return nightAvailable;
    }

    public static bool HasFactoryTimeChoice(bool dayAvailable, bool nightAvailable)
        => dayAvailable && nightAvailable;
}
