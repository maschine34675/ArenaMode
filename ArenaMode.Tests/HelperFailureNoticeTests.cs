using ArenaMode.Server.Rewards;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

/// <summary>
/// The dialogs the session helper shows a player: what happened, whether the profile was changed (only where the path
/// proves it), what to do, where the logs are, and a short reference code at the end.
/// </summary>
public sealed class HelperFailureNoticeTests
{
    private const string Spt = @"D:\SPT";

    private static Dictionary<string, object> Wire(RewardReturnReply reply)
    {
        Assert.True(ArenaMiniJson.TryParse(RewardRuntimeFiles.Json(reply), out var value, out var error), error);
        return ArenaMiniJson.AsObject(value);
    }

    // The path a declined entry takes in Entry.StopNormal: the reply goes through CompleteStop, whose reason code becomes the exception.
    private static HelperOfflineSession.EntryDeclinedException Declined(string stage, string detail)
    {
        string? code = null;
        bool resume = HelperOfflineSession.CompleteStop(
            () => Wire(new RewardReturnReply(false, "return-not-ready", false, true, stage, detail)),
            () => throw new Exception("must not read proof"), _ => throw new Exception("must not wait"),
            () => throw new Exception("must not ask"), _ => { }, c => code = c);
        Assert.True(resume);
        return HelperOfflineSession.EntryDeclinedException.ResumableFrom(code);
    }

    private static string Notice(Exception ex, bool untouched = false) =>
        HelperRunner.EnterFailureNotice(ex, Spt, untouched) + "\n\nDetails: log\nCode: " + HelperRunner.EnterFailureCode(ex);

    [Fact]
    public void TheDeclinedSaveOfAPlayersReportExplainsItselfAndEndsWithItsCode()
    {
        // v0.6.0 direct start: helper.log said "preflight-save/save-not-confirmed; ... normal resume=True".
        var ex = Declined("preflight-save", "return-profile-save-not-confirmed");
        Assert.Equal("Arena entry was declined; the normal server confirmed it can continue.", ex.Message); // the log line stays
        string text = Notice(ex);
        Assert.StartsWith("Arena did not start.", text);
        Assert.Contains("did not match the profile in its memory", text);
        Assert.Contains("stopped before copying anything", text);
        Assert.Contains("Your profile was not changed.", text);
        Assert.Contains("cloud sync, backup, antivirus", text);
        Assert.Contains("close them and try again", text);
        Assert.Contains("\"Session switch refused\" line from the SPT server log", text);
        Assert.Contains(@"SPT server log folder: D:\SPT\user\logs\spt", text);
        Assert.EndsWith("Code: preflight-save/save-not-confirmed", text);
        Assert.DoesNotContain("Arena startup could not finish", text);
        Assert.DoesNotContain("check the logs below", text);
        Assert.DoesNotContain("Keep the SPT server open", text);
    }

    [Theory]
    [InlineData("return-session-not-idle", "session-busy", "still in a raid or busy")]
    [InlineData("TimeoutException", "timeout", "did not finish saving in time")]
    [InlineData("OperationCanceledException", "timeout", "did not finish saving in time")]
    [InlineData("IOException", "file-access", "could not read or write the profile files")]
    [InlineData("UnauthorizedAccessException", "file-access", "could not read or write the profile files")]
    [InlineData("something-new", "see-server-log", "SPT declined to prepare Arena.")]
    public void EveryDeclinedReasonHasItsOwnTextAndCode(string detail, string category, string phrase)
    {
        var ex = Declined("entry-preflight", detail);
        Assert.Equal("entry-preflight/" + category, ex.Code);
        string text = Notice(ex);
        Assert.Contains(phrase, text);
        Assert.Contains("Your profile was not changed.", text);
        Assert.EndsWith("Code: entry-preflight/" + category, text);
        Assert.DoesNotContain("did not match the profile in its memory", text); // that text belongs to save-not-confirmed alone
    }

    [Fact]
    public void ADeclinedEntryNeverGetsTheGenericSentence()
    {
        foreach (string detail in new[] { "return-profile-save-not-confirmed", "return-session-not-idle", "TimeoutException", "IOException", "x" })
            Assert.DoesNotContain("Arena startup could not finish", Notice(Declined("preflight-save", detail)));
    }

