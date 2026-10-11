using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;

internal static partial class Program
{
    private static void CheckEquatorBoxHookAdmission()
    {
        var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
        plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
        try
        {
            foreach (bool ready in new[] { false, true })
            {
                ConfigureAreaNegotiation();
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaMode.Bots.ArenaEquatorTeamSpawns.IsReady = ready;
                Select(ArenaMapCatalog.EquatorId);
                int before = RequestHandler.StartCount;
                Assert(Start(Request(map: ArenaMapCatalog.EquatorId), out var task) == ready,
                    "EQUATOR-BOX-HOOK: only installed team-box hooks may negotiate an Arena raid");
                Assert(RequestHandler.StartCount == before + (ready ? 1 : 0),
                    "EQUATOR-BOX-SNAPSHOT: unavailable box hooks fail before a protected profile snapshot");
                Assert(ready ? task == null && ArenaSession.ArmedForNextRaid
                    : task.IsFaulted && !ArenaSession.ArmedForNextRaid,
                    "EQUATOR-BOX-ARM: failed box hook readiness never publishes an arm");
            }
            // Hook failure is scoped to independent Equator TDM; other maps,
            // supported solo modes and an ordinary PMC retain native admission.
            foreach (var pair in new[] { ("factory4_day", "teamdeathmatch"), (ArenaMapCatalog.EquatorId, "zombie") })
            {
                ConfigureAreaNegotiation();
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                Settings.GameMode.Value = pair.Item2;
                ArenaMode.Bots.ArenaEquatorTeamSpawns.IsReady = false;
                Select(pair.Item1);
                int before = RequestHandler.StartCount;
                Assert(Start(Request(map: pair.Item1), out var task) && task == null
                    && RequestHandler.StartCount == before + 1,
                    "EQUATOR-BOX-SCOPE: unavailable hooks leave unrelated maps and modes unchanged");
            }
            ConfigureAreaNegotiation();
            ArenaMode.Bots.ArenaEquatorTeamSpawns.IsReady = false;
            ArenaMenuSelection.EndFlow();
            int ordinaryBefore = RequestHandler.StartCount;
            Assert(Start(Request(map: ArenaMapCatalog.EquatorId), out var ordinaryTask)
                && ordinaryTask == null && RequestHandler.StartCount == ordinaryBefore,
                "EQUATOR-BOX-ORDINARY: no hook prerequisite for an ordinary native raid");
        }
        finally
        {
            ArenaMode.Bots.ArenaEquatorTeamSpawns.IsReady = true;
            plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
            ConfigureAreaNegotiation();
        }
    }

    private static void CheckEquatorPftAdmission()
    {
        var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
        plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
        try
        {
            foreach (bool pft in new[] { true, false })
            {
                ConfigureAreaNegotiation();
                Settings.GameMode.Value = "zombie";
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                Select(ArenaMapCatalog.EquatorId);
                ArenaMenuSelection.ConfigureEligibility(() => new(true, pft, ""), _ => string.Empty);
                ArenaMenuSelection.RegisterTeamArenaQuery(() => pft);
                int before = RequestHandler.StartCount;
                bool accepted = Start(Request(map: ArenaMapCatalog.EquatorId), out var task);
                Assert(accepted == !pft, "EQUATOR-LOCAL-START: real client prefix permits solo, rejects PFT Zombies");
                Assert(RequestHandler.StartCount == before + (pft ? 0 : 1),
                    "EQUATOR-HTTP-BOUNDARY: rejected squad never requests a server profile snapshot");
                Assert(pft ? task.IsFaulted && !ArenaSession.ArmedForNextRaid && ArenaAreaRuntime.ArmCalls == 0
                    : task == null && ArenaSession.ArmedForNextRaid,
                    "EQUATOR-ARM-BOUNDARY: only solo publishes an acknowledged arm");

                ConfigureAreaNegotiation();
                before = RequestHandler.StartCount;
                Assert(ArenaServerClient.PostRaidStart(ArenaMapCatalog.EquatorId, "zombie", pft) == !pft,
                    "EQUATOR-DIRECT-ANNOUNCEMENT: direct transport client has the same PFT exclusion");
                Assert(RequestHandler.StartCount == before + (pft ? 0 : 1),
                    "EQUATOR-DIRECT-HTTP: unsupported direct call sends no HTTP request");
            }
            ConfigureAreaNegotiation();
            Settings.GameMode.Value = "zombie";
            ArenaMenuSelection.EndFlow();
            int ordinaryBefore = RequestHandler.StartCount;
            Assert(Start(Request(map: ArenaMapCatalog.EquatorId), out var ordinaryTask)
                && ordinaryTask == null && RequestHandler.StartCount == ordinaryBefore,
                "EQUATOR-ORDINARY: unarmed native raid bypasses the Arena-only admission restriction");
        }
        finally
        {
            plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
            ConfigureAreaNegotiation();
        }
    }
}
