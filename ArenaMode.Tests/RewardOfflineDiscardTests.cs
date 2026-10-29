using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

public sealed partial class RewardOfflineSessionTests
{
    [Theory]
    [InlineData("root")]
    [InlineData("marker")]
    [InlineData("marker-json")]
    [InlineData("lease")]
    [InlineData("state")]
    public async Task LostDisposableStateRetiresWithoutTouchingTheNormalProfile(string lost)
    {
        using var f = new Fixture();
        f.Start();
        f.Capture(f.Coordinator());
        f.Owner!.Dispose(); f.Owner = null;
        string before = f.Profile;
        if (lost == "root") Directory.Move(f.Private, f.Private + "-lost");
        else if (lost == "marker-json") File.WriteAllText(Path.Combine(f.Private, "arena-session.json"), "{");
        else File.Delete(Path.Combine(f.Private, lost == "marker" ? "arena-session.json"
            : lost == "lease" ? RewardOfflineSession.LeaseFile : RewardOfflineSession.StateFile));
        f.Recover();
        Assert.Equal(before, f.Profile);
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        var discard = Assert.IsType<RewardOfflineDiscard>(RewardOfflineSession.ReadDiscard(f.Root, f.Lease!));
        Assert.True(RewardOfflineSession.DiscardCompleted(f.Root, f.Lease!, discard));
        Assert.False(File.Exists(Path.Combine(RewardOfflineSession.SessionDirectory(f.Root, f.Lease!.SessionId), "closed.json")));
        Assert.False(File.Exists(Path.Combine(RewardOfflineSession.SessionDirectory(f.Root, f.Lease.SessionId), "completion.json")));
        Assert.Equal("rewards-not-enabled", (await f.Pay()).Result.Reason);
        f.Recover();
        Assert.Equal(before, f.Profile);
        Assert.ThrowsAny<Exception>(() => f.Ready());
    }

