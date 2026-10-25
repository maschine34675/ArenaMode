using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;

namespace ArenaMode.Tests;

public sealed class RewardColaIntegrationTests
{
    [Fact]
    public void ResearchOnlyZombieResultIsEligibleAndItsZeroCurrencyDecisionRemainsReadable()
    {
        using var fixture = new Fixture(RewardColaDeltaTests.XpDelta());
        string before = fixture.Profile;
        var evaluation = RewardResultValidator.Evaluate(fixture.Candidate.Authorization, fixture.Candidate.Report);
        Assert.Equal(RewardEvaluationStatus.Eligible, evaluation.Status);
        Assert.Equal(0, evaluation.Roubles);
        Assert.Equal(0, evaluation.Experience);
        Assert.Equal(evaluation, RewardApprovalProducer.PrepareIfPresent(fixture.Window, fixture.Enrollment));
        var approval = fixture.Read<RewardStartupApproval>("approved.json");
        Assert.Equal(fixture.Candidate.Authorization.Cola, approval.Claim.Cola);
        Assert.Equal(0, approval.Claim.Roubles);
        Assert.Equal(before, fixture.Profile);
        // Same candidate reads the durable decision rather than minting a new one.
        Assert.Equal(evaluation, RewardApprovalProducer.PrepareIfPresent(fixture.Window, fixture.Enrollment));
        Assert.Equal(approval, fixture.Read<RewardStartupApproval>("approved.json"));
        Assert.Equal(before, fixture.Profile);
    }

    [Theory]
    [InlineData((int)RewardReportState.NoContest)]
    [InlineData((int)RewardReportState.Aborted)]
    public void ColaCannotReviveNoContestOrAbortedRewards(int state)
    {
        using var fixture = new Fixture(RewardColaDeltaTests.AchievementDelta("a_first"), (RewardReportState)state);
        string before = fixture.Profile;
        var evaluation = RewardApprovalProducer.PrepareIfPresent(fixture.Window, fixture.Enrollment)!;
        Assert.Equal(RewardEvaluationStatus.NoReward, evaluation.Status);
        Assert.Equal(0, evaluation.Roubles);
        Assert.Equal(0, evaluation.Experience);
        Assert.False(File.Exists(fixture.PathOf("approved.json")));
        Assert.Equal(evaluation, RewardApprovalProducer.PrepareIfPresent(fixture.Window, fixture.Enrollment));
        Assert.Equal(before, fixture.Profile);
    }

    [Fact]
    public void LegacyZeroKillResultRemainsUnrewardedAndAuthorizationOmitsCola()
    {
        using var fixture = new Fixture(null);
        Assert.DoesNotContain("\"cola\":", RewardRuntimeFiles.Json(fixture.Candidate.Authorization));
        var evaluation = RewardApprovalProducer.PrepareIfPresent(fixture.Window, fixture.Enrollment)!;
        Assert.Equal(RewardEvaluationStatus.NoReward, evaluation.Status);
        Assert.False(File.Exists(fixture.PathOf("approved.json")));
        Assert.Equal(evaluation, RewardApprovalProducer.PrepareIfPresent(fixture.Window, fixture.Enrollment));
    }

    [Theory]
    [InlineData("deathmatch")]
    [InlineData("gungame")]
    [InlineData("tactical")]
    [InlineData("teamdeathmatch")]
    public void ForeignProgressNeverAuthorizesOtherModes(string mode)
    {
        using var fixture = new Fixture(RewardColaDeltaTests.XpDelta());
        var run = fixture.Candidate.Authorization with { Mode = mode };
        Assert.Throws<RewardDataException>(() => RewardResultValidator.ValidateAuthorization(run));
    }

    [Fact]
    public void SignedPrivateReceiptPreservesColaAndBindsEveryDeltaByte()
    {
        using var fixture = new Fixture(RewardColaDeltaTests.AchievementDelta("a_first", "a_lvl5"));
        var authority = new RewardHandoff(1, true, fixture.Root, fixture.Enrollment.Binding,
            Id(50), Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        var earned = new RewardEarnedReceipt(1, authority.DelegationId, fixture.Candidate);
        var signed = RewardRuntimeFiles.Sign(authority, earned);
        string serialized = RewardRuntimeFiles.Json(signed);
        Assert.True(Encoding.UTF8.GetByteCount(serialized) < ArenaMode.SessionHelper.HelperRewardTransfer.MaxReceiptBytes);
        var returned = JsonSerializer.Deserialize<RewardSignedReceipt>(serialized, RewardSettlementCore.JournalJson)!;
        var verified = RewardRuntimeFiles.Verify(authority, returned);
        Assert.Equal(RewardRuntimeFiles.Json(earned), RewardRuntimeFiles.Json(verified));
        Assert.Equal(fixture.Candidate.Authorization.Cola, verified.Evidence.Authorization.Cola);
        var modified = earned with { Evidence = earned.Evidence with
        { Authorization = earned.Evidence.Authorization with { Cola = RewardColaDeltaTests.XpDelta() } } };
        string tamperedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(RewardRuntimeFiles.Json(modified)));
        Assert.Throws<RewardDataException>(() => RewardRuntimeFiles.Verify(authority, returned with { Payload = tamperedPayload }));
        // Startup may rebind only the clean ordinary profile revision/sequence;
        // record-copying an allocation must preserve the signed typed extension.
        var rebound = verified.Evidence.Authorization with { Sequence = 2, CleanProfileRevision = new string('b', 64) };
        Assert.Equal(verified.Evidence.Authorization.Cola, rebound.Cola);
    }

