#nullable disable
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>
/// PFT's hostile-layer prefix wakes standby, then searches for a layer whose
/// name/type denotes combat. Exact native infected layers cannot satisfy that
/// search. Bypass only that prefix while standby needs no wake; the original
/// strategy update, every other patch and PFT's finalizer still execute.
/// </summary>
internal static class ZombiePftFastPath
{
    private const string PftGuid = "xyz.pit.fireteam";
    private const string FilterType = "pitTeam.Patches.HostileNonCombatActiveLayerFilterPatch";
    private static readonly Harmony Harmony = new(Plugin.PluginGuid + ".zombie-pft-fast-path");
    private static bool _enabled, _attempted, _warned;
    private static Func<BotOwner, AIBossPlayer, bool> _isFollower;
    private static Func<List<AICoreLayer<BotLogicDecision>>, int> _listVersion;
    private static ConditionalWeakTable<InfectedLayersStrategy, LayerProof> _proofs = new();
    private static WeakReference _world;
    private static float _nextDiagnostic;
    private static long _calls, _skipped, _standbyFallbacks, _layerChecks, _shapeFallbacks, _errors;

    // ConditionalWeakTable owns no strong root to a dead bot/brain. A fresh
    // world or teardown also discards the whole table, even with GC disabled.
    private sealed class LayerProof
    {
        internal List<AICoreLayer<BotLogicDecision>> List;
        internal int Version;
        internal bool NativeOnly;
    }

