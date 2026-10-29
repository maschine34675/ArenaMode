using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvenLateRestorationKeepsCompletedDiscardAndAllowsNewRewardsOnlyOnce(bool lostMarker)
    {
        using var f = new Fixture();
        var oldRun = f.CaptureAndPublish(() => f.QueueAndClose());
        if (lostMarker)
            f.Edit(p =>
            {
                p[RewardProfileDocument.ExtensionKey]!.AsObject().Remove("recoveries");
                p["foreignMod"]!["progress"] = 927;
            });
        var before = f.Profile;
        var originals = f.SourceBytes();
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, (await f.Process()).Result.Status);
        if (lostMarker) AssertOnlyMarkerAdded(before, f.Profile);
        else Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
        Assert.False(File.Exists(f.PathOf("allocations/" + oldRun + ".json")));
        Assert.False(File.Exists(f.PathOf("imported/" + oldRun + ".json")));
        f.AssertSources(originals);
        var newRun = f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.StartsWith(newRun + ":", Assert.Single(f.Ledger).ReceiptKey);
        var paid = f.Profile;
        Assert.NotEqual(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(paid, f.Profile);
        Assert.Single(f.Ledger);
        if (lostMarker) Assert.Equal(927, JsonNode.Parse(f.Profile)!["foreignMod"]!["progress"]!.GetValue<int>());
        f.AssertSources(originals.Where(p => p.Key.Contains("arena-reward-checkpoints")
            || p.Key.Contains("arena-reward-snapshots") || p.Key.Contains("arena-reward-runs")
            || Path.GetFileName(p.Key) == oldRun + ".json").ToDictionary(p => p.Key, p => p.Value));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("readback")]
    [InlineData("completion")]
    public async Task ProvenLateRestorationReacknowledgementResumesAfterInterruptedCommit(string boundary)
    {
        using var f = new Fixture();
        var oldRun = f.CaptureAndPublish(() => f.QueueAndClose());
        f.Edit(p => p[RewardProfileDocument.ExtensionKey]!.AsObject().Remove("recoveries"));
        RewardRecovery.Checkpoint = step => { if (step == boundary) throw new IOException("interrupted reacknowledgement"); };
        try { Assert.Equal("startup-storage-unavailable", (await f.Process()).Result.Reason); }
        finally { RewardRecovery.Checkpoint = null; }
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, (await f.Process()).Result.Status);
        Assert.Empty(f.Ledger);
        var newRun = f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.StartsWith(newRun + ":", Assert.Single(f.Ledger).ReceiptKey);
        var paid = f.Profile;
        Assert.NotEqual(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(paid, f.Profile);
        Assert.DoesNotContain(f.Ledger, p => p.ReceiptKey.StartsWith(oldRun + ":", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing-proof")]
    [InlineData("changed-proof")]
    [InlineData("changed-original-checkpoint")]
    [InlineData("changed-arm")]
    [InlineData("unexpected-run-field")]
    [InlineData("unsupported-run-state")]
    [InlineData("unsupported-snapshot-state")]
    [InlineData("lost-marker-invalid-completion")]
    public async Task CompletedDiscardDoesNotAcceptUnprovenOrChangedTerminalEvidence(string corruption)
    {
        using var f = new Fixture();
        var run = f.CaptureAndPublish(() => f.QueueAndClose());
        var checkpoint = Assert.Single(Directory.GetDirectories(Path.Combine(f.Root, "user", "arena-reward-checkpoints", f.Id)));
        var generation = Path.GetFileName(checkpoint);
        switch (corruption)
        {
            case "missing-proof": File.Delete(Path.Combine(checkpoint, "persisted.json")); break;
            case "changed-proof":
                File.WriteAllText(Path.Combine(checkpoint, "persisted.json"), "{}"); break;
            case "changed-original-checkpoint":
                File.AppendAllText(Path.Combine(checkpoint, "checkpoint.json"), " "); break;
            case "changed-arm":
            case "unexpected-run-field":
            case "unsupported-run-state":
                var terminal = JsonNode.Parse(File.ReadAllText(f.RunPath(run)))!;
                if (corruption == "changed-arm") terminal["armToken"] = Guid.NewGuid().ToString("N");
                if (corruption == "unexpected-run-field") terminal["future"] = true;
                if (corruption == "unsupported-run-state") terminal["state"] = "discarded";
                File.WriteAllText(f.RunPath(run), terminal.ToJsonString());
                break;
            case "unsupported-snapshot-state":
                f.WriteAt(f.SnapshotPath(generation), new RewardSnapshotGuard(1, generation, "cancelled-before-start")); break;
            case "lost-marker-invalid-completion":
                f.Edit(p => p[RewardProfileDocument.ExtensionKey]!.AsObject().Remove("recoveries"));
                var decision = Assert.Single(Directory.GetDirectories(f.PathOf("recovery")));
                var completion = JsonNode.Parse(File.ReadAllText(Path.Combine(decision, "completion.json")))!;
                completion["planFingerprint"] = new string('a', 64);
                File.WriteAllText(Path.Combine(decision, "completion.json"), completion.ToJsonString());
                break;
        }
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
        Assert.False(File.Exists(f.PathOf("allocations/" + run + ".json")));
    }

    // Authority recovery (docs/REWARDS-RUNTIME.md, "Lost reward authority") archives only a settled history. A
    // receipt, run or restoration that a recovery closure ended WITHOUT payment is settled although the
    // settlement code leaves its files unmarked on purpose.
    [Fact]
    public async Task ARetainedReceiptThatARecoveryClosureEndedWithoutPaymentCountsAsSettled()
    {
        using var f = new Fixture();
        var run = f.CaptureAndPublish(() => f.QueueAndClose());
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, (await f.Process()).Result.Status);
        string receipt = f.PathOf("inbox/" + run + ".json"), directory = Path.GetDirectoryName(f.PathOf("enrollment.json"))!;
        Assert.True(File.Exists(receipt));
        Assert.False(File.Exists(f.PathOf("imported/" + run + ".json")));

        var closures = RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile);
        Assert.True(RewardAuthorityRecovery.ClosedWithoutPayment(f.Root, directory, "inbox", receipt, closures));
        // Without that closure the very same file is an unsettled received reward.
        Assert.False(RewardAuthorityRecovery.ClosedWithoutPayment(f.Root, directory, "inbox", receipt, RewardRecoveryClosureSet.Empty));
        // A signature that does not verify is never trusted for "closed".
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(receipt, RewardRuntimeFiles.MaxReceiptBytes);
        string forged = Path.Combine(f.Root, "forged.json");
        File.WriteAllText(forged, RewardRuntimeFiles.Json(signed with { Signature = new string('a', 64) }));
        Assert.Throws<RewardDataException>(() => RewardAuthorityRecovery.ClosedWithoutPayment(f.Root, directory, "inbox", forged, closures));
    }

    [Fact]
    public async Task AutomaticallyDiscardedRunsDoNotKeepAMovedInstallInQuarantine()
    {
        using var f = new Fixture();
        f.LegacyPending();
        Assert.NotEqual(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        var old = f.Binding;
        f.Write("enrollment.json", f.Read<RewardStartupEnrollment>("enrollment.json")
            with { RuntimeRoot = Path.Combine(Path.GetTempPath(), "ArenaMode.Moved.From", Guid.NewGuid().ToString("N")) });

        var outcome = await f.Process();

        Assert.True(outcome.Authority!.Recovered, outcome.Authority.Detail);
        Assert.NotEqual(old.Epoch, f.Binding.Epoch);
        Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(f.PathOf("enrollment.json")))!, "*.archived-*"));
    }

    [Fact]
    public async Task StartupDiscardsUnprovenOldRewardsWithoutPlayerActionAndNewRewardsPay()
    {
        using var f = new Fixture();
        var old = f.LegacyPending();
        f.Edit(p => p["foreignMod"]!["progress"] = 731);
        var before = f.Profile;
        var source = f.SourceBytes();
        Assert.False(Directory.Exists(f.PathOf("recovery")));
        Assert.NotEqual(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        AssertOnlyMarkerAdded(before, f.Profile);
        f.AssertSources(source);
        var closures = RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile);
        Assert.True(closures.ClosesSnapshot(old.Generation));
        Assert.True(closures.ClosesRun(old.Run, old.Arm));
        Assert.Empty(f.Ledger);
        var discarded = f.Profile;
        Assert.NotEqual(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(discarded, f.Profile);
        f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Single(f.Ledger);
        Assert.Equal(731, JsonNode.Parse(f.Profile)!["foreignMod"]!["progress"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("profile")]
    public async Task AutomaticDiscardResumesAfterCrashAndInterveningNormalProgress(string boundary)
    {
        using var f = new Fixture();
        var old = f.LegacyPending();
        RewardRecovery.Checkpoint = step => { if (step == boundary) throw new IOException("interrupted startup"); };
        try { Assert.Equal("startup-storage-unavailable", (await f.Process()).Result.Reason); }
        finally { RewardRecovery.Checkpoint = null; }
        f.Edit(p => p["foreignMod"]!["progress"] = 823);
        var before = f.Profile;
        Assert.NotEqual(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        AssertOnlyMarkerAdded(before, f.Profile);
        Assert.True(RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile).ClosesSnapshot(old.Generation));
        Assert.Equal(823, JsonNode.Parse(f.Profile)!["foreignMod"]!["progress"]!.GetValue<int>());
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public async Task AutomaticDiscardReacknowledgesOldDecisionAfterProfileBackupRollback()
    {
        using var f = new Fixture();
        var old = f.LegacyPending();
        var before = f.Profile;
        Assert.NotEqual(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        File.WriteAllText(f.ProfilePath, before);
        f.Edit(p => p["foreignMod"]!["progress"] = 919);
        var current = f.Profile;
        Assert.NotEqual(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        AssertOnlyMarkerAdded(current, f.Profile);
        Assert.True(RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile).ClosesSnapshot(old.Generation));
        Assert.Empty(f.Ledger);
    }

    [Theory]
    [InlineData("filename")]
    [InlineData("signature")]
    public async Task CompleteTransportPreflightPreventsPublicationAndPartialBatchPayment(string corruption)
    {
        using var f = new Fixture();
        f.CaptureAndPublish();
        var second = f.CaptureAndPublish();
        var path = f.PathOf("inbox/" + second + ".json");
        if (corruption == "filename") File.Move(path, f.PathOf("inbox/ffffffffffffffffffffffffffffffff.json"));
        else f.Write("inbox/" + second + ".json", RewardStartupFiles.Read<RewardSignedReceipt>(path) with { Signature = new string('a', 64) });
        var profile = f.Profile;
        var originals = f.SourceBytes();
        var outcome = await f.Process();
        Assert.Equal(RewardSettlementStatus.Quarantined, outcome.Result.Status);
        Assert.Equal(profile, f.Profile);
        Assert.Empty(f.Ledger);
        f.AssertSources(originals);
        if (corruption == "filename") Assert.False(File.Exists(path));
        bool settled = false;
        await Assert.ThrowsAsync<RewardDataException>(() => RewardRuntimeInbox.ProcessAsync(f.Window,
            RewardEnrollment.Read(f.Root, f.Id), CancellationToken.None, () =>
            {
                settled = true;
                throw new InvalidOperationException("Corrupt batch reached settlement.");
            }));
        Assert.False(settled);
        Assert.Equal(profile, f.Profile);
        Assert.False(Directory.Exists(f.PathOf("allocations")));
        Assert.False(Directory.Exists(f.PathOf("imported")));
    }

    [Fact]
    public async Task LegacyPendingCasesRequireExactReviewKeepProgressAndAllowNextReward()
    {
        using var f = new Fixture();
        var old = f.LegacyPending();
        var before = f.Profile;
        var sources = f.SourceBytes();
        var review = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(review.CanClose, review.Detail);
        Assert.Equal(new[] { "run", "snapshot" }, review.Cases.Select(c => c.Kind).Order());
        Assert.All(review.Cases, c => Assert.False(c.AmountKnown));
        Assert.Matches("^[a-f0-9]{64}$", review.ReviewId);
        Assert.Equal(before, f.Profile);
        Assert.False(Directory.Exists(f.PathOf("recovery")));
        f.Edit(p => p["foreignMod"]!["progress"] = 987);
        f.Edit(p => p["characters"]!["pmc"]!["Info"]!["Experience"] = 180_000);
        Assert.Equal(review.ReviewId, RewardRecovery.Preview(f.Root, f.Id).ReviewId);
        Assert.True(RewardRecovery.Request(f.Root, f.Id, review.ReviewId).Accepted);
        var requested = f.Profile;
        Assert.True(RewardRecovery.Request(f.Root, f.Id, review.ReviewId).Accepted);
        Assert.Equal(requested, f.Profile);
        Assert.True(RewardRecovery.Preview(f.Root, f.Id).Queued);
        RewardRecovery.ProcessAtStartup(f.Window, f.Id);
        AssertOnlyMarkerAdded(requested, f.Profile);
        f.AssertSources(sources);
        var closures = RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile);
        Assert.True(closures.ClosesSnapshot(old.Generation));
        Assert.True(closures.ClosesRun(old.Run, old.Arm));
        Assert.Empty(f.Ledger);
        Assert.False(RewardRecovery.Preview(f.Root, f.Id).Available);
        f.CaptureAndPublish();
        var paid = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, paid.Result.Status);
        Assert.Single(f.Ledger);
        Assert.Equal(180_100, JsonNode.Parse(f.Profile)!["characters"]!["pmc"]!["Info"]!["Experience"]!.GetValue<long>());
        Assert.Equal(987, JsonNode.Parse(f.Profile)!["foreignMod"]!["progress"]!.GetValue<int>());
    }

    [Fact]
    public async Task CommittedPaymentMissingFromLedgerIsAutomaticallyDiscardedAndNextResultPays()
    {
        using var f = new Fixture();
        var original = f.Profile;
        f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        var claim = f.Read<RewardStartupApproval>("approved.json").Claim;
        var journal = f.Read<RewardJournal>("journal.json");
        File.WriteAllText(f.ProfilePath, original);
        f.Edit(p => p["foreignMod"]!["progress"] = 444);
        var source = f.SourceBytes();
        var review = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(review.CanClose, review.Detail);
        var item = Assert.Single(review.Cases);
        Assert.Equal("payment", item.Kind);
        Assert.Equal(1000, item.Roubles);
        Assert.Equal(100, item.Experience);
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, (await f.Process()).Result.Status);
        f.AssertSources(source);
        Assert.Empty(f.Ledger);
        var profile = f.Profile;
        var closed = await f.Process();
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, closed.Result.Status);
        Assert.Equal(0, closed.Roubles);
        Assert.Equal(profile, f.Profile);
        Assert.Equal(0, f.Coordinator.GetStatus(f.Id).LastPaidRoubles);
        Assert.Equal(0, f.Coordinator.GetStatus(f.Id).LastPaidExperience);
        var approval = f.Read<RewardStartupApproval>("approved.json");
        var store = new RewardStartupStore(f.Window, RewardEnrollment.Read(f.Root, f.Id), approval, new());
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, (await new RewardSettlementCore(store).SettleAsync(claim)).Status);
        Assert.Equal(profile, f.Profile);
        f.CaptureAndPublish();
        var next = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, next.Result.Status);
        Assert.Single(f.Ledger);
        Assert.NotEqual(claim.Key, f.Ledger[0].ReceiptKey);
        Assert.Equal(journal.Entries[0], f.Read<RewardJournal>("journal.json").Entries[0]);
        Assert.Equal(2, f.Read<RewardJournal>("journal.json").Entries.Count);
        Assert.Equal(444, JsonNode.Parse(f.Profile)!["foreignMod"]!["progress"]!.GetValue<int>());
    }

    [Fact]
    public async Task AutomaticDiscardDoesNotChangeRewardsOff()
    {
        using var f = new Fixture();
        f.LegacyPending();
        RewardEnrollment.SetConsent(f.Root, f.Id, false);
        var consent = File.ReadAllBytes(f.PathOf("consent.json"));
        var handoff = File.ReadAllBytes(f.PathOf("handoff.json"));
        var review = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(review.CanClose, review.Detail);
        Assert.Equal("rewards-not-enabled", (await f.Process()).Result.Reason);
        Assert.Single(RewardProfileDocument.ReadRecoveryMarkers(f.Profile, f.Binding));
        Assert.Equal(consent, File.ReadAllBytes(f.PathOf("consent.json")));
        Assert.Equal(handoff, File.ReadAllBytes(f.PathOf("handoff.json")));
        Assert.False(RewardEnrollment.WantsRewards(f.Root, f.Id));
        Assert.Empty(f.Ledger);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("backup")]
    [InlineData("plan")]
    [InlineData("profile")]
    [InlineData("readback")]
    [InlineData("completion")]
    public async Task EveryDurableBoundaryRecoversIdempotentlyWithoutGameplayEdits(string point)
    {
        using var f = new Fixture();
        f.LegacyPending();
        var before = f.Profile;
        var sources = f.SourceBytes();
        var review = RewardRecovery.Preview(f.Root, f.Id);
        bool hit = false;
        RewardRecovery.Checkpoint = step => { if (!hit && step == point) { hit = true; throw new IOException("injected crash"); } };
        try
        {
            Assert.Throws<IOException>(() => RewardRecovery.DiscardUncertainAtStartup(f.Window, f.Id));
        }
        finally { RewardRecovery.Checkpoint = null; }
        Assert.True(hit);
        // The full pipeline includes checkpoint-before-recovery ordering.
        Assert.DoesNotContain((await f.Process()).Result.Status, new[] { RewardSettlementStatus.Paid, RewardSettlementStatus.Quarantined });
        AssertOnlyMarkerAdded(before, f.Profile);
        Assert.Single(RewardProfileDocument.ReadRecoveryMarkers(f.Profile, f.Binding));
        var after = f.Profile;
        RewardRecovery.ProcessAtStartup(f.Window, f.Id);
        Assert.Equal(after, f.Profile);
        f.AssertSources(sources);
        Assert.Equal("closed-without-payment", Assert.Single(RewardRecovery.ReadAttempts(f.Root, f.Id)).Completion!.State);
    }

    [Fact]
    public void ProfileProgressAfterMarkerWriteIsNeverRolledBackDuringAcknowledgementRetry()
    {
        using var f = new Fixture();
        f.LegacyPending();
        f.Queue();
        RewardRecovery.Checkpoint = step => { if (step == "profile") throw new IOException("crash"); };
        try { Assert.Throws<IOException>(() => RewardRecovery.ProcessAtStartup(f.Window, f.Id)); }
        finally { RewardRecovery.Checkpoint = null; }
        f.Edit(p => p["foreignMod"]!["progress"] = 991);
        var changed = f.Profile;
        RewardRecovery.ProcessAtStartup(f.Window, f.Id);
        Assert.Equal(changed, f.Profile);
        Assert.Single(RewardProfileDocument.ReadRecoveryMarkers(f.Profile, f.Binding));
    }

    [Fact]
    public void CompletedDecisionWithLostProfileMarkerRequiresNewExplicitReview()
    {
        using var f = new Fixture();
        var legacy = f.LegacyPending();
        var before = f.Profile;
        var initial = f.QueueAndClose();
        File.WriteAllText(f.ProfilePath, before);
        RewardRecovery.ProcessAtStartup(f.Window, f.Id);
        Assert.Equal(before, f.Profile);
        Assert.Throws<RewardDataException>(() => RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile));
        var second = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(second.CanClose, second.Detail);
        Assert.NotEqual(initial, second.ReviewId);
        Assert.False(RewardRecovery.Request(f.Root, f.Id, initial).Accepted);
        f.QueueAndClose(second.ReviewId);
        Assert.Equal(2, RewardRecovery.ReadAttempts(f.Root, f.Id).Count);
        Assert.True(RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile).ClosesSnapshot(legacy.Generation));
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public async Task ReturnedPaidLedgerWithoutClosureMarkerCanBeExplicitlyReacknowledged()
    {
        using var f = new Fixture();
        var empty = f.Profile;
        f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        var paidProfile = f.Profile;
        File.WriteAllText(f.ProfilePath, empty);
        var oldReview = f.QueueAndClose();
        File.WriteAllText(f.ProfilePath, paidProfile);
        Assert.Throws<RewardDataException>(() => RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile));
        var review = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(review.CanClose, review.Detail);
        Assert.Equal("payment", Assert.Single(review.Cases).Kind);
        Assert.NotEqual(oldReview, review.ReviewId);
        f.QueueAndClose(review.ReviewId);
        AssertOnlyMarkerAdded(paidProfile, f.Profile);
        Assert.Single(f.Ledger);
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, (await f.Process()).Result.Status);
        f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(2, f.Ledger.Count);
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("plan")]
    public void ProgressAfterUncommittedPlanRequiresFreshReviewWithoutStrandingOrRollback(string boundary)
    {
        using var f = new Fixture();
        f.LegacyPending();
        var oldReview = f.Queue();
        RewardRecovery.Checkpoint = step => { if (step == boundary) throw new IOException("crash"); };
        try { Assert.Throws<IOException>(() => RewardRecovery.ProcessAtStartup(f.Window, f.Id)); }
        finally { RewardRecovery.Checkpoint = null; }
        f.Edit(p => p["foreignMod"]!["progress"] = 888);
        var progressed = f.Profile;
        RewardRecovery.ProcessAtStartup(f.Window, f.Id);
        Assert.Equal(progressed, f.Profile);
        Assert.Equal("stale", Assert.Single(RewardRecovery.ReadAttempts(f.Root, f.Id)).Completion!.State);
        var next = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(next.CanClose, next.Detail);
        Assert.NotEqual(oldReview, next.ReviewId);
        f.QueueAndClose(next.ReviewId);
        AssertOnlyMarkerAdded(progressed, f.Profile);
    }

    [Fact]
    public async Task PaymentClosureDoesNotExemptChangedPaymentAmountOrAnotherMissingEntry()
    {
        using var f = new Fixture();
        var empty = f.Profile;
        f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        File.WriteAllText(f.ProfilePath, empty);
        f.QueueAndClose();
        var journal = f.Read<RewardJournal>("journal.json");
        var original = journal.Entries[0];
        f.Write("journal.json", journal with { Entries = [original with { Payment = original.Payment with { Roubles = original.Payment.Roubles + 1 } }] });
        var before = f.Profile;
        Assert.False(RewardRecovery.Preview(f.Root, f.Id).CanClose);
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public void NewCheckpointEvidenceChangesReviewAndIsPreservedByClosure()
    {
        using var f = new Fixture();
        var legacy = f.LegacyPending();
        var review = RewardRecovery.Preview(f.Root, f.Id);
        var handoff = f.Read<RewardHandoff>("handoff.json");
        RewardRestorationCheckpoint.Create(f.Root, f.Id, legacy.Generation, handoff, f.Profile);
        var updated = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(updated.CanClose, updated.Detail);
        Assert.NotEqual(review.ReviewId, updated.ReviewId);
        Assert.False(RewardRecovery.Request(f.Root, f.Id, review.ReviewId).Accepted);
        var originals = f.SourceBytes();
        f.QueueAndClose(updated.ReviewId);
        f.AssertSources(originals);
        Assert.True(RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile).ClosesSnapshot(legacy.Generation));
    }

    [Theory]
    [InlineData("reward")]
    [InlineData("no-final")]
    [InlineData("half-published")]
    public async Task PositiveDurableCheckpointIsRecoveredAutomaticallyRatherThanOfferedForWaiver(string stage)
    {
        using var f = new Fixture();
        var run = f.CaptureAndPublish();
        var inbox = f.PathOf("inbox/" + run + ".json");
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(inbox);
        var earned = RewardRuntimeFiles.Verify(f.Read<RewardHandoff>("handoff.json"), signed);
        var authorization = earned.Evidence.Authorization;
        var checkpoint = RewardRestorationCheckpoint.DirectoryFor(f.Root, f.Id, authorization.RestorationId);
        File.Delete(inbox);
        File.Delete(Path.Combine(checkpoint, "complete.json"));
        if (stage != "reward") File.Delete(Path.Combine(checkpoint, "final.json"));
        f.WriteAt(f.SnapshotPath(authorization.RestorationId), new RewardSnapshotGuard(1, authorization.RestorationId,
            stage == "half-published" ? "restored" : "pending"));
        f.WriteAt(f.RunPath(run), new RewardRuntimePending(1, authorization.ArmToken, run));
        var originals = f.SourceBytes();
        var review = RewardRecovery.Preview(f.Root, f.Id);
        Assert.False(review.CanClose, review.Detail);
        Assert.Empty(review.Cases);
        f.AssertSources(originals);
        Assert.False(Directory.Exists(f.PathOf("recovery")));
        var outcome = await f.Process();
        Assert.Equal(stage == "reward" ? RewardSettlementStatus.Paid : RewardSettlementStatus.Deferred, outcome.Result.Status);
        Assert.Equal(stage == "reward" ? 1 : 0, f.Ledger.Count);
        Assert.Empty(RewardProfileDocument.ReadRecoveryMarkers(f.Profile, f.Binding));
        Assert.Equal("restored", RewardStartupFiles.Read<RewardSnapshotGuard>(f.SnapshotPath(authorization.RestorationId)).State);
        Assert.True(JsonNode.Parse(File.ReadAllText(f.RunPath(run)))!["state"] is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterDurableProofSupersedesOnlyAnUnappliedDiscard(bool markerApplied)
    {
        using var f = new Fixture();
        var run = f.CaptureAndPublish();
        var inbox = f.PathOf("inbox/" + run + ".json");
        var receipt = RewardRuntimeFiles.Verify(f.Read<RewardHandoff>("handoff.json"),
            RewardStartupFiles.Read<RewardSignedReceipt>(inbox));
        var authorization = receipt.Evidence.Authorization;
        var directory = RewardRestorationCheckpoint.DirectoryFor(f.Root, f.Id, authorization.RestorationId);
        var proofPath = Path.Combine(directory, "persisted.json");
        var proof = File.ReadAllBytes(proofPath);
        File.Delete(proofPath);
        File.Delete(inbox);
        File.Delete(Path.Combine(directory, "complete.json"));
        f.WriteAt(f.SnapshotPath(authorization.RestorationId), new RewardSnapshotGuard(1, authorization.RestorationId, "pending"));
        f.WriteAt(f.RunPath(run), new RewardRuntimePending(1, authorization.ArmToken, run));
        f.Queue();
        if (markerApplied)
        {
            RewardRecovery.Checkpoint = step => { if (step == "profile") throw new IOException("interrupted acknowledgement"); };
            try { Assert.Throws<IOException>(() => RewardRecovery.ProcessAtStartup(f.Window, f.Id)); }
            finally { RewardRecovery.Checkpoint = null; }
        }
        var guard = File.ReadAllBytes(f.SnapshotPath(authorization.RestorationId));
        File.WriteAllBytes(proofPath, proof);
        var result = await f.Process();
        if (markerApplied)
        {
            Assert.NotEqual(RewardSettlementStatus.Quarantined, result.Result.Status);
            Assert.Empty(f.Ledger);
            Assert.False(File.Exists(inbox));
            Assert.Equal(guard, File.ReadAllBytes(f.SnapshotPath(authorization.RestorationId)));
            Assert.True(RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile).ClosesSnapshot(authorization.RestorationId));
        }
        else
        {
            Assert.Equal(RewardSettlementStatus.Paid, result.Result.Status);
            Assert.Single(f.Ledger);
            Assert.Equal("stale", Assert.Single(RewardRecovery.ReadAttempts(f.Root, f.Id)).Completion!.State);
            Assert.Empty(RewardProfileDocument.ReadRecoveryMarkers(f.Profile, f.Binding));
        }
    }

    [Fact]
    public void ChangedConflictMakesQueuedReviewStaleAndRequiresAnotherConfirmation()
    {
        using var f = new Fixture();
        var first = f.LegacyPending();
        var review = f.Queue();
        var before = f.Profile;
        f.LegacyPending();
        RewardRecovery.ProcessAtStartup(f.Window, f.Id);
        Assert.Equal(before, f.Profile);
        Assert.Equal("stale", Assert.Single(RewardRecovery.ReadAttempts(f.Root, f.Id)).Completion!.State);
        Assert.False(RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile).ClosesSnapshot(first.Generation));
        Assert.NotEqual(review, RewardRecovery.Preview(f.Root, f.Id).ReviewId);
    }

    [Fact]
    public async Task ChangedOriginalEvidenceFailsClosedAndCannotBeOfferedAsAnotherWaiver()
    {
        using var f = new Fixture();
        var old = f.LegacyPending();
        f.QueueAndClose();
        File.AppendAllText(f.SnapshotPath(old.Generation), " ");
        var before = f.Profile;
        Assert.False(RewardRecovery.Preview(f.Root, f.Id).CanClose);
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public async Task AnotherPendingGenerationGetsItsOwnAutomaticDiscardDecision()
    {
        using var f = new Fixture();
        var first = f.LegacyPending();
        f.QueueAndClose();
        var second = f.LegacyPending();
        var result = await f.Process();
        Assert.NotEqual(RewardSettlementStatus.Quarantined, result.Result.Status);
        Assert.Equal(2, RewardRecovery.ReadAttempts(f.Root, f.Id).Count);
        var closures = RewardRecovery.ReadClosures(f.Root, f.Id, f.Profile);
        Assert.True(closures.ClosesSnapshot(first.Generation));
        Assert.True(closures.ClosesSnapshot(second.Generation));
        Assert.Empty(f.Ledger);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("run")]
    public async Task LateSignedReceiptForClosedOperationNeverAllocatesOrPays(string match)
    {
        using var f = new Fixture();
        var legacy = f.LegacyPending();
        f.QueueAndClose();
        var newRun = f.CaptureAndPublish();
        var sourcePath = f.PathOf("inbox/" + newRun + ".json");
        var handoff = f.Read<RewardHandoff>("handoff.json");
        var source = RewardRuntimeFiles.Verify(handoff, RewardStartupFiles.Read<RewardSignedReceipt>(sourcePath));
        File.Delete(sourcePath); // fixture turns a fresh valid envelope into a late old-operation receipt
        var candidate = source.Evidence;
        var run = match == "run" ? legacy.Run : candidate.Authorization.RunId;
        var arm = match == "run" ? legacy.Arm : candidate.Authorization.ArmToken;
        var generation = match == "generation" ? legacy.Generation : candidate.Authorization.RestorationId;
        var native = Protocol.BuildNativeEndIdentity("factory4_day.Pmc 1728000000", arm);
        candidate = candidate with
        {
            Authorization = candidate.Authorization with { RunId = run, ArmToken = arm, RestorationId = generation, NativeEndIdentity = native },
            Report = candidate.Report with { RunId = run, ArmToken = arm, NativeEndIdentity = native },
            Restoration = candidate.Restoration with { RunId = run, ArmToken = arm, RestorationId = generation, NativeEndIdentity = native }
        };
        var late = RewardRuntimeFiles.Sign(handoff, source with { Evidence = candidate });
        f.Write("inbox/" + run + ".json", late);
        var before = f.Profile;
        var outcome = await RewardRuntimeInbox.ProcessAsync(f.Window, RewardEnrollment.Read(f.Root, f.Id), CancellationToken.None,
            () => throw new InvalidOperationException("Closed receipt reached settlement."));
        Assert.Equal(RewardSettlementStatus.ClosedWithoutPayment, outcome!.Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.False(File.Exists(f.PathOf("allocations/" + run + ".json")));
        Assert.False(File.Exists(f.PathOf("imported/" + run + ".json")));
        Assert.Equal(0, f.Coordinator.GetStatus(f.Id).PendingRoubles);
        Assert.Equal(0, f.Coordinator.GetStatus(f.Id).PendingExperience);
        Assert.Empty(f.Ledger);
    }

    [Theory]
    [InlineData("snapshot-schema")]
    [InlineData("snapshot-fields")]
    [InlineData("run-schema")]
    [InlineData("journal-schema")]
    [InlineData("profile-schema")]
    [InlineData("private")]
    [InlineData("offline")]
    [InlineData("cola")]
    public void UnsupportedOrTrueRecoveryStateIsNotClosable(string mutation)
    {
        using var f = new Fixture();
        var old = f.LegacyPending();
        switch (mutation)
        {
            case "snapshot-schema": f.WriteAt(f.SnapshotPath(old.Generation), new RewardSnapshotGuard(2, old.Generation, "pending")); break;
            case "snapshot-fields": File.WriteAllText(f.SnapshotPath(old.Generation), "{\"schema\":1,\"generation\":\"" + old.Generation + "\",\"state\":\"pending\",\"future\":true}"); break;
            case "run-schema": f.WriteAt(f.RunPath(old.Run), new RewardRuntimePending(2, old.Arm, old.Run)); break;
            case "journal-schema": f.Write("journal.json", new RewardJournal(2, f.Binding, [])); break;
            case "profile-schema": f.Edit(p => p[RewardProfileDocument.ExtensionKey]!["schema"] = 2); break;
            case "private": File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}"); break;
            case "offline": f.WriteAt(Path.Combine(f.Root, "user", "arena-rewards", "offline-active.json"), new { schema = 1 }); break;
            case "cola": f.Write("cola-plans/" + Guid.NewGuid().ToString("N") + ".json", new { schema = 1 }); break;
        }
        var before = f.Profile;
        var preview = RewardRecovery.Preview(f.Root, f.Id);
        Assert.False(preview.CanClose);
        Assert.False(RewardRecovery.Request(f.Root, f.Id, new string('a', 64)).Accepted);
        Assert.ThrowsAny<Exception>(() => RewardRecovery.DiscardUncertainAtStartup(f.Window, f.Id));
        Assert.Equal(before, f.Profile);
        Assert.False(Directory.Exists(f.PathOf("recovery")));
    }

    [Fact]
    public void LoadedOrClosedStartupWindowNeverWritesConfirmation()
    {
        using var f = new Fixture();
        f.LegacyPending();
        f.Queue();
        var before = f.Profile;
        using var loaded = new RewardStartupWindow(f.Root, () => false);
        Assert.Throws<RewardDataException>(() => RewardRecovery.ProcessAtStartup(loaded, f.Id));
        Assert.Throws<RewardDataException>(() => RewardRecovery.DiscardUncertainAtStartup(loaded, f.Id));
        f.Window.Dispose();
        Assert.Throws<RewardDataException>(() => RewardRecovery.ProcessAtStartup(f.Window, f.Id));
        Assert.Throws<RewardDataException>(() => RewardRecovery.DiscardUncertainAtStartup(f.Window, f.Id));
        Assert.Equal(before, f.Profile);
    }

    [SkippableTheory]
    [InlineData("local", "6a6f5c046536d896fc7500bd", "snapshot")]
    [InlineData("gpu2", "6ab7db1c64f04b565cf78be9", "payment")]
    public async Task IsolatedRealBaselineRetainsEveryOriginalCaseAndResumesFuturePayout(string host, string id, string kind)
    {
        var baseline = Environment.GetEnvironmentVariable("SPT_REWARD_RECOVERY_BASELINE");
        Skip.If(string.IsNullOrEmpty(baseline), "Set SPT_REWARD_RECOVERY_BASELINE to the private reward recovery baseline directory.");
        using var f = new Fixture(Path.Combine(baseline!, host), id);
        var sources = f.SourceBytes();
        var before = f.Profile;
        var originalPayments = f.Ledger.ToArray();
        var review = RewardRecovery.Preview(f.Root, f.Id);
        Assert.True(review.CanClose, review.Detail);
        Assert.Contains(review.Cases, c => c.Kind == kind);
        if (kind == "payment")
        {
            Assert.Equal(14, f.Read<RewardJournal>("journal.json").Entries.Count);
            Assert.Equal(13, originalPayments.Length);
            Assert.Equal(2000, Assert.Single(review.Cases).Roubles);
            Assert.Equal(200, Assert.Single(review.Cases).Experience);
        }
        RewardRecovery.DiscardUncertainAtStartup(f.Window, f.Id);
        AssertOnlyMarkerAdded(before, f.Profile);
        Assert.Equal(originalPayments, f.Ledger);
        f.AssertSources(sources);
        var resumed = await f.Process();
        Assert.NotEqual(RewardSettlementStatus.Quarantined, resumed.Result.Status);
        // Closing one old operation may unblock OTHER authentic queued results.
        // Each resulting payment must match its retained, verified signed source;
        // the explicitly waived key must remain absent from the native ledger.
        var waived = RewardRecovery.ReadAttempts(f.Root, f.Id).SelectMany(a => a.Request.Review.Cases)
            .Where(c => c.Kind == "payment").Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(f.Ledger, p => waived.Contains(p.ReceiptKey));
        var resumedPayments = f.Ledger.Except(originalPayments).ToArray();
        foreach (var payment in resumedPayments)
        {
            var parts = payment.ReceiptKey.Split(':');
            var source = RewardRuntimeInbox.ReadReceipt(f.Root, RewardRuntimeFiles.DirectoryFor(f.Root, f.Id),
                f.Read<RewardHandoff>("handoff.json"), f.PathOf("inbox/" + parts[0] + ".json"));
            var run = source.Evidence.Authorization;
            var evaluated = RewardResultValidator.Evaluate(run, source.Evidence.Report);
            var expected = new RewardClaim(f.Binding, run.RunId, long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                evaluated.Roubles, run.Policy.Version, evaluated.Experience, run.Cola);
            Assert.Equal(expected.Fingerprint, payment.Fingerprint);
            Assert.Equal(expected.Roubles, payment.Roubles);
            Assert.Equal(expected.Experience, payment.Experience);
        }
        Assert.Equal(resumedPayments.Sum(p => p.Roubles), resumed.Roubles);
        Assert.Equal(resumedPayments.Sum(p => p.Experience), resumed.Experience);
        var settledCount = f.Ledger.Count;
        f.CaptureAndPublish();
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(settledCount + 1, f.Ledger.Count);
        Assert.All(originalPayments, p => Assert.Contains(p, f.Ledger));
        var after = f.Profile;
        Assert.DoesNotContain((await f.Process()).Result.Status, new[] { RewardSettlementStatus.Paid, RewardSettlementStatus.Quarantined });
        Assert.Equal(after, f.Profile);
    }

    internal static void AssertOnlyMarkerAdded(string before, string after)
    {
        var a = JsonNode.Parse(before)!;
        var b = JsonNode.Parse(after)!;
        b[RewardProfileDocument.ExtensionKey]!.AsObject().Remove("recoveries");
        if (a[RewardProfileDocument.ExtensionKey]!["recoveries"] is { } markers)
            b[RewardProfileDocument.ExtensionKey]!["recoveries"] = markers.DeepClone();
        Assert.True(JsonNode.DeepEquals(a, b), "Recovery changed native progress or unrelated mod data.");
    }

    private sealed class Fixture : IDisposable
    {
        private const string DefaultId = "0123456789abcdef01234567";
        private const string LocalId = "aaaaaaaaaaaaaaaaaaaaaaaa";
        internal string Id { get; }
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ArenaMode.RewardRecovery.Tests", Guid.NewGuid().ToString("N"));
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Id + ".json");
        internal string Profile => File.ReadAllText(ProfilePath);
        internal RewardStartupWindow Window { get; }
        internal RewardRuntimeCoordinator Coordinator { get; }
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(20_000);
        internal RewardBinding Binding => Read<RewardStartupEnrollment>("enrollment.json").Binding;
        internal IReadOnlyList<RewardPayment> Ledger => RewardProfileDocument.ReadLedger(Profile, Binding, new());
        internal string PathOf(string path) => Path.Combine(RewardRuntimeFiles.DirectoryFor(Root, Id), path.Replace('/', Path.DirectorySeparatorChar));
        internal string SnapshotPath(string generation) => Path.Combine(Root, "user", "arena-reward-snapshots", Id, generation + ".json");
        internal string RunPath(string run) => Path.Combine(Root, "user", "arena-reward-runs", Id, run + ".json");
        internal T Read<T>(string name) => RewardStartupFiles.Read<T>(PathOf(name));
        internal void Write<T>(string name, T value) => WriteAt(PathOf(name), value);
        internal void WriteAt<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, RewardRuntimeFiles.Json(value));
        }
        internal Task<RewardStartupOutcome> Process() => RewardStartupProcessor.ProcessProfileAsync(Window, Id, CancellationToken.None);

        internal Fixture(string? baseline = null, string? id = null)
        {
            Id = id ?? DefaultId;
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            File.WriteAllText(Path.Combine(Root, ".owner"), "synthetic-reward-recovery");
            if (baseline is null)
            {
                var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
                profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15, ["Experience"] = 160_000 };
                profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
                profile["foreignMod"] = new JsonObject { ["progress"] = 123 };
                File.WriteAllText(ProfilePath, profile.ToJsonString());
            }
            else
            {
                File.Copy(Path.Combine(baseline, "user", "profiles", Id + ".json"), ProfilePath);
                foreach (var name in new[] { "arena-rewards", "arena-reward-runs", "arena-reward-snapshots", "arena-reward-checkpoints" })
                {
                    var source = Path.Combine(baseline, "user", name, Id);
                    if (!Directory.Exists(source)) continue;
                    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    {
                        var target = Path.Combine(Root, "user", name, Id, Path.GetRelativePath(source, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target);
                    }
                }
                // Closed offline leases/whitelists are read as historical proof;
                // never invoke owner recovery or dereference their old runtime
                // paths. Keep these signed/hash-bound records completely exact.
                var offline = Path.Combine(baseline, "user", "arena-rewards", "offline-sessions");
                if (Directory.Exists(offline))
                    foreach (var file in Directory.GetFiles(offline, "*", SearchOption.AllDirectories))
                    {
                        var target = Path.Combine(Root, "user", "arena-rewards", "offline-sessions", Path.GetRelativePath(offline, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target);
                    }
                // Explicit test relocation changes only copied enrollment and
                // transport root authority. Offline IDs, closed whitelists,
                // receipt payloads, signatures and conflict evidence stay exact.
                Write("enrollment.json", Read<RewardStartupEnrollment>("enrollment.json") with { RuntimeRoot = Root });
                foreach (var path in Directory.EnumerateFiles(PathOf("delegations"), "*.json").Append(PathOf("handoff.json")))
                {
                    var handoff = RewardStartupFiles.Read<RewardHandoff>(path);
                    WriteAt(path, handoff with { RuntimeRoot = Root });
                }
            }
            Window = new(Root, () => true);
            Coordinator = new(Root, () => _now);
            if (baseline is null)
            {
                Coordinator.SetConsent(Id, true);
                RewardEnrollment.EnsureAtStartup(Window, Id);
            }
        }

        internal (string Generation, string Run, string Arm) LegacyPending()
        {
            var generation = Guid.NewGuid().ToString("N");
            var run = Guid.NewGuid().ToString("N");
            var arm = Guid.NewGuid().ToString("N");
            WriteAt(SnapshotPath(generation), new RewardSnapshotGuard(1, generation, "pending"));
            WriteAt(RunPath(run), new RewardRuntimePending(1, arm, run));
            return (generation, run, arm);
        }
        internal string Queue()
        {
            var review = RewardRecovery.Preview(Root, Id);
            Assert.True(review.CanClose, review.Detail);
            Assert.True(RewardRecovery.Request(Root, Id, review.ReviewId).Accepted);
            return review.ReviewId;
        }
        internal string QueueAndClose(string? reviewId = null)
        {
            reviewId ??= Queue();
            Assert.True(RewardRecovery.Request(Root, Id, reviewId).Accepted);
            RewardRecovery.ProcessAtStartup(Window, Id);
            return reviewId;
        }
        internal string CaptureAndPublish(Action? whilePending = null)
        {
            var capturedProfile = Profile;
            Coordinator.ObserveSnapshot(Id, capturedProfile);
            var arm = Guid.NewGuid().ToString("N");
            var native = Protocol.BuildNativeEndIdentity("factory4_day.Pmc " + _now.ToUnixTimeSeconds(), arm);
            var begin = Coordinator.Begin(Id, arm, native, LocalId, "deathmatch", false, false,
                new() { ArmToken = arm, NativeEndIdentity = native, Mode = "deathmatch", Rule = "KillsOnly" });
            Assert.True(begin.Accepted, begin.Reason);
            // Models an independent ordinary host closing the pending copy
            // while this private capture still owns its original snapshot.
            whilePending?.Invoke();
            _now = _now.AddSeconds(10);
            var report = new RewardCaptureRequest
            {
                RunId = begin.RunId, ArmToken = arm, NativeEndIdentity = native, Revision = 1, Final = true,
                Rounds = [new() { Number = 1, Closed = true, ObservedDurationMilliseconds = 2000,
                    Actors = [new() { Body = 1, ParticipantId = LocalId, Side = "Friendly" },
                        new() { Body = 2, ParticipantId = "bbbbbbbbbbbbbbbbbbbbbbbb", Side = "Opponent" }],
                    Deaths = [new() { Sequence = 1, AtMilliseconds = 1000, VictimBody = 2, KillerBody = 1 }] }]
            };
            var captured = Coordinator.Capture(Id, report);
            Assert.True(captured.Accepted, captured.Reason);
            Coordinator.ObserveRestored(Id, capturedProfile);
            Coordinator.FinalizePersisted(Id, native, capturedProfile);
            return begin.RunId;
        }
        internal void Edit(Action<JsonNode> change)
        {
            var node = JsonNode.Parse(Profile)!;
            change(node);
            File.WriteAllText(ProfilePath, node.ToJsonString());
        }
        internal Dictionary<string, byte[]> SourceBytes() => Directory.GetFiles(Path.Combine(Root, "user"), "*", SearchOption.AllDirectories)
            .Where(p => p != ProfilePath && !p.Contains(Path.DirectorySeparatorChar + "recovery" + Path.DirectorySeparatorChar))
            .ToDictionary(p => p, File.ReadAllBytes, StringComparer.Ordinal);
        internal void AssertSources(Dictionary<string, byte[]> before)
        { foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key)); }
        public void Dispose()
        {
            RewardRecovery.Checkpoint = null;
            Window.Dispose();
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.RewardRecovery.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != parent || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".owner")) != "synthetic-reward-recovery") throw new InvalidOperationException("Not an owned fixture.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
