using ArenaMode.BigBrainCompat;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using pitTeam.Modules;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); _checks++; }

    private sealed class Raid
    {
        internal readonly GameWorld World = new();
        internal readonly LocalGame Game = new();
        internal readonly TacticalMode Mode = new();
        internal BotOwner Ally => Mode._boundTeamFollowers[0];
        internal BotOwner Target => Mode._boundTeamEnemies[0];
        internal readonly EnemyInfo Enemy;
        internal Raid()
        {
            FollowerGoalEnemyTracker.Token = null;
            FollowerGoalEnemyTracker.Entered = FollowerGoalEnemyTracker.Exited = 0;
            Check(PitFireTeamGoalContext.Configure(typeof(FollowerGoalEnemyTracker).Assembly, out _), "fixture contract accepted");
            ArenaSession.IsActive = PitFireTeamCompat.TeamArenaActive = true;
            ArenaSession.Game = Singleton<AbstractGame>.Instance = Game;
            ArenaSession.World = Singleton<GameWorld>.Instance = World;
            Mode._teamGame = Game;
            Mode._boundLocalPlayer = World.MainPlayer = new Player { ProfileId = "local" };
            Mode._boundLocalProfileId = "local";
            World.MainPlayer.HealthController.IsAlive = false;
            World.Ever.Add("local", World.MainPlayer);
            Mode._boundTeamFollowers = Enumerable.Range(0, 4).Select(i => Bot("ally" + i, true)).ToArray();
            Mode._boundTeamEnemies = Enumerable.Range(0, 5).Select(i => Bot("enemy" + i, false)).ToArray();
            Mode._boundTeamFollowerProfileIds = Mode._boundTeamFollowers.Select(b => b.ProfileId).ToArray();
            Mode._boundTeamEnemyProfileIds = Mode._boundTeamEnemies.Select(b => b.ProfileId).ToArray();
            Mode._match = ArenaMatchService.Current = new ArenaMatch { Mode = Mode };
            Enemy = Relation(Ally, Target);
            TeamArenaPursuitLeaseBridge.Current = new(Ally.ProfileId, Target.ProfileId, 3, 41);
        }
        internal BotOwner Bot(string id, bool follower)
        {
            var player = new Player { ProfileId = id };
            var bot = new BotOwner { ProfileId = id, GetPlayer = player, Follower = follower };
            bot.Memory = new BotMemory { Owner = bot };
            player.AIData.BotOwner = bot;
            World.Alive[id] = World.Ever[id] = player;
            World.RegisteredPlayers.Add(player);
            return bot;
        }
        internal EnemyInfo Relation(BotOwner source, BotOwner target, bool ownerKey = false)
        {
            IPlayer key = ownerKey ? target : target.GetPlayer;
            var group = new BotGroupEnemyInfo { Player = target.GetPlayer, _botGroup = source.BotsGroup };
            var enemy = new EnemyInfo { Person = key, GroupInfo = group, Owner = source, GroupOwner = source.BotsGroup };
            source.BotsGroup.Enemies.Add(key, group);
            source.EnemiesController.EnemyInfos.Add(key, enemy);
            return enemy;
        }
        internal bool Set(out string problem) => TeamArenaGoalHandoff.TrySetGoal(Ally, Enemy, 3, out problem);
    }

    private static void SuccessAndVetoes()
    {
        var raid = new Raid();
        raid.Ally.Memory.GoalEnemy = raid.Enemy;
        Check(raid.Ally.Memory.GoalEnemy == null, "positive control: unscoped memory-only target is refused");
        Check(raid.Set(out _) && ReferenceEquals(raid.Ally.Memory.GoalEnemy, raid.Enemy),
            "scoped handoff admits the live memory-only target");
        Check(raid.Ally.Memory.Reasons.Last() == PitFireTeamGoalContext.Reason
            && FollowerGoalEnemyTracker.CurrentReason == "unscopedSetter"
            && FollowerGoalEnemyTracker.Entered == 1 && FollowerGoalEnemyTracker.Exited == 1,
            "scope is confined to one native setter and restored after success");
        int calls = raid.Ally.Memory.SetCalls;
        Check(raid.Set(out _) && raid.Ally.Memory.SetCalls == calls && FollowerGoalEnemyTracker.Entered == 1,
            "an already exact goal causes no new assignment or context allocation");

        raid = new Raid();
        raid.Ally.Memory.MissionVeto = true;
        Check(!raid.Set(out var reason) && reason == "native-goal-setter-refused"
            && raid.Ally.Memory.GoalEnemy == null && raid.Ally.Memory.SetCalls == 1,
            "PFT mission veto is retained without retry or field replacement");
        Check(FollowerGoalEnemyTracker.Exited == 1 && FollowerGoalEnemyTracker.CurrentReason == "unscopedSetter",
            "scope is restored after native refusal");
        raid = new Raid();
        raid.Ally.Memory.ThrowOnSet = true;
        Check(!raid.Set(out reason) && reason == "goal-handoff-exception:InvalidOperationException"
            && raid.Ally.Memory.SetCalls == 1 && FollowerGoalEnemyTracker.Exited == 1
            && FollowerGoalEnemyTracker.CurrentReason == "unscopedSetter", "throwing native setter restores the scope without retry");
        raid = new Raid();
        using (FollowerGoalEnemyTracker.Begin("test", "existing-order"))
        {
            Check(!raid.Set(out reason) && reason == "pft-goal-context-already-owned"
                && raid.Ally.Memory.SetCalls == 0 && FollowerGoalEnemyTracker.CurrentReason == "existing-order",
                "an existing PFT command context is never relabelled or overwritten");
        }
        Check(FollowerGoalEnemyTracker.CurrentReason == "unscopedSetter", "outer owner still disposes its own context");

        raid = new Raid();
        var reverse = raid.Relation(raid.Target, raid.Ally);
        Check(TeamArenaGoalHandoff.TrySetGoal(raid.Target, reverse, 3, out _)
            && raid.Target.Memory.SetCalls == 1 && FollowerGoalEnemyTracker.Entered == 0,
            "opponents use native assignment without a PFT follower context");
        raid = new Raid();
        var ownerPair = raid.Relation(raid.Ally, raid.Target, true);
        Check(TeamArenaGoalHandoff.TrySetGoal(raid.Ally, ownerPair, 3, out _), "coherent BotOwner-keyed goals remain supported");
    }

    private static void Isolation()
    {
        var cases = new (string Name, Action<Raid> Change)[]
        {
            ("ordinary raid", r => ArenaSession.IsActive = false),
            ("PFT unavailable", r => PitFireTeamCompat.TeamArenaActive = false),
            ("solo Tactical", r => r.Mode._teamArena = false),
            ("other mode", r => ArenaMatchService.Current.Mode = new object()),
            ("other match", r => ArenaMatchService.Current = new ArenaMatch { Mode = r.Mode }),
            ("staging", r => r.Mode._phase = TacticalMode.Phase.Staging),
            ("ended match", r => r.Mode.IsComplete = true),
            ("old round", r => r.Mode._teamRoundEpoch++),
            ("stopping game", r => r.Game.Status = GameStatus.Stopping),
            ("other game", r => Singleton<AbstractGame>.Instance = new LocalGame()),
            ("other world", r => Singleton<GameWorld>.Instance = new GameWorld()),
            ("living human", r => r.World.MainPlayer.HealthController.IsAlive = true),
            ("replaced human", r => r.World.MainPlayer = new Player { ProfileId = "local" }),
            ("replaced persistent human", r => r.World.Ever["local"] = new Player { ProfileId = "local" }),
            ("duplicate registered human", r => r.World.RegisteredPlayers.Add(new Player { ProfileId = "local" })),
            ("wrong roster size", r => r.Mode._boundTeamFollowers = r.Mode._boundTeamFollowers.Take(3).ToArray()),
            ("old target owner", r => r.Mode._boundTeamEnemies[0] = r.Bot("enemy0", false)),
            ("changed frozen identity", r => r.Mode._boundTeamEnemyProfileIds[0] = "different"),
            ("dead source", r => r.Ally.HealthController.IsAlive = false),
            ("inactive source", r => r.Ally.BotState = EBotState.NonActive),
            ("dead target", r => r.Target.HealthController.IsAlive = false),
            ("missing alive target", r => r.World.Alive.Remove(r.Target.ProfileId)),
            ("replaced alive target", r => r.World.Alive[r.Target.ProfileId] = new Player { ProfileId = r.Target.ProfileId }),
            ("missing persistent target", r => r.World.Ever.Remove(r.Target.ProfileId)),
            ("wrong target backreference", r => r.Target.GetPlayer.AIData.BotOwner = r.Ally),
            ("mismatched source id", r => r.Ally.GetPlayer.ProfileId = "different"),
            ("foreign enemy owner", r => r.Enemy.Owner = r.Target),
            ("foreign enemy group", r => r.Enemy.GroupOwner = new BotsGroup()),
            ("foreign group info", r => r.Enemy.GroupInfo._botGroup = new BotsGroup()),
            ("missing registered enemy", r => r.Ally.EnemiesController.EnemyInfos.Clear()),
            ("missing registered group", r => r.Ally.BotsGroup.Enemies.Clear()),
            ("replaced enemy entry", r => r.Ally.EnemiesController.EnemyInfos[r.Enemy.Person] = new EnemyInfo()),
            ("different physical enemy key", r => r.Enemy.Person = new Player { ProfileId = r.Target.ProfileId }),
        };
        foreach (var test in cases)
        {
            var raid = new Raid();
            test.Change(raid);
            Check(!raid.Set(out _) && raid.Ally.Memory.SetCalls == 0 && FollowerGoalEnemyTracker.Entered == 0,
                "isolation: " + test.Name);
        }
        var valid = new Raid();
        valid.Mode._boundTeamFollowers[1].HealthController.IsAlive = false;
        valid.Mode._boundTeamEnemies[1].HealthController.IsAlive = false;
        Check(valid.Set(out _), "other combat deaths do not invalidate the frozen roster structure");
        var friendly = new Raid();
        var sameSide = friendly.Relation(friendly.Ally, friendly.Mode._boundTeamFollowers[1]);
        Check(!TeamArenaGoalHandoff.TrySetGoal(friendly.Ally, sameSide, 3, out _)
            && FollowerGoalEnemyTracker.Entered == 0, "friendly targets never acquire goal authority");
    }

    private static void RegistryAndLease()
    {
        foreach (bool removePrevious in new[] { false, true })
        {
            var raid = new Raid();
            var old = raid.Mode._boundTeamEnemies[1];
            raid.Ally.Memory.Value = raid.Relation(raid.Ally, old);
            old.HealthController.IsAlive = false;
            raid.World.Alive.Remove(old.ProfileId);
            if (removePrevious) raid.World.Ever.Remove(old.ProfileId);
            Check(raid.Set(out var reason) == !removePrevious, "dead previous goal requires persistent registration only");
            if (removePrevious) Check(reason == "previous-goal-body-not-in-world" && raid.Ally.Memory.SetCalls == 0,
                "missing previous subscription is rejected before the native setter");
        }
        var reentrant = new Raid();
        reentrant.Ally.Memory.OnSet = () => reentrant.Game.Status = GameStatus.Stopping;
        Check(!reentrant.Set(out var ended) && ended == "pursuit-context-ended-during-setter"
            && FollowerGoalEnemyTracker.CurrentReason == "unscopedSetter", "reentrant native stop cannot acknowledge a handoff");
        var sourceDeath = new Raid();
        sourceDeath.Ally.Memory.OnSet = () => sourceDeath.Ally.HealthController.IsAlive = false;
        Check(!sourceDeath.Set(out _), "source death during native callback cannot acknowledge a handoff");
        var sourceReplacement = new Raid();
        sourceReplacement.Ally.Memory.OnSet = () => sourceReplacement.World.Alive[sourceReplacement.Ally.ProfileId] = new Player();
        Check(!sourceReplacement.Set(out _), "source replacement during native callback cannot acknowledge a handoff");
        var lostRelation = new Raid();
        lostRelation.Ally.Memory.OnSet = () => lostRelation.Ally.EnemiesController.EnemyInfos.Clear();
        Check(!lostRelation.Set(out var relationReason) && relationReason == "pursuit-relation-ended-during-setter",
            "removed relation during native callback cannot acknowledge a handoff");
        var replacedLease = new Raid();
        var beforeLease = TeamArenaPursuitLeaseBridge.Current;
        replacedLease.Ally.Memory.OnSet = () => TeamArenaPursuitLeaseBridge.Current = beforeLease with { Generation = 42 };
        Check(!ArenaPursuitRuntime.TryBindLeaseTarget(replacedLease.Ally, beforeLease, out _),
            "lease replacement during native callback cannot acknowledge a handoff");
        var raid2 = new Raid();
        Check(ArenaPursuitRuntime.TryBindLeaseTarget(raid2.Ally, TeamArenaPursuitLeaseBridge.Current, out var enemy)
            && ReferenceEquals(enemy, raid2.Enemy) && FollowerGoalEnemyTracker.Entered == 1,
            "actual BigBrain consumer uses the scoped producer handoff");
        foreach (var stale in new[] { new TeamArenaPursuitLease("ally0", "enemy0", 2, 41),
            new TeamArenaPursuitLease("ally0", "enemy0", 3, 40), new TeamArenaPursuitLease("ally0", "enemy1", 3, 41),
            new TeamArenaPursuitLease("other-source", "enemy0", 3, 41) })
        {
            var raid = new Raid();
            Check(!ArenaPursuitRuntime.TryBindLeaseTarget(raid.Ally, stale, out _) && raid.Ally.Memory.SetCalls == 0,
                "consumer independently rejects stale epoch generation or target");
        }
        var absent = new Raid();
        Check(!PitFireTeamGoalContext.Configure(typeof(string).Assembly, out _), "foreign assembly cannot configure PFT context");
        Check(!absent.Set(out var unavailable) && unavailable == "pft-goal-context-unavailable"
            && absent.Ally.Memory.SetCalls == 0, "missing contract fails closed without unscoped fallback");
    }

    private static int Main()
    {
        try
        {
            SuccessAndVetoes();
            Isolation();
            RegistryAndLease();
            Console.WriteLine($"ARENA_PFT_GOAL_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ARENA_PFT_GOAL_CHECKS_FAILED: " + ex.Message);
            return 1;
        }
    }
}
