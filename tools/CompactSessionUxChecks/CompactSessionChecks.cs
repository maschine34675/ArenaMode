using System;
using System.IO;
using ArenaMode.Session;
using EFT;
using JsonType;

internal static class CompactSessionChecks
{
    private static int _checks;
    private static void Check(bool value, string why)
    {
        if (!value) throw new InvalidOperationException(why);
        _checks++;
    }

    private static RaidSettings Choose(object controller, Func<bool> allowed = null)
    {
        ArenaMenuSelection.EndFlow();
        ArenaMenuSelection.RegisterCompatibilityAdmission(() => null);
        var settings = new RaidSettings { Side = ESideType.Pmc,
            SelectedLocation = new LocationSettings.Location { Id = "Woods" }, SelectedDateTime = EDateTime.PAST };
        ArenaMenuSelection.Begin(controller, settings);
        ArenaMenuSelection.ConfigureEligibility(() => new ArenaMenuSelection.Eligibility(allowed?.Invoke() ?? true, false, "changed roster"), null);
        ArenaMenuSelection.Select();
        settings.SelectedLocation = new LocationSettings.Location { Id = "factory4_day" };
        settings.SelectedDateTime = EDateTime.CURR;
        settings.transitionType = ELocationTransition.Common;
        return settings;
    }

