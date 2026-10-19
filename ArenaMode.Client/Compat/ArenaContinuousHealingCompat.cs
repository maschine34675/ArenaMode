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
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Let native healing handle Arena bots and retired bodies; keep CH on the live local body.</summary>
internal static class ArenaContinuousHealingCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".ContinuousHealingCompat");
    private static readonly ArenaContinuousHealingScope Scope = new();
    private static MethodInfo[] _foreign;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _runtimeFault, _reportedNewer;
    private static string _failure, _lastWarning;
    private static int _reportedGuards, _reportedLocal;

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
        if (!Chainloader.PluginInfos.TryGetValue(ArenaContinuousHealingContract.PluginGuid, out var info))
        {
            ClearBinding();
            _failure = null;
            _runtimeFault = false;
            return;
        }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Continuous Healing has not finished loading");
            if (_foreign != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaContinuousHealingContract.Normalize(info.Metadata?.Version))
            {
                ArenaContinuousHealingContract.VerifyNativeBindings(_foreign);
                if (!OwnsPatches()) throw new InvalidOperationException("Continuous Healing compatibility hook was removed");
                return;
            }
            ClearBinding();
            var methods = ArenaContinuousHealingContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            _patchAttempted = true;
            for (int i = 0; i < methods.Length; ++i)
                Patches.Patch(methods[i], prefix: new HarmonyMethod(typeof(ArenaContinuousHealingCompat),
                    i == 0 ? nameof(BeforeEndHeal) : nameof(BeforeLocalPlayerAction)) { priority = Priority.First });
            _foreign = methods;
            _plugin = info.Instance;
            _version = version;
            if (!OwnsPatches()) throw new InvalidOperationException("Continuous Healing compatibility hooks did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Continuous Healing: exact-body medical guards ready.");
            if (version > ArenaContinuousHealingContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                Plugin.ModLogger.LogWarning("Arena Continuous Healing " + version
                    + " is newer than reviewed 1.6.3; its medical hook APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            ClearBinding();
            Reject(ex);
        }
    }

    private static bool OwnsPatches()
        => _foreign != null && _foreign.Select((method, index) => new { method, index }).All(entry =>
            Harmony.GetPatchInfo(entry.method)?.Prefixes.Any(p => p.owner == Patches.Id
                && p.PatchMethod == typeof(ArenaContinuousHealingCompat).GetMethod(
                    entry.index == 0 ? nameof(BeforeEndHeal) : nameof(BeforeLocalPlayerAction),
                    BindingFlags.NonPublic | BindingFlags.Static)) == true);

    // This prefixes CH's bool Prefix, not the native void OnEffectRemoved. A
    // true result from the skipped foreign prefix explicitly permits native work.
    private static bool BeforeEndHeal(Player.MedsController.MedsInHandsOperation __0, ref bool __result)
    {
        if (_foreign == null || !ShouldBypass(__0?._controller?._player, "effect removed")) return true;
        __result = true;
        return false;
    }

    // A retired local body's start/cancel event must not reset/cancel the new
    // body's continuous heal. Neither hook suppresses the underlying EFT action.
    private static bool BeforeLocalPlayerAction(Player __0, MethodBase __originalMethod)
        => _foreign == null || !ShouldBypass(__0, __originalMethod.DeclaringType.Name);

    private static bool ShouldBypass(Player body, string action)
    {
        bool owned = false;
        try
        {
            if (ReferenceEquals(body, null)) return false;
            var bodyWorld = body.GameWorld;
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
            bool exactActive = !ReferenceEquals(game, null) && !ReferenceEquals(world, null)
                && ReferenceEquals(game.GameWorld, world) && Singleton<IBotGame>.Instantiated
                && ReferenceEquals(Singleton<IBotGame>.Instance, game) && ArenaSession.IsExactActiveRaid(game, world);
            owned = Scope.Owns(body, bodyWorld, world, exactActive);
            if (!owned) return false;
            bool local = body.IsYourPlayer, alive = body.HealthController?.IsAlive == true;
            bool bypass = ArenaContinuousHealingScope.Bypass(body, bodyWorld, world, world?.MainPlayer, local, alive);
            if (ArenaLog.DeveloperEnabled)
            {
                // One line per seam and body category keeps an early start/cancel
                // burst from consuming the later effect-removal evidence budget.
                int seam = action == "effect removed" ? 0 : action == "CH_StartHeal_Patch" ? 1 : 2;
                int guardBit = 1 << (seam + (local ? 3 : 0));
                int localBit = 1 << seam;
                if (bypass && (_reportedGuards & guardBit) == 0)
                {
                    _reportedGuards |= guardBit;
                    ArenaLog.Developer("Continuous Healing " + action + ": native fallback for "
                        + (local ? "retired local body" : "Arena bot") + "; alive=" + alive + ".");
                }
                else if (!bypass && (_reportedLocal & localBit) == 0)
                {
                    _reportedLocal |= localBit;
                    ArenaLog.Developer("Continuous Healing " + action + ": current local body keeps foreign behavior.");
                }
            }
            return bypass;
        }
        catch (Exception ex)
        {
            _runtimeFault = true;
            Reject(ex);
            // Only an already proved body lease grants fallback authority.
            return owned;
        }
    }

    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely isolate Continuous Healing: " + ex.GetBaseException().Message
            + ". Update or remove Continuous Healing, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure;
        Plugin.ModLogger.LogWarning(_failure);
    }

    private static void ClearBinding()
    {
        _foreign = null;
        _plugin = null;
        _version = null;
        Scope.Clear();
        _reportedGuards = _reportedLocal = 0;
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
