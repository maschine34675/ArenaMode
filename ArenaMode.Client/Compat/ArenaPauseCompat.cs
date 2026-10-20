using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Modes;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using UnityEngine;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Reject only a new pause request before Pause's global flag or player/timer mutations.</summary>
internal static class ArenaPauseCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".PauseCompat");
    private static readonly ArenaPauseScope Scope = new();
    private static ArenaPauseContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static string _failure, _lastWarning;
    private static bool _initialized, _attempted, _faulted, _reportedNewer;

    internal static void Initialize()
    { if (_initialized) return; _initialized = true; Refresh(); }
    internal static string GetRejectionReason()
    { if (!_initialized) Initialize(); else Refresh(); return _failure; }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaPauseContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; _faulted = false; return; }
        if (_faulted) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Pause Continued has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaPauseContract.Normalize(info.Metadata?.Version))
            {
                if (!OwnsPatch()) throw new InvalidOperationException("Pause Continued compatibility hook was removed");
                return;
            }
            ClearBinding();
            var contract = ArenaPauseContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            _attempted = true;
            Patches.Patch(contract.Update, prefix: new HarmonyMethod(typeof(ArenaPauseCompat), nameof(BeforeUpdate))
                { priority = Priority.First });
            _contract = contract; _plugin = info.Instance; _version = version;
            if (!OwnsPatch()) throw new InvalidOperationException("Pause Continued compatibility hook did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Pause Continued: exact-world pause activation guard ready.");
            if (version > ArenaPauseContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                Plugin.ModLogger.LogWarning("Arena Pause Continued " + version
                    + " is newer than reviewed 1.5.0; required APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }

    private static bool OwnsPatch() => _contract != null
        && Harmony.GetPatchInfo(_contract.Update)?.Prefixes.Any(p => p.owner == Patches.Id
            && p.PatchMethod == typeof(ArenaPauseCompat).GetMethod(nameof(BeforeUpdate),
                BindingFlags.Static | BindingFlags.NonPublic)) == true;

    private static bool OwnsController(object instance, out GameWorld world)
    {
        world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        if (ReferenceEquals(game, null) || ReferenceEquals(world, null)
            || !ReferenceEquals(game.GameWorld, world) || instance is not Component component
            || !ReferenceEquals(component.gameObject, world.gameObject)
            || !ReferenceEquals(_contract.CachedWorld.GetValue(null), world)
            || !ReferenceEquals(_contract.CachedGame.GetValue(instance), game)) return false;
        // The component is normally created on OnGameStarted. A future valid
        // caller during loading still needs the actual server-confirmed arm.
        bool loading = game.Status == GameStatus.Running && game.Profile != null
            && (ReferenceEquals(game.LocalPlayer, null)
                || ReferenceEquals(game.LocalPlayer, world.MainPlayer) && game.LocalPlayer.IsYourPlayer
                    && ReferenceEquals(game.LocalPlayer.Profile, game.Profile));
        bool started = game.Status == GameStatus.Started && Singleton<IBotGame>.Instantiated
            && ReferenceEquals(Singleton<IBotGame>.Instance, game);
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        bool capture = ArenaPauseScope.CanCapture(loading || started, settings?.playerSide == ESideType.Pmc,
            world.LocationId, settings?.location, ArenaSession.IsExactActiveRaid(game, world),
            ArenaSession.ArmedForNextRaid, ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap,
            ArenaServerClient.PendingArmToken, settings?.serverId);
        return Scope.Owns(game, world, true, capture);
    }

    private static bool BeforeUpdate(object __instance)
    {
        if (_contract == null) return true;
        try
        {
            // Never interfere with release, even if someone paused before our
            // capture or another mod deliberately paused the game.
            if (_contract.IsPaused()) return true;
            if (!OwnsController(__instance, out var world)) return true;
            var key = (_contract.TogglePause.GetValue(null) as ConfigEntry<KeyboardShortcut>)?.Value;
            if (!key.HasValue) throw new InvalidOperationException("Pause Continued keybind is unavailable");
            if (!_contract.IsKeyPressed(key.Value)) return true;
            if (Scope.FirstNotice(world))
            {
                const string message = "Pause Continued is unavailable during Arena matches. Ordinary raids can still be paused.";
                Plugin.ModLogger.LogInfo("Arena: " + message);
                ArenaToasts.Show(message);
            }
            return false;
        }
        catch (Exception ex)
        {
            // An unknown world must remain ordinary. This closes the next Arena
            // admission; no foreign state is cleared or guessed during recovery.
            _faulted = true;
            Reject(ex);
            return true;
        }
    }

    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely isolate Pause Continued: " + ex.GetBaseException().Message
            + ". Update or remove Pause Continued, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; Plugin.ModLogger.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        _contract = null; _plugin = null; _version = null; Scope.Clear();
        if (!_attempted) return;
        _attempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown()
    { ClearBinding(); _initialized = _faulted = false; _failure = _lastWarning = null; }
}
