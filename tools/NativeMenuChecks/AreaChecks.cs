using System;
using System.Reflection;
using ArenaMode.Areas;
using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

internal static partial class Program
{
    private static void ConfigureAreaNegotiation()
    {
        // New process/flow fixture. These are transport latches, never profile
        // or scene data; the following cases execute the real negotiation hook.
        ArenaMenuSelection.EndFlow();
        ArenaMenuSelection.RegisterTeamArenaQuery(() => false);
        ArenaSession.ArmedForNextRaid = false;
        ArenaSession.ArmedMode = null;
        foreach (var name in new[] { "_pendingArmToken", "_activeArmToken", "_boundNativeEndIdentity",
            "_ordinaryStartIdentity", "_ordinaryNativeEndIdentity" })
            typeof(ArenaServerClient).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
        foreach (var name in new[] { nameof(ArenaServerClient.HasPendingArm), nameof(ArenaServerClient.HasPendingEnd) })
            typeof(ArenaServerClient).GetProperty(name).GetSetMethod(true).Invoke(null, new object[] { false });
        typeof(ArenaServerClient).GetField("_initialStateReconciled", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
        typeof(ArenaServerClient).GetField("_ordinaryRaidConfirmed", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        Settings.GameMode.Value = "teamdeathmatch";
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FactorySouthId;
        ArenaAreaRuntime.Reset();
        ArenaAreaNavigationPatches.IsReady = ArenaAreaLoadPatch.IsReady = ArenaAreaSpawnPointsPatch.IsReady = true;
        RequestHandler.Get = route => route == Protocol.StatusRoute ? "{\"state\":\"Idle\"}" : "{\"version\":\"test\"}";
        RequestHandler.Post = (route, body) =>
        {
            if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true,\"state\":\"Idle\"}";
            var sent = JObject.Parse(body);
            return new JObject { ["armed"] = true, ["armToken"] = sent["requestId"], ["areaId"] = sent["areaId"] }.ToString();
        };
    }

    private static void CheckAreaNegotiation()
    {
        foreach (string wrongArea in new[] { null, ArenaAreaCatalog.FullMapId, "factory-south-v2", "FACTORY-SOUTH-V1" })
        {
            ConfigureAreaNegotiation();
            RequestHandler.Post = (route, body) => route == Protocol.RaidStartRoute
                ? new JObject { ["armed"] = true, ["armToken"] = JObject.Parse(body)["requestId"], ["areaId"] = wrongArea }.ToString()
                : "{\"disarmed\":true,\"state\":\"Idle\"}";
            Assert(!ArenaServerClient.PostRaidStart("factory4_day", "teamdeathmatch", false, ArenaAreaCatalog.FactorySouthId),
                "bounded start rejects missing, downgraded, future or differently cased area acknowledgement");
            Assert(ArenaServerClient.HasPendingArm && Protocol.IsLifecycleRequestId(ArenaServerClient.PendingArmToken),
                "area acknowledgement mismatch retains exact snapshot cancellation identity");
            Assert(ArenaServerClient.PostRaidCancel(startFailed: true) && !ArenaServerClient.HasPendingArm,
                "area mismatch remains recoverable by acknowledged exact-token cancellation");
        }

        ConfigureAreaNegotiation();
        Select();
        var request = Request();
        Assert(Start(request, out var task) && task == null, "supported area starts through the actual local-start prefix");
        var sent = JObject.Parse(RequestHandler.LastPostBody);
        Assert(sent["areaId"].Value<string>() == ArenaAreaCatalog.FactorySouthId
            && sent["map"].Value<string>() == "factory4_day" && sent["mode"].Value<string>() == "teamdeathmatch",
            "exact area, map and normalized mode are sent together");
        Assert(ArenaAreaRuntime.ArmCalls == 1 && ReferenceEquals(ArenaAreaRuntime.ArmedArea, ArenaAreaCatalog.FactorySouth)
            && ArenaAreaRuntime.Token == sent["requestId"].Value<string>()
            && Protocol.IsNativeStartIdentity(request.serverId, ArenaAreaRuntime.Token),
            "only acknowledged area is armed with the same exact native-start token");
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
        Assert(ReferenceEquals(ArenaAreaRuntime.ArmedArea, ArenaAreaCatalog.FactorySouth),
            "changing F12 after negotiation cannot rewrite the captured area");

        ConfigureAreaNegotiation();
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.CustomsFactoryCenterPolygonId;
        Select(ArenaMapCatalog.CustomsId);
        request = Request(map: ArenaMapCatalog.CustomsId);
        Assert(Start(request, out task) && task == null
            && ReferenceEquals(ArenaAreaRuntime.ArmedArea, ArenaAreaCatalog.CustomsFactoryCenterPolygon)
            && !ArenaAreaRuntime.ArmedArea.Bounds.LimitsHeight,
            "Actual local start arms the selected height-unlimited contour");
        sent = JObject.Parse(RequestHandler.LastPostBody);
        Assert(sent["areaId"].Value<string>() == ArenaAreaCatalog.CustomsFactoryCenterPolygonId
            && ArenaAreaRuntime.Token == sent["requestId"].Value<string>()
            && Protocol.IsNativeStartIdentity(request.serverId, ArenaAreaRuntime.Token),
            "The new contour uses the same exact acknowledged identity on both halves");

        foreach (string wrongArea in new[] { ArenaAreaCatalog.CustomsFactoryCenterId,
                     ArenaAreaCatalog.FullMapId, "customs-factory-center-polygon-v2",
                     ArenaAreaCatalog.CustomsFactoryCenterPolygonId.ToUpperInvariant() })
        {
            ConfigureAreaNegotiation();
            RequestHandler.Post = (route, body) => route == Protocol.RaidStartRoute
                ? new JObject { ["armed"] = true, ["armToken"] = JObject.Parse(body)["requestId"], ["areaId"] = wrongArea }.ToString()
                : "{\"disarmed\":true,\"state\":\"Idle\"}";
            Assert(!ArenaServerClient.PostRaidStart(ArenaMapCatalog.CustomsId, "teamdeathmatch", false,
                    ArenaAreaCatalog.CustomsFactoryCenterPolygonId) && ArenaAreaRuntime.ArmCalls == 0,
                "A contour cannot downgrade to the old rectangle or accept another version in a server reply");
            Assert(ArenaServerClient.PostRaidCancel(startFailed: true) && !ArenaServerClient.HasPendingArm,
                "Contour negotiation mismatch preserves exact-token recovery");
        }

        foreach (string map in new[] { "factory4_night", "laboratory", "labyrinth" })
        {
            ConfigureAreaNegotiation();
            Select(map);
            int starts = RequestHandler.StartCount;
            Assert(!Start(Request(map: map), out task) && task.IsFaulted,
                "bounded area rejects other otherwise-supported map variants");
            Assert(RequestHandler.StartCount == starts && ArenaAreaRuntime.ArmCalls == 0,
                "wrong area map is rejected before server snapshot announcement and local arming");
        }

        foreach (string mode in new[] { "endless", "tactical", "gungame" })
        {
            ConfigureAreaNegotiation();
            Select();
            Settings.GameMode.Value = mode;
            Assert(Start(Request(), out task), "stored TDM area does not prevent a different Arena mode");
            Assert(JObject.Parse(RequestHandler.LastPostBody)["areaId"].Value<string>() == ArenaAreaCatalog.FullMapId
                && ArenaAreaRuntime.ArmedArea == null, "other modes explicitly negotiate full-map only");
        }

        foreach (string missingHook in new[] { "navigation", "load", "spawn" })
        {
            ConfigureAreaNegotiation();
            Select();
            ArenaAreaNavigationPatches.IsReady = missingHook != "navigation";
            ArenaAreaLoadPatch.IsReady = missingHook != "load";
            ArenaAreaSpawnPointsPatch.IsReady = missingHook != "spawn";
            int starts = RequestHandler.StartCount;
            Assert(!Start(Request(), out task) && task.IsFaulted && RequestHandler.StartCount == starts,
                "each required area hook must be ready before server arming");
        }

        ConfigureAreaNegotiation();
        Select();
        ArenaMenuSelection.RegisterTeamArenaQuery(() => true);
        int beforePft = RequestHandler.StartCount;
        Assert(!Start(Request(), out task) && task.IsFaulted && RequestHandler.StartCount == beforePft,
            "PitFireTeam cannot inherit the independent TDM area");

        ConfigureAreaNegotiation();
        Select();
        Settings.TeamDeathmatchArea.Value = "unknown-area";
        int beforeUnknown = RequestHandler.StartCount;
        Assert(!Start(Request(), out task) && task.IsFaulted && RequestHandler.StartCount == beforeUnknown,
            "unrecognized configured area cannot silently become full-map");

        ConfigureAreaNegotiation();
        Select();
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
        ArenaAreaNavigationPatches.IsReady = ArenaAreaLoadPatch.IsReady = ArenaAreaSpawnPointsPatch.IsReady = false;
        RequestHandler.Post = (route, body) => new JObject
            { ["armed"] = true, ["armToken"] = JObject.Parse(body)["requestId"] }.ToString();
        Assert(Start(Request(), out task) && ArenaAreaRuntime.ArmedArea == null,
            "legacy full-map server replies remain compatible without area hooks or an area echo");
        ConfigureAreaNegotiation();
    }
}
