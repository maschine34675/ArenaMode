using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Validate the published item-cleanup API before installing optional guards.</summary>
internal static class ArenaLateToThePartyContract
{
    internal const string PluginGuid = "com.danw.latetotheparty";
    internal static readonly Version ReviewedVersion = new(3, 1, 1, 0);
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);

    internal static MethodInfo[] Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Late To The Party 3.1.1 or newer is required");
        if (plugin == null || plugin.FullName != "LateToTheParty.LateToThePartyPlugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Late To The Party plugin identity changed");
        var identity = plugin.Assembly.GetName();
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (identity.Name != "LateToTheParty-Client" || Normalize(identity.Version) != version
            || attribute?.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Late To The Party plugin and assembly metadata disagree");
        var helpers = plugin.Assembly.GetType("LateToTheParty.Helpers.Loot.LootDestructionHelpers", true);
        var discovery = plugin.Assembly.GetType("LateToTheParty.Helpers.Loot.LootDiscoveryHelpers", true);
        var manager = plugin.Assembly.GetType("LateToTheParty.Controllers.LootManager", true);
        var start = Method(helpers, "StartDestruction", true, true, typeof(void), typeof(Item));
        var destroy = Method(helpers, "DestroyViaLTTP", true, true, typeof(void), typeof(Item));
        var eligible = Method(helpers, "IsEligibleForDestruction", true, true, typeof(bool),
            typeof(Item), typeof(IEnumerable<Vector3>), typeof(double));
        var found = Method(discovery, "ProcessFoundLooseLootItem", true, true, typeof(void), typeof(LootItem), typeof(double));
        var door = Method(helpers, "openNearbyDoorForLoot", false, true, typeof(void), typeof(Item));
        var confirm = Method(manager, "ConfirmItemDestruction", true, false, typeof(void), typeof(Item));
        var update = Method(helpers, "UpdateLootEligibility", true, true, typeof(void),
            typeof(Item), typeof(IEnumerable<Vector3>), typeof(double));
        var enumerate = Method(manager, "FindAndDestroyLoot", true, false, typeof(IEnumerator),
            typeof(IEnumerable<Vector3>), typeof(float), typeof(double));
        var iterator = Calls(enumerate).OfType<ConstructorInfo>().SingleOrDefault(c => c.DeclaringType?.DeclaringType == manager
            && typeof(IEnumerator).IsAssignableFrom(c.DeclaringType))?.DeclaringType;
        if (iterator == null) throw new MissingMemberException("Late To The Party destruction iterator changed");
        var move = Method(iterator, "MoveNext", false, false, typeof(bool));
        RequiresCall(start, door); RequiresCall(start, destroy); RequiresCall(start, confirm);
        RequiresCall(update, eligible); RequiresCall(move, start); RequiresCall(move, found); RequiresCall(move, update);
        RequiresNativeCall(start, typeof(Action<Item>), "Invoke");
        RequiresNativeCall(destroy, typeof(ItemAddress), "RemoveWithoutRestrictions");
        RequiresNativeCall(destroy, typeof(IItemOwner), "RaiseRemoveEvent");
        // Reject added local helpers that delete items around the protected seam.
        // Door logic is included: it may open an interactive object, never remove loot.
        ValidateEntryGraph(new[] { start, found, update, enumerate, move }, destroy, plugin.Assembly);
        return new[] { start, destroy, eligible, found };
    }

    private static MethodInfo Method(Type type, string name, bool isPublic, bool isStatic, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.DeclaredOnly | (isStatic ? BindingFlags.Static : BindingFlags.Instance)
            | (isPublic ? BindingFlags.Public : BindingFlags.NonPublic), null, args, null);
        if (method == null || method.IsStatic != isStatic || method.IsPublic != isPublic || (!isPublic && !method.IsPrivate)
            || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static void RequiresCall(MethodBase caller, MethodBase target)
    {
        if (!Calls(caller).Contains(target)) throw new MissingMethodException("Late To The Party cleanup chain changed: " + caller.Name);
    }

    private static void RequiresNativeCall(MethodBase caller, Type type, string name)
    {
        if (!Calls(caller).Any(c => c.DeclaringType == type && c.Name == name))
            throw new MissingMethodException("Late To The Party cleanup operation changed: " + caller.Name + "/" + name);
    }

    private static void ValidateEntryGraph(IEnumerable<MethodBase> entries, MethodInfo guarded, Assembly assembly)
    {
        var pending = new Stack<MethodBase>(entries);
        var visited = new HashSet<MethodBase>();
        while (pending.Count != 0)
        {
            var method = pending.Pop();
            if (method == guarded || !visited.Add(method)) continue;
            // A helper can defer deletion into a compiler state machine. Its
            // factory's constructor calls alone do not reveal that later body.
            var state = method.GetCustomAttribute<StateMachineAttribute>()?.StateMachineType;
            if (state != null)
            {
                var move = state.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (move == null) throw new MissingMethodException("Late To The Party helper state machine changed");
                pending.Push(move);
            }
            foreach (var call in Calls(method))
            {
                if (IsItemDestruction(call)) throw new MissingMethodException("Late To The Party item deletion bypasses the guarded method");
                if (call.DeclaringType?.Assembly == assembly && call.GetMethodBody() != null) pending.Push(call);
            }
        }
    }

    private static bool IsItemDestruction(MethodBase call)
        => typeof(ItemAddress).IsAssignableFrom(call.DeclaringType) && call.Name == "RemoveWithoutRestrictions"
            || typeof(IItemOwner).IsAssignableFrom(call.DeclaringType) && call.Name == "RaiseRemoveEvent"
            || call.DeclaringType?.FullName == "EFT.GameWorld" && call.Name == "DestroyLoot"
            || call.DeclaringType?.FullName == "EFT.AssetsManager.AssetPoolObject" && call.Name == "ReturnToPool"
            || typeof(LootItem).IsAssignableFrom(call.DeclaringType) && (call.Name == "Kill" || call.Name == "RemoveLootItem")
            || call.DeclaringType?.Name == "IKillable" && call.Name == "Kill";
    // Read IL operands, not raw byte patterns: tokens and embedded numeric values
    // can contain the same bytes as call opcodes. No foreign method is executed.
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new MissingMethodException("Late To The Party entry has no managed body");
        int at = 0;
        while (at < il.Length)
        {
            short code = il[at++];
            if (code == 0xfe)
            {
                if (at >= il.Length) throw new InvalidOperationException("Truncated Late To The Party IL");
                code = (short)(0xfe00 | il[at++]);
            }
            if (!Codes.TryGetValue(code, out var op)) throw new InvalidOperationException("Unknown Late To The Party IL");
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch:
                    if (at + 4 > il.Length) throw new InvalidOperationException("Truncated Late To The Party switch");
                    int count = BitConverter.ToInt32(il, at);
                    if (count < 0 || count > (il.Length - at - 4) / 4) throw new InvalidOperationException("Invalid Late To The Party switch");
                    size = 4 + count * 4;
                    break;
                default: size = 4; break;
            }
            if (at + size > il.Length) throw new InvalidOperationException("Truncated Late To The Party operand");
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj || op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, at),
                    method.DeclaringType?.GetGenericArguments(), (method as MethodInfo)?.GetGenericArguments());
            at += size;
        }
    }

    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null
            : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
