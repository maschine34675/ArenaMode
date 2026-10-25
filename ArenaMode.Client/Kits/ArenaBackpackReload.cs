using ArenaMode.Config;
using ArenaMode.Compat;
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
        UseItemsAnywhereCompat.Enable();
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
        Plugin.ModLogger.LogWarning("Arena backpack magazine extension unavailable; existing query retained: " + ex.GetBaseException().Message);
    }

    // Read-only mirror of the gate above for display code (the weapon HUD's
    // spare-magazine counter): reports whether THIS call would append the
    // backpack's direct children to the reload candidate list, without
    // touching the reload path itself. Allocation-free, safe to call every
    // HUD refresh.
    internal static bool AppendsBackpackFor(InventoryController controller) => Wanted(controller);

    private static bool Wanted(InventoryController controller)
    {
        if (!ArenaSession.IsActive || Settings.UseItemsAnywhere?.Value != true || UseItemsAnywhereCompat.OwnsItemAccess
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
    // Instead retain the native call in three non-generic consumers and extend
    // its result. UIA's transpiler still finds its one native call in either order.
    private static IEnumerable<Magazine> AppendBackpackToResult(IEnumerable<Magazine> native, InventoryController controller, Predicate<Magazine> predicate)
    {
        if (!Wanted(controller)) return native;
        var output = native.ToList();
        AppendBackpack(controller, output, predicate);
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

    private static IEnumerable<CodeInstruction> ExtendQuery(IEnumerable<CodeInstruction> instructions, ILGenerator generator, bool nonAlloc)
    {
        var source = typeof(InventoryController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == (nonAlloc ? nameof(InventoryController.GetReachableItemsOfTypeNonAlloc) : "GetReachableItemsOfType")
                && m.IsGenericMethodDefinition && m.GetParameters().Length == (nonAlloc ? 2 : 1))
            .MakeGenericMethod(typeof(Magazine));
        var code = instructions.ToList();
        var calls = code.Where(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt)
            && Equals(i.operand, source)).ToArray();
        var foreign = code.Count(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt)
            && i.operand is MethodInfo method && UseItemsAnywhereCompat.IsMagazineQuery(method, nonAlloc));
        if (!UseItemsAnywhereQueryPolicy.ShouldAppend(calls.Length, foreign)) return code;

        var receiver = generator.DeclareLocal(typeof(InventoryController));
        var predicate = generator.DeclareLocal(typeof(Predicate<Magazine>));
        var output = nonAlloc ? generator.DeclareLocal(typeof(IList<Magazine>)) : null;
        var before = new List<CodeInstruction> { new CodeInstruction(OpCodes.Stloc, predicate) };
        if (nonAlloc) before.Add(new CodeInstruction(OpCodes.Stloc, output));
        before.Add(new CodeInstruction(OpCodes.Stloc, receiver));
        before.Add(new CodeInstruction(OpCodes.Ldloc, receiver));
        if (nonAlloc) before.Add(new CodeInstruction(OpCodes.Ldloc, output));
        before.Add(new CodeInstruction(OpCodes.Ldloc, predicate));
        var after = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ldloc, receiver) };
        if (nonAlloc) after.Add(new CodeInstruction(OpCodes.Ldloc, output));
        after.Add(new CodeInstruction(OpCodes.Ldloc, predicate));
        after.Add(new CodeInstruction(OpCodes.Call, typeof(ArenaBackpackReload).GetMethod(
            nonAlloc ? nameof(AppendBackpack) : nameof(AppendBackpackToResult), BindingFlags.Static | BindingFlags.NonPublic)));

        // A branch to the call must still save its arguments. Start blocks stay
        // before it; end blocks move after the extension, preserving its scope.
        var call = calls[0];
        before[0].labels.AddRange(call.labels);
        call.labels.Clear();
        before[0].blocks.AddRange(call.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock));
        after[after.Count - 1].blocks.AddRange(call.blocks.Where(b => b.blockType == ExceptionBlockType.EndExceptionBlock));
        call.blocks.Clear();
        var index = code.IndexOf(call);
        code.InsertRange(index, before);
        code.InsertRange(index + before.Count + 1, after);
        return code;
    }

    private sealed class ArenaBackpackReloadReloadCandidatesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(FirearmHandsInputTranslator).GetMethod(nameof(FirearmHandsInputTranslator.ReloadExternalMagazine));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) => ExtendQuery(instructions, generator, true);
    }

    private sealed class ArenaBackpackReloadSelectorCandidatesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(AmmoSelector.CG_FindAmmoForWeapon).GetMethod(nameof(AmmoSelector.CG_FindAmmoForWeapon.method_0));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) => ExtendQuery(instructions, generator, false);
    }

    private sealed class ArenaBackpackReloadInventoryCandidatesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(ItemUiContext).GetMethod(nameof(ItemUiContext.FindSuitableMagazine));
        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) => ExtendQuery(instructions, generator, false);
    }
}
