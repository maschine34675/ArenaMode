using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Pickups;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Interactive;
using HarmonyLib;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Protect only native sources still owned by Arena's weapon-pickup factory.</summary>
internal static class ArenaLateToThePartyCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".LateToThePartyCompat");
    private static MethodInfo[] _methods;
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
        if (!Chainloader.PluginInfos.TryGetValue(ArenaLateToThePartyContract.PluginGuid, out var info))
        {
            ClearBinding();
            _failure = null;
            _runtimeFault = false;
            return;
        }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Late To The Party has not finished loading");
            if (_methods != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ArenaLateToThePartyContract.Normalize(info.Metadata?.Version))
            {
                if (!OwnsPatches()) throw new InvalidOperationException("Late To The Party compatibility hook was removed");
                return;
            }
            ClearBinding();
            var methods = ArenaLateToThePartyContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            _patchAttempted = true;
            for (int i = 0; i < methods.Length; i++)
                Patches.Patch(methods[i], prefix: new HarmonyMethod(typeof(ArenaLateToThePartyCompat),
                    PrefixName(i)) { priority = Priority.First });
            _methods = methods;
            _plugin = info.Instance;
            _version = version;
            if (!OwnsPatches()) throw new InvalidOperationException("Late To The Party compatibility hooks did not install");
            _failure = null;
            Plugin.ModLogger.LogDebug("Arena Late To The Party: exact weapon-source eligibility and destruction guards ready.");
            if (version > ArenaLateToThePartyContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                Plugin.ModLogger.LogWarning("Arena Late To The Party " + version
                    + " is newer than reviewed 3.1.1; the cleanup API was checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            ClearBinding();
            Reject(ex);
        }
    }

    private static bool OwnsPatches()
        => _methods != null && _methods.Select((method, index) => new { method, index }).All(entry =>
            Harmony.GetPatchInfo(entry.method)?.Prefixes.Any(p => p.owner == Patches.Id
                && p.PatchMethod == typeof(ArenaLateToThePartyCompat).GetMethod(
                    PrefixName(entry.index),
                    BindingFlags.Static | BindingFlags.NonPublic)) == true);

    private static bool BeforeDestruction(Item __0) => !Protects(__0);
    private static bool BeforeDiscovery(LootItem __0) => __0 == null || !Protects(__0.Item, __0);
    private static string PrefixName(int index) => index == 2 ? nameof(BeforeEligibility)
        : index == 3 ? nameof(BeforeDiscovery) : nameof(BeforeDestruction);

    private static bool BeforeEligibility(Item __0, ref bool __result)
    {
        if (!Protects(__0)) return true;
        __result = false;
        return false;
    }

    private static bool Protects(Item item, LootItem source = null)
    {
        if (_methods == null || item == null) return false;
        try
        {
            if (Singleton<AbstractGame>.Instance is not LocalGame game) return false;
            var world = Singleton<GameWorld>.Instance;
            if (!ArenaLateToThePartyScope.IsExactWorld(game, Singleton<IBotGame>.Instance, world, game.GameWorld)) return false;
            // The exact source handle survives pending retirement; a session flag
            // cannot revoke native cleanup ownership before that request settles.
            bool protects = ArenaWeaponPickup.ProtectsFromForeignCleanup(world, item, source);
            if (protects && (_reportedWorld == null || !_reportedWorld.TryGetTarget(out var previous)
                || !ReferenceEquals(previous, world)))
            {
                _reportedWorld = new WeakReference<GameWorld>(world);
                Plugin.ModLogger.LogDebug("Arena Late To The Party: owned weapon source excluded from foreign cleanup.");
            }
            return protects;
        }
        catch (Exception ex)
        {
            _runtimeFault = true;
            Reject(ex);
            // Unknown ordinary items never acquire protection from a read failure.
            return false;
        }
    }

    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely protect its weapon pickups from Late To The Party: " + ex.GetBaseException().Message
            + ". Update or remove Late To The Party, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure;
        Plugin.ModLogger.LogWarning(_failure);
    }

    private static void ClearBinding()
    {
        _methods = null;
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
