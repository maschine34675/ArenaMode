using ArenaMode.Combat;

namespace ArenaMode.Tests;

public class ArenaCombatAwardTests
{
    private static ArenaKill Kill(long killer = 1, long victim = 2, bool local = true,
        bool death = false, bool eligible = true, bool head = false) => new()
    {
        KillerBodyId = killer, VictimBodyId = victim, KilledByLocal = local,
        LocalIsVictim = death, AwardEligible = eligible, SingleHitHeadshot = head
    };

    [Fact]
    public void FirstBloodBelongsToFirstValidKillNotFirstPersonalKill()
    {
        var awards = new ArenaCombatAwards();
        Assert.Null(awards.ReportKill(Kill(eligible: false), null));
        Assert.Equal("FIRST BLOOD", awards.ReportKill(Kill(head: true), "DOUBLE KILL"));
        Assert.Equal("HEADSHOT", awards.ReportKill(Kill(victim: 3, head: true), null));
        awards.BeginRound();
        Assert.Null(awards.ReportKill(Kill(local: false), null));
        Assert.Null(awards.ReportKill(Kill(victim: 3), null));
    }

    [Fact]
    public void RespawnDoesNotResetFirstBloodOrInventPayback()
    {
        var awards = new ArenaCombatAwards();
        Assert.Null(awards.ReportKill(Kill(killer: 2, victim: 1, local: false, death: true), null));
        awards.LocalDeath();
        Assert.Equal("PAYBACK", awards.ReportKill(Kill(killer: 3, victim: 2), "KILLING SPREE"));
        Assert.Null(awards.ReportKill(Kill(killer: 3, victim: 4), null));
    }

    [Theory]
    [InlineData("Fall")]
    [InlineData("Dehydration")]
    [InlineData("Undefined")]
    [InlineData("LightBleeding")]
    [InlineData("HeavyBleeding")]
    [InlineData("Exhaustion")]
    [InlineData("Environment")]
    public void StaleAggressorOnNoncombatDeathCannotNominateRevenge(string lethalType)
    {
        var awards = new ArenaCombatAwards();
        var death = Kill(killer: 9, victim: 1, local: false, death: true,
            eligible: ArenaCombatAwards.IsCombatDamage(lethalType));
        Assert.Null(awards.ReportKill(death, null));
        awards.LocalDeath();
        Assert.Equal("FIRST BLOOD", awards.ReportKill(Kill(victim: 10), null));
        Assert.Null(awards.ReportKill(Kill(victim: 9), null));
    }

    [Theory]
    [InlineData("Bullet")]
    [InlineData("Blunt")]
    [InlineData("Melee")]
    [InlineData("Explosion")]
    [InlineData("GrenadeFragment")]
    public void DirectCombatDeathCanEarnPaybackAcrossOrdinaryScoringRound(string lethalType)
    {
        var awards = new ArenaCombatAwards();
        awards.ReportKill(Kill(killer: 9, victim: 1, local: false, death: true,
            eligible: ArenaCombatAwards.IsCombatDamage(lethalType)), null);
        awards.LocalDeath();
        awards.BeginRound(clearRevenge: false);
        awards.ReportKill(Kill(killer: 11, victim: 12, local: false), null);
        Assert.Equal("PAYBACK", awards.ReportKill(Kill(victim: 9), null));
    }

    [Fact]
    public void UnknownWoundedBodyCannotEarnOneHitAwardEvenIfLaterHealed()
    {
        var body = new object(); var shooter = new object();
        ArenaHitHistory.InitializeBody(body, false);
        ArenaHitHistory.InitializeBody(body, true);
        using (ArenaHitHistory.BeginShot(body, shooter, true, true))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
            Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
    }

