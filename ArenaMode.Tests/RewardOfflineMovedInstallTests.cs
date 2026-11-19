using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// An installation moved or copied to another folder: every offline-session lease still names the old absolute
/// folder. Finished sessions must not stop startup; unfinished ones can only be finished by the installation that
/// recorded them and must say so. docs/REWARDS-RUNTIME.md, "Lost reward authority".
/// </summary>
public sealed partial class RewardOfflineSessionTests
{
    private static string SessionFolder(Fixture f, string id) => RewardOfflineSession.SessionDirectory(f.Root, id);

    private static RewardOfflineException Stopped(Fixture f, string reason)
    {
        var e = Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.Equal(reason, Assert.IsType<RewardDataException>(e.InnerException).Message);
        return e;
    }

    private static void AssertNamesBothFolders(RewardOfflineException e, string oldRoot, Fixture f, string id)
    {
        Assert.Contains(id, e.Message);
        Assert.Contains("\"" + oldRoot + "\"", e.Message);
        Assert.Contains("\"" + f.Root + "\"", e.Message);
        Assert.Contains("Nothing was changed.", e.Message);
    }

    [Fact]
    public async Task FinishedSessionOfAMovedInstallDoesNotStopStartupAndTheProfilePassArchivesTheOldEnrollment()
    {
        using var f = new Fixture();
        f.Start(); f.Capture(f.Coordinator()); f.StopAndRecover();
        Assert.Equal(1000, (await f.Pay()).Roubles);
        f.MoveRoot();

        f.Recover();

        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.True(File.Exists(Path.Combine(SessionFolder(f, f.Lease!.SessionId), "completion.json")));
        var outcome = await f.Pay();
        Assert.True(outcome.Authority!.Recovered, outcome.Authority.Detail);
        Assert.Equal("install-root-changed", outcome.Authority.Trigger);
        Assert.NotNull(RewardEnrollment.Read(f.Root, Fixture.Id));
    }

    [Fact]
    public void CompletedDiscardOfAMovedInstallDoesNotStopStartup()
    {
        using var f = new Fixture();
        f.Start(); f.Capture(f.Coordinator());
        f.Owner!.Dispose(); f.Owner = null;
        Directory.Move(f.Private, f.Private + "-lost");
        f.Recover();
        var discard = Assert.IsType<RewardOfflineDiscard>(RewardOfflineSession.ReadDiscard(f.Root, f.Lease!));
        Assert.True(RewardOfflineSession.DiscardCompleted(f.Root, f.Lease!, discard));
        f.MoveRoot();

        f.Recover();

        Assert.False(RewardOfflineSession.HasActive(f.Root));
    }

    [Theory]
    [InlineData("closure-without-completion")]
    [InlineData("incomplete-discard")]
    [InlineData("no-closure")]
    public void UnfinishedSessionOfAMovedInstallStopsWithAMessageThatNamesTheFolders(string shape)
    {
        using var f = new Fixture();
        if (shape == "no-closure") f.Ready();
        else
        {
            f.Start();
            f.Owner!.Dispose(); f.Owner = null;
            if (shape == "incomplete-discard") File.Delete(Path.Combine(f.Private, RewardOfflineSession.StateFile));
            Assert.Throws<RewardOfflineException>(() => f.Recover(
                s => { if (s == (shape == "incomplete-discard" ? "discard-record" : "closure")) throw new IOException("interrupt"); }));
        }
        File.Delete(RewardOfflineSession.ActivePath(f.Root));
        string id = f.Lease!.SessionId;
        string oldRoot = f.MoveRoot();

        var e = Stopped(f, "offline-history-binding-mismatch");

        AssertNamesBothFolders(e, oldRoot, f, id);
        Assert.Contains("Only that installation can finish this Arena session", e.Message);
        Assert.Contains("offline-sessions\\" + id, e.Message);
        Assert.Contains("lost", e.Message);
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.True(Directory.Exists(SessionFolder(f, id)));
    }

