using ArenaMode.Bots;

namespace ArenaMode.Tests;

public sealed class BotLoadBoostPolicyTests
{
    private const float Grace = BotLoadBoostPolicy.ReleaseGraceSeconds;
    private const float Cap = BotLoadBoostPolicy.MaxHoldSeconds;

    private static bool Tick(ref BotLoadBoostPolicy.State s, bool loading, float now,
        bool enabled = true, float delay = 1f, int signature = 1, bool overBudget = false)
        => BotLoadBoostPolicy.Wanted(ref s, enabled, loading, now, delay, overBudget, signature);

    [Fact]
    public void NotWantedBeforeTheDelayHasPassed()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Assert.False(Tick(ref s, loading: true, now: 10f));
        Assert.False(Tick(ref s, loading: true, now: 10.5f));
        Assert.False(Tick(ref s, loading: true, now: 10.99f));
    }

    [Fact]
    public void WantedOnceLoadingHasPersistedForTheDelay()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 10f);
        Assert.True(Tick(ref s, loading: true, now: 11f));
        Assert.True(Tick(ref s, loading: true, now: 20f));
    }

    [Fact]
    public void ZeroDelayWantsOnTheFirstLoadingTick()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Assert.True(Tick(ref s, loading: true, now: 10f, delay: 0f));
    }

    [Fact]
    public void NegativeDelayBehavesAsZero()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Assert.True(Tick(ref s, loading: true, now: 5f, delay: -3f));
    }

    [Fact]
    public void HeldThroughTheGraceAndReleasedAfterIt()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 10f);
        Assert.True(Tick(ref s, loading: true, now: 12f));

        // Loading stops: the hold survives the grace, not a moment longer.
        Assert.True(Tick(ref s, loading: false, now: 12.1f));
        Assert.True(Tick(ref s, loading: false, now: 12.1f + Grace * 0.9f));
        Assert.False(Tick(ref s, loading: false, now: 12.2f + Grace));
        Assert.False(Tick(ref s, loading: false, now: 30f));
    }

    [Fact]
    public void AGapInsideTheGraceIsNotANewEpisode()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 10f);
        Assert.True(Tick(ref s, loading: true, now: 12f));

        // The one-frame gap between two kits of the warm-up.
        Assert.True(Tick(ref s, loading: false, now: 12.01f));
        Assert.True(Tick(ref s, loading: true, now: 12.02f));
        Assert.True(Tick(ref s, loading: true, now: 40f));
    }

    [Fact]
    public void AGapInsideTheGraceBeforeTheDelayKeepsTheClock()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Assert.False(Tick(ref s, loading: true, now: 10f));
        Assert.False(Tick(ref s, loading: false, now: 10.4f));
        Assert.False(Tick(ref s, loading: true, now: 10.6f));

        // Counted from 10.0, not from 10.6.
        Assert.True(Tick(ref s, loading: true, now: 11f));
    }

    [Fact]
    public void AGapLongerThanTheGraceStartsTheDelayAgain()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 10f);
        Assert.True(Tick(ref s, loading: true, now: 12f));
        Tick(ref s, loading: false, now: 12.1f);
        Assert.False(Tick(ref s, loading: false, now: 13f));

        Assert.False(Tick(ref s, loading: true, now: 13.1f));
        Assert.False(Tick(ref s, loading: true, now: 14f));
        Assert.True(Tick(ref s, loading: true, now: 14.1f));
    }

    [Fact]
    public void FlappingWithLongGapsNeverWants()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        for (int i = 0; i < 50; i++)
        {
            Assert.False(Tick(ref s, loading: true, now: i * 2f));
            Assert.False(Tick(ref s, loading: false, now: i * 2f + 0.5f));
            Assert.False(Tick(ref s, loading: false, now: i * 2f + 1.5f));
        }
    }

    [Fact]
    public void FlappingWithShortGapsIsOneEpisode()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Assert.False(Tick(ref s, loading: true, now: 0f));
        Assert.False(Tick(ref s, loading: false, now: 0.3f));
        Assert.False(Tick(ref s, loading: true, now: 0.6f));
        Assert.False(Tick(ref s, loading: false, now: 0.9f));
        Assert.True(Tick(ref s, loading: true, now: 1.2f));
    }

    [Fact]
    public void DisablingReleasesAtOnceAndForgetsTheEpisode()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 10f);
        Assert.True(Tick(ref s, loading: true, now: 12f));
        Assert.False(Tick(ref s, loading: true, now: 12.1f, enabled: false));

        // Re-enabled under continuous loading: the delay applies again rather
        // than resuming from the old episode.
        Assert.False(Tick(ref s, loading: true, now: 12.2f));
        Assert.True(Tick(ref s, loading: true, now: 13.2f));
    }

    [Fact]
    public void AnUnmovingCounterTripsAfterTheCap()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f);
        Assert.True(Tick(ref s, loading: true, now: 1f));
        Assert.True(Tick(ref s, loading: true, now: 1f + Cap));

        Assert.False(Tick(ref s, loading: true, now: 1.1f + Cap));
        Assert.True(s.Tripped);

        // The stuck counter, an hour later: still off.
        Assert.False(Tick(ref s, loading: true, now: 3600f));
    }

    [Fact]
    public void ATrippedEpisodeRecoversWhenTheCounterMoves()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f);
        Tick(ref s, loading: true, now: 1f);
        Tick(ref s, loading: true, now: 1.1f + Cap);
        Assert.True(s.Tripped);

        // A stuck 1 with a real load on top reads 2: not stuck after all, and
        // the delay is long past, so the boost is wanted at once.
        Assert.True(Tick(ref s, loading: true, now: 200f, signature: 2));
        Assert.False(s.Tripped);

        // Back to the stuck value: the cap runs from the moment the signature
        // last moved, then it trips again.
        Assert.True(Tick(ref s, loading: true, now: 210f, signature: 1));
        Assert.True(Tick(ref s, loading: true, now: 210f + Cap, signature: 1));
        Assert.False(Tick(ref s, loading: true, now: 210.1f + Cap, signature: 1));
        Assert.True(s.Tripped);
    }

    [Fact]
    public void ATrippedEpisodeRecoversOnIdle()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f);
        Tick(ref s, loading: true, now: 1f);
        Tick(ref s, loading: true, now: 1.1f + Cap);
        Assert.True(s.Tripped);

        Assert.False(Tick(ref s, loading: false, now: 3602f));
        Assert.False(s.Tripped);

        // After a real break the delay applies afresh.
        Assert.False(Tick(ref s, loading: true, now: 3603f));
        Assert.True(Tick(ref s, loading: true, now: 3604f));
    }

    [Fact]
    public void DisablingWhileTrippedDoesNotClearTheTrip()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f);
        Tick(ref s, loading: true, now: 1f);
        Tick(ref s, loading: true, now: 2f + Cap);
        Assert.True(s.Tripped);

        // Toggling the setting is not evidence the counter recovered.
        Tick(ref s, loading: true, now: 100f + Cap, enabled: false);
        Assert.True(s.Tripped);
        Assert.False(Tick(ref s, loading: true, now: 200f + Cap));
    }

    [Fact]
    public void AMovingCounterNeverTrips()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f);

        // Loads come and go for ten minutes; the counter changes every few
        // seconds and the boost stays up throughout.
        for (int i = 1; i < 600; i++)
            Assert.True(Tick(ref s, loading: true, now: i, signature: 1 + (i / 3) % 3));

        Assert.False(s.Tripped);
    }

    [Theory]
    [InlineData(0d, 80000L, false)]      // no sample yet: unknown, treated as under
    [InlineData(160000d, 0L, false)]     // no budget set: unknown
    [InlineData(160000d, -1L, false)]
    [InlineData(79999d, 80000L, false)]  // a hair under
    [InlineData(80000d, 80000L, true)]   // exactly the budget is over, as HasFrameTime reads it
    [InlineData(120000d, 80000L, true)]  // a 60 fps hold under the 120 limit, minus render: well over
    public void OverBudgetMirrorsTheSchedulersOwnComparison(double windowTicks, long frameTicks, bool expected)
        => Assert.Equal(expected, BotLoadBoostPolicy.IsOverBudget(windowTicks, frameTicks));

    [Fact]
    public void OverBudgetShortensTheDelayAndNeverLengthensIt()
    {
        Assert.Equal(1f, BotLoadBoostPolicy.DelayFor(overBudget: false, configuredDelaySeconds: 1f));
        Assert.Equal(BotLoadBoostPolicy.OverBudgetDelaySeconds, BotLoadBoostPolicy.DelayFor(overBudget: true, configuredDelaySeconds: 1f));

        // A user who asked for less than the short delay gets less.
        Assert.Equal(0f, BotLoadBoostPolicy.DelayFor(overBudget: true, configuredDelaySeconds: 0f));
        Assert.Equal(0.1f, BotLoadBoostPolicy.DelayFor(overBudget: true, configuredDelaySeconds: 0.1f));

        // Negative is zero, both ways.
        Assert.Equal(0f, BotLoadBoostPolicy.DelayFor(overBudget: false, configuredDelaySeconds: -2f));
        Assert.Equal(0f, BotLoadBoostPolicy.DelayFor(overBudget: true, configuredDelaySeconds: -2f));
    }

    [Fact]
    public void OverBudgetEpisodeUsesTheShortDelay()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Assert.False(Tick(ref s, loading: true, now: 10f, overBudget: true));
        Assert.False(Tick(ref s, loading: true, now: 10.2f, overBudget: true));
        Assert.True(Tick(ref s, loading: true, now: 10.3f, overBudget: true));
    }

    [Fact]
    public void TheVerdictIsLatchedForTheEpisode()
    {
        // Over at the start: the short delay admits the hold, and a flip to
        // under a moment later must not grow the delay under the running hold.
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f, overBudget: true);
        Assert.True(Tick(ref s, loading: true, now: 0.3f, overBudget: true));
        Assert.True(Tick(ref s, loading: true, now: 0.4f, overBudget: false));
        Assert.True(Tick(ref s, loading: true, now: 0.9f, overBudget: false));

        // Under at the start: a flip to over mid-wait must not shorten it.
        var u = BotLoadBoostPolicy.State.Idle;
        Tick(ref u, loading: true, now: 0f, overBudget: false);
        Assert.False(Tick(ref u, loading: true, now: 0.3f, overBudget: true));
        Assert.False(Tick(ref u, loading: true, now: 0.9f, overBudget: true));
        Assert.True(Tick(ref u, loading: true, now: 1f, overBudget: true));
    }

    [Fact]
    public void ANewEpisodeReadsTheVerdictAgain()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f, overBudget: false);
        Assert.True(Tick(ref s, loading: true, now: 1f, overBudget: false));
        Tick(ref s, loading: false, now: 1.1f, overBudget: true);
        Assert.False(Tick(ref s, loading: false, now: 2f, overBudget: true));

        // The next episode starts over budget and gets the short delay.
        Assert.False(Tick(ref s, loading: true, now: 3f, overBudget: true));
        Assert.True(Tick(ref s, loading: true, now: 3.3f, overBudget: true));
    }

    [Fact]
    public void AGapInsideTheGraceKeepsTheLatchedVerdict()
    {
        var s = BotLoadBoostPolicy.State.Idle;
        Tick(ref s, loading: true, now: 0f, overBudget: true);
        Tick(ref s, loading: false, now: 0.1f, overBudget: false);
        Assert.False(Tick(ref s, loading: true, now: 0.2f, overBudget: false));
        Assert.True(Tick(ref s, loading: true, now: 0.3f, overBudget: false));
    }
}
