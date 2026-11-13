using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;

internal static partial class Program
{
    // Solo Tactical is admitted on every ported map since ArenaMapPort's rows of 2026-11-12.
    // pftTactical=false: Chop Shop's yard centre has too few distant rows for the exact five of a
    // protected Tactical squad. pftZombie=false: Equator's and Chop Shop's main meshes are too small
    // for the protected squad's Zombies.
    private static void CheckPortedMapAdmission(string map, string excludedAllowlist, bool pftTactical = true, bool pftZombie = true)
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
                bool admitted = present;
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
                    map + ": AVAILABILITY: optional plugin and all generic modes");
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
                // The map-availability read below is the solo one: every mode is admitted.
                bool admitted = true;
                ConfigureAreaNegotiation();
                Settings.GameMode.Value = mode;
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaMenuSelection.ConfigureEligibility(() => new(true, true, ""), _ => string.Empty);
                ArenaMenuSelection.RegisterTeamArenaQuery(() => true);
                Select(map);
                // A map with neither protected-squad mode (Chop Shop) is not offered to a squad at all.
                Assert(ArenaMenuSelection.HasConfiguredMap(true) == (pftTactical || pftZombie) && ArenaMenuSelection.IsMapAvailable(map) == admitted,
                    map + ": PFT: protected map discovery and final mode admission");
                int before = RequestHandler.StartCount;
                bool accepted = Start(Request(map: map), out var task);
                // The start prefix sees the protected squad (the map-availability read above does not):
                // Chop Shop refuses PFT Zombies and PFT Tactical there, Equator PFT Zombies.
                bool startAdmitted = admitted && (pftZombie || mode != "zombie") && (pftTactical || mode != "tactical");
                Assert(startAdmitted
                    ? accepted && task == null && RequestHandler.StartCount == before + 1 && ArenaSession.ArmedForNextRaid
                    : !accepted && task.IsFaulted && RequestHandler.StartCount == before && !ArenaSession.ArmedForNextRaid,
                    map + ": PFT-START: actual protected local-start negotiation");
            }
            // This loop's eligibility is re-initialised by Select, so it checks the solo path of
            // each mode; the protected-squad refusal is asserted on the resolver itself.
            if (!pftZombie)
                Assert(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "zombie", true, out _, out _)
                    && ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "zombie", false, out _, out _),
                    map + ": PFT-ZOMBIE: squad Zombies refused, solo Zombies admitted");
            if (!pftTactical)
                Assert(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "tactical", true, out _, out _)
                    && ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, "tactical", false, out _, out _),
                    map + ": PFT-TACTICAL: squad Tactical refused, solo Tactical admitted");
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