    [Fact]
    public void StaleActivePointerOfAFinishedForeignSessionStopsWithTheDedicatedMessage()
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        byte[] pointer = File.ReadAllBytes(RewardOfflineSession.ActivePath(f.Root));
        f.Recover();
        File.WriteAllBytes(RewardOfflineSession.ActivePath(f.Root), pointer);
        string id = f.Lease!.SessionId;
        string oldRoot = f.MoveRoot();

        var e = Stopped(f, "offline-recovery-lease-mismatch");

        AssertNamesBothFolders(e, oldRoot, f, id);
        Assert.Contains("active Arena session", e.Message);
        Assert.True(RewardOfflineSession.HasActive(f.Root));
    }

    [Fact]
    public void ActivePointerWithAChangedLeaseHashKeepsTheGenericMismatch()
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        var reference = RewardOfflineSession.ReadReference(f.Root, RewardOfflineSession.ActivePath(f.Root));
        f.Recover(); // finished; the pointer below is a stale leftover
        File.WriteAllText(RewardOfflineSession.ActivePath(f.Root), RewardRuntimeFiles.Json(reference with { LeaseHash = new string('0', 64) }));
        var e = Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.Equal("offline-recovery-lease-mismatch", e.InnerException!.Message);
        Assert.Contains("(offline-recovery-lease-mismatch)", e.Message);
    }

    [Fact]
    public void TwoUnclosedSessionsOfTheSameRootStillStopAsBefore()
    {
        using var f = new Fixture();
        f.Ready(); File.Delete(RewardOfflineSession.ActivePath(f.Root));
        string next = Guid.NewGuid().ToString("N");
        f.Grant = f.Grant with { SessionId = next };
        f.Request = f.Request with { SessionId = next, OperationId = Guid.NewGuid().ToString("N") };
        f.Ready(); File.Delete(RewardOfflineSession.ActivePath(f.Root));

        var e = Stopped(f, "offline-multiple-unclosed-sessions");

        Assert.Contains("(offline-multiple-unclosed-sessions)", e.Message);
    }

    [Fact]
    public void UnfinishedSessionOfTheSameRootStillBecomesTheActivePointer()
    {
        using var f = new Fixture();
        f.Ready(); File.Delete(RewardOfflineSession.ActivePath(f.Root));
        Assert.Throws<RewardOfflineException>(() => f.Recover(s => { if (s == "closure") throw new IOException("interrupt"); }));
        Assert.True(RewardOfflineSession.HasActive(f.Root));
        Assert.Equal(f.Lease!.SessionId, RewardOfflineSession.ReadReference(f.Root, RewardOfflineSession.ActivePath(f.Root)).SessionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionFolderNameThatIsNotTheLeaseSessionIdStillStops(bool moved)
    {
        using var f = new Fixture();
        f.Start(); f.StopAndRecover();
        string id = f.Lease!.SessionId;
        if (moved) f.MoveRoot();
        Directory.Move(SessionFolder(f, id), SessionFolder(f, Guid.NewGuid().ToString("N")));

        Stopped(f, "offline-history-binding-mismatch");
    }

    [Fact]
    public void TheGenericWrapCarriesTheReasonOfTheInnerException()
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        var e = Assert.Throws<RewardOfflineException>(() => f.Recover(s => { if (s == "closure") throw new IOException("interrupt"); }));
        Assert.Equal("Offline Arena recovery is incomplete (IOException); normal startup stopped without replacing a profile.", e.Message);
        File.Delete(Path.Combine(SessionFolder(f, f.Lease!.SessionId), "activation.json"));
        File.Delete(Path.Combine(f.Private, RewardOfflineSession.StateFile));
        e = Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.Equal("Offline Arena recovery is incomplete (offline-activation-proof-missing); normal startup stopped without replacing a profile.", e.Message);
    }
}
