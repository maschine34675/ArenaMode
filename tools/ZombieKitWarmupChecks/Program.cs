using System.Reflection;
using ArenaMode;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Modes;
using ArenaMode.Compat;
using ArenaMode.Kits;
using Comfort.Common;
using EFT;
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
    private static KitDefinition Kit(string id) => new()
    {
        Id = id,
        Items = new() { new KitItemNode { Tpl = id, ParentId = "root" } }
    };
    private static ObjectsFactory New(string mode = "zombie", KitPreloadScope scope = KitPreloadScope.WholeCatalogue, bool ownGear = false)
    {
        KitBundlePreloader.DiscardRaidStart("test boundary");
        if (Singleton<ObjectsFactory>.Instance is { } previous) CompleteAll(previous);
        KitBundlePreloader.Reset();
        Singleton<GameWorld>.Instance = new();
        var pools = Singleton<ObjectsFactory>.Instance = new();
        var items = Singleton<ItemFactory>.Instance = new();
        foreach (var id in new[] { "opening", "optional", "ladder", "refill" })
            items.ItemTemplates[new MongoID(id)] = new ItemTemplate { AllResources = new() { new ResourceKey(id) } };
        KitCatalog.Opening = Kit("opening");
        KitCatalog.Kits = new() { KitCatalog.Opening, Kit("optional") };
        LadderCatalog.Rungs = new() { Kit("ladder") };
        KitCatalog.Draws = 0;
        KitCatalog.KitProvider = null;
        Settings.KitPreloadInLoadingScreen.Value = scope;
        Settings.RandomKit.Value = true;
        ArenaSession.IsActive = false;
        ArenaSession.ArmedForNextRaid = true;
        ArenaSession.ArmedMode = mode;
        ArenaSession.ActiveMode = null;
        ArenaSession.ZombieOwnGearArmed = ownGear;
        ArenaSession.ArmEpoch++;
        ArenaMenuSelection.CompatibilityTeamArenaActive = false;
        PitFireTeamCompat.TeamArenaActive = PitFireTeamCompat.IsFinalizing = false;
        ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress = false;
        ArenaMatchService.Current = null;
        typeof(ArenaController).GetField("_rewarmRequested", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        return pools;
    }
    private static void CompleteAll(ObjectsFactory pools)
    {
        while (pools.Loads.Count > 0) pools.Loads.Dequeue().SetResult();
    }
    private static void Activate(string mode = "zombie")
    {
        ArenaSession.IsActive = true;
        ArenaSession.ActiveMode = mode;
        KitBundlePreloader.Reset();
        KitBundlePreloader.AdoptRaidStart(Singleton<GameWorld>.Instance);
    }
    public static void Main()
    {
        foreach (var scope in Enum.GetValues<KitPreloadScope>())
        {
            foreach (var ownGear in new[] { false, true })
            {
                var pools = New(scope: scope, ownGear: ownGear);
                KitBundlePreloader.BeginRaidStart();
                CompleteAll(pools);
                var expected = !ownGear && scope != KitPreloadScope.Off ? 1 : 0;
                Check(pools.Requested.Count == expected, "Zombie loading requests only its opening kit, never the catalog");
                Check(KitCatalog.Draws == expected, "own gear and disabled loading do not consume a random draw");
                if (expected == 1)
                {
                    Check(ReferenceEquals(KitCatalog.Reserved, KitCatalog.Opening), "the warmed random opening remains the reserved issued kit");
                    Check(pools.Requested[0].Select(x => x.Value).SequenceEqual(new[] { "opening" }), "opening request contains the actual selected kit");
                }
                Activate();
                // Latched Zombie must win even when the next menu choice differs
                // and the match object has not been created yet.
                ArenaSession.ArmedMode = "deathmatch";
                var controller = new ArenaController();
                At(controller, 1); At(controller, 9); At(controller, 100);
                Check(controller.Starts == 0, "Zombie never schedules optional background catalog work");
                ArenaController.RequestKitWarmup();
                KitCatalog.KitProvider = new object();
                At(controller, 110); At(controller, 112);
                Check(controller.Starts == 0, "Zombie rewarm and transient kit provider cannot re-enable unused work");
                if (!ownGear)
                {
                    var required = KitBundlePreloader.Preload(KitCatalog.Opening);
                    if (expected == 0)
                    {
                        Check(required.MoveNext() && pools.Requested.Count == 1, "loading Off retains required cold opening preload");
                        CompleteAll(pools);
                    }
                    Check(!required.MoveNext(), "required opening uses adopted warm kit or completes its cold load");
                    var refill = KitBundlePreloader.Preload(KitCatalog.Opening);
                    Check(!refill.MoveNext() && pools.Requested.Count == 1, "same-kit living refill reuses warmed templates");
                }
            }
        }

        foreach (var mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "endless", "rounds", "unknown" })
        {
            foreach (var scope in Enum.GetValues<KitPreloadScope>())
            {
                var pools = New(mode, scope);
                KitBundlePreloader.BeginRaidStart();
                CompleteAll(pools);
                int count = scope == KitPreloadScope.Off ? 0 : scope == KitPreloadScope.InitialKit ? 1 : mode == "gungame" ? 3 : 2;
                Check(pools.Requested.Count == count, "non-Zombie loading scope unchanged: " + mode + "/" + scope);
            }
            Activate(mode);
            if (mode == "teamdeathmatch") ArenaMatchService.Current = new Match { Mode = new TeamDeathmatchMode { InitialRosterReady = true } };
            ArenaSession.ArmedMode = "zombie";
            var controller = new ArenaController();
            At(controller, 1); At(controller, 8.9f);
            Check(controller.Starts == 0, "other modes preserve the eight-second delay: " + mode);
            At(controller, 9);
            Check(controller.Starts == 1, "other modes preserve optional background catalog work: " + mode);
        }

        // Exercise the real asynchronous loading/adoption seam. Two later
        // promotions must already be in the opening request even if the raid
        // adopts it before its pools finish; a fourth rung remains cold.
        {
            var pools = New("gungame", KitPreloadScope.InitialKit);
            LadderCatalog.Rungs = new() { Kit("ladder"), Kit("optional"), Kit("refill"), Kit("opening") };
            KitBundlePreloader.BeginRaidStart();
            Check(pools.Requested.Single().Select(k => k.Value).SequenceEqual(new[] { "ladder", "optional", "refill" }), "Gun Game starts one request for the selected first three rungs");
            Check(KitCatalog.Draws == 0, "whole-kit ladder does not draw a random base kit");
            Activate("gungame");
            var next = KitBundlePreloader.Preload(LadderCatalog.Rungs[1]);
            Check(next.MoveNext() && pools.Requested.Count == 1, "early Gun Game promotion joins the opening batch");
            CompleteAll(pools);
            Check(!next.MoveNext(), "second rung is warm after opening completes");
            Check(!KitBundlePreloader.Preload(LadderCatalog.Rungs[2]).MoveNext(), "third rung needs no additional pool load");
            Check(KitBundlePreloader.Preload(LadderCatalog.Rungs[3]).MoveNext() && pools.Requested.Count == 2, "fourth rung retains required cold-load fallback");
            CompleteAll(pools);
        }

        foreach (var mode in new[] { "zombie", "tactical" })
        {
            var pools = New(mode);
            ArenaMenuSelection.CompatibilityTeamArenaActive = true;
            KitBundlePreloader.BeginRaidStart();
            Check(pools.Requested.Count == 0 && KitCatalog.Draws == 0, "protected PFT does not warm or reserve an Arena kit");
            Activate(mode);
            foreach (var finalizing in new[] { false, true })
            {
                PitFireTeamCompat.TeamArenaActive = !finalizing;
                PitFireTeamCompat.IsFinalizing = finalizing;
                var controller = new ArenaController();
                At(controller, 1); At(controller, 100);
                Check(controller.Starts == 0, "protected or finalizing PFT does not schedule catalogs");
            }
        }
        {
            var pools = New();
            ArenaSession.ArmedForNextRaid = false;
            KitBundlePreloader.BeginRaidStart();
            Check(pools.Requested.Count == 0 && KitCatalog.Draws == 0, "ordinary loading does not prewarm or reserve kits");
            var controller = new ArenaController();
            At(controller, 1); At(controller, 100);
            Check(controller.Starts == 0, "ordinary runtime does not warm catalog");
        }
        InFlightAdoptionAndFallback();
        Console.WriteLine($"ZOMBIE_KIT_WARMUP_CHECKS_PASSED: {_assertions} assertions");
    }
    private static void InFlightAdoptionAndFallback()
    {
        var pools = New();
        KitBundlePreloader.BeginRaidStart();
        Activate();
        var required = KitBundlePreloader.Preload(KitCatalog.Opening);
        Check(required.MoveNext() && pools.Requested.Count == 1, "required opening waits for its in-flight batch without duplicate loading");
        CompleteAll(pools);
        Check(!required.MoveNext() && pools.Requested.Count == 1, "one-life opening publishes after adoption without catalog follow-up");
        var future = KitBundlePreloader.Preload(Kit("refill"));
        Check(future.MoveNext() && pools.Requested.Count == 2, "required cold item preload remains available in Zombies");
        CompleteAll(pools);
        Check(!future.MoveNext(), "required cold item finishes normally");

        var failedRequired = KitBundlePreloader.Preload(Kit("optional"));
        Check(failedRequired.MoveNext(), "cold required load starts before native failure");
        pools.Loads.Dequeue().SetException(new InvalidOperationException("required native failure"));
        Check(!failedRequired.MoveNext(), "native failed required load returns to its caller");
        var requiredRetry = KitBundlePreloader.Preload(Kit("optional"));
        Check(requiredRetry.MoveNext(), "failed required load must not be counted warm");
        CompleteAll(pools);
        Check(!requiredRetry.MoveNext(), "required retry warms templates only after success");

        pools = New();
        KitBundlePreloader.BeginRaidStart();
        pools.Loads.Dequeue().SetException(new InvalidOperationException("native test failure"));
        Activate();
        required = KitBundlePreloader.Preload(KitCatalog.Opening);
        Check(required.MoveNext() && pools.Requested.Count == 2, "failed opening preload is retried when required");
        CompleteAll(pools);
        Check(!required.MoveNext(), "fallback retry completes");

        pools = New();
        KitBundlePreloader.BeginRaidStart();
        CompleteAll(pools);
        ArenaSession.ArmEpoch++;
        Activate();
        required = KitBundlePreloader.Preload(KitCatalog.Opening);
        Check(required.MoveNext() && pools.Requested.Count == 2, "different arm does not inherit old warmed pools");
        CompleteAll(pools);
        Check(!required.MoveNext(), "new raid owns its required load");
    }
}