    [Fact]
    public void ValidSignatureStillDoesNotAuthorizeUnknownOrMalformedColaCatalog()
    {
        using var fixture = new Fixture(RewardColaDeltaTests.XpDelta());
        var authority = new RewardHandoff(1, true, fixture.Root, fixture.Enrollment.Binding,
            Id(50), Convert.ToBase64String(new byte[32]));
        var malformed = fixture.Candidate with { Authorization = fixture.Candidate.Authorization with
        { Cola = fixture.Candidate.Authorization.Cola!.Replace("tarkova-cola-2.2.1", "tarkova-cola-9.9.9", StringComparison.Ordinal) } };
        var signed = RewardRuntimeFiles.Sign(authority, new(1, authority.DelegationId, malformed));
        Assert.Throws<RewardDataException>(() => RewardRuntimeFiles.Verify(authority, signed));
    }

    private static string Id(int value) => value.ToString("x32");

    private sealed class Fixture : IDisposable
    {
        // SyntheticRewardStore supplies a verified disposable owner root; this
        // fixture creates only artificial startup files below that directory.
        private readonly SyntheticRewardStore _owner = new(RewardSettlementCoreTests.Binding, RewardSettlementCoreTests.ProfileJson);
        internal string Root => _owner.DirectoryPath;
        internal string State { get; }
        private string ProfilePath { get; }
        internal string Profile => File.ReadAllText(ProfilePath);
        internal RewardStartupWindow Window { get; }
        internal RewardStartupEnrollment Enrollment { get; }
        internal RewardApprovalCandidate Candidate { get; }
        internal string PathOf(string file) => Path.Combine(State, file);
        internal T Read<T>(string file) => RewardStartupFiles.Read<T>(PathOf(file));

        internal Fixture(string? cola, RewardReportState reportState = RewardReportState.Completed)
        {
            var binding = RewardSettlementCoreTests.Binding;
            State = Path.Combine(Root, "user", "arena-rewards", binding.ProfileId);
            ProfilePath = Path.Combine(Root, "user", "profiles", binding.ProfileId + ".json");
            Directory.CreateDirectory(State);
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            var profile = JsonNode.Parse(_owner.Profile)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 1, ["Experience"] = 0 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1, ["Experience"] = 0 };
            File.WriteAllText(ProfilePath, profile.ToJsonString());
            Enrollment = new(1, true, Root, binding);
            Window = new(Root, () => true);
            var run = new RewardRunAuthorization(1, binding, Id(3), 1, Id(4),
                ArenaMode.Shared.Protocol.BuildNativeEndIdentity("cola-synthetic-end", Id(4)), Id(5), binding.ProfileId,
                "zombies", RewardIdentity.Hash(Profile), new("synthetic-v1", 1000, 10000, 100, 10, 200_000),
                [new(Id(6), RewardRoundRule.KillsOnly, 10_000, 0, 0,
                    [new(binding.ProfileId, Id(7), RewardSide.Friendly, 0),
                     new("aaaaaaaaaaaaaaaaaaaaaaaa", Id(8), RewardSide.Opponent, 0)])], cola);
            Candidate = new(1, run, new(1, run.RunId, run.ArmToken, run.NativeEndIdentity, reportState, [new(Id(6), [])]),
                new(1, binding, run.RunId, run.ArmToken, run.NativeEndIdentity, run.RestorationId,
                    RewardRestorationState.Persisted, run.CleanProfileRevision, run.CleanProfileRevision));
            File.WriteAllText(PathOf("enrollment.json"), RewardRuntimeFiles.Json(Enrollment));
            File.WriteAllText(PathOf("journal.json"), RewardRuntimeFiles.Json(new RewardJournal(1, binding, [])));
            File.WriteAllText(PathOf("candidate.json"), RewardRuntimeFiles.Json(Candidate));
        }

        public void Dispose() { Window.Dispose(); _owner.Dispose(); }
    }
}
