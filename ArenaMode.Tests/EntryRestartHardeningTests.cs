using ArenaMode.Server.Rewards;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

public sealed class EntryRestartHardeningTests
{
    private static Dictionary<string, object> Wire(RewardReturnReply reply)
    {
        Assert.True(ArenaMiniJson.TryParse(RewardRuntimeFiles.Json(reply), out var value, out var error), error);
        return ArenaMiniJson.AsObject(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrainDeadlineReportsItsStageAndPermitsOnlyNormalClientRecovery(bool update)
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(requests, updates);
        using var busy = (update ? updates : requests).TryEnter();
        using var deadline = new CancellationTokenSource();
        var preparation = sequence.PrepareAsync(() => true,
            _ => throw new Exception("must not save before drain"),
            _ => throw new Exception("must not stop before drain"), deadline.Token);
        deadline.Cancel();
        var reply = await preparation;
        Assert.False(reply.Accepted);
        Assert.False(reply.Held);
        Assert.True(reply.CanResumeNormal);
        Assert.Equal(update ? "update-drain" : "http-drain", reply.Stage);
        Assert.Equal(update ? 0 : 1, reply.ActiveRequests);
        Assert.Equal(update ? 1 : 0, reply.ActiveUpdates);
        Assert.False(requests.Held);
        Assert.False(updates.Held);
        var messages = new List<string>();
        Assert.True(HelperOfflineSession.CompleteStop(() => Wire(reply),
            () => throw new Exception("must not read old proof"),
            _ => throw new Exception("must not wait for exit"),
            () => throw new Exception("must not block on retry"), messages.Add));
        Assert.Contains(messages, line => line.Contains(reply.Stage + "/timeout"));
    }

    [Fact]
    public async Task FinalSaveFailureNeverPermitsReopeningAndKeepsBothGatesHeld()
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        var sequence = new RewardReturnSequence(requests, updates);
        int saves = 0, retries = 0;
        var reply = await sequence.PrepareAsync(() => true,
            _ => ++saves == 2 ? Task.FromException(new IOException("do-not-echo-secret")) : Task.CompletedTask,
            _ => Task.CompletedTask, CancellationToken.None);
        Assert.True(reply.Held);
        Assert.False(reply.CanResumeNormal);
        Assert.Equal("final-save", reply.Stage);
        Assert.True(requests.Held);
        Assert.True(updates.Held);
        Assert.DoesNotContain("do-not-echo-secret", RewardRuntimeFiles.Json(reply));
        Assert.Throws<HelperException>(() => HelperOfflineSession.CompleteStop(() => Wire(reply),
            () => throw new IOException(), _ => throw new Exception("must not infer exit"),
            () => { retries++; return false; }, _ => { }));
        Assert.Equal(1, retries);
    }

    [Fact]
    public async Task AnotherRestoreHoldCannotBeReleasedOrAuthorizedByRejectedSwitch()
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        using var restore = await updates.HoldAndDrainAsync(CancellationToken.None);
        var reply = await new RewardReturnSequence(requests, updates).PrepareAsync(() => false,
            _ => throw new Exception(), _ => throw new Exception(), CancellationToken.None);
        Assert.False(reply.CanResumeNormal);
        Assert.True(updates.Held);
        Assert.False(HelperOfflineSession.AllowsNormalResume(Wire(reply)));
    }

    [Theory]
    [InlineData("missing-accepted")]
    [InlineData("missing-held")]
    [InlineData("missing-permission")]
    [InlineData("missing-reason")]
    [InlineData("string-permission")]
    [InlineData("string-held")]
    [InlineData("accepted")]
    [InlineData("held")]
    [InlineData("generic-refusal")]
    public void IncompleteAmbiguousAndHeldRepliesCannotAuthorizeRecovery(string mutation)
    {
        var reply = Wire(new(false, "return-not-ready", CanResumeNormal: true));
        switch (mutation)
        {
            case "missing-accepted": reply.Remove("accepted"); break;
            case "missing-held": reply.Remove("held"); break;
            case "missing-permission": reply.Remove("canResumeNormal"); break;
            case "missing-reason": reply.Remove("reason"); break;
            case "string-permission": reply["canResumeNormal"] = "true"; break;
            case "string-held": reply["held"] = "false"; break;
            case "accepted": reply["accepted"] = true; break;
            case "held": reply["held"] = true; break;
            default: reply["reason"] = "return-unavailable"; break;
        }
        Assert.False(HelperOfflineSession.AllowsNormalResume(reply));
    }

    [Fact]
    public void LostOrMalformedReplyCannotFallBackToNormal()
    {
        foreach (bool malformed in new[] { false, true })
        {
            var messages = new List<string>();
            int retries = 0;
            Assert.Throws<HelperException>(() => HelperOfflineSession.CompleteStop(
                () => malformed ? new() { ["held"] = false, ["canResumeNormal"] = true }
                    : throw new IOException("private-token"),
                () => throw new IOException(), _ => throw new Exception("must not infer exit"),
                () => { retries++; return false; }, messages.Add));
            Assert.Equal(1, retries);
            Assert.DoesNotContain(messages, line => line.Contains("private-token"));
        }
    }

    [Fact]
    public void HelperDiagnosticsNeverEchoArbitraryWireText()
    {
        var reply = Wire(new(false, "secret\nline", Stage: "secret\nline", Detail: "secret\nline",
            ActiveRequests: -5, ActiveUpdates: int.MaxValue));
        string text = HelperOfflineSession.DescribeReply(reply);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain('\n', text);
        Assert.Contains("unknown-stage/see-server-log", text);
        Assert.Contains("active requests=unknown; active updates=unknown", text);
    }
}
