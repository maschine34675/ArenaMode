using System.Text.Json.Nodes;
using ArenaMode.Server.Rewards;

namespace ArenaMode.Tests;

public sealed class RewardStartupDefaultsTests
{
    [Fact]
    public async Task FirstOrdinaryStartupEnrollsWithoutAnyPriorChoiceOrRewardDirectory()
    {
        using var f = new Fixture();
        var original = JsonNode.Parse(f.Profile)!;
        Assert.False(Directory.Exists(f.State));
        var outcome = Assert.Single(await f.Start());
        Assert.Equal("rewards-enrolled-no-pending-result", outcome.Result.Reason);
        Assert.Equal(new RewardConsent(1, true, 0), RewardEnrollment.ReadConsent(f.Root, Fixture.Id));
        var status = new RewardRuntimeCoordinator(f.Root).GetStatus(Fixture.Id);
        Assert.True(status.Enabled);
        Assert.True(status.Enrolled);
        Assert.True(status.CanChange);
        Assert.Equal("ready", status.State);
        var enrolled = JsonNode.Parse(f.Profile)!.AsObject();
        Assert.True(enrolled.Remove(RewardProfileDocument.ExtensionKey));
        Assert.True(JsonNode.DeepEquals(original, enrolled));
        Assert.Empty(RewardProfileDocument.ReadLedger(f.Profile, RewardEnrollment.Read(f.Root, Fixture.Id).Binding, new()));
        Assert.True(RewardRuntimeFiles.ReadHandoff(f.Root, f.Metadata("handoff.json")).Enabled);

        var after = f.AllFiles();
        await f.Start();
        Assert.Equal(after, f.AllFiles());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitOffSurvivesEveryStartupAndIsNeverMigratedBackOn(bool previouslyEnrolled)
    {
        using var f = new Fixture();
        if (previouslyEnrolled) await f.Start();
        RewardEnrollment.SetConsent(f.Root, Fixture.Id, false);
        var before = f.AllFiles();
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal("rewards-not-enabled", Assert.Single(await f.Start()).Result.Reason);
            Assert.False(RewardEnrollment.WantsRewards(f.Root, Fixture.Id));
            Assert.Equal(before, f.AllFiles());
        }
        Assert.Equal(previouslyEnrolled, File.Exists(f.Metadata("enrollment.json")));
    }

