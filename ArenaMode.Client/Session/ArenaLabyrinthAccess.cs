using ArenaMode.Shared;
using EFT;
using JsonType;
using Redeploy.Core.Compat;
using System;
using System.Reflection;

namespace ArenaMode.Session;

/// <summary>
/// Labyrinth's native menu metadata is hidden and forces an online PVE raid.
/// Project only this Arena selection's view; the session database, preference
/// store and ordinary-raid location objects always keep their original values.
/// </summary>
internal static class ArenaLabyrinthAccess
{
    private static readonly MethodInfo CopyLocation = typeof(object).GetMethod(
        "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool IsLabyrinth(LocationSettings.Location location)
        => string.Equals(location?.Id, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase);

    public static LocationSettings.Location Project(RaidSettings settings, LocationSettings.Location location)
    {
        if (!CanProject(settings, location))
            return location;

        // EnableLabyrinth already supplies these values when co-installed.
        // Reuse that object (or our existing projection) instead of cloning it
        // again; none of its keys, Scav settings, exits or other data are edited.
        if (location.Enabled && !location.ForceOnlineRaidInPVE && location.IconY == 350f)
            return location;

        try
        {
            var view = (LocationSettings.Location)CopyLocation.Invoke(location, null);
            view.Enabled = true;
            view.IconY = 350f;
            view.ForceOnlineRaidInPVE = false;
            return view;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger?.LogWarning($"Arena Labyrinth menu access unavailable: {ex.Message}");
            return location;
        }
    }

    public static bool CanProject(RaidSettings settings, LocationSettings.Location location)
        => IsLabyrinth(location)
           && ArenaMenuSelection.IsSelectedFor(settings)
           && ArenaMenuSelection.CanStart
           && ArenaMenuSelection.IsMapAvailable(location.Id)
           && ArenaServerClient.IsServerModAvailable
           && !FikaCompatCheck.IsFikaLoaded;
}
