using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using HarmonyLib;
using JsonType;
using Version = System.Version;

namespace ArenaMode.Compat;

internal static class ArenaClassicMovementContract
{
    internal const string PluginGuid = "com.boogle.classicmovement";
    internal static readonly Version ReviewedVersion = new(1, 1, 3, 0);

    internal static MethodInfo[] Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Classic Movement 1.1.3 or newer is required");
        if (plugin == null || plugin.FullName != "OldTarkovMovement.Plugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Classic Movement plugin identity changed");
        var assembly = plugin.Assembly.GetName();
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        // The published 1.1.3 DLL deliberately still reports assembly 1.0.0.0.
        if (assembly.Name != "ClassicMovement" || (Normalize(assembly.Version) != new Version(1, 0, 0, 0)
                && Normalize(assembly.Version) != version)
            || attribute?.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Classic Movement plugin and assembly metadata disagree");

        var state = Method(plugin.Assembly, "StateReplacer", typeof(bool), typeof(MovementContext),
            typeof(BaseMovementState).MakeByRefType(), typeof(EPlayerState), typeof(bool));
        var speed = Method(plugin.Assembly, "SmoothSpeedFix", typeof(void), typeof(MovementContext), typeof(float));
        var methods = new[] { state, speed };
        VerifyNativeBindings(plugin, methods);
        var settings = AccessTools.Field(typeof(LocalGame), "_raidSettings");
        if (settings == null || settings.IsStatic || settings.FieldType != typeof(LocalRaidSettings))
            throw new MissingFieldException("LocalGame._raidSettings changed");
        var owner = AccessTools.Field(typeof(MovementContext), "_player");
        if (owner == null || owner.IsStatic || owner.FieldType != typeof(Player))
            throw new MissingFieldException("MovementContext._player changed");
        return methods;
    }

    internal static void VerifyNativeBindings(Type plugin, MethodInfo[] methods)
    {
        var configField = plugin.GetField("ModConfig", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var config = configField?.GetValue(null);
        if (configField?.FieldType.FullName != "OldTarkovMovement.OldTarkovMovementConfig"
            || configField.FieldType.Assembly != plugin.Assembly || config == null)
            throw new MissingFieldException("Classic Movement configuration is not loaded");
        bool noInertia = ConfigBool(config, "NoInertia");
        if (ConfigBool(config, "NostalgiaMode"))
            throw new NotSupportedException("Classic Movement NostalgiaMode is unsupported in Arena; disable that legacy option and restart");
        var targets = new[] {
            typeof(MovementContext).GetMethod("GetNewState", new[] { typeof(EPlayerState), typeof(bool) }),
            typeof(MovementContext).GetMethod("ManualUpdate", new[] { typeof(float) })
        };
        for (int i = 0; i < targets.Length; ++i)
        {
            var target = targets[i];
            var info = target == null ? null : Harmony.GetPatchInfo(target);
            bool expected = i == 1 || noInertia;
            var relevant = i == 0 ? info?.Prefixes : info?.Postfixes;
            int registered = relevant?.Count(p => p.PatchMethod == methods[i]) ?? 0;
            var all = info == null ? Enumerable.Empty<Patch>() : info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers);
            if (target == null || target.IsStatic || target.ReturnType != (i == 0 ? typeof(BaseMovementState) : typeof(void))
                || registered != (expected ? 1 : 0)
                || all.Count(p => p.PatchMethod.DeclaringType?.Assembly == plugin.Assembly) != registered)
                throw new MissingMethodException("Classic Movement native hook changed: " + (target?.Name ?? "missing target"));
        }
    }

    private static bool ConfigBool(object config, string name)
    {
        // Read the audited auto-property storage; never execute a foreign getter.
        var field = config.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != typeof(bool) || field.IsStatic)
            throw new MissingFieldException("Classic Movement config changed: " + name);
        return (bool)field.GetValue(config);
    }

    private static MethodInfo Method(Assembly assembly, string name, Type result, params Type[] args)
    {
        var type = assembly.GetType("OldTarkovMovement." + name, true);
        var method = type.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, "Prefix");
        return method;
    }

    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
