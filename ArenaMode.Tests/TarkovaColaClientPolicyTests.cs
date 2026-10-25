using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

public sealed class TarkovaColaClientPolicyTests
{
    private static TarkovaColaClientLease NewLease() => new("profile", "factory4_day", "zombie", 7);

    [Theory]
    [InlineData("2.2.0", false)]
    [InlineData("2.2.1", true)]
    [InlineData("2.3.0", true)]
    [InlineData("3.0.0", true)]
    public void MinimumVersionHasNoUpperGate(string version, bool admitted)
        => Assert.Equal(admitted, TarkovaColaPolicy.SupportedVersion(Version.Parse(version)));

    [Fact]
    public void UnknownVersionDoesNotGrantAdmission() => Assert.False(TarkovaColaPolicy.SupportedVersion(null));

    [Theory]
    [InlineData(null, false)]
    [InlineData("zombie", true)]
    [InlineData("tactical", false)]
    [InlineData("deathmatch", false)]
    [InlineData("gungame", false)]
    [InlineData("teamdeathmatch", false)]
    public void ForeignRewardsAreZombieOnly(string? mode, bool admitted)
        => Assert.Equal(admitted, TarkovaColaPolicy.RewardsAllowed(mode!));

    [Fact]
    public void AcceptedArmBlocksWritesBeforeAnyWorldExists()
    {
        var lease = NewLease();
        Assert.True(lease.BlocksWrites);
        Assert.False(lease.CanObserve(new object()));
        Assert.False(lease.Confirmed);
        Assert.False(lease.Finish());
    }

    [Theory]
    [InlineData("wrong-profile", "factory4_day", 7)]
    [InlineData("profile", "factory4_night", 7)]
    [InlineData("profile", "factory4_day", 8)]
    public void BindRequiresTheExactAcceptedArm(string profile, string map, int epoch)
    {
        var lease = NewLease();
        Assert.False(lease.BindWorld(new object(), new object(), profile, map, epoch));
        Assert.Null(lease.World);
        Assert.True(lease.BlocksWrites);
    }

