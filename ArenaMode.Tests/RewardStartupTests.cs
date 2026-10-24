using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardStartupTests
{
    [Fact]
    public async Task ApprovedOrdinaryProfileGetsOneDurableMailAndBoundAcknowledgement()
    {
        using var f = new StartupFixture();
        var before = JsonNode.Parse(f.Profile)!;
        var result = await f.Process();
        Assert.Equal(RewardSettlementStatus.Paid, result.Result.Status);
        Assert.Single(f.Ledger);
        var acknowledgement = RewardStartupFiles.Read<RewardStartupAcknowledgement>(f.AckPath);
        Assert.Equal(f.Claim.Key, acknowledgement.ReceiptKey);
        Assert.Equal(f.Claim.Fingerprint, acknowledgement.Fingerprint);
        Assert.Equal(result.Result.MessageId, acknowledgement.MessageId);
        Assert.True(JsonNode.DeepEquals(before["characters"], JsonNode.Parse(f.Profile)!["characters"]));
        var paid = f.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Equal(paid, f.Profile);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("blocked")]
    [InlineData("private-file")]
    [InlineData("private-directory")]
    [InlineData("loaded")]
    [InlineData("closed")]
    [InlineData("root-copy")]
    [InlineData("foreign-epoch")]
    [InlineData("changed-disk")]
    [InlineData("wipe")]
    [InlineData("invalid")]
    [InlineData("missing-character")]
    [InlineData("inraid")]
    public async Task UnsafeStateNeverPaysOrAcknowledges(string mutation)
    {
        using var f = new StartupFixture();
        switch (mutation)
        {
            case "disabled": f.WriteEnrollment(f.Enrollment with { Enabled = false }); break;
            case "blocked": f.WriteApproval(f.Approval with { State = RewardStartupApprovalState.Blocked }); break;
            case "private-file": File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{broken"); break;
            case "private-directory": Directory.CreateDirectory(Path.Combine(f.Root, "arena-session.json")); break;
            case "loaded": f.Unloaded = false; break;
            case "closed": f.Window.Dispose(); break;
            case "root-copy": f.WriteEnrollment(f.Enrollment with { RuntimeRoot = f.Root + "-other" }); break;
            case "foreign-epoch": f.WriteEnrollment(f.Enrollment with { Binding = f.Binding with { Epoch = Guid.NewGuid().ToString("N") } }); break;
            case "changed-disk": f.EditProfile(p => p["foreignMod"]!["progress"] = 999); break;
            case "wipe": f.EditProfile(p => p["info"]!["wipe"] = true, approve: true); break;
            case "invalid": f.EditProfile(p => p["info"]!["invalidOrUnloadableProfile"] = true, approve: true); break;
            case "missing-character": f.EditProfile(p => p["characters"]!["scav"]!["Info"] = null, approve: true); break;
            case "inraid": f.EditProfile(p => p["inraid"]!["location"] = "factory4_day", approve: true); break;
        }
        var before = f.Profile;
        var journal = File.ReadAllText(f.JournalPath);
        var outcome = await f.Process();
        Assert.DoesNotContain(outcome.Result.Status, new[] { RewardSettlementStatus.Paid, RewardSettlementStatus.AlreadyPaid });
        Assert.Equal(before, f.Profile);
        Assert.Equal(journal, File.ReadAllText(f.JournalPath));
        Assert.False(File.Exists(f.AckPath));
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"location\":null,\"character\":null}", true)]
    [InlineData("{\"location\":\"none\",\"character\":null}", true)]
    [InlineData("{\"location\":\"none\",\"character\":\"none\"}", true)]
    [InlineData("{\"location\":\"factory4_day\"}", false)]
    [InlineData("{\"character\":\"pmc\"}", false)]
    [InlineData("{\"location\":\"\"}", false)]
    [InlineData("{\"location\":false}", false)]
    [InlineData("[]", false)]
    [InlineData("null", false)]
    public async Task NativeNullableIdleRaidShapeIsAcceptedButActiveOrMalformedStateIsNot(string inraid, bool accepted)
    {
        using var f = new StartupFixture();
        f.EditProfile(p => p["inraid"] = JsonNode.Parse(inraid), approve: true);
        var before = f.Profile;
        var outcome = await f.Process();
        Assert.Equal(accepted ? RewardSettlementStatus.Paid : RewardSettlementStatus.Quarantined, outcome.Result.Status);
        if (!accepted) Assert.Equal(before, f.Profile);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("cleanProfileRevision")]
    [InlineData("claim")]
    [InlineData("schema")]
    public async Task MissingApprovalMembersCannotBecomeApproval(string field)
    {
        using var f = new StartupFixture();
        var json = JsonNode.Parse(File.ReadAllText(f.ApprovalPath))!.AsObject();
        json.Remove(field);
        File.WriteAllText(f.ApprovalPath, json.ToJsonString());
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.False(File.Exists(f.AckPath));
    }

    [Theory]
    [InlineData("enrollment.json")]
    [InlineData("approved.json")]
    [InlineData("journal.json")]
    public async Task MissingEnrollmentStateIsNeverRebuiltFromCopiedProfile(string name)
    {
        using var f = new StartupFixture();
        var path = Path.Combine(f.ProfileDirectory, name);
        File.Delete(path);
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(f.AckPath));
    }

    [Theory]
    [InlineData("future-schema")]
    [InlineData("duplicate-field")]
    [InlineData("null-binding")]
    [InlineData("wrong-id")]
    [InlineData("unknown-field")]
    public async Task MalformedEnrollmentCannotAuthorizePayment(string mutation)
    {
        using var f = new StartupFixture();
        var node = JsonNode.Parse(File.ReadAllText(f.EnrollmentPath))!;
        switch (mutation)
        {
            case "future-schema": node["schema"] = 2; break;
            case "null-binding": node["binding"] = null; break;
            case "wrong-id": node["binding"]!["profileId"] = "111111111111111111111111"; break;
            case "unknown-field": node["mystery"] = true; break;
        }
        var json = node.ToJsonString();
        if (mutation == "duplicate-field") json = json.Insert(1, "\"enabled\":false,");
        File.WriteAllText(f.EnrollmentPath, json);
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public async Task RecoveryConfirmsExistingPaymentWithoutRewritingNewOrdinaryProgress()
    {
        using var f = new StartupFixture();
        await f.Process();
        f.EditProfile(p => { p["foreignMod"]!["progress"] = 42; p["dialogues"]!.AsObject().Clear(); });
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.Single(f.Ledger);
    }

    [Fact]
    public async Task RecoveryLeaseTechnicallyForbidsAnyProfileWrite()
    {
        using var f = new StartupFixture();
        await f.Process();
        var store = new RewardStartupStore(f.Window, f.Enrollment, f.Approval, new());
        using var lease = await store.TryAcquireAsync(f.Binding, CancellationToken.None);
        Assert.NotNull(lease);
        var before = f.Profile;
        Assert.Throws<RewardDataException>(() => lease.TryWriteProfile(RewardIdentity.Hash(before), before));
        Assert.Equal(before, f.Profile);
    }

    [Fact]
    public async Task AcknowledgementFailureDoesNotRepeatPaymentOnNextStartup()
    {
        using var f = new StartupFixture();
        File.WriteAllText(f.AckPath, "old acknowledgement");
        using (var locked = new FileStream(f.AckPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(RewardSettlementStatus.Deferred, (await f.Process()).Result.Status);
            Assert.Single(f.Ledger);
        }
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.Equal(f.Claim.Fingerprint, RewardStartupFiles.Read<RewardStartupAcknowledgement>(f.AckPath).Fingerprint);
    }

    [Fact]
    public async Task AttemptingRecoveryOnlyWorksWithExactCommittedMail()
    {
        using var f = new StartupFixture();
        await f.Process();
        var journal = RewardSettlementCore.ParseJournal(File.ReadAllText(f.JournalPath), f.Binding, new());
        journal = journal with { Entries = [journal.Entries[0] with { Phase = RewardJournalPhase.Attempting }] };
        File.WriteAllText(f.JournalPath, JsonSerializer.Serialize(journal, RewardSettlementCore.JournalJson));
        File.Delete(f.AckPath);
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await f.Process()).Result.Status);
        File.WriteAllText(f.JournalPath, JsonSerializer.Serialize(journal, RewardSettlementCore.JournalJson));
        File.Delete(f.AckPath);
        f.EditProfile(p => p["dialogues"]!.AsObject().Clear());
        Assert.Equal(RewardSettlementStatus.Quarantined, (await f.Process()).Result.Status);
        Assert.False(File.Exists(f.AckPath));
        Assert.Single(f.Ledger);
    }

    [Fact]
    public async Task PaymentCannotUseAnotherClaimsStoreEvenWithSameBinding()
    {
        using var f = new StartupFixture();
        var before = f.Profile;
        var store = new RewardStartupStore(f.Window, f.Enrollment, f.Approval, new());
        var result = await new RewardSettlementCore(store).SettleAsync(f.Claim with { Roubles = 7 });
        Assert.Equal(RewardSettlementStatus.Quarantined, result.Status);
        Assert.Equal(before, f.Profile);
        Assert.Empty(RewardSettlementCore.ParseJournal(File.ReadAllText(f.JournalPath), f.Binding, new()).Entries);
    }

    [Fact]
    public async Task BoundaryClosingAfterAcquirePreventsFurtherWrites()
    {
        using var f = new StartupFixture();
        var store = new RewardStartupStore(f.Window, f.Enrollment, f.Approval, new());
        using var lease = await store.TryAcquireAsync(f.Binding, CancellationToken.None);
        Assert.NotNull(lease);
        f.Unloaded = false;
        Assert.Throws<RewardDataException>(() => lease.WriteJournal(File.ReadAllText(f.JournalPath)));
        Assert.Empty(f.Ledger);
    }

    [Fact]
    public void LifetimeLockExcludesAnotherServerForSameRoot()
    {
        using var f = new StartupFixture();
        using (var first = RewardStartupLifetimeLock.Acquire(f.Root))
            Assert.Throws<IOException>(() => RewardStartupLifetimeLock.Acquire(f.Root));
        using var afterShutdown = RewardStartupLifetimeLock.Acquire(f.Root);
    }

    [Fact]
    public async Task SecondStartupCannotProceedToRefusalOrNativeLoadDuringSettlement()
    {
        using var f = new StartupFixture();
        var first = await RewardStartupLifetimeLock.AcquireStartupAsync(f.Root, CancellationToken.None);
        var second = RewardStartupLifetimeLock.AcquireStartupAsync(f.Root, CancellationToken.None);
        Assert.False(second.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RewardStartupLifetimeLock.AcquireStartupAsync(f.Root, CancellationToken.None, TimeSpan.Zero));
        first.Dispose();
        using var afterSettlement = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(afterSettlement);
    }

    [Fact]
    public void ProcessImagesMustBelongToKnownUnlinkedRuntimeDirectories()
    {
        using var f = new StartupFixture();
        var ownImage = Path.Combine(f.Root, "SPT.Server.exe");
        File.WriteAllText(ownImage, "synthetic image identity, not executable");
        Assert.True(ArenaRewardStartup.CurrentImageOwnsRoot(f.Root, ownImage));
        Assert.False(ArenaRewardStartup.CurrentImageOwnsRoot(f.Root, null));
        var independent = Path.Combine(f.Root, "independent");
        Directory.CreateDirectory(independent);
        var peerImage = Path.Combine(independent, "SPT.Server.exe");
        File.WriteAllText(peerImage, "independent synthetic server");
        Assert.False(ArenaRewardStartup.CurrentImageOwnsRoot(f.Root, peerImage));
        var alias = Path.Combine(f.Root, "alias");
        Directory.CreateSymbolicLink(alias, f.Root);
        Assert.False(ArenaRewardStartup.CurrentImageOwnsRoot(f.Root, Path.Combine(alias, "SPT.Server.exe")));
        Directory.Delete(alias); // Remove the test link itself before recursive fixture cleanup.
        Assert.True(ArenaRewardStartup.IsServerPeer("SPT.Server", "SPT.Server"));
        Assert.True(ArenaRewardStartup.IsServerPeer("SPT.Server.Linux", "SPT.Server"));
        Assert.False(ArenaRewardStartup.IsServerPeer("SPT.Launcher", "SPT.Server"));
    }

    [Fact]
    public void PathsCannotEscapeThroughTraversalOrLinkedProfile()
    {
        using var f = new StartupFixture();
        Assert.Throws<RewardDataException>(() => RewardStartupFiles.RequireLocalPath(f.Root, Path.Combine(f.Root, "..", "external")));
        var target = Path.Combine(f.Root, "synthetic-target.json");
        File.WriteAllText(target, f.Profile);
        File.Delete(f.ProfilePath);
        File.CreateSymbolicLink(f.ProfilePath, target);
        Assert.Throws<RewardDataException>(() => RewardStartupFiles.RequireLocalPath(f.Root, f.ProfilePath));
        Assert.Equal(f.Profile, File.ReadAllText(target));
    }

    [Theory]
    [InlineData("native-version")]
    [InlineData("entry")]
    [InlineData("root")]
    [InlineData("loaded")]
    [InlineData("peer")]
    [InlineData("early-callback")]
    public void NativeAdmissionRejectsContractDrift(string mutation)
    {
        using var f = new StartupFixture();
        var callbacks = new List<ArenaRewardStartup.StartupCallback>
        {
            new(typeof(Watermark), typeof(IOnLoad), 0),
            new(typeof(ArenaRewardStartup), typeof(IOnLoad), 1)
        };
        Assert.Null(ArenaRewardStartup.GetAdmissionFailure(new(4, 1, 6, 0), "SPT.Server", f.Root, f.Root, 0, callbacks, false));
        if (mutation == "early-callback") callbacks.Add(new(typeof(RewardStartupTests), typeof(IOnLoad), 1));
        var failure = ArenaRewardStartup.GetAdmissionFailure(mutation == "native-version" ? new(4, 2, 0, 0) : new(4, 1, 6, 0),
            mutation == "entry" ? "testhost" : "SPT.Server", f.Root, mutation == "root" ? f.Root + "-other" : f.Root,
            mutation == "loaded" ? 1 : 0, callbacks, mutation == "peer");
        Assert.NotNull(failure);
    }

    private sealed class StartupFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ArenaMode.StartupRewards.Tests", Guid.NewGuid().ToString("N"));
        internal RewardBinding Binding { get; } = new("11111111111111111111111111111111", "0123456789abcdef01234567", "22222222222222222222222222222222");
        internal RewardClaim Claim { get; }
        internal RewardStartupEnrollment Enrollment { get; }
        internal RewardStartupApproval Approval { get; private set; }
        internal RewardStartupWindow Window { get; }
        internal bool Unloaded = true;
        internal string ProfileDirectory => Path.Combine(Root, "user", "arena-rewards", Binding.ProfileId);
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Binding.ProfileId + ".json");
        internal string JournalPath => Path.Combine(ProfileDirectory, "journal.json");
        internal string ApprovalPath => Path.Combine(ProfileDirectory, "approved.json");
        internal string EnrollmentPath => Path.Combine(ProfileDirectory, "enrollment.json");
        internal string AckPath => Path.Combine(ProfileDirectory, "acknowledged.json");
        internal string Profile => File.ReadAllText(ProfilePath);
        internal IReadOnlyList<RewardPayment> Ledger => RewardProfileDocument.ReadLedger(Profile, Binding, new());

        internal StartupFixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            Directory.CreateDirectory(ProfileDirectory);
            File.WriteAllText(Path.Combine(Root, ".synthetic-owner"), "startup-rewards");
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            profile["foreignMod"] = new JsonObject { ["progress"] = 123 };
            File.WriteAllText(ProfilePath, RewardProfileDocument.Enroll(profile.ToJsonString(), Binding));
            Claim = new(Binding, "33333333333333333333333333333333", 1, 750_001, "synthetic-v1");
            Enrollment = new(1, true, Root, Binding);
            Approval = new(1, RewardStartupApprovalState.RestoredAndApproved, Claim, RewardIdentity.Hash(Profile));
            WriteEnrollment(Enrollment);
            WriteApproval(Approval);
            File.WriteAllText(JournalPath, JsonSerializer.Serialize(new RewardJournal(1, Binding, []), RewardSettlementCore.JournalJson));
            Window = new(Root, () => Unloaded);
        }

        internal Task<RewardStartupOutcome> Process() => RewardStartupProcessor.ProcessProfileAsync(Window, Binding.ProfileId, CancellationToken.None);
        internal void WriteEnrollment(RewardStartupEnrollment value) => File.WriteAllText(EnrollmentPath, JsonSerializer.Serialize(value, RewardSettlementCore.JournalJson));
        internal void WriteApproval(RewardStartupApproval value) => File.WriteAllText(ApprovalPath, JsonSerializer.Serialize(value, RewardSettlementCore.JournalJson));
        internal void EditProfile(Action<JsonNode> action, bool approve = false)
        {
            var profile = JsonNode.Parse(Profile)!;
            action(profile);
            File.WriteAllText(ProfilePath, profile.ToJsonString());
            if (approve) { Approval = Approval with { CleanProfileRevision = RewardIdentity.Hash(Profile) }; WriteApproval(Approval); }
        }
        public void Dispose()
        {
            Window.Dispose();
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.StartupRewards.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != expected || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".synthetic-owner")) != "startup-rewards")
                throw new InvalidOperationException("Not an owned synthetic reward directory.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
