using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

// The actual HTTP/negotiation code runs against the established NativeMenuChecks stand-ins.
// Native PreloaderUI's message traversal is reproduced below; Unity rendering is not exercised.
internal static class Program
{
    private const string Refusal = "PitFireTeam persistence contract mismatch at pitTeam.Server.Services.FriendlyTeammateService.CreateTeammate.";
    private const string GenericRefusal = "The server did not arm Arena profile protection.";
    private static readonly MethodInfo Hook = typeof(ArenaLocalRaidStartedPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
    private static int _checks;

    public static void Main()
    {
        try
        {
            DirectReplies();
            NativeFailureFlow();
            Console.WriteLine($"ARENA_START_ERROR_CHECKS_PASSED ({_checks} assertions)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }
    }

    private static void Need(bool condition, string reason)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("ASSERT: " + reason);
    }

    private static void Reset()
    {
        RequestHandler.Post = (_, _) => "{\"disarmed\":true}";
        RequestHandler.Get = route => route == Protocol.StatusRoute ? "{\"state\":\"Idle\"}" : "{\"version\":\"test\"}";
        Need(ArenaServerClient.PostRaidCancel(startFailed: true), "fixture settles earlier exact arm");
        ArenaSession.ClearArmed("fixture reset");
        ArenaMenuSelection.EndFlow();
        ArenaMenuSelection.RegisterCompatibilityAdmission(null);
        ArenaMenuSelection.RegisterCompatibilityCleanup(null);
        ArenaMode.Config.Settings.GameMode.Value = "tactical";
        ArenaMode.Config.Settings.TeamDeathmatchArea.Value = ArenaAreaCatalog.FullMapId;
        InGameStatus.InRaid = false;
    }

    private static string ReplyWithReason(JToken reason)
        => new JObject { ["armed"] = false, ["reason"] = reason }.ToString();

    private static void DirectReplies()
    {
        Reset();
        RequestHandler.Post = (_, _) => ReplyWithReason("  " + Refusal + "  ");
        Need(!ArenaServerClient.PostRaidStart("factory4_day", "tactical", out string failure), "server refusal fails closed");
        Need(failure == Refusal, "exact server reason reaches caller");
        Need(ArenaServerClient.HasPendingArm && !string.IsNullOrEmpty(ArenaServerClient.PendingArmToken), "refusal retains uncertain exact arm");

        foreach (JToken reason in new JToken[] { null, JValue.CreateNull(), "  ", 17, false, new JObject { ["detail"] = "wrong shape" }, new JArray("wrong shape") })
        {
            Reset();
            RequestHandler.Post = (_, _) => ReplyWithReason(reason);
            Need(!ArenaServerClient.PostRaidStart("factory4_day", "tactical", out failure), "absent or invalid reason still refuses");
            Need(failure == GenericRefusal, "no stale or non-string refusal reason");
            Need(ArenaServerClient.HasPendingArm, "invalid reason does not discard uncertainty");
        }

        Reset();
        RequestHandler.Post = (_, _) => "{\"armed\":true,\"armToken\":\"wrong-token\",\"reason\":\"misleading server reason\"}";
        Need(!ArenaServerClient.PostRaidStart("factory4_day", "tactical", out failure), "wrong-token success cannot arm");
        Need(failure == GenericRefusal, "success reply reason cannot disguise identity rejection");

        Reset();
        RequestHandler.Post = (_, body) => new JObject { ["armed"] = true, ["armToken"] = JObject.Parse(body)["requestId"], ["reason"] = Refusal }.ToString();
        Need(ArenaServerClient.PostRaidStart("factory4_day", "tactical", out failure), "exact valid acknowledgement succeeds");
        Need(failure == null, "successful start has no failure reason");

        Reset();
        int requests = RequestHandler.StartCount;
        Need(!ArenaServerClient.PostRaidStart("factory4_day", "deathmatch", out failure), "invalid local selection fails");
        Need(RequestHandler.StartCount == requests && !ArenaServerClient.HasPendingArm, "invalid selection never sends or arms");
        Need(!string.IsNullOrWhiteSpace(failure) && failure != Refusal, "local validation never inherits server reason");

        Reset();
        RequestHandler.Post = (_, _) => throw new AggregateException(new InvalidOperationException("transport interrupted"));
        Need(!ArenaServerClient.PostRaidStart("factory4_day", "tactical", out failure), "transport failure refuses");
        Need(failure.Contains("transport interrupted") && !failure.Contains("One or more errors"), "transport cause is readable once");
        Need(ArenaMode.Plugin.ModLogger.LastError.Contains("AggregateException"), "transport log retains original exception evidence");
        Need(ArenaServerClient.HasPendingArm, "transport failure keeps exact cancellation claim");

        Reset();
        RequestHandler.Post = (_, _) => "{broken-json";
        Need(!ArenaServerClient.PostRaidStart("factory4_day", "tactical", out failure) && !string.IsNullOrWhiteSpace(failure), "malformed acknowledgement refuses with reason");
        Need(ArenaServerClient.HasPendingArm, "malformed acknowledgement retains uncertainty");
    }

    private static void Select()
    {
        var settings = new RaidSettings { Side = ESideType.Pmc };
        ArenaMenuSelection.Begin(new object(), settings);
        ArenaMenuSelection.Select();
        settings.SelectedLocation = new LocationSettings.Location { Id = "factory4_day" };
        settings.transitionType = ELocationTransition.Common;
    }

    private static Exception StartFailure(out bool runOriginal)
    {
        object[] arguments = { new LocalRaidSettings { playerSide = ESideType.Pmc, location = "factory4_day", transitionType = ELocationTransition.Common }, null };
        runOriginal = (bool)Hook.Invoke(null, arguments);
        var task = (Task<LocalSettings>)arguments[1];
        Need(task?.IsFaulted == true, "native start receives a faulted task");
        try { task.GetAwaiter().GetResult(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("ASSERT: missing failure");
    }

    // EFT.UI.PreloaderUI.ShowErrorScreen(header, Exception, callback), EFT build 40743:
    // starts with exception.Message, then appends each InnerException.Message on a new line.
    private static string NativeMessage(Exception exception)
    {
        string text = exception.Message ?? "";
        while (exception.InnerException != null)
        {
            exception = exception.InnerException;
            text = text + "\n" + exception.Message;
        }
        return text;
    }

    private static void NativeFailureFlow()
    {
        foreach (string reason in new[] { Refusal, null })
        {
            Reset();
            int cleanups = 0, cancels = 0;
            string requestedToken = null, cancelledToken = null;
            bool startFailed = false;
            ArenaMenuSelection.RegisterCompatibilityCleanup(_ => cleanups++);
            Select();
            RequestHandler.Post = (route, body) =>
            {
                if (route == Protocol.RaidStartRoute)
                {
                    requestedToken = JObject.Parse(body)["requestId"].Value<string>();
                    return ReplyWithReason(reason);
                }
                if (route == Protocol.RaidCancelRoute)
                {
                    cancels++;
                    var payload = JObject.Parse(body);
                    cancelledToken = payload["armToken"].Value<string>();
                    startFailed = payload["startFailed"].Value<bool>();
                }
                return "{\"disarmed\":true}";
            };
            InGameStatus.InRaid = true;
            Exception error = StartFailure(out bool runOriginal);
            string detail = reason ?? GenericRefusal;
            Need(!runOriginal && !ArenaSession.ArmedForNextRaid, "refused arm never reaches native start");
            Need(error.Message == "Arena start cancelled to protect your profile.", "wrapper contains context only");
            Need(error.InnerException is InvalidOperationException && error.InnerException.Message == detail, "original failure retained as inner cause");
            Need(NativeMessage(error) == "Arena start cancelled to protect your profile.\n" + detail, "native dialog shows detail exactly once");
            Need(cleanups == 1 && cancels == 1 && startFailed && requestedToken == cancelledToken, "failure cleans compatibility and cancels exact failed-start token");
            Need(!ArenaServerClient.HasPendingArm && !InGameStatus.InRaid, "successful cancellation clears arm and legacy raid flag");
        }

        Reset();
        Select();
        RequestHandler.Post = (route, _) => route == Protocol.RaidStartRoute ? ReplyWithReason(Refusal) : throw new InvalidOperationException("cancel unavailable");
        Exception pendingError = StartFailure(out bool original);
        Need(!original && ArenaServerClient.HasPendingArm && !InGameStatus.InRaid, "failed cancellation keeps protection uncertainty");
        Need(NativeMessage(pendingError).EndsWith(Refusal), "cancellation failure cannot replace original refusal");

        // Ordinary raids still bypass Arena arming after an exact cancellation settles the failed attempt.
        Reset();
        int starts = RequestHandler.StartCount;
        object[] ordinary = { new LocalRaidSettings { playerSide = ESideType.Pmc, location = "factory4_day", transitionType = ELocationTransition.Common }, null };
        Need((bool)Hook.Invoke(null, ordinary) && ordinary[1] == null && RequestHandler.StartCount == starts, "ordinary raid remains pass-through");
    }
}
