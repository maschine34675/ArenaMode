using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Modes;
using ArenaMode.Respawn;
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SAIN.Components;
using SAIN.Classes.Bot.Search;
using SAIN.SAINComponent.Classes.EnemyClasses;
using System.Reflection;
using UnityEngine;

static class Program
{
    static int checks;
    static void Check(bool value, string name)
    { checks++; if (!value) throw new Exception("ASSERT: " + name); }
    sealed record Case(GameWorld World, AbstractGame Game, BotOwner Owner, Player Target,
        BotComponent Bot, Enemy Enemy, SearchDecider Decider, TeamDeathmatchMode Mode);
    static Case Fresh(string modeId = "teamdeathmatch", EPlayerSide side = EPlayerSide.Usec)
    {
        TeamDeathmatchRelations.Reset(); Time.time = 1;
        var world = new GameWorld(); var game = new LocalGame();
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        ArenaSession.Game = game; ArenaSession.World = world;
        ArenaSession.IsActive = true; ArenaSession.ActiveMode = modeId;
        ArenaRespawnDriver.StandDown = false; PitFireTeamCompat.HasFrozenTeamRoster = false;
        var player = new Player { ProfileId = "private-bot", IsAI = true };
        player.Profile.Info.Side = side; player.Profile.Info.Settings.Role = side == EPlayerSide.Usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
        var target = new Player { ProfileId = "private-target" };
        target.Profile.Info.Side = side == EPlayerSide.Usec ? EPlayerSide.Bear : EPlayerSide.Usec;
        world.MainPlayer = target; world.Players[player.ProfileId] = player; world.Players[target.ProfileId] = target;
        var owner = new BotOwner { GetPlayer = player }; player.AIData.BotOwner = owner;
        owner.Memory = new BotMemory { _owner = owner };
        owner.BotsGroup = new BotsGroup { BotGame = game, _initialBot = owner, Side = side, InitialBotType = player.Profile.Info.Settings.Role };
        owner.BotsGroup.Members.Add(owner);
        var bot = new BotComponent { BotOwner = owner, Player = player, EnemyController = new EnemyController { Owner = owner } };
        owner.Component = bot;
        var decider = new SearchDecider { Bot = bot }; bot.Search = new SearchClass { Decider = decider };
        TeamDeathmatchRelations.Register(player); TeamDeathmatchRelations.Register(target);
        owner.BotsGroup.AddEnemy(target, EBotEnemyCause.bossKillArena);
        var enemy = bot.EnemyController.CheckAddEnemy(target); enemy.Bot = bot;
        var mode = new TeamDeathmatchMode(); mode.Adopted.UnionWith(new[] { player, target });
        ArenaMatchService.Current = new Match { Mode = mode };
        Plugin.ModLogger.Lines.Clear();
        return new(world, game, owner, target, bot, enemy, decider, mode);
    }
    static float Call(Case c, float input = 60, string method = "shallBeginSearch")
    {
        var hook = Harmony.Hooks.Single(h => h.Id.EndsWith(".TdmSearchTempo") && h.Original.Name == method);
        Check(hook.Prefix.ReturnType == typeof(void), "prefix cannot skip native predicate");
        object[] args = { c.Decider, c.Enemy, input, hook.Original };
        hook.Prefix.Invoke(null, args);
        object[] native = { c.Enemy, args[2], null };
        Check(Equals(hook.Original.Invoke(c.Decider, native), false) && native[2]?.ToString() == "Sentinel", "native result and reason unchanged");
        return (float)args[2];
    }
    static void Main()
    {
        TdmSearchTempo.Shutdown(); Harmony.ThrowOnMethod = "ShallContinueSearch";
        TdmSearchTempo.Enable();
        Check(!Harmony.Hooks.Any(h => h.Id.EndsWith(".TdmSearchTempo")), "partial patch transaction rolled back");
        Harmony.ThrowOnMethod = null; TdmSearchTempo.Enable();
        Check(Harmony.Hooks.Count(h => h.Id.EndsWith(".TdmSearchTempo")) == 2, "both actual predicates hooked");
        var c = Fresh();
        Check(Call(c) == 3 && Call(c, 99, "ShallContinueSearch") == 3, "both search arguments capped");
        Check(c.Bot.Info.TimeBeforeSearch == 60 && !c.Enemy.Heard && !c.Enemy.Seen
            && c.Owner.Memory.GoalEnemy == null, "stored timers contacts and goal untouched");
        foreach (string predicate in new[] { "shallBeginSearch", "ShallContinueSearch" })
        {
            foreach (float value in new[] { float.BitIncrement(3f), 4, 5, 6, float.MaxValue })
                Check(Call(c, value, predicate) == 3, "both predicates cap arguments above three seconds");
            foreach (float value in new[] { 0, -1, 2, float.BitDecrement(3f), 3, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Check(Call(c, value, predicate).Equals(value), "short or invalid delay preserved");
        }
        Check(Plugin.ModLogger.Lines.Count(l => l.StartsWith("[dev] Arena TDM search tempo:")) == 1, "tempo diagnostics throttled");
        Time.time += 30; Call(c);
        Check(Plugin.ModLogger.Lines.Count(l => l.StartsWith("[dev] Arena TDM search tempo:")) == 2, "tempo diagnostics resume");
        Check(!Plugin.ModLogger.Lines.Any(l => l.Contains("private-")), "no profile ids logged");
        foreach (string modeId in new[] { "teamdeathmatch", "domination" })
            foreach (EPlayerSide side in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
            {
                c = Fresh(modeId, side);
                Check(Call(c) == 3 && Call(c, 4, "ShallContinueSearch") == 3,
                    "both factions and independent team modes use three-second cap");
            }
        var rejected = new Dictionary<string, Action<Case>>
        {
            ["normal raid"] = c => ArenaSession.IsActive = false,
            ["other mode"] = c => ArenaSession.ActiveMode = "tactical",
            ["zombie mode"] = c => ArenaSession.ActiveMode = "zombie",
            ["not live"] = c => c.Mode.IsSearchActive = false,
            ["foreign match"] = c => ArenaMatchService.Current = new Match(),
            ["stand down"] = c => ArenaRespawnDriver.StandDown = true,
            ["PFT roster"] = c => PitFireTeamCompat.HasFrozenTeamRoster = true,
            ["foreign world"] = c => Singleton<GameWorld>.Instance = new(),
            ["native stop"] = c => c.Game.Status = GameStatus.Stopping,
            ["source not adopted"] = c => c.Mode.Adopted.Remove(c.Owner.GetPlayer),
            ["target not adopted"] = c => c.Mode.Adopted.Remove(c.Target),
            ["friendly target"] = c => c.Target.Profile.Info.Side = EPlayerSide.Usec,
            ["dead source"] = c => c.Owner.HealthController.IsAlive = false,
            ["dead target"] = c => c.Target.HealthController.IsAlive = false,
            ["inactive source"] = c => c.Owner.BotState = EBotState.NonActive,
            ["replaced body"] = c => c.World.Players[c.Owner.ProfileId] = new(),
            ["replaced target"] = c => c.World.Players[c.Target.ProfileId] = new(),
            ["foreign component"] = c => c.Owner.Component = new BotComponent(),
            ["foreign decider"] = c => c.Bot.Search.Decider = new(),
            ["foreign enemy bot"] = c => c.Enemy.Bot = new(),
            ["foreign native pair"] = c => c.Enemy.EnemyInfo = new(),
            ["foreign group pair"] = c => c.Owner.BotsGroup.Enemies[c.Target] = new(),
            ["missing source search"] = c => c.Bot.Search = null
        };
        foreach (var scenario in rejected)
        {
            c = Fresh(); scenario.Value(c);
            Check(Call(c) == 60, "reject " + scenario.Key);
        }
        // Dying local PMC must not disable bot-vs-bot searches. The owned bot
        // pair remains valid while the local body is absent during replacement.
        c = Fresh(); c.World.MainPlayer = null;
        c.Target.IsAI = true; c.Target.Profile.Info.Settings.Role = WildSpawnType.pmcBEAR;
        c.Target.AIData.BotOwner = new BotOwner { GetPlayer = c.Target };
        Check(Call(c) == 3, "bot search continues without local player");
        TdmSearchTempo.Shutdown();
        Check(!Harmony.Hooks.Any(h => h.Id.EndsWith(".TdmSearchTempo")), "shutdown removes own hooks");
        Console.WriteLine($"TDM_SEARCH_TEMPO_CONTRACTS_PASSED: {checks} assertions; linked production hooks and native pair guards, no AI simulation.");
    }
}
