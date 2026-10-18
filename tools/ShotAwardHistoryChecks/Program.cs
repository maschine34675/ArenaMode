using System.Runtime.CompilerServices;
using ArenaMode.Combat;

internal static class Program
{
    private static int _checks;
    private const string Weapon = "physical-weapon-id";
    private const int Fire = 31;

    private static void Check(bool pass, string name)
    {
        _checks++;
        if (!pass) throw new InvalidOperationException("ASSERT: " + name);
    }

    private static void Reset()
    {
        ArenaShotAwardHistory.Reset();
        ArenaHitHistory.Reset();
    }

    private static ArenaShotAwardFacts Record(object shot, object shooter, bool air = true, bool bolt = true,
        bool snapshot = true, object parent = null, string weapon = Weapon, int fire = Fire)
    {
        ArenaShotAwardHistory.RecordCreated(shot, parent, ArenaHitHistory.BodyId(shooter), weapon, fire,
            snapshot, air, bolt);
        return ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), weapon, fire);
    }

    private static void IdentityAndPool()
    {
        Reset();
        var shot = new object(); var shooter = new object();
        Check(!default(ArenaShotAwardFacts).IsKnown, "default is unknown");
        Check(!ArenaShotAwardHistory.IsCurrent(default), "default is not current");
        Check(!Record(shot, shooter, snapshot: false).IsKnown, "unknown root does not acquire facts");
        var first = Record(shot, shooter);
        Check(first.IsKnown && first.AirborneHipFire && first.BoltActionHipFire, "firing snapshot stored");
        Check(ArenaShotAwardHistory.IsCurrent(first), "known facts current");
        Check(first.ProjectileToken != 0, "nonzero projectile token");
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), "other-weapon", Fire).IsKnown,
            "weapon identity mismatch rejected");
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), Weapon.ToUpperInvariant(), Fire).IsKnown,
            "weapon identity is ordinal");
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(new object()), Weapon, Fire).IsKnown,
            "foreign physical shooter rejected");
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), Weapon, Fire + 1).IsKnown,
            "fire generation mismatch rejected");
        Check(!ArenaShotAwardHistory.ForImpact(null, ArenaHitHistory.BodyId(shooter), Weapon, Fire).IsKnown, "null projectile rejected");
        Check(!ArenaShotAwardHistory.ForImpact(new object(), ArenaHitHistory.BodyId(shooter), Weapon, Fire).IsKnown, "unregistered projectile rejected");
        Check(!ArenaShotAwardHistory.ForImpact(shot, 0, Weapon, Fire).IsKnown, "unknown shooter rejected");
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), null, Fire).IsKnown, "unknown weapon rejected");

        // A native pooled Shot is also recycled for non-arena/unknown creates.
        ArenaShotAwardHistory.RecordCreated(shot, null, ArenaHitHistory.BodyId(shooter), Weapon, Fire, false, false, false);
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), Weapon, Fire).IsKnown,
            "unknown pooled create removes previous shot facts");
        var allTokens = new HashSet<long> { first.ProjectileToken };
        for (int i = 0; i < 16; i++)
        {
            bool air = i % 2 == 0, bolt = i % 3 == 0;
            var next = Record(shot, shooter, air, bolt);
            Check(next.IsKnown && next.AirborneHipFire == air && next.BoltActionHipFire == bolt,
                "pooled create replaces firing facts " + i);
            Check(allTokens.Add(next.ProjectileToken), "pooled projectile token is unique " + i);
        }
        var grounded = Record(new object(), shooter, false, false);
        Check(grounded.IsKnown && !grounded.AirborneHipFire && !grounded.BoltActionHipFire,
            "known grounded aimed shot does not become unknown");
        ArenaShotAwardHistory.RecordCreated(shot, null, 0, Weapon, Fire, true, true, true);
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), Weapon, Fire).IsKnown,
            "unknown-shooter create clears pooled facts");
        Record(shot, shooter);
        ArenaShotAwardHistory.RecordCreated(shot, null, ArenaHitHistory.BodyId(shooter), "", Fire, true, true, true);
        Check(!ArenaShotAwardHistory.ForImpact(shot, ArenaHitHistory.BodyId(shooter), Weapon, Fire).IsKnown,
            "unknown-weapon create clears pooled facts");
        ArenaShotAwardHistory.RecordCreated(null, null, 0, null, 0, false, false, false);
    }

    private static void ParentAndEpoch()
    {
        Reset();
        var shooter = new object(); var root = new object(); var child = new object(); var grandchild = new object();
        var first = Record(root, shooter, true, false);
        var fragment = Record(child, shooter, false, true, snapshot: false, parent: root);
        Check(fragment.IsKnown && fragment.AirborneHipFire && !fragment.BoltActionHipFire,
            "child copies frozen parent facts rather than ambient booleans");
        Check(fragment.ProjectileToken != first.ProjectileToken, "fragment has distinct projectile identity");
        var ricochet = Record(grandchild, shooter, false, true, snapshot: true, parent: child);
        Check(ricochet.IsKnown && ricochet.AirborneHipFire && !ricochet.BoltActionHipFire,
            "recursive descendants copy current parent snapshot");
        Check(ricochet.ProjectileToken != fragment.ProjectileToken, "recursive descendant token is distinct");
        Record(root, shooter, false, true);
        var afterRecycle = ArenaShotAwardHistory.ForImpact(child, ArenaHitHistory.BodyId(shooter), Weapon, Fire);
        Check(afterRecycle.ProjectileToken == fragment.ProjectileToken && afterRecycle.AirborneHipFire
            && !afterRecycle.BoltActionHipFire, "child snapshot survives recycled parent");
        ArenaShotAwardHistory.RecordCreated(child, null, ArenaHitHistory.BodyId(shooter), Weapon, Fire, false, false, false);
        var retainedGrandchild = ArenaShotAwardHistory.ForImpact(grandchild, ArenaHitHistory.BodyId(shooter), Weapon, Fire);
        Check(retainedGrandchild.IsKnown && retainedGrandchild.AirborneHipFire,
            "descendant does not walk a now-invalid parent at impact");

        Check(!Record(new object(), shooter, parent: new object()).IsKnown, "unknown parent cannot borrow ambient fire snapshot");
        Check(!Record(new object(), new object(), parent: root).IsKnown, "parent shooter must match");
        Check(!Record(new object(), shooter, parent: root, weapon: "other").IsKnown, "parent weapon must match");
        Check(!Record(new object(), shooter, parent: root, fire: Fire + 1).IsKnown, "parent fire index must match");
        Check(!Record(root, shooter, parent: root).IsKnown, "self-parent create cannot reuse stale pooled facts");

        var staleShot = new object(); var stale = Record(staleShot, shooter);
        long oldEpoch = ArenaShotAwardHistory.CurrentEpoch;
        ArenaShotAwardHistory.Reset();
        Check(!ArenaShotAwardHistory.IsCurrent(stale), "new round invalidates copied old facts");
        Check(ArenaShotAwardHistory.CurrentEpoch != oldEpoch, "new round invalidates native firing-scope epoch");
        Check(!ArenaShotAwardHistory.ForImpact(staleShot, ArenaHitHistory.BodyId(shooter), Weapon, Fire).IsKnown,
            "new round invalidates registered projectiles");
        Check(!Record(new object(), shooter, parent: staleShot).IsKnown, "new round rejects old parent even inside new fire scope");
        var current = Record(staleShot, shooter);
        Check(current.IsKnown && ArenaShotAwardHistory.IsCurrent(current), "new round admits a new Create on same pooled instance");
        Check(current.ProjectileToken != stale.ProjectileToken && current.Epoch != stale.Epoch,
            "reset does not reuse token or epoch");
        Check(!ArenaShotAwardHistory.IsCurrent(stale), "reusing old instance does not revive old copied facts");
    }

    private static void Contacts()
    {
        Reset();
        var body = new object(); var shooter = new object();
        var facts = Record(new object(), shooter);
        Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "no active contact rejected");
        using (ArenaHitHistory.BeginShot(body, shooter, true, true, facts))
        {
            Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "unaccepted shot rejected");
            using (ArenaHitHistory.BeginDamage(body, shooter, true, true, facts))
            {
                var award = ArenaHitHistory.ShotAwards(body, shooter, true);
                Check(award.IsKnown && award.AirborneHipFire && award.BoltActionHipFire,
                    "accepted direct lethal bullet returns frozen facts");
                Check(award.ProjectileToken == facts.ProjectileToken, "contact returns exact projectile token");
                Check(!ArenaHitHistory.ShotAwards(body, shooter, false).IsKnown, "nonbullet lethal context rejected");
                Check(!ArenaHitHistory.ShotAwards(body, new object(), true).IsKnown, "death shooter must be exact physical shooter");
                Check(!ArenaHitHistory.ShotAwards(null, shooter, true).IsKnown, "missing victim rejected");
                Check(!ArenaHitHistory.ShotAwards(body, null, true).IsKnown, "missing death shooter rejected");
                Check(!ArenaHitHistory.ShotAwards(new object(), shooter, true).IsKnown, "different victim scope rejected");
                ArenaHitHistory.ObserveHealthDamage(body, shooter, true, 50);
                Check(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true), "original accepted one-hit headshot survives facts");
            }
            Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "damage disposal ends accepted context");
        }
        Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "shot disposal ends context");
        using (ArenaHitHistory.BeginDamage(body, shooter, true, false, facts))
            Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "damage without direct shot rejected");

        foreach (var situation in new[] { "missing-shot-facts", "missing-damage-facts", "different-token", "wrong-fact-shooter", "shot-not-bullet", "damage-not-bullet", "wrong-shot-shooter", "wrong-damage-shooter" })
        {
            var other = new object();
            var shotFacts = situation == "missing-shot-facts" ? default : facts;
            var damageFacts = situation == "missing-damage-facts" ? default : facts;
            if (situation == "different-token") damageFacts = Record(new object(), shooter);
            if (situation == "wrong-fact-shooter") shotFacts = damageFacts = Record(new object(), other);
            using (ArenaHitHistory.BeginShot(body, situation == "wrong-shot-shooter" ? other : shooter,
                situation != "shot-not-bullet", false, shotFacts))
            using (ArenaHitHistory.BeginDamage(body, situation == "wrong-damage-shooter" ? other : shooter,
                situation != "damage-not-bullet", false, damageFacts))
                Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "contact rejects " + situation);
        }

        // Trickshot is about the lethal projectile, not whether the victim was
        // pristine; the older Headshot award alone retains that one-hit rule.
        using (ArenaHitHistory.BeginShot(body, shooter, true, false, facts))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, false, facts))
        {
            Check(ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "prior damage does not disqualify lethal shot award");
            Check(!ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true), "headshot still rejects prior damage");
        }
        using (ArenaHitHistory.BeginShot(body, shooter, true, true, facts))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true, facts))
        {
            ArenaShotAwardHistory.Reset();
            Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "active old-round contact facts rejected");
        }
        facts = Record(new object(), shooter);
        ArenaHitHistory.Reset();
        using (ArenaHitHistory.BeginShot(body, shooter, true, true, facts))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true, facts))
            Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "reset physical body identity cannot reuse old shooter facts");
    }

    private static void NestingAndCleanup()
    {
        Reset();
        var shooter = new object(); var body = new object(); var facts = Record(new object(), shooter);
        foreach (bool sameBody in new[] { true, false })
        {
            using (ArenaHitHistory.BeginShot(body, shooter, true, true, facts))
            using (ArenaHitHistory.BeginDamage(body, shooter, true, true, facts))
            {
                var nested = sameBody ? body : new object();
                using (ArenaHitHistory.BeginShot(nested, shooter, true, true, facts))
                {
                    Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "nested shot invalidates outer contact " + sameBody);
                    using (ArenaHitHistory.BeginDamage(nested, shooter, true, true, facts))
                        Check(!ArenaHitHistory.ShotAwards(nested, shooter, true).IsKnown, "nested contact rejected across bodies " + sameBody);
                }
                Check(ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "outer accepted scope restored after nested contact " + sameBody);
                using (ArenaHitHistory.BeginDamage(nested, shooter, true, true, facts))
                    Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "nested damage invalidates outer contact " + sameBody);
            }
        }
        try
        {
            using (ArenaHitHistory.BeginShot(body, shooter, true, true, facts))
            using (ArenaHitHistory.BeginDamage(body, shooter, true, true, facts))
                throw new InvalidOperationException("native exception");
        }
        catch (InvalidOperationException ex) when (ex.Message == "native exception") { }
        Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "exception finalizers remove contact");
        var oldShot = ArenaHitHistory.BeginShot(body, shooter, true, true, facts);
        var oldDamage = ArenaHitHistory.BeginDamage(body, shooter, true, true, facts);
        Reset();
        facts = Record(new object(), shooter);
        using (ArenaHitHistory.BeginShot(body, shooter, true, true, facts))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true, facts))
        {
            oldDamage.Dispose(); oldShot.Dispose(); oldDamage.Dispose();
            Check(ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "old finalizers cannot decrement new epoch depth");
        }
        using (ArenaHitHistory.BeginShot(body, shooter, true, true, facts))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true, facts))
            Check(ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "balanced cleanup admits later direct contact");
    }

    private static void HeadshotCompatibility()
    {
        Reset();
        var shooter = new object(); var body = new object();
        using (ArenaHitHistory.BeginShot(body, shooter, true, true)) { }
        using (ArenaHitHistory.BeginShot(body, shooter, true, true))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
        {
            ArenaHitHistory.ObserveHealthDamage(body, shooter, true, 35);
            Check(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true), "legacy optional-argument call remains a one-hit headshot");
            Check(!ArenaHitHistory.ShotAwards(body, shooter, true).IsKnown, "legacy contacts cannot fabricate new shot facts");
        }
        Check(!ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true), "legacy headshot requires active stack");
        Check(ArenaHitHistory.TryReportDeath(body) && !ArenaHitHistory.TryReportDeath(body), "legacy physical death reported once");
        foreach (var prior in new[] { "wounded", "environment", "armor", "pellet" })
        {
            body = new object();
            if (prior == "wounded") { ArenaHitHistory.InitializeBody(body, false); ArenaHitHistory.InitializeBody(body, true); }
            if (prior == "environment") ArenaHitHistory.ObserveHealthDamage(body, null, false, 1);
            if (prior == "armor" || prior == "pellet")
                using (ArenaHitHistory.BeginDamage(body, shooter, true, false)) { }
            using (ArenaHitHistory.BeginShot(body, shooter, true, true))
            using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
                Check(!ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true), "legacy headshot rejects " + prior);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCollectableProjectile()
    {
        var shot = new object();
        ArenaShotAwardHistory.RecordCreated(shot, null, 5, Weapon, Fire, true, true, true);
        return new WeakReference(shot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCollectableCollateral(out ArenaCollateralProof proof)
    {
        var shot = new object();
        ArenaShotAwardHistory.RecordCreated(shot, null, 5, Weapon, Fire, true, false, false, singleBullet: true);
        proof = ArenaShotAwardHistory.CaptureCollateral(shot, ArenaShotAwardHistory.ForImpact(shot, 5, Weapon, Fire));
        return new WeakReference(shot);
    }

    private static void WeakOwnership()
    {
        Reset();
        var reference = RegisterCollectableProjectile();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!reference.IsAlive, "history does not retain dead projectile keys");
        var collateral = RegisterCollectableCollateral(out var proof);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!collateral.IsAlive, "pending managed collateral proof cannot retain a pooled projectile");
        Check(!ArenaShotAwardHistory.ReportCollateralDeath(proof, 21)
            && ArenaShotAwardHistory.ReportCollateralDeath(proof, 22),
            "native-free scalar proof survives projectile collection for synchronous settlement");
    }

    public static void Main(string[] args)
    {
        string part = args.FirstOrDefault() ?? "all";
        if (part is "all" or "identity") IdentityAndPool();
        if (part is "all" or "parent") ParentAndEpoch();
        if (part is "all" or "contact") Contacts();
        if (part is "all" or "nesting") NestingAndCleanup();
        if (part is "all" or "headshot") HeadshotCompatibility();
        if (part == "all") WeakOwnership();
        Console.WriteLine($"SHOT_AWARD_HISTORY_PRODUCTION_PASSED {_checks} assertions");
    }
}