    [Theory]
    [InlineData("empty-directory")]
    [InlineData("control-only")]
    [InlineData("malformed-consent")]
    [InlineData("lost-consent")]
    public async Task ExistingAmbiguousStateIsNeverReinterpretedAsDefaultConsent(string variant)
    {
        using var f = new Fixture();
        Directory.CreateDirectory(f.State);
        switch (variant)
        {
            case "control-only": File.WriteAllText(f.Metadata("control.json"), "{}"); break;
            case "malformed-consent": File.WriteAllText(f.Metadata("consent.json"), "{"); break;
            case "lost-consent":
                RewardEnrollment.SetConsent(f.Root, Fixture.Id, true);
                using (var window = new RewardStartupWindow(f.Root, () => true)) RewardEnrollment.EnsureAtStartup(window, Fixture.Id);
                File.Delete(f.Metadata("consent.json"));
                break;
        }
        var before = f.AllFiles();
        Assert.Equal(RewardSettlementStatus.Quarantined, Assert.Single(await f.Start()).Result.Status);
        Assert.Equal(before, f.AllFiles());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task CopiedOrDamagedProfileHistoryWithoutAuthorityCannotBeReenrolled(string history)
    {
        using var f = new Fixture();
        f.Edit(p => p[RewardProfileDocument.ExtensionKey] = JsonNode.Parse(history));
        var before = f.Profile;
        Assert.Equal("default-reward-profile-has-history", Assert.Single(await f.Start()).Result.Reason);
        Assert.Equal(before, f.Profile);
        Assert.False(Directory.Exists(f.State));
    }

    [Theory]
    [InlineData("wipe")]
    [InlineData("invalid")]
    [InlineData("inraid")]
    [InlineData("id")]
    [InlineData("pmc")]
    [InlineData("scav")]
    [InlineData("inventory")]
    [InlineData("uninitialized")]
    [InlineData("duplicate")]
    public async Task UnsafeOrUninitializedProfilesRemainUntouched(string variant)
    {
        using var f = new Fixture();
        f.Edit(p =>
        {
            switch (variant)
            {
                case "wipe": p["info"]!["wipe"] = true; break;
                case "invalid": p["info"]!["invalidOrUnloadableProfile"] = true; break;
                case "inraid": p["inraid"]!["location"] = "factory4_day"; break;
                case "id": p["info"]!["id"] = new string('b', 24); break;
                case "pmc": p["characters"]!["pmc"]!["Info"] = null; break;
                case "scav": p["characters"]!["scav"]!["Info"] = null; break;
                case "inventory": p["characters"]!["pmc"]!["Inventory"] = null; break;
                case "uninitialized": p["characters"] = new JsonObject(); break;
            }
        });
        if (variant == "duplicate") File.WriteAllText(f.ProfilePath, f.Profile.Insert(1, "\"info\":{},"));
        var before = f.Profile;
        Assert.Equal(RewardSettlementStatus.Quarantined, Assert.Single(await f.Start()).Result.Status);
        Assert.Equal(before, f.Profile);
        Assert.False(Directory.Exists(f.State));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateMarkerNeverBootstrapsNormalRewardState(bool directoryMarker)
    {
        using var f = new Fixture();
        var marker = Path.Combine(f.Root, "arena-session.json");
        if (directoryMarker) Directory.CreateDirectory(marker);
        else File.WriteAllText(marker, "{broken");
        var before = f.AllFiles();
        Assert.False(RewardStartupDefaults.PrepareDirectory(f.Root));
        using var window = new RewardStartupWindow(f.Root, () => true);
        Assert.Throws<RewardDataException>(() => RewardStartupDefaults.EnableIfNew(window, Fixture.Id));
        Assert.Equal(before, f.AllFiles());
        Assert.False(Directory.Exists(Path.Combine(f.Root, RewardStartupFiles.RelativeDirectory)));
    }

    [Fact]
    public void LoadedOrClosedStartupWindowCannotApplyDefaults()
    {
        using var f = new Fixture();
        var before = f.AllFiles();
        using var loaded = new RewardStartupWindow(f.Root, () => false);
        Assert.Throws<RewardDataException>(() => RewardStartupDefaults.EnableIfNew(loaded, Fixture.Id));
        using var closed = new RewardStartupWindow(f.Root, () => true);
        closed.Dispose();
        Assert.Throws<RewardDataException>(() => RewardStartupDefaults.EnableIfNew(closed, Fixture.Id));
        Assert.Equal(before, f.AllFiles());
    }

    [Fact]
    public async Task DiscoveryIgnoresConflictBackupsAndDoesNotLetOneInvalidProfileBlockOthers()
    {
        using var f = new Fixture();
        var folder = Path.GetDirectoryName(f.ProfilePath)!;
        File.WriteAllText(Path.Combine(folder, Fixture.Id + ".sync-conflict-20261025.json"), "broken conflict");
        File.WriteAllText(Path.Combine(folder, "backup.json"), "broken backup");
        File.WriteAllText(Path.Combine(folder, new string('b', 24) + ".json"), "{}");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllText(Path.Combine(folder, "nested", new string('c', 24) + ".json"), "{}");
        var outcomes = await f.Start();
        Assert.Equal(2, outcomes.Count);
        Assert.Contains(outcomes, o => o.ProfileId == Fixture.Id && o.Result.Reason == "rewards-enrolled-no-pending-result");
        Assert.Contains(outcomes, o => o.ProfileId == new string('b', 24) && o.Result.Status == RewardSettlementStatus.Quarantined);
        Assert.Single(Directory.GetDirectories(Path.Combine(f.Root, RewardStartupFiles.RelativeDirectory)));
        Assert.Equal("broken conflict", File.ReadAllText(Path.Combine(folder, Fixture.Id + ".sync-conflict-20261025.json")));
    }

    [Fact]
    public async Task TooManyProfilesRejectBeforeAnyEnrollmentOrConsentWrite()
    {
        using var f = new Fixture();
        for (var i = 1; i <= 64; i++)
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.ProfilePath)!, i.ToString("x24") + ".json"), "{}");
        var before = f.Profile;
        await Assert.ThrowsAsync<RewardDataException>(() => f.Start());
        Assert.Equal(before, f.Profile);
        Assert.Empty(Directory.GetDirectories(Path.Combine(f.Root, RewardStartupFiles.RelativeDirectory)));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("profiles-directory")]
    [InlineData("reward-directory")]
    public async Task LinkedSourcesOrDestinationsNeverWriteThroughToAnotherTree(string kind)
    {
        using var f = new Fixture();
        using var other = new Fixture();
        var before = other.AllFiles();
        switch (kind)
        {
            case "profile":
                File.Delete(f.ProfilePath);
                File.CreateSymbolicLink(f.ProfilePath, other.ProfilePath);
                break;
            case "profiles-directory":
                File.Delete(f.ProfilePath);
                Directory.Delete(Path.GetDirectoryName(f.ProfilePath)!);
                Directory.CreateSymbolicLink(Path.GetDirectoryName(f.ProfilePath)!, Path.GetDirectoryName(other.ProfilePath)!);
                break;
            case "reward-directory":
                Directory.CreateSymbolicLink(Path.Combine(f.Root, RewardStartupFiles.RelativeDirectory), other.Root);
                break;
        }
        if (kind == "profile")
            Assert.Equal(RewardSettlementStatus.Quarantined, Assert.Single(await f.Start()).Result.Status);
        else await Assert.ThrowsAsync<RewardDataException>(() => f.Start());
        Assert.Equal(before, other.AllFiles());
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Id = "aaaaaaaaaaaaaaaaaaaaaaaa";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ArenaMode.RewardDefaults.Tests", Guid.NewGuid().ToString("N"));
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Id + ".json");
        internal string State => RewardRuntimeFiles.DirectoryFor(Root, Id);
        internal string Profile => File.ReadAllText(ProfilePath);
        internal string Metadata(string name) => Path.Combine(State, name);
        internal Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            var p = JsonNode.Parse(RewardSettlementCoreTests.ProfileJson)!;
            p["info"]!["id"] = Id;
            p["characters"]!["pmc"]!["Info"] = new JsonObject { ["Level"] = 15 };
            p["characters"]!["scav"]!["Info"] = new JsonObject { ["Level"] = 1 };
            p["foreignMod"] = new JsonObject { ["progress"] = 123 };
            File.WriteAllText(ProfilePath, p.ToJsonString());
        }
        internal void Edit(Action<JsonObject> edit)
        {
            var p = JsonNode.Parse(Profile)!.AsObject();
            edit(p);
            File.WriteAllText(ProfilePath, p.ToJsonString());
        }
        internal async Task<IReadOnlyList<RewardStartupOutcome>> Start()
        {
            Assert.True(RewardStartupDefaults.PrepareDirectory(Root));
            using var window = new RewardStartupWindow(Root, () => true);
            return await RewardStartupProcessor.ProcessAsync(window, CancellationToken.None);
        }
        internal string[] AllFiles() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.GetRelativePath(Root, p) + "\n" + File.ReadAllText(p)).ToArray();
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
