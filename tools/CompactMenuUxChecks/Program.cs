using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using UnityEngine;

int checks = 0;
void Need(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
    checks++;
}

// Exercise the production category/visibility policy across every mode, not a
// separate test copy of the ordering. Both variants must own exactly one slot.
var ids = Enum.GetValues<ArenaMutator>();
var selection = new ArenaMutatorSelection();
foreach (var mode in ArenaModeCatalog.All)
foreach (bool team in new[] { false, true })
{
    selection.Synchronize(mode.Id, team);
    var visible = ids.Where(selection.Visible).ToArray();
    Need(visible.Length == 23, "Every mode presents 23 controls without hidden duplicate bot controls.");
    Need(visible.Count(id => id is ArenaMutator.FastBots or ArenaMutator.ZombieCrowd) == 1,
        "Normal bots and horde pressure share exactly one combat slot.");
    Need(selection.Visible(ArenaMutator.ZombieCrowd) == (mode.Id == ArenaModeCatalog.ZombieId),
        "Horde pressure is offered only by Zombies.");
    var counts = Enumerable.Range(0, 4).Select(g => visible.Count(id => ArenaMutatorSelection.Group(id) == g)).ToArray();
    Need(counts.SequenceEqual(new[] { 9, 3, 5, 6 }), "Movement, equipment, combat and feedback preserve their complete contents.");
    foreach (var (w, h) in new[] { (1920f,1080f), (1600f,900f), (1280f,720f), (2560f,1440f), (1440f,1080f) })
    {
        var screen = new RectTransform { rect = new Rect(w, h) };
        var panel = new RectTransform();
        ArenaMapPanelLayout.Place(panel, screen, ArenaMapPanelLayout.MutatorsTop, ArenaMapPanelLayout.MutatorsHeight);
        float scale = ArenaMapPanelLayout.Scale(screen), width = ArenaMapPanelLayout.Width(screen);
        float cell = ArenaMapPanelLayout.MutatorCellWidth(width);
        Need(panel.sizeDelta.x == width && panel.sizeDelta.y == ArenaMapPanelLayout.MutatorsHeight,
            "Placed panel consumes the production width and height.");
        Need(w + panel.anchoredPosition.x - width * scale >= 0,
            "The whole panel remains inside narrow screens.");
        Need(-panel.anchoredPosition.y + panel.sizeDelta.y * scale <= 980f * scale,
            "Grouping preserves the original bottom edge above native Ready.");
        var rectangles = new List<(int column, float top, float bottom)>();
        for (int group = 0; group < 4; group++)
        {
            int firstCount = counts[group < 2 ? 0 : 2];
            float left = ArenaMapPanelLayout.MutatorGroupLeft(group, width);
            float head = ArenaMapPanelLayout.MutatorGroupTop(group, firstCount);
            Need(left >= 20f && left + cell <= width - 20f, "Group does not leave its column.");
            rectangles.Add((group / 2, head, head + ArenaMapPanelLayout.MutatorHeadingHeight));
            for (int i = 0; i < counts[group]; i++)
            {
                float top = ArenaMapPanelLayout.MutatorRowTop(group, firstCount, i);
                float bottom = top + ArenaMapPanelLayout.MutatorRowPitch;
                Need(bottom <= ArenaMapPanelLayout.MutatorsHeight - 8f, "Last complete description retains a bottom margin.");
                Need(ArenaMapPanelLayout.MutatorButtonHeight >= 22f
                    && ArenaMapPanelLayout.MutatorRowPitch - ArenaMapPanelLayout.MutatorButtonHeight >= 18f,
                    "Grouping never squeezes buttons or readable description lines.");
                rectangles.Add((group / 2, top, bottom));
            }
        }
        for (int i = 0; i < rectangles.Count; i++)
        for (int j = i + 1; j < rectangles.Count; j++)
            if (rectangles[i].column == rectangles[j].column)
                Need(rectangles[i].bottom <= rectangles[j].top || rectangles[j].bottom <= rectangles[i].top,
                    "No category heading or control overlaps another in its column.");
        Need(ArenaMapPanelLayout.InfoTop + ArenaMapPanelLayout.InfoHeight + 20f == ArenaMapPanelLayout.MutatorsTop,
            "The shortened preview keeps its gap above the mutators.");
    }
}

