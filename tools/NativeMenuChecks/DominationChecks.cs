using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using SPT.Common.Http;

namespace ArenaMode.Bots
{
    internal static class DominationObjectiveBridge
    {
        internal static bool Available = true;
    }
}

internal static partial class Program
{
    private static void CheckDominationAdmission()
    {
        try
        {
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
            foreach (string map in new[] { ArenaMapCatalog.FactoryDayId, ArenaMapCatalog.FactoryNightId })
            {
                ConfigureAreaNegotiation(); Select(map);
                Settings.GameMode.Value = "domination";
                Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
                ArenaParticipantSettings.Override = 8;
                Assert(Start(Request(map: map), out var accepted) && accepted == null
                    && ArenaSession.ArmedMode == "domination" && ArenaSession.Participants == 8,
                    "Domination freezes its exact mode and equal participant count on " + map);
            }
            foreach (string map in new[] { ArenaMapCatalog.LaboratoryId, ArenaMapCatalog.LabyrinthId,
                ArenaMapCatalog.CustomsId, ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId,
                ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId })
            {
                ConfigureAreaNegotiation();
                announced = RequestHandler.StartCount;
                Assert(!ArenaServerClient.PostRaidStart(map, "domination", participants: 8)
                    && RequestHandler.StartCount == announced && !ArenaServerClient.HasPendingArm,
                    "unsupported objective map is refused before HTTP arming: " + map);
            }
            ConfigureAreaNegotiation();
            announced = RequestHandler.StartCount;
            Assert(!ArenaServerClient.PostRaidStart(ArenaMapCatalog.FactoryDayId, "domination", true,
                ArenaAreaCatalog.FullMapId, participants: 8) && RequestHandler.StartCount == announced,
                "PFT cannot acquire independent objective roster semantics through direct arming");
        }
        finally
        {
            ArenaMode.Bots.DominationObjectiveBridge.Available = true;
            ArenaParticipantSettings.Override = null;
            ConfigureAreaNegotiation();
        }
    }
}
