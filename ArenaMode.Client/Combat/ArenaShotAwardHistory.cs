#nullable disable
using System;
using System.Runtime.CompilerServices;

namespace ArenaMode.Combat;

/// <summary>Presentation facts frozen at firing, never a retained native player/weapon/projectile.</summary>
internal readonly struct ArenaShotAwardFacts
{
    internal readonly long ProjectileToken;
    internal readonly bool AirborneHipFire;
    internal readonly bool BoltActionHipFire;
    internal readonly long ShooterBodyId;
    internal readonly long Epoch;
    internal bool IsKnown => ProjectileToken != 0 && Epoch != 0 && ShooterBodyId != 0;

    internal ArenaShotAwardFacts(long token, long epoch, long shooterBodyId,
        bool airborneHipFire, bool boltActionHipFire)
    {
        ProjectileToken = token;
        Epoch = epoch;
        ShooterBodyId = shooterBodyId;
        AirborneHipFire = airborneHipFire;
        BoltActionHipFire = boltActionHipFire;
    }
}

/// <summary>
/// Weak pooled-projectile identities. Every Create, including an unrecognized
/// create, invalidates the old entry. Parent facts are copied at child creation:
/// walking a mutable/recycled Parent chain later at impact is not safe.
/// Native callers admit exact Arena/fire/impact scopes; this class only verifies
/// the scalar shooter, weapon and fire identities supplied by those scopes.
/// </summary>
internal static class ArenaShotAwardHistory
{
    private static ConditionalWeakTable<object, Entry> _shots = new();
    private static long _epoch = 1;
    private static long _nextToken;
    internal static long CurrentEpoch => _epoch;

    private sealed class Entry
    {
        internal ArenaShotAwardFacts Facts;
        internal string WeaponId;
        internal int FireIndex;
    }

    internal static void Reset()
    {
        _shots = new ConditionalWeakTable<object, Entry>();
        // Tokens and epochs are never reused during the process lifetime.
        _epoch = checked(_epoch + 1);
    }

    internal static bool IsCurrent(ArenaShotAwardFacts facts)
        => facts.IsKnown && facts.Epoch == _epoch;

    internal static void RecordCreated(object shot, object parent, long shooterBodyId,
        string weaponId, int fireIndex, bool hasFireSnapshot,
        bool airborneHipFire, bool boltActionHipFire)
    {
        if (shot == null) return;
        _shots.Remove(shot);
        if (shooterBodyId == 0 || string.IsNullOrEmpty(weaponId)) return;

        if (parent != null)
        {
            var inherited = ForImpact(parent, shooterBodyId, weaponId, fireIndex);
            // A fragment/ricochet cannot borrow an unrelated ambient fire scope.
            if (!inherited.IsKnown) return;
            airborneHipFire = inherited.AirborneHipFire;
            boltActionHipFire = inherited.BoltActionHipFire;
        }
        else if (!hasFireSnapshot) return;

        _shots.Add(shot, new Entry
        {
            Facts = new ArenaShotAwardFacts(checked(++_nextToken), _epoch, shooterBodyId,
                airborneHipFire, boltActionHipFire),
            WeaponId = weaponId,
            FireIndex = fireIndex
        });
    }

    internal static ArenaShotAwardFacts ForImpact(object shot, long shooterBodyId,
        string weaponId, int fireIndex)
    {
        if (shot == null || shooterBodyId == 0 || string.IsNullOrEmpty(weaponId)
            || !_shots.TryGetValue(shot, out var entry)
            || !IsCurrent(entry.Facts)
            || entry.Facts.ShooterBodyId != shooterBodyId
            || !string.Equals(entry.WeaponId, weaponId, StringComparison.Ordinal)
            || entry.FireIndex != fireIndex) return default;
        return entry.Facts;
    }
}
