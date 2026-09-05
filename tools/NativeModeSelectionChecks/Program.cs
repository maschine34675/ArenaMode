using ArenaMode.Modes;
using ArenaMode.Session;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _checks;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    private static bool Near(float actual, float expected)
        => Math.Abs(actual - expected) < 0.001f;

    private static GameObject Screen() => new("Matchmaker Location Selection", typeof(RectTransform));

    private static TMP_Text NativeText(GameObject screen)
    {
        var source = new GameObject("Native button label",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        source.transform.SetParent(screen.transform, false);
        var text = source.GetComponent<TextMeshProUGUI>();
        text.font = new TMP_FontAsset();
        return text;
    }

    private static RectTransform Conditions(GameObject screen)
    {
        var conditions = new GameObject("Conditions Panel", typeof(RectTransform));
        conditions.transform.SetParent(screen.transform, false);
        var rect = (RectTransform)conditions.transform;
        rect.anchoredPosition = new Vector2(0f, 175f);
        rect.sizeDelta = new Vector2(571f, 89f);
        return rect;
    }

    private static Button ButtonFor(ArenaModeSelectionView view, string id)
        => view.Root.GetComponentsInChildren<Button>(true)
            .Single(button => button.gameObject.name == "Mode " + id);

    private static TextMeshProUGUI TextFor(ArenaModeSelectionView view, string name)
        => view.Root.GetComponentsInChildren<TextMeshProUGUI>(true)
            .Single(text => text.gameObject.name == name);

    private static void CatalogContract()
    {
        var expected = new[] { "endless", "rounds", "gungame", "tactical", "teamdeathmatch" };
        Assert(ArenaModeCatalog.All.Select(mode => mode.Id).SequenceEqual(expected),
            "the native picker exposes the five registry ids in the intended order");
        Assert(ArenaModeCatalog.All.All(mode => !string.IsNullOrWhiteSpace(mode.Label)
                                               && !string.IsNullOrWhiteSpace(mode.Description)),
            "every mode has visible copy");
        Assert(ArenaModeCatalog.All.Select(mode => mode.Label).Distinct().Count() == expected.Length,
            "mode labels are distinct");
        Assert(ArenaModeCatalog.All.Single(mode => mode.Id == ArenaModeCatalog.TeamDeathmatchId).Label == "TEAM DM",
            "standalone team mode has the compact TEAM DM label");

        foreach (var id in expected)
        {
            Assert(ArenaModeCatalog.NormalizeId("  " + id.ToUpperInvariant() + "  ") == id,
                $"mode id '{id}' normalizes case and surrounding whitespace");
            Assert(ArenaModeCatalog.CanSelect(id, tacticalOnly: false),
                $"solo Arena can select '{id}'");
        }

        foreach (var invalid in new[] { null, "", " ", "unknown" })
            Assert(ArenaModeCatalog.NormalizeId(invalid) == ArenaModeCatalog.EndlessId,
                "invalid stored modes fall back to Endless");

        Assert(ArenaModeCatalog.ResolveSelection("gungame", tacticalOnly: true)
               == ArenaModeCatalog.TacticalId,
            "a protected team flow resolves an old solo choice to Tactical");
        Assert(!ArenaModeCatalog.CanSelect("endless", tacticalOnly: true)
               && !ArenaModeCatalog.CanSelect("rounds", tacticalOnly: true)
               && !ArenaModeCatalog.CanSelect("gungame", tacticalOnly: true)
               && !ArenaModeCatalog.CanSelect("teamdeathmatch", tacticalOnly: true)
               && ArenaModeCatalog.CanSelect("TACTICAL", tacticalOnly: true),
            "a protected team flow permits only Tactical");
    }

    private static void SoloPickerInteraction()
    {
        var screen = Screen();
        var nativeText = NativeText(screen);
        var conditions = Conditions(screen);
        var choices = new List<string>();
        var view = new ArenaModeSelectionView();
        view.Show(screen.transform, nativeText, "rounds", tacticalOnly: false,
            mode => { choices.Add(mode); return true; }, conditions);

        Assert(view.Root != null && view.Root.activeSelf,
            "show creates one active picker root");
        Assert(screen.transform.Children.Last().gameObject == view.Root,
            "picker renders as the last screen child");
        var rect = (RectTransform)view.Root.transform;
        Assert(Near(rect.anchorMin.x, 0.5f) && Near(rect.anchorMin.y, 0f)
               && Near(rect.anchoredPosition.y, 175f)
               && Near(rect.sizeDelta.x, 820f) && Near(rect.sizeDelta.y, 86f),
            "picker is lifted clear of the live-tested native navigation buttons");
        Assert(Near(conditions.anchoredPosition.y, 268f),
            "Factory conditions move by the same offset while the picker owns the screen");
        Assert(Near(conditions.anchoredPosition.y -
                    (rect.anchoredPosition.y + rect.sizeDelta.y), 7f),
            "the lifted panels retain the inspected seven-pixel separation");
        Assert(view.Root.GetComponentsInChildren<Button>(true).Length == 5,
            "picker creates exactly five buttons");
        Assert(view.Root.GetComponentsInChildren<Button>(true)
                   .Select(button => button.gameObject
                       .GetComponentsInChildren<TextMeshProUGUI>(true).Single().text)
                   .SequenceEqual(ArenaModeCatalog.All.Select(mode => mode.Label)),
            "all five buttons render their catalog labels in order");
        var buttonRects = view.Root.GetComponentsInChildren<Button>(true)
            .Select(button => (RectTransform)button.transform).ToArray();
        for (var index = 0; index < buttonRects.Length; index++)
        {
            var button = buttonRects[index];
            Assert(button.sizeDelta.x >= 150f && button.sizeDelta.x <= 190f,
                "five readable labels keep at least 150 pixels each");
            Assert(button.anchoredPosition.x - button.sizeDelta.x / 2f >= -400f
                   && button.anchoredPosition.x + button.sizeDelta.x / 2f <= 400f,
                "each button stays inside the original 820-pixel panel with side padding");
            Assert(Near(button.anchoredPosition.y, 41f) && Near(button.sizeDelta.y, 32f),
                "the fifth mode does not move the button row down onto vanilla controls");
            if (index > 0)
                Assert(Near(button.anchoredPosition.x - button.sizeDelta.x / 2f
                    - (buttonRects[index - 1].anchoredPosition.x + buttonRects[index - 1].sizeDelta.x / 2f), 8f),
                    "neighboring mode buttons retain an eight-pixel gap");
        }
        Assert(view.Root.GetComponentsInChildren<TextMeshProUGUI>(true)
                   .All(text => ReferenceEquals(text.font, nativeText.font)),
            "picker inherits the native screen font");
        Assert(view.SelectedMode == "rounds" && choices.Count == 0,
            "stored valid mode is selected without writing config again");
        Assert(TextFor(view, "Description").text.Contains("Score-limited", StringComparison.Ordinal),
            "selected mode description is visible");

        ButtonFor(view, "gungame").onClick.Invoke();
        Assert(view.SelectedMode == "gungame" && choices.SequenceEqual(new[] { "gungame" }),
            "a native button click publishes the stable mode id exactly once");
        Assert(TextFor(view, "Description").text.Contains("weapon ladder", StringComparison.Ordinal),
            "click refreshes the selected description");
        ButtonFor(view, "gungame").onClick.Invoke();
        Assert(choices.Count == 1,
            "clicking the already selected mode does not rewrite config");

        ButtonFor(view, "teamdeathmatch").onClick.Invoke();
        Assert(view.SelectedMode == ArenaModeCatalog.TeamDeathmatchId
               && choices.SequenceEqual(new[] { "gungame", "teamdeathmatch" }),
            "standalone TEAM DM publishes its stable id through the normal selection callback");
        Assert(TextFor(view, "Description").text.Contains("USEC versus BEAR", StringComparison.Ordinal),
            "TEAM DM explains its faction assignment without a PFT requirement");

        var oldRoot = view.Root;
        view.Show(screen.transform, nativeText, "tactical", tacticalOnly: false,
            mode => { choices.Add(mode); return true; }, conditions);
        Assert(oldRoot == null && view.Root != null && view.SelectedMode == "tactical",
            "reopening disposes the old picker before creating the replacement");
        Assert(Near(conditions.anchoredPosition.y, 268f),
            "reopening restores then reapplies one conditions-panel offset without stacking");
        Assert(screen.transform.Children.Count(child => child.gameObject.name == "Arena Mode Selection") == 1,
            "reopening cannot duplicate the picker root");
        view.Close();
        Assert(oldRoot == null && view.Root == null
               && screen.transform.Children.All(child => child.gameObject.name != "Arena Mode Selection"),
            "close removes the picker without touching native siblings");
        Assert(Near(conditions.anchoredPosition.y, 175f),
            "close restores the exact native conditions-panel position");
    }

    private static void TacticalTeamRestriction()
    {
        var screen = Screen();
        var nativeText = NativeText(screen);
        var choices = new List<string>();
        var view = new ArenaModeSelectionView();
        view.Show(screen.transform, nativeText, "endless", tacticalOnly: true,
            mode => { choices.Add(mode); return true; });

        Assert(view.SelectedMode == "tactical" && choices.SequenceEqual(new[] { "tactical" }),
            "opening a team picker immediately commits Tactical over a stale solo mode");
        Assert(TextFor(view, "Title").text.Contains("PITFIRETEAM 5v5", StringComparison.Ordinal),
            "team restriction is visible in the picker title");
        Assert(TextFor(view, "Description").text.Contains("require Tactical", StringComparison.Ordinal),
            "team restriction is explained beside the selected description");
        Assert(!ButtonFor(view, "endless").interactable
               && !ButtonFor(view, "rounds").interactable
               && !ButtonFor(view, "gungame").interactable
               && !ButtonFor(view, "teamdeathmatch").interactable
               && ButtonFor(view, "tactical").interactable,
            "only Tactical is interactable for a protected team");
        ButtonFor(view, "gungame").onClick.Invoke();
        ButtonFor(view, "teamdeathmatch").onClick.Invoke();
        Assert(view.SelectedMode == "tactical" && choices.Count == 1,
            "the view-level guard also rejects a synthetic click on a disabled team mode");
    }

    private static void FailureAndFallbackHandling()
    {
        var screen = Screen();
        var view = new ArenaModeSelectionView();
        var choices = new List<string>();
        view.Show(screen.transform, null, "invalid", tacticalOnly: false,
            mode => { choices.Add(mode); return true; });
        Assert(view.SelectedMode == "endless" && choices.SequenceEqual(new[] { "endless" }),
            "invalid config is repaired to the catalog default");
        Assert(view.Root.GetComponentsInChildren<TextMeshProUGUI>(true)
                   .All(text => ReferenceEquals(text.font, TMP_Settings.defaultFontAsset)),
            "missing native font safely falls back to TMP's default");
        view.Close();

        view.Show(screen.transform, null, "rounds", tacticalOnly: false,
            mode => false);
        ButtonFor(view, "gungame").onClick.Invoke();
        Assert(view.SelectedMode == "rounds",
            "a rejected config write cannot make the picker display an inactive mode");
        view.Close();

        try
        {
            view.Show(screen.transform, null, "invalid", false, mode => false);
            Assert(false, "failed fallback repair must throw");
        }
        catch (InvalidOperationException)
        {
            Assert(view.Root == null,
                "failed fallback repair removes the partially created picker");
        }

        try
        {
            view.Show(null, null, "endless", false,
                mode => { choices.Add(mode); return true; });
            Assert(false, "missing screen must throw");
        }
        catch (InvalidOperationException) { Assert(view.Root == null, "missing screen leaves no picker"); }

        try
        {
            view.Show(screen.transform, null, "endless", false, null);
            Assert(false, "missing selection callback must throw");
        }
        catch (ArgumentNullException) { Assert(view.Root == null, "missing callback leaves no picker"); }
    }

    private static void PatchIntegrationContract()
    {
        var repository = FindRepositoryRoot();
        var locationPatch = File.ReadAllText(Path.Combine(repository,
            "ArenaMode.Client", "Session", "ArenaLocationSelectionPatch.cs"));
        var settings = File.ReadAllText(Path.Combine(repository,
            "ArenaMode.Client", "Config", "Settings.cs"));
        var localStart = File.ReadAllText(Path.Combine(repository,
            "ArenaMode.Client", "Session", "LocalRaidStartedPatch.cs"));
        var registry = File.ReadAllText(Path.Combine(repository,
            "ArenaMode.Client", "Modes", "ModeRegistry.cs"));
        var sidePatch = File.ReadAllText(Path.Combine(repository,
            "ArenaMode.Client", "Session", "ArenaSideSelectionPatch.cs"));

        var showPatch = Slice(locationPatch,
            "private sealed class ShowPatch", "private sealed class DisplayPatch");
        var postfix = showPatch.IndexOf("[PatchPostfix]", StringComparison.Ordinal);
        var arenaGate = showPatch.IndexOf("if (!IsArena(__instance))", postfix,
            StringComparison.Ordinal);
        var pickerShow = showPatch.IndexOf("state.Modes.Show(", postfix,
            StringComparison.Ordinal);
        Assert(postfix >= 0 && arenaGate > postfix && pickerShow > arenaGate,
            "the production picker is created only after the Arena map-screen gate");
        Assert(showPatch.Contains("Restore(__instance);", StringComparison.Ordinal)
               && locationPatch.Contains(
                   "private static void Prefix(MatchMakerSelectionLocationScreen __instance) => Restore(__instance);",
                   StringComparison.Ordinal)
               && locationPatch.Contains("state.Modes.Close();", StringComparison.Ordinal),
            "re-entry, native close and construction failure all tear the picker down");
        Assert(settings.Contains("GameMode.Value = normalized;", StringComparison.Ordinal)
               && settings.Contains("SettingsSync.Report();", StringComparison.Ordinal)
               && settings.Contains("ArenaModeCatalog.GunGameId", StringComparison.Ordinal),
            "the map choice updates, persists and reports the existing GameMode entry");
        var teamSize = Slice(settings, "TeamDeathmatchSize = config.Bind(", "RoundKillTarget = config.Bind(");
        Assert(settings.Contains("ArenaModeCatalog.TeamDeathmatchId", StringComparison.Ordinal)
               && teamSize.Contains("\"Team Deathmatch\"", StringComparison.Ordinal)
               && teamSize.Contains("\"PlayersPerTeam\"", StringComparison.Ordinal)
               && teamSize.Contains("new AcceptableValueRange<int>(2, 12)", StringComparison.Ordinal)
               && teamSize.Contains("            5,", StringComparison.Ordinal),
            "F12 exposes TEAM DM and a 2-12 players-per-team setting with five as default");
        Assert(localStart.Contains("var requestedMode = Settings.SelectedGameModeId;",
                   StringComparison.Ordinal)
               && localStart.Contains("ArenaSession.MarkArmed(settings.location, requestedMode);",
                   StringComparison.Ordinal)
               && registry.Contains("[ArenaModeCatalog.GunGameId]", StringComparison.Ordinal),
            "the selected catalog id reaches both mode construction and the armed raid latch");
        Assert(sidePatch.Contains("allowPendingModeSelection: true", StringComparison.Ordinal)
               && sidePatch.Contains("EnsureCompatibleMode(eligibility);", StringComparison.Ordinal)
               && sidePatch.Contains("ArenaModeCatalog.TacticalId", StringComparison.Ordinal),
            "the side screen can reach the picker with a team while immediately pinning that flow to Tactical");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length,
            StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new Exception($"Could not slice production source between '{startMarker}' and '{endMarker}'.");
        return source.Substring(start, end - start);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("ArenaMode repository root not found.");
    }

    public static void Main()
    {
        CatalogContract();
        SoloPickerInteraction();
        TacticalTeamRestriction();
        FailureAndFallbackHandling();
        PatchIntegrationContract();
        Console.WriteLine($"PASS: {_checks} native Arena mode-selection checks (production view and catalog linked).");
    }
}
