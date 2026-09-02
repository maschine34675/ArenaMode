using BepInEx;
using BepInEx.Bootstrap;
using DrakiaXYZ.BigBrain.Brains;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.BigBrainCompat;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(ArenaMode.Plugin.PluginGuid, ArenaMode.Plugin.PluginVersion)]
[BepInDependency(BigBrainPluginGuid, SupportedBigBrainVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.ArenaMode.BigBrainCompat";
    public const string PluginName = "maschine-ArenaMode.BigBrainCompat";
    public const string PluginVersion = "0.1.0";
    public const string BigBrainPluginGuid = "xyz.drakia.bigbrain";
    public const string SupportedBigBrainVersion = "1.5.0";
    public const int PursuitLayerPriority = 74;

    private static readonly Version RequiredBigBrainVersion = new(1, 5, 0);
    private static readonly Version RequiredArenaVersion = new(0, 1, 0);

    internal static bool RuntimeEnabled { get; private set; }
    private bool _consumerRegistered;

    private void Awake()
    {
        RuntimeEnabled = false;

        bool hasBigBrain = HasExactDependency(
            BigBrainPluginGuid,
            RequiredBigBrainVersion,
            out string bigBrainProblem);
        bool hasArena = HasExactDependency(
            ArenaMode.Plugin.PluginGuid,
            RequiredArenaVersion,
            out string arenaProblem);
        if (!hasBigBrain || !hasArena)
        {
            Logger.LogError(
                "Arena BigBrain movement ownership stays disabled: "
                + (bigBrainProblem ?? arenaProblem));
            enabled = false;
            return;
        }

        try
        {
            var brains = new List<string>
            {
                "PmcBear",
                "PmcUsec",
                "ExUsec",
                "PMC",
                "Assault",
                "Obdolbs",
                "CursAssault"
            };

            int layerId = BrainManager.AddCustomLayer(
                typeof(ArenaPursuitLayer),
                brains,
                PursuitLayerPriority);

            if (!BrainManager.CustomLayersReadOnly.TryGetValue(layerId, out BrainManager.LayerInfo layer)
                || layer.customLayerType != typeof(ArenaPursuitLayer)
                || layer.customLayerPriority != PursuitLayerPriority
                || brains.Any(brain => !layer.CustomLayerBrains.Contains(brain)))
            {
                throw new InvalidOperationException(
                    "BigBrain did not retain the exact Arena pursuit layer registration.");
            }

            if (!Bots.TeamArenaPursuitLeaseBridge.RegisterConsumer(PluginGuid))
            {
                throw new InvalidOperationException(
                    "Arena's pursuit lease bridge is already owned by another consumer.");
            }

            _consumerRegistered = true;
            RuntimeEnabled = true;
            Logger.LogInfo(
                $"Arena pursuit layer {layerId} registered for {brains.Count} brains "
                + $"at priority {PursuitLayerPriority} (BigBrain {SupportedBigBrainVersion}).");
        }
        catch (Exception ex)
        {
            RuntimeEnabled = false;
            if (_consumerRegistered)
            {
                Bots.TeamArenaPursuitLeaseBridge.UnregisterConsumer(PluginGuid);
                _consumerRegistered = false;
            }
            Logger.LogError($"Arena BigBrain movement ownership could not be registered: {ex}");
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        RuntimeEnabled = false;
        if (_consumerRegistered)
        {
            Bots.TeamArenaPursuitLeaseBridge.UnregisterConsumer(PluginGuid);
            _consumerRegistered = false;
        }
    }

    private static bool HasExactDependency(string guid, Version required, out string problem)
    {
        problem = null;
        if (!Chainloader.PluginInfos.TryGetValue(guid, out BepInEx.PluginInfo pluginInfo))
        {
            problem = $"hard dependency '{guid}' is missing";
            return false;
        }

        Version actual = pluginInfo.Metadata?.Version;
        if (actual == null || actual != required)
        {
            problem = $"'{guid}' is {actual?.ToString() ?? "unknown"}; exact {required} is required";
            return false;
        }

        return true;
    }
}
