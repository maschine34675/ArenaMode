using ArenaMode.Compat;
using ArenaMode.Shared;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;

static class Checks
{
    private static int _assertions;
    private const string ProfileId = "012345678901234567890123";
    private static readonly string Token = new string('a', 32);
    private static readonly string End = Protocol.BuildNativeEndIdentity("native-raid", Token);
    private static readonly EftClientBackendSession Session = new() { Profile = new() { Id = ProfileId } };

    static void Assert(bool condition, string message)
    { _assertions++; if (!condition) throw new InvalidOperationException(message); }

    static void Install()
    {
        ArenaMoreBotsCompatibility.Shutdown();
        Chainloader.PluginInfos[ArenaMoreBotsCompatibility.PluginId] = new PluginInfo
        {
            Instance = new MoreBotsAPI.Plugin(),
            Metadata = new BepInPlugin(ArenaMoreBotsCompatibility.PluginId, "MoreBotsAPI", "2.1.1")
        };
        ArenaMoreBotsCompatibility.Initialize();
    }

    static LocalRaidSettings Ordinary(ESideType side = ESideType.Pmc)
        => new() { serverId = Guid.NewGuid().ToString("N"), location = "factory4_day", playerSide = side };
    static void World(LocalRaidSettings settings)
    {
        var world = new GameWorld();
        Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = new LocalGame { GameWorld = world, Settings = settings };
    }
    static BotOwner Bot(string marker, bool register = true)
    {
        var bot = new BotOwner { GetPlayer = new Player { ProfileId = Guid.NewGuid().ToString("N") } };
        bot.GetPlayer.AIData.BotOwner = bot;
        bot.SpawnProfileData.SpawnParams.Id_spawn = marker;
        if (register) Singleton<GameWorld>.Instance.Registered[bot.GetPlayer.ProfileId] = bot.GetPlayer;
        return bot;
    }
    static void AssertInert(LocalRaidSettings settings, string context)
    {
        World(settings);
        Assert(ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId), context + ": producer original");
        Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(settings), context + ": end original");
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot(null)), context + ": hunt original");
        Assert(ArenaMoreBotsCompatibility.BeforeEnemyRevenge(), context + ": relationship original");
    }

    static void Main()
    {
        foreach (var spawn in OwnedSpawns.BuildAll())
        {
            Assert(spawn.Id_spawn != null, "Arena-owned spawn marker is never null");
            Assert(!spawn.Id_spawn.ToLowerInvariant().Contains("hunt"), "Arena-owned spawn never opts into faction hunts");
        }
        Assert(ArenaMoreBotsCompatibility.GetRejectionReason() == null, "absent API does not block Arena");
        Install();
        Assert(ArenaMoreBotsCompatibility.GetRejectionReason() == null, "reviewed API patches installed");
        foreach (var side in new[] { ESideType.Pmc, ESideType.Savage })
        {
            var ordinary = Ordinary(side); World(ordinary);
            Assert(ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId), "ordinary PMC/Scav producer unchanged");
            Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(ordinary), "ordinary PMC/Scav end unchanged");
            Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot(null)), "ordinary PMC/Scav null spawn stays original");
            Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot("hunt")), "ordinary PMC/Scav hunt stays original");
            Assert(ArenaMoreBotsCompatibility.BeforeEnemyRevenge(), "ordinary PMC/Scav revenge relationship unchanged");
        }

        var arena = new LocalRaidSettings { serverId = Protocol.BuildNativeStartIdentity(Token), location = "factory4_day", playerSide = ESideType.Pmc };
        Assert(ArenaMoreBotsCompatibility.BindProtectedStart(Session, arena, new LocalSettings { serverId = End }, Token), "exact protected start bound");
        Assert(!ArenaMoreBotsCompatibility.BindProtectedStart(Session, Ordinary(), new LocalSettings { serverId = End }, Token), "untagged start rejected");
        Assert(!ArenaMoreBotsCompatibility.BindProtectedStart(Session, arena, new LocalSettings { serverId = "wrong" }, Token), "wrong returned identity rejected");
        arena.serverId = End; World(arena);
        Assert(!ArenaMoreBotsCompatibility.BeforeEnemyRevenge(), "Arena team relationship cannot be overridden by revenge");
        Assert(!ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot(null)), "confirmed Arena null marker bypasses foreign hunt handler");
        Assert(!ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot("")), "confirmed Arena empty marker bypasses foreign hunt handler");
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot("hunt")), "explicit Arena hunt remains foreign-owned");
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot("foreign-marker")), "custom Arena marker remains foreign-owned");
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(null), "absent bot not claimed");
        var unregistered = Bot(null, false);
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(unregistered), "unregistered bot not claimed");
        Singleton<GameWorld>.Instance.Registered[unregistered.GetPlayer.ProfileId] = new Player { ProfileId = unregistered.GetPlayer.ProfileId };
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(unregistered), "same profile with different registered body not claimed");
        var changedOwner = Bot(null); changedOwner.GetPlayer.AIData.BotOwner = new BotOwner();
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(changedOwner), "different body owner not claimed");
        var human = Bot(null); human.GetPlayer.IsAI = false;
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(human), "human not claimed by hunt safety");
        var noParams = Bot(null); noParams.SpawnProfileData.SpawnParams = null;
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(noParams), "unrelated incomplete profile data not claimed");

        // A saved ordinary queue must survive the complete Arena lifecycle.
        var queue = new Dictionary<string, int> { ["ordinary-pending"] = 2 };
        if (ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId)) queue["arena-kill"] = 1;
        Assert(!queue.ContainsKey("arena-kill"), "Arena kill never reaches persistent revenge queue");
        Assert(queue["ordinary-pending"] == 2, "preexisting ordinary queue preserved");
        Assert(ArenaMoreBotsCompatibility.BeforeRevenge("foreign-profile"), "foreign profile producer unchanged");

        var sameIdentityDifferentSettings = Ordinary(); sameIdentityDifferentSettings.serverId = End;
        World(sameIdentityDifferentSettings);
        Assert(ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId), "different settings instance cannot borrow Arena ownership");
        Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(sameIdentityDifferentSettings), "copied end identity is not the bound request");
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot(null)), "copied settings cannot claim bot activation");
        Assert(ArenaMoreBotsCompatibility.BeforeEnemyRevenge(), "copied settings cannot claim relationship");
        World(arena);
        Singleton<GameWorld>.Instance = new GameWorld();
        Assert(ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId), "wrong world cannot borrow Arena ownership");
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot(null)), "wrong world cannot claim bot activation");
        Assert(ArenaMoreBotsCompatibility.BeforeEnemyRevenge(), "wrong world cannot claim relationship");
        World(arena);
        arena.serverId = "wrong";
        Assert(ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId), "changed request identity not suppressed");
        Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(arena), "wrong end identity unchanged");
        arena.serverId = End;
        arena.location = "woods";
        Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(arena), "wrong map end unchanged");
        arena.location = "factory4_day";
        arena.playerSide = ESideType.Savage;
        Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(arena), "wrong side end unchanged");
        arena.playerSide = ESideType.Pmc;

        // No active main player exists in the fixture: body replacement must
        // not permit grenade revenge through a null MainPlayer window.
        Assert(!ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId), "respawn body absence retains exact raid protection");
        Singleton<AbstractGame>.Instance = null;
        Singleton<GameWorld>.Instance = null;
        Assert(!ArenaMoreBotsCompatibility.BeforeRaidEnd(arena), "end remains protected after live-world reset");
        Assert(!ArenaMoreBotsCompatibility.BeforeRaidEnd(arena), "repeated Arena end does not persist revenge");
        Assert(ArenaMoreBotsCompatibility.BeforeEnemyRevenge(), "no current world does not claim relationship");

        var next = Ordinary(); World(next);
        if (ArenaMoreBotsCompatibility.BeforeRevenge(ProfileId)) queue["next-ordinary"] = 3;
        Assert(queue["next-ordinary"] == 3, "next ordinary kills unchanged");
        if (ArenaMoreBotsCompatibility.BeforeRaidEnd(arena)) queue.Clear();
        Assert(queue.ContainsKey("next-ordinary") && queue["ordinary-pending"] == 2,
            "late Arena end cannot drain a later ordinary queue");
        Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(next), "next ordinary end unchanged");
        Assert(ArenaMoreBotsCompatibility.BeforeHuntCreated(Bot(null)), "next ordinary bot activation unchanged");
        Assert(ArenaMoreBotsCompatibility.BeforeEnemyRevenge(), "next ordinary revenge relationship unchanged");
        Assert(!queue.ContainsKey("arena-kill"), "no deferred Arena revenge reaches next ordinary raid");
        Assert(ArenaMoreBotsCompatibility.BeforeRaidEnd(null), "null end not claimed");

        // A failed internal Harmony rollback must neither escape Initialize/
        // Shutdown nor leave surviving prefixes active on ordinary raids.
        World(arena);
        var warningCount = ArenaMode.Plugin.ModLogger.Warnings.Count;
        Harmony.RemovePrefix("BeforeEnemyRevenge");
        Harmony.RejectPrefix = "BeforeEnemyRevenge";
        Harmony.ThrowUnpatch = true;
        ArenaMoreBotsCompatibility.Initialize();
        Assert(ArenaMoreBotsCompatibility.GetRejectionReason() != null, "failed unpatch retains Arena refusal");
        Assert(Harmony.GetPatchInfo(typeof(MoreBotsAPI.Components.FactionManager).GetMethod("UpdateRevengeByRole"))?.Prefixes.Count > 0,
            "failed unpatch fixture leaves actual prefix receipts");
        AssertInert(arena, "failed install with surviving hooks");
        AssertInert(Ordinary(), "ordinary after failed rollback");
        AssertInert(Ordinary(ESideType.Savage), "Scav after failed rollback");
        ArenaMoreBotsCompatibility.Initialize();
        ArenaMoreBotsCompatibility.Shutdown();
        ArenaMoreBotsCompatibility.Shutdown();
        Assert(ArenaMode.Plugin.ModLogger.Warnings.Count == warningCount + 2, "cleanup and refusal warnings remain bounded");
        Harmony.ThrowUnpatch = false;
        Harmony.RejectPrefix = null;
        Install();
        arena.serverId = Protocol.BuildNativeStartIdentity(Token);
        Assert(ArenaMoreBotsCompatibility.BindProtectedStart(Session, arena, new LocalSettings { serverId = End }, Token), "repair can bind new protected request");
        arena.serverId = End; World(arena);
        Assert(!ArenaMoreBotsCompatibility.BeforeRaidEnd(arena), "repaired hooks protect before shutdown");
        Harmony.ThrowUnpatch = true;
        ArenaMoreBotsCompatibility.Shutdown();
        AssertInert(arena, "working adapter shutdown with failed unpatch");
        AssertInert(Ordinary(), "ordinary after shutdown unpatch failure");
        Assert(ArenaMode.Plugin.ModLogger.Warnings.Count == warningCount + 2, "shutdown cleanup warning stays bounded");
        Harmony.ThrowUnpatch = false;
        ArenaMoreBotsCompatibility.Shutdown();

        // The shape of a reflected API is not enough: a missing patch receipt
        // must reject Arena, and a partial install must be rolled back.
        ArenaMoreBotsCompatibility.Shutdown();
        Harmony.RejectInstallation = true;
        ArenaMoreBotsCompatibility.Initialize();
        Assert(ArenaMoreBotsCompatibility.GetRejectionReason() != null, "uninstalled protection rejects Arena");
        Harmony.RejectInstallation = false;
        foreach (var name in new[] { "BeforeRevenge", "BeforeRaidEnd", "BeforeHuntCreated", "BeforeEnemyRevenge" })
        {
            Harmony.RejectPrefix = name;
            Install();
            Assert(ArenaMoreBotsCompatibility.GetRejectionReason() != null, "partial protection rejects Arena: " + name);
            Assert(Harmony.GetPatchInfo(typeof(MoreBotsAPI.Components.FactionManager).GetMethod("UpdateRevengeByRole"))?.Prefixes.Count == 0,
                "partial protection rolls back all hooks: " + name);
        }
        Harmony.RejectPrefix = null;
        Install();
        Chainloader.PluginInfos[ArenaMoreBotsCompatibility.PluginId].Metadata = new BepInPlugin(ArenaMoreBotsCompatibility.PluginId, "MoreBotsAPI", "1.0.0");
        ArenaMoreBotsCompatibility.Shutdown();
        Assert(ArenaMoreBotsCompatibility.GetRejectionReason() != null, "old version rejects Arena");
        Install();
        Chainloader.PluginInfos[ArenaMoreBotsCompatibility.PluginId].Metadata = new BepInPlugin(ArenaMoreBotsCompatibility.PluginId, "MoreBotsAPI", "2.1.2");
        ArenaMoreBotsCompatibility.Shutdown();
        Assert(ArenaMoreBotsCompatibility.GetRejectionReason() != null, "metadata mismatch rejects Arena");
        Chainloader.PluginInfos.Clear();
        ArenaMoreBotsCompatibility.Shutdown();
        Assert(ArenaMoreBotsCompatibility.GetRejectionReason() == null, "removing API restores optional absence");
        Console.WriteLine($"MOREBOTS_RUNTIME_CHECKS_PASSED ({_assertions})");
    }
}

public sealed class BotSpawnParams { public string Id_spawn; }