    [Fact]
    public void SameProfileInAnotherWorldCannotReuseTheLease()
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        Assert.True(lease.BindWorld(world, body, "profile", "factory4_day", 7));
        Assert.True(lease.BindWorld(world, body, "profile", "factory4_day", 7));
        Assert.False(lease.BindWorld(new object(), body, "profile", "factory4_day", 7));
        Assert.False(lease.BindWorld(world, new object(), "profile", "factory4_day", 7));
        Assert.False(lease.Confirm(new object(), "profile", "zombie", 7));
        Assert.False(lease.Confirm(world, "profile", "deathmatch", 7));
        Assert.False(lease.Confirm(world, "another-profile", "zombie", 7));
        Assert.True(lease.Confirm(world, "profile", "zombie", 7));
        Assert.True(lease.CanApply(world, body));
    }

    [Fact]
    public void ATemporaryMissingBodyNeverReleasesWriteProtection()
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        Assert.True(lease.BindWorld(world, body, "profile", "factory4_day", 7));
        Assert.True(lease.Confirm(world, "profile", "zombie", 7));
        Assert.False(lease.CanApply(world, null!));
        Assert.False(lease.CanObserve(null!));
        Assert.True(lease.BlocksWrites);
        Assert.Same(body, lease.Body);
    }

    [Fact]
    public void ReplacementInvalidatesOldDelayedActionsWithoutChangingTheMatch()
    {
        var lease = NewLease(); var world = new object(); var old = new object(); var current = new object();
        Assert.True(lease.BindWorld(world, old, "profile", "factory4_day", 7));
        Assert.True(lease.Confirm(world, "profile", "zombie", 7));
        Assert.True(lease.BeforeReplacementStatistics(world, old, current, 0));
        Assert.True(lease.Rebind(world, old, current));
        Assert.False(lease.CanApply(world, old));
        Assert.True(lease.CanApply(world, current));
        Assert.False(lease.Rebind(world, old, new object()));
        Assert.False(lease.Rebind(new object(), current, new object()));
        Assert.Equal(7, lease.Epoch);
        Assert.True(lease.BlocksWrites);
    }

    [Fact]
    public void ResultCaptureIsOneTimeAndKeepsWritesHeldThroughNativeEnd()
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        Assert.False(lease.Finish());
        Assert.True(lease.BeginFinal(world));
        Assert.True(lease.ObserveStatisticsEnd(world, body));
        Assert.True(lease.Finish());
        Assert.False(lease.Finish());
        Assert.False(lease.CanApply(world, body));
        Assert.False(lease.Rebind(world, body, new object()));
        Assert.True(lease.BlocksWrites);
        lease.Release();
        Assert.False(lease.BlocksWrites);
        Assert.False(lease.CanApply(world, body));
    }

    [Fact]
    public void FailedCleanupKeepsWritesBlockedAndDoesNotProduceRewards()
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        lease.Fail();
        Assert.True(lease.BlocksWrites);
        Assert.False(lease.CanApply(world, body));
        Assert.False(lease.Finish());
        Assert.False(lease.BindWorld(new object(), new object(), "profile", "factory4_day", 7));
    }

    [Fact]
    public void NativeStatisticsBeforeRealGameEndCannotAuthorizeResearch()
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        Assert.False(lease.ObserveStatisticsEnd(world, body));
        Assert.False(lease.BeginFinal(new object()));
        Assert.False(lease.StatisticsCompleted);
        Assert.False(lease.Finish());
    }

    [Fact]
    public void FinalResearchWaitsForItsCurrentPhysicalPlayersNativeStatistics()
    {
        var lease = NewLease(); var world = new object(); var oldBody = new object(); var body = new object();
        lease.BindWorld(world, oldBody, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        Assert.True(lease.BeforeReplacementStatistics(world, oldBody, body, 0));
        Assert.True(lease.Rebind(world, oldBody, body));
        Assert.True(lease.BeginFinal(world));
        Assert.False(lease.ObserveStatisticsEnd(world, oldBody));
        Assert.False(lease.ObserveStatisticsEnd(new object(), body));
        Assert.False(lease.Finish());
        Assert.False(lease.Rebind(world, body, new object()));
        Assert.True(lease.ObserveStatisticsEnd(world, body));
        Assert.True(lease.Finish());
        Assert.False(lease.ObserveStatisticsEnd(world, body));
        Assert.True(lease.BlocksWrites);
    }

    [Fact]
    public void ResearchExperienceSurvivesTheSharedProfileResetExactlyOncePerBody()
    {
        var lease = NewLease(); var world = new object(); var body = new object(); var next = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        Assert.True(lease.ObserveExperience(world, body, 300));
        Assert.True(lease.ObserveExperience(world, body, 300));
        Assert.True(lease.ObserveExperience(world, body, 0)); // native shared stats reset before Replaced
        Assert.Equal(300, lease.EarnedExperience);
        Assert.False(lease.ObserveExperience(new object(), body, 900));
        Assert.False(lease.ObserveExperience(world, next, 900));
        Assert.True(lease.BeforeReplacementStatistics(world, body, next, 300));
        Assert.True(lease.Rebind(world, body, next));
        Assert.False(lease.Rebind(world, body, next));
        Assert.False(lease.ObserveExperience(world, body, 900));
        Assert.True(lease.ObserveExperience(world, next, 100));
        Assert.Equal(400, lease.EarnedExperience);
        Assert.True(lease.ObserveExperience(world, next, 150));
        Assert.Equal(450, lease.EarnedExperience);
    }

    [Fact]
    public void NoReplacementCannotDoubleCountADeathObservation()
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        lease.ObserveExperience(world, body, 200);
        lease.ObserveExperience(world, body, 200); // death claim followed by failed replacement
        lease.ObserveExperience(world, body, 250);
        Assert.Equal(250, lease.EarnedExperience);
        Assert.True(lease.BeginFinal(world));
        Assert.True(lease.ObserveStatisticsEnd(world, body));
        Assert.True(lease.Finish());
        Assert.False(lease.ObserveExperience(world, body, 500));
        Assert.Equal(250, lease.EarnedExperience);
    }

    [Fact]
    public void LivingReplacementFreezesExperienceBeforeSharedNativeCountersAreCleared()
    {
        var lease = NewLease(); var world = new object(); var body = new object(); var next = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        lease.ObserveExperience(world, body, 100);
        Assert.True(lease.BeforeReplacementStatistics(world, body, next, 350));
        Assert.True(lease.ReplacementStatisticsPending);
        Assert.False(lease.ObserveExperience(world, body, 0));
        Assert.False(lease.BeginFinal(world));
        Assert.False(lease.Rebind(world, body, new object()));
        Assert.True(lease.Rebind(world, body, next));
        Assert.False(lease.ReplacementStatisticsPending);
        lease.ObserveExperience(world, next, 100);
        Assert.Equal(450, lease.EarnedExperience);
        Assert.True(lease.BeginFinal(world));
        Assert.False(lease.ObserveExperience(world, next, 900)); // native exit bonus is not research
        Assert.Equal(450, lease.EarnedExperience);
    }

    [Fact]
    public void FailedConstructionAfterAStatisticsResetCannotPublishResearch()
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        lease.BeforeReplacementStatistics(world, body, new object(), 350);
        lease.Fail();
        Assert.Equal(350, lease.EarnedExperience);
        Assert.False(lease.BeginFinal(world));
        Assert.False(lease.Finish());
        Assert.True(lease.BlocksWrites);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(2147483648L)]
    public void NativeExperienceMustFitTheBoundedUpstreamInput(long experience)
    {
        var lease = NewLease(); var world = new object(); var body = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        Assert.Throws<InvalidOperationException>(() => lease.ObserveExperience(world, body, experience));
    }

    [Fact]
    public void ReplacementRequiresADistinctBodyWithItsObservedNativeReset()
    {
        var lease = NewLease(); var world = new object(); var body = new object(); var next = new object();
        lease.BindWorld(world, body, "profile", "factory4_day", 7);
        lease.Confirm(world, "profile", "zombie", 7);
        Assert.False(lease.Rebind(world, body, body));
        Assert.False(lease.Rebind(world, body, next));
        Assert.False(lease.BeforeReplacementStatistics(world, body, body, 100));
        Assert.True(lease.BeforeReplacementStatistics(world, body, next, int.MaxValue));
        Assert.True(lease.Rebind(world, body, next));
        Assert.Throws<OverflowException>(() => lease.ObserveExperience(world, next, 1));
    }
}
