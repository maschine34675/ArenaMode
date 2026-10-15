using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
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
    public const int PickupLayerPriority = 35;

    private static readonly Version RequiredBigBrainVersion = new(1, 5, 0);
    private static readonly Version RequiredArenaVersion = new(0, 1, 0);

    internal static bool RuntimeEnabled { get; private set; }
    internal static ManualLogSource ModLogger { get; private set; }
    private bool _consumerRegistered;
    private float _nextPickupPrune;

    private void Update()
    {
        if (UnityEngine.Time.time < _nextPickupPrune) return;
        _nextPickupPrune = UnityEngine.Time.time + .5f;
        try { ArenaPickupObjective.Prune(); }
        catch { ArenaPickupObjective.ResetClaims(); }
    }

    private void Awake()
    {
        ModLogger = Logger;
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
            int pickupLayerId = BrainManager.AddCustomLayer(typeof(ArenaPickupLayer), brains, PickupLayerPriority);
            if (!BrainManager.CustomLayersReadOnly.TryGetValue(pickupLayerId, out var pickupLayer)
                || pickupLayer.customLayerType != typeof(ArenaPickupLayer)
                || pickupLayer.customLayerPriority != PickupLayerPriority
                || brains.Any(brain => !pickupLayer.CustomLayerBrains.Contains(brain)))
                throw new InvalidOperationException("BigBrain did not retain the Arena pickup layer registration.");
            RuntimeEnabled = true;
            int fireLayerId = BrainManager.AddCustomLayer(typeof(ArenaFireEscapeLayer), brains, 79);
            if (!BrainManager.CustomLayersReadOnly.TryGetValue(fireLayerId, out var fireLayer)
                || fireLayer.customLayerType != typeof(ArenaFireEscapeLayer) || fireLayer.customLayerPriority != 79
                || brains.Any(brain => !fireLayer.CustomLayerBrains.Contains(brain)))
                throw new InvalidOperationException("BigBrain did not retain the Arena ground fire escape layer.");
            Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = true;
            Pickups.ArenaPickupRuntime.BotObjectivesAvailable = true;
            Logger.LogDebug($"Arena pickup layer {pickupLayerId} registered at priority {PickupLayerPriority}: live Deathmatch/TDM; useful health, shield and damage only.");
            Logger.LogDebug(
                $"Arena pursuit layer {layerId} registered for {brains.Count} brains "
                + $"at priority {PursuitLayerPriority} (BigBrain {SupportedBigBrainVersion}).");
            Logger.LogInfo("Arena bot movement support is ready (BigBrain).");
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
        Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = false;
        Pickups.ArenaPickupRuntime.BotObjectivesAvailable = false;
        ArenaPickupObjective.ResetClaims();
        if (_consumerRegistered)
        {
            Bots.TeamArenaPursuitLeaseBridge.UnregisterConsumer(PluginGuid);
            _consumerRegistered = false;
        }

        ModLogger = null;
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
