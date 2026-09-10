using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ArenaMode.Server;
using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Bot;
using SPTarkov.Server.Core.Utils;
using NativeCloner = SPTarkov.Server.Core.Utils.Cloners.FastCloner;
using Path = System.IO.Path;

namespace ArenaMode.Tests;

public sealed partial class BotTierScalePatchTests
{
    /// <summary>
    /// Loads the user's installed APBS in this isolated test process only.
    /// Runs native deferred/parallel wave generation, native FastCloner, all
    /// production ownership hooks and actual APBS level/XP/tier selection.
    /// Inventory construction and cache persistence are deliberately stubbed;
    /// this is not a complete bot or raid simulation. No APBS sources bundled.
    /// </summary>
    [SkippableFact]
    public void InstalledApbsUsesArenaRangeThroughRealNativeCloneAndLevelPipeline()
    {
        string? root = Environment.GetEnvironmentVariable("ARENA_APBS_TEST_ROOT");
        Skip.If(string.IsNullOrEmpty(root), "Set ARENA_APBS_TEST_ROOT via tools/ApbsCompatibilityChecks/Verify.ps1");
        using var host = new InstalledApbsHost(root!);
        ApbsBotTierCompatibility.TryEnable([]);
        Assert.False(ApbsBotTierCompatibility.Enabled);
        var wrongVersion = host.UnsupportedVersion();
        ApbsBotTierCompatibility.TryEnable([wrongVersion]);
        Assert.False(ApbsBotTierCompatibility.Enabled);
        ApbsBotTierCompatibility.TryEnable([host.Mod, host.Mod]);
        Assert.False(ApbsBotTierCompatibility.Enabled);
        ApbsBotTierCompatibility.TryEnable([host.Mod]); // Installed but not yet active: fail open.
        Assert.False(ApbsBotTierCompatibility.Enabled);
        host.EnableApbs();
        ApbsBotTierCompatibility.TryEnable([host.Mod]);
        Assert.True(ApbsBotTierCompatibility.Enabled, ApbsBotTierCompatibility.FailureReason);

        foreach (int mode in new[] { 0, 1 }) // Delta and Weighted.
        foreach (bool split in new[] { false, true })
        foreach (int profileLevel in new[] { 1, 70 })
        {
            host.Configure(mode, split, profileLevel);
            var token = StartTimedArena();
            foreach (int elapsed in new[] { 0, 120, 240 })
            {
                var wave = host.Wave();
                var bots = host.Generate(new(ArenaSession), wave, Condition(token, 300, elapsed));
                Assert.Single(bots);
                var sample = Assert.Single(host.Samples);
                int anchor = elapsed == 0 ? 1 : elapsed == 120 ? 33 : 65;
                Assert.NotSame(wave, sample.Details);
                Assert.Equal(Math.Max(1, anchor - 5), sample.Min);
                Assert.Equal(anchor + 5, sample.Max);
                var info = Assert.IsType<Info>(bots[0].Info);
                Assert.InRange(info.Level!.Value, sample.Min, sample.Max);
                Assert.Equal(host.Tier(info.Level.Value), Convert.ToInt32(info.ExtensionData!["Tier"]));
                Assert.Equal(info.ExtensionData["Tier"], sample.Details.ExtensionData!["Tier"]);
                Assert.True(info.Experience > 0);
                Assert.Equal(profileLevel, host.CurrentRaidLevel());
                Assert.Equal(mode, Convert.ToInt32(host.GetConfig("LevelPickingMode")));
                Assert.Equal(split, host.GetAdditional("UseGroundZeroSplit"));
                Assert.False(ArenaBotTierOwnership.TryGetRange(sample.Details, new(1, 100), 79, out _));
            }
        }

        // Fixed Delta makes pass-through comparisons deterministic, including
        // PFT, another session on the same map and the map-level opt-out.
        host.Configure(0, false, 1);
        foreach (string context in new[] { "ordinary", "protected", "optout" })
        {
            StartTimedArena(context == "protected" ? "tactical" : "teamdeathmatch", context == "protected");
            if (context == "optout") ArenaContext.Config.Maps["factory4_day"].ScaleBotTier = false;
            var wave = host.Wave();
            var native = host.UnownedRange(wave);
            host.Generate(new(context == "ordinary" ? OrdinarySession : ArenaSession), wave, new GenerateCondition { Limit = 1 });
            var sample = Assert.Single(host.Samples);
            Assert.Equal((native.Min, native.Max), (sample.Min, sample.Max));
        }

        // Parallel legacy batch: the permit must survive real native PLINQ
        // dispatch while staying attached only to each synchronous clone.
        StartTimedArena("tactical");
        ArenaContext.Config.Maps["factory4_day"].ScaleBotTier = true;
        ArenaContext.Config.Maps["factory4_day"].BotTierStartLevel = 65;
        var batch = host.Wave(); batch.BotCountToGenerate = 24;
        Assert.Equal(24, host.Generate(new(ArenaSession), batch, new GenerateCondition { Limit = 24 }).Count);
        Assert.Equal(24, host.Samples.Count);
        Assert.All(host.Samples, s => Assert.Equal((60, 70), (s.Min, s.Max)));

        foreach (string role in new[] { "pmcBEAR", "assault" })
        {
            var wave = host.Wave(); wave.Role = role; wave.Side = role == "pmcBEAR" ? "Bear" : "Savage";
            wave.IsPmc = role == "pmcBEAR";
            Assert.Single(host.Generate(new(ArenaSession), wave, new GenerateCondition { Limit = 1 }));
            Assert.Equal((60, 70), (host.Samples.Single().Min, host.Samples.Single().Max));
        }
        host.SetAdditional("EnablePrestiging", true);
        Assert.Single(host.Generate(new(ArenaSession), host.Wave(), new GenerateCondition { Limit = 1 }));
        Assert.Equal((60, 70), (host.Samples.Single().Min, host.Samples.Single().Max));
        host.SetAdditional("EnablePrestiging", false);

        // Deferred enumeration must not borrow a replacement raid even when
        // its caller supplies exactly the same arm token again.
        host.AssertDeferredRearmRejected();

        // Known-bug control: disabling only the adapter restores APBS's global
        // profile anchor. The same late Arena demand no longer gets 60..70.
        ApbsBotTierCompatibility.Disable();
        var lateToken = StartTimedArena();
        host.Generate(new(ArenaSession), host.Wave(), Condition(lateToken, 300, 240));
        Assert.NotEqual((60, 70), (host.Samples.Single().Min, host.Samples.Single().Max));
        ApbsBotTierCompatibility.TryEnable([host.Mod]);
        Assert.True(ApbsBotTierCompatibility.Enabled);

        // A throwing generator must still remove cloned ownership and restore
        // the pending thread scope; native TryGenerateSingleBot handles failure.
        host.ThrowInPrepare = true;
        Assert.Empty(host.Generate(new(ArenaSession), host.Wave(), Condition(lateToken, 300, 240)));
        Assert.NotNull(host.LastClone);
        Assert.False(ArenaBotTierOwnership.TryGetRange(host.LastClone!, new(1, 100), 79, out _));
        host.ThrowInPrepare = false;
        var reuse = host.LastClone!;
        host.PrepareDirect(new(ArenaSession), reuse);
        Assert.False(ArenaBotTierOwnership.TryGetRange(reuse, new(1, 100), 79, out _));
    }

