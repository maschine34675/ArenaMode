using System;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Exact physical ownership, retained weakly across unregister and delayed damage.</summary>
internal sealed class ArenaStatTrackScope
{
    private sealed class BodyLease
    {
        internal readonly object World;
        internal BodyLease(object world) => World = world;
    }
    private sealed class WeaponLease
    {
        internal readonly object Body, World, Owner;
        internal WeaponLease(object body, object world, object owner)
        { Body = body; World = world; Owner = owner; }
    }
    private ConditionalWeakTable<object, BodyLease> _bodies = new();
    private ConditionalWeakTable<object, WeaponLease> _weapons = new();

    internal static bool CanCaptureWorld(bool exactNativePair, bool started, bool pmc,
        string map, string settingsMap, bool active, bool armed, bool pending, string armedMap,
        string token, string nativeIdentity)
        => exactNativePair && started && pmc && !string.IsNullOrWhiteSpace(map)
            && string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)
            && (active || armed && pending && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
                && Protocol.IsLifecycleRequestId(token) && Protocol.HasNativeEndIdentity(nativeIdentity, token));

    internal bool OwnsBody(object body, object bodyWorld, object currentWorld, bool exactCurrent)
    {
        if (body == null || bodyWorld == null) return false;
        if (_bodies.TryGetValue(body, out var lease))
        {
            if (ReferenceEquals(lease.World, bodyWorld)) return true;
            _bodies.Remove(body);
        }
        if (!exactCurrent || !ReferenceEquals(bodyWorld, currentWorld)) return false;
        _bodies.Add(body, new BodyLease(bodyWorld));
        return true;
    }

    internal bool OwnsWeapon(object weapon, object owner, object body, object bodyWorld,
        object currentWorld, bool exactCurrent)
    {
        if (weapon == null) return false;
        if (body != null)
        {
            // A weapon transferred to another physical body must never inherit
            // protection because it has the same item/template/profile id.
            bool owned = OwnsBody(body, bodyWorld, currentWorld, exactCurrent);
            _weapons.Remove(weapon);
            if (owned) _weapons.Add(weapon, new WeaponLease(body, bodyWorld, owner));
            return owned;
        }
        // Once detached, only the exact already observed source can name the
        // old Arena shot. An ordinary owner invalidates that association.
        if (_weapons.TryGetValue(weapon, out var lease)
            && (owner == null || ReferenceEquals(owner, lease.Owner))
            && _bodies.TryGetValue(lease.Body, out var bodyLease)
            && ReferenceEquals(lease.World, bodyLease.World)) return true;
        _weapons.Remove(weapon);
        return false;
    }

    internal void Clear()
    {
        _bodies = new ConditionalWeakTable<object, BodyLease>();
        _weapons = new ConditionalWeakTable<object, WeaponLease>();
    }
}
