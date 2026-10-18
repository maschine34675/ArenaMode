using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System.Reflection;
using UnityEngine;

internal static class Program
{
    private static int _assertions, _identity;
    private static readonly WildSpawnType[] Generic = { WildSpawnType.infectedAssault, WildSpawnType.infectedCivil,
        WildSpawnType.infectedLaborant, WildSpawnType.infectedPmc };
    private sealed class ForeignPlayer : IPlayer { public string ProfileId { get; set; } public int Id { get; set; } public AIData AIData { get; set; } = new(); }
    private sealed class Fixture
    {
        public GameWorld World; public LocalGame Game; public BotOwner Listener, Source;
        public Fixture()
        {
            Game = new(); World = new();
            Singleton<GameWorld>.Instance = World; Singleton<AbstractGame>.Instance = Game;
            ArenaSession.IsActive = true; ArenaSession.ActiveMode = "zombie";
            ArenaSession.StartedGame = Game; ArenaSession.StartedWorld = World;
            Time.time = Time.realtimeSinceStartup = 100;
            Plugin.ModLogger.Infos.Clear(); Plugin.ModLogger.Warnings.Clear();
            Listener = Bot(); Source = Bot();
        }
        public BotOwner Bot()
        {
            int id = ++_identity;
            var body = new Player { ProfileId = "bot-" + id, Id = id };
            var bot = new BotOwner { GetPlayer = body };
            body.AIData.BotOwner = bot;
            bot.HearingSensor = new BotHearingSensor { _botOwner = bot };
            bot.FriendChecker = new BotFriendChecker { _owner = bot };
            bot.BotsGroup = new BotsGroup { BotGame = Game, InitialBotType = WildSpawnType.infectedAssault };
            bot.BotsGroup.Members.Add(bot);
            foreach (var role in Generic) bot.BotsGroup.Suitable.Add(role);
            World.Alive.Add(body.ProfileId, body);
            return bot;
        }
        public bool Hearing(IPlayer source = null, AISoundType type = AISoundType.step)
            => ZombiePerformancePatches.ShouldRunHearing(Listener.HearingSensor, source ?? Source.GetPlayer, type);
        public bool Social() => ZombiePerformancePatches.ShouldRunSocial(Listener.FriendChecker);
    }
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void HearingRetained(string name, Action<Fixture> arrange)
    {
        var f = new Fixture(); arrange(f); Check(f.Hearing(), name);
    }
    private static void BothRetained(string name, Action<Fixture> arrange)
    {
        var f = new Fixture(); arrange(f); Check(f.Hearing(), name + " hearing"); Check(f.Social(), name + " social");
    }
    private static void FastHearingRetained(string name, Action<Fixture> arrange)
    {
        var f = new Fixture(); arrange(f); Check(f.Hearing(), name + " remains vanilla");
        Check(f.World.LookupCalls == 0, name + " does not pay for body/registry admission");
    }
    private static void Main()
    {
        foreach (var listener in Generic)
        foreach (var source in Generic)
        {
            var f = new Fixture(); f.Listener.GetPlayer.Profile.Info.Settings.Role = listener;
            f.Source.GetPlayer.Profile.Info.Settings.Role = source;
            Check(!f.Hearing(), "known-friendly role pair " + listener + "/" + source);
            Check(!f.Social(), "generic social " + listener);
        }
        BothRetained("ordinary", f => ArenaSession.IsActive = false);
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "unknown", null })
            BothRetained("other mode " + mode, f => ArenaSession.ActiveMode = mode);
        BothRetained("world missing", f => Singleton<GameWorld>.Instance = null);
        BothRetained("game missing", f => Singleton<AbstractGame>.Instance = null);
        BothRetained("foreign game", f => Singleton<AbstractGame>.Instance = new AbstractGame());
        BothRetained("stale started world", f => ArenaSession.StartedWorld = new GameWorld());
        BothRetained("stale started game", f => ArenaSession.StartedGame = new LocalGame());
        BothRetained("loading", f => f.Game.Status = GameStatus.Starting);
        BothRetained("teardown", f => f.Game.Status = GameStatus.Stopping);
        BothRetained("destroyed world", f => f.World.Destroyed = true);
        BothRetained("destroyed game", f => f.Game.Destroyed = true);
        foreach (var role in new[] { WildSpawnType.infectedTagilla, WildSpawnType.pmcUSEC, WildSpawnType.assault, (WildSpawnType)999 })
        {
            BothRetained("non-generic listener " + role, f => f.Listener.GetPlayer.Profile.Info.Settings.Role = role);
            HearingRetained("non-generic source " + role, f => f.Source.GetPlayer.Profile.Info.Settings.Role = role);
        }
        BothRetained("dead listener", f => f.Listener.IsDead = true);
        BothRetained("dead listener health", f => f.Listener.GetPlayer.HealthController.IsAlive = false);
        BothRetained("unknown listener health", f => f.Listener.GetPlayer.HealthController = null);
        BothRetained("human listener", f => f.Listener.GetPlayer.IsAI = false);
        BothRetained("local listener", f => f.Listener.GetPlayer.IsYourPlayer = true);
        BothRetained("main listener", f => f.World.MainPlayer = f.Listener.GetPlayer);
        BothRetained("destroyed listener", f => f.Listener.Destroyed = true);
        BothRetained("destroyed body", f => f.Listener.GetPlayer.Destroyed = true);
        BothRetained("preactive listener", f => f.Listener.BotState = EBotState.PreActive);
        BothRetained("retired listener", f => f.Listener.BotState = EBotState.Disposed);
        BothRetained("missing listener body", f => f.Listener.GetPlayer = null);
        BothRetained("wrong owner link", f => f.Listener.GetPlayer.AIData.BotOwner = f.Source);
        BothRetained("missing AI data", f => f.Listener.GetPlayer.AIData = null);
        BothRetained("missing role", f => f.Listener.GetPlayer.Profile.Info.Settings = null);
        BothRetained("missing profile", f => f.Listener.GetPlayer.Profile = null);
        BothRetained("missing ID", f => f.Listener.GetPlayer.ProfileId = "");
        BothRetained("unregistered body", f => f.World.Alive.Remove(f.Listener.ProfileId));
        BothRetained("same-profile replacement", f => f.World.Alive[f.Listener.ProfileId] = new Player { ProfileId = f.Listener.ProfileId });
        BothRetained("failed native lookup", f => f.World.ThrowLookup = true);
        HearingRetained("dead source", f => f.Source.IsDead = true);
        HearingRetained("source replaced", f => f.World.Alive[f.Source.ProfileId] = new Player { ProfileId = f.Source.ProfileId });
        HearingRetained("human source", f => f.Source.GetPlayer.IsAI = false);
        HearingRetained("local source", f => f.Source.GetPlayer.IsYourPlayer = true);
        HearingRetained("main source", f => f.World.MainPlayer = f.Source.GetPlayer);
        HearingRetained("source preactive", f => f.Source.BotState = EBotState.PreActive);
        HearingRetained("source missing owner", f => f.Source.GetPlayer.AIData.BotOwner = null);
        HearingRetained("listener missing group", f => f.Listener.BotsGroup = null);
        HearingRetained("source missing group", f => f.Source.BotsGroup = null);
        HearingRetained("foreign group", f => f.Listener.BotsGroup.BotGame = new LocalGame());
        HearingRetained("foreign source group", f => f.Source.BotsGroup.BotGame = new LocalGame());
        HearingRetained("nongeneric group", f => f.Listener.BotsGroup.InitialBotType = WildSpawnType.pmcUSEC);
        HearingRetained("nonmember listener", f => f.Listener.BotsGroup.Members.Clear());
        HearingRetained("uninitialized enemy list", f => f.Listener.BotsGroup.Enemies = null);
        HearingRetained("uninitialized neutral list", f => f.Listener.BotsGroup.Neutrals = null);
        HearingRetained("uninitialized memory", f => f.Listener.Memory = null);
        HearingRetained("active combat already inline", f => f.Listener.Memory.GoalEnemy = new() { Person = new ForeignPlayer() });
        HearingRetained("missing target", f => f.Listener.Memory.GoalTarget = null);
        HearingRetained("missing hearing settings", f => f.Listener.Settings.FileSettings.Hearing = null);
        HearingRetained("no native hearing delay", f => f.Listener.Settings.FileSettings.Hearing.HEAR_DELAY_WHEN_PEACE = 0);
        HearingRetained("negative hearing delay", f => f.Listener.Settings.FileSettings.Hearing.HEAR_DELAY_WHEN_PEACE = -1);
        HearingRetained("nonfinite hearing delay", f => f.Listener.Settings.FileSettings.Hearing.HEAR_DELAY_WHEN_PEACE = float.NaN);
        HearingRetained("infinite hearing delay", f => f.Listener.Settings.FileSettings.Hearing.HEAR_DELAY_WHEN_PEACE = float.PositiveInfinity);
        HearingRetained("target path zero delay", f => { f.Listener.Memory.GoalTarget.HasPlace = true; f.Listener.Settings.FileSettings.Hearing.HEAR_DELAY_WHEN_HAVE_SMT = 0; });
        HearingRetained("muted source", f => f.Source.GetPlayer.AIData.IsMute = true);
        FastHearingRetained("combat fast path", f => f.Listener.Memory.GoalEnemy = new() { Person = new ForeignPlayer() });
        FastHearingRetained("zero-delay fast path", f => f.Listener.Settings.FileSettings.Hearing.HEAR_DELAY_WHEN_PEACE = 0);
        FastHearingRetained("muted fast path", f => f.Source.GetPlayer.AIData.IsMute = true);
        HearingRetained("unknown relationship", f => f.Listener.BotsGroup.Suitable.Clear());
        HearingRetained("unknown reverse relationship", f => f.Source.BotsGroup.Suitable.Clear());
        HearingRetained("neutral", f => f.Listener.BotsGroup.Neutrals.Add(f.Source.GetPlayer, new()));
        HearingRetained("reverse neutral", f => f.Source.BotsGroup.Neutrals.Add(f.Listener.GetPlayer, new()));
        HearingRetained("hostility key", f => f.Listener.BotsGroup.Enemies.Add(f.Source.GetPlayer, new()));
        HearingRetained("hostility via native-ID wrapper", f => f.Listener.BotsGroup.Enemies.Add(f.Source, new() { Player = f.Source.GetPlayer }));
        HearingRetained("reverse hostility", f => f.Source.BotsGroup.Enemies.Add(f.Listener.GetPlayer, new()));
        HearingRetained("current enemy target", f => f.Listener.Memory.GoalEnemy = new() { Person = f.Source.GetPlayer });
        HearingRetained("current enemy wrapper", f => f.Listener.Memory.GoalEnemy = new() { Person = f.Source });
        HearingRetained("reverse enemy target", f => f.Source.Memory.GoalEnemy = new() { Person = f.Listener.GetPlayer });
        var fixture = new Fixture();
        foreach (var type in new[] { AISoundType.gun, AISoundType.silencedGun, AISoundType.other, (AISoundType)999 })
            Check(fixture.Hearing(type: type), "non-step " + type);
        Check(ZombiePerformancePatches.ShouldRunHearing(fixture.Listener.HearingSensor, null, AISoundType.step), "null source");
        Check(fixture.Hearing(new ForeignPlayer { ProfileId = fixture.Source.ProfileId, Id = fixture.Source.Id }), "foreign IPlayer");
        Check(fixture.Hearing(fixture.Source), "BotOwner IPlayer not native body");
        Check(ZombiePerformancePatches.ShouldRunHearing(new() { _botOwner = fixture.Listener }, fixture.Source.GetPlayer, AISoundType.step), "stale sensor");
        Check(ZombiePerformancePatches.ShouldRunHearing(null, fixture.Source.GetPlayer, AISoundType.step), "null sensor");
        Check(ZombiePerformancePatches.ShouldRunSocial(new() { _owner = fixture.Listener }), "stale checker");
        Check(ZombiePerformancePatches.ShouldRunSocial(null), "null checker");
        foreach (float next in new[] { Time.time, Time.time + 1, float.NaN, float.PositiveInfinity })
        {
            fixture.Listener.FriendChecker._nextTimeCheck = next;
            Check(fixture.Social(), "not-due native return " + next);
            Check(float.IsNaN(next) ? float.IsNaN(fixture.Listener.FriendChecker._nextTimeCheck) : fixture.Listener.FriendChecker._nextTimeCheck == next, "not-due clock unchanged");
        }
        fixture.Listener.FriendChecker._nextTimeCheck = 0;
        Check(!fixture.Social(), "due social search suppressed");
        Check(fixture.Listener.FriendChecker._nextTimeCheck == Time.time + 1, "social retries once per second");
        Check(fixture.Social(), "social cheap no-op until due");
        fixture = new Fixture(); fixture.Listener.BotsGroup.Suitable.Clear(); fixture.Source.BotsGroup.Suitable.Clear();
        fixture.Listener.BotsGroup.Allies.Add(fixture.Source.GetPlayer); fixture.Source.BotsGroup.Allies.Add(fixture.Listener.GetPlayer);
        Check(!fixture.Hearing(), "explicit mutual allies");
        fixture.Listener.BotsGroup.Allies.Clear(); fixture.Source.BotsGroup.Allies.Clear();
        fixture.Source.BotsGroup = fixture.Listener.BotsGroup; fixture.Listener.BotsGroup.Members.Add(fixture.Source);
        Check(!fixture.Hearing(), "same group members");
        fixture.Listener.BotsGroup.Enemies.Add(fixture.Source.GetPlayer, new());
        Check(fixture.Hearing(), "changed enemy wins over known membership on next event");

        // Exercise real prefix entry/target resolution, not just a reimplemented policy.
        ZombiePerformancePatches.EnablePatch(); ZombiePerformancePatches.EnablePatch();
        var installed = SPT.Reflection.Patching.ModulePatch.Installed;
        Check(installed.Count == 2, "idempotent two-hook registration");
        Check(installed[0].DeclaringType == typeof(BotHearingSensor) && installed[0].Name == "OnSoundPlayed", "early scheduling boundary");
        Check(installed[1].DeclaringType == typeof(BotFriendChecker) && installed[1].Name == "ManualUpdate", "only social boundary");
        fixture = new Fixture();
        var nested = typeof(ZombiePerformancePatches).GetNestedType("ArenaZombiePerformanceHearingPatch", BindingFlags.NonPublic);
        var prefix = nested.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        Check(!(bool)prefix.Invoke(null, new object[] { fixture.Listener.HearingSensor, fixture.Source.GetPlayer, AISoundType.step }), "hearing prefix forwards result");
        prefix = typeof(ZombiePerformancePatches).GetNestedType("ArenaZombiePerformanceSocialPatch", BindingFlags.NonPublic).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        Check(!(bool)prefix.Invoke(null, new object[] { fixture.Listener.FriendChecker }), "social prefix forwards result");

        fixture = new Fixture(); Check(!fixture.Hearing(), "aggregate starts");
        Check(fixture.World.LookupCalls == 2, "delayed positive control validates both current bodies");
        for (int i = 0; i < 99; i++) fixture.Hearing();
        Check(Plugin.ModLogger.Infos.Count == 1, "no per-event info spam");
        Time.realtimeSinceStartup = 160; fixture.Social();
        Check(Plugin.ModLogger.Infos.Count == 2, "minute summary");
        Check(Plugin.ModLogger.Infos[1].Contains("delayedStepCandidates=100, friendlyStepsSkipped=100, socialSearchesSkipped=1"), "aggregate counts");
        fixture = new Fixture(); fixture.Hearing(); Time.realtimeSinceStartup = 160; fixture.Social();
        Check(Plugin.ModLogger.Infos[1].Contains("delayedStepCandidates=1, friendlyStepsSkipped=1, socialSearchesSkipped=1"), "new world counters reset");
        fixture = new Fixture(); fixture.Hearing();
        for (int i = 0; i < 10000; i++) fixture.Hearing(); // Warm desktop runtime caches/JIT before the allocation check.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) fixture.Hearing();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, "steady-state managed allocation fixture: " + bytes);
        Console.WriteLine($"ZOMBIE_PERFORMANCE_CHECKS_PASSED {_assertions} assertions; 10000 steady-state hearing calls allocated {bytes} bytes in desktop boundary fixture (not Unity frame-time evidence).");
    }
}
