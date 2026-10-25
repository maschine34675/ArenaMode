using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ArenaMode.Tests;

public sealed class RewardXpSettlementTests
{
    private static readonly RewardBinding Binding = RewardSettlementCoreTests.Binding;
    private static readonly RewardLimits Limits = new();
    private const long InitialExperience = 12_345;
    private const string LegacyFingerprint = "bc207e49ad57660d512c93114cf5db084c901f92a14eb36499c530f2368126a4";
    private static RewardClaim Claim(long sequence = 1, long experience = 1_100) => new(Binding,
        "33333333333333333333333333333333", sequence, 11_000, "arena-v2", experience);

    private static string Profile()
    {
        var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
        profile["characters"]!["pmc"]!["Info"] = new JsonObject
        { ["Experience"] = InitialExperience, ["Level"] = 4, ["Nickname"] = "SYNTHETIC ONLY" };
        profile["characters"]!["pmc"]!["TradersInfo"] = new JsonObject
        { ["synthetic-trader"] = new JsonObject { ["loyaltyLevel"] = 1, ["standing"] = 0.75 } };
        return profile.ToJsonString();
    }

    private static SyntheticRewardStore Store() => new(Binding, Profile());
    private static long Experience(string profile) => JsonNode.Parse(profile)!["characters"]!["pmc"]!["Info"]!["Experience"]!.GetValue<long>();
    private static JsonArray Messages(string profile) => JsonNode.Parse(profile)!["dialogues"]![RewardProfileDocument.SystemSenderId]!["messages"]!.AsArray();
    private static IReadOnlyList<RewardPayment> Ledger(string profile) => RewardProfileDocument.ReadLedger(profile, Binding, Limits);
    private static Task<RewardSettlementResult> Settle(SyntheticRewardStore store, RewardClaim? claim = null)
        => new RewardSettlementCore(store).SettleAsync(claim ?? Claim()).AsTask();

