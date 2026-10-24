using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardSettlementCoreTests
{
    internal static readonly RewardBinding Binding = new("11111111111111111111111111111111", "0123456789abcdef01234567", "22222222222222222222222222222222");
    internal const string ProfileJson = """
        {
          "info":{"id":"0123456789abcdef01234567","username":"SYNTHETIC ONLY","invalidOrUnloadableProfile":false},
          "characters":{"pmc":{"_id":"0123456789abcdef01234567","Inventory":{"items":[{"_id":"000000000000000000000001","_tpl":"000000000000000000000002","upd":{"StackObjectsCount":42}}]},"Skills":{"Common":[{"Id":"Health","Progress":123}]},"Quests":[{"qid":"synthetic","status":4}]},"scav":{"synthetic":"unchanged"}},
          "dialogues":{},"spt":{"modState":{"marker":"do not replace"}},"userbuilds":{"weaponBuilds":[]},
          "foreign-mod":{"deep":[1,2,{"unknown":true}]},"inraid":{"location":"none","character":"none"}
        }
        """;
    private static readonly RewardLimits Limits = new();
    private static RewardClaim Claim(long sequence = 1) => new(Binding, "33333333333333333333333333333333", sequence, 750_001, "test-policy-v1");
    private static SyntheticRewardStore Store() => new(Binding, ProfileJson);
    private static RewardSettlementCore Core(SyntheticRewardStore store, RewardLimits? limits = null) => new(store, limits);
    private static IReadOnlyList<RewardPayment> Ledger(SyntheticRewardStore store) => RewardProfileDocument.ReadLedger(store.Profile, Binding, Limits);
    private static int MessageCount(SyntheticRewardStore store) => JsonNode.Parse(store.Profile)!["dialogues"]!.AsObject()
        .Sum(d => d.Value!["messages"]!.AsArray().Count);

    [Fact]
    public async Task PaymentAndLedgerPersistTogetherAndPreserveUnrelatedProfileState()
    {
        using var store = Store();
        var before = JsonNode.Parse(store.Profile)!.AsObject();
        store.Checkpoint = _ =>
        {
            var observed = Ledger(store);
            Assert.Equal(observed.Count, MessageCount(store));
        };
        var result = await Core(store).SettleAsync(Claim());
        Assert.Equal(RewardSettlementStatus.Paid, result.Status);
        Assert.Equal(RewardJournalPhase.Committed, Assert.Single(store.Journal.Entries).Phase);
        Assert.Equal(750_001, Assert.Single(Ledger(store)).Roubles);
        Assert.Equal(1, MessageCount(store));
        var after = JsonNode.Parse(store.Profile)!.AsObject();
        foreach (var field in before.Where(p => p.Key is not "dialogues" and not "com.maschine.ArenaMode.rewards"))
            Assert.True(JsonNode.DeepEquals(field.Value, after[field.Key]), field.Key);
    }

    [Fact]
    public async Task LostAcknowledgementAndNewCoreInstanceDoNotPayTwice()
    {
        using var store = Store();
        Assert.Equal(RewardSettlementStatus.Paid, (await Core(store).SettleAsync(Claim())).Status);
        var before = store.Profile;
        using var restart = store.Fork();
        var retry = await Core(restart).SettleAsync(Claim());
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, retry.Status);
        Assert.Equal(before, restart.Profile);
        Assert.Equal(0, restart.ProfileWrites);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("collect")]
    [InlineData("expire")]
    public async Task CommittedReceiptSurvivesMailLifecycle(string action)
    {
        using var store = Store();
        await Core(store).SettleAsync(Claim());
        var profile = JsonNode.Parse(store.Profile)!;
        var dialogues = profile["dialogues"]!.AsObject();
        var messages = dialogues.First().Value!["messages"]!.AsArray();
        if (action == "delete") dialogues.Clear();
        else if (action == "collect") { messages[0]!["rewardCollected"] = true; messages[0]!["items"] = null; }
        else { messages[0]!["maxStorageTime"] = 1; messages[0]!["dt"] = 1; }
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Single(Ledger(store));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("rules")]
    public async Task SameReceiptIdentityWithDifferentContentIsQuarantined(string field)
    {
        using var store = Store();
        await Core(store).SettleAsync(Claim());
        var profile = store.Profile;
        var changed = field == "amount" ? Claim() with { Roubles = 1 } : Claim() with { RulesVersion = "other-policy" };
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(changed)).Status);
        Assert.Equal(profile, store.Profile);
    }

    [Fact]
    public async Task ConcurrentIndependentStoresSettleOneReceiptOnlyOnce()
    {
        using var store = Store();
        var runs = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            using var peer = store.Fork();
            return await Core(peer).SettleAsync(Claim());
        }));
        var results = await Task.WhenAll(runs);
        Assert.Single(results, r => r.Status == RewardSettlementStatus.Paid);
        Assert.Equal(15, results.Count(r => r.Status == RewardSettlementStatus.AlreadyPaid));
        Assert.Single(Ledger(store));
        Assert.Equal(1, MessageCount(store));
    }

    [Fact]
    public async Task ConcurrentDistinctReceiptsPreserveEveryPayment()
    {
        using var store = Store();
        var tasks = Enumerable.Range(1, 12).Select(sequence => Task.Run(async () =>
        {
            using var peer = store.Fork();
            return await Core(peer).SettleAsync(Claim(sequence));
        }));
        Assert.All(await Task.WhenAll(tasks), r => Assert.Equal(RewardSettlementStatus.Paid, r.Status));
        Assert.Equal(12, Ledger(store).Count);
        Assert.Equal(12, MessageCount(store));
    }

    [Theory]
    [InlineData("before-temp", false)]
    [InlineData("after-temp", false)]
    [InlineData("before-replace", false)]
    [InlineData("after-replace", true)]
    public async Task ProfileIoFailureUsesDurableReadback(string point, bool committed)
    {
        using var store = Store();
        store.FailOnce("profile:" + point, new IOException("synthetic disk failure"));
        var result = await Core(store).SettleAsync(Claim());
        Assert.Equal(committed ? RewardSettlementStatus.Paid : RewardSettlementStatus.Deferred, result.Status);
        Assert.Equal(committed ? 1 : 0, Ledger(store).Count);
        Assert.Equal(committed ? RewardSettlementStatus.AlreadyPaid : RewardSettlementStatus.Paid,
            (await Core(store).SettleAsync(Claim())).Status);
        Assert.Single(Ledger(store));
    }

    [Theory]
    [InlineData("before-temp", false)]
    [InlineData("after-temp", false)]
    [InlineData("before-replace", false)]
    [InlineData("after-replace", true)]
    public async Task ProcessCrashNeverBlindlyRetriesAnAmbiguousAttempt(string point, bool committed)
    {
        using var store = Store();
        store.FailOnce("profile:" + point, new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(async () => await Core(store).SettleAsync(Claim()));
        using var restart = store.Fork();
        var result = await Core(restart).SettleAsync(Claim());
        Assert.Equal(committed ? RewardSettlementStatus.AlreadyPaid : RewardSettlementStatus.Quarantined, result.Status);
        Assert.Equal(committed ? 1 : 0, Ledger(store).Count);
        Assert.Equal(0, restart.ProfileWrites);
    }

    [Theory]
    [InlineData("Prepared:after-replace", (int)RewardSettlementStatus.Paid)]
    [InlineData("Attempting:before-replace", (int)RewardSettlementStatus.Paid)]
    [InlineData("Attempting:after-replace", (int)RewardSettlementStatus.Quarantined)]
    [InlineData("Committed:before-replace", (int)RewardSettlementStatus.AlreadyPaid)]
    [InlineData("Committed:after-replace", (int)RewardSettlementStatus.AlreadyPaid)]
    public async Task JournalIoFailuresDoNotCreateFalseAcknowledgements(string point, int expectedRetry)
    {
        using var store = Store();
        store.FailOnce("journal:" + point, new IOException("synthetic journal failure"));
        var result = await Core(store).SettleAsync(Claim());
        Assert.Equal(RewardSettlementStatus.Deferred, result.Status);
        Assert.Null(result.MessageId);
        using var restart = store.Fork();
        Assert.Equal((RewardSettlementStatus)expectedRetry, (await Core(restart).SettleAsync(Claim())).Status);
        Assert.InRange(Ledger(store).Count, 0, 1);
    }

    [Fact]
    public async Task SaveReturningSuccessWithoutWritingCannotAcknowledgePayment()
    {
        using var store = Store();
        store.SkipProfileWrite = true; // Models the native failed-save hash cache hazard.
        var result = await Core(store).SettleAsync(Claim());
        Assert.Equal(RewardSettlementStatus.Deferred, result.Status);
        Assert.Null(result.MessageId);
        Assert.Empty(Ledger(store));
        Assert.Equal(RewardJournalPhase.Prepared, Assert.Single(store.Journal.Entries).Phase);
        store.SkipProfileWrite = false;
        Assert.Equal(RewardSettlementStatus.Paid, (await Core(store).SettleAsync(Claim())).Status);
    }

    [Theory]
    [InlineData((int)RewardJournalPhase.Prepared)]
    [InlineData((int)RewardJournalPhase.Attempting)]
    [InlineData((int)RewardJournalPhase.Committed)]
    public async Task JournalNoopIsDetectedBeforeAcknowledging(int phaseValue)
    {
        var phase = (RewardJournalPhase)phaseValue;
        using var store = Store();
        store.SkipJournalPhase = phase;
        Assert.Equal(RewardSettlementStatus.Deferred, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(phase == RewardJournalPhase.Committed ? 1 : 0, Ledger(store).Count);
        store.SkipJournalPhase = null;
        Assert.Equal(phase == RewardJournalPhase.Committed ? RewardSettlementStatus.AlreadyPaid : RewardSettlementStatus.Paid,
            (await Core(store).SettleAsync(Claim())).Status);
        Assert.Single(Ledger(store));
    }

    [Fact]
    public async Task UnreadableProfileAfterReplaceDefersThenRecoversExistingPayment()
    {
        using var store = Store();
        store.Checkpoint = point =>
        {
            if (point == "profile:after-replace") store.FailOnce("profile:read", new IOException("read unavailable"));
        };
        Assert.Equal(RewardSettlementStatus.Deferred, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Single(Ledger(store));
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(1, store.ProfileWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredProfileIsQuarantinedEvenForFreshReceipt(bool fresh)
    {
        using var store = Store();
        var backup = store.Profile;
        await Core(store).SettleAsync(Claim());
        File.WriteAllText(store.ProfilePath, backup);
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim(fresh ? 2 : 1))).Status);
        Assert.Equal(backup, store.Profile);
    }

    [Fact]
    public async Task MissingJournalEntryForExistingLedgerIsQuarantined()
    {
        using var store = Store();
        var oldJournal = File.ReadAllText(store.JournalPath);
        await Core(store).SettleAsync(Claim());
        File.WriteAllText(store.JournalPath, oldJournal);
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim(2))).Status);
        Assert.Single(Ledger(store));
    }

    [Theory]
    [InlineData("{}")] // Missing journal is not first-time enrollment.
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("{\"schema\":1,\"schema\":2}")]
    public async Task CorruptJournalDoesNotModifyProfile(string json)
    {
        using var store = Store();
        var before = store.Profile;
        File.WriteAllText(store.JournalPath, json);
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(before, store.Profile);
    }

    [Fact]
    public async Task NewProfileGenerationWithSameNativeIdCannotReceiveOldReceipt()
    {
        using var store = Store();
        var nextEpoch = Binding with { Epoch = "44444444444444444444444444444444" };
        File.WriteAllText(store.ProfilePath, RewardProfileDocument.Enroll(ProfileJson, nextEpoch));
        var before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(before, store.Profile);
    }

    [Fact]
    public async Task UnavailableBoundaryDoesNotTouchEitherFile()
    {
        using var store = Store();
        var profile = store.Profile;
        var journal = File.ReadAllText(store.JournalPath);
        store.Available = false;
        Assert.Equal(RewardSettlementStatus.Deferred, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(profile, store.Profile);
        Assert.Equal(journal, File.ReadAllText(store.JournalPath));
    }

    [Fact]
    public async Task CancellationBeforeAcquiringLeavesNoReceipt()
    {
        using var store = Store();
        Assert.Equal(RewardSettlementStatus.Deferred,
            (await Core(store).SettleAsync(Claim(), new CancellationToken(canceled: true))).Status);
        Assert.Empty(store.Journal.Entries);
        Assert.Empty(Ledger(store));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task InvalidAmountsCannotMutateAnything(long amount)
    {
        using var store = Store();
        var before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Rejected, (await Core(store).SettleAsync(Claim() with { Roubles = amount })).Status);
        Assert.Equal(before, store.Profile);
        Assert.Empty(store.Journal.Entries);
    }

    [Fact]
    public async Task LedgerCapacityNeverPrunesOldReceipts()
    {
        using var store = Store();
        var core = Core(store, new RewardLimits(MaxReceipts: 1));
        Assert.Equal(RewardSettlementStatus.Paid, (await core.SettleAsync(Claim())).Status);
        Assert.Equal(RewardSettlementStatus.Rejected, (await core.SettleAsync(Claim(2))).Status);
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await core.SettleAsync(Claim())).Status);
        Assert.Single(Ledger(store));
    }

    [Fact]
    public async Task PendingReceiptIsRetriedBeforeUnrelatedPayment()
    {
        using var store = Store();
        store.SkipProfileWrite = true;
        await Core(store).SettleAsync(Claim());
        store.SkipProfileWrite = false;
        Assert.Equal(RewardSettlementStatus.Deferred, (await Core(store).SettleAsync(Claim(2))).Status);
        Assert.Equal(RewardSettlementStatus.Paid, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(RewardSettlementStatus.Paid, (await Core(store).SettleAsync(Claim(2))).Status);
    }

    [Fact]
    public async Task RevisionConflictDoesNotOverwriteForeignChanges()
    {
        using var store = Store();
        var foreign = JsonNode.Parse(store.Profile)!;
        foreign["foreign-mod"]!["newProgress"] = 12345;
        var expected = foreign.ToJsonString();
        store.Checkpoint = point =>
        {
            if (point == "journal:Attempting:after-replace") File.WriteAllText(store.ProfilePath, expected);
        };
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(expected, store.Profile);
        Assert.Equal(0, store.ProfileWrites);
    }

    [Theory]
    [InlineData("phase")]
    [InlineData("beforeRevision")]
    [InlineData("payment")]
    public async Task MissingJournalFieldsNeverDowngradeAttemptingToPrepared(string field)
    {
        using var store = Store();
        store.FailOnce("profile:before-temp", new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(async () => await Core(store).SettleAsync(Claim()));
        var journal = JsonNode.Parse(File.ReadAllText(store.JournalPath))!;
        journal["entries"]![0]!.AsObject().Remove(field);
        File.WriteAllText(store.JournalPath, journal.ToJsonString());
        var before = store.Profile;
        var writes = store.ProfileWrites;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(before, store.Profile);
        Assert.Equal(writes, store.ProfileWrites);
    }

    [Theory]
    [InlineData("future-schema")]
    [InlineData("wrong-binding")]
    [InlineData("unknown-field")]
    [InlineData("null-entries")]
    [InlineData("null-entry")]
    [InlineData("duplicate-receipt")]
    [InlineData("unknown-phase")]
    public async Task MalformedJournalIsQuarantinedWithoutRewriting(string mutation)
    {
        using var store = Store();
        await Core(store).SettleAsync(Claim());
        var journal = JsonNode.Parse(File.ReadAllText(store.JournalPath))!;
        switch (mutation)
        {
            case "future-schema": journal["schema"] = 2; break;
            case "wrong-binding": journal["binding"]!["epoch"] = "44444444444444444444444444444444"; break;
            case "unknown-field": journal["unreviewed"] = true; break;
            case "null-entries": journal["entries"] = null; break;
            case "null-entry": journal["entries"]![0] = null; break;
            case "duplicate-receipt": journal["entries"]!.AsArray().Add(journal["entries"]![0]!.DeepClone()); break;
            case "unknown-phase": journal["entries"]![0]!["phase"] = 99; break;
        }
        var json = journal.ToJsonString();
        File.WriteAllText(store.JournalPath, json);
        var before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim(2))).Status);
        Assert.Equal(before, store.Profile);
        Assert.Equal(json, File.ReadAllText(store.JournalPath));
    }

    [Fact]
    public async Task PendingCommitWithChangedMailRequiresReconciliation()
    {
        using var store = Store();
        store.FailOnce("journal:Committed:before-replace", new IOException("ack lost"));
        Assert.Equal(RewardSettlementStatus.Deferred, (await Core(store).SettleAsync(Claim())).Status);
        var profile = JsonNode.Parse(store.Profile)!;
        profile["dialogues"]!.AsObject().Clear();
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Single(Ledger(store));
        Assert.Equal(0, MessageCount(store));
    }

    [Fact]
    public async Task CancellationAfterPreparationCompletesDurableTransaction()
    {
        using var store = Store();
        using var cancellation = new CancellationTokenSource();
        store.Checkpoint = point =>
        {
            if (point == "journal:Prepared:after-replace") cancellation.Cancel();
        };
        Assert.Equal(RewardSettlementStatus.Paid, (await Core(store).SettleAsync(Claim(), cancellation.Token)).Status);
        Assert.Equal(RewardJournalPhase.Committed, Assert.Single(store.Journal.Entries).Phase);
        Assert.Single(Ledger(store));
    }

    [Fact]
    public async Task WaitingForWriterBoundaryCanBeCancelledWithoutWrites()
    {
        using var store = Store();
        using var lease = await store.TryAcquireAsync(Binding, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var peer = store.Fork();
        Assert.Equal(RewardSettlementStatus.Deferred, (await Core(peer).SettleAsync(Claim(), cancellation.Token)).Status);
        Assert.Empty(store.Journal.Entries);
        Assert.Empty(Ledger(store));
    }

    [Fact]
    public async Task OrdinaryProgressAfterPreparedFailureIsPreservedAndQuarantined()
    {
        using var store = Store();
        store.SkipProfileWrite = true;
        await Core(store).SettleAsync(Claim());
        var profile = JsonNode.Parse(store.Profile)!;
        profile["foreign-mod"]!["ordinaryProgress"] = 987;
        var changed = profile.ToJsonString();
        File.WriteAllText(store.ProfilePath, changed);
        store.SkipProfileWrite = false;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(changed, store.Profile);
    }

    [Fact]
    public async Task InvalidProfileCannotCreatePendingJournal()
    {
        using var store = Store();
        var profile = JsonNode.Parse(store.Profile)!;
        profile["info"]!["invalidOrUnloadableProfile"] = true;
        var changed = profile.ToJsonString();
        File.WriteAllText(store.ProfilePath, changed);
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Core(store).SettleAsync(Claim())).Status);
        Assert.Equal(changed, store.Profile);
        Assert.Empty(store.Journal.Entries);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("sequence")]
    [InlineData("rules")]
    [InlineData("profile")]
    [InlineData("origin")]
    [InlineData("epoch")]
    public async Task InvalidIdentityIsRejectedBeforeStoreAccess(string field)
    {
        using var store = Store();
        var claim = field switch
        {
            "run" => Claim() with { RunId = "../path" },
            "sequence" => Claim() with { Sequence = 0 },
            "rules" => Claim() with { RulesVersion = "v1\ninjected" },
            "profile" => Claim() with { Binding = Binding with { ProfileId = "../other-profile" } },
            "origin" => Claim() with { Binding = Binding with { InstallationId = "00000000000000000000000000000000" } },
            _ => Claim() with { Binding = Binding with { Epoch = "" } }
        };
        Assert.Equal(RewardSettlementStatus.Rejected, (await Core(store).SettleAsync(claim)).Status);
        Assert.Empty(store.Journal.Entries);
    }

    [Fact]
    public async Task UnexpectedChangeAfterProfileReplaceIsNotAcknowledgedOrOverwritten()
    {
        using var store = Store();
        string? changed = null;
        store.Checkpoint = point =>
        {
            if (point != "profile:after-replace") return;
            var profile = JsonNode.Parse(store.Profile)!;
            profile["foreign-mod"]!["concurrentProgress"] = 2468;
            changed = profile.ToJsonString();
            File.WriteAllText(store.ProfilePath, changed);
        };
        var result = await Core(store).SettleAsync(Claim());
        Assert.Equal(RewardSettlementStatus.Quarantined, result.Status);
        Assert.Null(result.MessageId);
        Assert.Equal(changed, store.Profile);
        Assert.Equal(RewardJournalPhase.Attempting, Assert.Single(store.Journal.Entries).Phase);
        Assert.Equal(1, store.ProfileWrites);
        Assert.Single(Ledger(store));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingEnrolledFileIsQuarantinedAndNeverRecreated(bool missingProfile)
    {
        using var store = Store();
        Assert.Equal(RewardSettlementStatus.Paid, (await Core(store).SettleAsync(Claim())).Status);
        var missing = missingProfile ? store.ProfilePath : store.JournalPath;
        var remaining = missingProfile ? store.JournalPath : store.ProfilePath;
        var preserved = File.ReadAllText(remaining);
        File.Delete(missing);
        var result = await Core(store).SettleAsync(Claim(2));
        Assert.Equal(RewardSettlementStatus.Quarantined, result.Status);
        Assert.Equal("durable-state-missing", result.Reason);
        Assert.Null(result.MessageId);
        Assert.False(File.Exists(missing));
        Assert.Equal(preserved, File.ReadAllText(remaining));
    }
}
