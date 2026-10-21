using System;
using ArenaMode.Config;
using ArenaMode.Session;
using EFT;
using SPT.Common.Http;

internal static partial class Program
{
    private static void CheckOptionalCompatibility()
    {
        var oldSync = SettingsSync.OnSync;
        var oldMode = Settings.GameMode.Value;
        string reason = null;
        int calls = 0, freezes = 0;
        try
        {
            Settings.GameMode.Value = "deathmatch";
            ArenaMenuSelection.RegisterCompatibilityAdmission(() => { calls++; return reason; });
            Select();
            ArenaMenuSelection.ConfigureEligibility(
                () => new ArenaMenuSelection.Eligibility(true, true, ""),
                _ => { freezes++; return ""; },
                () => new ArenaMenuSelection.Eligibility(true, true, ""));
            Assert(ArenaMenuSelection.CurrentEligibility.Allowed && ArenaMenuSelection.CurrentModeEligibility.Allowed,
                "installed but compatible optional mods preserve both menu providers");
            reason = "Arena is currently unavailable with ORBIT.";
            Assert(!ArenaMenuSelection.CanStart && !ArenaMenuSelection.CurrentModeEligibility.Allowed
                && ArenaMenuSelection.CurrentEligibility.Reason == reason,
                "late blocker overrides even an allowed squad/pending-mode provider with visible reason");
            Assert(!ArenaMenuSelection.TryValidateAndFreezeRequest(Request(), out var rejection)
                && rejection == reason && freezes == 0,
                "stale selected Arena cannot freeze a roster after optional-mod rejection");
            int starts = RequestHandler.StartCount;
            Assert(!Start(Request(), out var task) && task.IsFaulted
                && RequestHandler.StartCount == starts && !ArenaSession.ArmedForNextRaid,
                "blocked native Arena start never arms the server");
            Assert(task.Exception.ToString().Contains(reason), "native rejection explains the conflicting mod");

            ArenaMenuSelection.EndFlow();
            int before = calls;
            Assert(!ArenaMenuSelection.CurrentEligibility.Allowed && calls > before,
                "EndFlow preserves the process-lifetime protection provider");
            foreach (var side in new[] { ESideType.Pmc, ESideType.Savage })
            {
                before = calls;
                Assert(Start(Request(side), out task) && task == null
                    && calls == before && RequestHandler.StartCount == starts,
                    "ordinary " + side + " start bypasses compatibility blockers entirely");
            }

            reason = null;
            Select();
            SettingsSync.OnSync = () => reason = "Questing Bots became active during settings refresh.";
            Assert(!Start(Request(), out task) && task.IsFaulted && RequestHandler.StartCount == starts,
                "final post-refresh check catches newly active conflicts before server arm");

            SettingsSync.OnSync = oldSync;
            ArenaMenuSelection.RegisterCompatibilityAdmission(() => throw new InvalidOperationException("adapter probe failed"));
            Select();
            Assert(!ArenaMenuSelection.CurrentEligibility.Allowed && !ArenaMenuSelection.CurrentModeEligibility.Allowed,
                "failed compatibility probe fails closed for Arena presentation");
            Assert(!Start(Request(), out task) && task.IsFaulted && RequestHandler.StartCount == starts,
                "failed compatibility probe fails closed for Arena start");
            ArenaMenuSelection.EndFlow();
            Assert(Start(Request(), out task) && task == null,
                "ordinary raid still starts when optional compatibility probing would throw");

            CheckServerOnlyAdmission();
            CheckSessionPingFields();
            CheckBoundedPing();
        }
        finally
        {
            ArenaMenuSelection.EndFlow();
            ArenaMenuSelection.RegisterCompatibilityAdmission(null);
            SettingsSync.OnSync = oldSync;
            Settings.GameMode.Value = oldMode;
        }
    }