    [Fact]
    public void ARefusalTheServerCannotContinueFromDoesNotClaimAnUnchangedProfile()
    {
        var refused = Assert.ThrowsAny<HelperOfflineSession.EntryDeclinedException>(() => HelperOfflineSession.CompleteStop(
            () => Wire(new RewardReturnReply(false, "return-unavailable", false, false, "final-save", "IOException")),
            () => throw new Exception("x"), _ => throw new Exception("x"), () => throw new Exception("x"), _ => { }));
        Assert.False(refused.Resumed);
        Assert.Equal("Normal SPT refused Arena entry; no profile was copied.", refused.Message);
        string text = Notice(refused);
        Assert.DoesNotContain("profile was not changed", text);
        Assert.Contains("Arena copied no profile", text);
        Assert.EndsWith("Code: final-save/file-access", text);
    }

    [Fact]
    public void AServerThatIsShuttingDownKeepsItsOwnTextAndGetsTheLogFolderAndCode()
    {
        var ex = new HelperOfflineSession.ServerStoppingException();
        string text = Notice(ex);
        Assert.StartsWith(ex.Message, text);
        Assert.Contains(@"SPT server log folder: D:\SPT\user\logs\spt", text);
        Assert.EndsWith("Code: entry-proof-unpublished", text);
    }

    [Fact]
    public void AFailureBeforeTheNormalServerWasTouchedSaysSoAndOneAfterwardsDoesNot()
    {
        var ex = new HelperException("no free port between 6970 and 6980");
        string before = HelperRunner.EnterFailureNotice(ex, Spt, untouched: true);
        Assert.Equal("Arena did not start. No free port between 6970 and 6980. Your profile was not changed. Fix the problem above and try again.", before);
        string after = HelperRunner.EnterFailureNotice(ex, Spt, untouched: false);
        Assert.DoesNotContain("profile was not changed", after);
        Assert.Contains("If SPT is not running, start it again from the launcher.", after);
        Assert.DoesNotContain("SPT server log folder", after); // not a server refusal
        Assert.Equal("start-failed", HelperRunner.EnterFailureCode(ex));
        Assert.Equal("start-failed", HelperRunner.EnterFailureCode(new InvalidOperationException("x")));
        string unexpected = HelperRunner.EnterFailureNotice(new InvalidOperationException("secret path C:\\x"), Spt, true);
        Assert.DoesNotContain("secret", unexpected); // only helper.log carries the raw text
        Assert.Contains("unexpected error", unexpected);
    }

    [Fact]
    public void ANoticeIsShortAndEveryOneEndsWithTheDetailsAndTheCodeLine()
    {
        var shown = new List<string>();
        string root = Path.Combine(Path.GetTempPath(), "arena-notice-" + Guid.NewGuid().ToString("N"));
        var runner = new HelperRunner(root, new HelperLog(Path.Combine(root, "helper.log")), "test") { FailureNotice = shown.Add };
        runner.NotifyFailure(HelperRunner.EnterFailureNotice(Declined("preflight-save", "return-profile-save-not-confirmed"), Spt),
            "preflight-save/save-not-confirmed");
        string text = Assert.Single(shown);
        Assert.Contains("Details: " + Path.Combine(root, ArenaSessionContract.HelperLogFileName), text);
        Assert.EndsWith("\nCode: preflight-save/save-not-confirmed", text);
        string body = text.Substring(0, text.IndexOf("\n\n", StringComparison.Ordinal));
        Assert.True(body.Split(". ").Length <= 6, body); // about five sentences
    }

    [Fact]
    public void TheWaitingDialogNamesHelperLogInFullAndHasACode()
    {
        string text = HelperOfflineSession.WaitingDialogText(@"C:\logs\helper.log");
        Assert.Contains(@"Details: C:\logs\helper.log", text);
        Assert.EndsWith("Code: entry-waiting", text);
        Assert.DoesNotContain("check helper.log", text);
        Assert.Contains("no profile is copied", text);
    }

    [Fact]
    public void TheReturnDialogsPointAtTheServerLogAndEndWithACode()
    {
        foreach (var (text, code) in new[]
        {
            (HelperRewardReturn.QueuedDialogText(Spt), "return-rewards-queued"),
            (HelperRewardReturn.PausedDialogText("x", Spt), "return-paused"),
            (HelperRewardReturn.NotReadyDialogText(Spt), "return-server-not-ready"),
        })
        {
            Assert.Contains(@"SPT server log folder: D:\SPT\user\logs\spt", text);
            Assert.EndsWith("Code: " + code, text);
            Assert.Contains("rewards", text);
            Assert.DoesNotContain("review the SPT server log", text);
        }
    }

    [Fact]
    public void ALeaveFailureTellsWhatToDoNextInsteadOfPointingBelow()
    {
        string text = HelperRunner.LeaveFailureNotice(new HelperException("x"), Spt);
        Assert.Contains("start it again from the launcher", text);
        Assert.DoesNotContain("check the logs below", text);
        Assert.Contains(@"SPT server log folder: D:\SPT\user\logs\spt", text);
    }
}
