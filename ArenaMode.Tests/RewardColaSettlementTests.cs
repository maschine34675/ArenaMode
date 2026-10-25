using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

public sealed class RewardColaSettlementTests
{
    private static readonly RewardBinding Binding = RewardSettlementCoreTests.Binding;
    private static readonly RewardLimits Limits = new();
    private static readonly string Mixed = RewardColaDeltaTests.AchievementDelta("a_first", "a_lvl5", "a_all");
    private static string Profile()
    {
        var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
        profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Experience"] = 100, ["Level"] = 1 };
        profile["characters"]!["pmc"]!["Quests"]![0]!["qid"] = "000000000000000000000099";
        profile["characters"]!["pmc"]!["Quests"]![0]!["startTime"] = 0;
        profile["characters"]!["pmc"]!["Quests"]![0]!["statusTimers"] = new JsonObject();
        return profile.ToJsonString();
    }
    private static RewardClaim Claim(long sequence = 1, string? cola = null, long rub = 11_000, long xp = 100)
        => new(Binding, "33333333333333333333333333333333", sequence, rub, "arena-v3", xp, cola ?? Mixed);
    private static SyntheticRewardStore Store() => new(Binding, Profile());
    private static Task<RewardSettlementResult> Settle(SyntheticRewardStore store, RewardClaim? claim = null)
        => new RewardSettlementCore(store).SettleAsync(claim ?? Claim()).AsTask();
    private static JsonArray Messages(string profile) => JsonNode.Parse(profile)!["dialogues"]![RewardProfileDocument.SystemSenderId]!["messages"]!.AsArray();
    private static JsonArray Items(string profile) => Messages(profile).SelectMany(m => m!["items"]!["data"]!.AsArray()).Aggregate(new JsonArray(), (a, n) => { a.Add(n!.DeepClone()); return a; });
    private static IReadOnlyList<RewardPayment> Ledger(string profile) => RewardProfileDocument.ReadLedger(profile, Binding, Limits);
    private static JsonObject Achievements(string profile) => JsonNode.Parse(profile)!["characters"]!["pmc"]!["Achievements"]!.AsObject();