    [Theory]
    [InlineData("discard-record", false)]
    [InlineData("discard-record", true)]
    [InlineData("discard-consent", false)]
    [InlineData("discard-consent", true)]
    [InlineData("discard-completion", false)]
    [InlineData("discard-completion", true)]
    [InlineData("discard-retired", false)]
    public void InterruptedDiscardAndLostPointerFinishOnceWithoutRestoringOldOn(string stage, bool losePointer)
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        File.Delete(Path.Combine(f.Private, RewardOfflineSession.StateFile));
        string before = f.Profile;
        Assert.Throws<RewardOfflineException>(() => f.Recover(s => { if (s == stage) throw new IOException("interrupt"); }));
        if (losePointer) File.Delete(RewardOfflineSession.ActivePath(f.Root));
        f.Recover();
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.Equal(before, f.Profile);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        var enabled = RewardEnrollment.ReadConsent(f.Root, Fixture.Id);
        f.Recover();
        Assert.Equal(enabled, RewardEnrollment.ReadConsent(f.Root, Fixture.Id));
    }

    [Fact]
    public async Task LateDiscardedReceiptCannotPayOrBlockANewHealthySession()
    {
        using var f = new Fixture();
        f.Start();
        var oldReport = f.Capture(f.Coordinator());
        var oldLease = f.Lease!;
        byte[] receipt = File.ReadAllBytes(Path.Combine(f.Private, "user", "arena-rewards-outbox", oldReport.RunId + ".json"));
        f.Owner!.Dispose(); f.Owner = null;
        File.Delete(Path.Combine(f.Private, RewardOfflineSession.StateFile));
        f.Recover();
        string before = f.Profile;
        Directory.CreateDirectory(f.Metadata("inbox"));
        File.WriteAllBytes(f.Metadata("inbox/" + oldReport.RunId + ".json"), receipt);
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(f.Metadata("inbox/" + oldReport.RunId + ".json"));
        Assert.Throws<RewardOfflineDiscardedException>(() => RewardOfflineSession.RequireClosedReceipt(f.Root, f.Metadata(""), oldLease.Authority!, signed, oldReport.RunId));
        Assert.Equal("rewards-not-enabled", (await f.Pay()).Result.Reason);
        Assert.Equal(before, f.Profile);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        Directory.Move(f.Private, f.Private + "-discarded");
        string session = Guid.NewGuid().ToString("N");
        f.Grant = f.Grant with { SessionId = session };
        f.Request = f.Request with { SessionId = session, OperationId = Guid.NewGuid().ToString("N") };
        f.Start();
        f.Capture(f.Coordinator());
        f.StopAndRecover();
        Assert.Equal(1000, (await f.Pay()).Roubles);
        string paid = f.Profile;
        Assert.Single(RewardProfileDocument.ReadLedger(paid, f.Original.Binding, new()));
        Assert.False(File.Exists(f.Metadata("allocations/" + oldReport.RunId + ".json")));
        Assert.False(File.Exists(f.Metadata("imported/" + oldReport.RunId + ".json")));
        Assert.Equal(receipt, File.ReadAllBytes(f.Metadata("inbox/" + oldReport.RunId + ".json")));
        await f.Pay();
        Assert.Equal(paid, f.Profile);
    }

    [Fact]
    public void MissingTreeCannotWaiveALivePrivateProcessEvenAfterHandleRelease()
    {
        using var f = new Fixture();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        f.Process = new(process.Id, process.StartTime.ToUniversalTime(), []);
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        Directory.Move(f.Private, f.Private + "-missing");
        Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.Null(RewardOfflineSession.ReadDiscard(f.Root, f.Lease!));
        Assert.True(RewardOfflineSession.HasActive(f.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCandidateOrApprovalCannotBypassDiscardWithoutAnInboxReceipt(bool approvalOnly)
    {
        using var f = new Fixture();
        f.Start();
        var report = f.Capture(f.Coordinator());
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(Path.Combine(f.Private, "user", "arena-rewards-outbox", report.RunId + ".json"));
        var earned = RewardRuntimeFiles.Verify(f.Lease!.Authority!, signed);
        f.Owner!.Dispose(); f.Owner = null;
        // Retained archived evidence can reappear independently of the inbox.
        RewardRestorationCheckpoint.ArchivePrivateEvidence(f.Private, f.Root, Fixture.Id);
        File.Delete(Path.Combine(f.Private, RewardOfflineSession.StateFile));
        f.Recover();
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        string before = f.Profile;
        var candidate = earned.Evidence with {
            Authorization = earned.Evidence.Authorization with { CleanProfileRevision = RewardIdentity.Hash(before), Sequence = 1 },
            Restoration = earned.Evidence.Restoration with { ExpectedProfileRevision = RewardIdentity.Hash(before), PersistedProfileRevision = RewardIdentity.Hash(before) }
        };
        var claim = new RewardClaim(f.Original.Binding, report.RunId, 1, 1000, candidate.Authorization.Policy.Version, 100);
        var approval = new RewardStartupApproval(1, RewardStartupApprovalState.RestoredAndApproved, claim, RewardIdentity.Hash(before));
        if (approvalOnly) File.WriteAllText(f.Metadata("approved.json"), RewardRuntimeFiles.Json(approval));
        else File.WriteAllText(f.Metadata("candidate.json"), RewardRuntimeFiles.Json(candidate));
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, (await f.Pay()).Result.Status);
        Assert.Equal(before, f.Profile);
        using var window = new RewardStartupWindow(f.Root, () => true);
        var enrollment = RewardEnrollment.Read(f.Root, Fixture.Id);
        if (!approvalOnly) Assert.Throws<RewardDataException>(() => RewardApprovalProducer.PrepareIfPresent(window, enrollment));
        var store = new RewardStartupStore(window, enrollment, approval, new());
        await Assert.ThrowsAsync<RewardDataException>(async () => await store.TryAcquireAsync(claim.Binding, CancellationToken.None));
    }

    [Fact]
    public void TemporarilyLockedStateIsRetainedForRetry()
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        using (var held = new FileStream(Path.Combine(f.Private, RewardOfflineSession.StateFile), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<RewardOfflineException>(() => f.Recover());
            Assert.Null(RewardOfflineSession.ReadDiscard(f.Root, f.Lease!));
        }
        f.Recover();
        Assert.True(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
    }

    [Fact]
    public void LinkedPrivateStateCannotAuthorizeDiscard()
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        string state = Path.Combine(f.Private, RewardOfflineSession.StateFile);
        File.Move(state, state + ".real");
        File.CreateSymbolicLink(state, state + ".real");
        Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.Null(RewardOfflineSession.ReadDiscard(f.Root, f.Lease!));
    }

    [Fact]
    public void MissingRootIsNotMisreportedAsALink()
    {
        using var f = new Fixture();
        var exception = Assert.ThrowsAny<IOException>(() => RewardStartupFiles.RequireLocalPath(f.Private, f.Private));
        Assert.True(exception is FileNotFoundException or DirectoryNotFoundException);
    }

    [Fact]
    public void MissingRootBelowALinkStillRefusesTheLink()
    {
        using var f = new Fixture();
        Directory.CreateSymbolicLink(f.Private, f.Root);
        string missing = Path.Combine(f.Private, "missing-child");
        var error = Assert.Throws<RewardDataException>(() => RewardStartupFiles.RequireLocalPath(missing, missing));
        Assert.Equal("reward-root-is-linked", error.Message);
    }
}
