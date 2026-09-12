using ArenaMode.Combat;
using ArenaMode.Kits;
using EFT;
using System;

namespace ArenaMode.Events;

/// <summary>
/// Event surface of ArenaMode's own mode framework (HUD, announcer, spectator, modes).
/// Raises are isolated per subscriber, so one throwing handler can neither break the
/// respawn pipeline nor silence the handlers after it. Other mods should not bind
/// these: they cover only what the mode framework needs — a live kit swap, for one,
/// never raises OnPlayerRespawned — while Redeploy.Core's events see every replacement.
/// </summary>
public static class ArenaEvents
{
    /// <summary>Local player died in an arena session (before the instant respawn starts).</summary>
    public static event Action<Player> OnPlayerDied;

    /// <summary>
    /// The death respawn's replacement body, published by the driver at least one frame
    /// after the engine finished wiring it (the driver's own checks and lease release
    /// come first). Never raised for live kit swaps (raid start, round break).
    /// </summary>
    public static event Action<Player> OnPlayerRespawned;

    /// <summary>
    /// A kit is worn by the new body: raised after a successful death respawn or live
    /// swap (raid start, round break), once that body is the game's local player. Not
    /// raised for the protected 5v5 loadout restore, which is not a kit.
    /// </summary>
    public static event Action<KitDefinition> OnKitApplied;

    /// <summary>The local player dealt damage (hitmarker/damage-number feed).</summary>
    public static event Action<ArenaDamage> OnLocalDamageDealt;

    internal static void RaisePlayerDied(Player player) => Raise(OnPlayerDied, h => h(player), nameof(OnPlayerDied));
    internal static void RaisePlayerRespawned(Player player) => Raise(OnPlayerRespawned, h => h(player), nameof(OnPlayerRespawned));
    internal static void RaiseKitApplied(KitDefinition kit) => Raise(OnKitApplied, h => h(kit), nameof(OnKitApplied));

    // The damage feed fires per hit, so it neither closes over its argument nor snapshots
    // the invocation list on every raise: a multicast delegate is immutable, and the
    // snapshot is refreshed only when the subscription changed. The other three events
    // are rare enough for the generic path.
    private static Action<ArenaDamage> _damageSnapshotFor;
    private static Delegate[] _damageSnapshot;

    internal static void RaiseLocalDamageDealt(ArenaDamage damage)
    {
        var multicast = OnLocalDamageDealt;
        if (multicast == null)
            return;

        if (!ReferenceEquals(multicast, _damageSnapshotFor))
        {
            _damageSnapshotFor = multicast;
            _damageSnapshot = multicast.GetInvocationList();
        }

        foreach (var handler in _damageSnapshot)
        {
            try
            {
                ((Action<ArenaDamage>)handler)(damage);
            }
            catch (Exception ex)
            {
                LogFailure(nameof(OnLocalDamageDealt), handler, ex);
            }
        }
    }

    // One subscriber at a time, the pattern Redeploy.Core uses for its raid lifecycle:
    // the match service subscribes before the spectator, and a throwing scoreboard
    // handler used to skip the spectator's Stop + RestoreFirstPerson.
    private static void Raise<TDelegate>(TDelegate multicast, Action<TDelegate> invoke, string name)
        where TDelegate : Delegate
    {
        if (multicast == null)
            return;

        foreach (var handler in multicast.GetInvocationList())
        {
            try
            {
                invoke((TDelegate)handler);
            }
            catch (Exception ex)
            {
                LogFailure(name, handler, ex);
            }
        }
    }

    private static void LogFailure(string name, Delegate handler, Exception ex)
    {
        var method = handler.Method;
        Plugin.ModLogger.LogWarning($"Arena event {name} subscriber {method.DeclaringType?.FullName}.{method.Name} failed: {ex}");
    }
}
