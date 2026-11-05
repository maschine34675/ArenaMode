using System.Text.Json;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Routers;
using Xunit;

namespace ArenaMode.Tests;

public sealed class SessionSaveDiagnosticsTests
{
    private const string Selected = "0123456789abcdef01234567";
    private const string Other = "abcdef0123456789abcdef01";
    private const string Current = "{\"characters\":{\"pmc\":{\"Hideout\":{\"sptUpdateLastRunTimestamp\":10}}}}";
    private const string Changed = "{\"characters\":{\"pmc\":{\"Hideout\":{\"sptUpdateLastRunTimestamp\":11}}}}";

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task RealSaveSequenceKeepsReplyAndHoldsAndReportsComparisonDirectionsOnce(
        bool diskChanged, bool ramChanged, bool finalSave)
    {
        var diagnostic = new SessionSaveDiagnostics();
        var actual = await Run(diagnostic);
        var original = await Run(null);
        Assert.Equal(original.reply, actual.reply);
        Assert.Equal(original.requests.Held, actual.requests.Held);
        Assert.Equal(original.updates.Held, actual.updates.Held);
        Assert.Equal(finalSave, actual.reply.Held);
        Assert.Equal(!finalSave, actual.reply.CanResumeNormal);
        Assert.Equal(finalSave ? "final-save" : "preflight-save", actual.reply.Stage);
        var lines = new List<string>();
        diagnostic.WarnRefusal(true, actual.reply, true, lines.Add);
        diagnostic.WarnRefusal(true, actual.reply, true, lines.Add);
        string line = Assert.Single(lines);
        Assert.Contains("operation=enter", line);
        Assert.Contains($"ramDiskMatch={(!diskChanged ? "true" : "false")}", line);
        Assert.Contains($"ramStableDuringReadback={(!ramChanged ? "true" : "false")}", line);
        Assert.Contains("characters.pmc.Hideout.sptUpdateLastRunTimestamp", line);
        Assert.Equal(finalSave, line.Contains("do not close it"));
        Assert.DoesNotContain("held=", line);
        if (!finalSave)
        {
            using var requestLease = actual.requests.TryEnter();
            using var updateLease = actual.updates.TryEnter();
            Assert.NotNull(requestLease);
            Assert.NotNull(updateLease);
        }

        async Task<(RewardReturnReply reply, RewardReturnRequestGate requests, RewardReturnRequestGate updates)> Run(
            SessionSaveDiagnostics? capture)
        {
            var requests = new RewardReturnRequestGate();
            var updates = new RewardReturnRequestGate();
            var sequence = new RewardReturnSequence(requests, updates);
            int saves = 0;
            var reply = await sequence.PrepareAsync(() => true, _ =>
            {
                if (++saves == (finalSave ? 2 : 1))
                {
                    string disk = diskChanged ? Changed : Current;
                    string ram = ramChanged ? Changed : Current;
                    if (capture is null) ArenaRewardReturnListener.RequireSavedProfile(Current, disk, ram);
                    else ArenaRewardReturnListener.RequireSavedProfile(Current, disk, ram, capture, Selected, Selected);
                }
                return Task.CompletedTask;
            }, _ => Task.CompletedTask, CancellationToken.None);
            return (reply, requests, updates);
        }
    }

    [Fact]
    public void DynamicNamesValuesIdsAndNewlinesNeverReachWarningAndSampleHasAtMostThreeLabels()
    {
        const string secret = "SECRET_PROFILE_VALUE\r\nforged log entry";
        string disk = JsonSerializer.Serialize(new
        {
            characters = new { pmc = new
            {
                Hideout = new { Production = new Dictionary<string, string> { [secret] = secret } },
                Inventory = new { items = new[] { new { _id = Selected, _tpl = secret } } },
                Info = new { Nickname = secret }, Skills = new[] { secret }, Health = secret
            } },
            privateExtension = new Dictionary<string, string> { [secret] = Other }
        });
        var diagnostic = Capture("{}", disk, disk, Other);
        string line = Warning(diagnostic);
        Assert.Contains($"profile={RewardIdentity.Hash(Other)[..8]} (other-loaded)", line);
        Assert.DoesNotContain(Selected, line);
        Assert.DoesNotContain(Other, line);
        Assert.DoesNotContain("SECRET", line);
        Assert.DoesNotContain("forged", line);
        Assert.DoesNotContain("privateExtension", line);
        Assert.DoesNotContain('\n', line);
        string fields = line.Split("fieldSample=[")[1].Split(']')[0];
        Assert.Equal(3, fields.Split(',').Length);
        Assert.True(line.Length < 1000);
    }

