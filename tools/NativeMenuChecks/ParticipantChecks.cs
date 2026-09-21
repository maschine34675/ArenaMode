using System;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

internal static partial class Program
{
    private static JObject ParticipantAck(string body)
    {
        var sent = JObject.Parse(body);
        return new JObject { ["armed"] = true, ["armToken"] = sent["requestId"],
            ["areaId"] = sent["areaId"], ["areaTeamSize"] = sent["areaTeamSize"],
            ["deathmatchHostility"] = sent["deathmatchHostility"],
            ["participants"] = sent["participants"], ["participantBosses"] = false };
    }

    private static void CheckParticipantNegotiation()
    {
        BepInEx.Bootstrap.Chainloader.PluginInfos[ArenaMapCatalog.ArenaMapPortPluginId] = new object();
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch" })
        {
            string map = mode == "teamdeathmatch" ? ArenaMapCatalog.FactoryDayId : ArenaMapCatalog.EquatorId;
            ConfigureAreaNegotiation(); Select(map);
            Settings.GameMode.Value = mode; Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
            ArenaParticipantSettings.Override = 8;
            RequestHandler.Post = (route, body) => {
                if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true,\"state\":\"Idle\"}";
                ArenaParticipantSettings.Override = 24;
                return ParticipantAck(body).ToString();
            };
            Assert(Start(Request(map: map), out var task) && task == null,
                "map accepts explicit participant total in " + mode + ": " + ArenaMode.Plugin.ModLogger.LastError);
            Assert(ArenaSession.Participants == 8 && !ArenaSession.ParticipantBosses
                && JObject.Parse(RequestHandler.LastPostBody)["participants"].Value<int>() == 8,
                "loading edit cannot change frozen client/server participant agreement");
        }
        foreach (JToken bad in new JToken[] { null, 4, "8", 8.0, true })
        {
            ConfigureAreaNegotiation(); Select(); Settings.GameMode.Value = "gungame";
            ArenaParticipantSettings.Override = 8;
            RequestHandler.Post = (route, body) => {
                if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true,\"state\":\"Idle\"}";
                var result = ParticipantAck(body); result["participants"] = bad; return result.ToString();
            };
            Assert(!Start(Request(), out var task) && task.IsFaulted && !ArenaSession.ArmedForNextRaid,
                "missing, mismatched or coerced total blocks native start and cancels exact snapshot");
        }
        foreach (JToken bad in new JToken[] { null, "false", 0, true })
        {
            ConfigureAreaNegotiation(); Select(); Settings.GameMode.Value = "gungame";
            RequestHandler.Post = (route, body) => {
                if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true,\"state\":\"Idle\"}";
                var result = ParticipantAck(body); result["participantBosses"] = bad; return result.ToString();
            };
            Assert(!Start(Request(), out var task) && task.IsFaulted && !ArenaSession.ArmedForNextRaid,
                "boss slot requires exact bool and is never accepted on Factory");
        }
        ConfigureAreaNegotiation(); Select(ArenaMapCatalog.LabyrinthId); Settings.GameMode.Value = "gungame";
        RequestHandler.Post = (route, body) => {
            if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true,\"state\":\"Idle\"}";
            var result = ParticipantAck(body); result["participantBosses"] = true; return result.ToString();
        };
        Assert(Start(Request(map: ArenaMapCatalog.LabyrinthId), out var bossTask) && bossTask == null
            && ArenaSession.ParticipantBosses && ArenaSession.Participants == 8,
            "Labyrinth server boss slot is carried into exact arm alongside total");
        ArenaSession.ClearArmed("participant fixture ended");
        Assert(!ArenaServerClient.PostRaidStart("factory4_day", "tactical", participants: 8)
            && !ArenaServerClient.PendingParticipantBosses,
            "invalid subsequent request cannot reuse prior boss acknowledgement");
        ArenaParticipantSettings.Override = null;
        ConfigureAreaNegotiation(); Select(); Settings.GameMode.Value = "gungame";
        ArenaMode.Bots.ArenaBotSpawnPatches.SoloPopulationReady = false;
        int before = RequestHandler.StartCount;
        Assert(!Start(Request(), out var unavailable) && unavailable.IsFaulted && RequestHandler.StartCount == before,
            "missing native spawn protection refuses before server snapshot announcement");
        ArenaMode.Bots.ArenaBotSpawnPatches.SoloPopulationReady = true;
        BepInEx.Bootstrap.Chainloader.PluginInfos.Remove(ArenaMapCatalog.ArenaMapPortPluginId);
        ConfigureAreaNegotiation();
    }
}
