using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardRuntimeTests
{
    [Fact]
    public async Task ConsentDoesNotTouchLiveProfileAndEnrollmentOccursOnlyAtStartup()
    {
        using var f = new Fixture(enroll: false);
        var before = f.Profile;
        var status = f.Coordinator.SetConsent(Fixture.ProfileId, true);
        Assert.True(status.Enabled);
        Assert.False(status.Enrolled);
        Assert.Equal("restart-required", status.State);
        Assert.Equal(before, f.Profile);
        Assert.False(File.Exists(f.PathOf("journal.json")));
        var result = await f.Process();
        Assert.Equal("rewards-enrolled-no-pending-result", result.Result.Reason);
        Assert.True(f.Coordinator.GetStatus(Fixture.ProfileId).Enrolled);
        var original = JsonNode.Parse(before)!;
        var after = JsonNode.Parse(f.Profile)!;
        after.AsObject().Remove(RewardProfileDocument.ExtensionKey);
        Assert.True(JsonNode.DeepEquals(original, after));
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public async Task ActualCaptureRestoreReadbackQueueAndRestartPaysExactlyOnce()
    {
        using var f = new Fixture();
        var before = f.Profile;
        var report = f.Capture();
        f.RestoreAndPublish(report);
        Assert.Equal(before, f.Profile);
        Assert.Equal("pending", f.Coordinator.GetStatus(Fixture.ProfileId).State);
        Assert.Equal(1000, f.Coordinator.GetStatus(Fixture.ProfileId).PendingRoubles);
        Assert.Equal(100, f.Coordinator.GetStatus(Fixture.ProfileId).PendingExperience);
        var paid = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, paid.Result.Status);
        Assert.Equal(1000, paid.Roubles);
        Assert.Equal(100, paid.Experience);
        Assert.Equal(160_100, JsonNode.Parse(f.Profile)!["characters"]!["pmc"]!["Info"]!["Experience"]!.GetValue<long>());
        Assert.Single(f.Ledger);
        var profile = f.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Equal(profile, f.Profile);
        Assert.Equal(1000, f.Coordinator.GetStatus(Fixture.ProfileId).LastPaidRoubles);
        Assert.Equal(100, f.Coordinator.GetStatus(Fixture.ProfileId).LastPaidExperience);
    }

    [Fact]
    public async Task NativeEmptyInraidProfileEnrollsAndPaysAfterPositiveArenaRestore()
    {
        using var f = new Fixture(enroll: false);
        f.EditProfile(p => p["inraid"] = new JsonObject());
        f.Coordinator.SetConsent(Fixture.ProfileId, true);
        Assert.Equal("rewards-enrolled-no-pending-result", (await f.Process()).Result.Reason);
        f.RestoreAndPublish(f.Capture());
        var result = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, result.Result.Status);
        Assert.Equal(1000, result.Roubles);
        Assert.Empty(JsonNode.Parse(f.Profile)!["inraid"]!.AsObject());
    }

    [Fact]
    public async Task MultipleMatchesQueuedBeforeRestartPreserveInterveningNormalProgress()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        f.EditProfile(p => p["foreignMod"]!["progress"] = 200);
        f.RestoreAndPublish(f.Capture());
        f.EditProfile(p => p["foreignMod"]!["progress"] = 777);
        f.EditProfile(p => p["characters"]!["pmc"]!["Info"]!["Experience"] = 180_000);
        var result = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, result.Result.Status);
        Assert.Equal(2000, result.Roubles);
        Assert.Equal(200, result.Experience);
        Assert.Equal(180_200, JsonNode.Parse(f.Profile)!["characters"]!["pmc"]!["Info"]!["Experience"]!.GetValue<long>());
        Assert.Equal(2, f.Ledger.Count);
        Assert.Equal(777, JsonNode.Parse(f.Profile)!["foreignMod"]!["progress"]!.GetValue<int>());
        Assert.Equal(new long[] { 1, 2 }, Directory.GetFiles(f.PathOf("decisions")).Select(p => RewardStartupFiles.Read<RewardApprovalDecision>(p).Sequence).Order());
    }

    [Fact]
    public async Task PrivateReceiptTransfersOnlyMetadataAndCanPayAfterOrdinaryProgress()
    {
        using var f = new Fixture();
        using var p = new Fixture(enroll: false);
        File.WriteAllText(p.ProfilePath, f.Profile);
        File.WriteAllText(Path.Combine(p.Root, "arena-rewards-origin.json"), File.ReadAllText(f.PathOf("handoff.json")));
        File.WriteAllText(Path.Combine(p.Root, "arena-session.json"), JsonSerializer.Serialize(new { schema = 1, profileId = Fixture.ProfileId, sourceRuntime = f.Root }));
        var report = p.Capture();
        p.RestoreAndPublish(report);
        Assert.Single(Directory.GetFiles(Path.Combine(p.Root, "user", "arena-rewards-outbox"), "*.json"));
        f.EditProfile(profile => profile["foreignMod"]!["progress"] = 888);
        f.EditProfile(profile => profile["characters"]!["pmc"]!["Info"]!["Experience"] = 190_000);
        var before = f.Profile;
        Directory.CreateDirectory(f.PathOf("inbox"));
        File.Copy(Path.Combine(p.Root, "user", "arena-rewards-outbox", report.RunId + ".json"), f.PathOf("inbox/" + report.RunId + ".json"));
        Assert.Equal(before, f.Profile);
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        Assert.Equal(888, JsonNode.Parse(f.Profile)!["foreignMod"]!["progress"]!.GetValue<int>());
        Assert.Equal(190_100, JsonNode.Parse(f.Profile)!["characters"]!["pmc"]!["Info"]!["Experience"]!.GetValue<long>());
        Assert.Single(f.Ledger);
    }

    [Theory]
    [InlineData("no-restore")]
    [InlineData("wrong-restored")]
    [InlineData("wrong-durable")]
    [InlineData("wrong-native")]
    [InlineData("missing-final")]
    public void MissingPositiveEvidenceNeverPublishesReceipt(string problem)
    {
        using var f = new Fixture();
        var report = f.Capture(final: problem != "missing-final");
        var other = JsonNode.Parse(f.Profile)!;
        other["foreignMod"]!["progress"] = 999;
        if (problem == "wrong-restored")
            Assert.Throws<RewardDataException>(() => f.Coordinator.ObserveRestored(Fixture.ProfileId, other.ToJsonString()));
        else if (problem != "no-restore") f.Coordinator.ObserveRestored(Fixture.ProfileId, f.Profile);
        Assert.Throws<RewardDataException>(() => f.Coordinator.FinalizePersisted(Fixture.ProfileId,
            problem == "wrong-native" ? "wrong" : report.NativeEndIdentity, problem == "wrong-durable" ? other.ToJsonString() : f.Profile));
        Assert.False(Directory.Exists(f.PathOf("inbox")));
        Assert.Empty(f.Ledger);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("arm")]
    [InlineData("native")]
    [InlineData("negative-time")]
    [InlineData("future-time")]
    [InlineData("duplicate-body")]
    [InlineData("duplicate-death")]
    [InlineData("foreign-victim")]
    [InlineData("unknown-side")]
    [InlineData("unknown-kind")]
    [InlineData("round-gap")]
    public void InvalidCaptureIsRefusedWithoutAnyProfileWrite(string problem)
    {
        using var f = new Fixture();
        var report = f.BeginReport();
        switch (problem)
        {
            case "run": report.RunId = Guid.NewGuid().ToString("N"); break;
            case "arm": report.ArmToken = Guid.NewGuid().ToString("N"); break;
            case "native": report.NativeEndIdentity = "wrong"; break;
            case "negative-time": report.Rounds[0].ObservedDurationMilliseconds = -1; break;
            case "future-time": report.Rounds[0].ObservedDurationMilliseconds = 999_000; break;
            case "duplicate-body": report.Rounds[0].Actors.Add(report.Rounds[0].Actors[0]); break;
            case "duplicate-death": report.Rounds[0].Deaths.Add(new() { Sequence = 2, AtMilliseconds = 1000, VictimBody = 2, KillerBody = 1 }); break;
            case "foreign-victim": report.Rounds[0].Deaths[0].VictimBody = 999; break;
            case "unknown-side": report.Rounds[0].Actors[0].Side = "Bogus"; break;
            case "unknown-kind": report.Rounds[0].Deaths[0].Kind = "Bogus"; break;
            case "round-gap": report.Rounds[0].Number = 2; break;
        }
        var before = f.Profile;
        Assert.False(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public void IdenticalFinalRetrySucceedsButChangedPayloadAndLaterRevisionDoNot()
    {
        using var f = new Fixture();
        var report = f.Capture();
        Assert.True(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
        report.Rounds[0].Deaths[0].AtMilliseconds++;
        Assert.False(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
        report.Revision++;
        Assert.False(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
    }

    [Fact]
    public void EarlierPartialHistoryCannotBeEditedOrRemoved()
    {
        using var f = new Fixture();
        var report = f.Capture(final: false);
        report.Revision++;
        report.Final = true;
        report.Rounds[0].Deaths.Clear();
        Assert.False(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("payload")]
    [InlineData("origin")]
    [InlineData("binding")]
    [InlineData("delegation")]
    [InlineData("filename")]
    public async Task ChangedTransportOrOriginNeverPays(string problem)
    {
        using var f = new Fixture();
        var capture = f.Capture();
        f.RestoreAndPublish(capture);
        var path = f.PathOf("inbox/" + capture.RunId + ".json");
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(path);
        var handoff = RewardRuntimeFiles.ReadHandoff(f.Root, f.PathOf("handoff.json"));
        switch (problem)
        {
            case "signature": signed = signed with { Signature = new string('a', 64) }; break;
            case "payload": signed = signed with { Payload = Convert.ToBase64String("{}"u8.ToArray()) }; break;
            case "origin": handoff = handoff with { RuntimeRoot = Path.GetTempPath() }; break;
            case "binding": handoff = handoff with { Binding = handoff.Binding with { Epoch = Guid.NewGuid().ToString("N") } }; break;
            case "delegation": signed = signed with { DelegationId = Guid.NewGuid().ToString("N") }; break;
            case "filename": File.Move(path, f.PathOf("inbox/" + Guid.NewGuid().ToString("N") + ".json")); break;
        }
        if (problem != "filename") File.WriteAllText(path, RewardRuntimeFiles.Json(signed));
        if (problem is "origin" or "binding") File.WriteAllText(f.PathOf("handoff.json"), RewardRuntimeFiles.Json(handoff));
        var before = f.Profile;
        var outcome = await f.Process();
        Assert.DoesNotContain(outcome.Result.Status, new[] { RewardSettlementStatus.Paid, RewardSettlementStatus.AlreadyPaid });
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public async Task OptOutDefersAlreadyEarnedReceiptsAndCanReenableWithoutNewEpoch()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        var binding = f.Binding;
        var before = f.Profile;
        Assert.False(f.Coordinator.SetConsent(Fixture.ProfileId, false).Enabled);
        Assert.False(RewardRuntimeFiles.ReadHandoff(f.Root, f.PathOf("handoff.json")).Enabled);
        Assert.Equal(RewardSettlementStatus.Deferred, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        f.Coordinator.SetConsent(Fixture.ProfileId, true);
        Assert.Equal(binding, f.Binding);
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
    }

    [Fact]
    public async Task PendingOrdinaryRunBlocksPayoutAfterCrash()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        _ = f.BeginReport(); // No positive end proof for this next run.
        var before = f.Profile;
        var result = await f.Process();
        Assert.Equal("reward-snapshot-restoration-unresolved", result.Result.Reason);
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public async Task DeletedPaymentHistoryIsNotRecreatedByEnrollment()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
        var before = f.Profile;
        File.Delete(f.PathOf("journal.json"));
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.False(File.Exists(f.PathOf("journal.json")));
    }

    [Fact]
    public async Task LostEnrollmentCannotMintAnotherEpoch()
    {
        using var f = new Fixture();
        var before = f.Profile;
        File.Delete(f.PathOf("enrollment.json"));
        // The original plan permits recovery only from its exact empty enrolled
        // document, not after native progress or payment altered that document.
        f.EditProfile(p => p["foreignMod"]!["progress"] = 321);
        var changed = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(changed, f.Profile);
        Assert.NotEqual(before, changed);
    }

    [Fact]
    public async Task InterruptedEnrollmentRecoversExactPlannedProfileOnly()
    {
        using var f = new Fixture(enroll: false);
        f.Coordinator.SetConsent(Fixture.ProfileId, true);
        var binding = new RewardBinding(Guid.NewGuid().ToString("N"), Fixture.ProfileId, Guid.NewGuid().ToString("N"));
        var before = f.Profile;
        var after = RewardProfileDocument.Enroll(before, binding);
        f.Write("enrollment-plan.json", new RewardEnrollmentPlan(1, binding, RewardIdentity.Hash(before), RewardIdentity.Hash(after)));
        f.Write("journal.json", new RewardJournal(1, binding, []));
        File.WriteAllText(f.ProfilePath, after);
        Assert.Equal("rewards-enrolled-no-pending-result", (await f.Process()).Result.Reason);
        Assert.Equal(after, f.Profile);
        Assert.Equal(binding, f.Binding);
    }

    [Fact]
    public async Task PartialNormalExitPaysKillsWithoutRoundWin()
    {
        using var f = new Fixture();
        var report = f.Capture(rule: "LocalTarget", target: 5, closed: false);
        f.RestoreAndPublish(report);
        var result = await f.Process();
        Assert.Equal(1000, result.Roubles);
        var decision = RewardStartupFiles.Read<RewardApprovalDecision>(f.PathOf("decisions/" + report.RunId + ".json"));
        Assert.Equal(1, decision.Evaluation.EligibleKills);
        Assert.Equal(0, decision.Evaluation.RoundWins);
    }

    [Fact]
    public void ClosedUnfinishedScoringRoundRemainsRejected()
    {
        using var f = new Fixture();
        var report = f.BeginReport("LocalTarget", 5, closed: true);
        Assert.Equal("unfinished-round", f.Coordinator.Capture(Fixture.ProfileId, report).Reason);
    }

    [Theory]
    [InlineData("Aborted")]
    [InlineData("NoContest")]
    public async Task AbortedAndNoContestRunsNeverPayEvenWithValidKills(string state)
    {
        using var f = new Fixture();
        var report = f.BeginReport();
        report.State = state;
        Assert.True(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
        f.RestoreAndPublish(report);
        var before = f.Profile;
        var result = await f.Process();
        Assert.Equal(0, result.Roubles);
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
        Assert.True(File.Exists(f.PathOf("imported/" + report.RunId + ".json")));
    }

    [Fact]
    public async Task FrozenPolicyCapsRewardInsteadOfDiscardingLongMatch()
    {
        using var f = new Fixture();
        var report = f.BeginReport();
        var round = report.Rounds[0];
        round.Actors.RemoveAt(1);
        round.Deaths.Clear();
        for (var i = 0; i < 250; i++)
        {
            round.Actors.Add(new() { Body = i + 2, ParticipantId = (i + 100).ToString("x24"), Side = "Opponent" });
            round.Deaths.Add(new() { Sequence = i + 1, AtMilliseconds = 1000, VictimBody = i + 2, KillerBody = 1 });
        }
        Assert.True(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
        f.RestoreAndPublish(report);
        var payment = await f.Process();
        Assert.Equal(200_000, payment.Roubles);
        Assert.Equal(20_000, payment.Experience);
    }

    [Fact]
    public async Task CrashAfterReceiptPublicationRecoversFromItsPositiveProof()
    {
        using var f = new Fixture();
        var report = f.Capture();
        f.RestoreAndPublish(report);
        File.WriteAllText(Path.Combine(f.Root, "user", "arena-reward-runs", Fixture.ProfileId, report.RunId + ".json"),
            RewardRuntimeFiles.Json(new RewardRuntimePending(1, report.ArmToken, report.RunId)));
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
    }

    [Fact]
    public async Task MissingHandoffRecoversSamePublishedDelegation()
    {
        using var f = new Fixture();
        var original = File.ReadAllText(f.PathOf("handoff.json"));
        File.Delete(f.PathOf("handoff.json"));
        await f.Process();
        Assert.Equal(original, File.ReadAllText(f.PathOf("handoff.json")));
        Assert.Single(Directory.GetFiles(f.PathOf("delegations"), "*.json"));
    }

    [Fact]
    public async Task MissingFinalReportDoesNotPoisonTheNextCleanMatch()
    {
        using var f = new Fixture();
        var missing = f.Capture(final: false);
        f.Coordinator.ObserveRestored(Fixture.ProfileId, f.Profile);
        Assert.Throws<RewardDataException>(() => f.Coordinator.FinalizePersisted(Fixture.ProfileId, missing.NativeEndIdentity, f.Profile));
        Assert.False(f.Coordinator.HasCapture(Fixture.ProfileId));
        Assert.Equal("withheld", f.Coordinator.GetStatus(Fixture.ProfileId).State);
        f.RestoreAndPublish(f.Capture());
        Assert.Equal(1000, (await f.Process()).Roubles);
    }

    [Fact]
    public async Task PhysicalWrapperCanSurviveAcrossRoundsWithNewRoundLifeIdentity()
    {
        using var f = new Fixture();
        var report = f.BeginReport("LocalTarget", 1);
        report.Rounds.Add(new() { Number = 2, Closed = true, ObservedDurationMilliseconds = 2000,
            Actors = [new() { Body = 1, ParticipantId = "aaaaaaaaaaaaaaaaaaaaaaaa", Side = "Friendly" },
                new() { Body = 3, ParticipantId = "cccccccccccccccccccccccc", Side = "Opponent" }],
            Deaths = [new() { Sequence = 2, AtMilliseconds = 1000, VictimBody = 3, KillerBody = 1 }] });
        Assert.True(f.Coordinator.Capture(Fixture.ProfileId, report).Accepted);
        f.RestoreAndPublish(report);
        var payment = await f.Process();
        Assert.Equal(22_000, payment.Roubles);
        Assert.Equal(2200, payment.Experience);
    }

    [Fact]
    public void IncompleteEarlierRoundCannotHideInsideFinalPacket()
    {
        using var f = new Fixture();
        var report = f.BeginReport("LocalTarget", 5, closed: false);
        report.Rounds.Add(new() { Number = 2, Closed = false, ObservedDurationMilliseconds = 2000,
            Actors = [new() { Body = 1, ParticipantId = "aaaaaaaaaaaaaaaaaaaaaaaa", Side = "Friendly" },
                new() { Body = 3, ParticipantId = "cccccccccccccccccccccccc", Side = "Opponent" }],
            Deaths = [new() { Sequence = 2, AtMilliseconds = 1000, VictimBody = 3, KillerBody = 1 }] });
        Assert.Equal("reward-earlier-round-open", f.Coordinator.Capture(Fixture.ProfileId, report).Reason);
    }

    [Fact]
    public async Task AlreadyAllocatedApprovalCannotRebaseAfterOrdinaryProgress()
    {
        using var f = new Fixture();
        var capture = f.Capture();
        f.RestoreAndPublish(capture);
        var origin = RewardRuntimeFiles.ReadHandoff(f.Root, f.PathOf("handoff.json"));
        var earned = RewardRuntimeInbox.ReadReceipt(f.Root, Path.GetDirectoryName(f.PathOf("handoff.json"))!, origin,
            f.PathOf("inbox/" + capture.RunId + ".json"));
        Directory.CreateDirectory(f.PathOf("allocations"));
        var candidate = earned.Evidence;
        f.Write("allocations/" + capture.RunId + ".json", new RewardAllocation(1, RewardIdentity.Hash(RewardRuntimeFiles.Json(earned)), candidate));
        f.EditProfile(p => p["foreignMod"]!["progress"] = 12345);
        var before = f.Profile;
        Assert.Equal("result-clean-profile-changed", (await f.Process()).Result.Reason);
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public void PrivateSessionCannotEnrollOrRevokeNormalConsent()
    {
        using var f = new Fixture(enroll: false);
        File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        var before = f.Profile;
        Assert.Throws<RewardDataException>(() => f.Coordinator.SetConsent(Fixture.ProfileId, true));
        Assert.Equal(before, f.Profile);
        Assert.False(File.Exists(f.PathOf("consent.json")));
    }

    [Fact]
    public void ClosedNativeStartupWindowCannotEnroll()
    {
        using var f = new Fixture(enroll: false);
        f.Coordinator.SetConsent(Fixture.ProfileId, true);
        var before = f.Profile;
        f.Window.Dispose();
        Assert.ThrowsAny<Exception>(() => RewardEnrollment.EnsureAtStartup(f.Window, Fixture.ProfileId));
        Assert.Equal(before, f.Profile);
        Assert.False(File.Exists(f.PathOf("enrollment.json")));
    }

    [Fact]
    public async Task MissingRuntimeConsentCannotFallBackToLegacyApprovalAfterOptOut()
    {
        using var f = new Fixture();
        var report = f.Capture();
        f.RestoreAndPublish(report);
        var origin = RewardRuntimeFiles.ReadHandoff(f.Root, f.PathOf("handoff.json"));
        var source = RewardRuntimeInbox.ReadReceipt(f.Root, Path.GetDirectoryName(f.PathOf("handoff.json"))!, origin,
            f.PathOf("inbox/" + report.RunId + ".json"));
        f.Write("candidate.json", source.Evidence);
        _ = RewardApprovalProducer.PrepareIfPresent(f.Window, RewardEnrollment.Read(f.Root, Fixture.ProfileId));
        f.Coordinator.SetConsent(Fixture.ProfileId, false);
        File.Delete(f.PathOf("consent.json"));
        var before = f.Profile;
        Assert.Equal("runtime-reward-consent-missing", (await f.Process()).Result.Reason);
        Assert.Equal(before, f.Profile);
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public async Task MissingInboxCannotBypassUnresolvedOrdinaryRun()
    {
        using var f = new Fixture();
        _ = f.BeginReport();
        Assert.False(Directory.Exists(f.PathOf("inbox")));
        var before = f.Profile;
        Assert.Equal("reward-snapshot-restoration-unresolved", (await f.Process()).Result.Reason);
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public async Task EnrollmentFlagRetainsConsentRequirementWhenSideFilesAreLost()
    {
        using var f = new Fixture();
        File.Delete(f.PathOf("consent.json"));
        File.Delete(f.PathOf("enrollment-plan.json"));
        File.Delete(f.PathOf("handoff.json"));
        // The remaining enrollment still explicitly identifies the runtime
        // protocol even if unrelated sidecars were lost together.
        var before = f.Profile;
        Assert.True(RewardStartupFiles.Read<RewardStartupEnrollment>(f.PathOf("enrollment.json")).RuntimeManaged);
        Assert.Equal("runtime-reward-consent-missing", (await f.Process()).Result.Reason);
        Assert.Equal(before, f.Profile);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(60000, false)]
    public void TeamDeathmatchKillsOnlyRequiresUnlimitedFrozenSelection(long timeLimit, bool accepted)
    {
        using var f = new Fixture();
        f.Coordinator.ObserveSnapshot(Fixture.ProfileId, f.Profile);
        var arm = Guid.NewGuid().ToString("N");
        var native = Protocol.BuildNativeEndIdentity("native-tdm", arm);
        var result = f.Coordinator.Begin(Fixture.ProfileId, arm, native, "aaaaaaaaaaaaaaaaaaaaaaaa", "teamdeathmatch", false, false,
            new() { ArmToken = arm, NativeEndIdentity = native, Mode = "teamdeathmatch", Rule = "KillsOnly", TimeLimitMilliseconds = timeLimit });
        Assert.Equal(accepted, result.Accepted);
    }

    [Fact]
    public async Task CrashBeforeClientBeginStillBlocksEarlierPayout()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        f.Coordinator.ObserveSnapshot(Fixture.ProfileId, f.Profile);
        var before = f.Profile;
        Assert.Equal("reward-snapshot-restoration-unresolved", (await f.Process()).Result.Reason);
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public async Task StartupRefusalRemainsVisibleInPlayerStatus()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        f.Coordinator.ObserveSnapshot(Fixture.ProfileId, f.Profile);
        var before = f.Profile;
        var outcome = await f.Process();
        Assert.Equal("reward-snapshot-restoration-unresolved", outcome.Result.Reason);
        // A restarted coordinator has no in-memory match failure to rely on.
        var restarted = new RewardRuntimeCoordinator(f.Root);
        restarted.ObserveStartupOutcome(outcome);
        Assert.Equal("withheld", restarted.GetStatus(Fixture.ProfileId).State);
        Assert.Contains(outcome.Result.Reason, restarted.GetStatus(Fixture.ProfileId).Detail);
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public void UnavailableNativeStartupIsVisibleWithoutEnrollingOrEditingTheProfile()
    {
        using var f = new Fixture(enroll: false);
        var before = f.Profile;
        f.Coordinator.ObserveStartupUnavailable("unreviewed-native-startup-version");
        var status = f.Coordinator.GetStatus(Fixture.ProfileId);
        Assert.Equal("withheld", status.State);
        Assert.Contains("unreviewed-native-startup-version", status.Detail);
        Assert.False(status.Enrolled);
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public async Task UnlimitedTeamDeathmatchForfeitPreservesEarnedKillsWithoutWinBonus()
    {
        using var f = new Fixture();
        var report = f.BeginReport(closed: false, mode: "teamdeathmatch");
        report.Rounds[0].Deaths.Add(new()
        {
            Sequence = 2, AtMilliseconds = 1500, VictimBody = 1, Kind = "LocalForfeit"
        });
        var captured = f.Coordinator.Capture(Fixture.ProfileId, report);
        Assert.True(captured.Accepted, captured.Reason);
        f.RestoreAndPublish(report);
        var result = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, result.Result.Status);
        Assert.Equal(1000, result.Roubles);
        Assert.Single(f.Ledger);
    }

    [Fact]
    public async Task SafePreStartCancellationClosesSnapshotGuard()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        f.Coordinator.ObserveSnapshot(Fixture.ProfileId, f.Profile);
        f.Coordinator.DiscardSnapshot(Fixture.ProfileId);
        Assert.Equal(RewardSettlementStatus.Paid, (await f.Process()).Result.Status);
    }

    [Fact]
    public async Task FailedStartRecoveryRequiresPositiveRestoreAndReadbackWithoutAward()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        var report = f.Capture();
        Assert.Throws<RewardDataException>(() => f.Coordinator.RecoverPersisted(Fixture.ProfileId, f.Profile));
        f.Coordinator.ObserveRestored(Fixture.ProfileId, f.Profile);
        f.Coordinator.RecoverPersisted(Fixture.ProfileId, f.Profile);
        Assert.False(File.Exists(f.PathOf("inbox/" + report.RunId + ".json")));
        Assert.Equal(1000, (await f.Process()).Roubles);
    }

    [Fact]
    public async Task GenericDiscardCannotClearAlreadyStartedRun()
    {
        using var f = new Fixture();
        f.RestoreAndPublish(f.Capture());
        _ = f.BeginReport();
        f.Coordinator.DiscardSnapshot(Fixture.ProfileId);
        Assert.Equal("reward-snapshot-restoration-unresolved", (await f.Process()).Result.Reason);
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public void DisabledRewardsAvoidSnapshotSerializationCompletely()
    {
        using var f = new Fixture(enroll: false);
        f.Coordinator.ObserveSnapshot(Fixture.ProfileId, () => throw new InvalidOperationException("must-not-serialize"));
        f.Coordinator.ObserveRestored(Fixture.ProfileId, () => throw new InvalidOperationException("must-not-serialize"));
        Assert.False(f.Coordinator.HasCapture(Fixture.ProfileId));
    }

    [Theory]
    [InlineData("zombie", true)]
    [InlineData("zombies", false)]
    public void NativeZombieIdIsValidatedBeforeInternalNormalization(string requestedMode, bool accepted)
    {
        using var f = new Fixture();
        f.Coordinator.ObserveSnapshot(Fixture.ProfileId, f.Profile);
        var arm = Guid.NewGuid().ToString("N");
        var native = Protocol.BuildNativeEndIdentity("native-zombie", arm);
        var result = f.Coordinator.Begin(Fixture.ProfileId, arm, native, "aaaaaaaaaaaaaaaaaaaaaaaa", "zombie", false, false,
            new() { ArmToken = arm, NativeEndIdentity = native, Mode = requestedMode, Rule = "KillsOnly" });
        Assert.Equal(accepted, result.Accepted);
    }

    [Fact]
    public void FailedDurableSnapshotGuardCannotAuthorizeRunFromRamOnly()
    {
        using var f = new Fixture();
        File.WriteAllText(Path.Combine(f.Root, "user", "arena-reward-snapshots"), "blocked-directory");
        Assert.ThrowsAny<Exception>(() => f.Coordinator.ObserveSnapshot(Fixture.ProfileId, f.Profile));
        Assert.False(f.Coordinator.HasCapture(Fixture.ProfileId));
        var arm = Guid.NewGuid().ToString("N");
        var native = Protocol.BuildNativeEndIdentity("native", arm);
        Assert.Equal("reward-snapshot-not-observed", f.Coordinator.Begin(Fixture.ProfileId, arm, native,
            "aaaaaaaaaaaaaaaaaaaaaaaa", "deathmatch", false, false,
            new() { ArmToken = arm, NativeEndIdentity = native, Mode = "deathmatch", Rule = "KillsOnly" }).Reason);
    }

    [Fact]
    public async Task OldSignedRoubleReceiptAndNewXpReceiptKeepTheirFrozenPoliciesAcrossRestarts()
    {
        using var f = new Fixture();
        var report = f.Capture();
        f.RestoreAndPublish(report);
        var authority = RewardRuntimeFiles.ReadHandoff(f.Root, f.PathOf("handoff.json"));
        string path = f.PathOf("inbox/" + report.RunId + ".json");
        var current = RewardRuntimeFiles.Verify(authority, RewardStartupFiles.Read<RewardSignedReceipt>(path));
        var old = current with { Evidence = current.Evidence with
        {
            Authorization = current.Evidence.Authorization with
            {
                Policy = new("arena-v1", 1000, 10_000, 20_000, 100, 200_000, CapRoubles: true)
            }
        } };
        string oldJson = RewardRuntimeFiles.Json(old);
        Assert.DoesNotContain("experience", oldJson, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, RewardRuntimeFiles.Json(RewardRuntimeFiles.Sign(authority, old)));
        var first = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, first.Result.Status);
        Assert.Equal(0, first.Experience);
        Assert.Equal(160_000, JsonNode.Parse(f.Profile)!["characters"]!["pmc"]!["Info"]!["Experience"]!.GetValue<long>());
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);

        f.RestoreAndPublish(f.Capture(rule: "LocalTarget", target: 1));
        var second = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, second.Result.Status);
        Assert.Equal(11_000, second.Roubles);
        Assert.Equal(1100, second.Experience);
        Assert.Equal(new long[] { 0, 1100 }, f.Ledger.Select(p => p.Experience));
        string after = f.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Equal(after, f.Profile);
    }

    private sealed class Fixture : IDisposable
    {
        internal const string ProfileId = "0123456789abcdef01234567";
        private const string LocalId = "aaaaaaaaaaaaaaaaaaaaaaaa";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ArenaMode.RewardRuntime.Tests", Guid.NewGuid().ToString("N"));
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", ProfileId + ".json");
        internal string Profile => File.ReadAllText(ProfilePath);
        internal RewardStartupWindow Window { get; }
        internal RewardRuntimeCoordinator Coordinator { get; }
        internal DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(20_000);
        internal RewardBinding Binding => RewardEnrollment.Read(Root, ProfileId).Binding;
        internal IReadOnlyList<RewardPayment> Ledger => RewardProfileDocument.ReadLedger(Profile, Binding, new());
        internal string PathOf(string path) => Path.Combine(Root, "user", "arena-rewards", ProfileId, path);
        internal void Write<T>(string name, T value) => File.WriteAllText(PathOf(name), RewardRuntimeFiles.Json(value));
        internal Task<RewardStartupOutcome> Process() => RewardStartupProcessor.ProcessProfileAsync(Window, ProfileId, CancellationToken.None);

        internal Fixture(bool enroll = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            File.WriteAllText(Path.Combine(Root, ".owner"), "synthetic-reward-runtime");
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15, ["Experience"] = 160_000 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            profile["foreignMod"] = new JsonObject { ["progress"] = 123 };
            File.WriteAllText(ProfilePath, profile.ToJsonString());
            Window = new(Root, () => true);
            Coordinator = new(Root, () => Now);
            if (enroll)
            {
                Coordinator.SetConsent(ProfileId, true);
                RewardEnrollment.EnsureAtStartup(Window, ProfileId);
            }
        }

        internal RewardCaptureRequest BeginReport(string rule = "KillsOnly", int target = 0, bool closed = true, string mode = "deathmatch")
        {
            Coordinator.ObserveSnapshot(ProfileId, Profile);
            var arm = Guid.NewGuid().ToString("N");
            // SPT's real location/side/timestamp shape includes one space.
            var native = Protocol.BuildNativeEndIdentity("factory4_day.Pmc " + Now.ToUnixTimeSeconds(), arm);
            var begin = Coordinator.Begin(ProfileId, arm, native, LocalId, mode, false, false,
                new() { ArmToken = arm, NativeEndIdentity = native, Mode = mode, Rule = rule, KillTarget = target });
            Assert.True(begin.Accepted, begin.Reason);
            Now = Now.AddSeconds(10);
            return new()
            {
                RunId = begin.RunId, ArmToken = arm, NativeEndIdentity = native, Revision = 1, Final = true,
                Rounds = [new() { Number = 1, Closed = closed, ObservedDurationMilliseconds = 2000,
                    Actors = [new() { Body = 1, ParticipantId = LocalId, Side = "Friendly" },
                        new() { Body = 2, ParticipantId = "bbbbbbbbbbbbbbbbbbbbbbbb", Side = "Opponent" }],
                    Deaths = [new() { Sequence = 1, AtMilliseconds = 1000, VictimBody = 2, KillerBody = 1 }] }]
            };
        }

        internal RewardCaptureRequest Capture(bool final = true, string rule = "KillsOnly", int target = 0, bool closed = true)
        {
            var report = BeginReport(rule, target, closed);
            report.Final = final;
            if (!final) report.Rounds[0].Closed = false;
            var response = Coordinator.Capture(ProfileId, report);
            Assert.True(response.Accepted, response.Reason);
            return report;
        }

        internal void RestoreAndPublish(RewardCaptureRequest report)
        {
            Coordinator.ObserveRestored(ProfileId, Profile);
            Coordinator.FinalizePersisted(ProfileId, report.NativeEndIdentity, Profile);
        }

        internal void EditProfile(Action<JsonNode> change)
        {
            var node = JsonNode.Parse(Profile)!;
            change(node);
            File.WriteAllText(ProfilePath, node.ToJsonString());
        }

        public void Dispose()
        {
            Window.Dispose();
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.RewardRuntime.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != parent || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".owner")) != "synthetic-reward-runtime") throw new InvalidOperationException("Not an owned fixture.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
