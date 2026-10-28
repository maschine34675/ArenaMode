using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

public sealed partial class RewardOfflineSessionTests
{
    [Fact]
    public void ReadyIsImmutableAndDoesNotChangeTheOriginalProfileOrReceiptAuthority()
    {
        using var f = new Fixture();
        string profile = f.Profile, handoff = File.ReadAllText(f.Metadata("handoff.json"));
        var lease = f.Ready();
        Assert.Equal(RewardRuntimeFiles.Json(lease), RewardRuntimeFiles.Json(f.Ready()));
        Assert.Equal(profile, f.Profile);
        Assert.Equal(handoff, File.ReadAllText(f.Metadata("handoff.json")));
        Assert.NotEqual(f.Original.DelegationId, lease.Authority!.DelegationId);
        Assert.Equal(lease.SessionId, lease.Authority.OfflineSessionId);
        Assert.NotEqual(lease.ControlKey, lease.Authority.Key);
        Assert.ThrowsAny<Exception>(() => RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request with { OperationId = Guid.NewGuid().ToString("N") }));
    }

    [Fact]
    public async Task ConcurrentIdenticalEntryRetriesPublishExactlyOneAuthority()
    {
        using var f = new Fixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 32).Select(async _ =>
        {
            await gate.Task;
            return RewardRuntimeFiles.Json(RewardOfflineSession.CreateReady(f.Root, f.Grant, f.Request));
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results.Distinct(StringComparer.Ordinal));
        Assert.Equal(2, Directory.GetFiles(f.Metadata("delegations"), "*.json").Length); // original + session
    }

    [Fact]
    public async Task PrivateOnOffOnWorksWithoutOriginNetworkAndOnlyStartupPaysOnce()
    {
        using var f = new Fixture();
        string profile = f.Profile;
        f.Start();
        var p = f.Coordinator();
        await p.RefreshPrivateConsentAsync(Fixture.Id);
        Assert.True(p.GetStatus(Fixture.Id).AutomaticReturn);
        var first = f.Capture(p);
        var off = await p.SetConsentAsync(Fixture.Id, false, CancellationToken.None, f.Lease!.ConsentRevision);
        Assert.False(off.Enabled);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
        Assert.True(RewardEnrollment.WantsRewards(f.Root, Fixture.Id)); // ordinary metadata remains frozen
        var on = await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None, off.ConsentRevision);
        Assert.True(on.Enabled);
        var second = f.Capture(p);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(profile, f.Profile);
        f.StopAndRecover();
        Assert.Equal(on.ConsentRevision, RewardEnrollment.ReadConsent(f.Root, Fixture.Id).Revision);
        Assert.Equal(profile, f.Profile);
        Assert.Equal(2000, (await f.Pay()).Roubles);
        var paid = f.Profile;
        await f.Pay();
        Assert.Equal(paid, f.Profile);
        Assert.Equal(2, RewardProfileDocument.ReadLedger(f.Profile, f.Original.Binding, new()).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OffSurvivesReturnWithAndWithoutReceipts(bool earn)
    {
        using var f = new Fixture();
        f.Start(); var p = f.Coordinator();
        if (earn) f.Capture(p);
        var status = await p.SetConsentAsync(Fixture.Id, false, CancellationToken.None, f.Lease!.ConsentRevision);
        Assert.False(status.Enabled);
        string before = f.Profile;
        f.StopAndRecover();
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.Equal("rewards-not-enabled", (await f.Pay()).Result.Reason);
        Assert.Equal(before, f.Profile);
        if (earn)
        {
            RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
            Assert.Equal(1000, (await f.Pay()).Roubles);
        }
    }

    [Fact]
    public async Task UnenrolledSessionCanStopAndChooseOnButCannotEnrollOrCapturePrivately()
    {
        using var f = new Fixture(enroll: false);
        string original = f.Profile;
        f.Start(); var p = f.Coordinator();
        Assert.Null(f.Lease!.Authority);
        var result = await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None, f.Lease.ConsentRevision);
        Assert.True(result.Enabled);
        Assert.False(result.Enrolled);
        p.ObserveSnapshot(Fixture.Id, f.PrivateProfile);
        Assert.False(p.HasCapture(Fixture.Id));
        Assert.False(File.Exists(f.Metadata("enrollment.json")));
        Assert.Equal(original, f.Profile);
        f.StopAndRecover();
        Assert.True(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.False(File.Exists(f.Metadata("enrollment.json")));
        Assert.Equal(original, f.Profile);
        await f.Pay();
        Assert.True(File.Exists(f.Metadata("enrollment.json")));
    }

    [Fact]
    public async Task StaleRevisionDoesNotRepeatToggle()
    {
        using var f = new Fixture();
        f.Start(); var p = f.Coordinator();
        long original = f.Lease!.ConsentRevision;
        var off = await p.SetConsentAsync(Fixture.Id, false, CancellationToken.None, original);
        var stale = await p.SetConsentAsync(Fixture.Id, true, CancellationToken.None, original);
        Assert.False(stale.Enabled);
        Assert.Equal(off.ConsentRevision, stale.ConsentRevision);
        Assert.Equal("changed", stale.State);
    }

    [Fact]
    public void PrivateOwnsOriginalLifetimeAndCannotBeReactivatedAfterCrash()
    {
        using var f = new Fixture();
        f.Start();
        Assert.Throws<IOException>(() => RewardStartupLifetimeLock.Acquire(f.Root));
        Assert.Throws<RewardOfflineException>(() => RewardOfflineSession.StartPrivate(f.Private, f.Process));
        f.Owner!.Dispose(); f.Owner = null;
        Assert.Throws<RewardOfflineException>(() => RewardOfflineSession.StartPrivate(f.Private, f.Process with { Pid = f.Process.Pid + 1 }));
        f.Recover();
        Assert.Throws<RewardOfflineException>(() => RewardOfflineSession.StartPrivate(f.Private, f.Process));
        Assert.ThrowsAny<Exception>(() => f.Ready());
    }

    [Fact]
    public void ReleasedLockDoesNotProveThePrivateProcessHasFinishedShutdown()
    {
        using var f = new Fixture();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        f.Process = new(process.Id, process.StartTime.ToUniversalTime(), []);
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.True(RewardOfflineSession.HasActive(f.Root));
    }

    [Fact]
    public void LostActivePointerDoesNotReleaseTheAuthoritativeLifetimeHandle()
    {
        using var f = new Fixture();
        f.Start();
        File.Delete(RewardOfflineSession.ActivePath(f.Root));
        Assert.Throws<IOException>(() => RewardStartupLifetimeLock.Acquire(f.Root));
        Assert.False(f.Coordinator().GetStatus(Fixture.Id).Available);
    }

    [Fact]
    public void LostPointerAfterOffAndPrivateExitRecoversTheAcknowledgedChoice()
    {
        using var f = new Fixture();
        f.Start();
        f.Owner!.SetConsent(Fixture.Id, false, f.Lease!.ConsentRevision);
        f.Owner.Dispose(); f.Owner = null;
        File.Delete(RewardOfflineSession.ActivePath(f.Root));
        f.Recover();
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
    }

    [Fact]
    public void ReadyLeasePublishedBeforePointerCanRecoverAsUnstarted()
    {
        using var f = new Fixture();
        f.Ready();
        File.Delete(RewardOfflineSession.ActivePath(f.Root));
        f.Recover();
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.True(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
    }

    [Fact]
    public void MissingPointerDoesNotHideAnInterruptedClosurePlan()
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.SetConsent(Fixture.Id, false, f.Lease!.ConsentRevision);
        f.Owner.Dispose(); f.Owner = null;
        Assert.Throws<RewardOfflineException>(() => f.Recover(stage => { if (stage == "closure") throw new IOException("interrupt"); }));
        File.Delete(RewardOfflineSession.ActivePath(f.Root));
        f.Recover();
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.True(File.Exists(Path.Combine(RewardOfflineSession.SessionDirectory(f.Root, f.Lease.SessionId), "completion.json")));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("signature")]
    [InlineData("session")]
    public void ActivatedMissingOrChangedStateCannotRevertAnOffPreference(string damage)
    {
        using var f = new Fixture();
        f.Start();
        f.Owner!.SetConsent(Fixture.Id, false, f.Lease!.ConsentRevision);
        f.Owner.Dispose(); f.Owner = null;
        string path = Path.Combine(f.Private, RewardOfflineSession.StateFile);
        if (damage == "absent") File.Delete(path);
        else
        {
            var value = JsonNode.Parse(File.ReadAllText(path))!;
            value[damage == "signature" ? "signature" : "payload"] = damage == "signature" ? new string('0', 64) : "e30=";
            File.WriteAllText(path, value.ToJsonString());
        }
        string before = f.Profile;
        f.Recover();
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.NotNull(RewardOfflineSession.ReadDiscard(f.Root, f.Lease));
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public void NeverActivatedReadyLeaseRecoversWithoutPrivateTree()
    {
        using var f = new Fixture();
        f.Ready();
        string before = f.Profile;
        f.Recover();
        Assert.False(RewardOfflineSession.HasActive(f.Root));
        Assert.True(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.Equal(before, f.Profile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostActivationProofCannotRestoreInitialOnEvenIfPrivateStateAlsoDisappeared(bool loseState)
    {
        using var f = new Fixture();
        f.Start();
        f.Owner!.SetConsent(Fixture.Id, false, f.Lease!.ConsentRevision);
        f.Owner.Dispose(); f.Owner = null;
        File.Delete(Path.Combine(RewardOfflineSession.SessionDirectory(f.Root, f.Lease.SessionId), "activation.json"));
        if (loseState) File.Delete(Path.Combine(f.Private, RewardOfflineSession.StateFile));
        Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.True(RewardOfflineSession.HasActive(f.Root));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("missing-completion")]
    [InlineData("changed-completion")]
    [InlineData("new-marker")]
    [InlineData("new-lease")]
    [InlineData("changed-state")]
    [InlineData("pending-result")]
    public void EarlyNewBuildFailureCanOnlyIgnoreACompletelyClosedPredecessor(string predecessor)
    {
        using var f = new Fixture();
        f.Start();
        f.Owner!.SetConsent(Fixture.Id, false, f.Lease!.ConsentRevision);
        f.StopAndRecover();
        var previous = f.Lease!;
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        string nextId = Guid.NewGuid().ToString("N");
        f.Grant = f.Grant with { SessionId = nextId };
        f.Request = f.Request with { SessionId = nextId, OperationId = Guid.NewGuid().ToString("N") };
        var current = f.Ready(); // normal saved and stopped; Build has not replaced the old tree
        string before = f.Profile;
        string completed = Path.Combine(RewardOfflineSession.SessionDirectory(f.Root, previous.SessionId), "completion.json");
        switch (predecessor)
        {
            case "missing-completion": File.Delete(completed); break;
            case "changed-completion": File.WriteAllText(completed, "{}"); break;
            case "new-marker":
                var marker = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Private, "arena-session.json")))!;
                marker["offlineSessionId"] = current.SessionId;
                File.WriteAllText(Path.Combine(f.Private, "arena-session.json"), marker.ToJsonString());
                break;
            case "new-lease":
                File.WriteAllText(Path.Combine(f.Private, RewardOfflineSession.LeaseFile), RewardRuntimeFiles.Json(current));
                break;
            case "changed-state":
                var state = RewardOfflineSession.ReadState(previous);
                File.WriteAllText(Path.Combine(f.Private, RewardOfflineSession.StateFile),
                    RewardRuntimeFiles.Json(RewardOfflineSession.Sign(previous, state with { Revision = state.Revision + 1, Enabled = true })));
                break;
            case "pending-result":
                string outbox = Path.Combine(f.Private, "user", "arena-rewards-outbox");
                Directory.CreateDirectory(outbox);
                File.WriteAllText(Path.Combine(outbox, Guid.NewGuid().ToString("N") + ".json"), "{}");
                break;
        }
        if (predecessor == "complete")
        {
            f.Recover();
            Assert.False(RewardOfflineSession.HasActive(f.Root));
            var closure = RewardOfflineSession.ReadClosure(f.Root, current);
            Assert.False(closure.Activated);
            Assert.True(closure.State.Enabled); // old OFF is never copied into the new session
            Assert.Empty(closure.Receipts);
        }
        else
        {
            Assert.Throws<RewardOfflineException>(() => f.Recover());
            Assert.True(RewardOfflineSession.HasActive(f.Root));
        }
        Assert.True(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.Equal(before, f.Profile);
    }

    [Theory]
    [InlineData("closure")]
    [InlineData("consent")]
    [InlineData("receipt")]
    [InlineData("imported")]
    public async Task InterruptedClosureResumesWithoutLostPreferenceOrDuplicatePayment(string stage)
    {
        using var f = new Fixture();
        f.Start(); var p = f.Coordinator();
        f.Capture(p);
        f.Owner!.SetConsent(Fixture.Id, false, f.Lease!.ConsentRevision);
        f.Owner.Dispose(); f.Owner = null;
        Assert.Throws<RewardOfflineException>(() => f.Recover(s => { if (s == stage) throw new IOException("synthetic interruption"); }));
        Assert.True(RewardOfflineSession.HasActive(f.Root));
        f.Recover();
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
        Assert.Equal("rewards-not-enabled", (await f.Pay()).Result.Reason);
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
        Assert.Equal(1000, (await f.Pay()).Roubles);
        string paid = f.Profile;
        await f.Pay();
        Assert.Equal(paid, f.Profile);
    }

    [Fact]
    public void ConsentConflictIsNotOverwrittenByRecovery()
    {
        using var f = new Fixture();
        f.Start(); f.Owner!.Dispose(); f.Owner = null;
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, false);
        Assert.Throws<RewardOfflineException>(() => f.Recover());
        Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
    }

    [Fact]
    public void SessionReceiptCannotBeAcceptedBeforeClosureOrAfterItsIndexIsRemoved()
    {
        using var f = new Fixture();
        f.Start(); var report = f.Capture(f.Coordinator());
        string outbox = Path.Combine(f.Private, "user", "arena-rewards-outbox", report.RunId + ".json");
        var signed = RewardStartupFiles.Read<RewardSignedReceipt>(outbox);
        Assert.ThrowsAny<Exception>(() => RewardOfflineSession.RequireClosedReceipt(f.Root, f.Metadata(""), f.Lease!.Authority!, signed, report.RunId));
        f.StopAndRecover();
        RewardOfflineSession.RequireClosedReceipt(f.Root, f.Metadata(""), f.Lease!.Authority!, signed, report.RunId);
        Assert.ThrowsAny<Exception>(() => RewardOfflineSession.RequireClosedReceipt(f.Root, f.Metadata(""), f.Lease.Authority!, signed, Guid.NewGuid().ToString("N")));
        File.Delete(f.Metadata("offline-delegations/" + f.Lease.Authority!.DelegationId + ".json"));
        Assert.ThrowsAny<Exception>(() => RewardOfflineSession.RequireClosedReceipt(f.Root, f.Metadata(""), f.Lease.Authority!, signed, report.RunId));
    }

    [Theory]
    [InlineData("offlineSessionId")]
    [InlineData("profileId")]
    [InlineData("sourceRuntime")]
    [InlineData("createdUtc")]
    [InlineData("port")]
    public void ChangedMarkerCannotActivate(string property)
    {
        using var f = new Fixture();
        f.Copy();
        string path = Path.Combine(f.Private, "arena-session.json");
        var marker = JsonNode.Parse(File.ReadAllText(path))!;
        if (property == "port") marker[property] = 42;
        else marker[property] = property == "sourceRuntime" ? f.Private : "changed";
        File.WriteAllText(path, marker.ToJsonString());
        Assert.Throws<RewardOfflineException>(() => RewardOfflineSession.StartPrivate(f.Private, f.Process));
        Assert.False(File.Exists(Path.Combine(RewardOfflineSession.SessionDirectory(f.Root, f.Grant.SessionId), "activation.json")));
    }

    [Fact]
    public void LegacyHandoffSerializationDoesNotAcquireOfflineProperties()
    {
        using var f = new Fixture();
        Assert.DoesNotContain("offlineSessionId", RewardRuntimeFiles.Json(f.Original));
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Id = "0123456789abcdef01234567";
        private readonly string _home = Path.Combine(Path.GetTempPath(), "ArenaMode.Offline.Tests", Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(_home, "normal");
        internal string Private => Path.Combine(_home, "private");
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Id + ".json");
        internal string Profile => File.ReadAllText(ProfilePath);
        internal string PrivateProfile => File.ReadAllText(Path.Combine(Private, "user", "profiles", Id + ".json"));
        internal string Metadata(string name) => Path.Combine(Root, "user", "arena-rewards", Id, name);
        internal RewardHandoff Original => RewardRuntimeFiles.ReadHandoff(Root, Metadata("handoff.json"));
        internal RewardEntryGrant Grant;
        internal RewardEntryRequest Request;
        internal RewardOfflineLease? Lease;
        internal RewardOfflinePrivate? Owner;
        // A known readable PID with an impossible old start simulates PID reuse,
        // without depending on another machine's protected process inventory.
        internal RewardReturnProcess Process = new(Environment.ProcessId, DateTimeOffset.UnixEpoch.AddDays(20_000), []);
        internal DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(20_001);

        internal Fixture(bool enroll = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            File.WriteAllText(Path.Combine(_home, ".owner"), "synthetic-offline");
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15, ["Experience"] = 160_000 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            File.WriteAllText(ProfilePath, profile.ToJsonString());
            RewardStartupDefaults.PrepareDirectory(Root);
            if (enroll)
            {
                RewardEnrollment.SetConsent(Root, Id, true);
                using var window = new RewardStartupWindow(Root, () => true);
                RewardEnrollment.EnsureAtStartup(window, Id);
            }
            string session = Guid.NewGuid().ToString("N");
            Grant = new(1, "arena-entry", session, Id, Convert.ToBase64String(new byte[32]), Root,
                "https://127.0.0.1:6969", new string('a', 64), Now, Now.AddMinutes(5), 100, Now.AddHours(-1), []);
            Request = new(1, session, Grant.Token, Guid.NewGuid().ToString("N"), 200, Now.AddMinutes(-1), Private, Now.ToString("O"), 6970);
        }

        internal RewardOfflineLease Ready() => Lease = RewardOfflineSession.CreateReady(Root, Grant, Request);
        internal void Copy()
        {
            Ready(); Directory.CreateDirectory(Path.Combine(Private, "user", "profiles"));
            File.Copy(ProfilePath, Path.Combine(Private, "user", "profiles", Id + ".json"));
            File.Copy(RewardOfflineSession.LeasePath(Root, Grant.SessionId), Path.Combine(Private, RewardOfflineSession.LeaseFile));
            File.WriteAllText(Path.Combine(Private, "arena-session.json"), JsonSerializer.Serialize(new
            { schema = 1, offlineSessionId = Grant.SessionId, profileId = Id, sourceRuntime = Root,
                createdUtc = Request.MarkerCreatedUtc, port = Request.Port, serverExe = Path.Combine(Private, "SPT.Server.exe") }));
        }
        internal void Start() { Copy(); Owner = RewardOfflineSession.StartPrivate(Private, Process); }
        internal RewardRuntimeCoordinator Coordinator() => new(Private, () => Now,
            (_, _, _) => throw new InvalidOperationException("Offline flow contacted the original server."));
        internal void StopAndRecover() { Owner!.Dispose(); Owner = null; Recover(); }
        internal void Recover(Action<string>? step = null)
        {
            using var gate = RewardStartupLifetimeLock.AcquireStartupAsync(Root, CancellationToken.None).GetAwaiter().GetResult();
            using var owner = RewardStartupLifetimeLock.Acquire(Root);
            using var window = new RewardStartupWindow(Root, () => true);
            RewardOfflineSession.RecoverBeforeStartup(window, step);
        }
        internal async Task<RewardStartupOutcome> Pay()
        {
            using var window = new RewardStartupWindow(Root, () => true);
            return await RewardStartupProcessor.ProcessProfileAsync(window, Id, CancellationToken.None);
        }
        internal RewardCaptureRequest Capture(RewardRuntimeCoordinator coordinator)
        {
            coordinator.ObserveSnapshot(Id, PrivateProfile);
            string arm = Guid.NewGuid().ToString("N");
            string native = Protocol.BuildNativeEndIdentity("factory4_day.Pmc " + Now.ToUnixTimeSeconds(), arm);
            var begin = coordinator.Begin(Id, arm, native, "aaaaaaaaaaaaaaaaaaaaaaaa", "deathmatch", false, false,
                new() { ArmToken = arm, NativeEndIdentity = native, Mode = "deathmatch", Rule = "KillsOnly" });
            Assert.True(begin.Accepted, begin.Reason);
            Now = Now.AddSeconds(10);
            var report = new RewardCaptureRequest
            {
                RunId = begin.RunId, ArmToken = arm, NativeEndIdentity = native, Revision = 1, Final = true,
                Rounds = [new() { Number = 1, Closed = true, ObservedDurationMilliseconds = 2000,
                    Actors = [new() { Body = 1, ParticipantId = "aaaaaaaaaaaaaaaaaaaaaaaa", Side = "Friendly" },
                        new() { Body = 2, ParticipantId = "bbbbbbbbbbbbbbbbbbbbbbbb", Side = "Opponent" }],
                    Deaths = [new() { Sequence = 1, AtMilliseconds = 1000, VictimBody = 2, KillerBody = 1 }] }]
            };
            Assert.True(coordinator.Capture(Id, report).Accepted);
            coordinator.ObserveRestored(Id, PrivateProfile);
            coordinator.FinalizePersisted(Id, native, PrivateProfile);
            return report;
        }
        public void Dispose()
        {
            Owner?.Dispose();
            string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.Offline.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(_home)) != expected || !RewardIdentity.IsGuid(Path.GetFileName(_home))
                || File.ReadAllText(Path.Combine(_home, ".owner")) != "synthetic-offline") throw new InvalidOperationException("Unowned fixture.");
            Directory.Delete(_home, true);
        }
    }
}