    [Fact]
    public async Task NativeAchievementsCatalogItemsXpAndReceiptCommitInOnePreservingWrite()
    {
        using var store = Store();
        var before = JsonNode.Parse(store.Profile)!;
        store.Checkpoint = _ =>
        {
            var ledger = Ledger(store.Profile);
            if (ledger.Count == 0) Assert.Null(JsonNode.Parse(store.Profile)!["characters"]!["pmc"]!["Achievements"]);
            else { Assert.Equal(3, Achievements(store.Profile).Count); Assert.Single(Messages(store.Profile)); }
        };
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store)).Status);
        Assert.Equal(1, store.ProfileWrites);
        var payment = Assert.Single(Ledger(store.Profile));
        Assert.Equal(3, RewardColaProfile.Validate(payment.Cola!).Count);
        Assert.Equal(payment, Assert.Single(store.Journal.Entries).Payment);
        Assert.True(RewardProfileDocument.HasOriginalMail(store.Profile, payment, Limits));
        Assert.All(Achievements(store.Profile), entry => Assert.Equal(payment.PaidAtUnixSeconds, entry.Value!.GetValue<long>()));
        var items = Items(store.Profile);
        Assert.Equal(9, items.Count); // base RUB + achievement RUB + four GP + three bottles
        Assert.Equal(36_000, items.Where(i => i!["_tpl"]!.GetValue<string>() == RewardProfileDocument.RoubleTemplateId)
            .Sum(i => i!["upd"]!["StackObjectsCount"]!.GetValue<long>()));
        Assert.Equal(4, items.Count(i => i!["_tpl"]!.GetValue<string>() == RewardColaCatalog.GpCoin));
        Assert.Equal(3, items.Count(i => i!["_tpl"]!.GetValue<string>() == "6a1c00000000000000000c01"));
        Assert.False(items[0]!["upd"]!["SpawnedInSession"]!.GetValue<bool>());
        Assert.All(items.Skip(1), i => Assert.True(i!["upd"]!["SpawnedInSession"]!.GetValue<bool>()));
        Assert.Equal(items.Count, items.Select(i => i!["_id"]!.GetValue<string>()).Distinct().Count());
        var restored = JsonNode.Parse(store.Profile)!;
        restored["characters"]!["pmc"]!.AsObject().Remove("Achievements");
        restored["characters"]!["pmc"]!["Info"]!["Experience"] = 100;
        restored["dialogues"] = before["dialogues"]!.DeepClone();
        restored[RewardProfileDocument.ExtensionKey] = before[RewardProfileDocument.ExtensionKey]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(before, restored));
    }

    [Fact]
    public async Task DuplicateRestartAndOverlappingMatchesNeverRepeatAchievementRewards()
    {
        using var store = Store();
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store)).Status);
        var first = store.Profile;
        using var restart = store.Fork();
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Settle(restart)).Status);
        Assert.Equal(first, store.Profile);
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(restart, Claim(2))).Status);
        Assert.Equal(3, Achievements(store.Profile).Count);
        Assert.Equal(10, Items(store.Profile).Count); // only new base RUB in second receipt
        Assert.Empty(RewardColaProfile.Validate(Ledger(store.Profile)[1].Cola!));
    }

    [Fact]
    public async Task ExistingOrdinaryNativeAchievementWinsEvenWhenForeignFlagWasMissing()
    {
        using var store = Store();
        var profile = JsonNode.Parse(store.Profile)!;
        profile["characters"]!["pmc"]!["Achievements"] = new JsonObject
        { [RewardColaCatalog.Achievements["a_first"].Id] = 1_700_000_000 };
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store, Claim(cola: RewardColaDeltaTests.AchievementDelta("a_first"), rub: 0, xp: 0))).Status);
        Assert.Equal(1_700_000_000, Achievements(store.Profile).Single().Value!.GetValue<long>());
        Assert.Empty(Items(store.Profile));
        Assert.Empty(RewardColaProfile.Validate(Assert.Single(Ledger(store.Profile)).Cola!));
    }

    [Fact]
    public async Task ResearchOnlyReceiptCanHaveNoCurrencyAndAddsInformationalMailWithoutAttachmentBadge()
    {
        using var store = Store();
        var claim = Claim(cola: RewardColaDeltaTests.XpDelta(), rub: 0, xp: 0);
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store, claim)).Status);
        var message = Assert.Single(Messages(store.Profile));
        Assert.False(message!["hasRewards"]!.GetValue<bool>());
        Assert.Empty(Items(store.Profile));
        var dialogue = JsonNode.Parse(store.Profile)!["dialogues"]![RewardProfileDocument.SystemSenderId]!;
        Assert.Equal(1, dialogue["new"]!.GetValue<int>());
        Assert.Equal(0, dialogue["attachmentsNew"]!.GetValue<int>());
        Assert.True(RewardProfileDocument.HasOriginalMail(store.Profile, Assert.Single(Ledger(store.Profile)), Limits));
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Settle(store, claim)).Status);
        Assert.Throws<RewardDataException>(() => (claim with { Cola = null }).Validate(Limits));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeProfileSerializationRetainsMixedAndInformationalMailAndAchievementProof(bool researchOnly)
    {
        using var store = Store();
        var claim = researchOnly ? Claim(cola: RewardColaDeltaTests.XpDelta(), rub: 0, xp: 0) : Claim();
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store, claim)).Status);
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters()) options.Converters.Add(converter);
        var typed = JsonSerializer.Deserialize<SptProfile>(store.Profile, options)!;
        var payment = Assert.Single(Ledger(store.Profile));
        var mail = Assert.Single(typed.DialogueRecords![new MongoId(RewardProfileDocument.SystemSenderId)].Messages!);
        Assert.Equal(!researchOnly, mail.HasRewards);
        Assert.Equal(researchOnly ? 0 : 9, mail.Items!.Data!.Count);
        var roundTrip = JsonSerializer.Serialize(typed, options);
        Assert.Equal(payment, Assert.Single(Ledger(roundTrip)));
        Assert.True(RewardProfileDocument.HasOriginalMail(roundTrip, payment, Limits));
    }

    [Theory]
    [InlineData("profile:before-replace", false, (int)RewardSettlementStatus.Quarantined)]
    [InlineData("profile:after-replace", true, (int)RewardSettlementStatus.AlreadyPaid)]
    [InlineData("journal:Prepared:after-replace", false, (int)RewardSettlementStatus.Paid)]
    [InlineData("journal:Committed:before-replace", true, (int)RewardSettlementStatus.AlreadyPaid)]
    public async Task CrashesKeepAchievementAndMixedAttachmentGrantAtomic(string point, bool written, int expected)
    {
        using var store = Store();
        store.FailOnce(point, new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(() => Settle(store));
        Assert.Equal(written, Ledger(store.Profile).Count != 0);
        using var restart = store.Fork();
        Assert.Equal((RewardSettlementStatus)expected, (await Settle(restart)).Status);
        if (written || expected == (int)RewardSettlementStatus.Paid)
        { Assert.Single(Ledger(store.Profile)); Assert.Equal(3, Achievements(store.Profile).Count); Assert.Equal(9, Items(store.Profile).Count); }
        else { Assert.Empty(Ledger(store.Profile)); Assert.Equal(0, restart.ProfileWrites); }
    }

    [Theory]
    [InlineData("achievement")]
    [InlineData("attachment")]
    [InlineData("text")]
    public async Task RecoveryRequiresExactOwnedAchievementAndOriginalMixedMail(string changed)
    {
        using var store = Store();
        store.FailOnce("journal:Committed:before-replace", new SyntheticRewardCrash());
        await Assert.ThrowsAsync<SyntheticRewardCrash>(() => Settle(store));
        var profile = JsonNode.Parse(store.Profile)!;
        if (changed == "achievement") profile["characters"]!["pmc"]!["Achievements"]!.AsObject().Remove(RewardColaCatalog.Achievements["a_first"].Id);
        else
        {
            var mail = profile["dialogues"]![RewardProfileDocument.SystemSenderId]!["messages"]![0]!;
            if (changed == "text") mail["text"] = "changed";
            else mail["items"]!["data"]!.AsArray().RemoveAt(2);
        }
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        using var restart = store.Fork();
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(restart)).Status);
        Assert.Equal(0, restart.ProfileWrites);
    }

    [Fact]
    public async Task CommittedDeletedMailCannotReplayAchievementItemsAndChangedClaimIsQuarantined()
    {
        using var store = Store();
        await Settle(store);
        var profile = JsonNode.Parse(store.Profile)!;
        profile["dialogues"]![RewardProfileDocument.SystemSenderId]!["messages"]!.AsArray().Clear();
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        string before = store.Profile;
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Settle(store)).Status);
        Assert.Equal(before, store.Profile);
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(store, Claim(cola: RewardColaDeltaTests.AchievementDelta("a_first")))).Status);
        Assert.Equal(before, store.Profile);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("\"123\"")]
    [InlineData("-1")]
    public async Task MalformedOrdinaryAchievementRefusesBeforePreparingPayment(string value)
    {
        using var store = Store();
        var profile = JsonNode.Parse(store.Profile)!;
        profile["characters"]!["pmc"]!["Achievements"] = new JsonObject
        { [RewardColaCatalog.Achievements["a_first"].Id] = JsonNode.Parse(value) };
        File.WriteAllText(store.ProfilePath, profile.ToJsonString());
        Assert.Equal(RewardSettlementStatus.Quarantined, (await Settle(store)).Status);
        Assert.Empty(store.Journal.Entries);
        Assert.Equal(0, store.ProfileWrites);
    }

    [Fact]
    public void ResolvedPaymentCannotInventItemsAchievementsOrReplaceClaimDelta()
    {
        using var store = Store();
        var claim = Claim(cola: RewardColaDeltaTests.AchievementDelta("a_first"));
        string resolved = RewardColaProfile.Resolve(store.Profile, claim.Cola)!;
        var payment = new RewardPayment(claim.Key, claim.Fingerprint, claim.Roubles,
            RewardIdentity.MessageId(Binding, claim.Key), 1_790_000_000, claim.Experience, resolved);
        foreach (string field in new[] { "items", "refund", "roubles" })
        {
            var modified = JsonNode.Parse(resolved)!.AsObject();
            modified[field] = new JsonArray();
            Assert.Throws<RewardDataException>(() => RewardColaProfile.Validate(modified.ToJsonString()));
        }
        string other = RewardColaProfile.Resolve(store.Profile, Mixed)!;
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(store.Profile, claim, payment with { Cola = other }, Limits));
        var omit = JsonNode.Parse(resolved)!;
        omit["ach"] = new JsonArray();
        Assert.Throws<RewardDataException>(() => RewardProfileDocument.Apply(store.Profile, claim, payment with { Cola = omit.ToJsonString() }, Limits));
    }

    [Fact]
    public async Task ExistingLegacyPaymentRemainsUsableBeforeAndAfterColaPayment()
    {
        using var store = Store();
        var legacy = Claim() with { Cola = null, Experience = 0, RulesVersion = "arena-v1" };
        Assert.Equal("bc207e49ad57660d512c93114cf5db084c901f92a14eb36499c530f2368126a4", legacy.Fingerprint);
        Assert.DoesNotContain("cola", JsonSerializer.Serialize(legacy, RewardSettlementCore.JournalJson));
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store, legacy)).Status);
        Assert.DoesNotContain("cola", File.ReadAllText(store.JournalPath));
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store, Claim(2))).Status);
        Assert.Equal(RewardSettlementStatus.AlreadyPaid, (await Settle(store, legacy)).Status);
        Assert.Null(Ledger(store.Profile)[0].Cola);
        Assert.NotNull(Ledger(store.Profile)[1].Cola);
    }

    [Fact]
    public async Task EntireReviewedCatalogFitsExistingApprovalLimitsAndBoundedAttachmentCapacity()
    {
        var state = new JsonObject { ["xp"] = new JsonObject(), ["prog"] = new JsonObject(),
            ["researched"] = new JsonObject(), ["syn"] = new JsonObject { ["prog"] = new JsonObject(), ["done"] = new JsonObject() },
            ["ach"] = new JsonObject() };
        foreach (string perk in RewardColaCatalog.Perks) state["xp"]![perk] = RewardColaDelta.MaxXpDelta;
        foreach (var (id, def) in RewardColaCatalog.Challenges)
        {
            state["prog"]![def.Perk] ??= new JsonObject();
            state["researched"]![def.Perk] ??= new JsonObject();
            state["prog"]![def.Perk]![id] = def.Goal;
            state["researched"]![def.Perk]![id] = true;
        }
        foreach (var (id, goal) in RewardColaCatalog.Synergies)
        { state["syn"]!["prog"]![id] = goal; state["syn"]!["done"]![id] = true; }
        foreach (string key in RewardColaCatalog.Achievements.Keys) state["ach"]![key] = true;
        var claim = Claim(cola: RewardColaDelta.Capture("{}", state.ToJsonString())!);
        var approval = new RewardStartupApproval(1, RewardStartupApprovalState.RestoredAndApproved, claim, new string('a', 64));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(RewardRuntimeFiles.Json(approval)) < 16_384);
        using var store = Store();
        Assert.Equal(RewardSettlementStatus.Paid, (await Settle(store, claim)).Status);
        Assert.Equal(100, Achievements(store.Profile).Count);
        Assert.InRange(Items(store.Profile).Count, 100, 1025);
        Assert.True(RewardProfileDocument.HasOriginalMail(store.Profile, Assert.Single(Ledger(store.Profile)), Limits));
    }
}
