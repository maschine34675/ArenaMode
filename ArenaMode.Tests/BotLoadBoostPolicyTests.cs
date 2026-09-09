using ArenaMode.Bots;

namespace ArenaMode.Tests;

public sealed class BotLoadBoostPolicyTests
{
    private const float Grace = BotLoadBoostPolicy.ReleaseGraceSeconds;
    private const float Cap = BotLoadBoostPolicy.MaxHoldSeconds;

    private static bool Tick(ref BotLoadBoostPolicy.State s, bool loading, float now,
        bool enabled = true, float delay = 1f, int signature = 1)
        => BotLoadBoostPolicy.Wanted(ref s, enabled, loading, now, delay, signature);

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
}
