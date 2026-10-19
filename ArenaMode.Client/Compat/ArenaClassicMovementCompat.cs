using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Diagnostics;
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

/// <summary>Keep native movement-state caches and one speed authority on Arena bodies.</summary>
internal static class ArenaClassicMovementCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".ClassicMovementCompat");
    private static readonly ArenaClassicMovementScope Scope = new();
    private static MethodInfo[] _foreign;
    private static FieldInfo _contextPlayer;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _runtimeFault, _reportedNewer;
    private static string _failure, _lastWarning;
    private static int _reported;

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
        if (!Chainloader.PluginInfos.TryGetValue(ArenaClassicMovementContract.PluginGuid, out var info))
        {
            ClearBinding(); _failure = null; _runtimeFault = false;
            return;
        }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Classic Movement has not finished loading");
            if (_foreign != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaClassicMovementContract.Normalize(info.Metadata?.Version))
            {
                ArenaClassicMovementContract.VerifyNativeBindings(_plugin.GetType(), _foreign);
                if (!OwnsPatches()) throw new InvalidOperationException("Classic Movement compatibility hook was removed");
                return;
            }
            ClearBinding();
            var methods = ArenaClassicMovementContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out var version);
            _contextPlayer = AccessTools.Field(typeof(MovementContext), "_player");
            _patchAttempted = true;
            for (int i = 0; i < methods.Length; ++i)
                Patches.Patch(methods[i], prefix: new HarmonyMethod(typeof(ArenaClassicMovementCompat),
                    i == 0 ? nameof(BeforeStateReplacement) : nameof(BeforeSpeedPostfix)) { priority = Priority.First });
            _foreign = methods; _plugin = info.Instance; _version = version;
            if (!OwnsPatches()) throw new InvalidOperationException("Classic Movement compatibility hooks did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Classic Movement: exact-body state and speed guards ready.");
            if (version > ArenaClassicMovementContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                Plugin.ModLogger.LogWarning("Arena Classic Movement " + version + " is newer than reviewed 1.1.3; its movement hook APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }

    private static bool OwnsPatches()
        => _foreign != null && _foreign.Select((method, index) => new { method, index }).All(entry =>
            Harmony.GetPatchInfo(entry.method)?.Prefixes.Any(p => p.owner == Patches.Id
                && p.PatchMethod == typeof(ArenaClassicMovementCompat).GetMethod(entry.index == 0 ? nameof(BeforeStateReplacement) : nameof(BeforeSpeedPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)) == true);

    // Prefix the foreign bool prefix. Its true result lets native GetNewState
    // create/cache its own state. No class widening or state repair is involved.
    private static bool BeforeStateReplacement(MovementContext __0, ref bool __result)
    {
        if (_foreign == null || !ShouldBypass(__0, true)) return true;
        __result = true;
        return false;
    }

    private static bool BeforeSpeedPostfix(MovementContext __0)
        => _foreign == null || !ShouldBypass(__0, false);

    private static bool ShouldBypass(MovementContext context, bool construction)
    {
        bool owned = false;
        try
        {
            var body = context == null ? null : _contextPlayer.GetValue(context) as Player;
            if (body is not LocalPlayer || body.Profile == null) return false;
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
            // BaseLocalGame.Run constructs its local body BEFORE BotsController.Init
            // publishes IBotGame. This exception is exact constructor ownership,
            // not a missing-singleton permission for arbitrary loading bodies.
            bool initialLocal = construction && game != null && game.Status == GameStatus.Running
                && ReferenceEquals(game.LocalPlayer, null) && body.IsYourPlayer
                && ReferenceEquals(body.Profile, game.Profile) && ReferenceEquals(body.MovementContext, null);
            bool exactWorld = !ReferenceEquals(game, null) && !ReferenceEquals(world, null)
                && ReferenceEquals(game.GameWorld, world) && (initialLocal
                    || (Singleton<IBotGame>.Instantiated && ReferenceEquals(Singleton<IBotGame>.Instance, game)));
            var settings = exactWorld ? LocalGameAccessor.GetLocalRaidSettings(game) : null;
            bool exactRaid = ArenaClassicMovementScope.OwnsRaid(exactWorld,
                game != null && (game.Status == GameStatus.Running || game.Status == GameStatus.Starting || game.Status == GameStatus.Started),
                settings?.playerSide == ESideType.Pmc, world?.LocationId, settings?.location,
                exactWorld && ArenaSession.IsExactActiveRaid(game, world), ArenaSession.ArmedForNextRaid,
                ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap, ArenaServerClient.PendingArmToken, settings?.serverId);
            owned = Scope.Owns(context, body, body.GameWorld, body.MovementContext, world, true, construction, exactRaid);
            if (owned && ArenaLog.DeveloperEnabled)
            {
                int bit = 1 << ((body.IsYourPlayer ? 0 : 2) + (construction ? 0 : 1));
                if ((_reported & bit) == 0)
                {
                    _reported |= bit;
                    ArenaLog.Developer("Classic Movement: native " + (construction ? "state cache" : "speed update")
                        + " retained for Arena " + (body.IsYourPlayer ? "local body" : "bot body")
                        + "; loading=" + !ArenaSession.IsExactActiveRaid(game, world) + ".");
                }
            }
            return owned;
        }
        catch (Exception ex)
        {
            _runtimeFault = true; Reject(ex);
            return owned;
        }
    }

    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely isolate Classic Movement: " + ex.GetBaseException().Message
            + ". Update or remove Classic Movement, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; Plugin.ModLogger.LogWarning(_failure);
    }

    private static void ClearBinding()
    {
        _foreign = null; _contextPlayer = null; _plugin = null; _version = null; _reported = 0; Scope.Clear();
        if (!_patchAttempted) return;
        _patchAttempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }

    internal static void Shutdown()
    {
        ClearBinding(); _initialized = _runtimeFault = false; _failure = _lastWarning = null;
    }
}
