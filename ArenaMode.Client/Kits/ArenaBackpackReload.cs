using ArenaMode.Config;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace ArenaMode.Kits;

/// <summary>Add a candidate source, not a custom reload or inventory transaction.</summary>
internal static class ArenaBackpackReload
{
    private static readonly EquipmentSlot[] Backpack = { EquipmentSlot.Backpack };
    private static int? _warnedEpoch;

    internal static void Enable()
    {
        try
        {
            new ArenaBackpackReloadReloadCandidatesPatch().Enable();
            new ArenaBackpackReloadSelectorCandidatesPatch().Enable();
            new ArenaBackpackReloadInventoryCandidatesPatch().Enable();
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception ex)
    {
        if (_warnedEpoch == ArenaSession.ArmEpoch) return;
        _warnedEpoch = ArenaSession.ArmEpoch;
        Plugin.ModLogger.LogWarning("Arena backpack magazine selection failed; native candidates retained: " + ex.GetBaseException().Message);
    }

    // Read-only mirror of the gate above for display code (the weapon HUD's
    // spare-magazine counter): reports whether THIS call would append the
    // backpack's direct children to the reload candidate list, without
    // touching the reload path itself. Allocation-free, safe to call every
    // HUD refresh.
    internal static bool AppendsBackpackFor(InventoryController controller) => Wanted(controller);

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

    // NEVER patch a constructed generic query on Mono. Reference-type instantiations
    // share code; Harmony's Magazine trampoline also replaced Ammo/ThrowWeap calls,
    // corrupting the ORIGINAL scan before our postfix could check the arguments.
    // Instead replace only magazine-query calls inside three non-generic consumers.
    private static void CollectMagazines(InventoryController controller, IList<Magazine> output, Predicate<Magazine> predicate)
    {
        controller.GetReachableItemsOfTypeNonAlloc(output, predicate);
        AppendBackpack(controller, output, predicate);
    }

    private static IEnumerable<Magazine> FindMagazines(InventoryController controller, Predicate<Magazine> predicate)
    {
        var output = new List<Magazine>();
        CollectMagazines(controller, output, predicate);
        return output;
    }

    private static void AppendBackpack(InventoryController controller, IList<Magazine> output, Predicate<Magazine> predicate)
    {
        if (output == null || output.IsReadOnly) return;
        try
        {
            if (!Wanted(controller)) return;
            var backpack = controller.Inventory?.Equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (backpack == null) return;
            var candidates = new List<Magazine>();
            controller.GetAcceptableItemsNonAlloc(Backpack, candidates, predicate);
            // Predicates may replace the body or equipment while enumerating.
            if (!Wanted(controller) || !ReferenceEquals(backpack,
                controller.Inventory?.Equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem)) return;
            foreach (var magazine in candidates)
                if (!output.Any(existing => ReferenceEquals(existing, magazine))) output.Add(magazine);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static IEnumerable<CodeInstruction> ReplaceQuery(IEnumerable<CodeInstruction> instructions, bool nonAlloc)
    {
        var source = typeof(InventoryController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == (nonAlloc ? nameof(InventoryController.GetReachableItemsOfTypeNonAlloc) : "GetReachableItemsOfType")
                && m.IsGenericMethodDefinition && m.GetParameters().Length == (nonAlloc ? 2 : 1))
            .MakeGenericMethod(typeof(Magazine));
        var replacement = typeof(ArenaBackpackReload).GetMethod(nonAlloc ? nameof(CollectMagazines) : nameof(FindMagazines), BindingFlags.Static | BindingFlags.NonPublic);
        var code = instructions.ToList();
        var calls = code.Where(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt)
            && Equals(i.operand, source)).ToArray();
        if (calls.Length != 1)
            throw new InvalidOperationException("Expected exactly one native magazine query; found " + calls.Length);
        // Keep the instruction's labels/exception blocks, and the original stack:
        // the static helper takes the old instance receiver as its first argument.
        calls[0].opcode = OpCodes.Call;
        calls[0].operand = replacement;
        return code;
    }

    private sealed class ArenaBackpackReloadReloadCandidatesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(FirearmHandsInputTranslator).GetMethod(nameof(FirearmHandsInputTranslator.ReloadExternalMagazine));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceQuery(instructions, true);
    }

    private sealed class ArenaBackpackReloadSelectorCandidatesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(AmmoSelector.CG_FindAmmoForWeapon).GetMethod(nameof(AmmoSelector.CG_FindAmmoForWeapon.method_0));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceQuery(instructions, false);
    }

    private sealed class ArenaBackpackReloadInventoryCandidatesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(ItemUiContext).GetMethod(nameof(ItemUiContext.FindSuitableMagazine));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceQuery(instructions, false);
    }
}
