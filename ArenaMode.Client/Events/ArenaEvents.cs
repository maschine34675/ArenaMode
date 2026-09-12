using ArenaMode.Combat;
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

    /// <summary>
    /// A kit is worn by the new body: raised after a successful death respawn or live
    /// swap (raid start, round break), once that body is the game's local player. Not
    /// raised for the protected 5v5 loadout restore, which is not a kit.
    /// </summary>
    public static event Action<KitDefinition> OnKitApplied;

    /// <summary>The local player dealt damage (hitmarker/damage-number feed).</summary>
    public static event Action<ArenaDamage> OnLocalDamageDealt;

    internal static void RaisePlayerDied(Player player) => Raise(() => OnPlayerDied?.Invoke(player), nameof(OnPlayerDied));
    internal static void RaisePlayerRespawned(Player player) => Raise(() => OnPlayerRespawned?.Invoke(player), nameof(OnPlayerRespawned));
    internal static void RaiseKitApplied(KitDefinition kit) => Raise(() => OnKitApplied?.Invoke(kit), nameof(OnKitApplied));
    internal static void RaiseLocalDamageDealt(ArenaDamage damage) => Raise(() => OnLocalDamageDealt?.Invoke(damage), nameof(OnLocalDamageDealt));

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
