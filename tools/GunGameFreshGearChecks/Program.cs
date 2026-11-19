using System.Reflection;
using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;

// Compiles the real ArenaGunGameFreshGearPatch, ArenaDifficulty and ArenaDifficultyPolicy against the host stubs of
// tools/ArenaBalanceFixtures.cs and drives the prefix. Nothing of this has run in the game.
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception("FAIL: " + label); }

    private static GameWorld world = null!;
    private static LocalGame game = null!;
    private static BotCreatorClient creator = null!;
    private static BotProfileClient client = null!;
    private static BotCreationData data = null!;

    private static Profile P(WildSpawnType role, BotDifficulty difficulty, int level)
        => new() { Info = new Info { Settings = new ProfileSettings { Role = role, BotDifficulty = difficulty }, Level = level } };

    // The raid of the measurement: Gun Game, Maximum, 600 s into a 15 minute ramp 1..66, so the gear level is 44
    // and a profile below 34 is stale. The pool holds one of everything the patch must tell apart.
    private static void Raid(bool reset = true)
    {
        if (reset) ArenaGunGameFreshGearPatch.Reset();
        world = new GameWorld { LocationId = "arena_bay5" };
        game = new LocalGame();
        client = new BotProfileClient();
        creator = new BotCreatorClient { _game = game, _creatorProfile = client };
        game.BotsController.BotSpawner._botCreator = creator;
        data = new BotCreationData { _botCreator = creator };
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        ArenaSession.IsActive = true; ArenaSession.Game = game; ArenaSession.World = world;
        ArenaSession.ActiveMode = "gungame"; ArenaSession.Difficulty = ArenaDifficultyStep.Maximum; ArenaSession.ArmedMap = "arena_bay5";
        ArenaMatchService.Current = new Match { Mode = new Mode(), Elapsed = 600 };
        ArenaClientConfig.RampMinutes = 15; ArenaClientConfig.StartLevel = 1; ArenaClientConfig.EndLevel = 66;
        ArenaClientConfig.ScaleBotTier = true; ArenaClientConfig.Throw = false;
        UnityEngine.Time.unscaledTime = 1000f;
        Fill();
    }
    private static void Fill()
    {
        client._simpleProfiles = new List<Profile>
        {
            P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 20),
            P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 34), P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 50),
            P(WildSpawnType.pmcUSEC, BotDifficulty.hard, 3), P(WildSpawnType.pmcBEAR, BotDifficulty.impossible, 3),
            P(WildSpawnType.assault, BotDifficulty.impossible, 3),
        };
    }
    private static string Pool() => string.Join(" ", client._simpleProfiles.Select(p => $"{p.Info.Settings.Role}/{p.Info.Settings.BotDifficulty}/{p.Info.Level}"));
    private static bool Has(WildSpawnType role, BotDifficulty difficulty, int level)
        => client._simpleProfiles.Any(p => p.Info.Settings.Role == role && p.Info.Settings.BotDifficulty == difficulty && p.Info.Level == level);
    private static void Demand(WildSpawnType role = WildSpawnType.pmcUSEC, BotDifficulty difficulty = BotDifficulty.impossible, bool withDelete = true,
        CancellationToken token = default, bool nullData = false)
    {
        data._profileData.Role = role; data._profileData.Difficulty = difficulty;
        try { ArenaGunGameFreshGearPatch.Prefix(client, nullData ? null! : data, token, withDelete); }
        catch (Exception) { throw new Exception("FAIL: an exception inside the patch never reaches the game"); }
    }
    private static void NoPurge(string label, Action change = null, Action demand = null)
    {
        Raid(); change?.Invoke(); var before = Pool(); int debugs = Log.Debugs.Count;
        (demand ?? (() => Demand()))();
        Check(Pool() == before && Log.Debugs.Count == debugs, label);
    }

    private static void Purge()
    {
        Log.Debugs.Clear(); Log.Warnings = 0; Raid(); Demand();
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5) && !Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 20), "stale profiles of the demanded pair are dropped");
        Check(Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 34), "a profile exactly ten levels below the current one is kept");
        Check(Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 50), "a fresh profile of the demanded pair is kept");
        Check(Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 3), "profiles of another difficulty are untouched");
        Check(Has(WildSpawnType.pmcBEAR, BotDifficulty.impossible, 3) && Has(WildSpawnType.assault, BotDifficulty.impossible, 3), "profiles of other roles are untouched");
        Check(client._simpleProfiles.Count == 5, "nothing but the stale profiles leaves the pool");
        Check(Log.Debugs.Count == 1 && Log.Debugs[0].Contains("dropped 2 pooled pmcUSEC/impossible profile(s), lowest level 5, gear level now 44"), "a purge logs one line with role, difficulty, count, lowest and current level");
        Check(Log.Warnings == 0, "a purge logs at Debug, never a warning");
        // The BEAR demand is its own pair: the USEC purge above does not use its interval.
        Demand(WildSpawnType.pmcBEAR);
        Check(!Has(WildSpawnType.pmcBEAR, BotDifficulty.impossible, 3), "a BEAR demand drops its own stale profile");
    }

    private static void Guards()
    {
        NoPurge("a raid that is not active purges nothing", () => ArenaSession.IsActive = false);
        NoPurge("a raid that is not the exact one purges nothing", () => ArenaSession.World = new GameWorld());
        NoPurge("a game that is not started purges nothing", () => game.Status = GameStatus.Stopping);
        NoPurge("a demand of another creator purges nothing", () => game.BotsController.BotSpawner._botCreator = new BotCreatorClient { _game = game, _creatorProfile = client });
        NoPurge("a creator of another game purges nothing", () => creator._game = new LocalGame());
        NoPurge("a creator of another profile client purges nothing", () => creator._creatorProfile = new BotProfileClient());
        NoPurge("a demand without data purges nothing", demand: () => Demand(nullData: true));
        Check(Log.Warnings == 0, "a demand without data is no failure");
        NoPurge("a stopped spawn purges nothing", () => data.SpawnStopped = true);
        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        NoPurge("a cancelled demand purges nothing", demand: () => Demand(token: cancelled.Token));
        NoPurge("a demand without a role purges nothing", () => data._profileData.Valid = false);
        NoPurge("a demand that deletes nothing purges nothing", demand: () => Demand(withDelete: false));
        NoPurge("a scav demand purges nothing", demand: () => Demand(WildSpawnType.assault));
        NoPurge("Team Deathmatch purges nothing", () => ArenaSession.ActiveMode = "teamdeathmatch");
        NoPurge("a map with scaleBotTier off purges nothing", () => ArenaClientConfig.ScaleBotTier = false);
        NoPurge("an unreadable config purges nothing", () => ArenaClientConfig.Throw = true);
        Check(Log.Warnings == 0, "an unreadable config is no failure");
        NoPurge("the opening of the ramp purges nothing", () => ArenaMatchService.Current!.Elapsed = 0);
        NoPurge("a profile client with no stale profile purges nothing", () => client._simpleProfiles.RemoveAll(p => p.Info.Level < 34));
    }

    private static void Interval()
    {
        Log.Debugs.Clear(); Raid(); Demand();
        Fill(); UnityEngine.Time.unscaledTime = 1010f; Demand();
        Check(Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), "a second purge of the same pair within 30 seconds is held back");
        UnityEngine.Time.unscaledTime = 1029.5f; Demand();
        Check(Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), "the interval is not over before 30 seconds");
        UnityEngine.Time.unscaledTime = 1030f; Demand();
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), "the purge resumes after 30 seconds");
        // Another difficulty is another pair.
        Raid(); client._simpleProfiles.Add(P(WildSpawnType.pmcUSEC, BotDifficulty.hard, 4)); Demand();
        UnityEngine.Time.unscaledTime = 1005f; Demand(difficulty: BotDifficulty.hard);
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 4) && !Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 3), "the interval is kept per difficulty");
        // A new raid (another world) and an explicit Reset both start without an interval.
        Raid(); Demand(); Raid(reset: false); UnityEngine.Time.unscaledTime = 1001f; Demand();
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), "a new world starts with no interval");
        Raid(); Demand(); ArenaGunGameFreshGearPatch.Reset(); Fill(); UnityEngine.Time.unscaledTime = 1001f; Demand();
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), "Reset starts the next raid with no interval");
        // An empty purge starts no interval.
        Raid(); client._simpleProfiles.RemoveAll(p => p.Info.Level < 34); Demand();
        Fill(); UnityEngine.Time.unscaledTime = 1001f; Demand();
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), "a demand that found nothing to drop starts no interval");
    }

    private static void Failures()
    {
        Raid(); Log.Warnings = 0; Log.Debugs.Clear();
        client._simpleProfiles.Add(new Profile { Info = new Info { Settings = new ProfileSettings { Role = WildSpawnType.pmcUSEC, BotDifficulty = BotDifficulty.impossible }, Poison = true } });
        var before = client._simpleProfiles.ToList(); Demand();
        Check(client._simpleProfiles.SequenceEqual(before), "an exception while reading the pool leaves it intact");
        Check(Log.Warnings == 1, "a failure is a warning");
        Demand(); Demand();
        Check(Log.Warnings == 1, "a failure warns once per raid");
        ArenaGunGameFreshGearPatch.Reset(); Demand();
        Check(Log.Warnings == 2, "a failure warns again in the next raid");
        Check(Log.Debugs.Count == 0, "a failure logs nothing at Debug");
    }

    private static void Reader()
    {
        int? Level(string mode, ArenaDifficultyStep step, float elapsed) { Raid(); ArenaSession.ActiveMode = mode; ArenaSession.Difficulty = step; ArenaMatchService.Current = new Match { Mode = new Mode(), Elapsed = elapsed }; return ArenaDifficulty.CurrentGunGameGearLevel; }
        Check(Level("gungame", ArenaDifficultyStep.Maximum, 0) == 1, "the Gun Game gear level opens at the start level");
        Check(Level("gungame", ArenaDifficultyStep.Maximum, 600) == 44, "the Gun Game gear level follows the ramp at Maximum");
        Check(Level("gungame", ArenaDifficultyStep.Maximum, 900) == 66 && Level("gungame", ArenaDifficultyStep.Maximum, 5000) == 66, "the Gun Game gear level ends at the map's end level");
        Check(Level("gungame", ArenaDifficultyStep.Reduced, 600) == 27, "the Gun Game gear level follows the frozen step");
        Raid(); ArenaClientConfig.StartLevel = 10; ArenaClientConfig.EndLevel = 50; ArenaMatchService.Current!.Elapsed = 450;
        Check(ArenaDifficulty.CurrentGunGameGearLevel == 30, "the Gun Game gear level uses the map's start and end level");
        Check(Level("teamdeathmatch", ArenaDifficultyStep.Maximum, 600) == null && Level("zombie", ArenaDifficultyStep.Maximum, 600) == null, "outside Gun Game there is no gear level");
        Raid(); ArenaClientConfig.ScaleBotTier = false;
        Check(ArenaDifficulty.CurrentGunGameGearLevel == null, "a map with scaleBotTier off has no gear level");
        Raid(); ArenaClientConfig.Throw = true;
        Check(ArenaDifficulty.CurrentGunGameGearLevel == null, "an unreadable config gives no gear level");
        Raid(); ArenaMatchService.Current = null;
        Check(ArenaDifficulty.CurrentGunGameGearLevel == 1, "Gun Game without a match is the opening level");
    }

    private static void Policy()
    {
        Check(ArenaDifficultyPolicy.GunGameStaleGearLevels == 10, "the slack is ten levels");
        Check(!ArenaDifficultyPolicy.IsGunGameGearStale(34, 44) && ArenaDifficultyPolicy.IsGunGameGearStale(33, 44), "ten below is fresh, eleven below is stale");
        Check(!ArenaDifficultyPolicy.IsGunGameGearStale(44, 44) && !ArenaDifficultyPolicy.IsGunGameGearStale(60, 44), "a profile at or above the ramp is fresh");
        for (int centre = 1; centre <= 11; centre++) for (int level = 1; level <= 6; level++)
            Check(!ArenaDifficultyPolicy.IsGunGameGearStale(level, centre), "the opening of the ramp never purges the opening profiles");
    }

    private static string Text(string key)
    {
        var path = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;
        return System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"\s+", " ");
    }
    private static void Wiring()
    {
        var target = (MethodBase)typeof(ArenaGunGameFreshGearPatch).GetMethod("GetTargetMethod", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Activator.CreateInstance(typeof(ArenaGunGameFreshGearPatch), true), null);
        Check(target != null && target.Name == "CreateProfile" && target.DeclaringType == typeof(BotProfileClient)
            && target.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "data", "cancellationToken", "withDelete" }),
            "the patch targets BotProfileClient.CreateProfile(data, cancellationToken, withDelete)");
        var prefix = typeof(ArenaGunGameFreshGearPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(prefix.GetCustomAttribute<PatchPrefixAttribute>() != null && prefix.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "__instance", "data", "cancellationToken", "withDelete" }),
            "Prefix is a Harmony prefix with the target's parameter names");
        Log.Warnings = 0; int enabled = ModulePatch.Enabled;
        ArenaGunGameFreshGearPatch.EnablePatch();
        Check(ModulePatch.Enabled == enabled + 1 && Log.Warnings == 0, "EnablePatch enables the patch");
        ModulePatch.FailEnable = true;
        try { ArenaGunGameFreshGearPatch.EnablePatch(); } catch (Exception) { throw new Exception("FAIL: a failing Enable never reaches Plugin.Load"); }
        finally { ModulePatch.FailEnable = false; }
        Check(Log.Warnings == 1, "a failing Enable warns once and the plugin loads on");

        Check(Text("PluginText").Contains("Bots.ArenaProfilePreloadPatch.EnablePatch(); Bots.ArenaGunGameFreshGearPatch.EnablePatch();"), "Plugin.Load enables the Gun Game fresh gear patch");
        Check(Text("DriverText").Contains("Bots.ArenaGunGameFreshGearPatch.Reset();"), "the raid reset resets the Gun Game fresh gear patch");
        var clientText = Text("ClientConfigText"); var server = Text("ServerConfigText");
        foreach (var key in new[] { "botTierStartLevel", "botTierEndLevel", "scaleBotTier" })
            Check(clientText.Contains($"GetMapValue<") && clientText.Contains($"\"{key}\"") && server.Contains($"JsonPropertyName(\"{key}\")"), $"client and server read the same {key} key");
        Check(clientText.Contains("GetBotTierStartLevel(string map, int fallback = 1) => GetMapValue<int?>(map, \"botTierStartLevel\") ?? fallback;")
            && server.Contains("public int BotTierStartLevel { get; set; } = 1;"), "the client's start level fallback is the server's default");
        Check(clientText.Contains("GetBotTierEndLevel(string map, int fallback = 66) => GetMapValue<int?>(map, \"botTierEndLevel\") ?? fallback;")
            && server.Contains("public int BotTierEndLevel { get; set; } = 66;"), "the client's end level fallback is the server's default");
        Check(clientText.Contains("GetScaleBotTier(string map, bool fallback = true) => GetMapValue<bool?>(map, \"scaleBotTier\") ?? fallback;")
            && server.Contains("public bool ScaleBotTier { get; set; } = true;"), "the client's scaleBotTier fallback is the server's default");
    }

    private static void Main()
    {
        Policy();
        Reader();
        Purge();
        Guards();
        Interval();
        Failures();
        Wiring();
        Console.WriteLine($"ARENA_GUNGAME_FRESH_GEAR_BASELINE_PASSED checks={checks}");
    }
}