    // Arena session spec, sections 7 and 8: the ping may carry arenaInstance (bool) and serverMods
    // ([{guid,name,version,folder}]). Absent means an older server; malformed parts are ignored.
    private static void CheckSessionPingFields()
    {
        var oldGet = RequestHandler.Get;
        try
        {
            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.2\"}" : oldGet(route);
            int generation = ArenaServerClient.PingGeneration;
            Assert(ArenaServerClient.RefreshAvailability()
                && !ArenaServerClient.SessionFieldsPresent && !ArenaServerClient.IsArenaInstance
                && ArenaServerClient.ServerMods == null && ArenaServerClient.PingGeneration == generation + 1,
                "a ping without the session fields reads as an older server: no session support, not an error");

            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.3\",\"arenaInstance\":false,\"serverMods\":[]}" : oldGet(route);
            Assert(ArenaServerClient.RefreshAvailability()
                && ArenaServerClient.SessionFieldsPresent && !ArenaServerClient.IsArenaInstance
                && ArenaServerClient.ServerMods != null && ArenaServerClient.ServerMods.Count == 0,
                "a current normal server reports arenaInstance false and an empty list");

            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.3\",\"arenaInstance\":true,\"serverMods\":["
                    + "{\"guid\":\"me.sol.sain\",\"name\":\"SAIN\",\"version\":\"4.5.1\",\"folder\":\"C:\\\\SPT\\\\user\\\\mods\\\\SAIN\"},"
                    + "{\"guid\":\"\",\"name\":\"no guid\"},42,null,\"text\","
                    + "{\"guid\":\"com.example.partial\",\"name\":7,\"version\":null}]}"
                : oldGet(route);
            Assert(ArenaServerClient.RefreshAvailability()
                && ArenaServerClient.SessionFieldsPresent && ArenaServerClient.IsArenaInstance
                && ArenaServerClient.ServerMods.Count == 2,
                "arenaInstance true is read and malformed serverMods entries are skipped");
            var sain = ArenaServerClient.ServerMods[0];
            Assert(sain.Guid == "me.sol.sain" && sain.Name == "SAIN" && sain.Version == "4.5.1"
                && sain.Folder == "C:\\SPT\\user\\mods\\SAIN",
                "a complete serverMods entry keeps guid, name, version and folder");
            var partial = ArenaServerClient.ServerMods[1];
            Assert(partial.Guid == "com.example.partial" && partial.Name == null
                && partial.Version == null && partial.Folder == null,
                "wrongly typed or missing members of an entry read as absent, not as text");

            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.3\",\"arenaInstance\":\"yes\",\"serverMods\":{\"guid\":\"x\"}}" : oldGet(route);
            Assert(ArenaServerClient.RefreshAvailability()
                && !ArenaServerClient.SessionFieldsPresent && !ArenaServerClient.IsArenaInstance
                && ArenaServerClient.ServerMods == null,
                "arenaInstance that is not a boolean and serverMods that is not an array count as absent");

            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.3\",\"arenaInstance\":true,\"serverMods\":[{\"guid\":\"a\"}]}" : oldGet(route);
            ArenaServerClient.RefreshAvailability();
            generation = ArenaServerClient.PingGeneration;
            RequestHandler.Get = route => throw new InvalidOperationException("transport unavailable");
            Assert(!ArenaServerClient.RefreshAvailability() && ArenaServerClient.PingGeneration == generation + 1
                && !ArenaServerClient.SessionFieldsPresent && !ArenaServerClient.IsArenaInstance
                && ArenaServerClient.ServerMods == null,
                "a failed ping forgets the previous session fields and still counts as a new generation");
        }
        finally
        {
            RequestHandler.Get = oldGet;
            ArenaServerClient.RefreshAvailability();
        }
    }

