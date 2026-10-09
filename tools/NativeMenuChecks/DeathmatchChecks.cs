using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
internal static partial class Program
{
    private static void CheckDeathmatchNegotiation()
    {
        foreach (bool ffa in new[] { false, true })
        {
            ConfigureAreaNegotiation(); Select();
            Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
            Settings.GameMode.Value = "deathmatch"; Settings.FreeForAll = ffa;
            RequestHandler.Post = (route, body) => {
                if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true,\"state\":\"Idle\"}";
                var sent = JObject.Parse(body);
                Settings.FreeForAll = !ffa;
                return new JObject { ["armed"] = true, ["armToken"] = sent["requestId"],
                    ["participants"] = sent["participants"], ["participantBosses"] = false,
                    ["deathmatchHostility"] = sent["deathmatchHostility"] }.ToString();
            };
            Assert(Start(Request(), out var task) && task == null, "acknowledged Deathmatch arm succeeds");
            Assert(ArenaSession.FrozenDeathmatch.FreeForAll == ffa && Settings.FreeForAll != ffa,
                "HTTP boundary freezes same rules even if settings change during loading");
        }
        foreach (JToken echo in new JToken[] { null, "all_vs_player", 1, "FFA" })
        {
            ConfigureAreaNegotiation(); Select(); Settings.GameMode.Value = "deathmatch"; Settings.FreeForAll = true;
            Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
            RequestHandler.Post = (route, body) => route == Protocol.RaidStartRoute
                ? new JObject { ["armed"] = true, ["armToken"] = JObject.Parse(body)["requestId"],
                    ["participants"] = JObject.Parse(body)["participants"], ["participantBosses"] = false,
                    ["deathmatchHostility"] = echo }.ToString()
                : "{\"disarmed\":true,\"state\":\"Idle\"}";
            Assert(!Start(Request(), out var task) && task.IsFaulted && !ArenaSession.ArmedForNextRaid,
                "missing/mismatched hostility echo cancels protected start rather than silently changing rules");
        }
        ConfigureAreaNegotiation();
    }
}