    [Fact]
    public async Task ExperienceMailAndReceiptAreOneProfileWriteAndOnlyExperienceChangesInCharacters()
    {
        using var store = Store();
        var original = JsonNode.Parse(store.Profile)!;
        store.Checkpoint = _ =>
        {
            var payments = Ledger(store.Profile);
            Assert.Equal(InitialExperience + payments.Sum(p => p.Experience), Experience(store.Profile));
            if (payments.Count > 0) Assert.Single(Messages(store.Profile));
        };
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store)).Status);
        Assert.Equal(1, store.ProfileWrites);
        Assert.Equal(InitialExperience + 1_100, Experience(store.Profile));
        Assert.Equal(1_100, Assert.Single(Ledger(store.Profile)).Experience);
        var message = Assert.Single(Messages(store.Profile));
        Assert.Equal("Arena reward: 11000 RUB. 1100 XP already credited to your PMC. Collect the RUB below.", message!["text"]!.GetValue<string>());
        Assert.Null(message["profileChangeEvents"]);
        Assert.Equal(11_000, message["items"]!["data"]!.AsArray().Sum(x => x!["upd"]!["StackObjectsCount"]!.GetValue<long>()));
        var final = JsonNode.Parse(store.Profile)!;
        final["characters"]!["pmc"]!["Info"]!["Experience"] = InitialExperience;
        foreach (var field in original.AsObject().Where(p => p.Key is not "dialogues" and not RewardProfileDocument.ExtensionKey))
            Assert.True(JsonNode.DeepEquals(field.Value, final[field.Key]), field.Key);
    }

    [Theory]
    [InlineData("collect")]
    [InlineData("delete")]
    [InlineData("expire")]
    public async Task CollectedOrRemovedMailAndOrdinaryProgressCannotReplayExperience(string lifecycle)
    {
        using var store = Store();
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store)).Status);
        var changed = JsonNode.Parse(store.Profile)!;
        var messages = changed["dialogues"]![RewardProfileDocument.SystemSenderId]!["messages"]!.AsArray();
        if (lifecycle == "delete") messages.Clear();
        else
        {
            messages[0]!["items"]!["data"]!.AsArray().Clear();
            messages[0]!["rewardCollected"] = lifecycle == "collect";
            messages[0]!["hasRewards"] = false;
        }
        changed["characters"]!["pmc"]!["Info"]!["Experience"] = InitialExperience + 1_100 + 500;
        File.WriteAllText(store.ProfilePath, changed.ToJsonString());
        string ordinaryProgress = store.Profile;
        using var restart = store.Fork();
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Settle(restart)).Status);
        Assert.Equal(ordinaryProgress, store.Profile);
        Assert.Equal(0, restart.ProfileWrites);
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(restart, Claim(2, 200))).Status);
        Assert.Equal(InitialExperience + 1_100 + 500 + 200, Experience(store.Profile));
    }

    [Fact]
    public async Task ChangedExperienceForSameReceiptIsAConflict()
    {
        using var store = Store();
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store)).Status);
        string before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(store, Claim(experience: 1_101))).Status);
        Assert.Equal(before, store.Profile);
        Assert.NotEqual(Claim().Fingerprint, Claim(experience: 1_101).Fingerprint);
        Assert.NotEqual(Claim().Fingerprint, Claim(experience: 0).Fingerprint);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    [InlineData(long.MaxValue)]
    public async Task InvalidClaimExperienceIsRejectedBeforeAnyWrite(long value)
    {
        using var store = Store();
        string before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Rejected, (await Settle(store, Claim(experience: value))).Status);
        Assert.Equal(before, store.Profile);
        Assert.Empty(store.Journal.Entries);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"12345\"")]
    [InlineData("1.5")]
    [InlineData("12345.0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    [InlineData("9223372036854775808")]
    [InlineData("true")]
    [InlineData("{}")]
    public async Task InvalidNativeExperienceIsQuarantinedBeforeJournalPreparation(string json)
    {
        using var store = Store();
        var profile = JsonNode.Parse(store.Profile)!;
        profile["characters"]!["pmc"]!["Info"]!["Experience"] = JsonNode.Parse(json);
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        var before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(store)).Status);
        Assert.Equal(before, store.Profile);
        Assert.Empty(store.Journal.Entries);
        Assert.Equal(0, store.ProfileWrites);
    }

    [Theory]
    [InlineData("Experience")]
    [InlineData("Info")]
    public async Task MissingNativeExperienceIsNotInvented(string field)
    {
        using var store = Store();
        var profile = JsonNode.Parse(store.Profile)!;
        var pmc = profile["characters"]!["pmc"]!.AsObject();
        if (field == "Info") pmc.Remove(field); else pmc["Info"]!.AsObject().Remove(field);
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        var before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(store)).Status);
        Assert.Equal(before, store.Profile);
        Assert.Empty(store.Journal.Entries);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ExperienceCeilingMatchesNativeInt32WithoutClamping(int extra, bool allowed)
    {
        Assert.Equal(typeof(int?), typeof(Info).GetProperty(nameof(Info.Experience))!.PropertyType);
        using var store = Store();
        var profile = JsonNode.Parse(store.Profile)!;
        profile["characters"]!["pmc"]!["Info"]!["Experience"] = int.MaxValue - 1_100 + extra;
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        var before = store.Profile;
        Assert.Equal(allowed ? RewardSettlementStatus.Paid : RewardSettlementStatus.Quarantined, (await Settle(store)).Status);
        if (allowed) Assert.Equal(int.MaxValue, Experience(store.Profile));
        else
        {
            Assert.Equal(before, store.Profile);
            Assert.Empty(store.Journal.Entries);
        }
    }

    [Theory]
    [InlineData("before-temp", false)]
    [InlineData("after-temp", false)]
    [InlineData("before-replace", false)]
    [InlineData("after-replace", true)]
    public async Task ProfileIoFailureKeepsExperienceAtomicAndRetryPaysOnlyOnce(string point, bool committed)
    {
        using var store = Store();
        store.FailOnce("profile:" + point, new IOException("synthetic XP write failure"));
        Assert.Equal(committed ? RewardSettlementStatus.Paid : RewardSettlementStatus.Deferred, (await Settle(store)).Status);
        Assert.Equal(InitialExperience + (committed ? 1_100 : 0), Experience(store.Profile));
        using var restart = store.Fork();
        Assert.Equal(committed ? RewardSettlementStatus.AlreadyPaid : RewardSettlementStatus.Paid, (await Settle(restart)).Status);
        Assert.Equal(InitialExperience + 1_100, Experience(store.Profile));
        Assert.Single(Ledger(store.Profile));
        Assert.Single(Messages(store.Profile));
    }

    [Theory]
    [InlineData("profile:before-replace", false, (int)RewardSettlementStatus.Quarantined)]
    [InlineData("profile:after-replace", true, (int)RewardSettlementStatus.AlreadyPaid)]
    [InlineData("journal:Prepared:after-replace", false, (int)RewardSettlementStatus.Paid)]
    [InlineData("journal:Attempting:before-replace", false, (int)RewardSettlementStatus.Paid)]
    [InlineData("journal:Attempting:after-replace", false, (int)RewardSettlementStatus.Quarantined)]
    [InlineData("journal:Committed:before-replace", true, (int)RewardSettlementStatus.AlreadyPaid)]
    [InlineData("journal:Committed:after-replace", true, (int)RewardSettlementStatus.AlreadyPaid)]
    public async Task CrashRecoveryNeverCreatesAnExtraExperienceDelta(string point, bool written, int expected)
    {
        using var store = Store();
        store.FailOnce(point, new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(() => Settle(store));
        Assert.Equal(InitialExperience + (written ? 1_100 : 0), Experience(store.Profile));
        using var restart = store.Fork();
        Assert.Equal((RewardSettlementStatus)expected, (await Settle(restart)).Status);
        Assert.Equal(InitialExperience + (written || expected == (int)RewardSettlementStatus.Paid ? 1_100 : 0), Experience(store.Profile));
        Assert.Equal(written ? 0 : expected == (int)RewardSettlementStatus.Paid ? 1 : 0, restart.ProfileWrites);
    }

    [Fact]
    public async Task UncommittedReceiptRequiresOriginalExperienceNotificationAsWellAsAttachments()
    {
        using var store = Store();
        store.FailOnce("journal:Committed:before-replace", new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(() => Settle(store));
        var profile = JsonNode.Parse(store.Profile)!;
        profile["dialogues"]![RewardProfileDocument.SystemSenderId]!["messages"]![0]!["text"] = "Changed XP message";
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        using var restart = store.Fork();
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(restart)).Status);
        Assert.Equal(InitialExperience + 1_100, Experience(store.Profile));
        Assert.Equal(0, restart.ProfileWrites);
    }

    [Fact]
    public async Task IndependentConcurrentReceiptsDoNotLoseOrDuplicateExperience()
    {
        using var store = Store();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            using var peer = store.Fork();
            return await Settle(peer, Claim(index % 3 + 1, 100 * (index % 3 + 1)));
        }));
        Assert.Equal(3, results.Count(r => r.Status == RewardSettlementStatus.Paid));
        Assert.Equal(9, results.Count(r => r.Status == RewardSettlementStatus.AlreadyPaid));
        Assert.Equal(InitialExperience + 600, Experience(store.Profile));
        Assert.Equal(3, Ledger(store.Profile).Count);
    }

    [Fact]
    public async Task LegacyRoubleClaimFingerprintMailAndSerializedRecordsStayByteCompatible()
    {
        var claim = Claim(experience: 0) with { RulesVersion = "arena-v1" };
        Assert.Equal(LegacyFingerprint, claim.Fingerprint);
        const string legacyClaimJson = "{\"binding\":{\"installationId\":\"11111111111111111111111111111111\",\"profileId\":\"0123456789abcdef01234567\",\"epoch\":\"22222222222222222222222222222222\"},\"runId\":\"33333333333333333333333333333333\",\"sequence\":1,\"roubles\":11000,\"rulesVersion\":\"arena-v1\",\"key\":\"33333333333333333333333333333333:1\",\"fingerprint\":\"bc207e49ad57660d512c93114cf5db084c901f92a14eb36499c530f2368126a4\"}";
        Assert.Equal(legacyClaimJson, JsonSerializer.Serialize(claim, RewardSettlementCore.JournalJson));
        Assert.Equal(claim, JsonSerializer.Deserialize<RewardClaim>(legacyClaimJson, RewardSettlementCore.JournalJson));
        // Old RUB-only profiles need no XP field; no new progression field is invented.
        using var store = new SyntheticRewardStore(Binding, RewardSettlementCoreTests.ProfileJson);
        store.FailOnce("journal:Committed:before-replace", new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(() => Settle(store, claim));
        string paid = store.Profile;
        var payment = Assert.Single(Ledger(paid));
        Assert.Equal(0, payment.Experience);
        Assert.Equal("Arena reward: 11000 RUB.", Assert.Single(Messages(paid))!["text"]!.GetValue<string>());
        Assert.DoesNotContain("experience", File.ReadAllText(store.JournalPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("experience", paid, StringComparison.OrdinalIgnoreCase);
        var oldPayment = new
        {
            payment.ReceiptKey, payment.Fingerprint, payment.Roubles, payment.MessageId, payment.PaidAtUnixSeconds
        };
        var oldJson = JsonSerializer.Serialize(oldPayment, RewardSettlementCore.JournalJson);
        Assert.Equal(oldJson, JsonSerializer.Serialize(payment, RewardSettlementCore.JournalJson));
        Assert.Equal(payment, JsonSerializer.Deserialize<RewardPayment>(oldJson, RewardSettlementCore.JournalJson));
        using var restart = store.Fork();
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Settle(restart, claim)).Status);
        Assert.Equal(paid, store.Profile);
        Assert.Equal(0, restart.ProfileWrites);
    }

    [Fact]
    public async Task LegacyPreparedReceiptPaysRoublesOnlyBeforeANewExperienceReceipt()
    {
        using var store = Store();
        var oldClaim = Claim(experience: 0) with { RulesVersion = "arena-v1" };
        store.FailOnce("journal:Prepared:after-replace", new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(() => Settle(store, oldClaim));
        Assert.DoesNotContain("experience", File.ReadAllText(store.JournalPath), StringComparison.OrdinalIgnoreCase);
        using var restart = store.Fork();
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(restart, oldClaim)).Status);
        Assert.Equal(InitialExperience, Experience(store.Profile));
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(restart, Claim(2))).Status);
        Assert.Equal(InitialExperience + 1_100, Experience(store.Profile));
        Assert.Equal(new long[] { 0, 1_100 }, Ledger(store.Profile).Select(p => p.Experience));
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Settle(restart, oldClaim)).Status);
        Assert.Equal(InitialExperience + 1_100, Experience(store.Profile));
    }

    [Fact]
    public void PaymentCannotDeclareAnotherExperienceAmountWithTheSameFingerprint()
    {
        using var store = Store();
        var claim = Claim();
        var payment = new RewardPayment(claim.Key, claim.Fingerprint, claim.Roubles,
            RewardIdentity.MessageId(Binding, claim.Key), 1_790_000_000, claim.Experience + 1);
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(store.Profile, claim, payment, Limits));
        Assert.Equal(InitialExperience, Experience(store.Profile));
        Assert.Empty(store.Journal.Entries);
    }

    [Fact]
    public void ConfiguredExperienceBoundIsEnforcedByClaimLedgerAndJournal()
    {
        var bound = Limits with { MaxExperiencePerReceipt = 100 };
        var claim = Claim(experience: 101);
        Assert.Throws<RewardDataException>(() => claim.Validate(bound));
        var payment = new RewardPayment(claim.Key, claim.Fingerprint, claim.Roubles,
            RewardIdentity.MessageId(Binding, claim.Key), 1_790_000_000, claim.Experience);
        string enrolled = RewardProfileDocument.Enroll(Profile(), Binding);
        string paid = RewardProfileDocument.Apply(enrolled, claim, payment, Limits);
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.ReadLedger(paid, Binding, bound));
        string journal = JsonSerializer.Serialize(new RewardJournal(1, Binding,
            [new(payment, RewardJournalPhase.Committed, RewardIdentity.Hash(enrolled))]), RewardSettlementCore.JournalJson);
        Assert.Throws<RewardDataException>(() => RewardSettlementCore.ParseJournal(journal, Binding, bound));
        Assert.Throws<ArgumentOutOfRangeException>(() => (Limits with { MaxExperiencePerReceipt = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Limits with { MaxExperiencePerReceipt = 1_000_001 }).Validate());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("null")]
    [InlineData("\"1100\"")]
    public async Task InvalidLedgerExperienceRefusesFurtherPayment(string invalid)
    {
        using var store = Store();
        await Settle(store);
        var profile = JsonNode.Parse(store.Profile)!;
        profile[RewardProfileDocument.ExtensionKey]!["payments"]![0]!["experience"] = JsonNode.Parse(invalid);
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        string before = store.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(store, Claim(2))).Status);
        Assert.Equal(before, store.Profile);
        Assert.Single(store.Journal.Entries);
    }

    [Fact]
    public async Task RemovedExperienceFromLedgerCannotDowngradeAnExistingPaymentToLegacy()
    {
        using var store = Store();
        await Settle(store);
        var profile = JsonNode.Parse(store.Profile)!;
        profile[RewardProfileDocument.ExtensionKey]!["payments"]![0]!.AsObject().Remove("experience");
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(store, Claim(2))).Status);
        Assert.Equal(InitialExperience + 1_100, Experience(store.Profile));
        Assert.Single(store.Journal.Entries);
    }
}
