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

/// <summary>
/// AI Limit's OnGameStarted prefix calls Enable before Arena's started postfix.
/// Suppress only its component construction in the exact protected Arena raid.
/// The limiter's configuration, global patches and ordinary raids stay intact.
/// </summary>
internal static class ArenaAiLimitCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".AiLimitCompat");
    private static MethodInfo _enable;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static string _failure, _lastWarning;
    private static bool _initialized, _reportedNewerVersion, _runtimeFault, _patchAttempted;
    private static WeakReference<GameWorld> _reportedWorld;

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Refresh();
    }

    /// <summary>Menu and final pre-arm gate. No server calls or saved-setting edits.</summary>
    internal static string GetRejectionReason()
    {
        if (!_initialized) Initialize();
        else Refresh();
        return _failure;
    }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaAiLimitContract.PluginGuid, out var info))
        {
            ClearBinding();
            _failure = null;
            _runtimeFault = false;
            return;
        }
        // A runtime fault is not repaired merely by reopening the menu.
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("AI Limit has not finished loading");
            if (_enable != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaAiLimitContract.Normalize(info.Metadata?.Version))
            {
                if (!OwnsPatch()) throw new InvalidOperationException("AI Limit compatibility hook was removed");
                return;
            }

            ClearBinding();
            var target = ArenaAiLimitContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            // Fail before accepting Arena if Core cannot read the same native
            // settings field used by the protected start. No reflection guesses.
            var settings = AccessTools.Field(typeof(LocalGame), "_raidSettings");
            if (settings == null || settings.IsStatic || settings.FieldType != typeof(LocalRaidSettings))
                throw new MissingFieldException("LocalGame._raidSettings changed");
            _patchAttempted = true;
            Patches.Patch(target, prefix: new HarmonyMethod(typeof(ArenaAiLimitCompat), nameof(BeforeEnable))
                { priority = Priority.First });
            _enable = target;
            _plugin = info.Instance;
            _version = version;
            if (!OwnsPatch()) throw new InvalidOperationException("AI Limit compatibility hook did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena AI Limit: exact-raid component creation guard ready.");
            if (version > ArenaAiLimitContract.ReviewedVersion && !_reportedNewerVersion)
            {
                _reportedNewerVersion = true;
                Plugin.ModLogger.LogWarning("Arena AI Limit " + version
                    + " is newer than the reviewed 1.9.2; the checked entry point is supported, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            ClearBinding();
            Reject(ex);
        }
    }

    private static bool OwnsPatch()
        => _enable != null && Harmony.GetPatchInfo(_enable)?.Prefixes.Any(p => p.owner == Patches.Id
            && p.PatchMethod == typeof(ArenaAiLimitCompat).GetMethod(nameof(BeforeEnable), BindingFlags.Static | BindingFlags.NonPublic)) == true;

    private static bool BeforeEnable()
    {
        if (_enable == null || _runtimeFault) return true;
        try
        {
            if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated || !Singleton<IBotGame>.Instantiated
                || Singleton<AbstractGame>.Instance is not LocalGame game) return true;
            var world = Singleton<GameWorld>.Instance;
            if (game == null || world == null || !ReferenceEquals(game.GameWorld, world)
                || !ReferenceEquals(Singleton<IBotGame>.Instance, game)) return true;
            var settings = LocalGameAccessor.GetLocalRaidSettings(game);
            bool suppress = ArenaAiLimitScope.ShouldSuppress(
                exactLocalWorld: true,
                nativeRaidLive: game.Status == GameStatus.Started,
                pmc: settings?.playerSide == ESideType.Pmc,
                worldMap: world.LocationId,
                settingsMap: settings?.location,
                exactActiveRaid: ArenaSession.IsExactActiveRaid(game, world),
                armed: ArenaSession.ArmedForNextRaid,
                pendingArm: ArenaServerClient.HasPendingArm,
                armedMap: ArenaSession.ArmedMap,
                pendingToken: ArenaServerClient.PendingArmToken,
                nativeIdentity: settings?.serverId);
            if (!suppress) return true;
            if (_reportedWorld == null || !_reportedWorld.TryGetTarget(out var previous) || !ReferenceEquals(previous, world))
            {
                _reportedWorld = new WeakReference<GameWorld>(world);
                Plugin.ModLogger.LogInfo("Arena: AI Limit is paused for this match; all Arena participants remain active. Ordinary raids keep AI Limit unchanged.");
            }
            return false;
        }
        catch (Exception ex)
        {
            // Unknown identity must never suppress another raid. The failure
            // remains visible and closes admission for the next Arena start.
            _runtimeFault = true;
            Reject(ex);
            return true;
        }
    }

    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely pause AI Limit: " + ex.GetBaseException().Message
            + ". Update or remove AI Limit, then restart the game. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure;
        Plugin.ModLogger.LogWarning(_failure);
    }

    private static void ClearBinding()
    {
        // Only our prefix, never AI Limit's own Harmony owner or user settings.
        _enable = null;
        _plugin = null;
        _version = null;
        _reportedWorld = null;
        if (!_patchAttempted) return;
        _patchAttempted = false;
        try { Patches.UnpatchSelf(); }
        catch (Exception ex) { Reject(ex); } // A surviving prefix now returns true.
    }

    internal static void Shutdown()
    {
        ClearBinding();
        _initialized = false;
        _runtimeFault = false;
        _failure = _lastWarning = null;
    }
}
