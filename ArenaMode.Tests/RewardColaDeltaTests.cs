using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed class RewardColaDeltaTests
{
    internal static string XpDelta(int xp = 1) => RewardColaDelta.Capture("{}", "{\"xp\":{\"speedcola\":" + xp + "}}")!;
    internal static string AchievementDelta(params string[] keys)
    {
        var ach = new JsonObject();
        foreach (var key in keys) ach[key] = true;
        return RewardColaDelta.Capture("{}", new JsonObject { ["ach"] = ach }.ToJsonString())!;
    }

    [Fact]
    public void CatalogIsTheReviewed221DataAndContainsNoRefundOrUnboundedItemInput()
    {
        Assert.Equal("163bb9b1192d050b9e0da5aada8686e8c43877dd", RewardColaCatalog.SourceCommit);
        Assert.Equal(100, RewardColaCatalog.Achievements.Count);
        Assert.Equal(100, RewardColaCatalog.ById.Count);
        Assert.Equal(47, RewardColaCatalog.Challenges.Count);
        Assert.Equal(15, RewardColaCatalog.Synergies.Count);
        Assert.Equal(6, RewardColaCatalog.Perks.Count);
        Assert.Equal(new RewardColaItem(RewardProfileDocument.RoubleTemplateId, 25_000),
            Assert.Single(RewardColaCatalog.Achievements["a_first"].Items));
        Assert.Equal("first", RewardColaCatalog.Achievements["a_first"].ServerKey);
        Assert.False(RewardColaCatalog.Achievements.ContainsKey("first"));
        Assert.All(RewardColaCatalog.Achievements.Values, a =>
        {
            Assert.True(RewardIdentity.IsHex(a.Id, 24));
            Assert.All(a.Items, i => Assert.InRange(i.Count, 1, 35_000));
        });
    }

    [Fact]
    public void UnownedStateIsNotTransferableAndUnchangedOwnedStateProducesNoReceipt()
    {
        const string before = "{\"xp\":{\"speedcola\":10},\"ach\":{\"a_first\":true}}";
        var after = JsonNode.Parse(before)!.AsObject();
        foreach (var key in new[] { "stats", "equipped", "mode", "classic", "eternal", "refund", "arbitrary" })
            after[key] = new JsonObject { ["changed"] = true };
        Assert.Null(RewardColaDelta.Capture(before, after.ToJsonString()));
        Assert.Null(RewardColaDelta.Capture("", "{}"));
    }

    [Fact]
    public void CaptureIsCanonicalAndMergePreservesOrdinaryProgressAndEveryUnownedField()
    {
        const string before = """
            {"xp":{"speedcola":10},"prog":{"speedcola":{"a_quick":1}},"syn":{"prog":{"syn_cardio":2}},"ach":{"a_first":true}}
            """;
        const string after = """
            {"ach":{"a_lvl5":true,"a_first":true},"syn":{"done":{"syn_cardio":true},"prog":{"syn_cardio":5}},"researched":{"speedcola":{"a_quick":true}},"prog":{"speedcola":{"a_quick":3}},"xp":{"speedcola":15}}
            """;
        string delta = RewardColaDelta.Capture(before, after)!;
        RewardColaDelta.Validate(delta);
        Assert.Equal(delta, RewardColaDelta.Capture(before, JsonNode.Parse(after)!.ToJsonString()));
        Assert.Equal(new[] { "a_lvl5" }, RewardColaDelta.Read(delta).Achievements);
        const string current = """
            {"xp":{"speedcola":20,"deadshot":42},"prog":{"speedcola":{"a_quick":2}},"syn":{"prog":{"syn_cardio":100},"equipped":["keep"]},"stats":{"kills":99},"equipped":{"x":42},"mode":"Classic","classic":{"bought":true},"eternal":{"keep":true},"ach":{"a_first":true},"chv":2,"future":[1,2]}
            """;
        var merged = JsonNode.Parse(RewardColaDelta.Merge(current, delta))!;
        Assert.Equal(25, merged["xp"]!["speedcola"]!.GetValue<int>());
        Assert.Equal(42, merged["xp"]!["deadshot"]!.GetValue<int>());
        Assert.Equal(3, merged["prog"]!["speedcola"]!["a_quick"]!.GetValue<int>());
        Assert.Equal(103, merged["syn"]!["prog"]!["syn_cardio"]!.GetValue<int>());
        Assert.True(merged["researched"]!["speedcola"]!["a_quick"]!.GetValue<bool>());
        Assert.True(merged["syn"]!["done"]!["syn_cardio"]!.GetValue<bool>());
        Assert.True(merged["ach"]!["a_lvl5"]!.GetValue<bool>());
        foreach (string key in new[] { "stats", "equipped", "mode", "classic", "eternal", "chv", "future" })
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(current)![key], merged[key]), key);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(current)!["syn"]!["equipped"], merged["syn"]!["equipped"]));
    }

    [Theory]
    [InlineData("{\"xp\":{\"future\":1}}")]
    [InlineData("{\"xp\":{\"speedcola\":-1}}")]
    [InlineData("{\"xp\":{\"speedcola\":1.5}}")]
    [InlineData("{\"xp\":{\"speedcola\":\"1\"}}")]
    [InlineData("{\"xp\":{\"speedcola\":1000001}}")]
    [InlineData("{\"xp\":{\"speedcola\":2147483648}}")]
    [InlineData("{\"xp\":null}")]
    [InlineData("{\"xp\":{},\"xp\":{}}")]
    [InlineData("{\"prog\":{\"deadshot\":{\"a_quick\":1}}}")]
    [InlineData("{\"prog\":{\"speedcola\":{\"a_quick\":4}}}")]
    [InlineData("{\"researched\":{\"speedcola\":{\"a_quick\":1}}}")]
    [InlineData("{\"syn\":{\"done\":{\"future\":true}}}")]
    [InlineData("{\"ach\":{\"refund\":true}}")]
    public void InvalidOwnedStateRefusesCapture(string after)
        => Assert.Throws<RewardDataException>(() => RewardColaDelta.Capture("{}", after));

    [Theory]
    [InlineData("{\"xp\":{\"speedcola\":1}}")]
    [InlineData("{\"prog\":{\"speedcola\":{\"a_quick\":1}}}")]
    [InlineData("{\"researched\":{\"speedcola\":{\"a_quick\":true}}}")]
    [InlineData("{\"syn\":{\"done\":{\"syn_cardio\":true}}}")]
    [InlineData("{\"ach\":{\"a_first\":true}}")]
    public void MonotonicStateCannotRegress(string before)
        => Assert.Throws<RewardDataException>(() => RewardColaDelta.Capture(before, "{}"));

    [Fact]
    public void StrictExtensionRejectsNoncanonicalEmptyUnknownVersionAndInjectedRewards()
    {
        string valid = XpDelta();
        Assert.Throws<RewardDataException>(() => RewardColaDelta.Validate(" " + valid));
        foreach (var field in new[] { "items", "refund", "stats", "classic" })
        {
            var changed = JsonNode.Parse(valid)!.AsObject();
            changed[field] = new JsonObject();
            Assert.Throws<RewardDataException>(() => RewardColaDelta.Validate(changed.ToJsonString()));
        }
        var version = JsonNode.Parse(valid)!;
        version["catalog"] = "tarkova-cola-2.2.2";
        Assert.Throws<RewardDataException>(() => RewardColaDelta.Validate(version.ToJsonString()));
        var empty = JsonNode.Parse(valid)!;
        empty["xp"] = new JsonObject();
        Assert.Throws<RewardDataException>(() => RewardColaDelta.Validate(empty.ToJsonString()));
        var duplicate = JsonNode.Parse(AchievementDelta("a_first"))!;
        duplicate["ach"]!.AsArray().Add("a_first");
        Assert.Throws<RewardDataException>(() => RewardColaDelta.Validate(duplicate.ToJsonString()));
    }

    [Fact]
    public void MergeRejectsOverflowAndIncompatibleCurrentStateWithoutReplacingIt()
    {
        const string original = "{\"xp\":{\"speedcola\":2147483647},\"ordinary\":123}";
        Assert.Throws<RewardDataException>(() => RewardColaDelta.Merge(original, XpDelta()));
        Assert.Throws<RewardDataException>(() => RewardColaDelta.Merge("{\"prog\":{\"unknown\":{}}}", XpDelta()));
        Assert.Equal(2147483647, JsonNode.Parse(original)!["xp"]!["speedcola"]!.GetValue<int>());
        Assert.Throws<RewardDataException>(() => RewardColaDelta.Validate(new string(' ', RewardColaDelta.MaxBytes + 1)));
    }

    [Fact]
    public void FreshStateGetsDerivedChallengeVersionButPopulatedLegacyStateRequiresNativeMigration()
    {
        var fresh = JsonNode.Parse(RewardColaDelta.Merge("{\"ordinary\":true}", XpDelta()))!;
        Assert.Equal(2, fresh["chv"]!.GetValue<int>());
        Assert.True(fresh["ordinary"]!.GetValue<bool>());
        foreach (string version in new[] { "", "\"chv\":0,", "\"chv\":1," })
            Assert.Throws<RewardDataException>(() => RewardColaDelta.Merge("{" + version + "\"prog\":{\"speedcola\":{\"a_count\":1}}}", XpDelta()));
        Assert.NotNull(RewardColaDelta.Merge("{\"chv\":2,\"prog\":{\"speedcola\":{\"a_count\":1}}}", XpDelta()));
        foreach (string invalid in new[] { "3", "-1", "null", "\"2\"" })
        {
            string foreign = "{\"chv\":" + invalid + "}";
            Assert.Throws<RewardDataException>(() => RewardColaDelta.Merge(foreign, XpDelta()));
            Assert.Throws<RewardDataException>(() => RewardColaDelta.Capture("{}", foreign));
        }
    }

    [Fact]
    public void AdditiveProgressReachingCatalogGoalCompletesResearchWithoutInferringAchievementsOrItems()
    {
        const string current = "{\"chv\":2,\"prog\":{\"speedcola\":{\"a_quick\":2}},\"syn\":{\"prog\":{\"syn_cardio\":2999}}}";
        string delta = RewardColaDelta.Capture("{}", "{\"prog\":{\"speedcola\":{\"a_quick\":1}},\"syn\":{\"prog\":{\"syn_cardio\":1}}}")!;
        Assert.Empty(RewardColaDelta.Read(delta).Researched);
        Assert.Empty(RewardColaDelta.Read(delta).SynergyDone);
        var result = JsonNode.Parse(RewardColaDelta.Merge(current, delta))!;
        Assert.Equal(3, result["prog"]!["speedcola"]!["a_quick"]!.GetValue<int>());
        Assert.True(result["researched"]!["speedcola"]!["a_quick"]!.GetValue<bool>());
        Assert.Equal(3000, result["syn"]!["prog"]!["syn_cardio"]!.GetValue<int>());
        Assert.True(result["syn"]!["done"]!["syn_cardio"]!.GetValue<bool>());
        Assert.Null(result["ach"]);
        Assert.Null(result["items"]);
        string partial = RewardColaDelta.Capture("{}", "{\"prog\":{\"speedcola\":{\"a_quick\":1}}}")!;
        var incomplete = JsonNode.Parse(RewardColaDelta.Merge("{}", partial))!;
        Assert.Null(incomplete["researched"]);
    }

    [Fact]
    public void ClientSemanticFingerprintsMatchTheIndependentServerCatalog()
    {
        var research = RewardColaCatalog.Perks.Select(TarkovaColaResearchCatalog.Perk)
            .Concat(RewardColaCatalog.Challenges.Select(p => TarkovaColaResearchCatalog.Challenge(p.Value.Perk, p.Key, p.Value.Goal)))
            .Concat(RewardColaCatalog.Synergies.Select(p => TarkovaColaResearchCatalog.Synergy(p.Key, p.Value))).ToArray();
        Assert.Equal(TarkovaColaResearchCatalog.ResearchFingerprint, TarkovaColaResearchCatalog.Fingerprint(research));
        TarkovaColaResearchCatalog.RequireResearch(research.Reverse());
        var achievements = RewardColaCatalog.Achievements.Values.Select(a => TarkovaColaResearchCatalog.Achievement(a.Key, a.Id)).ToArray();
        Assert.Equal(TarkovaColaResearchCatalog.AchievementFingerprint, TarkovaColaResearchCatalog.Fingerprint(achievements));
        TarkovaColaResearchCatalog.RequireAchievements(achievements.Reverse());
        Assert.Throws<InvalidOperationException>(() => TarkovaColaResearchCatalog.RequireResearch(research.Skip(1)));
        Assert.Throws<InvalidOperationException>(() => TarkovaColaResearchCatalog.RequireResearch(research.Append("syn|unknown|1")));
        Assert.Throws<InvalidOperationException>(() => TarkovaColaResearchCatalog.RequireResearch(research.Append(research[0])));
        var changed = research.Select(value => value == "aug|speedcola|a_quick|3" ? "aug|speedcola|a_quick|10" : value);
        Assert.Throws<InvalidOperationException>(() => TarkovaColaResearchCatalog.RequireResearch(changed));
        Assert.Throws<InvalidOperationException>(() => TarkovaColaResearchCatalog.RequireAchievements(achievements.Select(v => v.Replace("ach|a_first|", "ach|first|", StringComparison.Ordinal))));
        Assert.Throws<InvalidOperationException>(() => TarkovaColaResearchCatalog.Challenge("speedcola", "arbitrary|field", 1));
        Assert.Throws<InvalidOperationException>(() => TarkovaColaResearchCatalog.Synergy("syn_cardio", -1));
    }
}
