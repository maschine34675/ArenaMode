using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;

namespace ArenaMode.Bots;

/// <summary>Changes only Arena's Original reverse copy, never the shared native
/// method or another mod's patches. A drifted shape refuses the copy; the existing
/// faithful fallback has the same explicit budget boundary.</summary>
internal static class ZombiePathBudgetTranspiler
{
    internal static IEnumerable<CodeInstruction> Apply(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.ToList();
        var hasPath = AccessTools.PropertyGetter(typeof(BotMover), nameof(BotMover.HasPathAndNoComplete));
        var getMover = AccessTools.PropertyGetter(typeof(BotOwner), nameof(BotOwner.Mover));
        var owner = AccessTools.Field(typeof(BotMeleeWeaponData), nameof(BotMeleeWeaponData._owner));
        var hit = AccessTools.Method(typeof(BotMeleeWeaponData), nameof(BotMeleeWeaponData.TryHit));
        var resetHit = AccessTools.Method(typeof(BotMeleeWeaponData), nameof(BotMeleeWeaponData.ResetHitTime));
        var route = AccessTools.Method(typeof(BotMeleeWeaponData), nameof(BotMeleeWeaponData.CanRunToEnemyToHit));
        int pathIndex = SingleCall(code, hasPath);
        int anchor = pathIndex - 3;
        int hitIndex = SingleCall(code, hit);
        int resetIndex = SingleCall(code, resetHit);
        var routes = code.Select((instruction, index) => (instruction, index))
            .Where(x => Calls(x.instruction, route)).Select(x => x.index).ToArray();
        if (generator == null || anchor < 0 || code[anchor].opcode != OpCodes.Ldarg_0
            || code[anchor + 1].opcode != OpCodes.Ldfld || !SameMember(code[anchor + 1].operand, owner)
            || !Calls(code[anchor + 2], getMover) || hitIndex >= resetIndex || resetIndex != anchor - 1
            || routes.Length != 2 || routes.Any(index => index <= pathIndex)
            || code.Any(instruction => instruction.blocks.Count != 0)
            || code.Skip(anchor + 1).Take(3).Any(instruction => instruction.labels.Count != 0))
            throw new InvalidOperationException("Native zombie melee planning boundary changed; guarded reverse copy refused.");

        var resume = generator.DefineLabel();
        var first = new CodeInstruction(OpCodes.Ldarg_0);
        // The hit-not-due branch jumps directly to the original anchor. Move its
        // labels onto the guard, otherwise every second tick bypasses the budget.
        first.labels.AddRange(code[anchor].labels);
        code[anchor].labels.Clear();
        code[anchor].labels.Add(resume);
        code.InsertRange(anchor, new[] {
            first,
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ZombiePathBudgetRuntime), nameof(ZombiePathBudgetRuntime.ShouldDeferMelee))),
            new CodeInstruction(OpCodes.Brfalse, resume),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ret)
        });
        return code;
    }

    private static bool Calls(CodeInstruction instruction, MethodInfo method)
        => method != null && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
            && SameMember(instruction.operand, method);
    // Native IL names BotData._owner, while a reflected lookup through the
    // derived melee type may have a different ReflectedType. Those FieldInfo
    // objects need not be Equals despite naming the exact same metadata field.
    private static bool SameMember(object operand, MemberInfo expected)
        => operand is MemberInfo actual && expected != null
            && actual.Module == expected.Module && actual.MetadataToken == expected.MetadataToken;
    private static int SingleCall(List<CodeInstruction> code, MethodInfo method)
    {
        int found = -1;
        for (int i = 0; i < code.Count; i++)
            if (Calls(code[i], method))
            {
                if (found >= 0) throw new InvalidOperationException("Ambiguous native zombie melee planning boundary.");
                found = i;
            }
        if (found < 0) throw new InvalidOperationException("Missing native zombie melee planning boundary.");
        return found;
    }
}
