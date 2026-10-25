using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

// All files below are fresh, explicitly owned synthetic profiles. No game tree,
// installed Cola assembly, private-session profile or running server is used.
public sealed class RewardColaJournalTests
{
    [Fact]
    public async Task PreparedIntentIsInertAndNativePaymentPrecedesCompanionProgress()
    {
        using var f = new Fixture();
        string native = f.Profile, foreign = f.Foreign;
        var plan = f.Prepare();
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.Equal(foreign, plan.BeforeJson);
        Assert.Equal(plan, f.ReadPlan());
        Assert.False(File.Exists(f.DonePath));

        Assert.Equal(RewardSettlementStatus.Paid, (await f.Pay()).Status);
        Assert.Equal(foreign, f.Foreign);
        f.Complete(plan);
        Assert.Equal(plan.AfterJson, f.Foreign);
        Assert.Equal(13, JsonNode.Parse(f.Foreign)!["xp"]!["speedcola"]!.GetValue<int>());
        Assert.Single(f.Ledger);
        Assert.True(File.Exists(f.DonePath));
        foreach (string field in new[] { "equipped", "stats", "mode", "classic", "future" })
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(foreign)![field], JsonNode.Parse(f.Foreign)![field]), field);
    }

    [Fact]
    public async Task ActualClientAchievementKeyCommitsNativeRewardAndCompanionFlagExactlyOnce()
    {
        using var f = new Fixture();
        var claim = f.Claim with { Cola = RewardColaDelta.Capture("{}", "{\"xp\":{\"speedcola\":3},\"ach\":{\"a_first\":true}}") };
        var approval = f.Approval with { Claim = claim };
        var plan = RewardColaJournal.Prepare(f.Window, f.Enrollment, approval, f.Resolve)!;
        var result = await new RewardSettlementCore(new RewardStartupStore(f.Window, f.Enrollment, approval, new()))
            .SettleAsync(claim);
        Assert.Equal(RewardSettlementStatus.Paid, result.Status);
        var payment = Assert.Single(f.Ledger);
        var achievement = RewardColaCatalog.Achievements["a_first"];
        Assert.Equal(payment.PaidAtUnixSeconds,
            JsonNode.Parse(f.Profile)!["characters"]!["pmc"]!["Achievements"]![achievement.Id]!.GetValue<long>());
        Assert.Null(JsonNode.Parse(f.Foreign)!["ach"]);
        string native = f.Profile;
        await f.Recover();
        Assert.Equal(native, f.Profile);
        Assert.True(JsonNode.Parse(f.Foreign)!["ach"]!["a_first"]!.GetValue<bool>());
        Assert.Equal(plan.AfterJson, f.Foreign);
        await f.Recover();
        Assert.Equal(plan.AfterJson, f.Foreign);
        Assert.Equal(native, f.Profile);
        Assert.Single(f.Ledger);
    }

    [Fact]
    public async Task NativePaidBeforeCompanionWriteRecoversExactlyOnce()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        string native = f.Profile;
        await f.Recover();
        Assert.Equal(plan.AfterJson, f.Foreign);
        Assert.Equal(native, f.Profile);
        string done = File.ReadAllText(f.DonePath);
        await f.Recover();
        f.Complete(plan);
        Assert.Equal(plan.AfterJson, f.Foreign);
        Assert.Equal(done, File.ReadAllText(f.DonePath));
        Assert.Equal(native, f.Profile);
    }

    [Fact]
    public void CompletingBeforeNativePaymentCannotWriteForeignProgressOrAck()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        string native = f.Profile, foreign = f.Foreign;
        Assert.Throws<RewardColaRecoveryException>(() => f.Complete(plan));
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.DonePath));
    }

    [Fact]
    public async Task OldCommittedPlanAndNewPendingPlanRecoverAgainstTheirOwnNativeApprovals()
    {
        using var f = new Fixture();
        var first = f.Prepare();
        await f.Pay();
        f.Complete(first);
        var ordinaryProgress = JsonNode.Parse(f.Foreign)!;
        ordinaryProgress["xp"]!["speedcola"] = 20;
        File.WriteAllText(f.ForeignPath, ordinaryProgress.ToJsonString());
        var secondClaim = f.Claim with { RunId = "44444444444444444444444444444444", Sequence = 2 };
        var secondApproval = f.Approval with { Claim = secondClaim, CleanProfileRevision = RewardIdentity.Hash(f.Profile) };
        var second = RewardColaJournal.Prepare(f.Window, f.Enrollment, secondApproval, f.Resolve)!;
        var result = await new RewardSettlementCore(new RewardStartupStore(f.Window, f.Enrollment, secondApproval, new()))
            .SettleAsync(secondClaim);
        Assert.Equal(RewardSettlementStatus.Paid, result.Status);
        f.WriteJournal(f.Journal with { Entries = f.Journal.Entries.Select(e =>
            e.Payment.ReceiptKey == secondClaim.Key ? e with { Phase = RewardJournalPhase.Attempting } : e).ToArray() });
        string native = f.Profile;
        await f.Recover();
        Assert.Equal(native, f.Profile);
        Assert.Equal(second.AfterJson, f.Foreign);
        Assert.Equal(23, JsonNode.Parse(f.Foreign)!["xp"]!["speedcola"]!.GetValue<int>());
        Assert.Equal(2, f.Ledger.Count);
        Assert.All(f.Journal.Entries, e => Assert.Equal(RewardJournalPhase.Committed, e.Phase));
        await f.Recover();
        Assert.Equal(second.AfterJson, f.Foreign);
        Assert.Equal(native, f.Profile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedColaHistoryDoesNotClaimAnOrdinaryRubOnlyRecovery(bool nativeWriteAbsent)
    {
        using var f = new Fixture();
        var first = f.Prepare();
        await f.Pay();
        f.Complete(first);
        string beforeOrdinaryPayment = f.Profile;
        var ordinaryClaim = f.Claim with { RunId = "44444444444444444444444444444444", Sequence = 2, Cola = null };
        var ordinaryApproval = f.Approval with { Claim = ordinaryClaim, CleanProfileRevision = RewardIdentity.Hash(f.Profile) };
        var result = await new RewardSettlementCore(new RewardStartupStore(f.Window, f.Enrollment, ordinaryApproval, new()))
            .SettleAsync(ordinaryClaim);
        Assert.Equal(RewardSettlementStatus.Paid, result.Status);
        f.WriteJournal(f.Journal with { Entries = f.Journal.Entries.Select(e =>
            e.Payment.ReceiptKey == ordinaryClaim.Key ? e with { Phase = RewardJournalPhase.Attempting } : e).ToArray() });
        if (nativeWriteAbsent) File.WriteAllText(f.ProfilePath, beforeOrdinaryPayment);
        string native = f.Profile, foreign = f.Foreign, journal = File.ReadAllText(f.JournalPath);
        await f.Recover();
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.Equal(journal, File.ReadAllText(f.JournalPath));
    }

    [Fact]
    public async Task ForeignWriteBeforeCompletionAckRecoversWithoutAddingAgain()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        File.WriteAllText(f.ForeignPath, plan.AfterJson); // Crash after external CAS.
        Assert.False(File.Exists(f.DonePath));
        string native = f.Profile;
        await f.Recover();
        Assert.Equal(plan.AfterJson, f.Foreign);
        Assert.Equal(native, f.Profile);
        Assert.True(File.Exists(f.DonePath));
    }

    [Fact]
    public async Task AttemptingNativeJournalWithActualPaidProfileIsConfirmedBeforeCompanionWrite()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        f.SetJournalPhase(RewardJournalPhase.Attempting);
        string native = f.Profile;
        await f.Recover();
        Assert.Equal(RewardJournalPhase.Committed, Assert.Single(f.Journal.Entries).Phase);
        Assert.Equal(native, f.Profile);
        Assert.Equal(plan.AfterJson, f.Foreign);
    }

    [Fact]
    public async Task MissingForeignDocumentIsCreatedOnlyAfterNativePayment()
    {
        using var f = new Fixture();
        File.Delete(f.ForeignPath);
        var plan = f.Prepare();
        Assert.False(plan.BeforeExists);
        Assert.Equal("{}", plan.BeforeJson);
        Assert.False(File.Exists(f.ForeignPath));
        await f.Pay();
        await f.Recover();
        Assert.Equal(plan.AfterJson, f.Foreign);
        Assert.Equal(3, JsonNode.Parse(f.Foreign)!["xp"]!["speedcola"]!.GetValue<int>());
    }

    [Fact]
    public async Task DisabledRewardsStillFinishAlreadyPaidCompanionTransaction()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        f.WriteEnrollment(f.Enrollment with { Enabled = false });
        string native = f.Profile;
        await f.Recover();
        Assert.Equal(plan.AfterJson, f.Foreign);
        Assert.Equal(native, f.Profile);
        Assert.False(RewardStartupFiles.Read<RewardStartupEnrollment>(f.EnrollmentPath).Enabled);
    }

    [Fact]
    public async Task RecoveryNeverPaysAnUnpaidPreparedClaimEvenWhenEnabledOrDisabled()
    {
        using var f = new Fixture();
        f.Prepare();
        string native = f.Profile, foreign = f.Foreign, journal = File.ReadAllText(f.JournalPath);
        await f.Recover();
        f.WriteEnrollment(f.Enrollment with { Enabled = false });
        await f.Recover();
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.Equal(journal, File.ReadAllText(f.JournalPath));
        Assert.Empty(f.Ledger);
        Assert.False(File.Exists(f.DonePath));
    }

    [Fact]
    public async Task CommittedHistoryAllowsLaterOrdinaryProgressAndCollectedMail()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        f.Complete(plan);
        var foreign = JsonNode.Parse(f.Foreign)!;
        foreign["xp"]!["speedcola"] = 900;
        foreign["stats"]!["ordinaryKills"] = 8;
        File.WriteAllText(f.ForeignPath, foreign.ToJsonString());
        var native = JsonNode.Parse(f.Profile)!;
        native["dialogues"] = new JsonObject(); // Mail already collected/deleted.
        native["characters"]!["pmc"]!["Info"]!["Experience"] = 20_000;
        File.WriteAllText(f.ProfilePath, native.ToJsonString());
        string expectedNative = f.Profile, expectedForeign = f.Foreign;
        f.Complete(plan);
        await f.Recover();
        Assert.Equal(expectedForeign, f.Foreign);
        Assert.Equal(expectedNative, f.Profile);
    }

    [Fact]
    public async Task LostAckPlusOrdinaryProgressFailsClosedInsteadOfRebasing()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        f.Complete(plan);
        File.Delete(f.DonePath);
        File.WriteAllText(f.ForeignPath, "{\"xp\":{\"speedcola\":900}}");
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.DonePath));
    }

    [Fact]
    public void UnpaidIntentIsImmutableAndCurrentNormalProgressCannotBeSilentlyRebased()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        Assert.Equal(plan, f.Prepare());
        File.WriteAllText(f.ForeignPath, "{\"xp\":{\"speedcola\":40}}");
        string native = f.Profile, foreign = f.Foreign;
        Assert.Throws<RewardDataException>(() => f.Prepare());
        Assert.Equal(plan, f.ReadPlan());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
    }

    [Fact]
    public async Task ProgressChangedAfterNativePaymentIsPreservedAndStopsRecovery()
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        File.WriteAllText(f.ForeignPath, "{\"xp\":{\"speedcola\":55}}");
        string native = f.Profile, foreign = f.Foreign;
        Assert.Throws<RewardColaRecoveryException>(() => f.Complete(plan));
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.DonePath));
    }

    [Theory]
    [InlineData("missing-plan")]
    [InlineData("missing-plan-and-ack")]
    [InlineData("corrupt-plan")]
    [InlineData("wrong-after")]
    [InlineData("wrong-approval-revision")]
    [InlineData("path-escape")]
    [InlineData("corrupt-ack")]
    public async Task PaidPlanOrAckDamageNeverReconstructsHistory(string damage)
    {
        using var f = new Fixture();
        var plan = f.Prepare();
        await f.Pay();
        f.Complete(plan);
        if (damage.StartsWith("missing-plan", StringComparison.Ordinal)) File.Delete(f.PlanPath);
        if (damage == "missing-plan-and-ack") File.Delete(f.DonePath);
        if (damage == "corrupt-plan") File.WriteAllText(f.PlanPath, "{");
        if (damage == "wrong-after") f.WritePlan(plan with { AfterJson = "{}" });
        if (damage == "wrong-approval-revision") f.WritePlan(plan with
        { Approval = plan.Approval with { CleanProfileRevision = new string('a', 64) } });
        if (damage == "path-escape") f.WritePlan(plan with { RelativePath = "../private/profile.json" });
        if (damage == "corrupt-ack") File.WriteAllText(f.DonePath, "{}");
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
    }

    [Fact]
    public async Task MissingPlanDirectoryForPaidColaLedgerIsDetected()
    {
        using var f = new Fixture();
        f.Prepare();
        await f.Pay();
        File.Delete(f.PlanPath);
        Directory.Delete(f.PlanDirectory); // Known empty owned fixture directory.
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(Directory.Exists(f.PlanDirectory));
    }

    [Fact]
    public async Task EscapedColaLedgerPropertyStillDetectsMissingPlanWhileRewardsAreOff()
    {
        using var f = new Fixture();
        f.Prepare();
        await f.Pay();
        File.Delete(f.PlanPath);
        Directory.Delete(f.PlanDirectory);
        File.WriteAllText(f.ProfilePath, f.Profile.Replace("\"cola\":", "\"\\u0063ola\":", StringComparison.Ordinal));
        f.WriteEnrollment(f.Enrollment with { Enabled = false });
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
    }

    [Fact]
    public async Task EscapedColaJournalPropertyRetainsRollbackEvidenceWithoutCompanionDirectory()
    {
        using var f = new Fixture();
        string nativeBefore = f.Profile;
        f.Prepare();
        await f.Pay();
        File.WriteAllText(f.ProfilePath, nativeBefore);
        File.Delete(f.PlanPath);
        Directory.Delete(f.PlanDirectory);
        File.WriteAllText(f.JournalPath, File.ReadAllText(f.JournalPath).Replace("\"cola\":", "\"\\u0063ola\":", StringComparison.Ordinal));
        f.WriteEnrollment(f.Enrollment with { Enabled = false });
        string foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(nativeBefore, f.Profile);
        Assert.Equal(foreign, f.Foreign);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeProfileRollbackCannotReplayOrForgetCommittedCompanionProgress(bool removePlanDirectory)
    {
        using var f = new Fixture();
        string nativeBefore = f.Profile;
        var plan = f.Prepare();
        await f.Pay();
        f.Complete(plan);
        File.WriteAllText(f.ProfilePath, nativeBefore);
        if (removePlanDirectory)
        {
            File.Delete(f.PlanPath);
            File.Delete(f.DonePath);
            Directory.Delete(f.PlanDirectory);
        }
        string foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(nativeBefore, f.Profile);
        Assert.Equal(foreign, f.Foreign);
    }

    [Fact]
    public async Task AttemptingNativeTransactionWithoutPaymentIsAmbiguousAndNeverPaidByRecovery()
    {
        using var f = new Fixture();
        string nativeBefore = f.Profile;
        f.Prepare();
        await f.Pay();
        f.SetJournalPhase(RewardJournalPhase.Attempting);
        File.WriteAllText(f.ProfilePath, nativeBefore);
        string foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(nativeBefore, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.DonePath));
    }

    [Fact]
    public async Task MalformedUnpaidIntentRefusesOnlyRewards()
    {
        using var f = new Fixture();
        f.Prepare();
        File.WriteAllText(f.PlanPath, "{");
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardDataException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.DonePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedNewUnpaidPlanRemainsRewardOnlyAfterValidOlderColaPayment(bool olderNeedsCompletion)
    {
        using var f = new Fixture();
        var first = f.Prepare();
        await f.Pay();
        if (!olderNeedsCompletion) f.Complete(first);
        var secondClaim = f.Claim with { RunId = "44444444444444444444444444444444", Sequence = 2 };
        var secondApproval = f.Approval with { Claim = secondClaim, CleanProfileRevision = RewardIdentity.Hash(f.Profile) };
        RewardColaJournal.Prepare(f.Window, f.Enrollment, secondApproval, f.Resolve);
        File.WriteAllText(Path.Combine(f.PlanDirectory, secondClaim.RunId + ".json"), "{");
        string native = f.Profile;
        await Assert.ThrowsAsync<RewardDataException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(first.AfterJson, f.Foreign);
        Assert.True(File.Exists(f.DonePath));
        Assert.Single(f.Ledger);
    }

    [Fact]
    public async Task MalformedUnpaidPlanCannotHideMissingPaidHistory()
    {
        using var f = new Fixture();
        f.Prepare();
        await f.Pay();
        File.WriteAllText(Path.Combine(f.PlanDirectory, "44444444444444444444444444444444.json"), "{");
        File.Delete(f.PlanPath);
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.DonePath));
    }

    [Fact]
    public async Task ChangedOrMissingNativeJournalStopsCompanionRecovery()
    {
        using var f = new Fixture();
        f.Prepare();
        await f.Pay();
        string native = f.Profile, foreign = f.Foreign, originalJournal = File.ReadAllText(f.JournalPath);
        f.WriteJournal(new(1, f.Binding, []));
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        File.Delete(f.JournalPath);
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        File.WriteAllText(f.JournalPath, originalJournal);
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.DonePath));
    }

    [Fact]
    public void ForeignResolverMustStayInLocalNormalModTree()
    {
        using var f = new Fixture();
        string wrong = Path.Combine(f.Root, "private-session");
        Directory.CreateDirectory(wrong);
        string native = f.Profile, foreign = f.Foreign;
        Assert.Throws<RewardDataException>(() => RewardColaJournal.Prepare(f.Window, f.Enrollment, f.Approval, _ => wrong));
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.PlanPath));
    }

    [Fact]
    public async Task ChangedModFolderAfterPaymentCannotRedirectProgress()
    {
        using var f = new Fixture();
        f.Prepare();
        await f.Pay();
        string alternate = Path.Combine(f.Root, "user", "mods", "DifferentCola");
        Directory.CreateDirectory(alternate);
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => RewardColaJournal.RecoverAsync(
            f.Window, f.Binding.ProfileId, CancellationToken.None, _ => alternate));
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.Empty(Directory.EnumerateFileSystemEntries(alternate));
    }

    [Fact]
    public async Task FreshProfileWithoutEnrollmentAndLegacyRubOnlyClaimNeedNoColaMod()
    {
        using var f = new Fixture();
        var noCola = f.Approval with { Claim = f.Approval.Claim with { Cola = null } };
        Assert.Null(RewardColaJournal.Prepare(f.Window, f.Enrollment, noCola, _ => throw new Exception("Should not resolve Cola")));
        File.Delete(f.EnrollmentPath);
        await RewardColaJournal.RecoverAsync(f.Window, f.Binding.ProfileId, CancellationToken.None,
            _ => throw new Exception("Should not resolve Cola"));
        File.Delete(f.ProfilePath);
        await RewardColaJournal.RecoverAsync(f.Window, f.Binding.ProfileId, CancellationToken.None);
        Assert.False(File.Exists(f.PlanPath));
    }

    [Fact]
    public async Task UnrelatedMalformedNativeProfileDoesNotBecomeFatalColaRecovery()
    {
        using var f = new Fixture();
        foreach (string profile in new[] { "{", "{\"info\":{},\"info\":{}}", "{\"foreign\":{\"cola\":1},\"info\":{},\"info\":{}}" })
        {
            File.WriteAllText(f.ProfilePath, profile);
            await f.Recover();
            Assert.Equal(profile, f.Profile);
        }
        Assert.False(File.Exists(f.PlanPath));
    }

    [SkippableFact]
    public void LinkedForeignFolderIsRejectedBeforePreparingOrPaying()
    {
        using var f = new Fixture();
        string link = Path.Combine(f.Root, "user", "mods", "LinkedCola");
        try { Directory.CreateSymbolicLink(link, f.ForeignFolder); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        { Skip.If(true, "Creating a test-only directory link is unavailable: " + e.GetType().Name); return; }
        try
        {
            string native = f.Profile, foreign = f.Foreign;
            Assert.Throws<RewardDataException>(() => RewardColaJournal.Prepare(f.Window, f.Enrollment, f.Approval, _ => link));
            Assert.Equal(native, f.Profile);
            Assert.Equal(foreign, f.Foreign);
            Assert.False(File.Exists(f.PlanPath));
        }
        finally { Directory.Delete(link); } // Delete only the owned link, never its target.
    }

    [SkippableFact]
    public async Task LinkedCompanionHistoryAfterNativePaymentIsFatalRatherThanTreatedAsAbsent()
    {
        using var f = new Fixture();
        f.Prepare();
        await f.Pay();
        string kept = Path.Combine(f.RewardDirectory, "kept-synthetic-plans");
        Directory.Move(f.PlanDirectory, kept);
        try { Directory.CreateSymbolicLink(f.PlanDirectory, kept); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Directory.Move(kept, f.PlanDirectory);
            Skip.If(true, "Creating a test-only directory link is unavailable: " + e.GetType().Name);
            return;
        }
        try
        {
            string native = f.Profile, foreign = f.Foreign;
            await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
            Assert.Equal(native, f.Profile);
            Assert.Equal(foreign, f.Foreign);
        }
        finally
        {
            Directory.Delete(f.PlanDirectory); // Known synthetic link, nonrecursive.
            Directory.Move(kept, f.PlanDirectory);
        }
    }

    [Fact]
    public async Task ClosedStartupBoundaryOrCancellationCannotWriteAnything()
    {
        using var f = new Fixture();
        string native = f.Profile, foreign = f.Foreign;
        f.Unloaded = false;
        Assert.Throws<RewardDataException>(() => f.Prepare());
        await Assert.ThrowsAsync<RewardDataException>(() => f.Recover());
        f.Unloaded = true;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RewardColaJournal.RecoverAsync(
            f.Window, f.Binding.ProfileId, cancelled.Token, f.Resolve));
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.False(File.Exists(f.PlanPath));
    }

    [Fact]
    public void DisabledEnrollmentCannotPrepareNewIntent()
    {
        using var f = new Fixture();
        Assert.Throws<RewardDataException>(() => RewardColaJournal.Prepare(
            f.Window, f.Enrollment with { Enabled = false }, f.Approval, f.Resolve));
        Assert.False(File.Exists(f.PlanPath));
    }

    [Fact]
    public async Task AtomicWriteOrphanDoesNotActAsPaidOrCommittedEvidence()
    {
        using var f = new Fixture();
        f.Prepare();
        string orphan = Path.Combine(f.PlanDirectory, ".arena-reward-44444444444444444444444444444444.tmp");
        File.WriteAllText(orphan, "incomplete staged file");
        string native = f.Profile, foreign = f.Foreign;
        await f.Recover();
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
        Assert.True(File.Exists(orphan));
        Assert.False(File.Exists(f.DonePath));
    }

    [Fact]
    public async Task OversizedPlanIsBoundedBeforeDeserialization()
    {
        using var f = new Fixture();
        f.Prepare();
        await f.Pay();
        File.WriteAllText(f.PlanPath, new string(' ', 2_500_001));
        string native = f.Profile, foreign = f.Foreign;
        await Assert.ThrowsAsync<RewardColaRecoveryException>(() => f.Recover());
        Assert.Equal(native, f.Profile);
        Assert.Equal(foreign, f.Foreign);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string FixtureParent = Path.Combine(Path.GetTempPath(), "ArenaMode.ColaJournal.Tests");
        internal string Root { get; } = Path.Combine(FixtureParent, Guid.NewGuid().ToString("N"));
        internal RewardBinding Binding { get; } = new("11111111111111111111111111111111", "0123456789abcdef01234567", "22222222222222222222222222222222");
        internal RewardClaim Claim { get; }
        internal RewardStartupEnrollment Enrollment { get; }
        internal RewardStartupApproval Approval { get; }
        internal RewardStartupWindow Window { get; }
        internal bool Unloaded = true;
        internal string RewardDirectory => Path.Combine(Root, "user", "arena-rewards", Binding.ProfileId);
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Binding.ProfileId + ".json");
        internal string EnrollmentPath => Path.Combine(RewardDirectory, "enrollment.json");
        internal string JournalPath => Path.Combine(RewardDirectory, "journal.json");
        internal string ForeignFolder => Path.Combine(Root, "user", "mods", "SyntheticTarkovaCola");
        internal string ForeignPath => Path.Combine(ForeignFolder, "profiles", Binding.ProfileId + ".json");
        internal string PlanDirectory => Path.Combine(RewardDirectory, "cola-plans");
        internal string PlanPath => Path.Combine(PlanDirectory, Claim.RunId + ".json");
        internal string DonePath => Path.Combine(PlanDirectory, Claim.RunId + ".done.json");
        internal string Profile => File.ReadAllText(ProfilePath);
        internal string Foreign => File.ReadAllText(ForeignPath);
        internal IReadOnlyList<RewardPayment> Ledger => RewardProfileDocument.ReadLedger(Profile, Binding, new());
        internal RewardJournal Journal => RewardSettlementCore.ParseJournal(File.ReadAllText(JournalPath), Binding, new());

        internal Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(ForeignPath)!);
            Directory.CreateDirectory(RewardDirectory);
            File.WriteAllText(Path.Combine(Root, ".synthetic-owner"), "cola-journal");
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15, ["Experience"] = 1000 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            File.WriteAllText(ProfilePath, RewardProfileDocument.Enroll(profile.ToJsonString(), Binding));
            File.WriteAllText(ForeignPath, """
                {"chv":2,"xp":{"speedcola":10},"equipped":["ordinary"],"stats":{"ordinaryKills":7},"mode":"Classic","classic":{"bought":true},"future":[1,2]}
                """);
            string delta = RewardColaDelta.Capture("{}", "{\"xp\":{\"speedcola\":3}}")!;
            Claim = new(Binding, "33333333333333333333333333333333", 1, 1000, "synthetic-cola-v1", 100, delta);
            Enrollment = new(1, true, Root, Binding);
            Approval = new(1, RewardStartupApprovalState.RestoredAndApproved, Claim, RewardIdentity.Hash(Profile));
            WriteEnrollment(Enrollment);
            File.WriteAllText(Path.Combine(RewardDirectory, "approved.json"), Json(Approval));
            WriteJournal(new(1, Binding, []));
            Window = new(Root, () => Unloaded);
        }

        internal string Resolve(string _) => ForeignFolder;
        internal RewardColaPlan Prepare() => RewardColaJournal.Prepare(Window, Enrollment, Approval, Resolve)!;
        internal void Complete(RewardColaPlan plan) => RewardColaJournal.Complete(Window, Enrollment, plan, Resolve);
        internal Task Recover() => RewardColaJournal.RecoverAsync(Window, Binding.ProfileId, CancellationToken.None, Resolve);
        internal ValueTask<RewardSettlementResult> Pay() => new RewardSettlementCore(
            new RewardStartupStore(Window, Enrollment, Approval, new())).SettleAsync(Claim);
        internal RewardColaPlan ReadPlan() => RewardStartupFiles.Read<RewardColaPlan>(PlanPath, 2_500_000);
        internal void WritePlan(RewardColaPlan plan) => File.WriteAllText(PlanPath, Json(plan));
        internal void WriteEnrollment(RewardStartupEnrollment value) => File.WriteAllText(EnrollmentPath, Json(value));
        internal void WriteJournal(RewardJournal value) => File.WriteAllText(JournalPath, Json(value));
        internal void SetJournalPhase(RewardJournalPhase phase)
            => WriteJournal(Journal with { Entries = Journal.Entries.Select(e => e with { Phase = phase }).ToArray() });
        private static string Json<T>(T value) => JsonSerializer.Serialize(value, RewardSettlementCore.JournalJson);

        public void Dispose()
        {
            Window.Dispose();
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != Path.GetFullPath(FixtureParent)
                || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".synthetic-owner")) != "cola-journal")
                throw new InvalidOperationException("Not an owned synthetic Cola journal directory.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
