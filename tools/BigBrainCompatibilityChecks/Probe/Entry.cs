using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using ArenaMode.BigBrainCompat;
using Companion = ArenaMode.BigBrainCompat.Plugin;

public static class Entry
{
    private static Companion _plugin;
    public static void Start(string version, string scenario)
    {
        Chainloader.PluginInfos[ArenaMode.Plugin.PluginGuid] = new PluginInfo
        {
            Metadata = new BepInPlugin("arena", "arena", scenario == "arena-mismatch" ? "0.2.0" : "0.1.0"),
            Instance = new object()
        };
        if (version != "absent")
            Chainloader.PluginInfos[Companion.BigBrainPluginGuid] = new PluginInfo
            {
                Metadata = scenario == "unknown-version" ? null : new BepInPlugin("bb", "bb", version),
                Instance = scenario == "not-loaded" ? null : new object()
            };
        if (scenario == "foreign-owner") ArenaMode.Bots.TeamArenaPursuitLeaseBridge.Consumer = "foreign";
        _plugin = new Companion();
        Invoke("Awake");
    }
    public static void Invoke(string method) => typeof(Companion).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_plugin, null);
    public static string[] State() => new[]
    {
        Companion.RuntimeEnabled.ToString(), _plugin.enabled.ToString(),
        ArenaMode.Combat.Fire.ArenaFireRuntime.BotEscapeAvailable.ToString(),
        ArenaMode.Pickups.ArenaPickupRuntime.BotObjectivesAvailable.ToString(),
        ArenaMode.Bots.TeamArenaPursuitLeaseBridge.Consumer ?? "none",
        ArenaPickupObjective.Prunes.ToString(), ArenaPickupObjective.Resets.ToString(),
        string.Join("\n", _plugin.Logger.Lines)
    };
    public static bool SoftDependency() => typeof(Companion).GetCustomAttributes<BepInDependency>()
        .Single(x => x.Guid == Companion.BigBrainPluginGuid).Flags == BepInDependency.DependencyFlags.SoftDependency;
}
