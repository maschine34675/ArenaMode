#nullable disable
using ArenaMode.Hud;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Combat.Translocator;

/// <summary>Native bound fire/aim commands while the exact persistent device is held.</summary>
internal static class ArenaTranslocatorInput
{
    private sealed class PressFrame { internal int Primary = int.MinValue, Secondary = int.MinValue; }
    private static ConditionalWeakTable<Player.UsableItemController, PressFrame> Frames = new();
    private static bool _enabled, _patched, _warned;

    internal static void Enable()
    {
        if (_enabled) return;
        if (!_patched) { new ArenaTranslocatorInputCommandPatch().Enable(); _patched = true; }
        _enabled = true;
    }

    internal static void Shutdown() { _enabled = false; Frames = new(); }

    internal static bool TryHandle(UsableItemInputTranslator translator, ECommand command)
    {
        if (!_enabled || (command != ECommand.ToggleShooting && command != ECommand.EndShooting
            && command != ECommand.ToggleAlternativeShooting && command != ECommand.EndAlternativeShooting)) return false;
        bool owned = false;
        try
        {
            if (!(translator?._controller is Player.UsableItemController hands)) return false;
            var player = hands._player;
            var item = hands.Item;
            if (!(owned = ArenaTranslocatorSupply.Owns(player, item))) return false;
            // Never let a private device enter native ADS, including a rejected
            // teleport or release event. Unknown/ordinary items remain native.
            if (command == ECommand.EndShooting || command == ECommand.EndAlternativeShooting) return true;
            var frame = Frames.GetValue(hands, _ => new PressFrame());
            bool primary = command == ECommand.ToggleShooting;
            if ((primary ? frame.Primary : frame.Secondary) == Time.frameCount) return true;
            if (primary) frame.Primary = Time.frameCount; else frame.Secondary = Time.frameCount;

            if (!ArenaTranslocatorRuntime.IsLivePlayer(player) || !ArenaTranslocatorSupply.HasVerifiedTemplate
                || !ReferenceEquals(player.HandsController, hands) || player.IsInventoryOpened
                || ArenaInputBlocker.Blocked || Respawn.ArenaRespawnDriver.IsRespawnInProgress
                || WeaponSwapService.IsSwapping || player.InventoryController.IsChangingWeapon
                || !ArenaTranslocatorPose.CanOperate(hands)) return true;

            // Native ToggleInputKeyCombination emits a start once for continuous
            // bindings and an end on release. Press bindings emit starts without
            // ends, so an end-dependent latch would break the user's toggle ADS bind.
            if (primary) ArenaTranslocatorRuntime.Primary(player, item);
            else ArenaTranslocatorRuntime.Secondary(player, item);
            return true;
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Plugin.ModLogger.LogWarning("Arena Translocator input rejected: " + ex.GetBaseException().Message);
            }
            return owned;
        }
    }

    private sealed class ArenaTranslocatorInputCommandPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(UsableItemInputTranslator),
            nameof(UsableItemInputTranslator.TranslateCommand), new[] { typeof(ECommand) });
        [PatchPrefix]
        private static bool Prefix(UsableItemInputTranslator __instance, ECommand command, ref InputNode.ETranslateResult __result)
        {
            if (!TryHandle(__instance, command)) return true;
            __result = InputNode.ETranslateResult.BlockAll;
            return false;
        }
    }
}
