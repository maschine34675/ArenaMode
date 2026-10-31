using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;

internal static partial class Program
{
    // tactical=false: the map's shared resolver refuses both Tactical formats
    // (Iceberg, like Equator, lacks five distant bot markers from its centre).
    private static void CheckPortedMapAdmission(string map, string excludedAllowlist, bool tactical = true)
    {
        var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
        var previousAllowlist = Settings.Allowlist;
        var previousEnabled = ArenaClientConfig.Enabled;
        try
        {
            Settings.Allowlist = map;
            ArenaClientConfig.Enabled = Settings.IsArenaMap;
            foreach (bool present in new[] { false, true })
            foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "tactical", "zombie" })
            {
                bool admitted = present && (tactical || mode != "tactical");
                ConfigureAreaNegotiation();
                if (present) plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
                else plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
                Settings.GameMode.Value = mode;
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaMenuSelection.ConfigureEligibility(() => new(true, false, ""), _ => string.Empty);
                Select(map);
                Assert(ArenaMenuSelection.HasConfiguredMap(false) == present,
                    map + ": DISCOVERY: optional plugin required for a single imported map picker");
                Assert(ArenaMenuSelection.IsMapAvailable(map) == admitted,
                    map + ": AVAILABILITY: optional plugin and all generic modes" + (tactical ? "" : " except Tactical"));
                int before = RequestHandler.StartCount;
                bool accepted = Start(Request(map: map), out var task);
                Assert(accepted == admitted, map + ": START: actual local-start prefix respects optional presence and mode admission");
                Assert(RequestHandler.StartCount == before + (admitted ? 1 : 0),
                    map + ": HTTP: unavailable map or refused mode never requests a profile snapshot");
                Assert(admitted ? task == null && ArenaSession.ArmedForNextRaid
                    : task.IsFaulted && !ArenaSession.ArmedForNextRaid,
                    map + ": ARM: only acknowledged installed-map start publishes an arm");
            }
            plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
            foreach (string mode in new[] { "tactical", "zombie" })
            {
                bool admitted = tactical || mode != "tactical";
                ConfigureAreaNegotiation();
                Settings.GameMode.Value = mode;
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaMenuSelection.ConfigureEligibility(() => new(true, true, ""), _ => string.Empty);
                ArenaMenuSelection.RegisterTeamArenaQuery(() => true);
                Select(map);
                Assert(ArenaMenuSelection.HasConfiguredMap(true) && ArenaMenuSelection.IsMapAvailable(map) == admitted,
                    map + ": PFT: protected map discovery and final mode admission");
                int before = RequestHandler.StartCount;
                bool accepted = Start(Request(map: map), out var task);
                Assert(admitted
                    ? accepted && task == null && RequestHandler.StartCount == before + 1 && ArenaSession.ArmedForNextRaid
                    : !accepted && task.IsFaulted && RequestHandler.StartCount == before && !ArenaSession.ArmedForNextRaid,
                    map + ": PFT-START: actual protected local-start negotiation");
            }
            ConfigureAreaNegotiation();
            Settings.GameMode.Value = "gungame";
            Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
            ArenaMenuSelection.ConfigureEligibility(() => new(true, false, ""), _ => string.Empty);
            Select(map);
            ArenaClientConfig.Enabled = _ => false;
            Assert(!ArenaMenuSelection.IsMapAvailable(map), map + ": SERVER-DISABLE: explicit server preference retained");
            ArenaClientConfig.Enabled = _ => true;
            Settings.Allowlist = excludedAllowlist;
            Assert(!ArenaMenuSelection.IsMapAvailable(map), map + ": CLIENT-DISABLE: custom allowlist retained");
            ConfigureAreaNegotiation();
            plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
            int ordinaryBefore = RequestHandler.StartCount;
            Assert(Start(Request(map: map), out var ordinaryTask) && ordinaryTask == null
                && RequestHandler.StartCount == ordinaryBefore,
                map + ": ORDINARY: unselected native raid never enters Arena admission");
        }
        finally
        {
            Settings.Allowlist = previousAllowlist;
            ArenaClientConfig.Enabled = previousEnabled;
            plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
            ConfigureAreaNegotiation();
            ArenaMenuSelection.ConfigureEligibility(() => new(true, false, ""), _ => string.Empty);
        }
    }
}
