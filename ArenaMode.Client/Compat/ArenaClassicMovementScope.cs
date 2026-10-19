using System;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>The movement cache belongs to an exact native body, never a profile or selected map.</summary>
internal sealed class ArenaClassicMovementScope
{
    private sealed class Lease
    {
        internal readonly object Body, World;
        internal Lease(object body, object world) { Body = body; World = world; }
    }

    private ConditionalWeakTable<object, Lease> _contexts = new();

    internal static bool OwnsRaid(bool exactWorld, bool constructingOrRunning, bool pmc,
        string map, string settingsMap, bool active, bool armed, bool pending,
        string armedMap, string token, string nativeId)
    {
        if (!exactWorld || !constructingOrRunning || !pmc || string.IsNullOrWhiteSpace(map)
            || !string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)) return false;
        if (active) return true;
        // State caches are constructed before OnGameStarted. A mere selected map
        // or outgoing start marker is insufficient: the server must have accepted it.
        return armed && pending && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
            && Protocol.IsLifecycleRequestId(token) && Protocol.HasNativeEndIdentity(nativeId, token);
    }

    internal bool Owns(object context, object body, object bodyWorld, object currentContext,
        object currentWorld, bool nativeBody, bool stateConstruction, bool exactRaid)
    {
        if (context == null || body == null || bodyWorld == null || !nativeBody) return false;
        // During Create the caller has not assigned Player.MovementContext yet.
        // A different non-null context is never ours, even after a previous lease.
        if (!ReferenceEquals(currentContext, context) && !(stateConstruction && currentContext == null)) return false;
        if (_contexts.TryGetValue(context, out var lease))
        {
            if (ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.World, bodyWorld)) return true;
            _contexts.Remove(context);
        }
        if (!exactRaid || !ReferenceEquals(bodyWorld, currentWorld)) return false;
        _contexts.Add(context, new Lease(body, bodyWorld));
        return true;
    }

    internal void Clear() => _contexts = new ConditionalWeakTable<object, Lease>();
}
