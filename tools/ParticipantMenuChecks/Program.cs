using ArenaMode.Config;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _checks;
    private static void Need(bool value, string reason)
    { _checks++; if (!value) throw new InvalidOperationException(reason); }
    private const string Day = ArenaMapCatalog.FactoryDayId, Full = ArenaAreaCatalog.FullMapId;
    private static ArenaAreaSelectionState Area(string map = Day, string mode = "deathmatch", string area = Full, bool pft = false)
        => ArenaAreaSelectionState.Create(map, mode, pft, area, 5);
    private static ArenaParticipantSelectionState Selection(ArenaAreaSelectionState area, bool pft = false)
        => ArenaParticipantSelectionState.Create(area, pft, ArenaParticipantSettings.Get(area.MapId, area.ModeId));

    private static void Persistence(string scratch)
    {
        foreach (bool autoSave in new[] { false, true })
        {
            string path = Path.Combine(scratch, "preferences-" + autoSave + ".cfg");
            var config = new ConfigFile(path, false) { SaveOnConfigSet = autoSave };
            ArenaParticipantSettings.Init(config);
            foreach (var pair in new[] { (ArenaMapCatalog.EquatorId, 4), (ArenaMapCatalog.Bay5Id, 8),
                (Day, 10), (ArenaMapCatalog.FactoryNightId, 10), (ArenaMapCatalog.LaboratoryId, 16), (ArenaMapCatalog.LabyrinthId, 12) })
            {
                Need(ArenaParticipantSettings.Get(pair.Item1, "deathmatch") == pair.Item2, "map-specific default");
                Need(ArenaParticipantSettings.Get(pair.Item1.ToUpperInvariant(), "gungame") == pair.Item2, "case-insensitive map and shared solo default");
            }
            Need(ArenaParticipantSettings.TrySelect(ArenaMapCatalog.EquatorId, "deathmatch", 24), "Equator recommendation is not a cap");
            Need(ArenaParticipantSettings.TrySelect(Day, "deathmatch", 7), "solo selection accepted");
            Need(ArenaParticipantSettings.TrySelect(Day, "teamdeathmatch", 14), "even TDM selection accepted");
            Need(config.SaveOnConfigSet == autoSave, "writer restores host save policy");
            Need(File.Exists(path) && File.ReadAllText(path).Contains("factory4_day_solo = 7"), "PERSISTENCE: accepted value reaches file without test save");
            ArenaParticipantSettings.Init(new ConfigFile(path, false) { SaveOnConfigSet = false });
            Need(ArenaParticipantSettings.Get(ArenaMapCatalog.EquatorId, "gungame") == 24, "reload preserves Equator override across solo modes");
            Need(ArenaParticipantSettings.Get(Day, "gungame") == 7 && ArenaParticipantSettings.Get(Day, "teamdeathmatch") == 14,
                "reload preserves separate mode families");
            Need(ArenaParticipantSettings.Get(ArenaMapCatalog.FactoryNightId, "deathmatch") == 10, "Factory variants have separate saved owners");
            Need(ArenaParticipantSettings.Get(ArenaMapCatalog.Bay5Id, "deathmatch") == 8, "another map keeps its default");

            byte[] before = File.ReadAllBytes(path);
            foreach (int invalid in new[] { int.MinValue, -1, 0, 1, 25, int.MaxValue })
                Need(!ArenaParticipantSettings.TrySelect(Day, "deathmatch", invalid), "invalid solo count refused");
            foreach (int invalid in new[] { 2, 3, 5, 13, 25 })
                Need(!ArenaParticipantSettings.TrySelect(Day, "teamdeathmatch", invalid), "invalid TDM count refused");
            foreach (var invalid in new[] { ("", "deathmatch"), ("woods", "deathmatch"), (Day, "tactical"), (Day, "unknown") })
                Need(!ArenaParticipantSettings.TrySelect(invalid.Item1, invalid.Item2, 10), "unknown map or fixed mode refused");
            ArenaSession.IsActive = true;
            Need(!ArenaParticipantSettings.TrySelect(Day, "deathmatch", 8), "ACTIVE-GUARD: active raid cannot change preferences");
            ArenaSession.IsActive = false; ArenaSession.ArmedForNextRaid = true;
            Need(!ArenaParticipantSettings.TrySelect(Day, "deathmatch", 8), "ARMED-GUARD: armed raid cannot change preferences");
            ArenaSession.ArmedForNextRaid = false;
            Need(before.SequenceEqual(File.ReadAllBytes(path)), "all rejected writes preserve the backing file");
        }

        string invalidPath = Path.Combine(scratch, "invalid.cfg");
        File.WriteAllText(invalidPath, "[Map participants]\nfactory4_day_solo = 99\nfactory4_day_team = 7\n");
        var invalidFile = new ConfigFile(invalidPath, false) { SaveOnConfigSet = false };
        ArenaParticipantSettings.Init(invalidFile);
        var bytes = File.ReadAllBytes(invalidPath);
        Need(ArenaParticipantSettings.Get(Day, "deathmatch") == 10 && ArenaParticipantSettings.Get(Day, "teamdeathmatch") == 10,
            "invalid persisted counts use the recommendation without clamping");
        Need(bytes.SequenceEqual(File.ReadAllBytes(invalidPath)), "read-time validation has no persistence side effect");
        Need(ArenaParticipantSettings.TrySelect(Day, "deathmatch", 2), "minimum solo accepted");
        Need(ArenaParticipantSettings.TrySelect(Day, "teamdeathmatch", 4), "minimum team accepted");

        // Real operating-system refusal: a directory occupies ConfigFilePath.
        string blockedPath = Path.Combine(scratch, "blocked.cfg");
        var blocked = new ConfigFile(blockedPath, false) { SaveOnConfigSet = false };
        ArenaParticipantSettings.Init(blocked);
        Directory.CreateDirectory(blockedPath);
        Need(!ArenaParticipantSettings.TrySelect(Day, "deathmatch", 15), "SAVE-FAILURE: failed disk write is never acknowledged");
        Need(ArenaParticipantSettings.Get(Day, "deathmatch") == 10 && !blocked.SaveOnConfigSet,
            "failed persistence rolls back memory and save policy");
    }

    private static void NativeView(string scratch)
    {
        ArenaParticipantSettings.Init(new ConfigFile(Path.Combine(scratch, "view.cfg"), false) { SaveOnConfigSet = false });
        var screen = new GameObject("native screen");
        var view = new ArenaAreaSelectionView();
        var area = Area();
        int calls = 0; bool accept = true;
        view.Show(screen.transform, null, _ => true, null, (rendered, total) =>
        {
            calls++;
            Need(rendered.MapId == area.MapId && rendered.ModeId == area.ModeId && rendered.AreaId == area.SelectedId,
                "real native click supplies rendered ownership");
            bool result = accept && ArenaParticipantSettings.TrySelect(rendered.MapId, rendered.ModeId, total);
            if (result) view.Refresh(area, false, Selection(area));
            return result;
        });
        void Refresh() => view.Refresh(area, false, Selection(area));
        Button[] Buttons() => view.Root.GetComponentsInChildren<Button>(true);
        Button Minus() => Buttons().Single(b => b.name == "Arena Participants minus");
        Button Plus() => Buttons().Single(b => b.name == "Arena Participants plus");
        string Text(string name) => view.Root.GetComponentsInChildren<TextMeshProUGUI>(true).Single(t => t.name == name).text;
        void Bounds()
        {
            var panel = (RectTransform)view.Root.transform;
            // 958: +42 over the pre-Zombie 916 for the fifth mode row, the same
            // shift AreaTeamMenuChecks applies. UpdateLayout places this panel at
            // ModePanelTop (168) + ModePanelHeight (Deathmatch 554->596) + 12, so
            // the ceiling tracks the panel. Measured maximum over this matrix:
            // 928 (Deathmatch top 776 + 152); the native READY area starts at
            // Y=984 (MUTATORS-MENU-REVIEW.md), so even the ceiling stays above it.
            Need(-panel.anchoredPosition.y + panel.sizeDelta.y <= 958, "native participants panel clears bottom controls");
            foreach (var b in Buttons())
            {
                var r = (RectTransform)b.transform;
                Need(r.anchoredPosition.x >= 0 && r.anchoredPosition.x + r.sizeDelta.x <= panel.sizeDelta.x
                    && -r.anchoredPosition.y >= 0 && -r.anchoredPosition.y + r.sizeDelta.y <= panel.sizeDelta.y,
                    "native button stays within participant panel");
            }
        }
        foreach (string map in ArenaMapCatalog.DefaultClientAllowlist.Split(','))
        foreach (string mode in new[] { "deathmatch", "gungame", "teamdeathmatch", "tactical" })
        {
            var candidate = Area(map, mode);
            for (int count = 2; count <= 24; count++)
            {
                var selection = ArenaParticipantSelectionState.Create(candidate, false, count);
                view.Refresh(candidate, false, selection);
                Bounds();
                bool expected = candidate.Available && mode != "tactical"
                    && (mode != "teamdeathmatch" || candidate.SelectedId == Full && count >= 4 && count % 2 == 0);
                Need(selection.Available == expected, "map/mode/count matrix matches offered formats");
                Need(Buttons().Count(b => b.name.StartsWith("Arena Participants")) == (expected ? 2 : 0),
                    "native controls reflect count and format admission");
            }
        }
        Refresh(); Bounds();
        Need(Text("Team size").Contains("you included") && Text("Mode restriction") == "Recommended: 10", "participant labels explain total and default");
        var stable = Plus(); Refresh(); Need(ReferenceEquals(stable, Plus()), "periodic same-context refresh preserves button listeners");
        Plus().onClick.Invoke(); Need(calls == 1 && Text("Participant value") == "11", "accepted click refreshes persisted total");
        accept = false; Plus().onClick.Invoke(); Need(calls == 2 && Text("Participant value") == "11", "refused click never fabricates displayed total"); accept = true;
        var stale = Plus().onClick.Snapshot();
        area = Area(ArenaMapCatalog.LaboratoryId); Refresh(); Bounds();
        foreach (var click in stale) click();
        Need(calls == 2 && Text("Participant value") == "16", "STALE-MAP: retained native delegates cannot target new map");
        stale = Plus().onClick.Snapshot(); area = Area(ArenaMapCatalog.LaboratoryId, "gungame"); Refresh();
        foreach (var click in stale) click();
        Need(calls == 2, "STALE-MODE: retained delegates cannot target a new solo mode");
        area = Area(Day, "teamdeathmatch"); Refresh(); Bounds();
        Plus().onClick.Invoke(); Need(Text("Participant value") == "12 (6v6)" && calls == 3, "team control increments two total participants");
        Need(Text("Mode restriction") == "Recommended: 10 (5v5)", "team recommendation explains symmetric format");
        ArenaParticipantSettings.TrySelect(Day, "teamdeathmatch", 24); Refresh();
        Need(!Plus().interactable && Minus().interactable, "maximum disables native plus");
        Plus().onClick.Invoke(); Need(calls == 3, "maximum also rejects forced delegate invocation");
        ArenaParticipantSettings.TrySelect(Day, "teamdeathmatch", 4); Refresh();
        Need(!Minus().interactable, "minimum disables native minus");
        Minus().onClick.Invoke(); Need(calls == 3, "minimum also rejects forced delegate invocation");
        stale = Plus().onClick.Snapshot();
        area = Area(ArenaMapCatalog.CustomsId, "teamdeathmatch", ArenaAreaCatalog.CustomsFactoryCenterTeamsId); Refresh(); Bounds();
        Need(Buttons().All(b => !b.name.StartsWith("Arena Participants")) && Buttons().Count(b => b.name.StartsWith("Arena Team Size")) == 4,
            "bounded teams retain their four native size controls");
        foreach (var click in stale) click(); Need(calls == 3, "bounded switch retires whole-map participant callbacks");
        area = Area(Day, "tactical"); Refresh(); Need(!Selection(area).Available && Buttons().Length == 1, "Tactical has no participant override");
        area = Area(Day, "tactical", Full, true); view.Refresh(area, true, Selection(area, true));
        Need(Text("Team size").StartsWith("5v5") && Buttons().Length == 1, "PFT retains fixed 5v5 label");
        area = Area(); Refresh(); stale = Plus().onClick.Snapshot(); view.Close();
        foreach (var click in stale) click(); Need(calls == 3 && view.Root == null, "closed panel rejects deferred clicks");
        view.Show(screen.transform, null, _ => true, null, (_, _) => { calls++; return true; }); Refresh();
        foreach (var click in stale) click(); Need(calls == 3, "same-view replacement rejects retired native buttons");
        view.Refresh(null, false); Need(Buttons().Length == 0, "no selected map removes all participant buttons");
        view.Close();
    }

    private static int Main()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "arena-participant-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try { Persistence(scratch); NativeView(scratch); Console.WriteLine($"PARTICIPANT_SOURCE_CHECKS_PASSED ({_checks} assertions)"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(scratch, true); }
    }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession { internal static bool IsActive, ArmedForNextRaid; }
}
