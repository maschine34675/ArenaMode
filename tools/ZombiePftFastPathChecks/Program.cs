using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

static class Program
{
    static int assertions, nativeUpdates, originalPrefixes, finalizers;
    static InfectedLayersStrategy brain;
    static BotOwner owner;
    static Player body;
    static GameWorld world;
    static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
    static FieldInfo Field(string name) => typeof(ZombiePftFastPath).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
    static long Count(string name) => (long)Field(name).GetValue(null);
    static void Fresh()
    {
        ZombiePftFastPath.Reset();
        Settings.ZombiePftFastPathEnabled = new();
        ZombieNativeEventBridge.Active = ArenaSession.Exact = true;
        pitTeam.Modules.BossPlayers.Throw = false;
        body = new Player { ProfileId = "infected-current" };
        body.Profile.Info.Settings.Role = WildSpawnType.infectedAssault;
        owner = new BotOwner { GetPlayer = body };
        body.AIData.BotOwner = owner;
        brain = new InfectedLayersStrategy { _owner = owner };
        owner.Brain.BaseBrain = brain;
        brain._activeLayers = new()
        {
            new InfectedFightRequestLayer { _owner = owner },
            new InfectedPeacefulRequestLayer { _owner = owner },
            new InfectedAttackLayer { _owner = owner },
            new InfectedTargetLayer { _owner = owner },
            new InfectedPatrolLayer { _owner = owner }
        };
        world = new GameWorld { MainPlayer = new Player { ProfileId = "local", IsAI = false } };
        world.Alive[body.ProfileId] = body;
        Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = new LocalGame();
        Time.realtimeSinceStartup = 0;
    }
    static void Pass(string name, Action mutate)
    { Fresh(); mutate(); Check(ZombiePftFastPath.ShouldRunOriginal(brain), name); }
    static void UpdateFixture()
    {
        // Use the exact hook registered by EnablePatch and Harmony's argument
        // position convention. The fixture still cannot certify Mono detours.
        Check(Harmony.InstalledHook.GetParameters().Single().Name == "__0", "static target must use __0");
        bool run = (bool)Harmony.InstalledHook.Invoke(null, new object[] { brain });
        if (run)
        {
            originalPrefixes++;
            if (!owner.PftFollower && owner.Hostile && owner.StandBy != null
                && owner.StandBy.StandByType != BotStandByType.none && owner.StandBy.StandByType != BotStandByType.active)
                owner.StandBy.Activate();
        }
        nativeUpdates++;
        finalizers++;
    }
    static void Main()
    {
        foreach (var version in new[] { "0.10.0", "0.10.3" })
            Check(ZombiePftFastPath.SupportedIdentity(version, new Version(version + ".0")), "supported identity " + version);
        foreach (var version in new[] { "0.10.1", "0.10.2", "0.10.4", "1.0.0" })
            Check(!ZombiePftFastPath.SupportedIdentity(version, new Version(version + ".0")), "unknown identity " + version);
        Check(!ZombiePftFastPath.SupportedIdentity("0.10.0", new Version(0, 10, 3, 0)), "metadata/assembly mismatch");
        Check(!ZombiePftFastPath.SupportedIdentity(null, null), "null identity");
        Chainloader.PluginInfos["xyz.pit.fireteam"] = new();
        ZombiePftFastPath.EnablePatch();
        Check(Harmony.InstalledTarget == pitTeam.Patches.HostileNonCombatActiveLayerFilterPatch.PrefixMethod, "private PFT seam installed");
        Fresh();
        UpdateFixture();
        Check(originalPrefixes == 0 && nativeUpdates == 1 && finalizers == 1, "skip only PFT prefix, retain native update and finalizer");
        Check(Count("_skipped") == 1 && Count("_layerChecks") == 1, "initial proof and native counters");
        for (int i = 0; i < 100; i++) Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "cached active infected");
        Check(Count("_layerChecks") == 1, "no repeated list scan");
        foreach (var role in new[] { WildSpawnType.infectedAssault, WildSpawnType.infectedCivil, WildSpawnType.infectedLaborant, WildSpawnType.infectedPmc })
        { Fresh(); body.Profile.Info.Settings.Role = role; Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "generic role " + role); }
        foreach (var standby in new[] { BotStandByType.paused, BotStandByType.goToSave })
        {
            Fresh(); owner.StandBy.StandByType = standby;
            int before = originalPrefixes;
            UpdateFixture();
            Check(originalPrefixes == before + 1 && owner.StandBy.Wakes == 1, "PFT wake preserved " + standby);
            Check(Count("_standbyFallbacks") == 1, "wake counter");
            Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "fast path after wake");
        }
        Fresh(); owner.StandBy.StandByType = BotStandByType.none;
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "none needs no wake");
        Fresh(); owner.StandBy = null;
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "null standby needs no wake");
        Pass("off toggle", () => Settings.ZombiePftFastPathEnabled.Value = false);
        Pass("null toggle", () => Settings.ZombiePftFastPathEnabled = null);
        Pass("ordinary raid", () => ZombieNativeEventBridge.Active = false);
        Pass("wrong raid identity", () => ArenaSession.Exact = false);
        Pass("stopping raid", () => ((LocalGame)Singleton<AbstractGame>.Instance).Status = GameStatus.Stopping);
        Pass("no world", () => Singleton<GameWorld>.Instance = null);
        Pass("destroyed world", () => world.Destroyed = true);
        Pass("nonlocal game", () => Singleton<AbstractGame>.Instance = new AbstractGame());
        Pass("custom derived brain", () => brain = new CustomBrain { _owner = owner, _activeLayers = brain._activeLayers });
        Pass("null brain", () => brain = null);
        Pass("null owner", () => brain._owner = null);
        Pass("dead owner", () => owner.IsDead = true);
        Pass("destroyed owner", () => owner.Destroyed = true);
        Pass("preactive owner", () => owner.BotState = EBotState.PreActive);
        Pass("stale brain", () => owner.Brain.BaseBrain = new InfectedLayersStrategy());
        Pass("null body", () => owner.GetPlayer = null);
        Pass("destroyed body", () => body.Destroyed = true);
        Pass("human body", () => body.IsAI = false);
        Pass("local body flag", () => body.IsYourPlayer = true);
        Pass("local body identity", () => world.MainPlayer = body);
        Pass("dead body", () => body.HealthController.IsAlive = false);
        Pass("owner mismatch", () => body.AIData.BotOwner = new BotOwner());
        Pass("empty profile", () => body.ProfileId = "");
        Pass("stale same-profile body", () => world.Alive[body.ProfileId] = new Player { ProfileId = body.ProfileId });
        Pass("missing profile role", () => body.Profile.Info.Settings = null);
        foreach (var role in new[] { WildSpawnType.infectedTagilla, WildSpawnType.pmcUSEC, WildSpawnType.assault })
            Pass("non-generic role " + role, () => body.Profile.Info.Settings.Role = role);
        Pass("native follower", () => owner.BotFollower.HaveBoss = true);
        Pass("PFT pending/registered follower", () => owner.PftFollower = true);
        Pass("null list", () => brain._activeLayers = null);
        Pass("empty list", () => brain._activeLayers.Clear());
        Pass("unknown custom subclass", () => brain._activeLayers[2] = new CustomCombatLayer { _owner = owner });
        Pass("unknown noncombat layer", () => brain._activeLayers[2] = new UnknownLayer { _owner = owner });
        Pass("null layer", () => brain._activeLayers[2] = null);
        Pass("foreign first owner", () => ((BaseLogicLayerSimple)brain._activeLayers[0])._owner = new BotOwner());
        Fresh();
        var repeatedWait = new InfectedWaitLayer { _owner = owner };
        for (int i = 0; i < 256; i++) brain._activeLayers.Insert(0, repeatedWait);
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "native duplicated wait layers remain eligible");
        Check(brain._activeLayers.Count == 261 && ReferenceEquals(brain._activeLayers[0], brain._activeLayers[255]), "native duplicate list untouched");
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain) && Count("_layerChecks") == 1, "long stable list proof cached");
        brain._activeLayers.Add(new UnknownLayer { _owner = owner });
        Check(ZombiePftFastPath.ShouldRunOriginal(brain), "unknown layer at far end of long native list fails open");
        brain._activeLayers.RemoveAt(brain._activeLayers.Count - 1);
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "removing far unknown layer restores native eligibility");
        var lastIndex = brain._activeLayers.Count - 1;
        var lastLayer = brain._activeLayers[lastIndex];
        brain._activeLayers[lastIndex] = new CustomCombatLayer { _owner = owner };
        Check(ZombiePftFastPath.ShouldRunOriginal(brain), "same-count far custom layer replacement fails open");
        brain._activeLayers[lastIndex] = lastLayer;
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "far layer restoration revalidates long native list");
        Check(brain._activeLayers.Count == 261, "all duplicate entries still intact");
        Fresh(); Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "warm list proof");
        var original = brain._activeLayers[2];
        brain._activeLayers[2] = new CustomCombatLayer { _owner = owner };
        Check(ZombiePftFastPath.ShouldRunOriginal(brain), "same-count replacement invalidates proof");
        Check(Count("_layerChecks") == 2, "replacement rescanned");
        brain._activeLayers[2] = original;
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "restoration rechecks native list");
        brain._activeLayers = new List<AICoreLayer<BotLogicDecision>> { new UnknownLayer { _owner = owner } };
        Check(ZombiePftFastPath.ShouldRunOriginal(brain), "new list identity invalidates proof");
        Fresh(); Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "warm owner proof");
        ((BaseLogicLayerSimple)brain._activeLayers[0])._owner = new BotOwner();
        Check(ZombiePftFastPath.ShouldRunOriginal(brain), "owner field mutation invalidates use without list mutation");
        Fresh(); Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "warm before settings toggle");
        Settings.ZombiePftFastPathEnabled.Value = false;
        Check(ZombiePftFastPath.ShouldRunOriginal(brain), "disabled after cache warm");
        brain._activeLayers.Add(new CustomCombatLayer { _owner = owner });
        Settings.ZombiePftFastPathEnabled.Value = true;
        Check(ZombiePftFastPath.ShouldRunOriginal(brain), "re-enabled does not trust old proof");
        Pass("registry failure fallback", () => pitTeam.Modules.BossPlayers.Throw = true);
        Pass("world lookup failure fallback", () => world.ThrowLookup = true);
        Fresh(); Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "first world accepted");
        var second = new GameWorld(); second.Alive[body.ProfileId] = body;
        Singleton<GameWorld>.Instance = second;
        Check(!ZombiePftFastPath.ShouldRunOriginal(brain), "second world accepted");
        Check(Count("_calls") == 1 && Count("_skipped") == 1 && Count("_layerChecks") == 1, "world change resets counters/cache");
        ZombiePftFastPath.Reset();
        Check(Count("_skipped") == 0 && Field("_world").GetValue(null) == null, "explicit reset clears state");
        Fresh(); ZombiePftFastPath.ShouldRunOriginal(brain);
        int lines = Plugin.ModLogger.Lines.Count;
        for (int i = 0; i < 100; i++) ZombiePftFastPath.TickDiagnostics();
        Check(Plugin.ModLogger.Lines.Count == lines, "no per-frame logs");
        Time.realtimeSinceStartup = 30; ZombiePftFastPath.TickDiagnostics();
        Check(Plugin.ModLogger.Lines.Count == lines + 1 && Plugin.ModLogger.Lines.Last().Contains("skipped=1"), "bounded meaningful diagnostic");
        ZombieNativeEventBridge.Active = false; ZombiePftFastPath.TickDiagnostics();
        Check(Count("_skipped") == 0 && Field("_world").GetValue(null) == null, "inactive cleanup");
        Console.WriteLine($"ZOMBIE_PFT_FAST_PATH_CHECKS_PASSED {assertions} assertions; actual source linked; native Mono detour requires game proof.");
    }
}
