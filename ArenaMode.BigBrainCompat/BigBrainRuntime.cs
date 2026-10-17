using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ArenaMode.BigBrainCompat;

/// <summary>
/// Optional typed boundary. Never move BigBrain types into the plugin entry
/// type or inline this method: the bundled plugin must load without BigBrain.
/// </summary>
internal static class BigBrainRuntime
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Register(ManualLogSource logger, Version actualVersion,
        out Action prunePickups, out Action resetPickups)
    {
        prunePickups = null;
        resetPickups = null;
        var brains = new List<string>
        {
            "PmcBear", "PmcUsec", "ExUsec", "PMC", "Assault", "Obdolbs", "CursAssault"
        };

        int layerId = BrainManager.AddCustomLayer(typeof(ArenaPursuitLayer), brains, Plugin.PursuitLayerPriority);
        if (!BrainManager.CustomLayersReadOnly.TryGetValue(layerId, out BrainManager.LayerInfo layer)
            || layer.customLayerType != typeof(ArenaPursuitLayer)
            || layer.customLayerPriority != Plugin.PursuitLayerPriority
            || brains.Any(brain => !layer.CustomLayerBrains.Contains(brain)))
            throw new InvalidOperationException("BigBrain did not retain the exact Arena pursuit layer registration.");

        if (!Bots.TeamArenaPursuitLeaseBridge.RegisterConsumer(Plugin.PluginGuid))
            throw new InvalidOperationException("Arena's pursuit lease bridge is already owned by another consumer.");

        int pickupLayerId = BrainManager.AddCustomLayer(typeof(ArenaPickupLayer), brains, Plugin.PickupLayerPriority);
        if (!BrainManager.CustomLayersReadOnly.TryGetValue(pickupLayerId, out var pickupLayer)
            || pickupLayer.customLayerType != typeof(ArenaPickupLayer)
            || pickupLayer.customLayerPriority != Plugin.PickupLayerPriority
            || brains.Any(brain => !pickupLayer.CustomLayerBrains.Contains(brain)))
            throw new InvalidOperationException("BigBrain did not retain the Arena pickup layer registration.");

        int fireLayerId = BrainManager.AddCustomLayer(typeof(ArenaFireEscapeLayer), brains, 79);
        if (!BrainManager.CustomLayersReadOnly.TryGetValue(fireLayerId, out var fireLayer)
            || fireLayer.customLayerType != typeof(ArenaFireEscapeLayer)
            || fireLayer.customLayerPriority != 79
            || brains.Any(brain => !fireLayer.CustomLayerBrains.Contains(brain)))
            throw new InvalidOperationException("BigBrain did not retain the Arena ground fire escape layer.");

        // Do not advertise any capability until all three actual registrations
        // passed their retained type/priority/brain checks.
        prunePickups = ArenaPickupObjective.Prune;
        resetPickups = ArenaPickupObjective.ResetClaims;
        logger.LogDebug($"Arena pickup layer {pickupLayerId} registered at priority {Plugin.PickupLayerPriority}: live Deathmatch/TDM; useful health, shield and damage only.");
        logger.LogDebug($"Arena pursuit layer {layerId} registered for {brains.Count} brains "
            + $"at priority {Plugin.PursuitLayerPriority} (BigBrain {actualVersion}).");
    }
}
