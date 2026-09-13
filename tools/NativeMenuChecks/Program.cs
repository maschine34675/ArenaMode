using System;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

// Fault-injection checks compile the actual selection/HTTP/negotiation files.
// Only game, BepInEx and transport dependencies are stand-ins; this is not a
// substitute for Harmony binding, Unity layout or a real raid/profile diff.
internal static partial class Program
{
    private static int _checks;
    private static readonly MethodInfo Hook = typeof(LocalRaidStartedPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);

    private static void Assert(bool condition, string scenario)
    {
        if (!condition) throw new Exception(scenario);
        _checks++;
    }

    private static bool Start(LocalRaidSettings request, out Task<LocalSettings> task)
    {
        var args = new object[] { request, null };
        var runOriginal = (bool)Hook.Invoke(null, args);
        task = (Task<LocalSettings>)args[1];
        if (task?.IsFaulted == true) _ = task.Exception;
        return runOriginal;
    }

    private static RaidSettings Select(string map = "factory4_day")
    {
        ArenaMenuSelection.EndFlow();
        var settings = new RaidSettings { Side = ESideType.Pmc };
        ArenaMenuSelection.Begin(new object(), settings);
        ArenaMenuSelection.Select();
        settings.SelectedLocation = new LocationSettings.Location { Id = map };
        settings.transitionType = ELocationTransition.Common;
        return settings;
    }

    private static LocalRaidSettings Request(ESideType side = ESideType.Pmc, string map = "factory4_day")
        => new() { playerSide = side, location = map, transitionType = ELocationTransition.Common };

