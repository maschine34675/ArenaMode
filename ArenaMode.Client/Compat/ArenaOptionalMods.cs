using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using Comfort.Common;
using HarmonyLib;

namespace ArenaMode.Compat;

/// <summary>
/// Admission only. Never disables another plugin, changes its configuration,
/// or prevents the ordinary PMC/Scav path from running.
/// </summary>
internal static class ArenaOptionalMods
{
    internal const string QuestingBotsGuid = "com.danw.questingbots";
    internal const string OrbitGuid = "com.chazut.orbit";
    private static object _questingInstance, _questingConfig;
    private static PropertyInfo _questingEnabled;
    private static bool _questingBlocked;

    internal static string GetRejectionReason()
    {
        if (Chainloader.PluginInfos.ContainsKey(OrbitGuid))
            return "Arena is currently unavailable with ORBIT. Ordinary PMC/Scav raids remain available.";

        if (Chainloader.PluginInfos.TryGetValue(QuestingBotsGuid, out var info)
            && !QuestingBotsDisabledAtStartup(info.Instance, info.Metadata?.Version))
            return "Arena is currently unavailable with active Questing Bots. Disable it in its server configuration and restart; ordinary PMC/Scav raids remain available.";

        return ArenaMoreBotsCompatibility.GetRejectionReason()
            ?? ArenaAiLimitCompat.GetRejectionReason()
            ?? ArenaLootingBotsCompat.GetRejectionReason()
            ?? ArenaRemoveTheDeadCompat.GetRejectionReason()
            ?? ArenaSmoothieCompat.GetRejectionReason();
    }

    // Only the reviewed whole-mod startup switch is an exemption. F12 questing,
    // spawning and limiter switches do not remove the other installed systems.
    // Never invoke ConfigUtil.CurrentConfig: that getter may perform HTTP I/O.
    private static bool QuestingBotsDisabledAtStartup(object instance, Version version)
    {
        if (instance == null) return false;
        try
        {
            if (!ReferenceEquals(instance, _questingInstance))
            {
                _questingInstance = instance;
                _questingBlocked = true;
                _questingConfig = null;
                _questingEnabled = null;
                // Unknown releases stay blocked, including their off-switch:
                // this is not a forward-compatible gameplay adapter.
                if (version == null || version.Major != 1 || version.Minor != 1
                    || version.Build != 0 || version.Revision > 0) return false;
                var assembly = instance.GetType().Assembly;
                var configType = assembly.GetType("QuestingBots.Utils.ConfigUtil", true);
                var singleton = typeof(Singleton<>).MakeGenericType(configType);
                var owner = singleton.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var config = configType.GetField("_currentConfig", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
                var enabled = config?.GetType().GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public);
                if (enabled?.PropertyType != typeof(bool) || (bool)enabled.GetValue(config)) return false;

                // A runtime error can switch the cached flag off after patches
                // were installed. Only MenuShowPatch is present on the actual
                // disabled-at-startup path. Perform this scan once, not per UI frame.
                foreach (var method in Harmony.GetAllPatchedMethods())
                {
                    var patches = Harmony.GetPatchInfo(method);
                    if (patches == null) continue;
                    if (patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers)
                        .Any(p => p.PatchMethod?.DeclaringType?.Assembly == assembly
                            && p.PatchMethod.DeclaringType.FullName != "QuestingBots.Patches.MenuShowPatch"))
                        return false;
                }
                _questingConfig = config;
                _questingEnabled = enabled;
                _questingBlocked = false;
            }
            // Once observed active/unknown, changing a flag cannot prove that
            // Harmony patches, generators and brain layers have been removed.
            if (!_questingBlocked && (bool)_questingEnabled.GetValue(_questingConfig))
                _questingBlocked = true;
            return !_questingBlocked;
        }
        catch (Exception)
        {
            _questingBlocked = true;
            return false;
        }
    }
}
