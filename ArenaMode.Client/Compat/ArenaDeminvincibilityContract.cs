using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using Version = System.Version;

namespace ArenaMode.Compat;

internal static class ArenaDeminvincibilityContract
{
    internal const string PluginGuid = "com.hazelify.deminvincibility";
    internal static readonly Version ReviewedVersion = new(2, 2, 0, 0);

    internal static MethodInfo[] Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Deminvincibility 2.2.0 or newer is required");
        if (plugin == null || plugin.FullName != "Deminvincibility.DeminvicibilityPlugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Deminvincibility plugin identity changed");
        var identity = plugin.Assembly.GetName();
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        // Published 2.2.0 retains the original assembly version 1.0.0.0.
        if (identity.Name != "Deminvincibility"
            || (Normalize(identity.Version) != new Version(1, 0, 0, 0) && Normalize(identity.Version) != version)
            || attribute?.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Deminvincibility metadata disagree");
        var methods = new[] {
            Prefix(plugin.Assembly, "Kill", typeof(ActiveHealthController), typeof(EDamageType), typeof(Player)),
            Prefix(plugin.Assembly, "DestroyBodyPartPatch", typeof(ActiveHealthController), typeof(Player), typeof(EBodyPart), typeof(EDamageType)),
            Prefix(plugin.Assembly, "ApplyDamage", typeof(ActiveHealthController), typeof(Player), typeof(float).MakeByRefType(), typeof(EBodyPart), typeof(DamageInfo)),
            Prefix(plugin.Assembly, "DoFracture", typeof(ActiveHealthController), typeof(EBodyPart), typeof(Player))
        };
        VerifyNativeBindings(methods);
        FindKeepRead(PatchProcessor.GetOriginalInstructions(methods[2]).ToList(), plugin);
        if (AccessTools.Field(typeof(ActiveHealthController), "Player")?.FieldType != typeof(Player)
            || AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer), new[] { typeof(IPlayer) })?.ReturnType != typeof(void)
            || AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted), Type.EmptyTypes)?.ReturnType != typeof(void))
            throw new MissingMemberException("Deminvincibility native ownership lifecycle changed");
        return methods;
    }

    internal static MethodInfo[] NativeTargets() => new[] {
        AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill), new[] { typeof(EDamageType) }),
        AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DestroyBodyPart), new[] { typeof(EBodyPart), typeof(EDamageType) }),
        AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage), new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) }),
        AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoFracture), new[] { typeof(EBodyPart) })
    };

    internal static void VerifyNativeBindings(MethodInfo[] methods)
    {
        var targets = NativeTargets();
        for (int i = 0; i < targets.Length; ++i)
        {
            var target = targets[i]; var info = target == null ? null : Harmony.GetPatchInfo(target);
            if (target == null || target.IsStatic || target.ReturnType != (i == 2 ? typeof(float) : typeof(void))
                || info?.Prefixes.Count(p => p.PatchMethod == methods[i]) != 1)
                throw new MissingMethodException("Deminvincibility native prefix changed: " + i);
        }
        // Future unguarded health/death hooks must not be admitted based on a version alone.
        foreach (var target in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(target);
            var foreign = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Concat(info.ILManipulators)
                .Where(p => p.PatchMethod.DeclaringType?.Assembly == methods[0].DeclaringType.Assembly).ToArray();
            int index = Array.IndexOf(targets, target);
            if (foreign.Length != 0 && (index < 0 || foreign.Length != 1 || foreign[0].PatchMethod != methods[index]
                || !info.Prefixes.Contains(foreign[0])))
                throw new MissingMethodException("Deminvincibility has an unreviewed native hook");
        }
    }

    internal static int FindKeepRead(IList<CodeInstruction> instructions, Type plugin)
    {
        var getter = plugin.GetProperty("Keep1Health", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)?.GetGetMethod();
        var value = typeof(ConfigEntry<bool>).GetProperty(nameof(ConfigEntry<bool>.Value))?.GetGetMethod();
        if (getter == null || getter.ReturnType != typeof(ConfigEntry<bool>) || getter.GetParameters().Length != 0)
            throw new MissingMemberException("Deminvincibility Keep1Health API changed");
        int index = -1;
        for (int i = 0; i < instructions.Count; ++i)
        {
            var code = instructions[i];
            // A future helper or direct config-field read could clamp damage a
            // second time outside the guarded read. Refuse that unknown graph.
            if (code.operand is MethodInfo call && call.DeclaringType?.Assembly == plugin.Assembly
                && (call.DeclaringType != plugin || !new[] { "get_Keep1Health", "get_MedicineBool", "get_CustomDamageModeVal", "get_Allow0HpLimbs" }.Contains(call.Name)))
                throw new MissingMethodException("Deminvincibility damage helper changed");
            if (code.operand is FieldInfo field && field.DeclaringType?.Assembly == plugin.Assembly
                && (field.DeclaringType.FullName != "Deminvincibility.Patches.ApplyDamage" || field.Name != "bodyParts"
                    || !field.IsStatic || field.FieldType != typeof(EBodyPart[])))
                throw new MissingFieldException("Deminvincibility damage state read changed");
            if (!Equals(code.operand, getter)) continue;
            if (index >= 0 || code.opcode != OpCodes.Call || i + 1 >= instructions.Count
                || instructions[i + 1].opcode != OpCodes.Callvirt || !Equals(instructions[i + 1].operand, value)
                || instructions[i + 1].labels.Count != 0 || instructions[i + 1].blocks.Count != 0)
                throw new MissingMethodException("Deminvincibility damage clamp read changed");
            index = i + 1;
        }
        if (index < 0) throw new MissingMethodException("Deminvincibility damage clamp read missing");
        return index;
    }

    private static MethodInfo Prefix(Assembly assembly, string typeName, params Type[] args)
    {
        var type = assembly.GetType("Deminvincibility.Patches." + typeName, true);
        var method = type.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != typeof(bool) || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, "Prefix");
        return method;
    }
    internal static Version Normalize(Version value)
        => value == null || value.Build < 0 ? null : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}
