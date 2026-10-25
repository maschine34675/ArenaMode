using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using EFT.InventoryLogic;

namespace ArenaMode.Compat;

/// <summary>
/// UIA owns item-access slots whenever loaded, including when its features are
/// switched off. Arena never changes those preferences or widens its answer.
/// No foreign implementation is copied and no constructed generic is patched.
/// </summary>
internal static class UseItemsAnywhereCompat
{
    internal const string PluginGuid = "com.cj.useFromAnywhere";
    private const string QueryType = "UseItemsAnywhere.Patches.InventorySlotQueries";
    private static readonly Version ReviewedVersion = new Version(2, 1, 6, 0);
    private static BepInEx.PluginInfo _observed;
    private static object _observedInstance;
    private static Action<InventoryController, IList<Magazine>, Predicate<Magazine>> _collect;
    private static bool _reportedFailure;
    private static bool _reportedNewerVersion;

    internal static bool OwnsItemAccess
    {
        get
        {
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info)) return false;
            Observe(info);
            // A changed/missing contract must not silently turn Arena's broader
            // slots back on over an installed foreign mod's restrictions.
            return true;
        }
    }

    internal static void Enable() { _ = OwnsItemAccess; }

    private static void Observe(BepInEx.PluginInfo info)
    {
        if (ReferenceEquals(info, _observed) && ReferenceEquals(info.Instance, _observedInstance)) return;
        _observed = info;
        _observedInstance = info.Instance;
        _collect = null;
        try
        {
            var assembly = info.Instance?.GetType().Assembly
                ?? throw new InvalidOperationException("plugin instance is unavailable");
            if (!HasIdentity(assembly)) throw new InvalidOperationException("plugin assembly identity changed");
            var query = assembly.GetType(QueryType, true);
            var collect = query.GetMethod("CollectReloadItems", BindingFlags.Static | BindingFlags.NonPublic);
            var find = query.GetMethod("ReloadItems", BindingFlags.Static | BindingFlags.NonPublic);
            if (!IsQueryDefinition(collect, true) || !IsQueryDefinition(find, false))
                throw new MissingMethodException("magazine query signature changed");
            _collect = (Action<InventoryController, IList<Magazine>, Predicate<Magazine>>)Delegate.CreateDelegate(
                typeof(Action<InventoryController, IList<Magazine>, Predicate<Magazine>>), collect.MakeGenericMethod(typeof(Magazine)));
            var version = info.Metadata?.Version;
            var normalized = version == null || version.Build < 0 ? null
                : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
            if (normalized == null || normalized < ReviewedVersion)
                throw new NotSupportedException("reviewed baseline is Use Items Anywhere 2.1.6");
            Plugin.ModLogger.LogDebug("Arena item access and magazine slots are owned by Use Items Anywhere " + version + ".");
            if (normalized > ReviewedVersion && !_reportedNewerVersion)
            {
                _reportedNewerVersion = true;
                Plugin.ModLogger.LogWarning("Arena is using Use Items Anywhere " + version
                    + " item-access rules after API checks; this version is newer than reviewed 2.1.6 and compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            _collect = null;
            Warn(ex);
        }
    }

    // HUD readers use the same foreign candidate query as actual reload. A
    // failed contract has no guessed backpack count: the caller hides that readout.
    internal static bool TryCollectMagazines(InventoryController controller, IList<Magazine> output, Predicate<Magazine> predicate)
    {
        if (!OwnsItemAccess || _collect == null) return false;
        try { _collect(controller, output, predicate); return true; }
        catch (Exception ex) { Warn(ex); return false; }
    }

    private static void Warn(Exception ex)
    {
        if (_reportedFailure) return;
        _reportedFailure = true;
        Plugin.ModLogger.LogWarning("Arena cannot verify Use Items Anywhere magazine access: " + ex.GetBaseException().Message
            + ". Its item-access rules remain authoritative; Arena's reach extension and spare-magazine readout are unavailable.");
    }

    internal static bool IsMagazineQuery(MethodInfo method, bool nonAlloc)
    {
        return method != null && method.IsGenericMethod && !method.ContainsGenericParameters
            && method.GetGenericArguments().SequenceEqual(new[] { typeof(Magazine) })
            && IsQueryDefinition(method.GetGenericMethodDefinition(), nonAlloc)
            && HasIdentity(method.DeclaringType.Assembly);
    }

    private static bool HasIdentity(Assembly assembly)
    {
        if (assembly.GetName().Name != "UseItemsAnywhere") return false;
        var plugin = assembly.GetType("UseItemsAnywhere.Plugin", false);
        var metadata = plugin?.GetCustomAttribute<BepInPlugin>();
        return metadata?.GUID == PluginGuid;
    }

    private static bool IsQueryDefinition(MethodInfo method, bool nonAlloc)
    {
        if (method == null || !method.IsStatic || !method.IsGenericMethodDefinition
            || method.DeclaringType?.FullName != QueryType
            || method.Name != (nonAlloc ? "CollectReloadItems" : "ReloadItems")) return false;
        var args = method.GetGenericArguments();
        if (args.Length != 1) return false;
        var item = args[0];
        if (!item.GetGenericParameterConstraints().Contains(typeof(Item))) return false;
        var expected = nonAlloc
            ? new[] { typeof(InventoryController), typeof(IList<>).MakeGenericType(item), typeof(Predicate<>).MakeGenericType(item) }
            : new[] { typeof(InventoryController), typeof(Predicate<>).MakeGenericType(item) };
        return method.GetParameters().Select(p => p.ParameterType).SequenceEqual(expected)
            && method.ReturnType == (nonAlloc ? typeof(void) : typeof(IEnumerable<>).MakeGenericType(item));
    }
}
