using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed class RewardRestorationCheckpointTests
{
    [Fact]
    public void FingerprintIncludesEveryFieldAndArrayPositionButIgnoresPropertyOrderAndEscaping()
    {
        const string first = "{\"inventory\":[1,2],\"foreign\":{\"progress\":3,\"name\":\"a\"}}";
        const string reordered = " { \"foreign\": { \"name\":\"\\u0061\", \"progress\":3 }, \"inventory\":[1,2] }";
        Assert.Equal(RewardRestorationCheckpoint.Fingerprint(first), RewardRestorationCheckpoint.Fingerprint(reordered));
        Assert.NotEqual(RewardRestorationCheckpoint.Fingerprint(first), RewardRestorationCheckpoint.Fingerprint(first.Replace("[1,2]", "[2,1]")));
        Assert.NotEqual(RewardRestorationCheckpoint.Fingerprint(first), RewardRestorationCheckpoint.Fingerprint(first.Replace(":3", ":4")));
        Assert.Throws<RewardDataException>(() => RewardRestorationCheckpoint.Fingerprint("{\"a\":1,\"a\":1}"));
    }

    [Fact]
    public async Task RestartAfterObservedRestorePublishesFinalAndPaysExactlyOnceWithoutReplacingProfile()
    {
        using var f = new Fixture();
        f.Capture();
        var before = f.Profile;
        f.Observe();
        Assert.False(File.Exists(f.CheckpointFile("persisted.json")));
        f.RestartRecovery();
        Assert.Equal(before, f.Profile);
        Assert.Single(Directory.GetFiles(f.Inbox, "*.json"));
        Assert.True(File.Exists(f.CheckpointFile("complete.json")));
        var result = await RewardStartupProcessor.ProcessProfileAsync(f.Window, Fixture.Id, CancellationToken.None);
        Assert.Equal(1000, result.Roubles);
        var paid = f.Profile;
        f.RestartRecovery();
        await RewardStartupProcessor.ProcessProfileAsync(f.Window, Fixture.Id, CancellationToken.None);
        Assert.Equal(paid, f.Profile);
        Assert.Single(RewardProfileDocument.ReadLedger(f.Profile, f.Authority.Binding, new()));
    }

    [Fact]
    public void EmptyRamAndMatchingDiskDoNotInventACloneRestore()
    {
        using var f = new Fixture();
        f.Capture();
        f.RestartRecovery();
        Assert.False(Directory.Exists(f.Inbox));
        Assert.False(File.Exists(f.CheckpointFile("persisted.json")));
        Assert.Equal("pending", f.GuardState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositiveNoRewardRecoveryClosesExactGenerationEvenWithoutRunOrFinalReport(bool beginRun)
    {
        using var f = new Fixture();
        if (beginRun) f.Capture(final: false);
        else f.TakeSnapshot();
        f.Observe();
        var before = f.Profile;
        f.RestartRecovery();
        Assert.Equal("restored", f.GuardState);
        Assert.False(Directory.Exists(f.Inbox));
        Assert.Equal(before, f.Profile);
        if (beginRun) Assert.Contains("unrewarded-restored", File.ReadAllText(f.Pending));
    }

    [Theory]
    [InlineData("Aborted")]
    [InlineData("NoContest")]
    public void ExplicitNoPayFinalReportRetainsProofAndProducesNoEligibleClaim(string state)
    {
        using var f = new Fixture();
        f.Capture(state: state);
        f.Observe();
        f.RestartRecovery();
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(Path.Combine(f.Inbox, f.Report!.RunId + ".json"));
        var evidence = RewardRuntimeFiles.Verify(f.Authority, signed).Evidence;
        Assert.Equal(RewardEvaluationStatus.NoReward, RewardResultValidator.Evaluate(evidence.Authorization, evidence.Report).Status);
        Assert.Equal("restored", f.GuardState);
    }

    [Fact]
    public void MismatchedCurrentProfileRemainsByteIdenticalAndRecordsOnlyBoundedHashDiagnostics()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        f.EditProfile(p => p["foreignMod"]!["progress"] = "private-value-never-in-diagnostics");
        var before = f.Profile;
        Assert.Throws<RewardDataException>(f.RestartRecovery);
        Assert.Equal(before, f.Profile);
        Assert.False(Directory.Exists(f.Inbox));
        Assert.False(File.Exists(f.CheckpointFile("persisted.json")));
        var mismatch = File.ReadAllText(f.CheckpointFile("mismatch.json"));
        Assert.DoesNotContain("private-value-never-in-diagnostics", mismatch);
        using var document = JsonDocument.Parse(mismatch);
        Assert.Equal(64, document.RootElement.GetProperty("expected").GetString()!.Length);
        Assert.Single(document.RootElement.GetProperty("differingPathHashes").EnumerateArray());
    }

    [Fact]
    public void CheckpointsContainNoRollbackProfileOrForeignValues()
    {
        using var f = new Fixture();
        f.EditProfile(p => p["foreignMod"]!["secret"] = "foreign-sentinel-unique-value");
        f.Capture();
        f.Observe();
        f.RestartRecovery();
        foreach (var path in Directory.EnumerateFiles(f.CheckpointDirectory))
            Assert.DoesNotContain("foreign-sentinel-unique-value", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("observed.json", "checkpointHash")]
    [InlineData("persisted.json", "observationHash")]
    [InlineData("persisted.json", "profileFingerprint")]
    public void MutatedWitnessCannotAuthorizePublication(string file, string field)
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        RewardRestorationCheckpoint.Persisted(f.Root, Fixture.Id, f.Generation, f.Profile);
        var path = f.CheckpointFile(file);
        var value = JsonNode.Parse(File.ReadAllText(path))!;
        value[field] = new string('0', 64);
        File.WriteAllText(path, value.ToJsonString());
        Assert.Throws<RewardDataException>(f.RestartRecovery);
        Assert.False(Directory.Exists(f.Inbox));
    }

    [Fact]
    public void ReceiptPublicationFailureRetainsFinalAndReadbackForRestartRetry()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        File.WriteAllText(f.Inbox, "synthetic-blocker");
        Assert.ThrowsAny<Exception>(() => f.Coordinator.FinalizePersisted(Fixture.Id, f.Report!.NativeEndIdentity, f.Profile));
        Assert.True(File.Exists(f.CheckpointFile("persisted.json")));
        Assert.True(File.Exists(f.CheckpointFile("final.json")));
        Assert.False(File.Exists(f.CheckpointFile("complete.json")));
        Assert.False(f.Coordinator.HasCapture(Fixture.Id));
        File.Delete(f.Inbox);
        f.RestartRecovery();
        Assert.True(File.Exists(Path.Combine(f.Inbox, f.Report!.RunId + ".json")));
        Assert.Equal("restored", f.GuardState);
    }

    [Fact]
    public void CrashAfterReceiptBeforeTerminalMarkersIsIdempotentlyCompleted()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        f.RestartRecovery();
        var receipt = File.ReadAllText(Path.Combine(f.Inbox, f.Report!.RunId + ".json"));
        File.Delete(f.CheckpointFile("complete.json"));
        File.WriteAllText(f.Guard, RewardRuntimeFiles.Json(new RewardSnapshotGuard(1, f.Generation, "pending")));
        File.WriteAllText(f.Pending, RewardRuntimeFiles.Json(new RewardRuntimePending(1, f.Report.ArmToken, f.Report.RunId)));
        f.RestartRecovery();
        Assert.Equal(receipt, File.ReadAllText(Path.Combine(f.Inbox, f.Report.RunId + ".json")));
        Assert.Equal("restored", f.GuardState);
        Assert.True(File.Exists(f.CheckpointFile("complete.json")));
    }

    [Fact]
    public void ExistingPositiveProofSurvivesLaterOrdinaryProgressWithoutComparingToOldSnapshot()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        RewardRestorationCheckpoint.Persisted(f.Root, Fixture.Id, f.Generation, f.Profile);
        f.EditProfile(p => p["foreignMod"]!["progress"] = 987654);
        var current = f.Profile;
        f.RestartRecovery();
        Assert.Equal(current, f.Profile);
        Assert.Single(Directory.GetFiles(f.Inbox, "*.json"));
    }

    [Fact]
    public void NewSnapshotSerializationFailureCannotLeaveOldRunOrGenerationUsable()
    {
        using var f = new Fixture();
        f.Capture();
        var originalGuard = File.ReadAllText(f.Guard);
        Assert.Throws<IOException>(() => f.Coordinator.ObserveSnapshot(Fixture.Id, () => throw new IOException("synthetic serialize failure")));
        Assert.False(f.Coordinator.HasCapture(Fixture.Id));
        Assert.Equal("reward-run-not-active", f.Coordinator.Capture(Fixture.Id, f.Report!).Reason);
        var arm = Guid.NewGuid().ToString("N");
        var native = Protocol.BuildNativeEndIdentity("new", arm);
        var begin = f.Coordinator.Begin(Fixture.Id, arm, native, Fixture.LocalId, "deathmatch", false, false,
            new() { ArmToken = arm, NativeEndIdentity = native, Mode = "deathmatch", Rule = "KillsOnly" });
        Assert.Equal("reward-snapshot-not-observed", begin.Reason);
        Assert.Equal(originalGuard, File.ReadAllText(f.Guard));
    }

    [Fact]
    public void NewRaidUsesNewGenerationAfterFailedOldReadback()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        var oldGeneration = f.Generation;
        f.EditProfile(p => p["foreignMod"]!["progress"] = 456);
        Assert.Throws<RewardDataException>(() => f.Coordinator.RecoverPersisted(Fixture.Id, f.Profile));
        f.Capture();
        Assert.NotEqual(oldGeneration, f.Generation);
        var final = RewardStartupFiles.Read<RewardCheckpointFinal>(f.CheckpointFile("final.json"));
        Assert.Equal(f.Generation, final.Authorization.RestorationId);
        Assert.Equal(f.Report!.RunId, final.Authorization.RunId);
        Assert.Equal("pending", RewardStartupFiles.Read<RewardSnapshotGuard>(Path.Combine(f.Root, "user", "arena-reward-snapshots", Fixture.Id, oldGeneration + ".json")).State);
    }

    [Fact]
    public void TerminalRunIdentityConflictCannotPublishEvenWithValidRestorationProof()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        File.WriteAllText(f.Pending, RewardRuntimeFiles.Json(new RewardRuntimePending(1, Guid.NewGuid().ToString("N"), f.Report!.RunId)));
        Assert.Throws<RewardDataException>(f.RestartRecovery);
        Assert.False(Directory.Exists(f.Inbox));
    }

    [Fact]
    public void ClosedOrLoadedStartupWindowCannotWriteProof()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        using var loaded = new RewardStartupWindow(f.Root, () => false);
        Assert.Throws<RewardDataException>(() => RewardRestorationCheckpoint.RecoverAtStartup(loaded, Fixture.Id));
        f.Window.Dispose();
        Assert.Throws<RewardDataException>(f.RestartRecovery);
        Assert.False(File.Exists(f.CheckpointFile("persisted.json")));
    }

    [Fact]
    public void StoppedPrivateRecoveryRequiresExactDelegationAndNeverCopiesProfileBack()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        var before = f.Profile;
        RewardRestorationCheckpoint.RecoverPrivateStopped(f.Root, Fixture.Id,
            f.Authority with { DelegationId = Guid.NewGuid().ToString("N") });
        Assert.False(Directory.Exists(Path.Combine(f.Root, "user", "arena-rewards-outbox")));
        RewardRestorationCheckpoint.RecoverPrivateStopped(f.Root, Fixture.Id, f.Authority);
        Assert.Equal(before, f.Profile);
        Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "user", "arena-rewards-outbox"), "*.json"));
    }

    [Fact]
    public void UnprovenPrivateReadbackStaysPendingAndArchiveCannotUseOrdinaryProfileAsProof()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        var original = f.Profile;
        var authority = f.Authority;
        File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        f.EditProfile(p => p["foreignMod"]!["progress"] = 999);
        RewardRestorationCheckpoint.RecoverPrivateStopped(f.Root, Fixture.Id, authority);
        Assert.False(File.Exists(f.CheckpointFile("persisted.json")));
        Assert.Equal("pending", f.GuardState);
        var normal = Path.Combine(f.Root, "synthetic-normal");
        Directory.CreateDirectory(Path.Combine(normal, "user", "profiles"));
        File.WriteAllText(Path.Combine(normal, "user", "profiles", Fixture.Id + ".json"), original);
        var normalRewards = RewardRuntimeFiles.DirectoryFor(normal, Fixture.Id);
        Directory.CreateDirectory(normalRewards);
        File.WriteAllText(Path.Combine(normalRewards, "handoff.json"), RewardRuntimeFiles.Json(authority));
        RewardRestorationCheckpoint.ArchivePrivateEvidence(f.Root, normal, Fixture.Id);
        RewardRestorationCheckpoint.ArchivePrivateEvidence(f.Root, normal, Fixture.Id);
        using var window = new RewardStartupWindow(normal, () => true);
        RewardRestorationCheckpoint.RecoverAtStartup(window, Fixture.Id);
        var archived = RewardRestorationCheckpoint.DirectoryFor(normal, Fixture.Id, f.Generation);
        Assert.False(File.Exists(Path.Combine(archived, "persisted.json")));
        Assert.False(Directory.Exists(Path.Combine(normalRewards, "inbox")));
        Assert.Equal(File.ReadAllBytes(f.CheckpointFile("mismatch.json")), File.ReadAllBytes(Path.Combine(archived, "mismatch.json")));
        Assert.Equal(original, File.ReadAllText(Path.Combine(normal, "user", "profiles", Fixture.Id + ".json")));
    }

    [Fact]
    public void CorruptPrivateCheckpointIsArchivedByteExactlyInsteadOfLosingItsPendingGuard()
    {
        using var f = new Fixture();
        f.Capture();
        var authority = f.Authority;
        File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        File.WriteAllText(f.CheckpointFile("checkpoint.json"), "{truncated-synthetic");
        RewardRestorationCheckpoint.RecoverPrivateStopped(f.Root, Fixture.Id, authority);
        var normal = Path.Combine(f.Root, "synthetic-normal");
        Directory.CreateDirectory(normal);
        RewardRestorationCheckpoint.ArchivePrivateEvidence(f.Root, normal, Fixture.Id);
        var archived = RewardRestorationCheckpoint.DirectoryFor(normal, Fixture.Id, f.Generation);
        Assert.Equal(File.ReadAllBytes(f.CheckpointFile("checkpoint.json")), File.ReadAllBytes(Path.Combine(archived, "checkpoint.json")));
        Assert.Equal(File.ReadAllBytes(f.Pending), File.ReadAllBytes(Path.Combine(normal, "user", "arena-reward-runs", Fixture.Id, f.Report!.RunId + ".json")));
        Assert.Equal("pending", f.GuardState);
    }

    [Fact]
    public void ArchiveConflictDoesNotOverwriteExistingEvidence()
    {
        using var f = new Fixture();
        f.Capture();
        File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        var normal = Path.Combine(f.Root, "synthetic-normal");
        Directory.CreateDirectory(normal);
        RewardRestorationCheckpoint.ArchivePrivateEvidence(f.Root, normal, Fixture.Id);
        var archived = Path.Combine(RewardRestorationCheckpoint.DirectoryFor(normal, Fixture.Id, f.Generation), "checkpoint.json");
        File.WriteAllText(archived, "existing-immutable-evidence");
        Assert.Throws<RewardDataException>(() => RewardRestorationCheckpoint.ArchivePrivateEvidence(f.Root, normal, Fixture.Id));
        Assert.Equal("existing-immutable-evidence", File.ReadAllText(archived));
    }

    [Fact]
    public void InterruptedInitialCheckpointWithoutGuardDoesNotBecomeAnActiveRecoveryCase()
    {
        using var f = new Fixture();
        f.TakeSnapshot();
        File.Delete(f.Guard);
        File.Delete(f.CheckpointFile("checkpoint.json"));
        f.RestartRecovery();
        Assert.False(Directory.Exists(f.Inbox));
        Assert.False(File.Exists(f.CheckpointFile("persisted.json")));
    }

    [Fact]
    public void ProvenPrivatePublicationFailureMustRemainRetryableBeforeOfflineClosure()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        var authority = f.Authority;
        File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}");
        var outbox = Path.Combine(f.Root, "user", "arena-rewards-outbox");
        File.WriteAllText(outbox, "synthetic write blocker");
        Assert.ThrowsAny<Exception>(() => RewardRestorationCheckpoint.RecoverPrivateStopped(f.Root, Fixture.Id, authority));
        Assert.True(File.Exists(f.CheckpointFile("persisted.json")));
        Assert.False(File.Exists(f.CheckpointFile("complete.json")));
        Assert.Equal("pending", f.GuardState);
        File.Delete(outbox);
        RewardRestorationCheckpoint.RecoverPrivateStopped(f.Root, Fixture.Id, authority);
        Assert.Single(Directory.GetFiles(outbox, "*.json"));
        Assert.Equal("restored", f.GuardState);
    }

    [Fact]
    public void CanonicalInterruptedArchiveStageDoesNotPreventCaseReview()
    {
        using var f = new Fixture();
        f.Capture();
        var stage = Path.Combine(f.CheckpointDirectory, ".arena-reward-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(stage, "uncommitted bytes");
        var status = RewardRecovery.Preview(f.Root, Fixture.Id);
        Assert.True(status.CanClose, status.Detail);
    }

    [Fact]
    public void LiveDurableProofInspectionIsReadOnlyAndReturnsExactRunOnlyAfterFullProof()
    {
        using var f = new Fixture();
        f.Capture();
        Assert.False(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation));
        f.Observe();
        Assert.False(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation));
        RewardRestorationCheckpoint.Persisted(f.Root, Fixture.Id, f.Generation, f.Profile);
        var before = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        Assert.True(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation, out var run));
        Assert.Equal(f.Report!.RunId, run!.RunId);
        Assert.Equal(f.Report.ArmToken, run.ArmToken);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Order());
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.False(Directory.Exists(f.Inbox));
    }

    [Fact]
    public void LiveDurableProofInspectionRejectsWrongRunGuardAndChangedProof()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        RewardRestorationCheckpoint.Persisted(f.Root, Fixture.Id, f.Generation, f.Profile);
        File.WriteAllText(f.Pending, RewardRuntimeFiles.Json(new RewardRuntimePending(1, Guid.NewGuid().ToString("N"), f.Report!.RunId)));
        Assert.False(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation, out var run));
        Assert.Null(run);
        File.WriteAllText(f.Pending, RewardRuntimeFiles.Json(new RewardRuntimePending(1, f.Report.ArmToken, f.Report.RunId)));
        var proof = JsonNode.Parse(File.ReadAllText(f.CheckpointFile("persisted.json")))!;
        proof["observationHash"] = new string('0', 64);
        File.WriteAllText(f.CheckpointFile("persisted.json"), proof.ToJsonString());
        Assert.False(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation, out run));
        Assert.Null(run);
    }

    [Fact]
    public void DurableProofCannotHideAnOfflineReceiptWithoutClosureAuthorization()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        RewardRestorationCheckpoint.Persisted(f.Root, Fixture.Id, f.Generation, f.Profile);
        Assert.True(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation));
        var authority = f.Authority with { OfflineSessionId = Guid.NewGuid().ToString("N") };
        File.WriteAllText(Path.Combine(f.RewardDirectory, "handoff.json"), RewardRuntimeFiles.Json(authority));
        File.WriteAllText(Path.Combine(f.RewardDirectory, "delegations", authority.DelegationId + ".json"), RewardRuntimeFiles.Json(authority));
        Assert.False(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation));
        Assert.False(Directory.Exists(f.Inbox));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LiveDurableProofIncludesNoPayRestorationWithOptionalRunAndPartialTerminalGuard(bool beginRun, bool restoredGuard)
    {
        using var f = new Fixture();
        if (beginRun) f.Capture(final: false);
        else f.TakeSnapshot();
        f.Observe();
        RewardRestorationCheckpoint.Persisted(f.Root, Fixture.Id, f.Generation, f.Profile);
        if (restoredGuard)
            File.WriteAllText(f.Guard, RewardRuntimeFiles.Json(new RewardSnapshotGuard(1, f.Generation, "restored")));
        var before = f.Profile;
        Assert.True(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation, out var run));
        if (beginRun)
        {
            Assert.Equal(f.Report!.RunId, run!.RunId);
            Assert.Equal(f.Report.ArmToken, run.ArmToken);
        }
        else Assert.Null(run);
        Assert.False(Directory.Exists(f.Inbox));
        Assert.False(File.Exists(f.CheckpointFile("complete.json")));
        f.RestartRecovery();
        Assert.Equal(before, f.Profile);
        Assert.Equal("unrewarded-restored", RewardStartupFiles.Read<RewardCheckpointCompletion>(f.CheckpointFile("complete.json")).State);
    }

    [Fact]
    public void MissingPreviouslyCompletedFinalCannotMasqueradeAsNoPayProof()
    {
        using var f = new Fixture();
        f.Capture();
        f.Observe();
        f.RestartRecovery();
        File.Delete(f.CheckpointFile("final.json"));
        Assert.False(RewardRestorationCheckpoint.HasDurableProof(f.Root, Fixture.Id, f.Generation));
        Assert.Throws<RewardDataException>(f.RestartRecovery);
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Id = "0123456789abcdef01234567";
        internal const string LocalId = "aaaaaaaaaaaaaaaaaaaaaaaa";
        private readonly string _owner = Guid.NewGuid().ToString("N");
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ArenaMode.Restoration.Tests", Guid.NewGuid().ToString("N"));
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Id + ".json");
        internal string Profile => File.ReadAllText(ProfilePath);
        internal string RewardDirectory => RewardRuntimeFiles.DirectoryFor(Root, Id);
        internal string Inbox => Path.Combine(RewardDirectory, "inbox");
        internal RewardHandoff Authority => RewardRuntimeFiles.ReadHandoff(Root, Path.Combine(RewardDirectory, "handoff.json"));
        internal RewardStartupWindow Window { get; }
        internal RewardRuntimeCoordinator Coordinator { get; }
        internal RewardCaptureRequest? Report { get; private set; }
        internal string Generation { get; private set; } = "";
        internal string CheckpointDirectory => RewardRestorationCheckpoint.DirectoryFor(Root, Id, Generation);
        internal string CheckpointFile(string name) => Path.Combine(CheckpointDirectory, name);
        internal string Guard => Path.Combine(Root, "user", "arena-reward-snapshots", Id, Generation + ".json");
        internal string GuardState => RewardStartupFiles.Read<RewardSnapshotGuard>(Guard).State;
        internal string Pending => Path.Combine(Root, "user", "arena-reward-runs", Id, Report!.RunId + ".json");
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(20_000);

        internal Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            File.WriteAllText(Path.Combine(Root, ".owner"), _owner);
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15, ["Experience"] = 160_000 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            profile["foreignMod"] = new JsonObject { ["progress"] = 123 };
            File.WriteAllText(ProfilePath, profile.ToJsonString());
            Window = new(Root, () => true);
            Coordinator = new(Root, () => _now);
            Coordinator.SetConsent(Id, true);
            RewardEnrollment.EnsureAtStartup(Window, Id);
        }

        internal void TakeSnapshot()
        {
            var parent = Path.Combine(Root, "user", "arena-reward-checkpoints", Id);
            var before = Directory.Exists(parent) ? Directory.GetDirectories(parent).ToHashSet() : [];
            Coordinator.ObserveSnapshot(Id, Profile);
            Generation = Path.GetFileName(Directory.GetDirectories(parent).Single(d => !before.Contains(d)));
        }

        internal void Capture(bool final = true, string state = "Completed")
        {
            TakeSnapshot();
            var arm = Guid.NewGuid().ToString("N");
            var native = Protocol.BuildNativeEndIdentity("factory4_day.Pmc " + _now.ToUnixTimeSeconds(), arm);
            var begin = Coordinator.Begin(Id, arm, native, LocalId, "deathmatch", false, false,
                new() { ArmToken = arm, NativeEndIdentity = native, Mode = "deathmatch", Rule = "KillsOnly" });
            Assert.True(begin.Accepted, begin.Reason);
            _now = _now.AddSeconds(10);
            Report = new()
            {
                RunId = begin.RunId, ArmToken = arm, NativeEndIdentity = native, Revision = 1, Final = final, State = state,
                Rounds = [new() { Number = 1, Closed = final, ObservedDurationMilliseconds = 2000,
                    Actors = [new() { Body = 1, ParticipantId = LocalId, Side = "Friendly" },
                        new() { Body = 2, ParticipantId = "bbbbbbbbbbbbbbbbbbbbbbbb", Side = "Opponent" }],
                    Deaths = [new() { Sequence = 1, AtMilliseconds = 1000, VictimBody = 2, KillerBody = 1 }] }]
            };
            var response = Coordinator.Capture(Id, Report);
            Assert.True(response.Accepted, response.Reason);
        }

        internal void Observe() => Coordinator.ObserveRestored(Id, Profile);
        internal void RestartRecovery() => RewardRestorationCheckpoint.RecoverAtStartup(Window, Id);
        internal void EditProfile(Action<JsonNode> change)
        {
            var profile = JsonNode.Parse(Profile)!;
            change(profile);
            File.WriteAllText(ProfilePath, profile.ToJsonString());
        }

        public void Dispose()
        {
            Window.Dispose();
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.Restoration.Tests"))
                || !RewardIdentity.IsGuid(Path.GetFileName(Root)) || File.ReadAllText(Path.Combine(Root, ".owner")) != _owner)
                throw new InvalidOperationException("Not an owned synthetic fixture.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
