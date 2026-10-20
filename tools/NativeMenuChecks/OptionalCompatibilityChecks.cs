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
        }
        finally
        {
            ArenaMenuSelection.EndFlow();
            ArenaMenuSelection.RegisterCompatibilityAdmission(null);
            SettingsSync.OnSync = oldSync;
            Settings.GameMode.Value = oldMode;
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
