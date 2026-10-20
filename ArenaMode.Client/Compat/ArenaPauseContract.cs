using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Optional named-type and activation graph contract; never a hard Pause dependency.</summary>
internal sealed class ArenaPauseContract
{
    internal const string PluginGuid = "com.rejzbihai.pause";
    internal static readonly Version ReviewedVersion = new(1, 5, 0, 0);
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);
    internal readonly MethodInfo Update;
    internal readonly FieldInfo CachedWorld, CachedGame, TogglePause;
    internal readonly Func<bool> IsPaused;
    internal readonly Func<KeyboardShortcut, bool> IsKeyPressed;

    private ArenaPauseContract(Type plugin)
    {
        var assembly = plugin.Assembly;
        var controller = assembly.GetType("Pause.PauseController", true);
        if (!typeof(MonoBehaviour).IsAssignableFrom(controller) || controller.IsAbstract)
            throw new MissingMemberException("Pause controller type changed");
        Update = Method(controller, "Update", Instance, typeof(void));
        var pause = Method(controller, "Pause", Instance, typeof(void));
        var unpause = Method(controller, "Unpause", Instance, typeof(void));
        var awake = Method(controller, "Awake", Instance, typeof(void));
        var destroy = Method(controller, "OnDestroy", Instance, typeof(void));
        var key = Method(controller, "IsKeyPressed", Static, typeof(bool), typeof(KeyboardShortcut));
        var paused = controller.GetProperty("IsPaused", Static);
        var getPaused = paused?.GetGetMethod(true);
        var setPaused = paused?.GetSetMethod(true);
        if (paused?.PropertyType != typeof(bool) || getPaused?.ReturnType != typeof(bool)
            || !getPaused.IsStatic || setPaused == null || !setPaused.IsStatic
            || setPaused.ReturnType != typeof(void) || setPaused.GetParameters().Length != 1
            || setPaused.GetParameters()[0].ParameterType != typeof(bool))
            throw new MissingMemberException("Pause state accessors changed");
        CachedWorld = Field(controller, "GameWorld", Static, typeof(GameWorld));
        CachedGame = Field(controller, "_abstractGame", Instance, typeof(AbstractGame));
        TogglePause = Field(plugin, "TogglePause", Static, typeof(ConfigEntry<KeyboardShortcut>));
        IsPaused = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getPaused);
        IsKeyPressed = (Func<KeyboardShortcut, bool>)Delegate.CreateDelegate(typeof(Func<KeyboardShortcut, bool>), key);
        // Every activation must pass the guarded Update. Initialization and
        // disposal may clear the flag, but may not silently activate it.
        var pauseCallers = new List<MethodBase>();
        var setterCallers = new HashSet<MethodBase>();
        foreach (var type in assembly.GetTypes())
        foreach (var method in type.GetMethods(Static | Instance).Cast<MethodBase>()
            .Concat(type.GetConstructors(Static | Instance)))
        {
            if (method.GetMethodBody() == null) continue;
            var instructions = Read(method).ToArray();
            for (int i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                if (instruction.Member == pause) pauseCallers.Add(method);
                if (instruction.Member != setPaused) continue;
                setterCallers.Add(method);
                if (method == Update) continue;
                int previous = i - 1;
                while (previous >= 0 && instructions[previous].Op == OpCodes.Nop) previous--;
                if ((method != awake && method != destroy) || previous < 0
                    || instructions[previous].Op != OpCodes.Ldc_I4_0)
                    throw new MissingMethodException("Pause has an unguarded state writer");
            }
        }
        var updateMembers = Read(Update).Select(i => i.Member).ToArray();
        if (pauseCallers.Count != 1 || pauseCallers[0] != Update
            || !setterCallers.SetEquals(new[] { Update, awake, destroy })
            || updateMembers.Count(m => m == key) != 1
            || updateMembers.Count(m => m == setPaused) != 1
            || updateMembers.Count(m => m == unpause) != 1
            || updateMembers.Count(m => m == TogglePause) != 1
            || Array.IndexOf(updateMembers, key) > Array.IndexOf(updateMembers, setPaused))
            throw new MissingMethodException("Pause activation graph changed");
    }

    internal static ArenaPauseContract Resolve(Type plugin, string guid, Version metadata, out Version version)
    {
        version = Normalize(metadata);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Pause Continued 1.5.0 or newer is required");
        if (plugin == null || plugin.FullName != "Pause.Plugin" || plugin.IsAbstract
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("Pause plugin identity changed");
        var identity = plugin.Assembly.GetName();
        var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (identity.Name != "rejzbihai.Pause" || Normalize(identity.Version) != version
            || attr?.GUID != PluginGuid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("Pause plugin and assembly metadata disagree");
        var settings = AccessTools.Field(typeof(LocalGame), "_raidSettings");
        if (settings == null || settings.IsStatic || settings.FieldType != typeof(LocalRaidSettings))
            throw new MissingFieldException("LocalGame._raidSettings changed");
        return new ArenaPauseContract(plugin);
    }

    private static FieldInfo Field(Type type, string name, BindingFlags flags, Type value)
    {
        var field = type.GetField(name, flags);
        if (field == null || field.FieldType != value || field.IsInitOnly)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static MethodInfo Method(Type type, string name, BindingFlags flags, Type result, params Type[] parameters)
    {
        var method = type.GetMethod(name, flags, null, parameters, null);
        if (method?.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static IEnumerable<(OpCode Op, MemberInfo Member)> Read(MethodBase method)
    {
        var il = method.GetMethodBody().GetILAsByteArray();
        int at = 0;
        while (at < il.Length)
        {
            short code = il[at++];
            if (code == 0xfe)
            {
                if (at == il.Length) throw new InvalidOperationException("Truncated Pause IL");
                code = (short)(0xfe00 | il[at++]);
            }
            if (!Codes.TryGetValue(code, out var op)) throw new InvalidOperationException("Unknown Pause IL");
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch:
                    if (at + 4 > il.Length) throw new InvalidOperationException("Truncated Pause switch");
                    int count = BitConverter.ToInt32(il, at);
                    if (count < 0 || count > (il.Length - at - 4) / 4) throw new InvalidOperationException("Invalid Pause switch");
                    size = 4 + count * 4; break;
                default: size = 4; break;
            }
            if (at + size > il.Length) throw new InvalidOperationException("Truncated Pause operand");
            MemberInfo member = null;
            if (op.OperandType == OperandType.InlineField || op.OperandType == OperandType.InlineMethod)
                member = method.Module.ResolveMember(BitConverter.ToInt32(il, at),
                    method.DeclaringType?.GetGenericArguments(), (method as MethodInfo)?.GetGenericArguments());
            yield return (op, member);
            at += size;
        }
    }
    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null
            : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
