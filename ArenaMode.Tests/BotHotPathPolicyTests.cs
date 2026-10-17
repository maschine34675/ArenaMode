using ArenaMode.Performance;

namespace ArenaMode.Tests;

public sealed class BotHotPathPolicyTests
{
    [Fact]
    public void TrimAppliesTheQueueAndEmptiesIt()
    {
        var live = new HashSet<string> { "a", "b", "c" };
        var queued = new HashSet<string> { "b" };

        Assert.Equal(1, BotHotPathPolicy.TrimDeletedAgents(live, queued, enabled: true, controllerEnabled: true));

        Assert.Equal(new[] { "a", "c" }, live.OrderBy(x => x));
        Assert.Empty(queued);
    }

    [Fact]
    public void TrimLeavesTheLiveSetExactlyAsTheNativePassWould()
    {
        // Native: every queued agent removed, queue kept. Ours: same removal, queue
        // cleared. The live set must be identical after both, including an agent
        // queued that was never in the live set.
        var nativeLive = new HashSet<int> { 1, 2, 3, 4 };
        var nativeQueue = new HashSet<int> { 2, 4, 9 };
        foreach (var agent in nativeQueue) nativeLive.Remove(agent);

        var ourLive = new HashSet<int> { 1, 2, 3, 4 };
        var ourQueue = new HashSet<int> { 2, 4, 9 };
        BotHotPathPolicy.TrimDeletedAgents(ourLive, ourQueue, true, true);

        Assert.Equal(nativeLive.OrderBy(x => x), ourLive.OrderBy(x => x));
        Assert.Empty(ourQueue);
    }

    [Fact]
    public void TrimDoesNothingWhenDisabledOrWhileTheControllerIsStopped()
    {
        var live = new HashSet<string> { "a", "b" };
        var queued = new HashSet<string> { "b" };

        Assert.Equal(0, BotHotPathPolicy.TrimDeletedAgents(live, queued, enabled: false, controllerEnabled: true));
        Assert.Equal(0, BotHotPathPolicy.TrimDeletedAgents(live, queued, enabled: true, controllerEnabled: false));

        Assert.Equal(2, live.Count);
        Assert.Single(queued);
    }

    [Fact]
    public void TrimToleratesEmptyAndMissingSets()
    {
        Assert.Equal(0, BotHotPathPolicy.TrimDeletedAgents(new HashSet<int>(), new HashSet<int>(), true, true));
        Assert.Equal(0, BotHotPathPolicy.TrimDeletedAgents<int>(null, new HashSet<int> { 1 }, true, true));
        Assert.Equal(0, BotHotPathPolicy.TrimDeletedAgents(new HashSet<int> { 1 }, null, true, true));
    }

    [Fact]
    public void FovShortCircuitNeedsEveryCondition()
    {
        Assert.True(BotHotPathPolicy.ShouldShortCircuitFov(true, true, true, true));
        Assert.False(BotHotPathPolicy.ShouldShortCircuitFov(false, true, true, true));
        Assert.False(BotHotPathPolicy.ShouldShortCircuitFov(true, false, true, true));
        Assert.False(BotHotPathPolicy.ShouldShortCircuitFov(true, true, false, true));
        Assert.False(BotHotPathPolicy.ShouldShortCircuitFov(true, true, true, false));
    }
}