    // The ping the side screen asks for when it opens must never hold the main thread beyond its
    // budget and must never latch an outage; only a successful, timely answer changes anything.
    private static void CheckBoundedPing()
    {
        var oldGet = RequestHandler.Get;
        try
        {
            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.3\",\"arenaInstance\":true,\"serverMods\":[{\"guid\":\"first\"}]}" : oldGet(route);
            Assert(ArenaServerClient.RefreshAvailability() && ArenaServerClient.IsArenaInstance,
                "baseline ping for the bounded refresh");
            int generation = ArenaServerClient.PingGeneration;

            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.3\",\"arenaInstance\":false,\"serverMods\":[{\"guid\":\"second\"},{\"guid\":\"third\"}]}" : oldGet(route);
            Assert(ArenaServerClient.TryRefreshWithin(5000) && ArenaServerClient.IsServerModKnownAvailable
                && !ArenaServerClient.IsArenaInstance && ArenaServerClient.ServerMods.Count == 2
                && ArenaServerClient.PingGeneration == generation + 1,
                "a timely answer is applied exactly like a refresh");

            // A slow server: the caller returns within the budget and the late answer changes nothing.
            generation = ArenaServerClient.PingGeneration;
            RequestHandler.Get = route =>
            {
                if (route != ArenaMode.Shared.Protocol.PingRoute) return oldGet(route);
                System.Threading.Thread.Sleep(600);
                return "{\"version\":\"0.1.3\",\"arenaInstance\":true,\"serverMods\":[]}";
            };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool timely = ArenaServerClient.TryRefreshWithin(40);
            long waited = clock.ElapsedMilliseconds;
            Assert(!timely && waited < 400,
                "a slow server holds the caller for the budget only, not for the server's delay");
            System.Threading.Thread.Sleep(900);
            Assert(!ArenaServerClient.IsArenaInstance && ArenaServerClient.ServerMods.Count == 2
                && ArenaServerClient.PingGeneration == generation && ArenaServerClient.IsServerModKnownAvailable,
                "the late answer is dropped and the earlier ping stays in force");

            // An unreachable server: no state change, no latched outage, no error line.
            string error = ArenaMode.Plugin.ModLogger.LastError;
            RequestHandler.Get = route => throw new InvalidOperationException("transport unavailable");
            Assert(!ArenaServerClient.TryRefreshWithin(2000) && ArenaServerClient.IsServerModKnownAvailable
                && ArenaServerClient.PingGeneration == generation && ArenaMode.Plugin.ModLogger.LastError == error,
                "an unreachable server neither latches an outage nor logs an error at screen open");

            // A malformed answer is not applied either.
            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute ? "<html>" : oldGet(route);
            Assert(!ArenaServerClient.TryRefreshWithin(2000) && ArenaServerClient.PingGeneration == generation
                && ArenaServerClient.ServerMods.Count == 2, "a malformed answer is not applied");

            // After an explicit refresh latched an outage, a timely answer recovers it.
            RequestHandler.Get = route => throw new InvalidOperationException("transport unavailable");
            Assert(!ArenaServerClient.RefreshAvailability() && !ArenaServerClient.IsServerModKnownAvailable,
                "the explicit refresh latches an outage as before");
            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.3\"}" : oldGet(route);
            Assert(ArenaServerClient.TryRefreshWithin(5000) && ArenaServerClient.IsServerModKnownAvailable,
                "a timely answer lifts a latched outage");
        }
        finally
        {
            RequestHandler.Get = oldGet;
            ArenaServerClient.RefreshAvailability();
        }
    }

    private static void CheckServerOnlyAdmission()
    {
        var oldGet = RequestHandler.Get;
        try
        {
            ArenaMenuSelection.RegisterCompatibilityAdmission(() => ArenaServerClient.ServerAdmissionFailure);
            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.2\",\"admissionFailure\":\"ISB Aishi server component is not isolated\"}"
                : oldGet(route);
            Select();
            int starts = RequestHandler.StartCount;
            Assert(ArenaServerClient.RefreshAvailability()
                && ArenaServerClient.ServerAdmissionFailure.Contains("ISB Aishi"),
                "server-only blocker preserves server reachability and exposes its reason");
            Assert(!ArenaMenuSelection.CurrentEligibility.Allowed
                && ArenaMenuSelection.CurrentEligibility.Reason.Contains("ISB Aishi"),
                "server-only restriction participates in the same visible menu admission");
            Assert(!Start(Request(), out var task) && task.IsFaulted && RequestHandler.StartCount == starts,
                "server-only restriction cannot reach snapshot arming from a stale selection");
            ArenaMenuSelection.EndFlow();
            foreach (var side in new[] { ESideType.Pmc, ESideType.Savage })
                Assert(Start(Request(side), out task) && task == null,
                    "server-only restriction leaves ordinary " + side + " start unchanged");

            RequestHandler.Get = route => route == ArenaMode.Shared.Protocol.PingRoute
                ? "{\"version\":\"0.1.2\"}" : oldGet(route);
            Assert(ArenaServerClient.RefreshAvailability() && ArenaServerClient.ServerAdmissionFailure == null,
                "fresh server discovery clears a removed restriction; legacy ping remains readable");

            RequestHandler.Get = route => throw new InvalidOperationException("transport unavailable");
            Assert(!ArenaServerClient.RefreshAvailability() && ArenaServerClient.ServerAdmissionFailure == null,
                "failed transport does not advertise a stale server-only mod reason");
        }
        finally
        {
            RequestHandler.Get = oldGet;
            ArenaServerClient.RefreshAvailability();
        }
    }
}
