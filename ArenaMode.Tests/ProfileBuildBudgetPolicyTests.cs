using ArenaMode.Performance;

namespace ArenaMode.Tests;

public sealed class ProfileBuildBudgetPolicyTests
{
    private const long Budget = 3000; // ticks per frame in these tests

    private static bool May(ref ProfileBuildBudgetPolicy.State state, int frame, bool builtThisFrame = false, int batches = 1)
        => ProfileBuildBudgetPolicy.MayStart(ref state, frame, Budget, batches, builtThisFrame);

    [Fact]
    public void TheFirstProfileOfAFrameAlwaysStarts()
    {
        var state = new ProfileBuildBudgetPolicy.State();
        Assert.True(May(ref state, 10));
        // Even when the previous build was far over budget and the average is huge:
        // the batch has built in this frame, so only the fit test remains, which fails...
        ProfileBuildBudgetPolicy.Record(ref state, 50_000);
        Assert.False(May(ref state, 10, builtThisFrame: true));
        // ...and the next frame grants the slot again.
        Assert.True(May(ref state, 11));
    }

    [Fact]
    public void FurtherProfilesStartOnlyWhileTheAverageStillFits()
    {
        var state = new ProfileBuildBudgetPolicy.State();
        Assert.True(May(ref state, 1));
        ProfileBuildBudgetPolicy.Record(ref state, 1000);            // spent 1000, average 1000
        Assert.True(May(ref state, 1, builtThisFrame: true));      // 1000 + 1000 <= 3000
        ProfileBuildBudgetPolicy.Record(ref state, 1000);            // spent 2000
        Assert.True(May(ref state, 1, builtThisFrame: true));      // 2000 + 1000 <= 3000
        ProfileBuildBudgetPolicy.Record(ref state, 1000);            // spent 3000
        Assert.False(May(ref state, 1, builtThisFrame: true));     // 3000 + 1000 > 3000
        // The next frame starts fresh.
        Assert.True(May(ref state, 2));
        Assert.Equal(0, state.SpentTicks);
        Assert.Equal(0, state.Started);
    }

    [Fact]
    public void EveryOpenBatchGetsOneGuaranteedBuildPerFrame()
    {
        // Two batches, each build 9000 ticks (three times the budget).
        var state = new ProfileBuildBudgetPolicy.State();
        Assert.True(May(ref state, 5, builtThisFrame: false, batches: 2));   // older batch, slot 1 of 2
        ProfileBuildBudgetPolicy.Record(ref state, 9000);
        Assert.False(May(ref state, 5, builtThisFrame: true, batches: 2));   // older batch again: over budget
        Assert.True(May(ref state, 5, builtThisFrame: false, batches: 2));   // newer batch, slot 2 of 2
        ProfileBuildBudgetPolicy.Record(ref state, 9000);
        Assert.False(May(ref state, 5, builtThisFrame: false, batches: 2));  // a third batch this frame: no slot left
        Assert.False(May(ref state, 5, builtThisFrame: true, batches: 2));
        // With a single batch the second slot does not exist.
        var single = new ProfileBuildBudgetPolicy.State();
        Assert.True(May(ref single, 5));
        ProfileBuildBudgetPolicy.Record(ref single, 9000);
        Assert.False(May(ref single, 5, builtThisFrame: false, batches: 1));
    }

    [Fact]
    public void AZeroOrNegativeBatchCountStillGrantsOneSlot()
    {
        var state = new ProfileBuildBudgetPolicy.State();
        Assert.True(May(ref state, 3, builtThisFrame: false, batches: 0));
        ProfileBuildBudgetPolicy.Record(ref state, 9000);
        Assert.False(May(ref state, 3, builtThisFrame: false, batches: -4));
    }

    [Fact]
    public void ANewFrameResetsSpentButKeepsTheAverage()
    {
        var state = new ProfileBuildBudgetPolicy.State();
        May(ref state, 1);
        ProfileBuildBudgetPolicy.Record(ref state, 2000);
        May(ref state, 2);
        Assert.Equal(0, state.SpentTicks);
        Assert.Equal(2000, state.AverageTicks);
        // A 2000-tick average after one 2000-tick build in frame 2 leaves no room for a second.
        ProfileBuildBudgetPolicy.Record(ref state, 2000);
        Assert.False(May(ref state, 2, builtThisFrame: true));
    }

    [Fact]
    public void TheAverageFollowsSamplesAtOneEighthAndIgnoresNegativeClocks()
    {
        var state = new ProfileBuildBudgetPolicy.State();
        ProfileBuildBudgetPolicy.Record(ref state, 800);
        Assert.Equal(800, state.AverageTicks);
        ProfileBuildBudgetPolicy.Record(ref state, 1600);   // 800 + (800 >> 3) = 900
        Assert.Equal(900, state.AverageTicks);
        ProfileBuildBudgetPolicy.Record(ref state, -500);   // clamped to 0: 900 + (-900 >> 3) = 900 - 113 = 787
        Assert.Equal(787, state.AverageTicks);
        Assert.Equal(3, state.Started);
        Assert.Equal(2400, state.SpentTicks);
    }

    [Fact]
    public void BudgetTicksScaleWithTheClockAndRefuseNonsense()
    {
        Assert.Equal(30_000, ProfileBuildBudgetPolicy.BudgetTicks(3f, 10_000_000));
        Assert.Equal(0, ProfileBuildBudgetPolicy.BudgetTicks(0f, 10_000_000));
        Assert.Equal(0, ProfileBuildBudgetPolicy.BudgetTicks(-1f, 10_000_000));
        Assert.Equal(0, ProfileBuildBudgetPolicy.BudgetTicks(3f, 0));
    }

    [Fact]
    public void TakeOverNeedsTheSwitchAPositiveBudgetAnArenaRaidTheMainThreadAndWork()
    {
        Assert.True(ProfileBuildBudgetPolicy.ShouldTakeOver(true, 3f, true, true, 2));
        Assert.False(ProfileBuildBudgetPolicy.ShouldTakeOver(false, 3f, true, true, 2));
        Assert.False(ProfileBuildBudgetPolicy.ShouldTakeOver(true, 0f, true, true, 2));
        Assert.False(ProfileBuildBudgetPolicy.ShouldTakeOver(true, float.NaN, true, true, 2));
        Assert.False(ProfileBuildBudgetPolicy.ShouldTakeOver(true, float.PositiveInfinity, true, true, 2));
        Assert.False(ProfileBuildBudgetPolicy.ShouldTakeOver(true, 3f, false, true, 2));
        Assert.False(ProfileBuildBudgetPolicy.ShouldTakeOver(true, 3f, true, false, 2));
        Assert.False(ProfileBuildBudgetPolicy.ShouldTakeOver(true, 3f, true, true, 0));
    }

    [Fact]
    public void AZeroBudgetStillBuildsOneProfilePerFrameAndNeverMore()
    {
        var state = new ProfileBuildBudgetPolicy.State();
        Assert.True(ProfileBuildBudgetPolicy.MayStart(ref state, 7, 0, 1, false));
        ProfileBuildBudgetPolicy.Record(ref state, 10);
        Assert.False(ProfileBuildBudgetPolicy.MayStart(ref state, 7, 0, 1, true));
        Assert.True(ProfileBuildBudgetPolicy.MayStart(ref state, 8, 0, 1, false));
    }
}
