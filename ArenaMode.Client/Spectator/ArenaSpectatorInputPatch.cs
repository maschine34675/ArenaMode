using EFT;
using EFT.InputSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace ArenaMode.Spectator;

/// <summary>
/// Restores only the native Escape-menu seam which GamePlayerOwner normally
/// rejects before dispatch when its bound local player is dead.
/// </summary>
internal static class ArenaSpectatorInputPatch
{
    private static bool _enabled;

    internal static void Enable()
    {
        if (_enabled)
            return;

        _enabled = true;
        new ArenaSpectatorInputEftOwnerTranslateCommandPatch().Enable();
    }

    private sealed class ArenaSpectatorInputEftOwnerTranslateCommandPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.DeclaredMethod(
                typeof(EftGamePlayerOwner),
                nameof(EftGamePlayerOwner.TranslateCommand),
                new[] { typeof(ECommand) })
               ?? throw new MissingMethodException(
                   typeof(EftGamePlayerOwner).FullName,
                   nameof(EftGamePlayerOwner.TranslateCommand));

        [PatchPrefix]
        private static bool Prefix(
            EftGamePlayerOwner __instance,
            ECommand __0,
            ref InputNode.ETranslateResult __result)
        {
            if (!ArenaSpectatorService.TryTranslateEscape(__instance, __0, out var translated))
                return true;

            __result = translated;
            return false;
        }
    }
}
