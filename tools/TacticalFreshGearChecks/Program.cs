using System.Reflection;
using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;

// Compiles the real ArenaTacticalFreshGearPatch, ArenaTacticalOpening, TacticalOpeningBandGate and
// ArenaDifficultyPolicy against the host stubs of tools/ArenaBalanceFixtures.cs and drives the prefix, with the
// gate answering the way the server does. Nothing of this has run in the game; the native acceptance reads the
// levels of the bots (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md).
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception("FAIL: " + label); }

    private static GameWorld world = null!;
    private static LocalGame game = null!;
    private static BotCreatorClient creator = null!;
    private static BotProfileClient client = null!;
    private static BotCreationData data = null!;
    private static TacticalOpeningBandGate gate = null!;
    private static float now;

    /// <summary>How the scripted server answers the next report.</summary>
    private enum Answer { Ack, Pending, NotApplying, Refuse }
    private static Answer answer = Answer.Ack;
    private static readonly List<TaskCompletionSource<TacticalRoundReply>> pending = new();
    private static readonly List<(int Round, int Seq)> pendingRequests = new();

    private static Profile P(WildSpawnType role, BotDifficulty difficulty, int level)
        => new() { Info = new Info { Settings = new ProfileSettings { Role = role, BotDifficulty = difficulty }, Level = level } };

    private static Task<TacticalRoundReply> Send(string token, int round, int seq)
    {
        TacticalRoundReply Reply(bool accepted, bool applies, string reason = null)
            => new() { Accepted = accepted, Applies = applies, Round = round, Seq = seq, Reason = reason };
        switch (answer)
        {
            case Answer.Pending:
                var source = new TaskCompletionSource<TacticalRoundReply>();
                pending.Add(source);
                pendingRequests.Add((round, seq));
                return source.Task;
            case Answer.NotApplying: return Task.FromResult(Reply(true, false));
            case Answer.Refuse: return Task.FromResult(Reply(false, false, "scripted refusal"));
            default: return Task.FromResult(Reply(true, true));
        }
    }

    // A solo Tactical raid on the default map (Maximum, levels 1..66), the way the mode publishes it: a world, a game
    // with its creator and profile client, the Arena session, and a gate bound to this world. No round is
    // acknowledged yet, so the purge has no centre.
    private static void Raid(bool reset = true, string mode = "tactical")
    {
        if (reset)
        {
            ArenaTacticalFreshGearPatch.Reset();
            ArenaTacticalOpening.Reset();
        }
        world = new GameWorld { LocationId = "arena_bay5" };
        game = new LocalGame();
        client = new BotProfileClient();
        creator = new BotCreatorClient { _game = game, _creatorProfile = client };
        game.BotsController.BotSpawner._botCreator = creator;
        data = new BotCreationData { _botCreator = creator };
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        ArenaSession.IsActive = true; ArenaSession.Game = game; ArenaSession.World = world;
        ArenaSession.ActiveMode = mode; ArenaSession.Difficulty = ArenaDifficultyStep.Maximum; ArenaSession.ArmedMap = "arena_bay5";
        ArenaClientConfig.StartLevel = 1; ArenaClientConfig.EndLevel = 66;
        ArenaClientConfig.ScaleBotTier = true; ArenaClientConfig.Throw = false;
        Settings.TacticalSquadIsPmc.Value = true;
        PitFireTeamCompat.TeamArenaActive = false;
        UnityEngine.Time.unscaledTime = 1000f;
        now = 0f; answer = Answer.Ack; pending.Clear(); pendingRequests.Clear();
        TacticalOpeningBandGate.LastSeqForTests = 100;
        gate = new TacticalOpeningBandGate(() => now, Send, () => "arm-token", ArenaTacticalOpening.ReadGearContext);
        ArenaTacticalOpening.Bind(gate, world);
        Log.Debugs.Clear(); Log.Warnings = 0;
    }

    /// <summary>The server stored and acknowledged <paramref name="round"/> (answered at once).</summary>
    private static void Acked(int round) { gate.Report(round); gate.Tick(); }

    private static void Fill()
    {
        client._simpleProfiles = new List<Profile>
        {
            P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 5), P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 19),
            P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 20), P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 25),
            P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 30), P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 31),
            P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 45), P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, 66),
            P(WildSpawnType.pmcUSEC, BotDifficulty.hard, 3), P(WildSpawnType.pmcBEAR, BotDifficulty.impossible, 3),
            P(WildSpawnType.pmcBEAR, BotDifficulty.impossible, 70), P(WildSpawnType.assault, BotDifficulty.impossible, 3),
        };
    }
    private static string Pool() => string.Join(" ", client._simpleProfiles.Select(p => $"{p.Info.Settings.Role}/{p.Info.Settings.BotDifficulty}/{p.Info.Level}"));
    private static bool Has(WildSpawnType role, BotDifficulty difficulty, int level)
        => client._simpleProfiles.Any(p => p.Info.Settings.Role == role && p.Info.Settings.BotDifficulty == difficulty && p.Info.Level == level);
    private static bool Usec(int level) => Has(WildSpawnType.pmcUSEC, BotDifficulty.impossible, level);
    private static bool Bear(int level) => Has(WildSpawnType.pmcBEAR, BotDifficulty.impossible, level);
    private static int UsecCount() => client._simpleProfiles.Count(p => p.Info.Settings.Role == WildSpawnType.pmcUSEC && p.Info.Settings.BotDifficulty == BotDifficulty.impossible);

    private static void Demand(WildSpawnType role = WildSpawnType.pmcUSEC, BotDifficulty difficulty = BotDifficulty.impossible, bool withDelete = true,
        CancellationToken token = default, bool nullData = false)
    {
        data._profileData.Role = role; data._profileData.Difficulty = difficulty;
        try { ArenaTacticalFreshGearPatch.Prefix(client, nullData ? null! : data, token, withDelete); }
        catch (Exception) { throw new Exception("FAIL: an exception inside the patch never reaches the game"); }
    }

    // A raid whose round 1 is acknowledged (centre 25, band 20..30) and whose pool holds one of everything.
    private static void Round1Raid() { Raid(); Acked(1); Fill(); }

    private static void NoPurge(string label, Action change = null, Action demand = null, bool acknowledge = true)
    {
        Raid(); if (acknowledge) Acked(1); Fill();
        change?.Invoke();
        var before = Pool(); int debugs = Log.Debugs.Count;
        (demand ?? (() => Demand()))();
        Check(Pool() == before && Log.Debugs.Count == debugs, label);
    }

    private static void Purge()
    {
        Round1Raid(); Demand();
        Check(!Usec(5) && !Usec(19) && !Usec(31) && !Usec(45) && !Usec(66), "profiles below and above the band of the acknowledged round are dropped");
        Check(Usec(20) && Usec(25) && Usec(30), "a profile at the lower edge, the centre and the upper edge of the band is kept");
        Check(Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 3), "profiles of another difficulty are untouched");
        Check(Bear(3) && Bear(70) && Has(WildSpawnType.assault, BotDifficulty.impossible, 3), "profiles of other roles are untouched");
        Check(client._simpleProfiles.Count == 7, "nothing but the profiles outside the band leaves the pool");
        Check(Log.Debugs.Count == 1 && Log.Debugs[0].Contains("dropped 5 pooled pmcUSEC/impossible profile(s), levels 5 to 66, gear level now 25"),
            "a purge logs one line with role, difficulty, count, lowest, highest and the gear level");
        Check(Log.Warnings == 0, "a purge logs at Debug, never a warning");
        Demand(WildSpawnType.pmcBEAR);
        Check(!Bear(3) && !Bear(70), "a BEAR demand drops its own profiles outside the band, both sides");
    }

    // Round 1 -> 2 -> 3, then a falling band: each acknowledgement moves the centre, and the purge follows it.
    private static void Stages()
    {
        Round1Raid(); Demand();
        Acked(2);
        Fill(); Demand();
        Check(!Usec(5) && !Usec(25) && !Usec(31) && !Usec(66) && Usec(45) && Usec(30) == false, "round 2 acknowledged: the band is 40..50, round 1's profiles are dropped and 45 is kept");
        Acked(3);
        Fill(); Demand();
        Check(Usec(66) && !Usec(45) && !Usec(30), "round 3 acknowledged: the band is 61..71 at the full level, the round 2 profile is dropped");
        Acked(4);
        Fill(); Demand();
        Check(Usec(66) && !Usec(45), "round 4 acknowledged: the same full band");
        Acked(1);
        Fill(); Demand();
        Check(Usec(25) && Usec(30) && !Usec(66) && !Usec(45), "a falling band (round 1 acknowledged after round 4) drops the high profiles");
        Check(Log.Warnings == 0, "the stage changes cost no warning");

        // Reduced follows its own centres (15, 27, 40).
        Raid(); ArenaSession.Difficulty = ArenaDifficultyStep.Reduced; Acked(1);
        client._simpleProfiles = new List<Profile> { P(WildSpawnType.pmcUSEC, BotDifficulty.hard, 9), P(WildSpawnType.pmcUSEC, BotDifficulty.hard, 10), P(WildSpawnType.pmcUSEC, BotDifficulty.hard, 20), P(WildSpawnType.pmcUSEC, BotDifficulty.hard, 21) };
        Demand(difficulty: BotDifficulty.hard);
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 9) && Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 10)
              && Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 20) && !Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 21),
            "Reduced round 1: centre 15, band 10..20");
    }

    // At most two purges per (role, difficulty, centre); a new centre, a new raid and Reset start over.
    private static void Budget()
    {
        Round1Raid(); Demand();
        Fill(); Demand();
        Check(!Usec(5) && !Usec(66), "a second purge for the same centre is allowed (a refill that landed after the first)");
        Check(Log.Debugs.Count == 2, "each purge logs one line");
        Fill(); var before = Pool(); Log.Debugs.Clear(); Demand();
        Check(Pool() == before, "the third purge for the same centre is not made");
        Check(Log.Debugs.Count == 1 && Log.Debugs[0].Contains("2 purges for this level are spent"), "a skipped purge says so once");
        Check(Log.Warnings == 1, "a skipped purge warns once per raid that the server's band does not match the round");
        Demand(); Demand();
        Check(Log.Debugs.Count == 1 && Log.Warnings == 1, "the skip is reported once per pair and centre, the warning once per raid");

        // The other role has a budget of its own (the USEC budget is spent).
        Check(Bear(3) && Bear(70), "a BEAR profile is untouched while the USEC budget is spent");
        Demand(WildSpawnType.pmcBEAR);
        Check(!Bear(3) && !Bear(70), "a BEAR demand has its own budget");
        Fill(); Demand(difficulty: BotDifficulty.hard);
        Check(!Has(WildSpawnType.pmcUSEC, BotDifficulty.hard, 3), "a demand of another difficulty has its own budget");

        // A new centre starts over: the acknowledgement of round 2 (a late ack that raises the round does the same).
        Acked(2); Fill(); Demand();
        Check(!Usec(5) && !Usec(25) && Usec(45), "a new centre starts the budget over");

        // A report that is answered late: the centre stays at the acknowledged round until the ack lands.
        Round1Raid(); Demand(); Fill(); Demand(); Fill(); Demand();
        answer = Answer.Pending; gate.Report(2); gate.Tick();
        Check(gate.AckedRound == 1 && pendingRequests.Count == 1, "round 2's report is in flight");
        Fill(); before = Pool(); Demand();
        Check(Pool() == before, "while the ack is outstanding the centre is still round 1's and its budget stays spent");
        pending[0].SetResult(new TacticalRoundReply { Accepted = true, Applies = true, Round = 2, Seq = pendingRequests[0].Seq });
        gate.Tick();
        Check(gate.AckedRound == 2, "the late acknowledgement raises the acknowledged round");
        Fill(); Demand();
        Check(!Usec(25) && !Usec(5) && Usec(45), "a late acknowledgement that raises the round starts the new centre's budget fresh");

        // The lost-ack shape: the server is one stage AHEAD of the client's centre (round 1 acknowledged on the client,
        // round 2 stored on the server). Every fresh batch the miss path returns is at the server's band (levels 40..50
        // for round 2's centre 45), which is outside the client's band 20..30, so the second demand purges it too and
        // only the third is served, with the one warning. The cost is two extra 15-profile batches, bounded by the budget.
        Round1Raid(); Demand();
        void ServerBandBatch() { foreach (int level in new[] { 41, 45, 49 }) client._simpleProfiles.Add(P(WildSpawnType.pmcUSEC, BotDifficulty.impossible, level)); }
        ServerBandBatch(); Demand();
        Check(!Usec(41) && !Usec(45) && !Usec(49) && Log.Debugs.Count == 2 && Log.Warnings == 0,
            "a batch at the server's band that is one stage ahead of the client is purged once more (the second purge)");
        ServerBandBatch(); before = Pool(); Demand();
        Check(Pool() == before && Usec(45) && Log.Warnings == 1,
            "the third purge is not made: the batch at the server's band is served and the mismatch is warned once");

        // A purge that found nothing spends nothing.
        Round1Raid(); client._simpleProfiles.RemoveAll(p => p.Info.Level < 20 || p.Info.Level > 30);
        for (int i = 0; i < 4; i++) Demand();
        Fill(); Demand();
        Check(!Usec(5) && !Usec(66), "a demand that found nothing to drop spends nothing of the budget");

        // The cost of a short round: there is no time interval, so a stage change right after a purge purges.
        Round1Raid(); Demand(); UnityEngine.Time.unscaledTime = 1005f; Acked(2); Fill(); Demand();
        Check(!Usec(25) && Usec(45), "a stage change five seconds after a purge still purges (no interval)");
    }

    private static void SecondRaid()
    {
        Round1Raid(); Demand(); Fill(); Demand(); Fill(); Demand();
        Check(Usec(5), "the budget of the first raid is spent");
        // The next raid has another world; nothing calls Reset here, the world check clears the budget.
        Raid(reset: false); Acked(1); Fill(); Demand();
        Check(!Usec(5) && !Usec(66), "a second raid with the same centre starts with a fresh budget");
        Log.Warnings = 0;
        Fill(); Demand(); Fill(); Demand();
        Check(Log.Warnings == 1, "the mismatch warning of the second raid is its own");
        // Reset, in the same world.
        Round1Raid(); Demand(); Fill(); Demand(); Fill(); Demand();
        ArenaTacticalFreshGearPatch.Reset(); Fill(); Demand();
        Check(!Usec(5), "Reset starts the next raid with a fresh budget");
        Round1Raid(); Demand(); Fill(); Demand(); Fill(); Demand(); Log.Warnings = 0;
        ArenaTacticalFreshGearPatch.Reset(); Fill(); Demand(); Fill(); Demand(); Fill(); Demand();
        Check(Log.Warnings == 1, "Reset lets the mismatch warn again");
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
        NoPurge("Gun Game purges nothing", () => ArenaSession.ActiveMode = "gungame");
        NoPurge("Team Deathmatch purges nothing", () => ArenaSession.ActiveMode = "teamdeathmatch");
        NoPurge("Zombies purge nothing", () => ArenaSession.ActiveMode = "zombie");
        NoPurge("a protected team raid purges nothing", () => PitFireTeamCompat.TeamArenaActive = true);
        NoPurge("a raid with no acknowledged round purges nothing", acknowledge: false);
        NoPurge("a feature the server does not apply purges nothing", () => { answer = Answer.NotApplying; Acked(2); }, null);
        Check(!gate.Applies, "the gate that was told applies:false stops applying");
        NoPurge("a refused report purges nothing", () => { answer = Answer.Refuse; Acked(2); });
        NoPurge("a scav squad purges nothing", () => Settings.TacticalSquadIsPmc.Value = false);
        NoPurge("a map with scaleBotTier off purges nothing", () => ArenaClientConfig.ScaleBotTier = false);
        NoPurge("an unreadable config purges nothing", () => ArenaClientConfig.Throw = true);
        Check(Log.Warnings == 0, "an unreadable config is no failure of the patch");
        NoPurge("a map whose opening equals the full level purges nothing", () => ArenaClientConfig.EndLevel = 20);
        NoPurge("a gate bound in another world purges nothing", () => ArenaTacticalOpening.Bind(gate, new GameWorld()));
        NoPurge("a match that ended (its gate unpublished) purges nothing", () => ArenaTacticalOpening.Unbind(gate));
        NoPurge("a profile client with nothing outside the band purges nothing", () => client._simpleProfiles.RemoveAll(p => p.Info.Level < 20 || p.Info.Level > 30));
        // The scav setting is read live: flipping it back purges again.
        Round1Raid(); Settings.TacticalSquadIsPmc.Value = false; Demand(); Settings.TacticalSquadIsPmc.Value = true; Demand();
        Check(!Usec(5), "the squad's role is read at every demand");
    }

    private static void Failures()
    {
        Round1Raid(); Log.Warnings = 0; Log.Debugs.Clear();
        client._simpleProfiles.Add(new Profile { Info = new Info { Settings = new ProfileSettings { Role = WildSpawnType.pmcUSEC, BotDifficulty = BotDifficulty.impossible }, Poison = true } });
        var before = client._simpleProfiles.ToList(); Demand();
        Check(client._simpleProfiles.SequenceEqual(before), "an exception while reading the pool leaves it intact");
        Check(Log.Warnings == 1, "a failure is a warning");
        Demand(); Demand();
        Check(Log.Warnings == 1, "a failure warns once per raid");
        ArenaTacticalFreshGearPatch.Reset(); ArenaTacticalOpening.Bind(gate, world); Demand();
        Check(Log.Warnings == 2, "a failure warns again in the next raid");
        Check(Log.Debugs.Count == 0, "a failure logs nothing at Debug");
    }

    private static string Text(string key)
    {
        var path = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;
        return System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"\s+", " ");
    }

    private static void Wiring()
    {
        var target = (MethodBase)typeof(ArenaTacticalFreshGearPatch).GetMethod("GetTargetMethod", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Activator.CreateInstance(typeof(ArenaTacticalFreshGearPatch), true), null);
        Check(target != null && target.Name == "CreateProfile" && target.DeclaringType == typeof(BotProfileClient)
            && target.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "data", "cancellationToken", "withDelete" }),
            "the patch targets BotProfileClient.CreateProfile(data, cancellationToken, withDelete)");
        var prefix = typeof(ArenaTacticalFreshGearPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(prefix.GetCustomAttribute<PatchPrefixAttribute>() != null && prefix.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "__instance", "data", "cancellationToken", "withDelete" }),
            "Prefix is a Harmony prefix with the target's parameter names");
        Log.Warnings = 0; int enabled = ModulePatch.Enabled;
        ArenaTacticalFreshGearPatch.EnablePatch();
        Check(ModulePatch.Enabled == enabled + 1 && Log.Warnings == 0, "EnablePatch enables the patch");
        ModulePatch.FailEnable = true;
        try { ArenaTacticalFreshGearPatch.EnablePatch(); } catch (Exception) { throw new Exception("FAIL: a failing Enable never reaches Plugin.Load"); }
        finally { ModulePatch.FailEnable = false; }
        Check(Log.Warnings == 1, "a failing Enable warns once and the plugin loads on");

        var plugin = Text("PluginText");
        Check(plugin.Contains("Bots.ArenaGunGameFreshGearPatch.EnablePatch(); Bots.ArenaTacticalFreshGearPatch.EnablePatch();"),
            "Plugin.Load enables the Tactical fresh gear patch right after the Gun Game one");
        var driver = Text("DriverText");
        Check(driver.Contains("Bots.ArenaGunGameFreshGearPatch.Reset(); Bots.ArenaTacticalFreshGearPatch.Reset();"),
            "the raid reset resets the Tactical fresh gear patch beside the Gun Game one");
        Check(driver.Contains("Bots.ArenaTacticalFreshGearPatch.Reset(); Bots.ArenaTacticalOpening.Reset();"),
            "the raid reset unpublishes the Tactical opening state");
    }

    private static void Main()
    {
        Check(ArenaTacticalFreshGearPatch.PurgesPerCentre == 2, "two purges per centre");
        Purge();
        Stages();
        Failures();
        Budget();
        SecondRaid();
        Guards();
        Wiring();
        Console.WriteLine($"ARENA_TACTICAL_FRESH_GEAR_BASELINE_PASSED checks={checks}");
    }
}
