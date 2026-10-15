using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

internal static partial class Program
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
    private static string[] Snapshots() => Plugin.ModLogger.Lines.Where(line => line.StartsWith("[dev] Arena TDM awareness:")).ToArray();
    private static string[] PathDetails() => Plugin.ModLogger.Lines.Where(line => line.StartsWith("[dev] Arena TDM path detail:")).ToArray();

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
        Check(Snapshots().Single().Contains("sainGoal=n/a") && Snapshots().Single().Contains("pathReason=n/a"), "absent SAIN diagnostic does not invent a goal or path failure");
        var drift = Fresh(); var enemy = drift.Enemy();
        TdmSearchAwareness.Configure(typeof(UnsupportedEnemy), typeof(BotComponent));
        drift.Tick(0);
        Check(enemy.HearingState.Calls == 0 && enemy.PositionUpdates == 1 && drift.Source.BotsGroup.PositionReports == 1, "unsupported hearing shape retains original awareness");
        Check(Snapshots().Single().Contains("cue=hearing-unavailable"), "unsupported hearing shape is observable");
        TdmSearchAwareness.Configure(typeof(Enemy), typeof(BotComponent));

        var diagnostic = Fresh(); var observed = diagnostic.Enemy();
        diagnostic.Source.Memory.GoalEnemy = observed.EnemyInfo;
        diagnostic.Sain.EnemyController.GoalEnemy = observed;
        diagnostic.Sain.Decision.CurrentCombatDecision = "Search";
        diagnostic.Sain.Decision.EnemyDecisions.DebugShallSearch = false;
        diagnostic.Sain.Decision.EnemyDecisions.DebugSearchReasons = new SearchReasons
        { NotSearchReason = "DontWantTo", PathCalcFailReason = "", WantSearchReasons = new WantReasons { NotWantToSearchReason = "TimeBeforeSearch", WantToSearchReason = "None", CantStartReason = "NoPath" } };
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

    private static void SelectedGoalDiagnostics()
    {
        var raid = Fresh(); var nearest = raid.Enemy();
        var selectedPlayer = new Player { ProfileId = "private-other-goal", Position = new Vector3(400) };
        var selected = new Enemy { EnemyPlayer = selectedPlayer, Seen = true, IsVisible = true, EnemyKnown = false };
        selected.HearingState.Heard = true; selected.HearingState.At = -20;
        raid.Source.Memory.GoalEnemy = nearest.EnemyInfo;
        raid.Sain.EnemyController.GoalEnemy = selected;
        raid.Sain.Decision.CurrentCombatDecision = "SeekCover";
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = false;
        raid.Sain.Decision.EnemyDecisions.DebugSearchReasons = new SearchReasons
        {
            NotSearchReason = "PathCalcFailed", PathCalcFailReason = "path not complete",
            WantSearchReasons = new WantReasons { WantToSearchReason = "NewSearch_EnemyNotSeen" }
        };
        raid.Tick(0);
        string line = Snapshots().Single();
        Check(line.Contains("target=2, distance=200.0m") && line.Contains("goal=2,") && line.Contains("sainGoal=3, goalMatchesTarget=False, goalDistance=400.0m"), "selected SAIN goal stays distinct from nearest and native goals");
        Check(line.Contains("seen=False, heard=True, visible=False, known=True, heardAge=0.0")
            && line.Contains("goalSeen=True, goalHeard=True, goalVisible=True, goalKnown=False, goalHeardAge=20.0"), "selected goal contact never borrows nearest contact");
        Check(line.Contains("shallSearch=False, searchReason=PathCalcFailed, pathReason=path not complete"), "evaluated search exposes the exact path failure");
        Check(selected.HearingState.Calls == 0 && selected.PositionUpdates == 0 && nearest.HearingState.Calls == 1
            && ReferenceEquals(raid.Sain.EnemyController.GoalEnemy, selected), "goal diagnostics neither seed nor update nor select an opponent");
        Check(!line.Contains("private-"), "selected goal diagnostic keeps identities private");

        // A higher-priority SAIN decision did not evaluate search this pass;
        // its retained reasons must not be attributed to the newly selected goal.
        raid.Sain.EnemyController.GoalEnemy = nearest;
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = null;
        raid.Tick(30);
        line = Snapshots().Last();
        Check(line.Contains("goalMatchesTarget=True") && line.Contains("shallSearch=n/a, searchReason=n/a, pathReason=n/a")
            && line.Contains("want=n/a, notWant=n/a, cantStart=n/a"), "unevaluated search does not relabel retained reasons as current");
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = false;
        raid.Sain.EnemyController.GoalEnemy = null;
        raid.Tick(60);
        line = Snapshots().Last();
        Check(line.Contains("sainGoal=none, goalMatchesTarget=n/a, goalDistance=n/am") && line.Contains("goalSeen=n/a")
            && line.Contains("pathReason=n/a"), "no selected goal never falls back to cue target");
        raid.Sain.OnGetGoal = () => throw new InvalidOperationException("modeled optional getter failure");
        raid.Tick(90);
        line = Snapshots().Last();
        Check(line.Contains("sainGoal=n/a") && line.Contains("heardAge=90.0"), "unavailable goal getter retains independent cue diagnostics");
        Check(nearest.HearingState.Calls == 1 && nearest.PositionUpdates == 4 && Snapshots().Length == 4,
            "extended diagnostics retain hearing age position refresh and cadence");
        raid.Sain.OnGetGoal = null;
        raid.Sain.EnemyController.GoalEnemy = nearest;
        foreach (string earlyDecision in new[] { "None", "AvoidGrenade" })
        {
            raid.Sain.Decision.CurrentCombatDecision = earlyDecision;
            raid.Tick(Time.time + 30);
            Check(Snapshots().Last().Contains("shallSearch=n/a, searchReason=n/a, pathReason=n/a"), "early decision cannot expose stale search result");
        }
    }
    internal static void Main()
    {
        AdmissionAndAge(); ContactAndForget(); Ownership(); PolicyAndCallbacks(); FallbackAndDiagnostics(); SelectedGoalDiagnostics(); InvalidPathPositions(); MoverDiagnostics();
        Console.WriteLine($"TDM_AWARENESS_CONTRACTS_PASSED: {_checks} assertions; production Relations and hearing bridge linked, no GPU claim.");
    }

    private static void InvalidPathPositions()
    {
        var raid = Fresh(); var nearest = raid.Enemy();
        var selected = new Enemy
        {
            EnemyPlayer = new Player { ProfileId = "private-selected-path-target", Position = new Vector3(510, 9, 19) },
            KnownPlaces = new KnownPlaces { LastKnownPosition = new Vector3(501, 4, 14) },
            Path = new EnemyPath { PathToEnemyStatus = "PathInvalid", PathCornerCount = 0, PathLength = 321 }
        };
        selected.EnemyTransform.NavData = new NavData
        {
            Position = new Vector3(500, 3, 12), LastValidHit = new NavHit { position = new Vector3(497, 3, 10), hit = true },
            Status = "CloseToNavMesh", IsOnNavMesh = true, TimeLastOnNavMesh = 10
        };
        raid.Source.GetPlayer.Position = new Vector3(3, 8, 7);
        raid.Sain.TransformState.NavData = new NavData
        {
            Position = new Vector3(3, 1, 7), LastValidHit = new NavHit { position = new Vector3(1, 1, 4), hit = true },
            Status = "OnNavMesh", IsOnNavMesh = false, TimeLastOnNavMesh = 10
        };
        int navReads = 0; raid.Sain.OnGetTransform = () => navReads++;
        raid.Sain.EnemyController.GoalEnemy = nearest;
        raid.Sain.Decision.CurrentCombatDecision = "SeekCover";
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = false;
        raid.Sain.Decision.EnemyDecisions.DebugSearchReasons = new SearchReasons
        { NotSearchReason = "PathCalcFailed", PathCalcFailReason = "path Invalid" };
        raid.Tick(0);
        Check(PathDetails().Length == 0 && navReads == 0, "first invalid path sample performs no detailed reads");
        for (int i = 0; i < 200; i++) Refresh(raid, nearest);
        Check(PathDetails().Length == 0 && navReads == 0, "same-frame calls cannot reach repeated path threshold");
        raid.Sain.EnemyController.GoalEnemy = selected;
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = null;
        raid.Tick(30);
        Check(PathDetails().Length == 0 && navReads == 0, "retained invalid reason cannot count as a repeated failure");
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = false;
        raid.Tick(60);
        string line = PathDetails().Single();
        Check(line.Contains("bot=1, sainGoal=3, invalidSamples=2, sampleWindow=60.0s"), "repeated invalid samples across goals capture selected endpoint");
        Check(line.Contains("sourceActual=(3.00;8.00;7.00), sourceNav=(3.00;1.00;7.00), sourceLastHit=(1.00;1.00;4.00), sourceHitValid=True"), "source actual nav and hit positions stay distinct");
        Check(line.Contains("sourceActualMinusNav=(0.00;7.00;0.00), sourceNavMinusHit=(2.00;0.00;3.00)"), "source offsets preserve height and lateral sample drift");
        Check(line.Contains("goalActual=(510.00;9.00;19.00), goalNav=(500.00;3.00;12.00), goalLastHit=(497.00;3.00;10.00)"), "path detail uses selected goal nav rather than cue target");
        Check(line.Contains("goalKnown=(501.00;4.00;14.00), goalKnownMinusNav=(1.00;1.00;2.00)"), "last known path destination is distinct from actual target position");
        Check(line.Contains("sourceStatus=OnNavMesh, sourceOnNav=False, sourceLastOnAge=50.0")
            && line.Contains("goalStatus=CloseToNavMesh, goalOnNav=True, goalLastOnAge=50.0"), "cached navigation flags and status age retain native meaning");
        Check(line.Contains("pathStatus=PathInvalid, pathCorners=0, pathLength=321.0"), "path detail reads cached status corners and length");
        Check(!line.Contains("private-"), "path detail never prints profile identities");
        for (int time = 90; time <= 300; time += 30) raid.Tick(time);
        Check(PathDetails().Length == 1 && navReads == 1, "path detail and extra reads bounded to once per body per raid");
        Check(nearest.HearingState.Calls == 1 && selected.HearingState.Calls == 0 && selected.PositionUpdates == 0
            && raid.Source.GetPlayer.Position.sqrMagnitude == new Vector3(3, 8, 7).sqrMagnitude
            && selected.Path.PathCornerCount == 0 && selected.Path.PathLength == 321, "path diagnosis leaves contacts bodies and path cache untouched");

        TeamDeathmatchRelations.Reset();
        raid.Tick(330); raid.Tick(360);
        Check(PathDetails().Length == 2 && navReads == 2, "new raid resets per-body path detail allowance");

        // Best-effort diagnostics must not invent zero coordinates when optional
        // state disappeared. A getter exception is also represented as n/a.
        raid = Fresh(); nearest = raid.Enemy();
        raid.Sain.EnemyController.GoalEnemy = nearest;
        raid.Sain.OnGetTransform = () => throw new InvalidOperationException("modeled missing nav getter");
        nearest.EnemyTransform = null; nearest.KnownPlaces = null; nearest.Path = null;
        raid.Sain.Decision.CurrentCombatDecision = "SeekCover";
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = false;
        raid.Sain.Decision.EnemyDecisions.DebugSearchReasons = new SearchReasons
        { NotSearchReason = "PathCalcFailed", PathCalcFailReason = "path Invalid" };
        raid.Tick(0); raid.Tick(30);
        line = PathDetails().Single();
        Check(line.Contains("sourceNav=n/a, sourceLastHit=n/a, sourceHitValid=n/a") && line.Contains("goalKnown=n/a")
            && line.Contains("pathStatus=n/a, pathCorners=n/a, pathLength=n/a"), "missing path diagnostic members never invent coordinates or state");

        TeamDeathmatchRelations.Reset();
        raid.Sain.OnGetTransform = () => raid.Sain.EnemyController.GoalEnemy = selected;
        raid.Tick(60); raid.Tick(90);
        Check(PathDetails().Length == 1, "goal change during detail reads discards mixed endpoints");
        raid.Sain.EnemyController.GoalEnemy = nearest;
        raid.Sain.OnGetTransform = null;
        raid.Tick(120);
        Check(PathDetails().Length == 2, "discarded mixed snapshot does not consume the detail allowance");

        // A non-invalid result does not meet the trigger even if path details
        // were retained elsewhere; normal raids never enter the observer.
        raid = Fresh(); nearest = raid.Enemy();
        raid.Sain.EnemyController.GoalEnemy = nearest;
        raid.Sain.Decision.CurrentCombatDecision = "SeekCover";
        raid.Sain.Decision.EnemyDecisions.DebugShallSearch = false;
        raid.Sain.Decision.EnemyDecisions.DebugSearchReasons = new SearchReasons
        { NotSearchReason = "PathCalcFailed", PathCalcFailReason = "path not complete" };
        raid.Tick(0); raid.Tick(30);
        Check(PathDetails().Length == 0, "other path failures do not trigger invalid-path position detail");
        raid.Sain.Decision.EnemyDecisions.DebugSearchReasons = new SearchReasons
        { NotSearchReason = "PathCalcFailed", PathCalcFailReason = "path Invalid" };
        ArenaSession.IsActive = false; raid.Tick(60); raid.Tick(90);
        Check(PathDetails().Length == 0, "ordinary raid has no path position diagnosis");
    }
}