    [Theory]
    [InlineData("{\"characters\":{\"pmc\":{\"Inventory\":null}}}", "characters.pmc.Inventory")]
    [InlineData("{\"characters\":{\"pmc\":{\"mod-owned-secret-key\":42}}}", "characters.pmc")]
    [InlineData("{\"mod-owned-secret-key\":42}", "unclassified-section")]
    public void MissingVersusNullAndUnknownKeysHaveOnlyFixedSectionLabels(string disk, string section)
    {
        string line = Warning(Capture("{}", disk, "{}"));
        Assert.Contains("fieldSample=[disk:" + section + "]", line);
        Assert.DoesNotContain("mod-owned-secret-key", line);
    }

    [Fact]
    public void ArraysAreComparedWithoutExposingItemsOrIndices()
    {
        const string before = "{\"characters\":{\"pmc\":{\"Inventory\":{\"items\":[1,2]}}}}";
        const string disk = "{\"characters\":{\"pmc\":{\"Inventory\":{\"items\":[2,1]}}}}";
        Assert.Contains("fieldSample=[disk:characters.pmc.Inventory]", Warning(Capture(before, disk, before)));
    }

    [Fact]
    public async Task SuccessIncludingSemanticJsonFormattingEmitsNoWarning()
    {
        var diagnostic = new SessionSaveDiagnostics();
        var sequence = new RewardReturnSequence(new(), new());
        const string before = "{\"info\":1,\"inraid\":{}}";
        var reply = await sequence.PrepareAsync(() => true, _ =>
        {
            ArenaRewardReturnListener.RequireSavedProfile(before, "{ \"inraid\":{}, \"info\":1 }", before,
                diagnostic, Selected, Selected);
            return Task.CompletedTask;
        }, _ => Task.CompletedTask, CancellationToken.None);
        Assert.True(reply.Accepted);
        var lines = new List<string>();
        diagnostic.WarnRefusal(true, reply, true, lines.Add);
        Assert.Empty(lines);
        // A later unrelated refusal cannot acquire readback data from a successful comparison.
        Assert.Contains("profile=unavailable", Warning(diagnostic));
    }

    [Fact]
    public void OversizedDiagnosticKeepsOriginalRefusalWithoutParsingOrPrintingData()
    {
        string large = "{\"private\":\"" + new string('x', SessionSaveDiagnostics.MaxSnapshotCharacters) + "\"}";
        string line = Warning(Capture(large, "{}", large));
        Assert.Contains("diagnostics=size-limit", line);
        Assert.Contains("ramDiskMatch=unknown; ramStableDuringReadback=unknown", line);
        Assert.Contains("fieldSample=[]", line);
    }

    [Theory]
    [InlineData("{invalid-json")]
    [InlineData("{\"info\":1,\"info\":2}")]
    [InlineData("too-deep")]
    public void InvalidSecondComparisonCannotReplaceOriginalShortCircuitRefusal(string after)
    {
        if (after == "too-deep") after = new string('[', 70) + "1" + new string(']', 70);
        var original = Assert.Throws<RewardDataException>(() =>
            ArenaRewardReturnListener.RequireSavedProfile(Current, Changed, after));
        Assert.Equal("return-profile-save-not-confirmed", original.Message);
        string line = Warning(Capture(Current, Changed, after));
        Assert.Contains("ramDiskMatch=false; ramStableDuringReadback=unknown", line);
        Assert.Contains("diagnostics=partial", line);
        Assert.DoesNotContain("invalid-json", line);
    }

    [Fact]
    public void DuplicateDiskPropertiesRetainOriginalRefusalCode()
    {
        var diagnostic = new SessionSaveDiagnostics();
        var exception = Assert.Throws<RewardDataException>(() =>
            ArenaRewardReturnListener.RequireSavedProfile("{}", "{\"x\":1,\"x\":2}", "{}", diagnostic, Selected, Selected));
        Assert.Equal("duplicate-reward-property", exception.Message);
    }

    [Fact]
    public void DiagnosticComparisonUsesTheSameNumericSemanticsAsTheSaveProof()
    {
        const string before = "{\"info\":1,\"inraid\":{}}";
        const string disk = "{\"inraid\":{},\"info\":1.0}";
        bool expected = RewardRuntimeFiles.SameProfile(before, disk);
        string line = Warning(Capture(before, disk, "{}"));
        Assert.Contains("ramDiskMatch=" + (expected ? "true" : "false"), line);
        Assert.Contains("ramStableDuringReadback=false", line);
    }

