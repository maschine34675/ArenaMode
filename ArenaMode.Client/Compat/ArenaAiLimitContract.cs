using System;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace ArenaMode.Compat;

/// <summary>The optional limiter's entry point, never a hard assembly reference.</summary>
internal static class ArenaAiLimitContract
{
    internal const string PluginGuid = "com.dvize.ailimit";
    internal const string AssemblyName = "dvize.AILimit";
    internal static readonly Version ReviewedVersion = new(1, 9, 2, 0);

    internal static MethodInfo Resolve(Type pluginType, string metadataGuid, Version metadataVersion,
        out Version version)
    {
        version = Normalize(metadataVersion);
        if (metadataGuid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("AI Limit 1.9.2 or newer is required");
        if (pluginType == null || pluginType.FullName != "AIlimit.AILimitPlugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(pluginType))
            throw new MissingMemberException("AI Limit plugin identity changed");
        var assembly = pluginType.Assembly;
        var identity = assembly.GetName();
        var attribute = pluginType.GetCustomAttribute<BepInPlugin>(false);
        if (identity.Name != AssemblyName || Normalize(identity.Version) != version
            || attribute == null || attribute.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("AI Limit plugin and assembly metadata do not match");

        var component = assembly.GetType("AILimit.AILimitComponent", true);
        if (!typeof(MonoBehaviour).IsAssignableFrom(component) || component.IsAbstract)
            throw new MissingMemberException("AI Limit component identity changed");
        var enable = component.GetMethod("Enable", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        if (enable == null || enable.ReturnType != typeof(void) || enable.ContainsGenericParameters
            || enable.GetMethodBody() == null)
            throw new MissingMethodException("AILimit.AILimitComponent.Enable(): void changed");
        // The reviewed GameWorld prefix does nothing before calling Enable.
        // Future versions may proceed only while that construction seam remains
        // a direct forwarder; an earlier mutation would escape our guard.
        var entry = assembly.GetType("AIlimit.NewGamePatch", true)?.GetMethod("PatchPrefix",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        if (entry == null || entry.ReturnType != typeof(void) || entry.ContainsGenericParameters
            || !IsDirectForwarder(entry, enable))
            throw new MissingMethodException("AI Limit raid-start forwarder changed");
        return enable;
    }

    private static bool IsDirectForwarder(MethodInfo entry, MethodInfo target)
    {
        var il = entry.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return false;
        int at = 0;
        while (at < il.Length && il[at] == 0) at++; // nop
        if (at + 5 > il.Length || il[at++] != 0x28) return false; // call
        int token = BitConverter.ToInt32(il, at);
        at += 4;
        if (entry.Module.ResolveMethod(token) != target) return false;
        while (at < il.Length && il[at] == 0) at++;
        if (at >= il.Length || il[at++] != 0x2a) return false; // ret
        while (at < il.Length && il[at] == 0) at++;
        return at == il.Length;
    }

    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null
            : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
