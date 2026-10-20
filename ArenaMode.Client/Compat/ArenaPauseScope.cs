using System;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Pause ownership is physical game/world identity, never a map or profile ID alone.</summary>
internal sealed class ArenaPauseScope
{
    private sealed class Lease
    {
        internal readonly object Game;
        internal bool Notified;
        internal Lease(object game) => Game = game;
    }
    private ConditionalWeakTable<object, Lease> _worlds = new();

    internal static bool CanCapture(bool nativeLifecycle, bool pmc, string map, string settingsMap,
        bool exactActive, bool armed, bool pending, string armedMap, string token, string nativeIdentity)
        => nativeLifecycle && pmc && !string.IsNullOrWhiteSpace(map)
            && string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)
            && (exactActive || armed && pending
                && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
                && Protocol.IsLifecycleRequestId(token)
                && Protocol.HasNativeEndIdentity(nativeIdentity, token));

    internal bool Owns(object game, object world, bool exactCurrent, bool capture)
    {
        if (game == null || world == null || !exactCurrent) return false;
        if (_worlds.TryGetValue(world, out var lease)) return ReferenceEquals(lease.Game, game);
        if (!capture) return false;
        _worlds.Add(world, new Lease(game));
        return true;
    }

    internal bool FirstNotice(object world)
    {
        if (world == null || !_worlds.TryGetValue(world, out var lease) || lease.Notified) return false;
        lease.Notified = true;
        return true;
    }

    internal void Clear() => _worlds = new ConditionalWeakTable<object, Lease>();
}