    internal static void EnablePatch()
    {
        if (_attempted) return;
        _attempted = true;
        if (!Chainloader.PluginInfos.TryGetValue(PftGuid, out var info)) return;
        try
        {
            var assembly = info.Instance.GetType().Assembly;
            string pluginVersion = info.Metadata.Version.ToString();
            var assemblyVersion = assembly.GetName().Version;
            if (!SupportedIdentity(pluginVersion, assemblyVersion))
                throw new InvalidOperationException("unsupported PitFireTeam plugin/assembly identity");
            var type = assembly.GetType(FilterType, throwOnError: true);
            var state = type.GetNestedType("FilterState", BindingFlags.NonPublic);
            if (state == null) throw new MissingMemberException(FilterType, "FilterState");
            var prefix = type.GetMethod("PatchPrefix", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(object), typeof(AICoreActionResult<BotLogicDecision, CoreActionResultParams>), state.MakeByRefType() }, null);
            if (prefix == null || prefix.ReturnType != typeof(void) || prefix.IsGenericMethod)
                throw new MissingMethodException(FilterType, "PatchPrefix");
            var update = AccessTools.DeclaredMethod(typeof(AICoreStrategy<BotLogicDecision>), "Update",
                new[] { typeof(AICoreActionResult<BotLogicDecision, CoreActionResultParams>) });
            var patches = HarmonyLib.Harmony.GetPatchInfo(update);
            bool registered = false;
            if (patches != null)
                foreach (var patch in patches.Prefixes)
                    if (patch.PatchMethod == prefix) registered = true;
            if (!registered) throw new InvalidOperationException("PFT's expected Update prefix is not installed");
            var follower = assembly.GetType("pitTeam.Modules.BossPlayers", throwOnError: true)
                .GetMethod("IsFollower", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(BotOwner), typeof(AIBossPlayer) }, null);
            if (follower == null || follower.ReturnType != typeof(bool))
                throw new MissingMethodException("PitFireTeam IsFollower contract changed");
            _isFollower = (Func<BotOwner, AIBossPlayer, bool>)Delegate.CreateDelegate(
                typeof(Func<BotOwner, AIBossPlayer, bool>), follower);
            _listVersion = CreateListVersionReader();
            // This target is a NON-generic static method. __0 below is its
            // strategy argument; __instance would be null for the static target.
            // No detour of AICoreStrategy<T> and no foreign unpatch/reordering.
            Harmony.Patch(prefix, prefix: new HarmonyMethod(typeof(ZombiePftFastPath), nameof(Prefix)));
            _enabled = true;
            Plugin.ModLogger.LogDebug("Arena zombie PFT fast path: installed for PitFireTeam " + pluginVersion
                + "; native execution is confirmed by the per-raid calls/skipped counters.");
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static bool SupportedIdentity(string pluginVersion, System.Version assemblyVersion)
        => PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion)
            && PitFireTeamVersions.IsSupportedAssemblyVersion(assemblyVersion)
            && PitFireTeamVersions.IsSupportedIdentity(pluginVersion, assemblyVersion);

    internal static Func<List<AICoreLayer<BotLogicDecision>>, int> CreateListVersionReader()
    {
        var field = typeof(List<AICoreLayer<BotLogicDecision>>).GetField("_version", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(int))
            throw new MissingFieldException("List<T> mutation version is unavailable");
        var list = Expression.Parameter(typeof(List<AICoreLayer<BotLogicDecision>>), "list");
        var reader = Expression.Lambda<Func<List<AICoreLayer<BotLogicDecision>>, int>>(Expression.Field(list, field), list).Compile();
        // Verify the installed CLR's add AND same-count replacement behavior.
        var probe = new List<AICoreLayer<BotLogicDecision>>();
        int empty = reader(probe);
        probe.Add(null);
        int added = reader(probe);
        probe[0] = null;
        if (empty == added || added == reader(probe))
            throw new InvalidOperationException("List<T> mutation tracking differs from the checked contract");
        return reader;
    }

    private static bool Prefix(object __0) => ShouldRunOriginal(__0);

    internal static bool ShouldRunOriginal(object strategy)
    {
        try
        {
            if (!_enabled || Settings.ZombiePftFastPathEnabled?.Value != true
                || !ZombieNativeEventBridge.IsActiveZombieRaid()
                || strategy == null || strategy.GetType() != typeof(InfectedLayersStrategy)
                || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated) return true;
            var world = Singleton<GameWorld>.Instance;
            var game = Singleton<AbstractGame>.Instance as LocalGame;
            if (world == null || game == null || game.Status != GameStatus.Started
                || !ArenaSession.IsExactActiveRaid(game, world)) return true;
            ObserveWorld(world);
            _calls++;
            var brain = (InfectedLayersStrategy)strategy;
            var owner = brain._owner;
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active
                || !ReferenceEquals(owner.Brain?.BaseBrain, brain)) return true;
            var body = owner.GetPlayer;
            if (body == null || !body.IsAI || body.IsYourPlayer || ReferenceEquals(world.MainPlayer, body)
                || body.HealthController?.IsAlive != true || !ReferenceEquals(body.AIData?.BotOwner, owner)
                || string.IsNullOrEmpty(body.ProfileId)
                || !ReferenceEquals(world.GetAlivePlayerByProfileID(body.ProfileId), body)) return true;
            var settings = body.Profile?.Info?.Settings;
            if (settings == null || !GenericInfected(settings.Role)
                || owner.BotFollower?.HaveBoss == true || _isFollower(owner, null)) return true;
            // Preserve PFT's complete hostility predicate and original wake,
            // including exceptions and future state changes during Activate.
            var standby = owner.StandBy;
            if (standby != null && standby.StandByType != BotStandByType.none
                && standby.StandByType != BotStandByType.active)
            {
                _standbyFallbacks++;
                return true;
            }
            var layers = brain._activeLayers;
            if (layers == null || layers.Count == 0) return true;
            // Native SetCanAttack(false) repeatedly activates the same wait
            // layer without deduplication. The number of list entries is not
            // the number of layer types and can grow over a long wave. Validate
            // every entry only when the list changes; never change its contents.
            // PFT obtains its owner from the first usable active layer. Do not
            // substitute the brain owner for a malformed/foreign owner binding.
            if (!(layers[0] is BaseLogicLayerSimple first) || !ReferenceEquals(first._owner, owner)) return true;
            if (!_proofs.TryGetValue(brain, out var proof))
            {
                proof = new LayerProof();
                _proofs.Add(brain, proof);
            }
            int version = _listVersion(layers);
            if (!ReferenceEquals(proof.List, layers) || proof.Version != version)
            {
                _layerChecks++;
                proof.List = layers;
                proof.Version = version;
                proof.NativeOnly = false;
                proof.NativeOnly = NativeLayersOnly(layers) && _listVersion(layers) == version;
            }
            if (!proof.NativeOnly) { _shapeFallbacks++; return true; }
            _skipped++;
            return false;
        }
        catch (Exception ex) { _errors++; Warn(ex); return true; }
    }

    private static bool GenericInfected(WildSpawnType role)
        => role == WildSpawnType.infectedAssault || role == WildSpawnType.infectedCivil
            || role == WildSpawnType.infectedLaborant || role == WildSpawnType.infectedPmc;

    private static bool NativeLayersOnly(List<AICoreLayer<BotLogicDecision>> layers)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            var type = layers[i]?.GetType();
            // Exact types, not `is`: a custom subclass can override Name().
            // All native names (including request enum variants) were checked
            // against PFT 0.10.0/0.10.3's IsCombatLayer predicate.
            if (type != typeof(InfectedAttackLayer) && type != typeof(InfectedTargetLayer)
                && type != typeof(InfectedPatrolLayer) && type != typeof(InfectedWaitLayer)
                && type != typeof(InfectedFightRequestLayer) && type != typeof(InfectedPeacefulRequestLayer)
                && type != typeof(DebugLogicLayer)) return false;
        }
        return true;
    }

    private static void ObserveWorld(GameWorld world)
    {
        if (_world != null && ReferenceEquals(_world.Target, world)) return;
        Reset();
        _world = new WeakReference(world);
        _nextDiagnostic = Time.realtimeSinceStartup + 30f;
    }

    internal static void TickDiagnostics()
    {
        if (!ZombieNativeEventBridge.IsActiveZombieRaid() || !Singleton<GameWorld>.Instantiated)
        {
            if (_world != null) Reset();
            return;
        }
        ObserveWorld(Singleton<GameWorld>.Instance);
        if (Time.realtimeSinceStartup < _nextDiagnostic) return;
        _nextDiagnostic = Time.realtimeSinceStartup + 30f;
        Plugin.ModLogger.LogDebug($"Arena zombie PFT fast path: installed={_enabled} enabled={Settings.ZombiePftFastPathEnabled?.Value == true} calls={_calls} skipped={_skipped} standbyFallbacks={_standbyFallbacks} layerChecks={_layerChecks} shapeFallbacks={_shapeFallbacks} errors={_errors}.");
    }

    internal static void Reset()
    {
        _proofs = new ConditionalWeakTable<InfectedLayersStrategy, LayerProof>();
        _world = null;
        _nextDiagnostic = 0f;
        _calls = _skipped = _standbyFallbacks = _layerChecks = _shapeFallbacks = _errors = 0;
    }

    private static void Warn(Exception ex)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena zombie PFT fast path: original PFT behavior retained on unavailable/invalid calls. "
            + ex.GetBaseException().Message);
    }
}
