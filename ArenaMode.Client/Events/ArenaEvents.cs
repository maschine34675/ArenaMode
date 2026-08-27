using ArenaMode.Kits;
using EFT;
using System;

namespace ArenaMode.Events;

/// <summary>
/// Public event surface for the mode framework (M5+). Raises are exception-isolated
/// so a subscriber can never break the respawn pipeline.
/// </summary>
public static class ArenaEvents
{
    /// <summary>Local player died in an arena session (before the instant respawn starts).</summary>
    public static event Action<Player> OnPlayerDied;

    /// <summary>Replacement player is fully wired up.</summary>
    public static event Action<Player> OnPlayerRespawned;

    /// <summary>A kit was applied (raid start, respawn, or swap).</summary>
    public static event Action<KitDefinition> OnKitApplied;

    internal static void RaisePlayerDied(Player player) => Raise(() => OnPlayerDied?.Invoke(player), nameof(OnPlayerDied));
    internal static void RaisePlayerRespawned(Player player) => Raise(() => OnPlayerRespawned?.Invoke(player), nameof(OnPlayerRespawned));
    internal static void RaiseKitApplied(KitDefinition kit) => Raise(() => OnKitApplied?.Invoke(kit), nameof(OnKitApplied));

    private static void Raise(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena event {name} subscriber failed: {ex}");
        }
    }
}