    [Fact]
    public void AnotherKillerOrEnvironmentalDeathClearsRevenge()
    {
        var awards = new ArenaCombatAwards();
        awards.ReportKill(Kill(killer: 2, victim: 1, local: false, death: true), null);
        awards.LocalDeath();
        awards.ReportKill(Kill(killer: 4, victim: 2, local: false), null);
        Assert.Null(awards.ReportKill(Kill(victim: 2), null));
        awards.ReportKill(Kill(killer: 5, local: false, death: true), null);
        awards.LocalDeath();
        awards.LocalDeath(); // no captured valid aggressor on this later death
        Assert.Null(awards.ReportKill(Kill(victim: 5), null));
    }

    [Fact]
    public void ReplacementBodyAndFriendlyDeathsDoNotEarnRevenge()
    {
        var awards = new ArenaCombatAwards();
        awards.ReportKill(Kill(killer: 2, local: false, death: true), null);
        awards.LocalDeath();
        Assert.Null(awards.ReportKill(Kill(victim: 22), null));
        awards.ReportKill(Kill(killer: 5, local: false, death: true, eligible: false), null);
        awards.LocalDeath();
        Assert.Null(awards.ReportKill(Kill(victim: 5), null));
        awards.BeginRound();
        Assert.Equal("FIRST BLOOD", awards.ReportKill(Kill(victim: 2), null));
    }

    [Fact]
    public void ExistingMilestonesAndLastManHaveSinglePrioritizedPresentation()
    {
        var awards = new ArenaCombatAwards();
        awards.ReportKill(Kill(local: false), null);
        Assert.Equal("UNSTOPPABLE", awards.ReportKill(Kill(head: true), ScoreRules.StreakLabel(20)));
        Assert.False(awards.LastManStanding(false, 1, 3));
        Assert.False(awards.LastManStanding(true, 2, 3));
        Assert.False(awards.LastManStanding(true, 1, 0));
        Assert.True(awards.LastManStanding(true, 1, 3));
        Assert.False(awards.LastManStanding(true, 1, 2));
        awards.BeginRound();
        Assert.True(awards.LastManStanding(true, 1, 1));
    }

    [Fact]
    public void OneAcceptedHeadContactIsDetectedInsideNestedNativeDeath()
    {
        var body = new object(); var shooter = new object();
        using (ArenaHitHistory.BeginShot(body, shooter, true, true))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
        {
            ArenaHitHistory.ObserveHealthDamage(body, shooter, true, 35);
            Assert.True(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
            Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, false, true));
            Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, false));
            Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, new object(), true, true));
        }
        Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PriorContactIncludingArmorOrAnotherShooterDisqualifies(bool anotherShooter)
    {
        var body = new object(); var shooter = new object();
        using (ArenaHitHistory.BeginDamage(body, anotherShooter ? new object() : shooter, true, false)) { }
        using (ArenaHitHistory.BeginShot(body, shooter, true, true))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
            Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
    }

    [Fact]
    public void EarlierEnvironmentDamageAndMultiplePelletContactsDisqualify()
    {
        var body = new object(); var shooter = new object();
        ArenaHitHistory.ObserveHealthDamage(body, null, false, 1);
        using (ArenaHitHistory.BeginShot(body, shooter, true, true))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
            Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
        body = new object();
        using (ArenaHitHistory.BeginShot(body, shooter, true, false))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, false)) { }
        using (ArenaHitHistory.BeginShot(body, shooter, true, true))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
            Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
    }

    [Fact]
    public void RejectedShotsDoNotCountAndExceptionScopeCannotSurvive()
    {
        var body = new object(); var shooter = new object();
        using (ArenaHitHistory.BeginShot(body, shooter, true, true)) { } // native returns null
        using (ArenaHitHistory.BeginShot(body, shooter, true, true))
        using (ArenaHitHistory.BeginDamage(body, shooter, true, true))
            Assert.True(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
        Assert.False(ArenaHitHistory.IsSingleHitHeadshot(body, shooter, true, true));
        Assert.True(ArenaHitHistory.TryReportDeath(body));
        Assert.False(ArenaHitHistory.TryReportDeath(body));
        Assert.NotEqual(ArenaHitHistory.BodyId(body), ArenaHitHistory.BodyId(new object()));
    }
}
