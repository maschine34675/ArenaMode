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
/// Short-lived managed proof of an accepted direct lethal contact. It contains
/// only immutable scalar identity and a two-victim presentation ledger, never
/// native Shot/Player/Weapon references. Settlement waits for mode admission.
/// </summary>
internal readonly struct ArenaCollateralProof
{
    internal readonly ArenaShotAwardFacts Facts;
    internal readonly ArenaShotAwardHistory.CollateralLineage Lineage;
    internal ArenaCollateralProof(ArenaShotAwardFacts facts, ArenaShotAwardHistory.CollateralLineage lineage)
    { Facts = facts; Lineage = lineage; }
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
        internal long WorldId, GameId;
        internal CollateralLineage Collateral;
        internal bool Continued;
    }

    // Shared by the original bullet and its sole native penetration continuation.
    // Children are created BEFORE the parent's hit callback, so copying a kill
    // count at creation would lose the first death. No native objects are retained.
    internal sealed class CollateralLineage
    {
        internal long FirstVictim;
        internal bool Awarded;
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
        bool airborneHipFire, bool boltActionHipFire, bool singleBullet = false,
        bool continuedBullet = false, long parentToken = 0, long worldId = 0, long gameId = 0)
    {
        if (shot == null) return;
        _shots.Remove(shot);
        if (shooterBodyId == 0 || string.IsNullOrEmpty(weaponId)) return;

        CollateralLineage collateral = null;
        if (parent != null)
        {
            var inherited = ForImpact(parent, shooterBodyId, weaponId, fireIndex, worldId, gameId);
            // A fragment/ricochet cannot borrow an unrelated ambient fire scope.
            if (!inherited.IsKnown) return;
            airborneHipFire = inherited.AirborneHipFire;
            boltActionHipFire = inherited.BoltActionHipFire;
            if (singleBullet && continuedBullet && parentToken == inherited.ProjectileToken
                && _shots.TryGetValue(parent, out var source) && !source.Continued)
            {
                source.Continued = true;
                collateral = source.Collateral;
            }
        }
        else if (!hasFireSnapshot) return;
        else if (singleBullet) collateral = new CollateralLineage();

        _shots.Add(shot, new Entry
        {
            Facts = new ArenaShotAwardFacts(checked(++_nextToken), _epoch, shooterBodyId,
                airborneHipFire, boltActionHipFire),
            WeaponId = weaponId,
            FireIndex = fireIndex,
            WorldId = worldId, GameId = gameId,
            Collateral = collateral
        });
    }

    internal static ArenaShotAwardFacts ForImpact(object shot, long shooterBodyId,
        string weaponId, int fireIndex, long worldId = 0, long gameId = 0)
    {
        if (shot == null || shooterBodyId == 0 || string.IsNullOrEmpty(weaponId)
            || !_shots.TryGetValue(shot, out var entry)
            || !IsCurrent(entry.Facts)
            || entry.Facts.ShooterBodyId != shooterBodyId
            || entry.WorldId != worldId || entry.GameId != gameId
            || !string.Equals(entry.WeaponId, weaponId, StringComparison.Ordinal)
            || entry.FireIndex != fireIndex) return default;
        return entry.Facts;
    }

    internal static ArenaCollateralProof CaptureCollateral(object shot, ArenaShotAwardFacts facts)
    {
        if (shot == null || !IsCurrent(facts) || !_shots.TryGetValue(shot, out var entry)
            || entry.Facts.ProjectileToken != facts.ProjectileToken
            || entry.Facts.ShooterBodyId != facts.ShooterBodyId
            || entry.Collateral == null) return default;
        return new ArenaCollateralProof(facts, entry.Collateral);
    }

    internal static bool ReportCollateralDeath(ArenaCollateralProof proof, long victimBodyId)
    {
        if (!IsCurrent(proof.Facts) || victimBodyId == 0
            || victimBodyId == proof.Facts.ShooterBodyId || proof.Lineage == null) return false;
        var lineage = proof.Lineage;
        if (lineage.Awarded || lineage.FirstVictim == victimBodyId) return false;
        if (lineage.FirstVictim == 0) { lineage.FirstVictim = victimBodyId; return false; }
        lineage.Awarded = true;
        return true;
    }
}
