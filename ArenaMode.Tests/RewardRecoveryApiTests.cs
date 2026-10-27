using System.Text.Json;
using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Routers;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public sealed class RewardRecoveryApiTests
{
    [Fact]
    public async Task UnsupportedNativeVersionCannotQueueRecovery()
    {
        using var f = new Fixture(new Version(4, 2, 0, 0));
        Assert.False((await f.Read()).CanClose);
        Assert.False((await f.Request(new string('f', 64), true)).Accepted);
        Assert.Empty(f.Requests());
    }

    [Fact]
    public async Task NativeRoutesRequireFreshExplicitConsentAndOnlyQueueMetadata()
    {
        using var f = new Fixture();
        var before = f.Profile;
        var status = await f.Read();
        Assert.True(status.CanClose, status.Detail);
        Assert.Single(status.Cases);
        Assert.Empty(f.Requests());
        Assert.False((await f.Request(status.ReviewId, false)).Accepted);
        Assert.False((await f.Request(new string('f', 64), true)).Accepted);
        Assert.Empty(f.Requests());
        Assert.Equal(before, f.Profile);
        var response = await f.Request(status.ReviewId, true);
        Assert.True(response.Accepted);
        Assert.True(response.Status.Queued);
        Assert.Single(f.Requests());
        Assert.Equal(before, f.Profile);
        Assert.False((await f.Request(status.ReviewId, true)).Accepted);
        Assert.Single(f.Requests());
    }

    [Theory]
    [InlineData("private-file")]
    [InlineData("private-directory")]
    [InlineData("snapshot")]
    [InlineData("pending-restore-save")]
    [InlineData("armed")]
    [InlineData("unknown-login")]
    public async Task ActiveOrForeignContextCannotInspectOrQueueAnotherProfilesRecovery(string context)
    {
        using var f = new Fixture();
        var valid = await f.Read();
        var before = f.Profile;
        var oldLadder = LadderPicker.Current;
        try
        {
            switch (context)
            {
                case "private-file": File.WriteAllText(Path.Combine(f.Root, "arena-session.json"), "{}"); break;
                case "private-directory": Directory.CreateDirectory(Path.Combine(f.Root, "arena-session.json")); break;
                case "snapshot": Assert.True(f.Snapshots.TakeSnapshot(Fixture.Id)); break;
                case "pending-restore-save":
                    Assert.True(f.Snapshots.TakeSnapshot(Fixture.Id));
                    Assert.True(f.Snapshots.RestoreSnapshot(Fixture.Id));
                    break;
                case "armed": f.Sessions.Arm(Fixture.Id, "factory4_day", "tactical"); break;
                case "unknown-login": f.Authenticated = new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa"); break;
            }
            var denied = await f.Read();
            Assert.False(denied.CanClose);
            Assert.Empty(denied.Cases);
            Assert.False((await f.Request(valid.ReviewId, true)).Accepted);
            Assert.Empty(f.Requests());
            Assert.Equal(before, f.Profile);
        }
        finally { LadderPicker.Redraw(oldLadder, 0); }
    }

    [Fact]
    public async Task ChangedEvidenceRequiresAnotherReviewButUnrelatedAutosaveDoesNot()
    {
        using var f = new Fixture();
        var review = await f.Read();
        var profile = JsonNode.Parse(f.Profile)!;
        profile["foreign-mod"]!["ordinary-progress"] = 123;
        File.WriteAllText(f.ProfilePath, profile.ToJsonString());
        Assert.Equal(review.ReviewId, (await f.Read()).ReviewId);
        f.AddPendingSnapshot(Guid.NewGuid().ToString("N"));
        Assert.False((await f.Request(review.ReviewId, true)).Accepted);
        Assert.Empty(f.Requests());
        var current = await f.Read();
        Assert.True(current.CanClose, current.Detail);
        Assert.Equal(2, current.Cases.Count);
        Assert.True((await f.Request(current.ReviewId, true)).Accepted);
        Assert.Equal(123, JsonNode.Parse(f.Profile)!["foreign-mod"]!["ordinary-progress"]!.GetValue<int>());
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly MongoId Id = new(RewardSettlementCoreTests.Binding.ProfileId);
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "ArenaMode.RecoveryApi.Tests", Guid.NewGuid().ToString("N"));
        internal readonly ProfileSnapshotService Snapshots;
        internal readonly ArenaSessionService Sessions;
        internal readonly ArenaRewardRouter Router;
        internal MongoId Authenticated = Id;
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Id + ".json");
        internal string Profile => File.ReadAllText(ProfilePath);
        private string Metadata => Path.Combine(Root, "user", "arena-rewards", Id.ToString());

        internal Fixture(Version? version = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            Directory.CreateDirectory(Metadata);
            File.WriteAllText(Path.Combine(Root, ".synthetic-owner"), "recovery-api");
            var profile = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            profile["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15 };
            profile["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            var binding = RewardSettlementCoreTests.Binding;
            File.WriteAllText(ProfilePath, RewardProfileDocument.Enroll(profile.ToJsonString(), binding));
            RewardRuntimeFiles.WriteNew(Root, Path.Combine(Metadata, "enrollment.json"), new RewardStartupEnrollment(1, true, Root, binding));
            RewardRuntimeFiles.WriteNew(Root, Path.Combine(Metadata, "journal.json"), new RewardJournal(1, binding, []));
            AddPendingSnapshot("33333333333333333333333333333333");
            var saves = new SaveServer(null!, [], null!, null!, null!, null!,
                BotTierScalePatchTests.TestLogger.Create<SaveServer>(), null!);
            saves.AddProfile(new SptProfile { ProfileInfo = new() { ProfileId = Id } });
            Snapshots = new ProfileSnapshotService(new SPTarkov.Server.Core.Utils.Cloners.FastCloner(), saves,
                BotTierScalePatchTests.TestLogger.Create<ProfileSnapshotService>());
            Sessions = new ArenaSessionService(BotTierScalePatchTests.TestLogger.Create<ArenaSessionService>());
            var ordinary = new OrdinaryRaidLifecycleService(null!, null!, Sessions, Snapshots,
                BotTierScalePatchTests.TestLogger.Create<OrdinaryRaidLifecycleService>());
            Router = new ArenaRewardRouter(new SPTarkov.Server.Core.Utils.JsonUtil([]), saves, Sessions, Snapshots, ordinary, Root, version ?? new Version(4, 1, 6, 0));
        }

        internal void AddPendingSnapshot(string generation)
        {
            var directory = Path.Combine(Root, "user", "arena-reward-snapshots", Id.ToString());
            Directory.CreateDirectory(directory);
            RewardRuntimeFiles.WriteNew(Root, Path.Combine(directory, generation + ".json"), new RewardSnapshotGuard(1, generation, "pending"));
        }

        internal async Task<RewardRecoveryStatusResponse> Read() => JsonSerializer.Deserialize<RewardRecoveryStatusResponse>(
            (string)await Router.HandleStaticAsync(RewardWire.RecoveryStatusRoute, null, Authenticated, string.Empty))!;
        internal async Task<RewardRecoveryResponse> Request(string reviewId, bool confirmed) => JsonSerializer.Deserialize<RewardRecoveryResponse>(
            (string)await Router.HandleStaticAsync(RewardWire.RecoveryRequestRoute,
                JsonSerializer.Serialize(new ArenaRewardRecoveryRequest { ReviewId = reviewId, ConfirmWithoutPayment = confirmed }), Authenticated, string.Empty))!;
        internal string[] Requests() => Directory.GetFiles(Metadata, "request.json", SearchOption.AllDirectories);
        public void Dispose()
        {
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.RecoveryApi.Tests"));
            if (Path.GetDirectoryName(Path.GetFullPath(Root)) != expected || !RewardIdentity.IsGuid(Path.GetFileName(Root))
                || File.ReadAllText(Path.Combine(Root, ".synthetic-owner")) != "recovery-api")
                throw new InvalidOperationException("Not an owned synthetic API directory.");
            Directory.Delete(Root, true);
        }
    }
}
