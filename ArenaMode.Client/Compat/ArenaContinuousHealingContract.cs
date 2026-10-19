using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Foreign methods are identified and guarded, never copied or invoked for discovery.</summary>
internal static class ArenaContinuousHealingContract
{
    internal const string PluginGuid = "com.lacyway.ch";
    internal static readonly Version ReviewedVersion = new(1, 6, 3, 0);

    internal static MethodInfo[] Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Continuous Healing 1.6.3 or newer is required");
        if (plugin == null || plugin.FullName != "ContinuousHealing.CH_Plugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Continuous Healing plugin identity changed");
        var identity = plugin.Assembly.GetName();
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (identity.Name != "ContinuousHealing" || Normalize(identity.Version) != version
            || attribute?.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Continuous Healing plugin and assembly metadata disagree");

        var end = Prefix(plugin.Assembly, "CH_EndHeal_Patch", typeof(bool),
            typeof(Player.MedsController.MedsInHandsOperation), typeof(IHealthEffect));
        var start = Prefix(plugin.Assembly, "CH_StartHeal_Patch", typeof(void), typeof(Player));
        var cancel = Prefix(plugin.Assembly, "CH_CancelHeal_Patch", typeof(void), typeof(Player));
        var methods = new[] { end, start, cancel };
        VerifyNativeBindings(methods);
        return methods;
    }

    internal static void VerifyNativeBindings(MethodInfo[] prefixes)
    {
        var targets = new[] {
            typeof(Player.MedsController.MedsInHandsOperation).GetMethod("OnEffectRemoved", new[] { typeof(IHealthEffect) }),
            typeof(Player.MedsController).GetMethod("Spawn", new[] { typeof(float), typeof(Action) }),
            typeof(PlayerHealthController).GetMethod("CancelApplyingItem", Type.EmptyTypes)
        };
        for (int i = 0; i < targets.Length; ++i)
        {
            var target = targets[i];
            var info = target == null ? null : Harmony.GetPatchInfo(target);
            if (target == null || target.IsStatic || target.ReturnType != typeof(void)
                || info == null || info.Prefixes.Count(p => p.PatchMethod == prefixes[i]) != 1
                || info.Prefixes.Any(p => p.PatchMethod.DeclaringType?.Assembly == prefixes[i].DeclaringType.Assembly
                    && p.PatchMethod != prefixes[i])
                || info.Postfixes.Concat(info.Transpilers).Concat(info.Finalizers)
                    .Any(p => p.PatchMethod.DeclaringType?.Assembly == prefixes[i].DeclaringType.Assembly))
                throw new MissingMethodException("Continuous Healing native hook changed: " + (target?.Name ?? "missing target"));
        }
    }

    private static MethodInfo Prefix(Assembly assembly, string name, Type result, params Type[] args)
    {
        var type = assembly.GetType("ContinuousHealing.Patches." + name, true);
        var method = type.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null, args, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, "Prefix");
        return method;
    }

    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null
            : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
