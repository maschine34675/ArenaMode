using System;
using System.Linq;
using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

internal static partial class Program
{
    private static void ConfigureAreaTeams(int size = 2, string area = ArenaAreaCatalog.CustomsFactoryCenterTeamsId)
    {
        ConfigureAreaNegotiation();
        Settings.AreaTeamSize.Value = size;
        Settings.TeamDeathmatchArea.Value = area;
        Settings.RefuseAreaWrite = false;
        SettingsSync.OnSync = null;
        ArenaClientConfig.Enabled = _ => true;
        Settings.Allowlist = ArenaMapCatalog.DefaultClientAllowlist;
        ArenaAreaInitialSpawnPatch.IsReady = true;
        RequestHandler.Post = (route, body) => route == Protocol.RaidStartRoute
            ? new JObject { ["armed"] = true, ["armToken"] = JObject.Parse(body)["requestId"],
                ["participants"] = JObject.Parse(body)["participants"], ["participantBosses"] = false,
                ["areaId"] = JObject.Parse(body)["areaId"], ["areaTeamSize"] = JObject.Parse(body)["areaTeamSize"] }.ToString()
            : "{\"disarmed\":true,\"state\":\"Idle\"}";
    }

    private static void CheckAreaTeamMenu()
    {
        const string teams = ArenaAreaCatalog.CustomsFactoryCenterTeamsId, map = ArenaMapCatalog.CustomsId;
        foreach (string selectedArea in new[] { teams, ArenaAreaCatalog.CustomsFactoryEastPolygonId, ArenaAreaCatalog.CustomsYardId })
        foreach (int size in new[] { 2, 3, 4, 5 })
        {
            ConfigureAreaTeams(size, selectedArea);
            var owner = Select(map);
            var state = ArenaMenuSelection.AreaSelection(map);
            Assert(state.Options.Count == 3 && state.HasTeamSizeChoice && state.SelectedTeamSize == size
                && state.TeamSizeOptions.SequenceEqual(new[] { 2,3,4,5 }), "new area exposes exact immutable 2..5 size choices");
            Settings.AreaTeamSize.Value = size == 5 ? 2 : 5;
            Assert(state.SelectedTeamSize == size, "existing proposal cannot change when F12 changes");
            Assert(ArenaMenuSelection.TrySelectAreaTeamSize(owner,map,selectedArea,size), "current area size click accepted");
            var request = Request(map:map);
            Assert(Start(request,out var task) && task == null && ArenaAreaRuntime.TeamSize == size,
                "real negotiation arms exact menu size");
            var sent = JObject.Parse(RequestHandler.LastPostBody);
            Assert(sent["areaTeamSize"]?.Type == JTokenType.Integer && sent["areaTeamSize"].Value<int>() == size,
                "exact selected count is sent as integer");
            Assert(sent["areaId"]?.Value<string>() == selectedArea && ArenaAreaRuntime.ArmedArea?.Id == selectedArea,
                "both team-start presets retain their own exact protocol identity through negotiation");
            Settings.AreaTeamSize.Value = size == 5 ? 2 : 5;
            Assert(ArenaAreaRuntime.TeamSize == size, "post-arm F12 mutation does not rewrite runtime selection");
        }

        foreach (JToken reply in new JToken[] { null, JValue.CreateNull(), new JValue(0), new JValue(3),new JValue("4"),new JValue(4.0),new JValue(true) })
        {
            ConfigureAreaTeams(4);
            RequestHandler.Post = (route, body) =>
            {
                if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true,\"state\":\"Idle\"}";
                var result = new JObject { ["armed"] = true,["armToken"] = JObject.Parse(body)["requestId"],["areaId"] = teams };
                if (reply != null) result["areaTeamSize"] = reply;
                return result.ToString();
            };
            Assert(!ArenaServerClient.PostRaidStart(map,"teamdeathmatch",false,teams,4)
                && ArenaServerClient.HasPendingArm, "missing, wrong or coercible size acknowledgement retains exact cancellation token");
            Assert(ArenaServerClient.PostRaidCancel(startFailed:true), "size mismatch cancellation remains available");
        }
        foreach (int invalid in new[] { 0,1,6,12 })
        {
            ConfigureAreaTeams(invalid);
            var owner=Select(map); int before=RequestHandler.StartCount;
            Assert(!ArenaMenuSelection.TrySelectAreaTeamSize(owner,map,teams,invalid), "unsupported click rejected");
            Assert(!Start(Request(map:map),out var task) && task.IsFaulted && RequestHandler.StartCount==before
                && ArenaAreaRuntime.ArmCalls==0 && Settings.AreaTeamSize.Value==invalid, "invalid final choice rejected without repair or arm");
        }
        ConfigureAreaTeams();
        var current=Select(map);
        Assert(!ArenaMenuSelection.TrySelectAreaTeamSize(new RaidSettings{Side=ESideType.Pmc},map,teams,4), "wrong owner rejected");
        Assert(!ArenaMenuSelection.TrySelectAreaTeamSize(current,ArenaMapCatalog.FactoryDayId,teams,4), "stale map rejected");
        Settings.TeamDeathmatchArea.Value=ArenaAreaCatalog.CustomsFactoryCenterPolygonId;
        Assert(!ArenaMenuSelection.TrySelectAreaTeamSize(current,map,teams,4), "stale team-area row rejected");
        Assert(ArenaMenuSelection.AreaSelection(map).SelectedId == teams && Settings.TeamDeathmatchArea.Value == ArenaAreaCatalog.CustomsFactoryCenterPolygonId, "retired area is only a menu proposal until explicitly saved");
        Settings.TeamDeathmatchArea.Value=teams;
        Settings.RefuseAreaWrite=true;
        Assert(!ArenaMenuSelection.TrySelectAreaTeamSize(current,map,teams,4) && Settings.AreaTeamSize.Value==2, "failed setting write cannot paint accepted size");
        Settings.RefuseAreaWrite=false;
        ArenaMenuSelection.EndFlow();
        Assert(!ArenaMenuSelection.TrySelectAreaTeamSize(current,map,teams,4), "closed flow rejects retired click");

        ConfigureAreaTeams(2); Select(map);
        SettingsSync.OnSync=()=>Settings.AreaTeamSize.Value=5;
        Assert(Start(Request(map:map),out var synced) && synced==null && ArenaAreaRuntime.TeamSize==5,
            "size freezes after settings synchronization, not before");
        SettingsSync.OnSync=null;
        ConfigureAreaTeams(3); Select(map); ArenaAreaInitialSpawnPatch.IsReady=false;
        int starts=RequestHandler.StartCount;
        Assert(!Start(Request(map:map),out var missing) && missing.IsFaulted && RequestHandler.StartCount==starts,
            "new area requires initial placement hook before server arm");
        ConfigureAreaNegotiation(); Settings.TeamDeathmatchArea.Value=ArenaAreaCatalog.FullMapId;
        Select(ArenaMapCatalog.FactoryDayId); ArenaAreaInitialSpawnPatch.IsReady=false;
        Assert(Start(Request(map:ArenaMapCatalog.FactoryDayId),out var legacy) && legacy==null && ArenaAreaRuntime.TeamSize==0,
            "missing new placement hook leaves legacy area negotiation unchanged");
        Assert(JObject.Parse(RequestHandler.LastPostBody)["areaTeamSize"]==null, "legacy request omits override field");
        ArenaAreaInitialSpawnPatch.IsReady=true;
        ConfigureAreaNegotiation();
    }
}
