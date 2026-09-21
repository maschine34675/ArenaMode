using ArenaMode.Config;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ArenaMode.Kits;

/// <summary>Add a candidate source, not a custom reload or inventory transaction.</summary>
internal static class ArenaBackpackReload
{
    private static readonly EquipmentSlot[] Backpack = { EquipmentSlot.Backpack };
    private static int? _warnedEpoch;

    internal static void Enable()
    {
        try { new ReachableMagazinesPatch().Enable(); }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception ex)
    {
        if (_warnedEpoch == ArenaSession.ArmEpoch) return;
        _warnedEpoch = ArenaSession.ArmEpoch;
        Plugin.ModLogger.LogWarning("Arena backpack magazine selection failed; native candidates retained: " + ex.GetBaseException().Message);
    }

    private static bool Wanted(InventoryController controller)
    {
        if (!ArenaSession.IsActive || Settings.UseItemsAnywhere?.Value != true
            || !Singleton<GameWorld>.Instantiated || !Singleton<AbstractGame>.Instantiated
            || ArenaRespawnDriver.IsRespawnInProgress || ArenaRespawnDriver.StandDown || ArenaSpectatorService.IsActive) return false;
        var world = Singleton<GameWorld>.Instance;
        var game = Singleton<AbstractGame>.Instance as LocalGame;
        var player = world?.MainPlayer;
        return player != null && !player.IsAI && player.IsYourPlayer && player.HealthController?.IsAlive == true
            && game != null && game.Status == GameStatus.Started && ReferenceEquals(game.GameWorld, world)
            && ReferenceEquals(game.LocalPlayer, player) && ArenaSession.IsExactActiveRaid(game, world)
            && ReferenceEquals(player.InventoryController, controller);
    }

    private sealed class ReachableMagazinesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(InventoryController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == nameof(InventoryController.GetReachableItemsOfTypeNonAlloc)
                    && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(Magazine));

        // Mono may share generic reference-type code. Runtime argument checks are
        // mandatory: an Ammo/ThrowWeap query must never acquire magazine results.
        [PatchPostfix]
        private static void Postfix(InventoryController __instance, object preAllocatedList, object predicate)
        {
            if (preAllocatedList is not IList<Magazine> output || output.IsReadOnly
                || (predicate != null && predicate is not Predicate<Magazine>)) return;
            try
            {
                if (!Wanted(__instance)) return;
                var backpack = __instance.Inventory?.Equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
                if (backpack == null) return;
                var candidates = new List<Magazine>();
                __instance.GetAcceptableItemsNonAlloc(Backpack, candidates, (Predicate<Magazine>)predicate);
                // Predicate callbacks may change body or worn equipment. Publish
                // nothing from an inventory that was replaced while enumerating.
                if (!Wanted(__instance) || !ReferenceEquals(backpack,
                    __instance.Inventory?.Equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem)) return;
                foreach (var magazine in candidates)
                    if (!output.Any(existing => ReferenceEquals(existing, magazine))) output.Add(magazine);
            }
            catch (Exception ex) { Warn(ex); }
        }
    }
}
