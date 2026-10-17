using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System;

namespace ArenaMode.BigBrainCompat;

// Keep this entry type free of BigBrain types (including fields and signatures).
// BepInEx discovers plugins through Cecil, then loads only this named type. A
// soft dependency orders an installed BigBrain first without making its absence
// a loader error; the typed implementation is reached only after the checks.
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(ArenaMode.Plugin.PluginGuid, ArenaMode.Plugin.PluginVersion)]
[BepInDependency(BigBrainPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.ArenaMode.BigBrainCompat";
    public const string PluginName = "maschine-ArenaMode.BigBrainCompat";
    public const string PluginVersion = "0.1.0";
    public const string BigBrainPluginGuid = "xyz.drakia.bigbrain";
    public const string TestedBigBrainVersion = "1.5.0";
    public const int PursuitLayerPriority = 74;
    public const int PickupLayerPriority = 35;

    private static readonly Version MinimumBigBrainVersion = new(1, 5, 0);
    private static readonly Version RequiredArenaVersion = new(0, 1, 0);

    internal static bool RuntimeEnabled { get; private set; }
    internal static ManualLogSource ModLogger { get; private set; }
    private Action _prunePickups;
    private Action _resetPickups;
    private float _nextPickupPrune;

    private void Update()
    {
        if (!RuntimeEnabled || _prunePickups == null || UnityEngine.Time.time < _nextPickupPrune) return;
        _nextPickupPrune = UnityEngine.Time.time + .5f;
        try { _prunePickups(); }
        catch { _resetPickups?.Invoke(); }
    }

    private void Awake()
    {
        ModLogger = Logger;
        RuntimeEnabled = false;

        if (!Chainloader.PluginInfos.TryGetValue(BigBrainPluginGuid, out BepInEx.PluginInfo bigBrain))
        {
            // The companion is always shipped. No warning is needed for an
            // intentionally absent optional mod, and no BigBrain type is touched.
            Logger.LogDebug("Arena BigBrain companion is inactive: optional BigBrain is not installed.");
            enabled = false;
            return;
        }

        Version actual = bigBrain.Metadata?.Version;
        if (bigBrain.Instance == null || actual == null || actual < MinimumBigBrainVersion)
        {
            Logger.LogWarning("Arena bot movement support is disabled: BigBrain "
                + $"{actual?.ToString() ?? "unknown"} is not loaded or is older than {MinimumBigBrainVersion}.");
            enabled = false;
            return;
        }

        if (!HasExactArenaDependency(out string arenaProblem))
        {
            Logger.LogError("Arena BigBrain movement ownership stays disabled: " + arenaProblem);
            enabled = false;
            return;
        }

        if (actual > MinimumBigBrainVersion)
            Logger.LogWarning($"Arena BigBrain support was tested with {TestedBigBrainVersion}; "
                + $"installed {actual} is newer. Trying the existing API with runtime safety checks.");

        try
        {
            // NoInlining on Register prevents either JIT from resolving its
            // BigBrain layer types while compiling this optional bootstrap.
            BigBrainRuntime.Register(Logger, actual, out _prunePickups, out _resetPickups);
            RuntimeEnabled = true;
            Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = true;
            Pickups.ArenaPickupRuntime.BotObjectivesAvailable = true;
            Logger.LogInfo("Arena bot movement support is ready (BigBrain).");
        }
        catch (Exception ex)
        {
            DisableRuntime();
            Logger.LogError($"Arena BigBrain movement ownership could not be registered: {ex}");
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        DisableRuntime();
        ModLogger = null;
    }

    private void DisableRuntime()
    {
        RuntimeEnabled = false;
        Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = false;
        Pickups.ArenaPickupRuntime.BotObjectivesAvailable = false;
        // Registration can fail after claiming the bridge. Revocation only
        // removes our exact consumer ID, never an unrelated owner's lease.
        Bots.TeamArenaPursuitLeaseBridge.UnregisterConsumer(PluginGuid);
        _resetPickups?.Invoke();
        _prunePickups = null;
        _resetPickups = null;
    }

    private static bool HasExactArenaDependency(out string problem)
    {
        problem = null;
        if (!Chainloader.PluginInfos.TryGetValue(ArenaMode.Plugin.PluginGuid, out BepInEx.PluginInfo info)
            || info.Instance == null)
        {
            problem = $"hard dependency '{ArenaMode.Plugin.PluginGuid}' is not loaded";
            return false;
        }

        Version actual = info.Metadata?.Version;
        if (actual != RequiredArenaVersion)
        {
            problem = $"'{ArenaMode.Plugin.PluginGuid}' is {actual?.ToString() ?? "unknown"}; exact {RequiredArenaVersion} is required";
            return false;
        }

        return true;
    }
}
