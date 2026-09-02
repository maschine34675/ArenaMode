using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using ArenaMode.Compat;
using ArenaMode.Session;
using System;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Spectator;

internal static class ArenaSpectatorCameraPatches
{
    private static bool _enabled;

    internal static void Enable()
    {
        if (_enabled)
            return;

        _enabled = true;
        new ForceSetPositionPatch().Enable();
    }

    private sealed class ForceSetPositionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(
                typeof(CameraManager),
                nameof(CameraManager.ForceSetPosition),
                new[] { typeof(Vector3) })
               ?? throw new MissingMethodException(
                   typeof(CameraManager).FullName,
                   nameof(CameraManager.ForceSetPosition));

        [PatchPrefix]
        private static bool Prefix()
            => !ArenaSession.IsActive
               || !PitFireTeamCompat.TeamArenaActive
               || !ArenaSpectatorService.OwnsCamera;
    }
}
