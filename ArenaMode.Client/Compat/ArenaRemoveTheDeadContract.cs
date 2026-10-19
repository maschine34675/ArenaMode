using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using UnityEngine;
using Object = UnityEngine.Object;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Validate both published cleanup routes before taking their common seam.</summary>
internal static class ArenaRemoveTheDeadContract
{
    // Forge displays a lower-case identifier; the actual released BepInPlugin does not.
    internal const string PluginGuid = "com.jbs4bmx.RemoveTheDead";
    internal static readonly Version ReviewedVersion = new(4, 1, 3, 0);
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);

    internal static MethodInfo Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Remove The Dead 4.1.3 or newer is required");
        if (plugin == null || plugin.FullName != "RemoveTheDead.RTDPlugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Remove The Dead plugin identity changed");
        var identity = plugin.Assembly.GetName();
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (identity.Name != "RemoveTheDead" || Normalize(identity.Version) != version
            || attribute?.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Remove The Dead plugin and assembly metadata disagree");

        var remove = Method(plugin, "RemoveBotAndWeapons", false, typeof(void), typeof(BotOwner));
        var weapons = Method(plugin, "RemoveNearbyWeapons", false, typeof(void), typeof(Vector3), typeof(float));
        var manual = Method(plugin, "RunRemovalNow", true, typeof(void));
        var queue = Method(plugin, "QueueRemoval", false, typeof(IEnumerator));
        var update = Method(plugin, "Update", true, typeof(void));
        var button = Method(plugin, "RTDButtonActionDrawer", true, typeof(void), typeof(ConfigEntryBase));
        // Inspect the object actually constructed, not merely an iterator attribute.
        var constructor = Calls(queue).OfType<ConstructorInfo>().SingleOrDefault(c => c.DeclaringType?.DeclaringType == plugin
            && typeof(IEnumerator).IsAssignableFrom(c.DeclaringType)
            && c.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(int) }));
        var iterator = constructor?.DeclaringType;
        if (iterator == null)
            throw new MissingMemberException("Remove The Dead automatic-cleanup iterator changed");
        var move = Method(iterator, "MoveNext", false, typeof(bool));
        RequiresCall(update, queue);
        RequiresCall(update, manual);
        RequiresCall(button, manual);
        RequiresCall(move, remove);
        RequiresCall(manual, remove);
        RequiresCall(remove, weapons);
        var destroy = typeof(Object).GetMethod(nameof(Object.Destroy), BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(Object) }, null);
        RequiresCall(remove, destroy);
        RequiresCall(weapons, destroy);

        // Neither entry may acquire a second destruction route around the guarded
        // method. Review a changed call graph instead of accepting its version alone.
        ValidateEntryGraph(new[] { update, button, manual, queue, move }, remove, plugin.Assembly);
        return remove;
    }

    private static void ValidateEntryGraph(IEnumerable<MethodBase> entries, MethodInfo guarded, Assembly assembly)
    {
        var pending = new Stack<MethodBase>(entries);
        var visited = new HashSet<MethodBase>();
        while (pending.Count != 0)
        {
            var method = pending.Pop();
            if (method == guarded || !visited.Add(method)) continue;
            foreach (var call in Calls(method))
            {
                if (IsDestructive(call))
                    throw new MissingMethodException("Remove The Dead cleanup bypasses its common guarded method");
                // Include newly introduced local helpers and iterator-finally
                // methods: a helper call must not conceal a second cleanup route.
                if (call.DeclaringType?.Assembly == assembly) pending.Push(call);
            }
        }
    }

    private static MethodInfo Method(Type type, string name, bool isPublic, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.DeclaredOnly
            | (isPublic ? BindingFlags.Public : BindingFlags.NonPublic), null, args, null);
        if (method == null || method.IsStatic || (!isPublic && !method.IsPrivate) || method.ReturnType != result || method.ContainsGenericParameters
            || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static void RequiresCall(MethodBase caller, MethodBase target)
    {
        if (target == null || !Calls(caller).Contains(target))
            throw new MissingMethodException("Remove The Dead cleanup call chain changed: " + caller.Name);
    }

    private static bool IsDestructive(MethodBase method)
        => method.DeclaringType == typeof(Object) && (method.Name == "Destroy" || method.Name == "DestroyImmediate")
            || method.DeclaringType == typeof(GameWorld) && method.Name == "DestroyLoot"
            || method.DeclaringType?.FullName == "EFT.AssetsManager.AssetPoolObject" && method.Name == "ReturnToPool"
            || method.DeclaringType?.FullName == "EFT.Interactive.Corpse" && method.Name == "Kill";

    // Read IL operands, not raw byte patterns: tokens and embedded numeric values
    // can contain the same bytes as call opcodes. No foreign method is executed.
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new MissingMethodException("Remove The Dead entry has no managed body");
        int at = 0;
        while (at < il.Length)
        {
            short code = il[at++];
            if (code == 0xfe)
            {
                if (at >= il.Length) throw new InvalidOperationException("Truncated Remove The Dead IL");
                code = (short)(0xfe00 | il[at++]);
            }
            if (!Codes.TryGetValue(code, out var op)) throw new InvalidOperationException("Unknown Remove The Dead IL");
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
                    if (at + 4 > il.Length) throw new InvalidOperationException("Truncated Remove The Dead switch");
                    int count = BitConverter.ToInt32(il, at);
                    if (count < 0 || count > (il.Length - at - 4) / 4) throw new InvalidOperationException("Invalid Remove The Dead switch");
                    size = 4 + count * 4;
                    break;
                default: size = 4; break;
            }
            if (at + size > il.Length) throw new InvalidOperationException("Truncated Remove The Dead operand");
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, at));
            at += size;
        }
    }

    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null
            : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
