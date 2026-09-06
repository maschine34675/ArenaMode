using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SAIN.Components;
using SAIN.Layers;
using SAIN.Layers.Combat.Solo;
using SAIN.Layers.Combat.Solo.Cover;

internal static class Program
{
    private static int _checks;
    private static readonly MethodInfo AddMethod = typeof(BotEnemiesController).GetMethod("SetInfo");
    private static readonly MethodInfo RemoveMethod = typeof(BotEnemiesController).GetMethod("Remove");
    private static readonly MethodInfo ObserveMethod = typeof(BotOwner).GetMethod("PreActivate");

    private static int Main()
    {
        try
        {
            NativeControls();
            Binding();
            Run("capacity boundaries", Capacity);
            Run("full-array removals", FullRemovals);
            Run("ordinary scope", Scope);
            Run("retired identity", RetiredIdentity);
            Run("live and retired actions", Actions);
            Run("shutdown", Cleanup);
            Console.WriteLine($"ARENA_BOT_LIFECYCLE_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("ARENA_BOT_LIFECYCLE_CHECKS_FAILED: " + exception);
            return 1;
        }
    }

    private static void Run(string name, Action body)
    {
        try { body(); }
        catch (Exception ex) { throw new InvalidOperationException(name + ": " + ex.GetBaseException().Message, ex); }
    }

    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); _checks++; }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (Exception exception) { return exception.GetBaseException() is T; }
    }

    private static BotOwner Setup(bool active = true, bool armed = false)
    {
        ArenaBotLifecycleScope.Clear();
        ArenaSession.IsActive = active;
        ArenaSession.ArmedForNextRaid = armed;
        ArenaSession.ArmedMap = "labyrinth";
        ArenaSession.World = Singleton<GameWorld>.Instance = new();
        ArenaSession.Game = Singleton<AbstractGame>.Instance = new();
        BotManagerComponent.Instance = new();
        var owner = NewOwner();
        return owner;
    }

    private static BotOwner NewOwner()
    {
        var player = new Player();
        Singleton<GameWorld>.Instance.Players[player.ProfileId] = player;
        var owner = new BotOwner { GetPlayer = player };
        owner.EnemiesController = new BotEnemiesController(owner);
        return owner;
    }

    private static EnemyInfo Enemy(BotOwner owner)
        => new(new BotsGroup(), new Player(), owner, new BotGroupEnemyInfo());

    private static bool Prefix(MethodInfo method, object instance)
    {
        if (!Harmony.Hooks.TryGetValue(method, out var hook)) return true;
        var result = hook.Prefix.method.Invoke(null, new[] { instance });
        return result is not bool value || value;
    }

    private static void Observe(BotOwner owner) => Prefix(ObserveMethod, owner);
    private static void Add(BotOwner owner, EnemyInfo enemy)
    {
        if (Prefix(AddMethod, owner.EnemiesController)) owner.EnemiesController.SetInfo(enemy.Person, enemy);
    }
    private static void Remove(BotOwner owner, EnemyInfo enemy)
    {
        if (Prefix(RemoveMethod, owner.EnemiesController)) owner.EnemiesController.Remove(enemy.Person);
    }
    private static void Stop(BotAction action)
    {
        if (Prefix(action.GetType().GetMethod("Stop"), action)) action.Stop();
    }

    private static void NativeControls()
    {
        var owner = Setup();
        for (int i = 0; i < 100; i++) { var e = Enemy(owner); owner.EnemiesController.SetInfo(e.Person, e); }
        var extra = Enemy(owner);
        Check(Throws<IndexOutOfRangeException>(() => owner.EnemiesController.SetInfo(extra.Person, extra)),
            "unpatched actual native SetInfo fails beyond 100");
        owner = Setup();
        for (int i = 0; i < 100; i++) { var e = Enemy(owner); owner.EnemiesController.SetInfo(e.Person, e); }
        Check(Throws<IndexOutOfRangeException>(() => owner.EnemiesController.Remove(owner.EnemiesController.SortedInfos[50].Person)),
            "unpatched actual native Remove fails at full capacity");
        Check(Throws<NullReferenceException>(() => new DogFightAction(owner).Stop()), "missing-component action positive control");
    }

    private static void Binding()
    {
        Harmony.FailPatchNumber = 2;
        ArenaBotLifecycleSafety.Enable();
        Check(Harmony.Hooks.Count == 0, "native partial installation rolls back");
        ArenaBotLifecycleSafety.Enable();
        Check(Harmony.Hooks.Count == 3, "native hooks installed");
        Check(!ArenaSainActionSafety.TryBind(null), "absent SAIN fails open");
        Harmony.FailPatchNumber = 2;
        Check(!ArenaSainActionSafety.TryBind(typeof(Program).Assembly) && Harmony.Hooks.Count == 3,
            "optional partial installation preserves native hooks only");
        Check(ArenaSainActionSafety.TryBind(typeof(Program).Assembly) && Harmony.Hooks.Count == 5,
            "only two optional stop hooks installed");
    }

    private static void Capacity()
    {
        var owner = Setup();
        Observe(owner);
        var controller = owner.EnemiesController;
        var order = new List<EnemyInfo>();
        for (int i = 0; i < 1100; i++)
        {
            var before = controller.SortedInfos;
            var snapshot = before.Take(controller._countEnemies).ToArray();
            int count = controller._countEnemies, dictionaryCount = controller.EnemyInfos.Count;
            Prefix(AddMethod, controller);
            Check(controller._countEnemies == count && controller.EnemyInfos.Count == dictionaryCount
                && snapshot.SequenceEqual(controller.SortedInfos.Take(count)), "prefix preserves native bookkeeping");
            Check(controller.SortedInfos.Length >= count + 2, "add reserves entry and null sentinel");
            var enemy = Enemy(owner);
            controller.SetInfo(enemy.Person, enemy);
            order.Add(enemy);
            if (i is 98 or 99 or 100 or 199 or 399 or 799) CheckState(controller, order);
        }
        for (int i = 0; i < 500; i++)
        {
            int a = (i * 37) % order.Count, b = (i * 71) % order.Count;
            controller.ChangeByIndex(a, b);
            (order[a], order[b]) = (order[b], order[a]);
            int index = (i * 11) % order.Count;
            var removed = order[index];
            Remove(owner, removed);
            order.RemoveAt(index);
            Check(removed.DisposeCount == 1, "native disposal occurs exactly once");
            CheckState(controller, order);
        }
        Check(ArenaMode.Plugin.ModLogger.Messages.Count(m => m.StartsWith("Arena enemy capacity:")) <= 2,
            "capacity diagnostics bounded per owner");
        var array = controller.SortedInfos;
        controller._countEnemies = -1;
        Prefix(AddMethod, controller);
        Check(ReferenceEquals(array, controller.SortedInfos), "negative count is not repaired");
        controller._countEnemies = array.Length + 1;
        Prefix(AddMethod, controller);
        Check(ReferenceEquals(array, controller.SortedInfos), "invalid large count is not allocated");
    }

    private static void CheckState(BotEnemiesController controller, List<EnemyInfo> order)
    {
        Check(controller._countEnemies == order.Count && controller.EnemyInfos.Count == order.Count,
            "native count and dictionary match independent model");
        Check(order.SequenceEqual(controller.SortedInfos.Take(order.Count))
            && order.Select((enemy, index) => enemy.PriorityIndex == index
                && ReferenceEquals(controller.EnemyInfos[enemy.Person], enemy)).All(x => x),
            "native order and priorities match independent model");
        Check(controller.SortedInfos[order.Count] == null, "native removal retains terminal null");
    }

    private static void FullRemovals()
    {
        foreach (int index in new[] { 0, 50, 99 })
        {
            var owner = Setup();
            var order = new List<EnemyInfo>();
            for (int i = 0; i < 100; i++)
            {
                var enemy = Enemy(owner); owner.EnemiesController.SetInfo(enemy.Person, enemy); order.Add(enemy);
            }
            var removed = order[index];
            Remove(owner, removed);
            order.RemoveAt(index);
            CheckState(owner.EnemiesController, order);
            Check(removed.DisposeCount == 1, "full-array removal disposes once");
        }
    }

    private static void Scope()
    {
        foreach (var reason in new[] { "ordinary", "wrong-world", "wrong-game", "human", "unregistered", "wrong-controller", "wrong-armed-map" })
        {
            var owner = Setup();
            switch (reason)
            {
                case "ordinary": ArenaSession.IsActive = false; break;
                case "wrong-world": Singleton<GameWorld>.Instance = new(); break;
                case "wrong-game": Singleton<AbstractGame>.Instance = new(); break;
                case "human": owner.GetPlayer.IsAI = false; break;
                case "unregistered": Singleton<GameWorld>.Instance.Players.Clear(); break;
                case "wrong-controller": owner.EnemiesController._owner = NewOwner(); break;
                case "wrong-armed-map": ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMap = "factory4_day"; break;
            }
            var array = owner.EnemiesController.SortedInfos;
            owner.EnemiesController._countEnemies = array.Length;
            Prefix(AddMethod, owner.EnemiesController);
            Check(ReferenceEquals(array, owner.EnemiesController.SortedInfos), "out-of-scope capacity unchanged: " + reason);
        }
        var armed = Setup(false, true);
        Observe(armed);
        Check(ArenaBotLifecycleScope.Owns(armed), "armed bot captured before exact active raid");
    }

    private static void RetiredIdentity()
    {
        var owner = Setup();
        Observe(owner);
        ArenaSession.IsActive = false;
        Singleton<GameWorld>.Instance = null;
        Singleton<AbstractGame>.Instance = null;
        owner.Destroyed = owner.GetPlayer.Destroyed = true;
        owner.EnemiesController._countEnemies = 100;
        Prefix(RemoveMethod, owner.EnemiesController);
        Check(owner.EnemiesController.SortedInfos.Length >= 101, "owned retired wrapper still gets sentinel after globals clear");
        Check(ArenaBotLifecycleScope.Owns(owner), "owned retired wrapper survives GameEnd");
        Singleton<GameWorld>.Instance = new();
        Check(!ArenaBotLifecycleScope.Owns(owner), "owned old world rejected after new world");
        Singleton<GameWorld>.Instance = null;
        Singleton<AbstractGame>.Instance = new();
        Check(!ArenaBotLifecycleScope.Owns(owner), "owned old game rejected after new game");
        Singleton<AbstractGame>.Instance = null;
        owner.GetPlayer = new Player();
        Check(!ArenaBotLifecycleScope.Owns(owner), "same owner with different body rejected");
    }

    private static void Actions()
    {
        foreach (bool cover in new[] { false, true })
        foreach (var mode in new[] { "missing", "destroyed-cache", "cached", "attached", "registered", "destroyed-manager-live-registry",
            "destroyed-cache-live-attachment", "retired", "ordinary", "foreign-owner", "foreign-world", "unknown-lookup", "original-failure" })
        {
            var owner = Setup();
            Observe(owner);
            BotAction action = cover ? new SeekCoverAction(owner) : new DogFightAction(owner);
            var live = new BotComponent();
            bool shouldSkip = mode is "missing" or "destroyed-cache" or "retired";
            switch (mode)
            {
                case "destroyed-cache": action.Cache(new BotComponent { Destroyed = true }); break;
                case "cached": action.Cache(live); break;
                case "attached": owner.Components[typeof(BotComponent)] = live; break;
                case "registered": BotManagerComponent.Instance.Bots[owner.ProfileId] = live; break;
                case "destroyed-manager-live-registry": BotManagerComponent.Instance.Destroyed = true; BotManagerComponent.Instance.Bots[owner.ProfileId] = live; break;
                case "destroyed-cache-live-attachment": action.Cache(new BotComponent { Destroyed = true }); owner.Components[typeof(BotComponent)] = live; break;
                case "retired": ArenaSession.IsActive = false; Singleton<GameWorld>.Instance = null; Singleton<AbstractGame>.Instance = null; BotManagerComponent.Instance = null; owner.Destroyed = owner.GetPlayer.Destroyed = true; break;
                case "ordinary": ArenaSession.IsActive = false; ArenaBotLifecycleScope.Clear(); break;
                case "foreign-owner":
                    ArenaSession.IsActive = false;
                    var other = NewOwner(); other.GetPlayer.ProfileId = owner.ProfileId;
                    action = cover ? new SeekCoverAction(other) : new DogFightAction(other); break;
                case "foreign-world": Singleton<GameWorld>.Instance = new(); break;
                case "unknown-lookup": BotManagerComponent.Instance.ThrowLookup = true; break;
                case "original-failure": action.Cache(live); action.ThrowOriginal = true; break;
            }
            bool runOriginal = Prefix(action.GetType().GetMethod("Stop"), action);
            Check(runOriginal != shouldSkip, "missing-component guard preserves every viable or unknown cleanup: " + mode);
            if (shouldSkip)
            {
                int logsBefore = ArenaMode.Plugin.ModLogger.Messages.Count;
                for (int i = 0; i < 100; i++) Stop(action);
                Check(action.NativeStops == 0, "retired action never dereferences missing component");
                Check(ArenaMode.Plugin.ModLogger.Messages.Count == logsBefore, "retired stop diagnostics stay bounded");
            }
            else if (mode is "cached" or "attached" or "registered" or "destroyed-manager-live-registry" or "destroyed-cache-live-attachment")
            {
                Stop(action);
                Check(action.NativeStops == 1, "live original stop executes once: " + mode);
            }
            else
            {
                Check(Throws<Exception>(() => Stop(action)) && action.NativeStops == 1,
                    "original stop exceptions remain visible: " + mode);
            }
        }
    }

    private static void Cleanup()
    {
        ArenaSainActionSafety.Shutdown();
        Check(Harmony.Hooks.Count == 3, "optional shutdown leaves native hooks intact");
        ArenaBotLifecycleSafety.Shutdown();
        Check(Harmony.Hooks.Count == 0, "all safety hooks removed on shutdown");
        var owner = Setup();
        Check(Throws<NullReferenceException>(() => Stop(new DogFightAction(owner))), "shutdown restores native action");
    }
}
