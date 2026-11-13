using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
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
            ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId, ArenaMapCatalog.IcebergId,
            ArenaMapCatalog.AutoServiceId };
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
                && reason == "Domination needs BigBrain 1.5.0 or newer (separate download).",
                "missing Domination companion blocks map readiness and final freeze with a reason naming BigBrain");
            plugins["xyz.drakia.bigbrain"] = new object();
            Assert(ArenaMenuSelection.ModeAvailabilityRejection("domination") is string loaded
                && loaded.StartsWith("Domination needs BigBrain 1.5.0 or newer") && loaded.Contains("see the BepInEx log"),
                "BigBrain present but companion inactive points at the log");
            plugins.Remove("xyz.drakia.bigbrain");
            Assert(!Start(Request(), out var stopped) && stopped.IsFaulted
                && RequestHandler.StartCount == announced && !ArenaSession.ArmedForNextRaid,
                "missing Domination companion rejects native start before any server snapshot arm");

            ArenaMode.Bots.DominationObjectiveBridge.Available = true;
            foreach (string map in new[] { ArenaMapCatalog.FactoryDayId, ArenaMapCatalog.FactoryNightId,
                ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id, ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId,
                ArenaMapCatalog.IcebergId, ArenaMapCatalog.AutoServiceId,
                ArenaMapCatalog.LaboratoryId, ArenaMapCatalog.LabyrinthId })
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
            foreach (string map in new[] { ArenaMapCatalog.CustomsId, "unknown-future-map" })
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
            // Customs is a Domination map through its three surveyed areas: the exact area and the chosen team size
            // (2..5, the Team Deathmatch setting) are negotiated, armed and sent, and no participant count is.
            foreach (string area in new[] { ArenaAreaCatalog.CustomsFactoryCenterTeamsId,
                ArenaAreaCatalog.CustomsFactoryEastPolygonId, ArenaAreaCatalog.CustomsYardId })
            foreach (int size in new[] { 2, 3, 5 })
            {
                ConfigureAreaTeams(size, area);
                Select(ArenaMapCatalog.CustomsId);
                Settings.GameMode.Value = "domination";
                ArenaParticipantSettings.Override = 8;
                announced = RequestHandler.StartCount;
                Assert(Start(Request(map: ArenaMapCatalog.CustomsId), out var areaStart) && areaStart == null
                    && RequestHandler.StartCount == announced + 1 && ArenaSession.ArmedMode == "domination"
                    && ArenaAreaRuntime.ArmedArea?.Id == area && ArenaAreaRuntime.TeamSize == size,
                    $"Domination in a Customs area arms the exact area and its team size: {area} {size}");
                var sentArea = JObject.Parse(RequestHandler.LastPostBody);
                Assert(sentArea["mode"]?.Value<string>() == "domination" && sentArea["areaId"]?.Value<string>() == area
                    && sentArea["areaTeamSize"]?.Type == JTokenType.Integer && sentArea["areaTeamSize"].Value<int>() == size
                    && (sentArea["participants"] == null || sentArea["participants"].Type == JTokenType.Null),
                    $"the Domination arm names the area and the team size and no participant count: {area} {size}");
            }
            // The size is explicit, as in Team Deathmatch: an out-of-range F12 value is refused before any arm.
            foreach (int invalid in new[] { 1, 6 })
            {
                ConfigureAreaTeams(invalid, ArenaAreaCatalog.CustomsYardId);
                Select(ArenaMapCatalog.CustomsId);
                Settings.GameMode.Value = "domination";
                announced = RequestHandler.StartCount;
                Assert(!Start(Request(map: ArenaMapCatalog.CustomsId), out var badSize) && badSize.IsFaulted
                    && RequestHandler.StartCount == announced && ArenaAreaRuntime.ArmCalls == 0,
                    "an unsupported Customs area team size refuses a Domination start before any arm: " + invalid);
            }
            // A squad never plays Domination, in an area either; and whole Customs stays refused with the area setting reset.
            ConfigureAreaTeams(3, ArenaAreaCatalog.CustomsYardId);
            announced = RequestHandler.StartCount;
            Assert(!ArenaServerClient.PostRaidStart(ArenaMapCatalog.CustomsId, "domination", true,
                ArenaAreaCatalog.CustomsYardId, 3) && RequestHandler.StartCount == announced,
                "PFT cannot acquire Domination in a Customs area through direct arming");
            foreach (string wholeId in new[] { null, ArenaAreaCatalog.FullMapId })
            {
                ConfigureAreaTeams(3, ArenaAreaCatalog.CustomsYardId);
                announced = RequestHandler.StartCount;
                Assert(!ArenaServerClient.PostRaidStart(ArenaMapCatalog.CustomsId, "domination", false, wholeId, 3)
                    && RequestHandler.StartCount == announced, "whole Customs stays refused for Domination before any arm");
            }
            foreach (string map in new[] { ArenaMapCatalog.FactoryDayId, ArenaMapCatalog.FactoryNightId,
                ArenaMapCatalog.LaboratoryId, ArenaMapCatalog.LabyrinthId }.Concat(ports))
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
            plugins.Remove("xyz.drakia.bigbrain");
            ConfigureAreaNegotiation();
        }
    }
}
