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
    public const string CustomsId = "bigmap";
    public const string EquatorId = "arena_equator";
    public const string Bay5Id = "arena_bay5";
    public const string AirPitId = "arena_airpit";
    public const string BowlId = "arena_bowl";
    public const string ArenaMapPortPluginId = "com.maschine.ArenaMapPort";
    public const int OptionalMapsRevision = 4;

    public const string LegacyDefaultClientAllowlist =
        FactoryDayId + "," + LabyrinthId + "," + LaboratoryId;

    public const string PreviousDefaultClientAllowlist =
        FactoryDayId + "," + FactoryNightId + "," + LabyrinthId + "," + LaboratoryId;

    public const string NativeDefaultClientAllowlist = PreviousDefaultClientAllowlist + "," + CustomsId;

    public const string EquatorDefaultClientAllowlist = NativeDefaultClientAllowlist + "," + EquatorId;
    public const string Bay5DefaultClientAllowlist = EquatorDefaultClientAllowlist + "," + Bay5Id;
    public const string AirPitDefaultClientAllowlist = Bay5DefaultClientAllowlist + "," + AirPitId;
    public const string DefaultClientAllowlist = AirPitDefaultClientAllowlist + "," + BowlId;

    public static bool IsSupportedMap(string locationId)
        => string.Equals(locationId, FactoryDayId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, FactoryNightId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, LabyrinthId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, LaboratoryId, StringComparison.OrdinalIgnoreCase)
           || string.Equals(locationId, CustomsId, StringComparison.OrdinalIgnoreCase)
           || IsOptionalMap(locationId);

    public static bool IsEquator(string locationId)
        => string.Equals(locationId, EquatorId, StringComparison.OrdinalIgnoreCase);

    public static bool IsBay5(string locationId)
        => string.Equals(locationId, Bay5Id, StringComparison.OrdinalIgnoreCase);

    public static bool IsAirPit(string locationId)
        => string.Equals(locationId, AirPitId, StringComparison.OrdinalIgnoreCase);

    public static bool IsBowl(string locationId)
        => string.Equals(locationId, BowlId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Explicit canonical lookup; unknown names return empty, never another map's ID.</summary>
    public static string CanonicalOptionalMapId(string locationId)
        => IsEquator(locationId) ? EquatorId : IsBay5(locationId) ? Bay5Id
            : IsAirPit(locationId) ? AirPitId : IsBowl(locationId) ? BowlId : string.Empty;

    public static bool IsOptionalMap(string locationId) => CanonicalOptionalMapId(locationId).Length != 0;

    public static bool RequiresModeFilteredView(string locationId)
        => string.Equals(locationId, CustomsId, StringComparison.OrdinalIgnoreCase) || IsOptionalMap(locationId);

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
        => UpgradeOptionalClientAllowlist(allowlist, 0, false);

    /// <summary>
    /// Revision 4 extends the exact AirPit default. Earlier revisions keep their own
    /// cutoffs: an Equator-only list at revision 2 means Bay5 was removed, and
    /// must not acquire Bay5, AirPit or Bowl. The old boolean likewise protects
    /// an intentional Equator removal that resembles a pre-Equator default.
    /// </summary>
    public static string UpgradeOptionalClientAllowlist(string allowlist, int revision, bool equatorMigrationRecorded)
        => revision < OptionalMapsRevision
           && (string.Equals(allowlist, AirPitDefaultClientAllowlist, StringComparison.Ordinal)
               || revision < 3 && string.Equals(allowlist, Bay5DefaultClientAllowlist, StringComparison.Ordinal)
               || revision < 2 && string.Equals(allowlist, EquatorDefaultClientAllowlist, StringComparison.Ordinal)
               || revision <= 0 && !equatorMigrationRecorded
                   && (string.Equals(allowlist, LegacyDefaultClientAllowlist, StringComparison.Ordinal)
                       || string.Equals(allowlist, PreviousDefaultClientAllowlist, StringComparison.Ordinal)
                       || string.Equals(allowlist, NativeDefaultClientAllowlist, StringComparison.Ordinal)))
            ? DefaultClientAllowlist
            : allowlist;

    public static bool AnyMapAvailable(Func<string, bool> isAvailable)
        => isAvailable != null
           && (isAvailable(FactoryDayId)
               || isAvailable(FactoryNightId)
               || isAvailable(LabyrinthId)
               || isAvailable(LaboratoryId)
               || isAvailable(CustomsId)
               || isAvailable(EquatorId)
               || isAvailable(Bay5Id)
               || isAvailable(AirPitId)
               || isAvailable(BowlId));

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
