using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;
using System.Linq;

internal static partial class Program
{
    private static void CheckDominationAdmission()
    {
        var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
        bool hadMapPort = plugins.TryGetValue(ArenaMapCatalog.ArenaMapPortPluginId, out var originalMapPort);
        var originalAllowlist = Settings.Allowlist;
        var originalEnabled = ArenaClientConfig.Enabled;
        string[] ports = { ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id,
            ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId };
        try
        {
            Settings.Allowlist = ArenaMapCatalog.DefaultClientAllowlist;
            ArenaClientConfig.Enabled = Settings.IsArenaMap;
            plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
            ConfigureAreaNegotiation(); Select();
            Settings.GameMode.Value = "domination";
            Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
            ArenaMode.Bots.DominationObjectiveBridge.Available = false;
            int announced = RequestHandler.StartCount;
            Assert(ArenaMenuSelection.CurrentEligibility.Allowed,
                "missing Domination companion still permits entering Arena to choose another mode");
            Assert(!ArenaMenuSelection.IsMapDiscoverable(ArenaMapCatalog.FactoryDayId)
                && !ArenaMenuSelection.TryValidateAndFreezeRequest(Request(), out string reason)
                && reason.Contains("objective companion"),
                "missing Domination companion blocks map readiness and final freeze with a reason");
            Assert(!Start(Request(), out var stopped) && stopped.IsFaulted
                && RequestHandler.StartCount == announced && !ArenaSession.ArmedForNextRaid,
                "missing Domination companion rejects native start before any server snapshot arm");

            ArenaMode.Bots.DominationObjectiveBridge.Available = true;
            foreach (string map in new[] { ArenaMapCatalog.FactoryDayId, ArenaMapCatalog.FactoryNightId,
                ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id, ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId })
            {
                ConfigureAreaNegotiation(); Select(map);
                Settings.GameMode.Value = "domination";
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaParticipantSettings.Override = 8;
                announced = RequestHandler.StartCount;
                Assert(Start(Request(map: map), out var accepted) && accepted == null
                    && ArenaSession.ArmedMode == "domination" && ArenaSession.Participants == 8
                    && RequestHandler.StartCount == announced + 1,
                    "Domination freezes its exact mode and equal participant count on " + map);
            }
            foreach (string map in ports)
            {
                ConfigureAreaNegotiation(); Select(map);
                Settings.GameMode.Value = "domination";
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaParticipantSettings.Override = 8;
                plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
                announced = RequestHandler.StartCount;
                Assert(!Start(Request(map: map), out var missingPort) && missingPort.IsFaulted
                    && RequestHandler.StartCount == announced && !ArenaSession.ArmedForNextRaid,
                    "Domination imported map without loaded MapPort rejects before HTTP arm: " + map);
                plugins[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
                ConfigureAreaNegotiation(); Select(map);
                Settings.GameMode.Value = "domination";
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaClientConfig.Enabled = _ => false;
                announced = RequestHandler.StartCount;
                Assert(!Start(Request(map: map), out var disabledMap) && disabledMap.IsFaulted
                    && RequestHandler.StartCount == announced && !ArenaSession.ArmedForNextRaid,
                    "Domination imported map disabled on server rejects before HTTP arm: " + map);
                ArenaClientConfig.Enabled = Settings.IsArenaMap;
            }
            foreach (string map in new[] { ArenaMapCatalog.LaboratoryId, ArenaMapCatalog.LabyrinthId,
                ArenaMapCatalog.CustomsId, "unknown-future-map" })
            {
                ConfigureAreaNegotiation(); Select(map);
                Settings.GameMode.Value = "domination";
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                announced = RequestHandler.StartCount;
                Assert(!Start(Request(map: map), out var unsupported) && unsupported.IsFaulted
                    && RequestHandler.StartCount == announced && !ArenaSession.ArmedForNextRaid,
                    "unsupported objective map rejects native start before HTTP arming: " + map);
                ConfigureAreaNegotiation();
                announced = RequestHandler.StartCount;
                Assert(!ArenaServerClient.PostRaidStart(map, "domination", participants: 8)
                    && RequestHandler.StartCount == announced && !ArenaServerClient.HasPendingArm,
                    "unsupported objective map is refused before HTTP arming: " + map);
            }
            foreach (string map in new[] { ArenaMapCatalog.FactoryDayId, ArenaMapCatalog.FactoryNightId }.Concat(ports))
            {
                ConfigureAreaNegotiation();
                announced = RequestHandler.StartCount;
                Assert(!ArenaServerClient.PostRaidStart(map, "domination", true,
                    ArenaAreaCatalog.FullMapId, participants: 8) && RequestHandler.StartCount == announced,
                    "PFT cannot acquire independent objective roster semantics through direct arming: " + map);
            }
        }
        finally
        {
            ArenaMode.Bots.DominationObjectiveBridge.Available = true;
            ArenaParticipantSettings.Override = null;
            Settings.Allowlist = originalAllowlist;
            ArenaClientConfig.Enabled = originalEnabled;
            if (hadMapPort) plugins[ArenaMapCatalog.ArenaMapPortPluginId] = originalMapPort;
            else plugins.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
            ConfigureAreaNegotiation();
        }
    }
}
