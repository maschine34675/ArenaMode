using System;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Effect bindings belong to a physical body in one admitted native raid.</summary>
internal sealed class ArenaEffectsScope
{
    internal sealed class Lease
    {
        internal object Game, World, Body, Controller;
        internal bool Retired;
    }
    private ConditionalWeakTable<object, Lease> _bodies = new();
    internal Lease Current;
    internal Lease Find(object body) => body != null && _bodies.TryGetValue(body, out var value) ? value : null;
    internal Lease Capture(object game, object world, object body, object controller)
    {
        if (game == null || world == null || body == null || controller == null)
            throw new InvalidOperationException("Effects body lease is incomplete");
        var existing = Find(body);
        if (existing != null)
        {
            if (existing.Retired || !ReferenceEquals(existing.Game, game) || !ReferenceEquals(existing.World, world)
                || !ReferenceEquals(existing.Controller, controller))
                throw new InvalidOperationException("Effects body identity was reused");
            return Current = existing;
        }
        var lease = new Lease { Game = game, World = world, Body = body, Controller = controller };
        _bodies.Add(body, lease);
        return Current = lease;
    }
    internal void Retire(Lease lease)
    {
        if (lease == null) return;
        lease.Retired = true;
        if (ReferenceEquals(Current, lease)) Current = null;
    }
    internal static bool ExactRaid(bool pair, bool lifecycle, bool pmc, string map, string settingsMap,
        bool active, bool armed, bool pending, string armedMap, string token, string nativeId)
        => pair && lifecycle && pmc && !string.IsNullOrWhiteSpace(map)
            && string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)
            && (active || armed && pending && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
                && Protocol.IsLifecycleRequestId(token) && Protocol.HasNativeEndIdentity(nativeId, token));
    internal static bool CanBind(bool exactRaid, bool local, bool sameWorld, bool sameProfile,
        bool authoritativeWorld, bool authoritativeGame, bool alive)
        => exactRaid && local && sameWorld && sameProfile && authoritativeWorld && authoritativeGame && alive;
    internal static bool CanReplace(Lease old, object game, object world, object previous, object next)
        => old != null && !old.Retired && !ReferenceEquals(previous, next)
            && ReferenceEquals(old.Body, previous) && ReferenceEquals(old.World, world)
            && ReferenceEquals(old.Game, game);
    internal void Clear() { Current = null; _bodies = new ConditionalWeakTable<object, Lease>(); }
}
