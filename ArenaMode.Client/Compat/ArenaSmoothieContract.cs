using System;
using System.Reflection;
using BepInEx;

namespace ArenaMode.Compat;

/// <summary>Optional reflected seam; no Smoothie code or binary is distributed.</summary>
internal static class ArenaSmoothieContract
{
    internal const string PluginGuid = "com.tominocz.smoothie";
    internal static readonly Version ReviewedVersion = new(1, 0, 8, 0);

    internal static Version ValidateIdentity(Type pluginType, string guid, Version metadataVersion)
    {
        var version = Normalize(metadataVersion);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Smoothie 1.0.8 or newer is required for this optional adapter");
        if (pluginType == null || pluginType.FullName != "Smoothie.Plugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(pluginType))
            throw new MissingMemberException("Smoothie plugin identity changed");
        var name = pluginType.Assembly.GetName();
        var attribute = pluginType.GetCustomAttribute<BepInPlugin>(false);
        if (name.Name != "Smoothie" || Normalize(name.Version) != version || attribute == null
            || attribute.GUID != PluginGuid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Smoothie plugin and assembly metadata do not match");
        return version;
    }

    internal static MethodInfo ResolveCleanup(Type pluginType)
    {
        var diagnostics = pluginType.Assembly.GetType("Smoothie.Diagnostics", true);
        var method = diagnostics.GetMethod("KeepCleanupPaused", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        if (!diagnostics.IsAbstract || !diagnostics.IsSealed || method == null || !method.IsAssembly
            || method.ReturnType != typeof(void) || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException("Smoothie.Diagnostics.KeepCleanupPaused(): void changed");
        return method;
    }

    internal static Version Normalize(Version version)
        => version == null || version.Build < 0 ? null
            : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}
