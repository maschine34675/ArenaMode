using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

// Runs in both linked-source menu harnesses. The production negotiation and HTTP
// code execute; callbacks stand in for optional Cola reflection/gameplay only.
internal static class ColaArmChecks
{
    private static readonly MethodInfo Hook = typeof(ArenaLocalRaidStartedPatch)
        .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
    private const string ProfileId = "fedcba9876543210fedcba98";
    private const string Baseline = "{\"xp\":{\"speedcola\":12},\"ach\":{\"a_first\":true}}";

    internal static void Run(Action<bool, string> check)
    {
        try
        {
            Reset(); Select();
            JObject sent = null;
            RequestHandler.Post = (route, body) =>
            {
                if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true}";
                sent = JObject.Parse(body);
                return Accept(sent);
            };
            check(Start(Request(), out var task) && task == null, "Cola-absent fixture preserves accepted Arena start");
            check(sent != null && sent["colaBaseline"] == null, "Cola-absent fixture sends no optional baseline field");

            Reset(); Select();
            var events = new List<string>();
            string preparedProfile = null, preparedMode = null;
            int preparedEpoch = -1, beforeEpoch = ArenaSession.ArmEpoch;
            bool acceptedAfterAck = false, identityBound = false, armedAtPrepare = false, ackProduced = false;
            var request = Request();
            ArenaTarkovaColaCompat.BeforeArm = () => { events.Add("baseline"); return Baseline; };
            ArenaTarkovaColaCompat.AcceptedArm = (profile, mode, epoch) =>
            {
                events.Add("accepted");
                preparedProfile = profile; preparedMode = mode; preparedEpoch = epoch;
                acceptedAfterAck = ackProduced;
                armedAtPrepare = ArenaSession.ArmedForNextRaid;
                identityBound = Protocol.IsNativeStartIdentity(request.serverId, ArenaServerClient.PendingArmToken);
            };
            RequestHandler.Post = (route, body) =>
            {
                if (route != Protocol.RaidStartRoute) return "{\"disarmed\":true}";
                events.Add("request");
                sent = JObject.Parse(body);
                Settings.GameMode.Value = ArenaModeCatalog.TacticalId; // F12 changes after the captured request.
                ackProduced = true;
                return Accept(sent);
            };
            check(Start(request, out task) && task == null, "Cola acknowledged arm continues native start");
            check(events.SequenceEqual(new[] { "baseline", "request", "accepted" }) && acceptedAfterAck,
                "Cola preparation follows exact server acknowledgement");
            check(sent["colaBaseline"]?.Type == JTokenType.String && sent["colaBaseline"].Value<string>() == Baseline,
                "Cola baseline is transported as its exact optional state string");
            check(preparedProfile == ProfileId, "Cola accepted arm uses the exact backend profile");
            check(preparedMode == ArenaModeCatalog.ZombieId && Settings.SelectedGameModeId == ArenaModeCatalog.TacticalId,
                "Cola accepted arm uses the frozen requested mode");
            check(armedAtPrepare && preparedEpoch == beforeEpoch + 1 && preparedEpoch == ArenaSession.ArmEpoch,
                "Cola accepted arm uses the current accepted epoch");
            check(identityBound, "Cola accepted arm follows exact native identity binding");

            foreach (bool wrongToken in new[] { false, true })
            {
                Reset(); Select();
                int prepares = 0, cleanups = 0, cancellations = 0;
                string requested = null, cancelled = null;
                ArenaTarkovaColaCompat.BeforeArm = () => Baseline;
                ArenaTarkovaColaCompat.AcceptedArm = (_, _, _) => prepares++;
                ArenaMenuSelection.RegisterCompatibilityCleanup(_ => cleanups++);
                RequestHandler.Post = (route, body) =>
                {
                    if (route == Protocol.RaidStartRoute)
                    {
                        requested = JObject.Parse(body)["requestId"].Value<string>();
                        return wrongToken ? "{\"armed\":true,\"armToken\":\"wrong-token\"}"
                            : "{\"armed\":false,\"reason\":\"Cola baseline refused\"}";
                    }
                    if (route == Protocol.RaidCancelRoute)
                    {
                        cancellations++;
                        cancelled = JObject.Parse(body)["armToken"].Value<string>();
                    }
                    return "{\"disarmed\":true}";
                };
                InGameStatus.InRaid = true;
                check(!Start(Request(), out task) && task.IsFaulted && prepares == 0,
                    "Cola refused or mismatched arm never prepares a raid lease");
                check(!ArenaSession.ArmedForNextRaid && !ArenaServerClient.HasPendingArm && !InGameStatus.InRaid
                    && cleanups == 1 && cancellations == 1 && requested == cancelled,
                    "Cola refused arm cleans compatibility and cancels its exact uncertain token");
            }

            Reset(); Select();
            int startCount = RequestHandler.StartCount, acceptedCount = 0;
            ArenaTarkovaColaCompat.BeforeArm = () => throw new InvalidOperationException("Cola pre-arm save failed");
            ArenaTarkovaColaCompat.AcceptedArm = (_, _, _) => acceptedCount++;
            check(!Start(Request(), out task) && task.IsFaulted && RequestHandler.StartCount == startCount && acceptedCount == 0,
                "Cola pre-arm save failure sends no arm and prepares no lease");
            check(!ArenaSession.ArmedForNextRaid && !ArenaServerClient.HasPendingArm,
                "Cola pre-arm save failure settles its locally allocated uncertainty");

            Reset(); Select();
            bool leased = false;
            int ended = 0, failureCleanups = 0, cancelCount = 0;
            string acceptedToken = null, cancellationToken = null;
            ArenaTarkovaColaCompat.BeforeArm = () => Baseline;
            ArenaTarkovaColaCompat.AcceptedArm = (_, _, _) =>
            { leased = true; throw new InvalidOperationException("Cola shadow could not load"); };
            ArenaTarkovaColaCompat.End = () => { ended++; leased = false; };
            ArenaMenuSelection.RegisterCompatibilityCleanup(_ => failureCleanups++);
            RequestHandler.Post = (route, body) =>
            {
                var payload = JObject.Parse(body);
                if (route == Protocol.RaidStartRoute)
                {
                    acceptedToken = payload["requestId"].Value<string>();
                    return Accept(payload);
                }
                if (route == Protocol.RaidCancelRoute)
                { cancelCount++; cancellationToken = payload["armToken"].Value<string>(); }
                return "{\"disarmed\":true}";
            };
            InGameStatus.InRaid = true;
            check(!Start(Request(), out task) && task.IsFaulted,
                "Cola preparation failure after acceptance blocks native start");
            check(ended == 1 && !leased && failureCleanups == 1 && cancelCount == 1 && acceptedToken == cancellationToken
                && !ArenaSession.ArmedForNextRaid && !ArenaServerClient.HasPendingArm && !InGameStatus.InRaid,
                "Cola failure after acceptance cancels its exact arm and clears lease");

            foreach (var side in new[] { ESideType.Pmc, ESideType.Savage })
            {
                Reset();
                int optionalCalls = 0;
                ArenaTarkovaColaCompat.BeforeArm = () => { optionalCalls++; throw new InvalidOperationException("ordinary must not call Cola"); };
                ArenaTarkovaColaCompat.AcceptedArm = (_, _, _) => optionalCalls++;
                startCount = RequestHandler.StartCount;
                check(Start(Request(side), out task) && task == null && optionalCalls == 0 && RequestHandler.StartCount == startCount,
                    "Ordinary " + side + " start bypasses Cola arm preparation");
            }
        }
        finally { Reset(); }
    }

    private static string Accept(JObject payload) => new JObject
    {
        ["armed"] = true, ["armToken"] = payload["requestId"],
        ["participants"] = payload["participants"], ["participantBosses"] = false,
        ["deathmatchHostility"] = payload["deathmatchHostility"]
    }.ToString();

    private static bool Start(LocalRaidSettings request, out Task<LocalSettings> task)
    {
        object[] args = { new EftClientBackendSession { Profile = new Profile { Id = ProfileId } }, request, null };
        bool result = (bool)Hook.Invoke(null, args);
        task = (Task<LocalSettings>)args[2];
        if (task?.IsFaulted == true) _ = task.Exception;
        return result;
    }

    private static LocalRaidSettings Request(ESideType side = ESideType.Pmc) => new()
    { location = "factory4_day", playerSide = side, transitionType = ELocationTransition.Common };

    private static void Select()
    {
        var settings = new RaidSettings { Side = ESideType.Pmc };
        ArenaMenuSelection.Begin(new object(), settings);
        ArenaMenuSelection.Select();
        settings.SelectedLocation = new LocationSettings.Location { Id = "factory4_day" };
        settings.transitionType = ELocationTransition.Common;
    }

    private static void Reset()
    {
        ArenaTarkovaColaCompat.BeforeArm = null;
        ArenaTarkovaColaCompat.AcceptedArm = null;
        ArenaTarkovaColaCompat.End = null;
        RequestHandler.Post = (_, _) => "{\"disarmed\":true}";
        RequestHandler.Get = route => route == Protocol.StatusRoute ? "{\"state\":\"Idle\"}" : "{\"version\":\"test\"}";
        ArenaSession.ClearArmed("Cola fixture reset");
        ArenaMenuSelection.EndFlow();
        ArenaMenuSelection.RegisterCompatibilityAdmission(null);
        ArenaMenuSelection.RegisterCompatibilityCleanup(null);
        ArenaMenuSelection.RegisterTeamArenaQuery(null);
        foreach (string name in new[] { "_pendingArmToken", "_activeArmToken", "_boundNativeEndIdentity", "_ordinaryStartIdentity", "_ordinaryNativeEndIdentity" })
            typeof(ArenaServerClient).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        foreach (string name in new[] { nameof(ArenaServerClient.HasPendingArm), nameof(ArenaServerClient.HasPendingEnd) })
            typeof(ArenaServerClient).GetProperty(name).GetSetMethod(true).Invoke(null, new object[] { false });
        typeof(ArenaServerClient).GetField("_initialStateReconciled", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        typeof(ArenaServerClient).GetField("_ordinaryRaidConfirmed", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        Settings.GameMode.Value = ArenaModeCatalog.ZombieId;
        Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
        SettingsSync.OnSync = null;
        ArenaParticipantSettings.Override = null;
        InGameStatus.InRaid = false;
    }
}
