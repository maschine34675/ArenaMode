using System.Text.Json;
using System.Text.Json.Nodes;
using Path = System.IO.Path;
using ArenaMode.Server.Rewards;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Migration;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Game;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers.SaveLoad;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace ArenaMode.Tests;

// Native SaveServer deliberately resolves user/profiles relative to process CWD.
// This collection excludes ALL parallel test collections for the full CWD scope.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RewardNativeLoadCollection
{
    public const string Name = "Reward native load - exclusive synthetic CWD";
}

/// <summary>
/// Executes real native load/migration/router/save methods from the referenced SPT
/// package. It does not execute the installed 4.1.6 Program startup/HTTP lifecycle.
/// Every file is generated under an owned temporary directory; no installed SPT
/// database, profile, configuration or server process is used.
/// </summary>
[Collection(RewardNativeLoadCollection.Name)]
public sealed class RewardNativeLoadTests
{
    [Fact]
    public async Task StartupPaymentSurvivesNativeMigrationSaveAndFreshNativeReload()
    {
        using var fixture = new NativeFixture();
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(fixture.Root);
            var (saves, migration) = fixture.CreateNativeServer();
            Assert.Empty(saves.GetProfiles());
            string original = File.ReadAllText(fixture.ProfilePath);
            RewardStartupOutcome outcome;
            using (var window = new RewardStartupWindow(fixture.Root, () => saves.GetProfiles().Count == 0))
            {
                outcome = Assert.Single(await RewardStartupProcessor.ProcessAsync(window, CancellationToken.None));
                Assert.Equal(RewardSettlementStatus.Paid, outcome.Result.Status);
                Assert.Empty(saves.GetProfiles()); // The adapter has not published a live clone.
            }

            string paid = File.ReadAllText(fixture.ProfilePath);
            var payment = Assert.Single(RewardProfileDocument.ReadLedger(paid, fixture.Binding, new()));
            Assert.Equal(fixture.Claim.Roubles, payment.Roubles);
            Assert.True(RewardProfileDocument.HasOriginalMail(paid, payment, new()));
            Assert.Equal("Before migration", JsonNode.Parse(paid)!["characters"]!["pmc"]!["Info"]!["Nickname"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original)!["characters"], JsonNode.Parse(paid)!["characters"]));
            var acknowledgement = RewardStartupFiles.Read<RewardStartupAcknowledgement>(fixture.StatePath("acknowledged.json"));
            Assert.Equal(payment.MessageId, acknowledgement.MessageId);

            await saves.LoadProfileAsync(new MongoId(fixture.Binding.ProfileId));
            var loaded = saves.GetProfile(new MongoId(fixture.Binding.ProfileId));
            Assert.False(saves.IsProfileInvalidOrUnloadable(new MongoId(fixture.Binding.ProfileId)));
            Assert.Equal(1, migration.RawRuns);
            Assert.Equal(1, migration.TypedRuns);
            Assert.Equal("After migration", loaded.CharacterData!.PmcData!.Info!.Nickname);
            Assert.Equal("synthetic-migrated-account", loaded.ProfileInfo!.Username);
            Assert.True(loaded.SptData!.Migrations!.ContainsKey(SyntheticMigration.Name));
            Assert.NotNull(loaded.InsuranceList); // Native InsuranceSaveLoadRouter ran.

            // Loading/migration is still an in-memory step: no accidental write of
            // the original or normalized profile happens before native SaveProfile.
            Assert.Equal(paid, File.ReadAllText(fixture.ProfilePath));
            await saves.SaveProfileAsync(new MongoId(fixture.Binding.ProfileId));
            string persisted = File.ReadAllText(fixture.ProfilePath);
            Assert.NotEqual(paid, persisted);
            AssertPersistedNativeResult(fixture, persisted, payment, original);