// Presentation reads must not change saved values. Exercise the real guard
// with both positive and negative toggle controls, including a disabled child.
selection.Synchronize(ArenaModeCatalog.DeathmatchId, false);
bool priorJump = Settings.DoubleJumpEnabled.Value;
int saves = Settings.File.Saves;
foreach (var id in ids) { selection.Enabled(id); selection.UnavailableReason(id); selection.GroupTitle(ArenaMutatorSelection.Group(id)); }
Need(Settings.DoubleJumpEnabled.Value == priorJump && Settings.File.Saves == saves, "Reading the UI never saves preferences.");
Need(selection.TryToggle(ArenaMutator.DoubleJump) && Settings.DoubleJumpEnabled.Value != priorJump,
    "An available control still writes exactly its existing setting.");
Need(Settings.File.Saves == saves + 1, "A real accepted change saves once.");
ArenaSession.ArmedForNextRaid = true;
Need(!selection.TryToggle(ArenaMutator.DoubleJump), "Armed raid blocks control writes.");
ArenaSession.ArmedForNextRaid = false;
ArenaSession.IsActive = true;
Need(!selection.TryToggle(ArenaMutator.DoubleJump), "Active raid blocks control writes.");
ArenaSession.IsActive = false;
Settings.MovementEnabled.Value = false;
Need(selection.UnavailableReason(ArenaMutator.DoubleJump) != null && !selection.TryToggle(ArenaMutator.DoubleJump),
    "Disabled movement parent does not change its saved child preference.");
Need(selection.GroupTitle(0) == "MOVEMENT", "Movement heading avoids duplicating its parent toggle.");
Settings.MovementEnabled.Value = true;
Need(selection.GroupTitle(0) == "MOVEMENT", "Enabled movement retains the stable category name.");
Need(!selection.TryToggle(ArenaMutator.ZombieCrowd), "Hidden horde control cannot be toggled in another mode.");
Need(!selection.TryToggle(ArenaMutator.FriendlyFire), "Solo modes do not acquire a team-only setting.");
Need(ArenaMutatorSelection.TeamModesOnly.Contains("PitFireTeam") && !ArenaMutatorSelection.TeamModesOnly.Contains("PFT"),
    "The visible team restriction names the optional mod without unexplained initials.");

var customs = ArenaAreaSelectionState.Create(ArenaMapCatalog.CustomsId, ArenaModeCatalog.TeamDeathmatchId,
    false, ArenaAreaCatalog.CustomsFactoryCenterTeamsId, 5, 3);
Need(customs.Options.Select(x => x.Id).SequenceEqual(new[] { ArenaAreaCatalog.CustomsFactoryCenterTeamsId,
    ArenaAreaCatalog.CustomsFactoryEastPolygonId, ArenaAreaCatalog.CustomsYardId }),
    "Renaming industrial areas preserves their exact ordered IDs and never offers whole Customs.");
Need(customs.SelectedId == ArenaAreaCatalog.CustomsFactoryCenterTeamsId && customs.SelectedTeamSize == 3,
    "Saved area and team-size selections retain their original identity.");
Need(customs.Options.All(x => !x.Label.Contains("Factory")), "Customs display names cannot be confused with the separate Factory map.");
var whole = ArenaAreaSelectionState.Create(ArenaMapCatalog.FactoryDayId, ArenaModeCatalog.GunGameId,
    false, ArenaAreaCatalog.FullMapId, 5);
Need(whole.Options.Count == 1 && whole.SelectedId == ArenaAreaCatalog.FullMapId && whole.Options[0].Label == "Whole map",
    "A full-map choice remains explicit and unchanged.");
var empty = ArenaAreaSelectionState.Create(null, ArenaModeCatalog.GunGameId, false, null, 5);
Need(!empty.Available && empty.Options.Count == 0 && empty.Summary == "Select a map.",
    "No map does not imply that every future map needs an area choice.");

Console.WriteLine($"COMPACT_MENU_UX_CHECKS_PASSED checks={checks}");
