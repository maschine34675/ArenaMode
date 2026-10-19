using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using ArenaMode.BigBrainCompat;
using Companion = ArenaMode.BigBrainCompat.Library;

public static class Entry
{
    private static BepInEx.Logging.ManualLogSource _logger;
    public static void Start(string version, string scenario)
    {
        Chainloader.PluginInfos[ArenaMode.Plugin.PluginGuid] = new PluginInfo
        {
            Metadata = new BepInPlugin("arena", "arena", scenario == "arena-mismatch" ? "0.2.0" : ArenaMode.Plugin.PluginVersion),
            Instance = new object()
        };
        if (version != "absent")
            Chainloader.PluginInfos[Companion.BigBrainPluginGuid] = new PluginInfo
            {
                Metadata = scenario == "unknown-version" ? null : new BepInPlugin("bb", "bb", version),
                Instance = scenario == "not-loaded" ? null : new object()
            };
        if (scenario == "foreign-owner") ArenaMode.Bots.TeamArenaPursuitLeaseBridge.Consumer = "foreign";
        _logger = new();
        Companion.Initialize(_logger);
    }
    public static void Invoke(string method) => typeof(Companion).GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
    public static bool InitializeAgain() => Companion.Initialize(_logger);
    public static void StartHost(string version, string path, bool legacy)
    {
        _logger = new();
        if (version != "absent") Chainloader.PluginInfos[Companion.BigBrainPluginGuid] = new()
        { Metadata = new BepInPlugin("bb", "bb", version), Instance = new object() };
        if (legacy) Chainloader.PluginInfos[Companion.ConsumerId] = new() { Instance = new object() };
        ArenaMode.Compat.ArenaBigBrainLibrary.Initialize(_logger, path);
        ArenaMode.Compat.ArenaBigBrainLibrary.Initialize(_logger, path);
        ArenaMode.Compat.ArenaBigBrainLibrary.Tick();
        ArenaMode.Compat.ArenaBigBrainLibrary.Shutdown();
        ArenaMode.Compat.ArenaBigBrainLibrary.Shutdown();
        ArenaMode.Compat.ArenaBigBrainLibrary.Tick();
    }
    public static string[] State() => new[]
    {
        Companion.RuntimeEnabled.ToString(), Companion.RuntimeEnabled.ToString(),
        ArenaMode.Combat.Fire.ArenaFireRuntime.BotEscapeAvailable.ToString(),
        ArenaMode.Pickups.ArenaPickupRuntime.BotObjectivesAvailable.ToString(),
        ArenaMode.Bots.TeamArenaPursuitLeaseBridge.Consumer ?? "none",
        ArenaPickupObjective.Prunes.ToString(), ArenaPickupObjective.Resets.ToString(),
        string.Join("\n", _logger.Lines)
    };
    public static bool IsPlainLibrary() => !typeof(Companion).GetCustomAttributes<BepInPlugin>().Any()
        && !typeof(BaseUnityPlugin).IsAssignableFrom(typeof(Companion))
        && typeof(Companion).IsAbstract && typeof(Companion).IsSealed;
}
