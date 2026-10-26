using System;
using System.Linq;
using System.Reflection;
using ArenaMode.Combat;
using ArenaMode.Hud;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Compose the optional quick-throw entry with Arena's native selection and input ownership.</summary>
internal static class FastGrenadeThrowCompat
{
    internal const string PluginGuid = "com.vultify.fastgrenadethrow";
    private static readonly Version Reviewed = new(2, 0, 0, 0);
    private static readonly Harmony Patches = new("com.maschine.ArenaMode.FastGrenadeThrowCompat");
    private static object _instance;
    private static Version _version;
    private static MethodInfo _find, _throw;
    private static string _failure, _warning;
    private static bool _newerWarned;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        { Clear(); return _failure = null; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = Normalize(info.Metadata?.Version);
            if (ReferenceEquals(info.Instance, _instance) && version == _version)
            { VerifyHooks(); return _failure; }
            Clear();
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>(false);
            if (version == null || version < Reviewed) throw new NotSupportedException("version 2.0.0 or newer is required");
            if (type.FullName != "FastGrenadeThrow.FastGrenadeThrowPlugin"
                || type.Assembly.GetName().Name != "FastGrenadeThrow"
                || !typeof(BaseUnityPlugin).IsAssignableFrom(type)
                || info.Metadata.GUID != PluginGuid || metadata?.GUID != PluginGuid
                || Normalize(metadata.Version) != version)
                throw new InvalidOperationException("plugin identity or metadata changed");
            _find = Method(type, "FindGrenade", typeof(ThrowWeap), typeof(Player));
            _throw = Method(type, "TryQuickThrow", typeof(void), typeof(Player), typeof(bool));
            Patches.Patch(_find, postfix: new HarmonyMethod(typeof(FastGrenadeThrowCompat), nameof(AfterFind)) { priority = Priority.Last });
            Patches.Patch(_throw, prefix: new HarmonyMethod(typeof(FastGrenadeThrowCompat), nameof(BeforeThrow)) { priority = Priority.First });
            _instance = info.Instance; _version = version;
            VerifyHooks(); _failure = null;
            Plugin.ModLogger.LogDebug("Fast Grenade Throw: Arena grenade selection and direct-key input guard ready.");
            if (version > Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                Plugin.ModLogger.LogWarning("Fast Grenade Throw " + version + " is newer than reviewed 2.0.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            Clear();
            _failure = "Fast Grenade Throw cannot be integrated with Arena: " + ex.GetBaseException().Message + ".";
            if (_warning != _failure) { _warning = _failure; Plugin.ModLogger.LogWarning(_failure); }
        }
        return _failure;
    }

    private static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
    private static MethodInfo Method(Type type, string name, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    private static void VerifyHooks()
    {
        bool Present(MethodInfo target, string name, bool prefix)
        {
            var info = target == null ? null : Harmony.GetPatchInfo(target);
            var hooks = prefix ? info?.Prefixes : info?.Postfixes;
            var hook = typeof(FastGrenadeThrowCompat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            return hooks?.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) == 1;
        }
        if (!Present(_find, nameof(AfterFind), false) || !Present(_throw, nameof(BeforeThrow), true))
            throw new MissingMethodException("quick-throw compatibility hook is missing");
    }
    private static void Clear()
    {
        Patches.UnpatchSelf(); _instance = null; _version = null; _find = _throw = null;
    }
    private static bool CurrentPlayer(Player player)
    {
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        return player != null && game != null && world != null && ArenaSession.IsExactActiveRaid(game, world)
            && ReferenceEquals(game.LocalPlayer, player) && ReferenceEquals(world.MainPlayer, player)
            && player.IsYourPlayer && !player.IsAI && player.HealthController != null && player.HealthController.IsAlive;
    }
    private static bool BeforeThrow(Player __0)
    {
        // FGT polls Unity keys directly, bypassing InputManager and Arena's panels.
        if (!ArenaSession.IsActive) return true;
        return CurrentPlayer(__0) && !ArenaInputBlocker.Blocked
            && !ArenaRespawnDriver.IsRespawnInProgress && !ArenaRespawnDriver.StandDown
            && !ArenaSpectatorService.IsActive && !WeaponSwapService.IsSwapping;
    }
    private static void AfterFind(Player __0, ref ThrowWeap __result)
    {
        if (!ArenaSession.IsActive || !CurrentPlayer(__0)) return;
        var inventory = __0.InventoryController;
        var equipment = inventory?.Inventory?.Equipment;
        var selected = equipment?.TopPriorityGrenade;
        if (selected == null || selected.CurrentAddress == null || !equipment.Contains(selected)
            || !inventory.Examined(selected)) return;
        // Reuse the effective native/FGT/UIA candidates. A disabled foreign slot
        // remains disabled; no ID-based substitution or permanent Molotov override.
        var candidates = InventoryExtension.GetThrowablePriorityGrenadesList(inventory);
        if (candidates != null && candidates.Any(item => ReferenceEquals(item, selected))) __result = selected;
    }
}
