using System;
using System.Runtime.CompilerServices;

namespace ArenaMode.Compat;

/// <summary>Weak exact-body ownership survives a body's delayed medical callbacks.</summary>
internal sealed class ArenaContinuousHealingScope
{
    private sealed class Lease
    {
        internal readonly object World;
        internal Lease(object world) => World = world;
    }

    private ConditionalWeakTable<object, Lease> _bodies = new();

    internal bool Owns(object body, object bodyWorld, object currentWorld, bool exactActiveWorld)
    {
        if (body == null || bodyWorld == null) return false;
        if (_bodies.TryGetValue(body, out var lease))
        {
            // A reused native object must not inherit its previous world's lease.
            if (ReferenceEquals(lease.World, bodyWorld)) return true;
            _bodies.Remove(body);
        }
        if (!exactActiveWorld || !ReferenceEquals(bodyWorld, currentWorld)) return false;
        _bodies.Add(body, new Lease(bodyWorld));
        return true;
    }

    internal static bool Bypass(object body, object bodyWorld, object currentWorld,
        object currentLocalBody, bool isYourPlayer, bool alive)
        => !ReferenceEquals(bodyWorld, currentWorld) || !ReferenceEquals(body, currentLocalBody)
            || !isYourPlayer || !alive;

    internal void Clear() => _bodies = new ConditionalWeakTable<object, Lease>();
}
