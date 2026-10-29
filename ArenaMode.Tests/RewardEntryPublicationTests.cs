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
    public async Task TheHelperKnowsTheStageOfAnUnpublishedEntryProof()
    {
        var reply = await new Probe().Run(() => throw new IOException("disk"));
        string text = HelperOfflineSession.DescribeReply(Wire(reply));
        Assert.StartsWith("publish-entry-proof/file-access", text);
        Assert.DoesNotContain("unknown-stage", text);
        Assert.Contains("normal resume=False", text);
    }

    [Fact]
    public void ACauseThatThePreflightAlreadyNamedIsNotLoggedAgainAfterTheStop()
    {
        Assert.False(ArenaRewardReturnListener.ReportsAfterStop("IOException", "IOException"));
        Assert.True(ArenaRewardReturnListener.ReportsAfterStop("IOException", "offline-existing-consent-missing"));
        Assert.True(ArenaRewardReturnListener.ReportsAfterStop(null, "IOException"));
    }

    [Fact]
    public async Task AFailedPublicationEndsTheHelpersWaitInsteadOfOfferingRetryAgainstADyingServer()
    {
        var reply = await new Probe().Run(() => throw new IOException("disk"));
        Assert.False(HelperOfflineSession.AllowsNormalResume(Wire(reply)));
        int asked = 0;
        Assert.ThrowsAny<HelperException>(() => HelperOfflineSession.CompleteStop(() => Wire(reply),
            () => throw new Exception("must not look for proof"), _ => throw new Exception("must not wait for exit"),
            () => { asked++; return true; }, _ => { }));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task AFailedPublicationTellsThePlayerThatTheServerIsShuttingDownInsteadOfToKeepItOpen()
    {
        var reply = await new Probe().Run(() => throw new IOException("disk"));
        var stopping = Assert.Throws<HelperOfflineSession.ServerStoppingException>(() => HelperOfflineSession.CompleteStop(() => Wire(reply),
            () => throw new Exception("must not look for proof"), _ => throw new Exception("must not wait for exit"),
            () => throw new Exception("must not ask"), _ => { }));
        Assert.Contains("shutting down", stopping.Message);
        Assert.Contains("Start SPT again", stopping.Message);
        // This is the text the player gets at the end of a failed enter; "keep the server open" would be wrong here.
        Assert.Equal(stopping.Message, HelperRunner.EnterFailureNotice(stopping));
        Assert.DoesNotContain("Keep the SPT server open", HelperRunner.EnterFailureNotice(stopping));
    }

    [Fact]
    public void EveryOtherRefusalKeepsTheGenericNoticeBecauseTheServerIsStillThere()
    {
        var refused = Assert.ThrowsAny<HelperException>(() => HelperOfflineSession.CompleteStop(
            () => Wire(new(false, "return-unavailable")),
            () => throw new Exception("must not look for proof"), _ => throw new Exception("must not wait for exit"),
            () => throw new Exception("must not ask"), _ => { }));
        Assert.IsNotType<HelperOfflineSession.ServerStoppingException>(refused);
        Assert.Contains("Keep the SPT server open", HelperRunner.EnterFailureNotice(refused));
    }

    [Fact]
    public void TheHelperKnowsTheStageOfADeclinedPreflight()
    {
        string text = HelperOfflineSession.DescribeReply(Wire(RewardEntryPublication.Declined("offline-session-already-active", canResumeNormal: true)));
        Assert.StartsWith("entry-preflight/", text);
        Assert.DoesNotContain("unknown-stage", text);
        Assert.Contains("normal resume=True", text);
    }

    // The wiring itself lives in the listener's request handler, which needs the whole SPT host to run. These pin its
    // order in the source, which is the whole fix: ask first (undoable), then stop, then publish through the
    // publisher that schedules the shutdown whatever happens.
    [Fact]
    public void TheListenerPreflightsBeforeThePauseAndPublishesOnlyThroughTheSchedulingPublisher()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root, "ArenaMode.Server", "Routers", "ArenaRewardReturnListener.cs")).Replace("\r\n", "\n");
        int preflight = source.IndexOf("RewardOfflineSession.PreflightReady(", StringComparison.Ordinal);
        int declined = source.IndexOf("RewardEntryPublication.Declined(", StringComparison.Ordinal);
        int pause = source.IndexOf("RewardReturnQuiescence.Sequence.PrepareAsync(", StringComparison.Ordinal);
        int publish = source.IndexOf("RewardEntryPublication.PublishAsync(", StringComparison.Ordinal);
        Assert.True(preflight > 0 && declined > preflight && pause > declined && publish > pause,
            $"order preflight={preflight} declined={declined} pause={pause} publish={publish}");
        // Exactly one place creates the lease, and it is inside the publisher's callback, which gets the shutdown.
        Assert.Equal(1, source.Split("RewardOfflineSession.CreateReady(").Length - 1);
        int create = source.IndexOf("RewardOfflineSession.CreateReady(", StringComparison.Ordinal);
        Assert.True(create > publish, "the lease must be created inside PublishAsync");
        int end = source.IndexOf("ScheduleShutdown,", publish, StringComparison.Ordinal);
        Assert.True(end > create && end - publish < 900, "PublishAsync must be handed ScheduleShutdown");
        // The refusal before the pause releases what it reserved, and answers with the decline that lets the helper resume.
        string branch = source.Substring(declined - 400, 700);
        Assert.Contains("Release();", branch);
        // The cause the preflight already named is not logged a second time after the stop: the callback must go through the helper.
        Assert.Contains("if (ReportsAfterStop(preflightUnavailable, reason)) WarnAuthorityUnavailable(", source);
    }
}
