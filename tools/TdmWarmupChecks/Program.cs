using System.Reflection;
using ArenaMode;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Modes;
using ArenaMode.Compat;
using ArenaMode.Respawn;
using ArenaMode.Kits;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static readonly MethodInfo Tick = typeof(ArenaController).GetMethod("TickKitWarmup", BindingFlags.NonPublic | BindingFlags.Instance);
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void At(ArenaController controller, float time)
    {
        Time.time = time;
        Tick.Invoke(controller, null);
    }
    private static ArenaController New(string mode = "teamdeathmatch")
    {
        ArenaSession.IsActive = true;
        ArenaSession.ActiveMode = mode;
        Settings.RandomKit.Value = true;
        PitFireTeamCompat.TeamArenaActive = PitFireTeamCompat.IsFinalizing = false;
        ArenaRespawnDriver.IsRespawnInProgress = false;
        ArenaMatchService.Current = null;
        KitCatalog.KitProvider = null;
        KitBundlePreloader.Reset();
        typeof(ArenaController).GetField("_rewarmRequested", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        return new ArenaController();
    }
    public static void Main()
    {
        var c = New();
        At(c, 1); At(c, 50);
        Check(c.Starts == 0, "latched TDM with no match does not warm catalog");
        ArenaMatchService.Current = new Match { Mode = new object() };
        At(c, 60);
        Check(c.Starts == 0, "mismatched current mode does not warm TDM catalog");
        var team = new TeamDeathmatchMode();
        ArenaMatchService.Current.Mode = team;
        for (int second = 61; second <= 430; second++) At(c, second);
        Check(c.Starts == 0, "slow TDM assembly never starts optional catalog work");
        team.InitialRosterReady = true;
        var pools = new EFT.ObjectsFactory();
        Comfort.Common.Singleton<EFT.ObjectsFactory>.Instance = pools;
        var factory = new EFT.ItemFactory();
        factory.ItemTemplates[new MongoID("test")] = new EFT.ItemTemplate { AllResources = new() { new ResourceKey("resource") } };
        Comfort.Common.Singleton<EFT.ItemFactory>.Instance = factory;
        var openingKit = new KitDefinition { Id = "opening", Items = new() { new KitItemNode { Tpl = "test", ParentId = "root" } } };
        var opening = KitBundlePreloader.Preload(openingKit);
        Check(opening.MoveNext() && KitBundlePreloader.IsPreloading, "opening preload owns active native pool work before pipeline flag");
        At(c, 430.5f);
        Check(c.Starts == 0, "initial kit pool loading blocks optional TDM catalog before pipeline flag");
        pools.Loads.Dequeue().SetResult();
        Check(!opening.MoveNext() && !KitBundlePreloader.IsPreloading, "successful native load releases its epoch ownership");
        ArenaRespawnDriver.IsRespawnInProgress = true;
        At(c, 431);
        Check(c.Starts == 0, "active player pipeline blocks optional TDM warmup");
        ArenaRespawnDriver.IsRespawnInProgress = false;
        At(c, 432);
        Check(c.Starts == 1, "ready TDM starts the deferred catalog once without another eight-second delay");
        At(c, 440);
        Check(c.Starts == 1, "subsequent ticks do not duplicate warmup");
        ArenaController.RequestKitWarmup();
        team.InitialRosterReady = false;
        At(c, 450); At(c, 452);
        Check(c.Starts == 1, "rewarm also respects current TDM readiness");
        team.InitialRosterReady = true;
        At(c, 453);
        Check(c.Starts == 2, "ready rewarm starts once");
        ArenaSession.IsActive = false;
        At(c, 460);
        Check(c.Starts == 2, "inactive raid starts no work");
        ArenaSession.IsActive = true;
        team.InitialRosterReady = false;
        At(c, 470); At(c, 490);
        Check(c.Starts == 2, "new raid does not inherit prior readiness in the scheduler");
        team.InitialRosterReady = true;
        At(c, 491);
        Check(c.Starts == 3, "new ready raid can warm its own pools");

        c = New("domination");
        At(c, 1); At(c, 50);
        Check(c.Starts == 0, "latched Domination also defers optional catalog while no match exists");
        var objectiveTeam = new TeamDeathmatchMode();
        ArenaMatchService.Current = new Match { Mode = objectiveTeam };
        At(c, 60);
        Check(c.Starts == 0, "Domination assembly does not contend with optional catalog warmup");
        objectiveTeam.InitialRosterReady = true;
        ArenaRespawnDriver.IsRespawnInProgress = true;
        At(c, 61);
        Check(c.Starts == 0, "Domination player pipeline retains the same warmup hold");
        ArenaRespawnDriver.IsRespawnInProgress = false;
        At(c, 62); At(c, 63);
        Check(c.Starts == 1, "Domination releases optional catalog once the roster and player are ready");

        foreach (string mode in new[] { "endless", "rounds", "gungame", "tactical" })
        {
            c = New(mode);
            At(c, 1); At(c, 8.9f);
            Check(c.Starts == 0, "other modes retain initial delay: " + mode);
            At(c, 9);
            Check(c.Starts == 1, "other modes retain warmup schedule: " + mode);
        }
        c = New();
        Settings.RandomKit.Value = false;
        ArenaMatchService.Current = new Match { Mode = new TeamDeathmatchMode { InitialRosterReady = true } };
        At(c, 1); At(c, 100);
        Check(c.Starts == 0, "fixed kit does not start full catalog warmup");
        KitCatalog.KitProvider = new object();
        At(c, 101); At(c, 109);
        Check(c.Starts == 1, "kit providers retain optional catalog warming");
        foreach (bool finalizing in new[] { false, true })
        {
            c = New("tactical");
            PitFireTeamCompat.TeamArenaActive = !finalizing;
            PitFireTeamCompat.IsFinalizing = finalizing;
            At(c, 1); At(c, 100);
            Check(c.Starts == 0, "protected PFT never warms Arena catalogs");
        }
        c = New("endless");
        ArenaSession.IsActive = false;
        At(c, 1); At(c, 100);
        Check(c.Starts == 0, "ordinary raids never warm Arena catalogs");
        PoolLifecycle(openingKit, pools);
        Console.WriteLine($"TDM_WARMUP_PRODUCTION_PASSED: {_assertions} assertions");
    }

    private static void PoolLifecycle(KitDefinition kit, EFT.ObjectsFactory pools)
    {
        ArenaSession.IsActive = true;
        KitBundlePreloader.Reset();
        var first = KitBundlePreloader.Preload(kit);
        var second = KitBundlePreloader.Preload(kit);
        first.MoveNext(); second.MoveNext();
        var oldA = pools.Loads.Dequeue();
        var oldB = pools.Loads.Dequeue();
        oldA.SetResult();
        Check(KitBundlePreloader.IsPreloading, "one completed native load cannot hide another current load");
        KitBundlePreloader.Reset();
        Check(!KitBundlePreloader.IsPreloading, "reset clears prior raid load ownership");
        var current = KitBundlePreloader.Preload(kit);
        current.MoveNext();
        var currentTask = pools.Loads.Dequeue();
        oldB.SetResult();
        Check(KitBundlePreloader.IsPreloading, "old completion cannot release the new raid load");
        Check(!second.MoveNext(), "old polling coroutine cannot publish into new raid cache");
        currentTask.SetException(new InvalidOperationException("controlled native pool failure"));
        Check(!current.MoveNext() && !KitBundlePreloader.IsPreloading, "fault releases ownership and preload reports without throwing");
        var canceled = KitBundlePreloader.Preload(kit);
        Check(canceled.MoveNext(), "failed native load did not mark templates warm");
        pools.Loads.Dequeue().SetCanceled();
        Check(!canceled.MoveNext() && !KitBundlePreloader.IsPreloading, "canceled native work releases its ownership");
        KitOpticPreloadGuard.ThrowOnBegin = true;
        var guardFailure = KitBundlePreloader.Preload(kit);
        Check(!guardFailure.MoveNext() && !KitBundlePreloader.IsPreloading, "guard exception cannot leak active-load counter");
        KitOpticPreloadGuard.ThrowOnBegin = false;
        var detached = KitBundlePreloader.Preload(kit);
        detached.MoveNext();
        ((IDisposable)detached).Dispose();
        Check(KitBundlePreloader.IsPreloading, "disposing polling coroutine does not pretend native task completed");
        pools.Loads.Dequeue().SetResult();
        Check(!KitBundlePreloader.IsPreloading, "detached native completion still releases ownership");
    }
}
