using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;

internal static partial class Program
{
    private static void CheckPortedMapAdmission(string map, string excludedAllowlist)
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
                ConfigureAreaNegotiation();
                if (present) plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
                else plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
                Settings.GameMode.Value = mode;
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaMenuSelection.ConfigureEligibility(() => new(true, false, ""), _ => string.Empty);
                Select(map);
                Assert(ArenaMenuSelection.HasConfiguredMap(false) == present,
                    map + ": DISCOVERY: optional plugin required for a single imported map picker");
                Assert(ArenaMenuSelection.IsMapAvailable(map) == present,
                    map + ": AVAILABILITY: optional plugin and all generic modes");
                int before = RequestHandler.StartCount;
                bool accepted = Start(Request(map: map), out var task);
                Assert(accepted == present, map + ": START: actual local-start prefix respects optional presence");
                Assert(RequestHandler.StartCount == before + (present ? 1 : 0),
                    map + ": HTTP: unavailable map never requests a profile snapshot");
                Assert(present ? task == null && ArenaSession.ArmedForNextRaid
                    : task.IsFaulted && !ArenaSession.ArmedForNextRaid,
                    map + ": ARM: only acknowledged installed-map start publishes an arm");
            }
            plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
            foreach (string mode in new[] { "tactical", "zombie" })
            {
                ConfigureAreaNegotiation();
                Settings.GameMode.Value = mode;
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaMenuSelection.ConfigureEligibility(() => new(true, true, ""), _ => string.Empty);
                ArenaMenuSelection.RegisterTeamArenaQuery(() => true);
                Select(map);
                Assert(ArenaMenuSelection.HasConfiguredMap(true) && ArenaMenuSelection.IsMapAvailable(map),
                    map + ": PFT: protected map discovery and final mode admission");
                int before = RequestHandler.StartCount;
                Assert(Start(Request(map: map), out var task) && task == null
                    && RequestHandler.StartCount == before + 1 && ArenaSession.ArmedForNextRaid,
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
