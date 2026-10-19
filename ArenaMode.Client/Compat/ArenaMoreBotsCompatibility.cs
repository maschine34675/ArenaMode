using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Compat;

/// <summary>
/// Optional MoreBotsAPI boundary. No foreign source or binaries are included.
/// Suppress Arena's revenge producer and its exact end sender, not ordinary
/// requests or the shared queue (which may contain a prior ordinary retry).
/// </summary>
internal static class ArenaMoreBotsCompatibility
{
    internal const string PluginId = "com.morebotsapi.tacticaltoaster";
    private const string PatchId = "com.maschine.ArenaMode.MoreBots";
    private static readonly System.Version ReviewedVersion = new(2, 1, 1);
    private static readonly Harmony Patches = new(PatchId);
    private static ArenaMoreBotsRaidPolicy _raids = new();
    private static Assembly _assembly;
    private static MethodInfo _update, _end, _hunt, _enemy;
    private static bool _ready, _warned, _cleanupWarned;
    private static string _failure;

    internal static void Initialize()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginId, out var plugin)) return;
        if (_ready && ReferenceEquals(_assembly, plugin.Instance?.GetType().Assembly)
            && HasOurPrefix(_update, nameof(BeforeRevenge)) && HasOurPrefix(_end, nameof(BeforeRaidEnd))
            && HasOurPrefix(_hunt, nameof(BeforeHuntCreated)) && HasOurPrefix(_enemy, nameof(BeforeEnemyRevenge))) return;
        try
        {
            var type = plugin.Instance?.GetType();
            var version = plugin.Metadata?.Version;
            if (type?.FullName != "MoreBotsAPI.Plugin" || type.Assembly.GetName().Name != "MoreBotsPlugin"
                || plugin.Metadata.GUID != PluginId || version == null || version < ReviewedVersion)
                throw new InvalidOperationException("MoreBotsAPI identity/version is not the reviewed 2.1.1+ contract");
            var declared = type.GetCustomAttributes(typeof(BepInPlugin), false).Cast<BepInPlugin>().SingleOrDefault();
            if (declared?.GUID != PluginId || declared.Version != version)
                throw new InvalidOperationException("MoreBotsAPI plugin metadata disagrees with its loaded assembly");
            _assembly = type.Assembly;
            var manager = _assembly.GetType("MoreBotsAPI.Components.FactionManager", true);
            var endPatch = _assembly.GetType("MoreBotsAPI.Patches.FactionRaidEndPatch", true);
            var huntManager = _assembly.GetType("MoreBotsAPI.Components.HuntManager", true);
            var enemyPatch = _assembly.GetType("MoreBotsAPI.Patches.BotsGroupIsPlayerEnemyPatch", true);
            var update = RequiredMethod(manager, "UpdateRevengeByRole", false,
                typeof(WildSpawnType), typeof(string));
            var end = RequiredMethod(endPatch, "PatchPostfix", true,
                typeof(LocalRaidSettings), typeof(SessionResult), typeof(FlatItem[]),
                typeof(Dictionary<string, FlatItem[]>));
            var hunt = RequiredMethod(huntManager, "OnBotCreated", false, typeof(BotOwner));
            var enemy = RequiredMethod(enemyPatch, "PatchPostfix", true,
                typeof(bool).MakeByRefType(), typeof(BotsGroup), typeof(IPlayer));
            RequiredMethod(manager, "SendRevenges", false);
            var queue = manager.GetField("UpdateRevengeFactions", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (queue?.FieldType != typeof(Dictionary<string, List<string>>))
                throw new InvalidOperationException("MoreBotsAPI revenge queue contract changed");
            Patches.Patch(update, prefix: Prefix(nameof(BeforeRevenge)));
            Patches.Patch(end, prefix: Prefix(nameof(BeforeRaidEnd)));
            Patches.Patch(hunt, prefix: Prefix(nameof(BeforeHuntCreated)));
            Patches.Patch(enemy, prefix: Prefix(nameof(BeforeEnemyRevenge)));
            if (!HasOurPrefix(update, nameof(BeforeRevenge)) || !HasOurPrefix(end, nameof(BeforeRaidEnd))
                || !HasOurPrefix(hunt, nameof(BeforeHuntCreated)) || !HasOurPrefix(enemy, nameof(BeforeEnemyRevenge)))
                throw new InvalidOperationException("MoreBotsAPI protection patches could not be verified");
            _update = update;
            _end = end;
            _hunt = hunt;
            _enemy = enemy;
            _ready = true;
            _failure = null;
            if (version > ReviewedVersion && !_warned)
            {
                _warned = true;
                Plugin.ModLogger.LogWarning($"Arena: MoreBotsAPI {version} is newer than reviewed 2.1.1; its required API was validated, gameplay compatibility is unverified.");
            }
            Plugin.ModLogger.LogDebug($"Arena MoreBotsAPI {version}: exact-raid profile, spawn and relationship protection ready.");
        }
        catch (Exception ex)
        {
            _ready = false;
            _failure = "Arena is unavailable because MoreBotsAPI profile protection could not be installed: " + ex.GetBaseException().Message;
            TryUnpatch();
            if (!_warned) { _warned = true; Plugin.ModLogger.LogWarning(_failure); }
        }
    }

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.ContainsKey(PluginId)) return null;
        Initialize();
        return _ready ? null : _failure ?? "Arena is unavailable because MoreBotsAPI protection is not ready.";
    }

    // Called only after the server's exact local-start response was validated,
    // before native GamePrepare can construct bots or hand over player control.
    internal static bool BindProtectedStart(EftClientBackendSession session,
        LocalRaidSettings settings, LocalSettings response, string armToken)
    {
        if (!Chainloader.PluginInfos.ContainsKey(PluginId)) return true;
        return GetRejectionReason() == null && session?.Profile != null && settings != null
            && _raids.TryBindProtectedRaid(settings, settings.serverId, response?.serverId,
                armToken, settings.location, session.Profile.Id, (int)settings.playerSide);
    }

    internal static bool BeforeRaidEnd(LocalRaidSettings settings)
        => !_ready || settings == null || !_raids.OwnsEnd(settings, settings.serverId,
            settings.location, (int)settings.playerSide);

    internal static bool BeforeRevenge(string playerID)
    {
        if (!TryGetProtectedWorld(out _, out var settings)) return true;
        return !_raids.OwnsRevenge(settings, settings.serverId,
            settings.location, (int)settings.playerSide, playerID);
    }

    internal static bool BeforeHuntCreated(BotOwner bot)
    {
        // PFT constructs its own BotSpawnParams without Id_spawn. Never edit
        // those foreign objects or interpret a nonempty custom marker for it.
        if (bot == null || bot.SpawnProfileData?.SpawnParams == null
            || !string.IsNullOrEmpty(bot.SpawnProfileData.SpawnParams.Id_spawn)
            || !TryGetProtectedWorld(out var world, out _)) return true;
        var player = bot.GetPlayer;
        // Player.Init registers the real body before BotCreatorClient creates
        // BotOwner and invokes native OnBotCreated; Active/AfterCreation is too
        // late. The profile ID alone is never enough across round respawns.
        return player == null || !player.IsAI || string.IsNullOrEmpty(player.ProfileId)
            || !ReferenceEquals(player.AIData?.BotOwner, bot)
            || !ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player);
    }

    // Arena owns its faction/team rules. Leave the native/Arena relationship
    // result intact; ordinary raids still run all MoreBots revenge overrides.
    internal static bool BeforeEnemyRevenge()
        => !TryGetProtectedWorld(out _, out _);

    private static bool TryGetProtectedWorld(out GameWorld world, out LocalRaidSettings settings)
    {
        world = null;
        settings = null;
        if (!_ready || !Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated)
            return false;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        world = Singleton<GameWorld>.Instance;
        if (game == null || world == null || !ReferenceEquals(game.GameWorld, world)) return false;
        settings = LocalGameAccessor.GetLocalRaidSettings(game);
        return settings != null && _raids.OwnsEnd(settings, settings.serverId,
            settings.location, (int)settings.playerSide);
    }

    private static HarmonyMethod Prefix(string name)
        => new(typeof(ArenaMoreBotsCompatibility).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic))
            { priority = Priority.First };

    private static bool HasOurPrefix(MethodInfo target, string name)
        => Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.owner == PatchId
            && p.PatchMethod.DeclaringType == typeof(ArenaMoreBotsCompatibility)
            && p.PatchMethod.Name == name) == true;

    private static MethodInfo RequiredMethod(Type type, string name, bool isStatic, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.DeclaredOnly
            | (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, args, null);
        if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters
            || method.DeclaringType.Assembly != _assembly)
            throw new InvalidOperationException("MoreBotsAPI method contract changed: " + name);
        return method;
    }

    internal static void Shutdown()
    {
        _ready = false;
        TryUnpatch();
        _assembly = null;
        _update = _end = _hunt = _enemy = null;
        _raids = new ArenaMoreBotsRaidPolicy();
    }

    private static void TryUnpatch()
    {
        // Both callers revoke readiness first. Any surviving prefix is inert;
        // an internal Harmony failure must not abort ordinary startup/cleanup
        // or replace the original Arena admission refusal.
        try { Patches.UnpatchSelf(); }
        catch (Exception cleanupError)
        {
            if (!_cleanupWarned)
            {
                _cleanupWarned = true;
                Plugin.ModLogger.LogWarning("Arena MoreBotsAPI hook cleanup failed; surviving hooks are inactive: "
                    + cleanupError.GetBaseException().Message);
            }
        }
    }
}
