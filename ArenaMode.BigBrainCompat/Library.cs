using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System;

namespace ArenaMode.BigBrainCompat;

// Plain library, explicitly owned by Arena's Start/Update/OnDestroy lifecycle.
// Keep this type free of BigBrain types; only the NoInlining boundary may use them.
public static class Library
{
    public const int ApiVersion = 1;
    // Internal lease identity only, retained across migration; not a plugin GUID.
    public const string ConsumerId = "com.maschine.ArenaMode.BigBrainCompat";
    public const string BigBrainPluginGuid = "xyz.drakia.bigbrain";
    public const string TestedBigBrainVersion = "1.5.0";
    public const int PursuitLayerPriority = 74;
    public const int PickupLayerPriority = 35;

    private static readonly Version MinimumBigBrainVersion = new(1, 5, 0);
    private static readonly Version RequiredArenaVersion = new(0, 1, 0);

    internal static bool RuntimeEnabled { get; private set; }
    internal static ManualLogSource ModLogger { get; private set; }
    private static Action _prunePickups;
    private static Action _resetPickups;
    private static float _nextPickupPrune;
    private static bool _attempted;

    public static void Tick()
    {
        if (!RuntimeEnabled || _prunePickups == null || UnityEngine.Time.time < _nextPickupPrune) return;
        _nextPickupPrune = UnityEngine.Time.time + .5f;
        try { _prunePickups(); }
        catch (Exception ex)
        {
            DisableRuntime();
            ModLogger?.LogError($"Arena BigBrain maintenance failed; movement support disabled: {ex}");
        }
    }

    public static bool Initialize(ManualLogSource logger)
    {
        // BigBrain retains registered layer types for the process lifetime.
        // Repeated calls (including after shutdown/failure) must not add duplicates.
        if (_attempted) return RuntimeEnabled;
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        _attempted = true;
        ModLogger = logger;
        RuntimeEnabled = false;

        if (!Chainloader.PluginInfos.TryGetValue(BigBrainPluginGuid, out BepInEx.PluginInfo bigBrain))
        {
            // The library is always shipped. No warning is needed for an
            // intentionally absent optional mod, and no BigBrain type is touched.
            ModLogger.LogDebug("Arena BigBrain library is inactive: optional BigBrain is not installed.");
            return false;
        }

        Version actual = bigBrain.Metadata?.Version;
        if (bigBrain.Instance == null || actual == null || actual < MinimumBigBrainVersion)
        {
            ModLogger.LogWarning("Arena bot movement support is disabled: BigBrain "
                + $"{actual?.ToString() ?? "unknown"} is not loaded or is older than {MinimumBigBrainVersion}.");
            return false;
        }

        if (!HasExactArenaDependency(out string arenaProblem))
        {
            ModLogger.LogError("Arena BigBrain movement ownership stays disabled: " + arenaProblem);
            return false;
        }

        if (actual > MinimumBigBrainVersion)
            ModLogger.LogWarning($"Arena BigBrain support was tested with {TestedBigBrainVersion}; "
                + $"installed {actual} is newer. Trying the existing API with runtime safety checks.");

        try
        {
            // NoInlining on Register prevents either JIT from resolving its
            // BigBrain layer types while compiling this optional bootstrap.
            BigBrainRuntime.Register(ModLogger, actual, out _prunePickups, out _resetPickups);
            RuntimeEnabled = true;
            Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = true;
            Pickups.ArenaPickupRuntime.BotObjectivesAvailable = true;
            ModLogger.LogInfo("Arena bot movement support is ready (BigBrain).");
            return true;
        }
        catch (Exception ex)
        {
            DisableRuntime();
            ModLogger.LogError($"Arena BigBrain movement ownership could not be registered: {ex}");
            return false;
        }
    }

    public static void Shutdown()
    {
        DisableRuntime();
        ModLogger = null;
    }

    private static void DisableRuntime()
    {
        RuntimeEnabled = false;
        Combat.Fire.ArenaFireRuntime.BotEscapeAvailable = false;
        Pickups.ArenaPickupRuntime.BotObjectivesAvailable = false;
        // Registration can fail after claiming the bridge. Revocation only
        // removes our exact consumer ID, never an unrelated owner's lease.
        Bots.TeamArenaPursuitLeaseBridge.UnregisterConsumer(ConsumerId);
        Action reset = _resetPickups;
        _prunePickups = null;
        _resetPickups = null;
        try { reset?.Invoke(); }
        catch (Exception ex) { ModLogger?.LogError($"Arena BigBrain claim cleanup failed: {ex}"); }
    }

    private static bool HasExactArenaDependency(out string problem)
    {
        problem = null;
        if (!Chainloader.PluginInfos.TryGetValue(ArenaMode.Plugin.PluginGuid, out BepInEx.PluginInfo info)
            || info.Instance == null)
        {
            problem = $"owning plugin '{ArenaMode.Plugin.PluginGuid}' is not loaded";
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
