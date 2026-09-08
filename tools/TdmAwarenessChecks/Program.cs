using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private sealed record Raid(GameWorld World, AbstractGame Game, BotOwner Source, Player Target, BotComponent Sain)
    {
        internal Player[] Actors => new[] { Source.GetPlayer, Target };
        internal Enemy Enemy()
        {
            Source.BotsGroup.AddEnemy(Target, EBotEnemyCause.bossKillArena);
            return Sain.EnemyController.CheckAddEnemy(Target);
        }
        internal void Tick(float time) { Time.time = time; TeamDeathmatchRelations.Reconcile(Actors); }
    }
    private static void Check(bool condition, string name)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("ASSERT: " + name + ";");
    }
    private static Raid Fresh(EPlayerSide side = EPlayerSide.Usec)
    {
        Time.time = 0;
        TeamDeathmatchRelations.Reset();
        var world = new GameWorld();
        var game = new AbstractGame();
        Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = game;
        ArenaSession.World = world; ArenaSession.Game = game;
        ArenaSession.IsActive = true; ArenaSession.ActiveMode = "teamdeathmatch";
        var target = new Player { ProfileId = "private-local-profile", Position = new Vector3(200), Profile = new Profile() };
        target.Profile.Info.Side = side == EPlayerSide.Usec ? EPlayerSide.Bear : EPlayerSide.Usec;
        world.MainPlayer = target; world.Players[target.ProfileId] = target;
        var source = Bot(world, game, "private-bot-profile", side, 0);
        TeamDeathmatchRelations.Register(source.GetPlayer);
        TeamDeathmatchRelations.Register(target);
        Plugin.ModLogger.Lines.Clear();
        return new Raid(world, game, source, target, (BotComponent)source.Component);
    }
    private static BotOwner Bot(GameWorld world, AbstractGame game, string id, EPlayerSide side, float x)
    {
        var player = new Player { IsAI = true, ProfileId = id, Position = new Vector3(x) };
        player.Profile.Info.Side = side;
        player.Profile.Info.Settings.Role = side == EPlayerSide.Usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
        var owner = new BotOwner { GetPlayer = player };
        player.AIData.BotOwner = owner;
        owner.Memory = new BotMemory { _owner = owner };
        owner.BotsGroup = new BotsGroup { BotGame = game, _initialBot = owner, Side = side, InitialBotType = player.Profile.Info.Settings.Role };
        owner.BotsGroup.Members.Add(owner);
        owner.Component = new BotComponent { BotOwner = owner, Player = player, EnemyController = new EnemyController { Owner = owner } };
        world.Players[id] = player;
        return owner;
    }
    private static void Refresh(Raid raid, Enemy enemy)
        => TdmSearchAwareness.Refresh(raid.Source, raid.Target, raid.Sain.EnemyController, enemy, enemy.EnemyInfo);
    private static string[] Snapshots() => Plugin.ModLogger.Lines.Where(line => line.StartsWith("Arena TDM awareness:")).ToArray();

    private static void AdmissionAndAge()
    {
        foreach (var side in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
        {
            var raid = Fresh(side);
            var enemy = raid.Enemy();
            raid.Tick(0);
            Check(enemy.HearingState.Calls == 1 && enemy.Heard, "cold opponent receives search cue");
            var report = enemy.HearingState.LastReport;
            Check(report.soundType.ToString() == "Generic" && report.placeType == EEnemyPlaceType.Hearing, "cue uses generic hearing report");
            Check(!report.isDanger && !report.shallReportToSquad, "cue stays non-danger and private");
            Check((report.position - raid.Target.Position).sqrMagnitude == 0, "cue uses exact current opponent position");
            Check(!enemy.Seen && !enemy.IsVisible && !enemy.CanShoot, "cue does not fabricate visual contact");
            Check(raid.Source.Memory.GoalEnemy == null, "cue does not force native combat goal");
            for (int second = 1; second <= 120; second++)
            {
                raid.Target.Position = new Vector3(200 + second);
                raid.Tick(second);
                Check(enemy.HearingState.Calls == 1 && enemy.TimeSinceHeard == second, "existing heard timer ages without refresh");
                Check(enemy.PositionUpdates == second + 1 && enemy.KnownPosition.x == raid.Target.Position.x, "existing position refresh retained");
            }
            // Explicit age prerequisite, not a simulator or claim of SAIN movement.
            Check(enemy.Heard && enemy.TimeSinceHeard >= raid.Sain.Info.TimeBeforeSearch, "search-age prerequisite becomes eligible");
            Check(Snapshots().Length == 5, "diagnostics bounded to thirty seconds");
            Check(Snapshots()[0].Contains("cue=admitted") && Snapshots().Last().Contains("cue=preserved"), "diagnostics confirm admission and preservation");
        }
    }
    private static void ContactAndForget()
    {
        foreach (string existing in new[] { "seen", "heard", "visible" })
        {
            var raid = Fresh(); var enemy = raid.Enemy();
            enemy.Seen = existing == "seen";
            enemy.HearingState.Heard = existing == "heard";
            enemy.HearingState.At = -10;
            enemy.IsVisible = existing == "visible";
            raid.Tick(0);
            Check(enemy.HearingState.Calls == 0, "existing " + existing + " is never reseeded");
        }
        var forget = Fresh(); var forgotten = forget.Enemy();
        forget.Tick(0); forget.Tick(100);
        forgotten.HearingState.Heard = false; forgotten.EnemyKnown = false;
        // Modeled native forgetting on the same wrapper, not an Arena flag write.
        forget.Tick(101);
        Check(forgotten.HearingState.Calls == 2 && forgotten.HearingState.At == 101, "native forgetting permits same-wrapper reacquisition");
        forget.Tick(102);
        Check(forgotten.HearingState.Calls == 2 && forgotten.TimeSinceHeard == 1, "reacquired timer is not renewed");
        foreach (string callback in new[] { "seen", "heard", "visible" })
        {
            var raid = Fresh(); var enemy = raid.Enemy();
            enemy.OnGetHearing = () =>
            {
                enemy.Seen = callback == "seen";
                enemy.HearingState.Heard = callback == "heard";
                enemy.IsVisible = callback == "visible";
            };
            raid.Tick(0);
            Check(enemy.HearingState.Calls == 0, "contact callback " + callback + " is rechecked");
        }
    }
    private static void Ownership()
    {
        var cases = new Dictionary<string, Action<Raid, Enemy>>
        {
            ["ordinary raid"] = (r, e) => ArenaSession.IsActive = false,
            ["other Arena mode"] = (r, e) => ArenaSession.ActiveMode = "tactical",
            ["foreign world"] = (r, e) => Singleton<GameWorld>.Instance = new(),
            ["foreign game"] = (r, e) => Singleton<AbstractGame>.Instance = new(),
            ["native stopping"] = (r, e) => r.Game.Status = GameStatus.Stopping,
            ["native stopped"] = (r, e) => r.Game.Status = GameStatus.Stopped,
            ["inactive source"] = (r, e) => r.Source.BotState = EBotState.NonActive,
            ["dead source"] = (r, e) => r.Source.HealthController.IsAlive = false,
            ["dead target"] = (r, e) => r.Target.HealthController.IsAlive = false,
            ["replaced source"] = (r, e) => r.World.Players[r.Source.ProfileId] = new Player { ProfileId = r.Source.ProfileId },
            ["replaced target"] = (r, e) => r.World.Players[r.Target.ProfileId] = new Player { ProfileId = r.Target.ProfileId },
            ["allied target"] = (r, e) => r.Target.Profile.Info.Side = r.Source.GetPlayer.Profile.Info.Side,
            ["foreign group"] = (r, e) => r.Source.BotsGroup.BotGame = new(),
            ["foreign component owner"] = (r, e) => r.Sain.BotOwner = new(),
            ["foreign component player"] = (r, e) => r.Sain.Player = new(),
            ["foreign component controller"] = (r, e) => r.Source.Component = new BotComponent { Player = r.Source.GetPlayer, BotOwner = r.Source, EnemyController = new() },
            ["foreign SAIN player"] = (r, e) => e.EnemyPlayer = new(),
            ["foreign SAIN native pair"] = (r, e) => e.EnemyInfo = new(),
            ["replaced native dictionary pair"] = (r, e) => r.Source.EnemiesController.EnemyInfos[r.Target] = new(),
            ["foreign native owner"] = (r, e) => e.EnemyInfo.Owner = new(),
            ["foreign native group"] = (r, e) => e.EnemyInfo.GroupOwner = new(),
            ["foreign native person"] = (r, e) => e.EnemyInfo.Person = new Player(),
            ["foreign group pair"] = (r, e) => r.Source.BotsGroup.Enemies[r.Target] = new(),
            ["foreign group target"] = (r, e) => e.EnemyInfo.GroupInfo.Player = new(),
            ["foreign group owner"] = (r, e) => e.EnemyInfo.GroupInfo._botGroup = new()
        };
        foreach (var test in cases)
        {
            var raid = Fresh(); var enemy = raid.Enemy(); test.Value(raid, enemy);
            Refresh(raid, enemy);
            Check(enemy.HearingState.Calls == 0, "ownership rejects " + test.Key);
        }
        var mismatched = Fresh(); var mismatchedEnemy = mismatched.Enemy(); var correctNative = mismatchedEnemy.EnemyInfo;
        mismatchedEnemy.EnemyInfo = new EnemyInfo();
        TdmSearchAwareness.Refresh(mismatched.Source, mismatched.Target, mismatched.Sain.EnemyController, mismatchedEnemy, correctNative);
        Check(mismatchedEnemy.HearingState.Calls == 0, "SAIN wrapper must reference the exact independently supplied native pair");
        var nearest = Fresh();
        var ally = Bot(nearest.World, nearest.Game, "private-ally", EPlayerSide.Usec, 1);
        var far = Bot(nearest.World, nearest.Game, "private-far", EPlayerSide.Bear, 500);
        TeamDeathmatchRelations.Reconcile(new[] { nearest.Source.GetPlayer, ally.GetPlayer, far.GetPlayer, nearest.Target });
        Check(nearest.Sain.EnemyController.Enemies[nearest.Target.ProfileId].Heard, "nearest real opponent selected without distance cap");
        Check(!nearest.Sain.EnemyController.Enemies.ContainsKey(ally.ProfileId), "near ally never admitted to SAIN");
        Check(!nearest.Sain.EnemyController.Enemies.ContainsKey(far.ProfileId), "farther opponent not chosen by source");
        var replaced = Fresh(); var old = replaced.Enemy(); replaced.Tick(0);
        var next = new Player { ProfileId = replaced.Target.ProfileId, Position = new Vector3(250), Profile = replaced.Target.Profile };
        replaced.World.Players[next.ProfileId] = next; replaced.World.MainPlayer = next;
        Time.time = 10;
        TeamDeathmatchRelations.Reconcile(new[] { replaced.Source.GetPlayer, next });
        var wrapper = replaced.Sain.EnemyController.Enemies[next.ProfileId];
        Check(!ReferenceEquals(old, wrapper) && ReferenceEquals(wrapper.EnemyPlayer, next) && wrapper.HearingState.Calls == 1,
            "replacement body receives a fresh exact wrapper cue");
        Check(old.HearingState.Calls == 1, "replacement never renews old body cue");
    }
    private static void PolicyAndCallbacks()
    {
        var ignored = Fresh(); var silent = ignored.Enemy(); silent.HearingState.Ignore = true;
        for (int i = 0; i < 121; i++) ignored.Tick(i);
        Check(silent.HearingState.Calls == 121 && !silent.Heard, "SAIN ignore policy is honored without bypass");
        Check(Snapshots().Length == 5 && Snapshots().All(line => line.Contains("cue=not-admitted")), "ignored cues are not reported as admitted");
        silent.HearingState.Ignore = false; ignored.Tick(121); ignored.Tick(151);
        Check(silent.HearingState.Calls == 122 && silent.TimeSinceHeard == 30, "cue can be admitted after ignore policy clears");
        foreach (string result in new[] { "null", "no-readback" })
        {
            var raid = Fresh(); var enemy = raid.Enemy();
            enemy.HearingState.ReturnNull = result == "null";
            enemy.HearingState.SuppressReadback = result == "no-readback";
            raid.Tick(0);
            Check(Snapshots().Single().Contains("cue=not-admitted"), "unconfirmed " + result + " result is not admission");
        }
        foreach (string point in new[] { "add", "position", "hearing-get", "hearing-set" })
        {
            var raid = Fresh(); var enemy = raid.Enemy();
            Action stop = () => raid.Game.Status = GameStatus.Stopping;
            if (point == "add") raid.Sain.EnemyController.OnAdd = stop;
            if (point == "position") enemy.OnPositionUpdate = stop;
            if (point == "hearing-get") enemy.OnGetHearing = stop;
            if (point == "hearing-set") enemy.HearingState.OnSet = stop;
            raid.Tick(0);
            Check(enemy.HearingState.Calls == (point == "hearing-set" ? 1 : 0), "native stop at " + point + " blocks later hearing writes");
            Check(!Snapshots().Any(line => line.Contains("cue=admitted")), "native stop at " + point + " blocks success readback");
        }
        var changed = Fresh(); var prior = changed.Enemy();
        prior.HearingState.OnSet = () => changed.World.Players[changed.Target.ProfileId] = new Player();
        changed.Tick(0);
        Check(!Snapshots().Any(line => line.Contains("cue=admitted")), "body replacement callback blocks success readback");
        var thrower = Fresh(); var failed = thrower.Enemy(); failed.HearingState.Throw = true;
        for (int i = 0; i < 100; i++) thrower.Tick(i);
        Check(Plugin.ModLogger.Lines.Count(line => line.Contains("hearing/diagnostics unavailable")) == 1, "optional hearing error bounded once per raid");
        Check(failed.PositionUpdates == 100 && thrower.Source.BotsGroup.PositionReports == 100, "optional hearing errors preserve old awareness");
    }
    private sealed class UnsupportedEnemy { }
    private static void FallbackAndDiagnostics()
    {
        var absent = Fresh(); absent.Source.Component = null; absent.Tick(0);
        Check(absent.Source.BotsGroup.PositionReports == 1 && !absent.Source.Memory.IsPeace, "absent SAIN retains native fallback");
        Check(Snapshots().Single().Contains("cue=native-only"), "absent SAIN diagnostic is explicit");
        var drift = Fresh(); var enemy = drift.Enemy();
        TdmSearchAwareness.Configure(typeof(UnsupportedEnemy), typeof(BotComponent));
        drift.Tick(0);
        Check(enemy.HearingState.Calls == 0 && enemy.PositionUpdates == 1 && drift.Source.BotsGroup.PositionReports == 1, "unsupported hearing shape retains original awareness");
        Check(Snapshots().Single().Contains("cue=hearing-unavailable"), "unsupported hearing shape is observable");
        TdmSearchAwareness.Configure(typeof(Enemy), typeof(BotComponent));

        var diagnostic = Fresh(); var observed = diagnostic.Enemy();
        diagnostic.Source.Memory.GoalEnemy = observed.EnemyInfo;
        diagnostic.Sain.Decision.CurrentCombatDecision = "Search";
        diagnostic.Sain.Decision.EnemyDecisions.DebugSearchReasons = new SearchReasons
        { NotSearchReason = "DontWantTo", WantSearchReasons = new WantReasons { NotWantToSearchReason = "TimeBeforeSearch", WantToSearchReason = "None", CantStartReason = "NoPath" } };
        diagnostic.Tick(0);
        diagnostic.Source.GetPlayer.Position = new Vector3(10);
        diagnostic.Source.Mover.IsMoving = true;
        diagnostic.Tick(90);
        string line = Snapshots().Last();
        Check(line.Contains("goal=2") && line.Contains("heard=True") && line.Contains("seen=False") && line.Contains("visible=False"), "diagnostics report actual goal and contact flags");
        Check(line.Contains("heardAge=90.0") && line.Contains("searchDelay=60.0") && line.Contains("combat=Search"), "diagnostics report search age delay and decision");
        Check(line.Contains("searchReason=DontWantTo") && line.Contains("notWant=TimeBeforeSearch") && line.Contains("cantStart=NoPath"), "diagnostics read nested search reason fields");
        Check(line.Contains("mover=True") && line.Contains("standby=active") && line.Contains("moved=10.0m/90.0s"), "diagnostics report movement over actual interval");
        Check(!Plugin.ModLogger.Lines.Any(text => text.Contains("private-")), "diagnostics do not disclose profile identities");
        for (int i = 0; i < 500; i++) Refresh(diagnostic, observed);
        Check(Snapshots().Length == 2 && observed.HearingState.Calls == 1, "same-frame retries do not spam diagnostics or reset hearing");
        TeamDeathmatchRelations.Reset();
        diagnostic.Tick(100);
        Check(Snapshots().Length == 3 && Snapshots().Last().Contains("bot=1"), "new raid reset clears only local diagnostic ownership");
        var ordinary = Fresh(); ArenaSession.ActiveMode = "tactical"; ordinary.Tick(0);
        Check(Snapshots().Length == 0 && ordinary.Source.BotsGroup.PositionReports == 0, "other modes have no TDM awareness mutation or diagnostics");
    }
    internal static void Main()
    {
        AdmissionAndAge(); ContactAndForget(); Ownership(); PolicyAndCallbacks(); FallbackAndDiagnostics();
        Console.WriteLine($"TDM_AWARENESS_CONTRACTS_PASSED: {_checks} assertions; production Relations and hearing bridge linked, no GPU claim.");
    }
}