    public static void Main()
    {
        var controller = new object();
        var remembered = new LocationSettings.Location { Id = "Woods" };
        var settings = new RaidSettings
        {
            Side = ESideType.Pmc, SelectedLocation = remembered,
            SelectedDateTime = EDateTime.PAST, transitionType = ELocationTransition.Event, KeyId = "normal-key"
        };
        ArenaMenuSelection.Begin(controller, settings);
        ArenaMenuSelection.Select();
        Assert(ArenaMode.Audio.AnnouncerService.EnsureLoadedCalls == 1,
            "Selecting Arena must start announcer preload exactly once.");
        ArenaMenuSelection.Select();
        Assert(ArenaMode.Audio.AnnouncerService.EnsureLoadedCalls == 1,
            "Repeated selection in the same flow must not restart announcer preload.");
        settings.SelectedLocation = new LocationSettings.Location { Id = "laboratory" };
        settings.SelectedDateTime = EDateTime.CURR;
        settings.transitionType = ELocationTransition.Common;
        settings.KeyId = "";
        ArenaMenuSelection.Begin(controller, settings);
        Assert(ArenaMenuSelection.IsSelectedFor(settings), "Back must preserve Arena within the same controller");
        Assert(!ArenaMenuSelection.IsSelectedFor(new RaidSettings { Side = ESideType.Pmc }), "Another settings object must not inherit Arena");
        ArenaMenuSelection.Clear();
        Assert(settings.SelectedLocation == remembered && settings.SelectedDateTime == EDateTime.PAST
            && settings.transitionType == ELocationTransition.Event && settings.KeyId == "normal-key", "Normal map/time/transition/key must be restored");
        Assert(Start(Request(), out var task) && task == null && RequestHandler.StartCount == 0, "Ordinary raid must not announce Arena");
        Assert(RequestHandler.PostCount == 1 && !ArenaServerClient.NeedsInitialReconciliation, "First ordinary raid must reconcile an arm surviving a client restart");
        Assert(Start(Request(), out task) && RequestHandler.PostCount == 1, "Confirmed normal raids need no repeated startup reconciliation");

        Select();
        Assert(!Start(Request(ESideType.Savage), out task) && task.IsFaulted, "Scav cannot enter an explicitly selected Arena");
        Assert(RequestHandler.StartCount == 0 && !InGameStatus.InRaid, "Rejected Scav request must not arm server or leave in-raid flag");
        Select();
        Assert(!Start(Request(map: "Woods"), out task) && task.IsFaulted, "Changed map cannot enter Arena");
        Assert(RequestHandler.StartCount == 0, "Changed map must be rejected before announcement");

        // PitFireTeam puts a synthetic group on EFT's local-ready branch. EFT's
        // offline clone omitted transitionType, so repair only the exact
        // Common-to-None change after vanilla applies that clone.
        int teamFreezeCount = 0;
        var teamSettings = Select("laboratory");
        ArenaMenuSelection.ConfigureEligibility(
            () => new ArenaMenuSelection.Eligibility(true, true, string.Empty, new object()),
            eligibility => { teamFreezeCount++; return string.Empty; });
        var offlineSettings = new RaidSettings { transitionType = ELocationTransition.None };
        var transitionBeforeApply = teamSettings.transitionType;
        teamSettings.transitionType = offlineSettings.transitionType; // RaidSettings.Apply
        Assert(ArenaMenuSelection.RestoreArenaTransitionAfterCloneApply(
                   teamSettings, offlineSettings, transitionBeforeApply)
               && teamSettings.transitionType == ELocationTransition.Common
               && offlineSettings.transitionType == ELocationTransition.None,
            "Arena must repair the proven clone omission without mutating EFT's offline clone");
        var teamLocalRequest = Request(map: "laboratory");
        Assert(ArenaMenuSelection.TryValidateAndFreezeRequest(teamLocalRequest, out var teamReason),
            "Preserved local team request must pass final validation: " + teamReason);
        Assert(teamFreezeCount == 1,
            "Preserved local team request must freeze the freshly validated roster exactly once");

        teamLocalRequest.transitionType = ELocationTransition.Event;
        Assert(!ArenaMenuSelection.TryValidateAndFreezeRequest(teamLocalRequest, out _),
            "Authenticated team Arena must still reject event/transit starts");

        teamSettings.transitionType = ELocationTransition.Event;
        Assert(!ArenaMenuSelection.RestoreArenaTransitionAfterCloneApply(
                   teamSettings, offlineSettings, ELocationTransition.Common)
               && teamSettings.transitionType == ELocationTransition.Event,
            "Unexpected post-Apply transitions must remain visible to the strict final gate");
        ArenaMenuSelection.EndFlow();
        Assert(teamSettings.transitionType == ELocationTransition.None
               && offlineSettings.transitionType == ELocationTransition.None,
            "Back/cancel must restore the selected settings and leave the same offline clone untouched");
        Assert(!ArenaMenuSelection.RestoreArenaTransitionAfterCloneApply(
                   teamSettings, offlineSettings, ELocationTransition.Common)
               && teamSettings.transitionType == ELocationTransition.None,
            "The same settings object must remain ordinary after Arena selection is cleared");
        Assert(!ArenaMenuSelection.RestoreArenaTransitionAfterCloneApply(
                   null, offlineSettings, ELocationTransition.Common),
            "A missing Apply destination must fail closed without mutation");

        // Server received the announcement, but BOTH its reply and our cancel
        // response are lost. A later ordinary raid must still be blocked.
        Select();
        RequestHandler.Post = (route, body) => throw new InvalidOperationException("simulated lost response");
        InGameStatus.InRaid = true;
        Assert(!Start(Request(), out task) && task.IsFaulted, "Lost announcement response must block original local/start");
        Assert(ArenaServerClient.HasPendingArm && !InGameStatus.InRaid, "Uncertain server arm must survive cancellation failure");
        ArenaMenuSelection.EndFlow();
        Assert(!Start(Request(ESideType.Savage), out task) && task.IsFaulted, "Pending cancellation must block a later normal Scav");
        Assert(!Start(Request(), out task) && task.IsFaulted, "Pending cancellation must block a later normal PMC");
        Assert(ArenaServerClient.HasPendingArm, "Repeated failures must not forget uncertainty");
        RequestHandler.Post = (route, body) => "{\"disarmed\":false,\"state\":\"Idle\"}";
        Assert(Start(Request(), out task) && task == null, "Confirmed idle must restore normal raid start");
        Assert(!ArenaServerClient.HasPendingArm, "Idle acknowledgement must settle pending arm");
        Assert(RequestHandler.LastPostBody.Contains("\"startFailed\":true", StringComparison.Ordinal),
            "same-process retry must recover a consumed unconfirmed native start");

        // A negative/malformed announcement response can arrive after the
        // server committed Armed (for example, the first POST response was lost
        // and a transport retry observed the existing state). Never forget that
        // uncertainty until status plus the exact token settles it.
        RequestHandler.Post = (route, body) =>
            route == Protocol.RaidStartRoute
                ? "{\"armed\":false,\"state\":\"Armed\"}"
                : "{\"disarmed\":false,\"state\":\"Idle\"}";
        Assert(!ArenaServerClient.PostRaidStart("factory4_day", "tactical"),
            "an Armed rejection is not a successful announcement");
        Assert(ArenaServerClient.HasPendingArm,
            "an Armed or ambiguous rejection must retain announcement uncertainty");
        Assert(ArenaServerClient.PostRaidCancel(startFailed: true)
               && !ArenaServerClient.HasPendingArm,
            "status-derived exact token cancellation must settle the uncertain arm");

        // GameEnd reaches the server before /client/match/local/end, so the first
        // raid-end answer is normally deferred:true with state InRaid. That exact
        // answer is the designed hand-off to the post-raid prefix and logs Info;
        // every other unacknowledged answer is a real failure and stays a warning.
        // Either way the end stays pending (the block below settles it).
        typeof(ArenaServerClient).GetField("_activeArmToken", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, "deferred-end-token");
        var raidEndAnswer = "{\"restored\":false,\"deferred\":true,\"state\":\"InRaid\"}";
        RequestHandler.Post = (route, body) => route == Protocol.RaidEndRoute
            ? raidEndAnswer
            : "{\"disarmed\":false,\"state\":\"Idle\"}";
        var warningsBeforeEnd = ArenaMode.Plugin.ModLogger.WarningCount;
        var infosBeforeEnd = ArenaMode.Plugin.ModLogger.InfoCount;
        Assert(!ArenaServerClient.PostRaidEnd() && ArenaServerClient.HasPendingEnd,
            "a deferred raid-end answer is not an acknowledgement; the end stays pending");
        Assert(ArenaMode.Plugin.ModLogger.WarningCount == warningsBeforeEnd
               && ArenaMode.Plugin.ModLogger.InfoCount == infosBeforeEnd + 1
               && ArenaMode.Plugin.ModLogger.LastInfo.Contains("remains pending", StringComparison.Ordinal),
            "the designed deferral (deferred:true, state InRaid) logs Info, not a warning");
        foreach (var failure in new[]
                 {
                     "{\"restored\":false,\"completed\":false,\"state\":\"InRaid\",\"reason\":\"snapshot restore failed\"}",
                     "{\"restored\":false,\"deferred\":true,\"state\":\"FinalizingEnd\"}",
                     "{\"restored\":false}",
                 })
        {
            raidEndAnswer = failure;
            var warningsBeforeFailure = ArenaMode.Plugin.ModLogger.WarningCount;
            Assert(!ArenaServerClient.PostRaidEnd()
                   && ArenaServerClient.HasPendingEnd
                   && ArenaMode.Plugin.ModLogger.WarningCount == warningsBeforeFailure + 1
                   && ArenaMode.Plugin.ModLogger.LastWarning.Contains("remains pending", StringComparison.Ordinal),
                "an unacknowledged raid-end answer other than the designed deferral stays a warning: " + failure);
        }

        // GameEnd is observed before EFT's async native end completes. If that
        // native request later exhausts its retries, the next negotiation must
        // explicitly recover the exact token rather than defer forever.
        typeof(ArenaServerClient).GetField("_activeArmToken", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, "pending-end-token");
        typeof(ArenaServerClient).GetProperty(nameof(ArenaServerClient.HasPendingEnd))
            .GetSetMethod(nonPublic: true).Invoke(null, new object[] { true });
        RequestHandler.Post = (route, body) => route == Protocol.RaidEndRoute
            ? "{\"restored\":true,\"completed\":true,\"state\":\"Idle\"}"
            : "{\"disarmed\":false,\"state\":\"Idle\"}";
        Assert(Start(Request(), out task) && task == null,
            "next ordinary raid must recover a failed native Arena end in the same process");
        Assert(RequestHandler.LastPostRoute == Protocol.RaidEndRoute
               && RequestHandler.LastPostBody.Contains("\"recoverWithoutNativeEnd\":true", StringComparison.Ordinal),
            "same-process end recovery must be explicit and token bound");

        // JSON property order and whitespace must not affect acknowledgement.
        Select("factory4_night");
        RequestHandler.Post = (route, body) => route == Protocol.RaidStartRoute
            ? $"{{ \"snapshotTaken\": true, \"armToken\": \"{JObject.Parse(body)["requestId"]?.Value<string>()}\", \"armed\": true }}"
            : route == Protocol.RaidConfirmRoute
                ? "{\"confirmed\":true,\"state\":\"InRaid\"}"
                : "{\"disarmed\":true}";
        ArenaMode.Config.Settings.GameMode.Value = "  GUNGAME  ";
        var acknowledgedArenaRequest = Request(map: "factory4_night");
        Assert(Start(acknowledgedArenaRequest, out task) && task == null && ArenaSession.ArmedForNextRaid,
            "Valid Factory-night server acknowledgement must arm Arena");
        Assert(ArenaSession.ArmedMode == "gungame"
               && JObject.Parse(RequestHandler.LastPostBody)["mode"]?.Value<string>() == "gungame"
               && JObject.Parse(RequestHandler.LastPostBody)["map"]?.Value<string>() == "factory4_night",
            "the canonical native mode and Factory-night id reach both client and server raid state");
        string acknowledgedArmToken = acknowledgedArenaRequest.serverId[
            Protocol.NativeStartIdentityPrefix.Length..];
        Assert(ArenaServerClient.BindNativeEndIdentity(new LocalSettings
        {
            serverId = Protocol.BuildNativeEndIdentity(
                "factory4_night.Pmc menu-test",
                acknowledgedArmToken)
        }), "Client must acknowledge the exact server-bound native end identity");
        ArenaServerClient.ConfirmRaidStarted();
        Assert(!ArenaServerClient.HasPendingArm, "Started raid must settle announcement");
        ArenaSession.ClearArmed("test cleanup");
        ArenaMenuSelection.Begin(new object(), settings);
        Assert(!ArenaMenuSelection.IsSelected, "A fresh setup controller must reset Arena");

        var solo = true;
        ArenaMenuSelection.Begin(new object(), settings, () => solo);
        ArenaMenuSelection.Select();
        settings.SelectedLocation = new LocationSettings.Location { Id = "factory4_day" };
        settings.transitionType = ELocationTransition.Common;
        solo = false;
        Assert(!Start(Request(), out task) && task.IsFaulted, "A party joined after selection must not start Arena");
        ArenaMenuSelection.EndFlow();

        // Simulate a new client process with an old server arm: reconciliation
        // must fail closed even though no in-memory pending flag exists yet.
        typeof(ArenaServerClient).GetField("_initialStateReconciled", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        RequestHandler.Post = (route, body) => throw new InvalidOperationException("server cancellation unavailable");
        Assert(!Start(Request(), out task) && task.IsFaulted, "Client restart cannot bypass failed server reconciliation");
        Assert(ArenaServerClient.NeedsInitialReconciliation && ArenaServerClient.HasPendingArm, "Startup uncertainty must remain pending");
        RequestHandler.Post = (route, body) => "{\"disarmed\":false,\"state\":\"Idle\"}";
        Assert(Start(Request(ESideType.Savage), out task) && task == null, "Retry after client restart must recover normal Scav start");

        typeof(ArenaServerClient).GetField("_initialStateReconciled", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        RequestHandler.Get = route => throw new AggregateException(new Exception("Http response status code: NotFound"));
        var serverAbsentRequest = Request();
        Assert(Start(serverAbsentRequest, out task) && task == null, "Confirmed 404 absence must allow normal raids without server mod");
        Assert(!ArenaServerClient.NeedsInitialReconciliation, "Confirmed absent server mod must settle startup check");
        Assert(serverAbsentRequest.serverId == null && !ArenaServerClient.HasOrdinaryLifecycle,
            "Client-only installs must leave an ordinary native request byte-identical and publish no lifecycle claim");

        var reconnectRequest = Request();
        reconnectRequest.serverId = "native-reconnect-id";
        Assert(Start(reconnectRequest, out task) && reconnectRequest.serverId == "native-reconnect-id",
            "A pre-existing ordinary reconnect identity must pass through byte-identically");

        typeof(ArenaServerClient).GetField("_initialStateReconciled", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        RequestHandler.Get = route => throw new Exception("simulated transport timeout");
        Assert(!Start(Request(), out task) && task.IsFaulted, "Transport timeout is uncertain, not a missing server mod");
        Assert(ArenaServerClient.NeedsInitialReconciliation, "Failed probe must remain retryable");
        RequestHandler.Get = route => throw new Exception("Http response status code: InternalServerError");
        Assert(!Start(Request(), out task) && task.IsFaulted, "HTTP 500 must not be treated as absent server mod");
        RequestHandler.Get = route => "{\"wrongResponse\":true}";
        Assert(!Start(Request(), out task) && task.IsFaulted, "Malformed ping payload cannot settle server state");
        RequestHandler.Get = route => "{\"version\":\"test\"}";
        Assert(Start(Request(), out task) && task == null, "Probe must recover after temporary failure");

        RequestHandler.Get = route => route == Protocol.ClientSettingsRoute
            ? "{}"
            : "{\"version\":\"test\"}";
        var wrapperGets = RequestHandler.WrapperGetCount;
        var lowLevelGets = RequestHandler.LowLevelGetCount;
        Assert(ArenaServerClient.GetClientSettings() != null
               && RequestHandler.WrapperGetCount == wrapperGets
               && RequestHandler.LowLevelGetCount == lowLevelGets + 1
               && RequestHandler.LastLowLevelGetRoute == Protocol.ClientSettingsRoute,
            "Periodic client-settings reads bypass the noisy RequestHandler wrapper");

        RequestHandler.Get = route => route == Protocol.ClientSettingsRoute
            ? string.Empty
            : "{\"version\":\"test\"}";
        var warnings = ArenaMode.Plugin.ModLogger.WarningCount;
        Assert(ArenaServerClient.GetClientSettings() == null
               && ArenaMode.Plugin.ModLogger.WarningCount == warnings + 1
               && ArenaMode.Plugin.ModLogger.LastWarning.Contains("empty response", StringComparison.Ordinal),
            "A malformed empty success response remains visible without the wrapper");
        CheckAreaNegotiation();
        CheckDeathmatchNegotiation();
        CheckCustomsNegotiation();
        CheckLivePftModeRepair();
        CheckNativeAreas();
        CheckAreaTeamMenu();
        Console.WriteLine($"ARENA_MENU_CHECKS_PASSED ({_checks} assertions)");
    }
}