    private sealed class InstalledApbsHost : IDisposable
    {
        private const string TestHarmonyId = "ArenaMode.Tests.installed-apbs";
        private readonly Harmony _harmony = new(TestHarmonyId);
        private readonly AbstractPatch _apbsPatch;
        private readonly Assembly _assembly;
        private readonly object _config;
        private readonly object _additional;
        private readonly object _tierHelper;
        private readonly MethodInfo _range;
        private readonly Type _raidInfo;
        private readonly BotController _controller;
        private readonly BotGenerator _generator = Blank<BotGenerator>();
        private readonly BotLevelGenerator _levelGenerator;
        private static InstalledApbsHost? _current;
        internal readonly ConcurrentBag<Sample> Samples = [];
        internal BotGenerationDetails? LastClone;
        internal bool ThrowInPrepare;
        internal SptMod Mod { get; }
        internal sealed record Sample(BotGenerationDetails Details, int Min, int Max);

        internal InstalledApbsHost(string root)
        {
            string modPath = Path.Combine(root, "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem");
            // APBS metadata implements the server's Blazor metadata interface;
            // load that installed contract without referencing/bundling APBS.
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "SPT_Runtime/SPTarkov.Server.Web.dll"));
            _assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(modPath, "acidphantasm-progressivebotsystem.dll"));
            Type Type(string name) => _assembly.GetType("ProgressiveBotSystem." + name, true)!;
            Mod = new SptMod { Directory = modPath, Assemblies = [_assembly], ModMetadata = (IModMetadata)Activator.CreateInstance(Type("ModMetadata"))! };
            Assert.Equal("2.4.1", Mod.ModMetadata.Version.ToString());
            _config = Activator.CreateInstance(Type("Models.ApbsServerConfig"))!;
            Type("Globals.ModConfig").GetProperty("Config")!.SetValue(null, _config);
            _additional = _config.GetType().GetProperty("PmcBots")!.GetValue(_config)!;
            _additional = _additional.GetType().GetProperty("AdditionalOptions")!.GetValue(_additional)!;
            Set(_additional, "EnablePrestiging", false);
            var compatibility = _config.GetType().GetProperty("CompatibilityConfig")!.GetValue(_config)!;
            Set(compatibility.GetType().GetProperty("Secrets")!.GetValue(compatibility)!, "AprilFoolsEvent", false);
            var tierInformation = Activator.CreateInstance(Type("Globals.TierInformation"))!;
            var tiers = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(modPath, "Data/Tiers/TierData.json")),
                typeof(List<>).MakeGenericType(Type("Models.TierData")))!;
            tierInformation.GetType().GetField("Tiers")!.SetValue(tierInformation, tiers);
            var random = new RandomUtil(TestLogger.Create<RandomUtil>(), new NativeCloner());
            _tierHelper = Activator.CreateInstance(Type("Helpers.TierHelper"), tierInformation, Activator.CreateInstance(Type("Helpers.DateHelper")), random)!;
            var globals = Blank<GlobalTable>();
            var config = Blank<GlobalConfig>();
            typeof(GlobalTable).GetProperty("Configuration")!.SetValue(globals, config);
            config.Exp = new Exp { Level = new Level { ExperienceTable = Enumerable.Range(0, 79).Select(_ => new ExpTable { Experience = 100 }).ToArray() } };
            _levelGenerator = new BotLevelGenerator(globals, random);
            _apbsPatch = (AbstractPatch)Activator.CreateInstance(Type("Patches.GenerateBotLevel"), globals, random, null, _tierHelper)!;
            _range = AccessTools.Method(Type("Patches.GenerateBotLevel"), "GetRelativePmcBotLevelRange");
            _raidInfo = Type("Models.RaidInformation");
            _current = this;
            _harmony.Patch(_range, postfix: new HarmonyMethod(typeof(InstalledApbsHost), nameof(ObserveRange)) { priority = Priority.Last });
            _harmony.Patch(AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot)),
                prefix: new HarmonyMethod(typeof(InstalledApbsHost), nameof(PrepareForLevelOnly)) { priority = Priority.Last });
            _harmony.Patch(AccessTools.Method(typeof(MatchBotDetailsCacheService), "CacheBot"),
                prefix: new HarmonyMethod(typeof(InstalledApbsHost), nameof(SkipPersistence)));
            _controller = new BotController(TestLogger.Create<BotController>(), null!, _generator, null!, null!, null!, null!,
                Blank<MatchBotDetailsCacheService>(), null!, null!, random, null!, null!, new NativeCloner());
            _harmony.Patch(AccessTools.Method(typeof(BotController), "GenerateBotWave"),
                prefix: new HarmonyMethod(typeof(BotTierScalePatch), nameof(BotTierScalePatch.Prefix)));
        }

        // SPT AbstractPatch.Enable rejects callers outside its owning assembly.
        // Register the actual APBS prefix through test-owned Harmony instead.
        internal void EnableApbs() => _harmony.Patch(
            AccessTools.Method(typeof(BotLevelGenerator), nameof(BotLevelGenerator.GenerateBotLevel)),
            prefix: new HarmonyMethod(_apbsPatch.GetType(), "Prefix"));
        internal SptMod UnsupportedVersion() => new() { Directory = Mod.Directory, Assemblies = Mod.Assemblies,
            ModMetadata = new ArenaMode.Server.ModMetadata { ModGuid = ApbsBotTierCompatibility.ModGuid, Version = new("2.4.2") } };
        internal void Configure(int mode, bool split, int level)
        {
            var modeProperty = _config.GetType().GetProperty("LevelPickingMode")!;
            modeProperty.SetValue(_config, Enum.ToObject(modeProperty.PropertyType, mode));
            Set(_additional, "UseGroundZeroSplit", split);
            _raidInfo.GetMethod("ClearRaidLevels")!.Invoke(null, null);
            _raidInfo.GetMethod("AddOrUpdatePlayerLevel")!.Invoke(null, [ArenaSession, level]);
        }
        internal object? GetConfig(string property) => _config.GetType().GetProperty(property)!.GetValue(_config);
        internal object? GetAdditional(string property) => _additional.GetType().GetProperty(property)!.GetValue(_additional);
        internal void SetAdditional(string property, object value) => Set(_additional, property, value);
        internal int CurrentRaidLevel() => (int)_raidInfo.GetProperty("CurrentRaidLevel")!.GetValue(null)!;
        internal int Tier(int level) => (int)_tierHelper.GetType().GetMethod("GetTierByLevel")!.Invoke(_tierHelper, [level])!;
        internal MinMax<int> UnownedRange(BotGenerationDetails details) => (MinMax<int>)_range.Invoke(null, [details, new MinMax<int>(1, 100), 79])!;
        internal BotGenerationDetails Wave() => new() { Location = "factory4_day", Role = "pmcUSEC", Side = "Usec", IsPmc = true,
            PlayerLevel = 47, LocationSpecificPmcLevelOverride = new(33, 61), BotCountToGenerate = 1 };

        internal List<BotBase> Generate(MongoId session, BotGenerationDetails wave, GenerateCondition condition)
        {
            Samples.Clear();
            var deferred = (IEnumerable<BotBase?>)AccessTools.Method(typeof(BotController), "GenerateBotWave")
                .Invoke(_controller, [session, condition, wave])!;
            return deferred.Where(b => b != null).Cast<BotBase>().ToList();
        }
        internal void AssertDeferredRearmRejected()
        {
            Samples.Clear();
            string arm = ArenaContext.Sessions!.GetArmToken(new(ArenaSession))!;
            var deferred = (IEnumerable<BotBase?>)AccessTools.Method(typeof(BotController), "GenerateBotWave")
                .Invoke(_controller, [new MongoId(ArenaSession), new GenerateCondition { Limit = 1 }, Wave()])!;
            ArenaContext.Sessions.Arm(new(ArenaSession), "factory4_day", "tactical", armToken: arm);
            ArenaContext.Sessions.ProcessLocalRaidStart(new(ArenaSession), "factory4_day", ArenaMode.Shared.Protocol.BuildNativeStartIdentity(arm));
            Assert.Single(deferred.ToList());
            Assert.NotEqual((60, 70), (Samples.Single().Min, Samples.Single().Max));
        }
        internal BotBase PrepareDirect(MongoId session, BotGenerationDetails clone) => _generator.PrepareAndGenerateBot(session, clone);
        private static bool PrepareForLevelOnly(BotGenerationDetails __1, ref BotBase __result)
        {
            var host = _current!;
            host.LastClone = __1;
            if (host.ThrowInPrepare) throw new InvalidOperationException("intentional test generator failure");
            var bot = new BotBase { Info = new Info { Side = "Usec" } };
            var level = host._levelGenerator.GenerateBotLevel(new(1, 100), __1, bot);
            bot.Info.Level = level.Level; bot.Info.Experience = level.Exp;
            __result = bot;
            return false;
        }
        private static void ObserveRange(BotGenerationDetails __0, MinMax<int> __result) => _current!.Samples.Add(new(__0, __result.Min, __result.Max));
        private static bool SkipPersistence() => false;
        private static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        private static void Set(object instance, string property, object value) => instance.GetType().GetProperty(property)!.SetValue(instance, value);
        public void Dispose()
        {
            ApbsBotTierCompatibility.Disable();
            _harmony.UnpatchSelf();
            _current = null;
        }
    }
}
