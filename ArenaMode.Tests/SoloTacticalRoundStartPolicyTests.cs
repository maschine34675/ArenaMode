using ArenaMode.Modes;
using static ArenaMode.Modes.SoloTacticalRoundStartDecision;

namespace ArenaMode.Tests;

public sealed class SoloTacticalRoundStartPolicyTests
{
    private const float Cap = SoloTacticalRoundStartPolicy.InitialSwapHoldSeconds;

    private static SoloTacticalRoundStartDecision Ask(
        ref SoloTacticalRoundStartPolicy.State state, bool pending, float now)
        => SoloTacticalRoundStartPolicy.Evaluate(ref state, pending, now);

    [Fact]
    public void NoSwapPendingStartsTheRoundAtOnce()
    {
        var s = default(SoloTacticalRoundStartPolicy.State);
        Assert.Equal(Start, Ask(ref s, pending: false, now: 10f));
        Assert.False(s.Holding);
        Assert.False(s.GaveUp);
    }

    [Fact]
    public void APendingSwapHoldsTheRoundAndAnnouncesTheWaitOnce()
    {
        // The bug this guards: a buy phase that ended while the raid-start
        // swap was still preparing started the round regardless, charged the
        // purchase and lost it to the swap landing first.
        var s = default(SoloTacticalRoundStartPolicy.State);
        Assert.Equal(HoldBegun, Ask(ref s, pending: true, now: 10f));
        Assert.True(s.Holding);
        Assert.Equal(Hold, Ask(ref s, pending: true, now: 10.5f));
        Assert.Equal(Hold, Ask(ref s, pending: true, now: 10f + Cap - 0.01f));
        Assert.False(s.GaveUp);
    }

    [Fact]
    public void TheRoundStartsAsSoonAsTheSwapHasSettled()
    {
        var s = default(SoloTacticalRoundStartPolicy.State);
        Ask(ref s, pending: true, now: 10f);
        Ask(ref s, pending: true, now: 12f);

        Assert.Equal(StartAfterHold, Ask(ref s, pending: false, now: 13.5f));
        Assert.False(s.Holding);
        Assert.False(s.GaveUp);
        Assert.Equal(3.5f, s.LastHoldSeconds, 3);

        // A settled swap holds nothing later: the next buy phase starts on its clock.
        Assert.Equal(Start, Ask(ref s, pending: false, now: 100f));
    }

    [Fact]
    public void ASwapThatOutlivesTheCapIsGivenUpOnForTheRestOfTheMatch()
    {
        var s = default(SoloTacticalRoundStartPolicy.State);
        Ask(ref s, pending: true, now: 10f);
        Assert.Equal(Hold, Ask(ref s, pending: true, now: 10f + Cap - 0.001f));

        Assert.Equal(StartAfterGivingUp, Ask(ref s, pending: true, now: 10f + Cap));
        Assert.True(s.GaveUp);
        Assert.False(s.Holding);
        Assert.Equal(Cap, s.LastHoldSeconds, 3);

        // Round two's buy phase with the flag still stuck: no second wait, and
        // no announcement of one — a deadlocked swap costs one cap, not one
        // cap per round.
        Assert.Equal(Start, Ask(ref s, pending: true, now: 500f));
        Assert.Equal(Start, Ask(ref s, pending: true, now: 501f));
        Assert.False(s.Holding);
    }

    [Fact]
    public void ASwapThatSettlesAfterTheCapChangesNothing()
    {
        var s = default(SoloTacticalRoundStartPolicy.State);
        Ask(ref s, pending: true, now: 0f);
        Ask(ref s, pending: true, now: Cap);

        Assert.Equal(Start, Ask(ref s, pending: false, now: Cap + 5f));
        Assert.True(s.GaveUp);
    }
}
