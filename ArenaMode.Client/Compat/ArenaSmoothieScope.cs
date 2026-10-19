using System;

namespace ArenaMode.Compat;

/// <summary>No cached raid flag or foreign state: each invocation proves its current world.</summary>
internal static class ArenaSmoothieScope
{
    internal static bool SuppressCleanup(bool exactLocalWorld, bool exactActiveRaid,
        bool started, bool pmc, string worldMap, string settingsMap)
        => exactLocalWorld && exactActiveRaid && started && pmc
            && !string.IsNullOrWhiteSpace(worldMap)
            && string.Equals(worldMap, settingsMap, StringComparison.OrdinalIgnoreCase);

    // Smoothie 1.0.8 excludes Loot and Interactive from static-mesh discovery.
    // Never assume an EFT layer number or change a camera mask to make it work.
    internal static int MarkerLayer(bool available, bool hasCamera, int mainMask, int? opticMask,
        int lootLayer, int interactiveLayer)
    {
        if (!available || !hasCamera) return -1;
        int visibleMask = mainMask & (opticMask ?? -1);
        if (Visible(lootLayer, visibleMask)) return lootLayer;
        return Visible(interactiveLayer, visibleMask) ? interactiveLayer : -1;
    }

    private static bool Visible(int layer, int mask)
        => layer >= 0 && layer < 32 && (mask & (1 << layer)) != 0;
}
