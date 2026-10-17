using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;

internal static partial class Program
{
    private static void CheckAirPitAdmission()
    {
        const string map = ArenaMapCatalog.AirPitId;
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
                    "AIRPIT-DISCOVERY: optional plugin required for AirPit-only picker");
                Assert(ArenaMenuSelection.IsMapAvailable(map) == present,
                    "AIRPIT-AVAILABILITY: optional plugin and all generic modes");
                int before = RequestHandler.StartCount;
                bool accepted = Start(Request(map: map), out var task);
                Assert(accepted == present, "AIRPIT-START: actual local-start prefix respects optional presence");
                Assert(RequestHandler.StartCount == before + (present ? 1 : 0),
                    "AIRPIT-HTTP: unavailable map never requests a profile snapshot");
                Assert(present ? task == null && ArenaSession.ArmedForNextRaid
                    : task.IsFaulted && !ArenaSession.ArmedForNextRaid,
                    "AIRPIT-ARM: only acknowledged installed-map start publishes an arm");
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
                    "AIRPIT-PFT: protected map discovery and final mode admission");
                int before = RequestHandler.StartCount;
                Assert(Start(Request(map: map), out var task) && task == null
                    && RequestHandler.StartCount == before + 1 && ArenaSession.ArmedForNextRaid,
                    "AIRPIT-PFT-START: actual protected local-start negotiation");
            }
            ConfigureAreaNegotiation();
            Settings.GameMode.Value = "gungame";
            Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
            ArenaMenuSelection.ConfigureEligibility(() => new(true, false, ""), _ => string.Empty);
            Select(map);
            ArenaClientConfig.Enabled = _ => false;
            Assert(!ArenaMenuSelection.IsMapAvailable(map), "AIRPIT-SERVER-DISABLE: explicit server preference retained");
            ArenaClientConfig.Enabled = _ => true;
            Settings.Allowlist = ArenaMapCatalog.Bay5DefaultClientAllowlist;
            Assert(!ArenaMenuSelection.IsMapAvailable(map), "AIRPIT-CLIENT-DISABLE: custom allowlist retained");
            ConfigureAreaNegotiation();
            plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
            int ordinaryBefore = RequestHandler.StartCount;
            Assert(Start(Request(map: map), out var ordinaryTask) && ordinaryTask == null
                && RequestHandler.StartCount == ordinaryBefore,
                "AIRPIT-ORDINARY: unselected native raid never enters Arena admission");
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
