using BepInEx;
using BepInEx.Logging;

namespace ArenaMode.BigBrainCompat;

#if LEGACY_PLUGIN
[BepInPlugin("com.maschine.ArenaMode.BigBrainCompat", "Old companion", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin { }
#else
// Disk-loaded test double for the real client's reflection/delegate boundary.
// Production library behavior is tested separately by the isolated Probe.
public static class Library
{
#if BAD_API
    public const int ApiVersion = 2;
#else
    public const int ApiVersion = 1;
#endif
    private static ManualLogSource _logger;
    private static string Scenario => Environment.GetEnvironmentVariable("ARENA_BIGBRAIN_HOST_FIXTURE");
    public static bool Initialize(ManualLogSource logger)
    {
        _logger = logger;
        logger.LogDebug("fixture initialize");
        if (Scenario == "init-throw") throw new InvalidOperationException("fixture init failure");
        return Scenario != "init-false";
    }
    public static void Tick()
    {
        _logger.LogDebug("fixture tick");
        if (Scenario == "tick-throw") throw new InvalidOperationException("fixture tick failure");
    }
    public static void Shutdown()
    {
        _logger.LogDebug("fixture shutdown");
        if (Scenario == "shutdown-throw") throw new InvalidOperationException("fixture shutdown failure");
    }
}
#endif