    [Fact]
    public async Task ZeroActiveCountersDoNotHideAnotherOwnersScopedHold()
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        using var otherOwner = await updates.HoldAndDrainAsync(CancellationToken.None);
        var diagnostic = new SessionSaveDiagnostics();
        var reply = await new RewardReturnSequence(requests, updates).PrepareAsync(() => true, _ =>
        {
            ArenaRewardReturnListener.RequireSavedProfile(Current, Changed, Current, diagnostic, Selected, Selected);
            return Task.CompletedTask;
        }, _ => Task.CompletedTask, CancellationToken.None);
        Assert.False(reply.Held);
        Assert.False(reply.CanResumeNormal);
        var lines = new List<string>();
        diagnostic.WarnRefusal(true, reply, true, lines.Add);
        string line = Assert.Single(lines);
        Assert.Contains("active requests=0, active updates=0", line);
        Assert.Contains("updateStopStarted=false; canResumeNormal=false", line);
        Assert.True(updates.Held);
    }

    [Fact]
    public async Task StopFailureReportsStartedRatherThanCompletedAndThrowingLoggerCannotAlterReply()
    {
        var requests = new RewardReturnRequestGate();
        var updates = new RewardReturnRequestGate();
        var reply = await new RewardReturnSequence(requests, updates).PrepareAsync(() => true,
            _ => Task.CompletedTask, _ => throw new IOException(), CancellationToken.None);
        string originalWireReply = RewardRuntimeFiles.Json(reply);
        var diagnostic = new SessionSaveDiagnostics();
        string? observed = null;
        diagnostic.WarnRefusal(true, reply, true, line => { observed = line; throw new IOException(); });
        Assert.Contains("stop-updates/IOException", observed);
        Assert.Contains("updateStopStarted=true; canResumeNormal=false", observed);
        Assert.DoesNotContain("ramDiskMatch", observed);
        Assert.True(requests.Held);
        Assert.True(updates.Held);
        Assert.Equal(originalWireReply, RewardRuntimeFiles.Json(reply));
    }

    [Theory]
    [InlineData(true, false, "unconfirmed")]
    [InlineData(false, true, "false")]
    public void WarningUsesFinalResumeDecisionOrMarksItUnconfirmed(bool candidateResume, bool confirmed, string expected)
    {
        var lines = new List<string>();
        var reply = Refusal with { CanResumeNormal = candidateResume };
        Capture(Current, Changed, Current).WarnRefusal(false, reply, confirmed, lines.Add);
        string line = Assert.Single(lines);
        Assert.Contains("operation=return", line);
        Assert.Contains("updateStopStarted=false; canResumeNormal=" + expected, line);
    }

    [Fact]
    public void AttemptsDoNotShareDiagnosticsOrSuppressTheNextWarningAndWireContractStaysUnchanged()
    {
        string first = Warning(Capture(Current, Changed, Current, Other));
        string second = Warning(Capture(Current, Current, Changed));
        Assert.Contains("(other-loaded); ramDiskMatch=false; ramStableDuringReadback=true", first);
        Assert.Contains("(selected); ramDiskMatch=true; ramStableDuringReadback=false", second);
        using var wire = JsonDocument.Parse(RewardRuntimeFiles.Json(Refusal));
        Assert.Equal(new[] { "accepted", "reason", "held", "canResumeNormal", "stage", "detail", "activeRequests", "activeUpdates" },
            wire.RootElement.EnumerateObject().Select(p => p.Name));
    }

    private static readonly RewardReturnReply Refusal = new(false, "return-not-ready", false, true,
        "preflight-save", "return-profile-save-not-confirmed");

    private static SessionSaveDiagnostics Capture(string before, string disk, string after, string profile = Selected)
    {
        var diagnostic = new SessionSaveDiagnostics();
        var error = Assert.Throws<RewardDataException>(() =>
            ArenaRewardReturnListener.RequireSavedProfile(before, disk, after, diagnostic, profile, Selected));
        Assert.Equal("return-profile-save-not-confirmed", error.Message);
        return diagnostic;
    }

    private static string Warning(SessionSaveDiagnostics diagnostic)
    {
        var lines = new List<string>();
        diagnostic.WarnRefusal(true, Refusal, true, lines.Add);
        return Assert.Single(lines);
    }
}