    public static int Main()
    {
        try
        {
            var owner = new object();
            var settings = Choose(owner);
            Check(ArenaMenuSelection.IsSelectedFor(settings), "positive control: original Arena selection is owned");
            ArenaMenuSelection.SuspendForMenu();
            Check(!ArenaMenuSelection.IsSelectedFor(settings), "trader detour revokes selection immediately");
            Check(!ArenaMenuSelection.MatchesRequest(new LocalRaidSettings { playerSide = ESideType.Pmc,
                location = "factory4_day", transitionType = ELocationTransition.Common }), "suspended choice cannot authorize a request");
            ArenaMenuSelection.SuspendForMenu();
            Check(settings.SelectedLocation.Id == "factory4_day", "nested utility pages retain the chosen Arena map");
            Check(ArenaMenuSelection.ResumeAfterMenu(settings), "return to the same settings resumes after fresh admission");
            Check(ArenaMenuSelection.IsSelectedFor(settings), "resumed picker owns the actual native settings");
            ArenaMenuSelection.EndFlow();
            Check(settings.SelectedLocation.Id == "Woods" && settings.SelectedDateTime == EDateTime.PAST,
                "ending the resumed flow restores the original ordinary map/time");

            settings = Choose(owner);
            ArenaMenuSelection.SuspendForMenu();
            var other = new RaidSettings { Side = ESideType.Pmc, SelectedLocation = new LocationSettings.Location { Id = "factory4_day" } };
            Check(!ArenaMenuSelection.ResumeAfterMenu(other), "equal map data in different settings cannot inherit the choice");
            Check(!ArenaMenuSelection.ResumeAfterMenu(settings), "mismatched return consumes the old suspended context");
            Check(settings.SelectedLocation.Id == "Woods", "rejected return restores original settings");

            bool allowed = true;
            settings = Choose(owner, () => allowed);
            ArenaMenuSelection.SuspendForMenu();
            allowed = false;
            Check(!ArenaMenuSelection.ResumeAfterMenu(settings), "fresh roster rejection blocks return");
            allowed = true;
            Check(!ArenaMenuSelection.ResumeAfterMenu(settings), "later admission cannot revive a rejected context");

            settings = Choose(owner);
            ArenaMenuSelection.SuspendForMenu();
            ArenaMenuSelection.RegisterCompatibilityAdmission(() => "policy changed");
            Check(!ArenaMenuSelection.ResumeAfterMenu(settings), "current compatibility rejection wins over old selection");

            settings = Choose(owner);
            ArenaMenuSelection.SuspendForMenu();
            settings.Side = ESideType.Savage;
            Check(!ArenaMenuSelection.ResumeAfterMenu(settings), "Scav settings never resume Arena");
            settings = Choose(owner);
            ArenaMenuSelection.SuspendForMenu();
            ArenaMenuSelection.Begin(new object(), settings);
            Check(!ArenaMenuSelection.IsSelected && !ArenaMenuSelection.ResumeAfterMenu(settings), "new side controller starts a clean flow");

            settings = Choose(owner);
            ArenaMenuSelection.SuspendForMenu();
            ArenaMenuSelection.Begin(owner, settings);
            Check(ArenaMenuSelection.IsSelectedFor(settings), "return through the same side controller resumes safely");
            ArenaMenuSelection.SuspendForMenu();
            ArenaMenuSelection.Clear();
            Check(!ArenaMenuSelection.ResumeAfterMenu(settings) && settings.SelectedLocation.Id == "Woods",
                "explicit deselection cancels suspension and restores ordinary fields");

            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("repository missing");
            string Read(string name) => File.ReadAllText(Path.Combine(root.FullName, "ArenaMode.Client", "Session", name));
            string side = Read("ArenaSideSelectionPatch.cs"), map = Read("ArenaLocationSelectionPatch.cs"), banner = Read("ArenaSessionBanner.cs");
            Check(side.Contains("ArenaMenuSelection.SuspendForMenu();") && side.Contains("ArenaSession.ClearArmed(\"left raid setup\");"),
                "utility-page suspension retains explicit arm revocation");
            Check(map.Contains("ArenaMenuSelection.ResumeAfterMenu(raidSettings);") && map.Contains("state.RestoreTimeLabels();"),
                "native map display resumes and ordinary time labels are restored");
            Check(banner.Contains("!RaidWorldActive()") && banner.Contains("|| RaidWorldActive()")
                && banner.Contains("Singleton<GameWorld>.Instance is not HideoutGameWorld")
                && banner.Contains("|| ModalOpen()") && banner.Contains("CurrentScreenController?.ScreenType"),
                "session return blocks raid worlds, permits a retained hideout world and checks live screen and modal guards");
            Check(banner.Contains("HasActiveWindow(context)") && banner.Contains("HasActiveWindow(MenuUI.Instance)")
                && banner.Contains("HasActiveWindow(CommonUI.Instance)") && banner.Contains("typeof(Window<>)")
                && banner.Contains("List<BaseUiWindow> PanelWindows") && banner.Contains("GetComponentsInChildren(false, PanelWindows)")
                && banner.Contains("_filterWindow?.gameObject.activeInHierarchy == true"),
                "unregistered purchase, quest, raid-settings and build dialogs plus flea filters participate in the modal guard");
            Check(banner.Contains("preloader.Console?.IsConsoleVisible == true")
                && !banner.Contains("preloader.Console?.gameObject.activeInHierarchy"),
                "the persistent console object only blocks the strip while the console is actually visible");
            Check(banner.Contains("if (!Singleton<ClientApplication<IEftSession>>.Instantiated) return null;")
                && banner.Contains(".Instance.GetClientBackEndSession()?.Profile?.Id;")
                && banner.Contains("string profile = ProfileId();") && !banner.Contains("Controller(menu)?.Profile?.Id"),
                "session return resolves the live backend profile again on click even while the main menu is hidden");
            string notice = Read("ArenaAvailabilityNotice.cs");
            Check(notice.Contains("_details != null && unavailable && compactRestartConflict")
                && side.Contains("compactRestartConflict: ArenaWhitelistAdmission.CanOfferSessionStart(reason)")
                && notice.Contains("MOD CONFLICT · Restart with filtered mods\\nHover for affected mods and details.")
                && notice.Contains("_details.SetMessageText(tooltipText, rawText: true);")
                && side.Contains("tooltipNotes: ArenaWhitelistAdmission.WarningLine")
                && notice.Contains("fullText + \"\\n\\n\" + tooltipNotes"),
                "a restartable mod conflict uses two concise lines and keeps the complete reason in its tooltip");
            int failureStart = side.IndexOf("private static void ShowSwitchFailure(", StringComparison.Ordinal);
            int failureEnd = side.IndexOf("private static void EnsureCompatibleMode(", failureStart, StringComparison.Ordinal);
            string failure = side.Substring(failureStart, failureEnd - failureStart);
            Check(notice.Contains("bool compactRestartConflict = false)")
                && !notice.Contains("action?.Label == ArenaWhitelistAdmission.StartActionLabel")
                && failure.Contains("ArenaAvailabilityNotice.Show(screen, error, retry);")
                && !failure.Contains("compactRestartConflict:"),
                "helper, timeout and file failures retain their real error instead of a mod-conflict summary");
            Console.WriteLine($"COMPACT_SESSION_UX_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { ArenaMenuSelection.EndFlow(); ArenaMenuSelection.RegisterCompatibilityAdmission(() => null); }
    }
}
