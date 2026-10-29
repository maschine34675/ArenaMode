using ArenaMode.Server.Rewards;
using ArenaMode.Server.Routers;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// After the native stop sequence the normal server can never serve again (its update loop is stopped for
/// good), so a failure to publish the entry proof must still end in the graceful shutdown, never in a server
/// that stays paused with nothing registered. docs/REWARDS-RUNTIME.md, "Lost reward authority".
/// </summary>
public sealed class RewardEntryPublicationTests
{
    private static readonly RewardReturnReply Ready = new(true, "ready");

    private sealed class Probe
    {
        internal readonly List<bool> Shutdowns = [];
        internal readonly List<string> Warnings = [];
        internal int Delays;
        internal Task<RewardReturnReply> Run(Func<RewardOfflineLease> create)
            => RewardEntryPublication.PublishAsync(create, Ready, Shutdowns.Add, Warnings.Add, _ => { Delays++; return Task.CompletedTask; });
    }

    private static RewardOfflineLease Lease() => null!; // the publisher never looks at the lease it is handed back

    [Fact]
    public async Task PublishedProofAcceptsAndSchedulesTheShutdownOnce()
    {
        var probe = new Probe();
        Assert.Equal(Ready, await probe.Run(Lease));
        Assert.Equal([true], probe.Shutdowns);
        Assert.Empty(probe.Warnings);
        Assert.Equal(0, probe.Delays);
    }

    [Fact]
    public async Task ATransientSharingViolationIsRetriedAndStillPublishes()
    {
        var probe = new Probe();
        int calls = 0;
        var reply = await probe.Run(() => ++calls < RewardEntryPublication.Attempts ? throw new IOException("busy") : Lease());
        Assert.Equal(Ready, reply);
        Assert.Equal(RewardEntryPublication.Attempts, calls);
        Assert.Equal([true], probe.Shutdowns);
        Assert.Equal(RewardEntryPublication.Attempts - 1, probe.Delays);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("data")]
    public async Task AFailedPublicationDeclinesAndStillSchedulesTheShutdown(string kind)
    {
        var probe = new Probe();
        int calls = 0;
        var reply = await probe.Run(() =>
        {
            calls++;
            throw kind switch
            {
                "io" => new IOException("disk"),
                "access" => new UnauthorizedAccessException(),
                _ => new RewardDataException("offline-session-already-active")
            };
        });
        // Declined, not held: the helper must not wait in its Retry/Cancel loop for a server that is about to exit.
        Assert.False(reply.Accepted);
        Assert.False(reply.Held);
        Assert.False(reply.CanResumeNormal);
        Assert.Equal("entry-proof-unpublished", reply.Reason);
        Assert.Equal([false], probe.Shutdowns); // the server is stopped for good: it must go down gracefully
        Assert.Single(probe.Warnings);
        Assert.Contains("shutting down", probe.Warnings[0]);
        // Only transient file errors are retried; a refusal by the entry state itself is final.
        Assert.Equal(kind == "data" ? 1 : RewardEntryPublication.Attempts, calls);
    }

    [Fact]
    public async Task ThePausedServerThatTheStopSequenceLeavesBehindIsAlwaysFollowedByAShutdown()
    {
        var requests = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(requests, new RewardReturnRequestGate());
        var prepared = await sequence.PrepareAsync(() => true, _ => Task.CompletedTask, _ => Task.CompletedTask, CancellationToken.None);
        Assert.True(prepared.Accepted);
        // Paused for good: updates stopped and requests held. Nothing in the sequence resumes it.
        Assert.True(ArenaRewardReturnListener.ReturnHeld(sequence, requests));

        var probe = new Probe();
        var reply = await probe.Run(() => throw new IOException("disk"));

        Assert.False(reply.Accepted);
        Assert.True(ArenaRewardReturnListener.ReturnHeld(sequence, requests));
        Assert.Single(probe.Shutdowns); // so the only consistent end is the graceful exit
    }

    private static Dictionary<string, object> Wire(RewardReturnReply reply)
    {
        Assert.True(ArenaMiniJson.TryParse(RewardRuntimeFiles.Json(reply), out var value, out var error), error);
        return ArenaMiniJson.AsObject(value);
    }

    [Fact]
    public void ADeclinedPreflightMakesTheHelperReopenTheNormalSessionWithoutWaiting()
    {
        var wire = Wire(RewardEntryPublication.Declined("offline-session-already-active", canResumeNormal: true));
        Assert.True(HelperOfflineSession.AllowsNormalResume(wire));
        // The helper returns "explicit safe refusal" at once: no proof is read, no exit awaited, no dialog shown.
        Assert.True(HelperOfflineSession.CompleteStop(() => wire,
            () => throw new Exception("must not read proof"), _ => throw new Exception("must not wait for exit"),
            () => throw new Exception("must not ask"), _ => { }));
    }

    [Fact]
    public async Task AFailedPublicationEndsTheHelpersWaitInsteadOfOfferingRetryAgainstADyingServer()
    {
        var reply = await new Probe().Run(() => throw new IOException("disk"));
        Assert.False(HelperOfflineSession.AllowsNormalResume(Wire(reply)));
        int asked = 0;
        Assert.Throws<HelperException>(() => HelperOfflineSession.CompleteStop(() => Wire(reply),
            () => throw new Exception("must not look for proof"), _ => throw new Exception("must not wait for exit"),
            () => { asked++; return true; }, _ => { }));
        Assert.Equal(0, asked);
    }
}
