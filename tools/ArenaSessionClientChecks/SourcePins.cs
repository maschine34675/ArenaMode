using System.Text.RegularExpressions;

// The Unity- and Harmony-bound part of the client cannot run offline, so its wiring is pinned by
// source text, comments removed and whitespace collapsed (the repo's established proof for such
// code). Verify.ps1 mutates each source through the ARENA_SESSION_*_SOURCE variables and must see
// the named assertion fail. What these pins do not prove: how the screen looks and that the clicks
// land. That is for a raid on the GPU host.
internal static partial class Program
{
    private static string Code(string variable, params string[] relative)
    {
        string path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(path)) path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(relative).ToArray());
        return Regex.Replace(Regex.Replace(File.ReadAllText(path), @"//[^\r\n]*|/\*.*?\*/", " ", RegexOptions.Singleline), @"\s+", " ").Trim();
    }

    private static bool Before(string code, string first, string second)
    {
        int a = code.IndexOf(first, StringComparison.Ordinal);
        int b = code.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b > a;
    }

    private static string Slice(string code, string from, string to)
    {
        int start = code.IndexOf(from, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        int end = code.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        return end < 0 ? code.Substring(start) : code.Substring(start, end - start);
    }

    private static void CheckSourcePins()
    {
        string side = Code("ARENA_SESSION_SIDE_SOURCE", "ArenaMode.Client", "Session", "ArenaSideSelectionPatch.cs");
        string notice = Code("ARENA_SESSION_NOTICE_SOURCE", "ArenaMode.Client", "Session", "ArenaAvailabilityNotice.cs");
        string optional = Code("ARENA_SESSION_OPTIONAL_SOURCE", "ArenaMode.Client", "Compat", "ArenaOptionalMods.cs");
        string switchCode = Code("ARENA_SESSION_SWITCH_SOURCE", "ArenaMode.Client", "Session", "ArenaSessionSwitch.cs");

        // ---- admission chain
        Require(Before(optional, "var whitelistFailure = ArenaWhitelistAdmission.GetRejectionReason(); if (whitelistFailure != null) return whitelistFailure;",
                "ArenaServerClient.ServerAdmissionFailure != null")
                && Before(optional, "ArenaServerClient.ServerAdmissionFailure != null", "ArenaContinuousHealingCompat.GetRejectionReason()"),
            "the whitelist is the first provider of the admission chain, ahead of the server's reported failure and the adapters");

        // ---- side screen
        string enable = Slice(side, "public static void Enable()", "private sealed class ArenaSideSelectionShowPatch");
        Require(enable.Contains("if (ArenaSessionMode.IsArenaSession) new ArenaSideSelectionSessionLockPatch().Enable();", StringComparison.Ordinal)
                && Regex.Matches(side, @"new ArenaSideSelectionSessionLockPatch\(\)").Count == 1,
            "the PMC/Scav lock patch is installed in an Arena session only");

        string show = Slice(side, "private sealed class ArenaSideSelectionShowPatch", "private sealed class ArenaSideSelectionClosePatch");
        Require(Before(show, "ArenaWhitelistAdmission.PrepareForScreen();", "var eligibility = EvaluateEligibility(")
                && Before(show, "eligibility = PrepareArenaSession(__instance, eligibility);", "ShowNotice(__instance, eligibility);")
                && Before(show, "ShowNotice(__instance, eligibility);", "if (ArenaMenuSelection.IsSelected) {")
                && Before(show, "ArenaSideSelectionLayout.Apply(", "eligibility = PrepareArenaSession("),
            "the side screen asks for the ping, prepares the Arena session and shows the notice before it restores a selection");
        Require(show.Contains("ArenaAvailabilityNotice.Show(__instance, \"The Arena menu could not initialize.", StringComparison.Ordinal),
            "the side screen's own failure notice is untouched");

        string lockPatch = Slice(side, "private sealed class ArenaSideSelectionSessionLockPatch", "private sealed class ArenaSideSelectionLeaveSetupPatch");
        Require(lockPatch.Contains("nameof(MatchMakerSideSelectionScreen.SetScavAvailable)", StringComparison.Ordinal)
                && lockPatch.Contains("[PatchPostfix]", StringComparison.Ordinal)
                && lockPatch.Contains("LockCharacterChoices(__instance);", StringComparison.Ordinal)
                && lockPatch.Contains("PitFireTeamCompat.IsSquadManagementScreen", StringComparison.Ordinal),
            "the native Scav timer's re-enabling is undone after every run, never on PFT's squad screen");

        string locks = Slice(side, "private static void LockCharacterChoices(", "private static void SetNextAvailable(");
        foreach (string statement in new[]
        {
            "screen._pmcButton.SpawnedObject.interactable = false;",
            "screen._pmcBigButton.interactable = false;",
            "screen._savagesButton.SpawnedObject.interactable = false;",
            "screen._savagesBigButton.interactable = false;",
            "screen._savagesBigButton.gameObject.SetActive(false);",
            "screen._savageBlocker.interactable = false;",
        })
            Require(locks.Contains(statement, StringComparison.Ordinal), "PMC and Scav are not selectable in an Arena session: " + statement);

        string prepare = Slice(side, "private static ArenaMenuSelection.Eligibility PrepareArenaSession(", "private static void LockCharacterChoices(");
        Require(prepare.Contains("if (!ArenaSessionMode.IsArenaSession) return eligibility;", StringComparison.Ordinal)
                && Before(prepare, "LockCharacterChoices(screen);", "SelectArena(screen, eligibility);")
                && prepare.Contains("SetNextAvailable(screen, false);", StringComparison.Ordinal),
            "an Arena session locks the other sides, selects ARENA on its own and keeps Next closed without it");
        Require(Before(prepare, "if (!ArenaServerClient.IsServerModKnownAvailable) { SetNextAvailable(screen, false); return eligibility; }", "SelectArena(screen, eligibility);"),
            "the automatic selection waits for a server that has answered the bounded ping");

        string select = Slice(side, "private static void SelectArena(", "private static ArenaMenuSelection.Eligibility PrepareArenaSession(");
        Require(Before(select, "ArenaServerClient.RefreshAvailability()", "ArenaWhitelistAdmission.GetRejectionReason()")
                && Before(select, "ArenaWhitelistAdmission.GetRejectionReason()", "ArenaMenuSelection.Select();"),
            "a click re-checks the whitelist with the fresh ping before it selects Arena");

        string prefix = Slice(side, "private sealed class ArenaSideSelectionSelectPatch", "private static void SetDescription");
        Require(prefix.Contains("if (!ArenaSessionMode.IsArenaSession) __instance._pmcButton.IsToggled = true;", StringComparison.Ordinal),
            "a failed Arena click falls back to PMC outside an Arena session only");

        string unavailable = Slice(side, "private static void ShowUnavailable(", "private static string WithWarnings(");
        Require(unavailable.Contains("if (ArenaSessionMode.IsArenaSession) ArenaAvailabilityNotice.Show(screen, text, ReturnAction(screen));", StringComparison.Ordinal)
                && unavailable.Contains("else if (ArenaWhitelistAdmission.CanOfferSessionStart(reason)) ArenaAvailabilityNotice.Show(screen, text, StartAction(screen));", StringComparison.Ordinal),
            "START ARENA SESSION appears only on the whitelist refusal of a normal session, RETURN only in an Arena session");

        string start = Slice(side, "private static ArenaNoticeAction StartAction(", "private static ArenaNoticeAction ReturnAction(");
        string back = Slice(side, "private static ArenaNoticeAction ReturnAction(", "private static void SwitchSession(");
        Require(start.Contains("ArenaWhitelistAdmission.StartActionLabel", StringComparison.Ordinal)
                && start.Contains("ArenaSessionContract.DirectionEnter", StringComparison.Ordinal)
                && back.Contains("ArenaWhitelistAdmission.ReturnActionLabel", StringComparison.Ordinal)
                && back.Contains("ArenaSessionContract.DirectionLeave", StringComparison.Ordinal),
            "the actions request the matching direction");

        string notices = Slice(side, "private static void ShowNotice(", "private static void ShowSelectedNotice(");
        Require(notices.Contains("ArenaAvailabilityNotice.ShowInfo(screen, WithWarnings(ArenaWhitelistAdmission.SessionNoticeText, warnings), back);", StringComparison.Ordinal)
                && notices.Contains("else if (warnings != null && ArenaMenuSelection.IsSelected) ArenaAvailabilityNotice.ShowInfo(screen, warnings);", StringComparison.Ordinal),
            "the session notice always shows in a session; whitelist warnings show only once ARENA is selected");

        // The policy-override note travels with the whitelist notes: a refusal shows them as its second line, and
        // an Arena click (ARENA selectable, nothing refused) shows them as an information notice.
        Require(unavailable.Contains("string text = WithWarnings(reason, ArenaWhitelistAdmission.WarningLine);", StringComparison.Ordinal),
            "a refusal carries the whitelist notes, the policy override included, as its second line");
        string selectedNotice = Slice(side, "private static void ShowSelectedNotice(", "private static void ShowUnavailable(");
        Require(selectedNotice.Contains("string warnings = ArenaWhitelistAdmission.WarningLine;", StringComparison.Ordinal)
                && selectedNotice.Contains("else if (warnings != null) ArenaAvailabilityNotice.ShowInfo(screen, warnings);", StringComparison.Ordinal),
            "an Arena click shows the whitelist notes, the policy override included, as an information notice");

        string switching = Slice(side, "private static void SwitchSession(", "private static void EnsureCompatibleMode(");
        Require(switching.Contains("Controller(screen)?.Session?.Profile?.Id", StringComparison.Ordinal)
                && switching.Contains("ArenaSessionSwitch.Request(direction, profileId, out var error, failure => ShowSwitchFailure(screen, direction, failure))", StringComparison.Ordinal)
                && switching.Contains("ShowSwitchFailure(screen, direction, error);", StringComparison.Ordinal)
                && switching.Contains("ArenaAvailabilityNotice.Show(screen, error, retry);", StringComparison.Ordinal),
            "the confirmed action passes the logged-in profile, shows a failure that arrives later as well, and keeps the action after a failure");
        Require(switching.Contains("if (ArenaSessionSwitch.HelperPending) ArenaAvailabilityNotice.ShowInfo(screen, ArenaSessionSwitch.WaitingText);", StringComparison.Ordinal),
            "while the helper has not answered the screen says so and offers no second click");

        // ---- notice
        Require(notice.Contains("_text.richText = false;", StringComparison.Ordinal)
                && notice.Contains("_buttonLabel.richText = false;", StringComparison.Ordinal),
            "foreign text and the action label stay literal");
        Require(Before(notice, "bool confirmed = _gate.Click(now);", "if (!confirmed) return;")
                && Before(notice, "if (!confirmed) return;", "action.Callback?.Invoke();"),
            "the action needs two clicks: nothing runs before the confirmation");
        Require(notice.Contains("internal const string DefaultConfirmLabel = \"CLICK AGAIN TO RESTART THE GAME\";", StringComparison.Ordinal),
            "the confirmation label is exactly CLICK AGAIN TO RESTART THE GAME");
        Require(!notice.Contains("Redeploy", StringComparison.Ordinal) && !notice.Contains("ArenaSettings", StringComparison.Ordinal)
                && !notice.Contains("PitFireTeam", StringComparison.Ordinal),
            "the notice stays independent of Core, settings and adapters");

        // ---- switch
        string request = Slice(switchCode, "private static bool RequestUnsafe(", "private static void StartWaiting(");
        Require(Before(request, "DeleteStaleAcknowledgement(requestPath);", "WriteRequestFile(requestPath, requestJson);")
                && Before(request, "WriteRequestFile(requestPath, requestJson);", "LaunchHelper(helper, direction, requestPath, out int helperPid, out error)")
                && Before(request, "LaunchHelper(helper, direction, requestPath, out int helperPid, out error)", "StartWaiting(pending);")
                && !request.Contains("QuitGame", StringComparison.Ordinal),
            "a stale acknowledgement goes first, the request file exists before the helper starts and the request itself never quits the game");
        string step = Slice(switchCode, "private static bool Step(", "private static void Fail(");
        string accepted = Slice(step, "case ArenaHandshakeStatus.Accepted:", "case ArenaHandshakeStatus.HelperExited:");
        Require(Regex.Matches(switchCode, @"QuitGame\(\)").Count == 1 && Before(accepted, "_requested = true;", "QuitGame();"),
            "the game quits in exactly one place: when the helper has accepted the request");
        Require(!switchCode.Contains("Thread.Sleep", StringComparison.Ordinal) && !switchCode.Contains(".Wait(", StringComparison.Ordinal)
                && !switchCode.Contains("WaitOne(", StringComparison.Ordinal) && !switchCode.Contains("SpinWait", StringComparison.Ordinal)
                && switchCode.Contains("plugin.StartCoroutine(WaitForHelper(pending));", StringComparison.Ordinal)
                && switchCode.Contains("yield return null;", StringComparison.Ordinal),
            "the wait for the helper is a coroutine on the plugin and never blocks the main thread");
        Require(switchCode.Contains("Clock = () => Time.realtimeSinceStartup;", StringComparison.Ordinal)
                && !switchCode.Contains("Time.time", StringComparison.Ordinal) && !switchCode.Contains("deltaTime", StringComparison.Ordinal),
            "the wait runs on real time, not on game time that a pause or a slow frame would stretch");
        Require(Before(request, "string requestJson = BuildRequestJson(request);", "HelperRequest.TryParse(requestJson, direction, out _, out string refusal)")
                && Before(request, "HelperRequest.TryParse(requestJson, direction, out _, out string refusal)", "WriteRequestFile(requestPath, requestJson);"),
            "the helper's own parser checks the request text before the file is written, the helper started or the game quit");
        Require(switchCode.Contains("if (process == null)", StringComparison.Ordinal) && switchCode.Contains("if (process.HasExited)", StringComparison.Ordinal)
                && switchCode.Contains("UseShellExecute = false", StringComparison.Ordinal) && switchCode.Contains("CreateNoWindow = true", StringComparison.Ordinal),
            "the real launcher verifies the helper process and shows no window");
        Require(Regex.Matches(switchCode, @"Application\.Quit\(\)").Count == 0 && switchCode.Contains("QuitGame = Application.Quit;", StringComparison.Ordinal),
            "the game quits through the one Application.Quit seam");
    }
}
