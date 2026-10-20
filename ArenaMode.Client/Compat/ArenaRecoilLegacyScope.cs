using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ArenaMode.Shared;

namespace ArenaMode.Compat;

/// <summary>Recoil components belong to physical players; a shared profile is not a lease.</summary>
internal sealed class ArenaRecoilLegacyScope
{
    internal sealed class Lease
    {
        internal object Body, World, Game, Recoil, Camera, LastHands;
        internal Lease Previous;
        internal bool Committed, Retired, Failed, RecoveryPending, OldRecoilEnabled, OldCameraEnabled;
        internal object[] OldCache;
    }

    private readonly List<Lease> _leases = new();
    private ConditionalWeakTable<object, Lease> _bodies = new();
    private ConditionalWeakTable<object, Lease> _hands = new();
    internal IEnumerable<Lease> Leases => _leases;
    internal Lease Find(object body)
        => body != null && _bodies.TryGetValue(body, out var lease) ? lease : null;
    internal Lease ForHands(object hands)
        => hands != null && _hands.TryGetValue(hands, out var lease) ? lease : null;
    internal void TrackHands(object hands, Lease lease)
    {
        if (hands == null || lease == null) return;
        if (_hands.TryGetValue(hands, out var old))
        {
            if (!ReferenceEquals(old, lease)) throw new InvalidOperationException("Recoil controller changed physical owner");
            return;
        }
        _hands.Add(hands, lease);
    }

    internal static bool ExactRaid(bool pair, bool lifecycle, bool pmc, string map, string settingsMap,
        bool active, bool armed, bool pending, string armedMap, string token, string nativeId)
        => pair && lifecycle && pmc && !string.IsNullOrWhiteSpace(map)
            && string.Equals(map, settingsMap, StringComparison.OrdinalIgnoreCase)
            && (active || armed && pending && string.Equals(map, armedMap, StringComparison.OrdinalIgnoreCase)
                && Protocol.IsLifecycleRequestId(token) && Protocol.HasNativeEndIdentity(nativeId, token));

    internal static bool MayCreate(bool exactRaid, bool local, bool sameWorld, bool sameProfile,
        bool registeredNewBody, bool initial, bool replacing, bool oldOwned, bool sameBody)
        => exactRaid && local && sameWorld && sameProfile && registeredNewBody
            && (initial || replacing && oldOwned && !sameBody);

    internal Lease Add(object body, object world, object game, Lease previous)
    {
        if (body == null || world == null || game == null || Find(body) != null)
            throw new InvalidOperationException("Recoil body already captured or incomplete");
        var lease = new Lease { Body = body, World = world, Game = game, Previous = previous };
        _bodies.Add(body, lease);
        _leases.Add(lease);
        return lease;
    }

    internal static bool Current(Lease lease, object world, object game, object main, object local,
        bool exactRaid, bool alive)
        => lease != null && !lease.Retired && !lease.Failed && exactRaid && alive
            && ReferenceEquals(lease.World, world) && ReferenceEquals(lease.Game, game)
            && ReferenceEquals(lease.Body, main) && ReferenceEquals(lease.Body, local);

    internal static bool OwnsComponent(Lease lease, object component)
        => lease != null && component != null
            && (ReferenceEquals(lease.Recoil, component) || ReferenceEquals(lease.Camera, component));

    internal void Prune(Predicate<Lease> removed) => _leases.RemoveAll(removed);
    internal void ClearActive() => _leases.Clear();
    internal void Clear()
    {
        _leases.Clear(); _bodies = new ConditionalWeakTable<object, Lease>();
        _hands = new ConditionalWeakTable<object, Lease>();
    }
}
