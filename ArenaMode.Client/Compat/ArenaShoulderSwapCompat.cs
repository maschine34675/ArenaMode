using System;
using System.Reflection;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Redeploy.Core.Compat;

namespace ArenaMode.Compat;

/// <summary>
/// Auto Shoulder Swap 1.1.1 assumes every lean has firearm hands and a populated scope list.
/// This is an upstream input defect, not a body cache: guard only its unsafe Arena callbacks.
/// </summary>
internal static class ArenaShoulderSwapCompat
{
    private const string Guid = "hazelify.StanceSync";
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".ShoulderSwap");
    private static object _instance;
    private static MethodInfo _lean, _camera;
    private static string _rejection;

    internal static string GetRejectionReason()
    {
        // Nothing to bind, inspect or unpatch in the common absent-mod path. Admission is
        // polled by the menu, so a failed contract is cached just like a successful binding.
        if (!Chainloader.PluginInfos.TryGetValue(Guid, out var info)) return null;
        if (info.Instance == null) return "Auto Shoulder Swap has not finished loading.";
        if (ReferenceEquals(_instance, info.Instance)) return _rejection;
        Shutdown();
        _instance = info.Instance;
        try
        {
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>();
            if (type.FullName != "hazelify.StanceSync.Plugin" || metadata?.GUID != Guid
                || info.Metadata?.GUID != Guid || metadata.Version != info.Metadata.Version)
                throw new InvalidOperationException("plugin identity does not match its loaded metadata");
            var assembly = type.Assembly;
            _lean = Find(assembly, "hazelify.StanceSync.Patches.OnLeanPatchPostfix", "Postfix", typeof(Player), typeof(float));
            _camera = Find(assembly, "hazelify.StanceSync.Patches.PlayerCameraControllerPatchPrefix", "Prefix", typeof(PlayerCameraController));
            Patches.Patch(_lean, prefix: new HarmonyMethod(typeof(ArenaShoulderSwapCompat), nameof(BeforeLean)) { priority = Priority.First });
            Patches.Patch(_camera, prefix: new HarmonyMethod(typeof(ArenaShoulderSwapCompat), nameof(BeforeCamera)) { priority = Priority.First });
        }
        catch (Exception ex)
        {
            RemovePatches();
            _rejection = "Auto Shoulder Swap Arena input integration is unavailable: " + ex.GetBaseException().Message + ".";
        }
        return _rejection;
    }

    internal static void Shutdown()
    {
        RemovePatches();
        _instance = null;
        _rejection = null;
    }

    private static void RemovePatches()
    {
        // Targeted removal only; never scan every Harmony-patched method at menu refresh.
        if (_lean != null) Patches.Unpatch(_lean, HarmonyPatchType.Prefix, Patches.Id);
        if (_camera != null) Patches.Unpatch(_camera, HarmonyPatchType.Prefix, Patches.Id);
        _lean = _camera = null;
    }

    private static MethodInfo Find(Assembly assembly, string typeName, string name, params Type[] args)
    {
        var type = assembly.GetType(typeName, true);
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(typeName, name);
        return method;
    }

    // These patch the foreign static callbacks, not Player.ToggleLean or the native camera
    // update. __0 is therefore the original callback's first parameter, NOT __instance.
    private static bool BeforeLean(Player __0, float __1)
    {
        var player = ExactArenaPlayer();
        return player == null || !ReferenceEquals(player, __0) || HasSafeHands(player);
    }

    private static bool BeforeCamera(PlayerCameraController __0)
    {
        var player = ExactArenaPlayer();
        return player == null || __0 == null || !ReferenceEquals(__0.Player, player)
            || !ReferenceEquals(Singleton<PlayerCameraController>.Instance, __0) || HasSafeHands(player);
    }

    private static Player ExactArenaPlayer()
    {
        if (!ArenaSession.IsActive || FikaCompatCheck.IsFikaLoaded) return null;
        var world = Singleton<GameWorld>.Instance;
        if (world == null || !(Singleton<AbstractGame>.Instance is LocalGame game)
            || !ReferenceEquals(game.GameWorld, world) || !ArenaSession.IsExactActiveRaid(game, world)) return null;
        var player = world.MainPlayer;
        return player != null && player.IsYourPlayer && !player.IsAI && ReferenceEquals(game.LocalPlayer, player) ? player : null;
    }

    private static bool HasSafeHands(Player player)
    {
        // Native CurrentScope indexes element zero even for an empty list. Melee/meds/empty
        // hands have no firearm or scopes; a newly drawn firearm can also be unready briefly.
        // Suppress only the foreign shoulder action until those native preconditions exist.
        if (!(player.HandsController is Player.FirearmController) || player.HealthController == null
            || !player.HealthController.IsAlive || player.MovementContext == null || player.CameraPosition == null) return false;
        var pwa = player.ProceduralWeaponAnimation;
        if (pwa == null || pwa.ScopeAimTransforms == null || pwa.ScopeAimTransforms.Count == 0) return false;
        var aimIndex = pwa.AimIndex;
        return aimIndex >= 0 && pwa.ScopeAimTransforms[aimIndex % pwa.ScopeAimTransforms.Count] != null;
    }
}
