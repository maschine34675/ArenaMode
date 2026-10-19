using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Pause RTD's shared destructive seam, never its saved settings or timer.</summary>
internal static class ArenaRemoveTheDeadCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".RemoveTheDeadCompat");
    private static MethodInfo _remove;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _runtimeFault, _reportedNewer;
    private static string _failure, _lastWarning;
    private static WeakReference<GameWorld> _reportedWorld;

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Refresh();
    }

    internal static string GetRejectionReason()
    {
        if (!_initialized) Initialize();
        else Refresh();
        return _failure;
    }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaRemoveTheDeadContract.PluginGuid, out var info))
        {
            ClearBinding();
            _failure = null;
            _runtimeFault = false;
            return;
        }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Remove The Dead has not finished loading");
            if (_remove != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaRemoveTheDeadContract.Normalize(info.Metadata?.Version))
            {
                if (!OwnsPatch()) throw new InvalidOperationException("Remove The Dead compatibility hook was removed");
                return;
            }
            ClearBinding();
            var method = ArenaRemoveTheDeadContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            var settings = AccessTools.Field(typeof(LocalGame), "_raidSettings");
            if (settings == null || settings.IsStatic || settings.FieldType != typeof(LocalRaidSettings))
                throw new MissingFieldException("LocalGame._raidSettings changed");
            _patchAttempted = true;
            Patches.Patch(method, prefix: new HarmonyMethod(typeof(ArenaRemoveTheDeadCompat), nameof(BeforeRemove))
                { priority = Priority.First });
            _remove = method;
            _plugin = info.Instance;
            _version = version;
            if (!OwnsPatch()) throw new InvalidOperationException("Remove The Dead compatibility hook did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Remove The Dead: shared automatic/manual cleanup guard ready.");
            if (version > ArenaRemoveTheDeadContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                Plugin.ModLogger.LogWarning("Arena Remove The Dead " + version
                    + " is newer than reviewed 4.1.3; the cleanup entry chain was checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            ClearBinding();
            Reject(ex);
        }
    }

    private static bool OwnsPatch()
        => _remove != null && Harmony.GetPatchInfo(_remove)?.Prefixes.Any(p => p.owner == Patches.Id
            && p.PatchMethod == typeof(ArenaRemoveTheDeadCompat).GetMethod(nameof(BeforeRemove), BindingFlags.Static | BindingFlags.NonPublic)) == true;

    private static bool BeforeRemove()
    {
        if (_remove == null) return true;
        bool exactActive = false;
        try
        {
            if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated || !Singleton<IBotGame>.Instantiated
                || Singleton<AbstractGame>.Instance is not LocalGame game) return true;
            var world = Singleton<GameWorld>.Instance;
            if (game == null || world == null || !ReferenceEquals(game.GameWorld, world)
                || !ReferenceEquals(Singleton<IBotGame>.Instance, game)) return true;
            exactActive = ArenaSession.IsExactActiveRaid(game, world);
            var settings = LocalGameAccessor.GetLocalRaidSettings(game);
            bool pause = ArenaRemoveTheDeadScope.ShouldPause(true, settings?.playerSide == ESideType.Pmc,
                world.LocationId, settings?.location, exactActive, game.Status == GameStatus.Started,
                ArenaSession.ArmedForNextRaid, ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap,
                ArenaServerClient.PendingArmToken, settings?.serverId);
            if (!pause) return true;
            if (_reportedWorld == null || !_reportedWorld.TryGetTarget(out var previous) || !ReferenceEquals(previous, world))
            {
                _reportedWorld = new WeakReference<GameWorld>(world);
                Plugin.ModLogger.LogInfo("Arena: Remove The Dead cleanup is paused for this match; Arena manages its corpses and pickups. Ordinary raids remain unchanged.");
            }
            return false;
        }
        catch (Exception ex)
        {
            _runtimeFault = true;
            Reject(ex);
            // An unreadable ordinary world grants no authority. If the exact
            // active Arena pair was already proved, never destroy its retained
            // bodies just because a subsequent diagnostic/settings read failed.
            return !exactActive;
        }
    }

    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely pause Remove The Dead: " + ex.GetBaseException().Message
            + ". Update or remove Remove The Dead, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure;
        Plugin.ModLogger.LogWarning(_failure);
    }

    private static void ClearBinding()
    {
        _remove = null; // revoke before unpatch, including an internal Harmony failure
        _plugin = null;
        _version = null;
        _reportedWorld = null;
        if (!_patchAttempted) return;
        _patchAttempted = false;
        try { Patches.UnpatchSelf(); }
        catch (Exception ex) { Reject(ex); }
    }

    internal static void Shutdown()
    {
        ClearBinding();
        _initialized = _runtimeFault = false;
        _failure = _lastWarning = null;
    }
}
