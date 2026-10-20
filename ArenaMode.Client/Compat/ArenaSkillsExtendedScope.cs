using System;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Physical world/body leases, not map names, profile IDs or a current-global Arena flag.</summary>
internal sealed class ArenaSkillsExtendedScope
{
    private sealed class WorldLease { }
    private sealed class BodyLease
    {
        internal readonly object World;
        internal BodyLease(object world) => World = world;
    }
    private ConditionalWeakTable<object, WorldLease> _worlds = new();
    private ConditionalWeakTable<object, BodyLease> _bodies = new();

    internal static bool CanCapture(bool exactNativeWorld, bool nativeLifecycle, bool pmc,
        string map, string settingsMap, bool active, bool armed, bool pending,
        string armedMap, string token, string nativeIdentity)
        => exactNativeWorld && nativeLifecycle && pmc && !string.IsNullOrWhiteSpace(map)
            && string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)
            && (active || armed && pending
                && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
                && Protocol.IsLifecycleRequestId(token)
                && Protocol.HasNativeEndIdentity(nativeIdentity, token));

    internal bool OwnsWorld(object world, object currentWorld, bool exactCurrent)
    {
        if (world == null) return false;
        if (_worlds.TryGetValue(world, out _)) return true;
        if (!exactCurrent || !ReferenceEquals(world, currentWorld)) return false;
        _worlds.Add(world, new WorldLease());
        return true;
    }

    internal bool OwnsBody(object body, object bodyWorld)
    {
        if (body == null || bodyWorld == null) return false;
        if (_bodies.TryGetValue(body, out var lease))
        {
            if (ReferenceEquals(lease.World, bodyWorld)) return true;
            _bodies.Remove(body);
        }
        if (!OwnsWorld(bodyWorld, null, false)) return false;
        _bodies.Add(body, new BodyLease(bodyWorld));
        return true;
    }

    internal void Clear()
    {
        _worlds = new ConditionalWeakTable<object, WorldLease>();
        _bodies = new ConditionalWeakTable<object, BodyLease>();
    }
}