            // A fresh SaveServer has no in-memory reward cache. Native reload/save
            // must retain the single paid receipt and not rerun this migration.
            var (fresh, secondMigration) = fixture.CreateNativeServer();
            await fresh.LoadProfileAsync(new MongoId(fixture.Binding.ProfileId));
            Assert.False(fresh.IsProfileInvalidOrUnloadable(new MongoId(fixture.Binding.ProfileId)));
            Assert.Equal(0, secondMigration.RawRuns);
            Assert.Equal(0, secondMigration.TypedRuns);
            await fresh.SaveProfileAsync(new MongoId(fixture.Binding.ProfileId));
            AssertPersistedNativeResult(fixture, File.ReadAllText(fixture.ProfilePath), payment, original);
            var journal = RewardStartupFiles.Read<RewardJournal>(fixture.StatePath("journal.json"));
            Assert.Equal(RewardJournalPhase.Committed, Assert.Single(journal.Entries).Phase);
            Assert.Equal(payment, Assert.Single(journal.Entries).Payment);
        }
        finally { Directory.SetCurrentDirectory(previousDirectory); }
    }

    [Fact]
    public async Task UnapprovedPaymentDoesNotStopNativeMigrationOrCreateMail()
    {
        using var fixture = new NativeFixture(RewardStartupApprovalState.Blocked);
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(fixture.Root);
            var (saves, migration) = fixture.CreateNativeServer();
            string before = File.ReadAllText(fixture.ProfilePath);
            using (var window = new RewardStartupWindow(fixture.Root, () => saves.GetProfiles().Count == 0))
            {
                var outcome = Assert.Single(await RewardStartupProcessor.ProcessAsync(window, CancellationToken.None));
                Assert.Equal(RewardSettlementStatus.Deferred, outcome.Result.Status);
                Assert.Equal("profile-restoration-not-approved", outcome.Result.Reason);
            }
            Assert.Equal(before, File.ReadAllText(fixture.ProfilePath));
            Assert.False(File.Exists(fixture.StatePath("acknowledged.json")));

            await saves.LoadProfileAsync(new MongoId(fixture.Binding.ProfileId));
            await saves.SaveProfileAsync(new MongoId(fixture.Binding.ProfileId));
            string migrated = File.ReadAllText(fixture.ProfilePath);
            Assert.Equal(1, migration.RawRuns);
            Assert.Equal(1, migration.TypedRuns);
            Assert.Equal("After migration", JsonNode.Parse(migrated)!["characters"]!["pmc"]!["Info"]!["Nickname"]!.GetValue<string>());
            Assert.Empty(RewardProfileDocument.ReadLedger(migrated, fixture.Binding, new()));
            Assert.Empty(JsonNode.Parse(migrated)!["dialogues"]!.AsObject());
            Assert.Empty(RewardStartupFiles.Read<RewardJournal>(fixture.StatePath("journal.json")).Entries);
        }
        finally { Directory.SetCurrentDirectory(previousDirectory); }
    }

    [Fact]
    public async Task LaterNativeValidationFailureIsNotMisreportedAsNativeProfileAcceptance()
    {
        using var fixture = new NativeFixture();
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(fixture.Root);
            var (saves, migration) = fixture.CreateNativeServer(includeRoubleTemplate: false);
            using (var window = new RewardStartupWindow(fixture.Root, () => saves.GetProfiles().Count == 0))
            {
                var outcome = Assert.Single(await RewardStartupProcessor.ProcessAsync(window, CancellationToken.None));
                Assert.Equal(RewardSettlementStatus.Paid, outcome.Result.Status);
            }
            string paid = File.ReadAllText(fixture.ProfilePath);
            var payment = Assert.Single(RewardProfileDocument.ReadLedger(paid, fixture.Binding, new()));

            // The current startup consumer precedes native orphan validation. A
            // changed/missing native template remains a native admission failure;
            // a durable mail receipt is not a claim that login/collection worked.
            await saves.LoadProfileAsync(new MongoId(fixture.Binding.ProfileId));
            Assert.True(saves.IsProfileInvalidOrUnloadable(new MongoId(fixture.Binding.ProfileId)));
            Assert.Equal(1, migration.RawRuns);
            Assert.Equal(0, migration.TypedRuns);
            await saves.SaveProfileAsync(new MongoId(fixture.Binding.ProfileId));
            Assert.Equal(paid, File.ReadAllText(fixture.ProfilePath));
            Assert.True(RewardProfileDocument.HasOriginalMail(paid, payment, new()));

            // Once native templates are available again, ordinary native loading
            // sees the same paid file; no second payout is necessary or performed.
            var (repaired, _) = fixture.CreateNativeServer();
            await repaired.LoadProfileAsync(new MongoId(fixture.Binding.ProfileId));
            Assert.False(repaired.IsProfileInvalidOrUnloadable(new MongoId(fixture.Binding.ProfileId)));
            await repaired.SaveProfileAsync(new MongoId(fixture.Binding.ProfileId));
            Assert.Equal(payment, Assert.Single(RewardProfileDocument.ReadLedger(File.ReadAllText(fixture.ProfilePath), fixture.Binding, new())));
        }
        finally { Directory.SetCurrentDirectory(previousDirectory); }
    }

    private static void AssertPersistedNativeResult(NativeFixture fixture, string persisted, RewardPayment payment, string original)
    {
        Assert.Equal(payment, Assert.Single(RewardProfileDocument.ReadLedger(persisted, fixture.Binding, new())));
        Assert.True(RewardProfileDocument.HasOriginalMail(persisted, payment, new()));
        var saved = JsonNode.Parse(persisted)!;
        Assert.Equal("After migration", saved["characters"]!["pmc"]!["Info"]!["Nickname"]!.GetValue<string>());
        Assert.Equal("synthetic-migrated-account", saved["info"]!["username"]!.GetValue<string>());
        Assert.Equal("native-migration-preserved", saved["fixtureMigration"]!.GetValue<string>());
        Assert.NotNull(saved["spt"]!["migrations"]![SyntheticMigration.Name]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original)!["foreignFixture"], saved["foreignFixture"]));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original)!["characters"]!["pmc"]!["Inventory"], saved["characters"]!["pmc"]!["Inventory"]));
        Assert.Single(saved["dialogues"]![RewardProfileDocument.SystemSenderId]!["messages"]!.AsArray());
    }

    private sealed class SyntheticMigration : IProfileMigration
    {
        internal const string Name = "reward-fixture-native-migration";
        internal int RawRuns { get; private set; }
        internal int TypedRuns { get; private set; }
        public string MigrationName => Name;
        public IEnumerable<Type> PrerequisiteMigrations => [];
        public bool CanMigrate(JsonObject profile, IEnumerable<IProfileMigration> previouslyRanMigrations)
            => profile["characters"]?["pmc"]?["Info"]?["Nickname"]?.GetValue<string>() == "Before migration";
        public JsonObject Migrate(JsonObject profile)
        {
            RawRuns++;
            profile["characters"]!["pmc"]!["Info"]!["Nickname"] = "After migration";
            profile["fixtureMigration"] = "native-migration-preserved";
            return profile;
        }
        public bool PostMigrate(SptProfile profile)
        {
            TypedRuns++;
            profile.ProfileInfo!.Username = "synthetic-migrated-account";
            return true;
        }
    }

    private sealed class NativeFixture : IDisposable
    {
        private const string Owner = "ArenaMode native reward load fixture only";
        private const string StashTemplate = "566abbc34bdc2d92178b4576";
        private readonly string _ownerPath;
        private readonly JsonSerializerOptions? _previousJsonOptions = JsonUtil.JsonSerializerOptionsNoIndent;
        private readonly JsonSerializerOptions? _previousIndentedJsonOptions = JsonUtil.JsonSerializerOptionsIndented;
        internal string Root { get; }
        internal RewardBinding Binding { get; }
        internal RewardClaim Claim { get; }
        internal string ProfilePath => Path.Combine(Root, "user", "profiles", Binding.ProfileId + ".json");
        internal string StatePath(string name) => Path.Combine(Root, RewardStartupFiles.RelativeDirectory, Binding.ProfileId, name);

        internal NativeFixture(RewardStartupApprovalState state = RewardStartupApprovalState.RestoredAndApproved)
        {
            Root = Path.Combine(Path.GetTempPath(), "ArenaMode.RewardNativeLoad.Tests", Guid.NewGuid().ToString("N"));
            Binding = new(Guid.NewGuid().ToString("N"), new MongoId().ToString(), Guid.NewGuid().ToString("N"));
            Claim = new(Binding, Guid.NewGuid().ToString("N"), 1, 750_001, "synthetic-native-v1");
            Directory.CreateDirectory(Root);
            _ownerPath = Path.Combine(Root, ".synthetic-owner");
            File.WriteAllText(_ownerPath, Owner);
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath("enrollment.json"))!);
            string profile = RewardProfileDocument.Enroll(CreateProfile(), Binding);
            File.WriteAllText(ProfilePath, profile);
            WriteState("enrollment.json", new RewardStartupEnrollment(1, true, Root, Binding));
            WriteState("approved.json", new RewardStartupApproval(1, state, Claim, RewardIdentity.Hash(profile)));
            WriteState("journal.json", new RewardJournal(1, Binding, []));

            // Real native localization is restricted to this generated one-key DB.
            string locale = Path.Combine(Root, "SPT_Data", "database", "locales", "server");
            Directory.CreateDirectory(locale);
            File.WriteAllText(Path.Combine(locale, "en.json"), "{\"fixer-mod_item_found\":\"Missing synthetic item template\"}");
        }

        private void WriteState<T>(string name, T value)
            => File.WriteAllText(StatePath(name), JsonSerializer.Serialize(value, RewardSettlementCore.JournalJson));

        internal (SaveServer Saves, SyntheticMigration Migration) CreateNativeServer(bool includeRoubleTemplate = true)
        {
            Assert.Equal(Path.GetFullPath(Root), Path.GetFullPath(Environment.CurrentDirectory));
            var files = new FileUtil();
            var json = new JsonUtil([new SptJsonConverterRegistrator()]);
            var time = new TimeUtil();
            var random = new RandomUtil(BotTierScalePatchTests.TestLogger.Create<RandomUtil>(),
                new SPTarkov.Server.Core.Utils.Cloners.FastCloner());
            var locales = new LocaleService(BotTierScalePatchTests.TestLogger.Create<LocaleService>(),
                new LocaleTable { Global = [], Menu = [], Languages = new() { ["en"] = "English" } },
                new LocaleConfig { GameLocale = "en", ServerLocale = "en", ServerSupportedLocales = ["en"], Fallbacks = [] });
            var localization = new ServerLocalisationService(BotTierScalePatchTests.TestLogger.Create<ServerLocalisationService>(),
                random, locales, json, files);
            var config = new CoreConfig
            {
                ProjectName = "Synthetic reward test", CompatibleTarkovVersion = "synthetic", ServerName = "synthetic",
                ProfileSaveIntervalInSeconds = 600, SptFriendNickname = "synthetic", AllowProfileWipe = false,
                BsgLogging = new(), Release = new(), Survey = new SurveyResponseData(),
                Fixes = new GameFixes { RemoveModItemsFromProfile = false },
                Features = new ServerFeatures
                {
                    CompressProfile = false, CreateNewProfileTypesBlacklist = [], AchievementProfileIdBlacklist = [],
                    ChatbotFeatures = new ChatbotFeatures
                    {
                        CommandoFeatures = new(), CommandUseLimits = [], Ids = [], Aids = [], EnabledBots = []
                    }
                }
            };
            var items = new Dictionary<MongoId, TemplateItem>
            {
                [new MongoId(StashTemplate)] = new() { Id = new MongoId(StashTemplate), Name = "Synthetic stash" }
            };
            if (includeRoubleTemplate)
                items[new MongoId(RewardProfileDocument.RoubleTemplateId)] = new TemplateItem
                { Id = new MongoId(RewardProfileDocument.RoubleTemplateId), Name = "Synthetic RUB" };
            var templates = new TemplateTable
            {
                Character = [], CustomisationStorage = [], Items = items, Prestige = null!, Quests = [],
                RepeatableQuests = null!, Handbook = null!, Customization = [], Dialogue = null!, Profiles = [],
                Prices = [], DefaultEquipmentPresets = [], Achievements = [], CustomAchievements = [], LocationServices = null!
            };
            var validator = new ProfileValidatorHelper(templates, new TradersTable(), config,
                BotTierScalePatchTests.TestLogger.Create<ProfileValidatorHelper>(), localization);
            var migration = new SyntheticMigration();
            var migrationService = new ProfileMigrationService([migration], validator, time,
                BotTierScalePatchTests.TestLogger.Create<ProfileMigrationService>());
            var backup = new BackupService(BotTierScalePatchTests.TestLogger.Create<BackupService>(), [], json, time,
                new BackupConfig
                {
                    Enabled = false, Directory = Path.Combine(Root, "disabled-backups"),
                    BackupInterval = new BackupConfigInterval { Enabled = false, IntervalMinutes = 60 }
                }, files);
            SaveLoadRouter[] routers = [new HealthSaveLoadRouter(), new InraidSaveLoadRouter(), new InsuranceSaveLoadRouter(), new ProfileSaveLoadRouter()];
            var saves = new SaveServer(files, routers, json, new HashUtil(random), migrationService, backup,
                BotTierScalePatchTests.TestLogger.Create<SaveServer>(), config);
            return (saves, migration);
        }

        private string CreateProfile() => new JsonObject
        {
            ["info"] = new JsonObject
            {
                ["id"] = Binding.ProfileId, ["username"] = "synthetic-native-account", ["wipe"] = false,
                ["invalidOrUnloadableProfile"] = false
            },
            ["characters"] = new JsonObject
            {
                ["pmc"] = new JsonObject
                {
                    ["_id"] = Binding.ProfileId, ["Info"] = new JsonObject { ["Nickname"] = "Before migration", ["Side"] = "Usec" },
                    ["Inventory"] = new JsonObject
                    {
                        ["stash"] = "aaaaaaaaaaaaaaaaaaaaaaaa",
                        // Native BotBaseInventory explicitly serializes these
                        // nulls; include its shape before comparing preservation.
                        ["sortingTable"] = null, ["questStashItems"] = null,
                        ["hideoutAreaStashes"] = null, ["hideoutCustomizationStashId"] = null,
                        ["items"] = new JsonArray(new JsonObject { ["_id"] = "aaaaaaaaaaaaaaaaaaaaaaaa", ["_tpl"] = StashTemplate })
                    }
                },
                ["scav"] = new JsonObject
                {
                    ["_id"] = "bbbbbbbbbbbbbbbbbbbbbbbb", ["Info"] = new JsonObject { ["Nickname"] = "Synthetic scav", ["Side"] = "Savage" }
                }
            },
            ["inraid"] = new JsonObject { ["location"] = "none", ["character"] = "none" },
            ["dialogues"] = new JsonObject(),
            ["spt"] = new JsonObject { ["version"] = "4.1.0", ["migrations"] = new JsonObject() },
            ["foreignFixture"] = new JsonObject { ["untouched"] = new JsonArray(1, "two", true), ["nullValue"] = null }
        }.ToJsonString();

        public void Dispose()
        {
            // JsonUtil initializes process-static serializer options. Restore the
            // prior references before this exclusive collection releases control.
            typeof(JsonUtil).GetProperty(nameof(JsonUtil.JsonSerializerOptionsNoIndent))!.SetValue(null, _previousJsonOptions);
            typeof(JsonUtil).GetProperty(nameof(JsonUtil.JsonSerializerOptionsIndented))!.SetValue(null, _previousIndentedJsonOptions);
            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.RewardNativeLoad.Tests"));
            var target = Path.GetFullPath(Root);
            if (!string.Equals(Path.GetDirectoryName(target), expectedParent, RewardStartupFiles.PathComparison)
                || !RewardIdentity.IsGuid(Path.GetFileName(target)) || File.ReadAllText(_ownerPath) != Owner
                || string.Equals(target, Path.GetFullPath(Environment.CurrentDirectory), RewardStartupFiles.PathComparison))
                throw new InvalidOperationException("Refusing cleanup outside the restored, owned synthetic fixture.");
            // Verify all names before recursive deletion; never follow a link which
            // a failed/mutated fixture could have introduced into the owned root.
            var pending = new Stack<string>();
            pending.Push(target);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Refusing synthetic cleanup through a directory link.");
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Refusing synthetic cleanup through a link.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            }
            Directory.Delete(target, recursive: true);
        }
    }
}
