using System;
using System.Linq;
using ArenaMode.Shared;
using ArenaMode.Session;
using EFT.UI.Matchmaker;
using UnityEngine;
using UnityObject = UnityEngine.Object;

// Recorded level48: _locationButtonsContainer and _locationPathsContainer
// are both Transform15523, GO269 "Paths Container". The LocationPath template
// GO276 is outside that container. These checks link the production helper;
// they do not exercise Harmony, map filtering, rendering, or actual navigation.
internal static class Program
{
    private static int _checks;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    private static GameObject Child(GameObject parent, string name, bool active = true)
    {
        var child = new GameObject(name);
        child.transform.parent = parent.transform;
        if (!active) child.SetActive(false);
        return child;
    }

    private static GameObject Path(GameObject parent, string name, bool active = true)
    {
        var path = Child(parent, name, active);
        path.AddComponent<LocationPath>();
        return path;
    }

    private sealed class Fixture
    {
        public readonly GameObject Screen = new("MatchMaker Selection Location Screen");
        public readonly GameObject Container;
        public readonly GameObject[] Maps;
        public readonly GameObject ActivePath;
        public readonly GameObject InactivePath;
        public readonly GameObject InactiveBranch;
        public readonly GameObject PathUnderInactiveBranch;
        public readonly GameObject ExternalTemplate;
        public readonly GameObject Marathon;
        public readonly ArenaLocationSelectionView View = new();

        public Fixture(bool containerActive = true, bool marathonActive = true)
        {
            Container = Child(Screen, "Paths Container", containerActive);
            Maps = new[] { "Factory", "Labyrinth", "Labs" }.Select(name => Child(Container, name)).ToArray();
            ActivePath = Path(Container, "Generated path A");
            InactivePath = Path(Container, "Generated path B", false);
            InactiveBranch = Child(Container, "Inactive branch", false);
            PathUnderInactiveBranch = Path(InactiveBranch, "Nested path");
            ExternalTemplate = Path(Screen, "Path template", false);
            Marathon = Child(Screen, "Marathon", marathonActive);
        }

        public void Hide() => View.HideExtras(Container.transform, Marathon);
        public void Restore() => View.Restore();
    }

    private static void AssertUntouched(GameObject value, bool active, int callCount, string description)
    {
        Assert(value.activeSelf == active && value.SetActiveCalls == callCount, description);
    }

    private static void SharedContainerAndRestoration()
    {
        var fixture = new Fixture();
        var templateCalls = fixture.ExternalTemplate.SetActiveCalls;
        var inactiveBranchCalls = fixture.InactiveBranch.SetActiveCalls;
        fixture.Hide();
        Assert(fixture.Maps.All(map => map.activeInHierarchy),
            "Arena must keep all three base-map buttons visible inside the shared paths/buttons container");
        AssertUntouched(fixture.Container, true, 0, "Shared container must never be toggled");
        Assert(fixture.Maps.All(map => map.activeSelf && map.SetActiveCalls == 0),
            "Map GameObjects must remain unchanged");
        Assert(!fixture.ActivePath.activeSelf && !fixture.InactivePath.activeSelf && !fixture.PathUnderInactiveBranch.activeSelf,
            "Only generated paths, including inactive descendants, must be hidden");
        Assert(!fixture.Marathon.activeSelf, "Marathon must be hidden in Arena");
        AssertUntouched(fixture.ExternalTemplate, false, templateCalls, "External native path template must remain untouched");
        AssertUntouched(fixture.InactiveBranch, false, inactiveBranchCalls, "Path ancestry must never be toggled");

        fixture.Restore();
        Assert(fixture.ActivePath.activeSelf && !fixture.InactivePath.activeSelf && fixture.PathUnderInactiveBranch.activeSelf,
            "Leaving Arena must restore each path's original activeSelf, including originally inactive paths");
        Assert(fixture.Marathon.activeSelf, "Leaving Arena must restore the originally visible Marathon button");
        AssertUntouched(fixture.Container, true, 0, "Restoration must never toggle the shared container");
        Assert(fixture.Maps.All(map => map.activeInHierarchy && map.SetActiveCalls == 0),
            "Restoration must preserve map buttons");
        var pathCalls = fixture.ActivePath.SetActiveCalls;
        var marathonCalls = fixture.Marathon.SetActiveCalls;
        fixture.Restore();
        Assert(fixture.ActivePath.SetActiveCalls == pathCalls && fixture.Marathon.SetActiveCalls == marathonCalls,
            "Repeated Restore must do nothing after tracked state was cleared");
    }

    private static void InactiveContainerAndMarathon()
    {
        var fixture = new Fixture(containerActive: false, marathonActive: false);
        var containerCalls = fixture.Container.SetActiveCalls;
        fixture.Hide();
        AssertUntouched(fixture.Container, false, containerCalls, "An initially inactive container must also remain untouched");
        Assert(fixture.Maps.All(map => map.activeSelf && !map.activeInHierarchy && map.SetActiveCalls == 0),
            "The helper must preserve both map activeSelf and inherited visibility");
        Assert(!fixture.ActivePath.activeSelf && !fixture.InactivePath.activeSelf && !fixture.PathUnderInactiveBranch.activeSelf,
            "Path discovery must include descendants of an inactive container");
        fixture.Restore();
        Assert(!fixture.Marathon.activeSelf, "Restoration must not expose an originally inactive Marathon button");
        Assert(fixture.ActivePath.activeSelf && !fixture.InactivePath.activeSelf && fixture.PathUnderInactiveBranch.activeSelf,
            "Inactive ancestors must not replace each path's own original state");
        AssertUntouched(fixture.Container, false, containerCalls, "Restoration must preserve inactive container ownership");
    }

    private static void ReentryAndChangedNativeViews()
    {
        var fixture = new Fixture();
        fixture.Hide();
        var addedPath = Path(fixture.Container, "New path generated on reopening");
        fixture.Hide();
        Assert(!addedPath.activeSelf && !fixture.ActivePath.activeSelf && !fixture.Marathon.activeSelf,
            "Re-entry must hide newly generated paths as well as existing extras");
        Assert(fixture.Maps.All(map => map.activeInHierarchy && map.SetActiveCalls == 0),
            "Repeated entry must not hide or modify map buttons");
        fixture.Restore();
        Assert(addedPath.activeSelf && fixture.ActivePath.activeSelf && fixture.Marathon.activeSelf,
            "Repeated entry must not overwrite originally active states with the hidden state");
        Assert(!fixture.InactivePath.activeSelf, "Repeated entry must preserve originally inactive paths");

        fixture.ActivePath.SetActive(false);
        fixture.InactivePath.SetActive(true);
        fixture.Marathon.SetActive(false);
        fixture.Hide();
        fixture.Restore();
        Assert(!fixture.ActivePath.activeSelf && fixture.InactivePath.activeSelf && !fixture.Marathon.activeSelf,
            "After Restore, a new visit must snapshot fresh native states rather than retain stale originals");

        var other = new Fixture();
        fixture.Hide();
        fixture.View.HideExtras(other.Container.transform, other.Marathon);
        Assert(!fixture.ActivePath.activeSelf && fixture.InactivePath.activeSelf && !fixture.Marathon.activeSelf,
            "Switching to another screen instance must restore the first instance before tracking the second");
        Assert(!other.ActivePath.activeSelf && !other.Marathon.activeSelf && other.Maps.All(map => map.activeInHierarchy),
            "Switching screen instances must hide only the new screen's extras");
        fixture.Restore();
        Assert(other.ActivePath.activeSelf && other.Marathon.activeSelf,
            "Restoring after instance replacement must recover the new screen's original states");
    }

    private static void LifecycleAndDuplicateReferences()
    {
        var fixture = new Fixture();
        fixture.ActivePath.AddComponent<LocationPath>();
        fixture.View.HideExtras(fixture.Container.transform, fixture.ActivePath);
        Assert(fixture.ActivePath.SetActiveCalls == 1,
            "Multiple path components and a duplicate extra reference must only hide and snapshot an object once");
        fixture.Restore();
        Assert(fixture.ActivePath.activeSelf && fixture.ActivePath.SetActiveCalls == 2,
            "Duplicate references must not overwrite the object's original state or restore it twice");

        fixture.Hide();
        var removedPath = fixture.ActivePath;
        var removedMarathon = fixture.Marathon;
        UnityObject.Destroy(removedPath);
        UnityObject.Destroy(removedMarathon);
        Assert(removedPath == null && removedMarathon == null,
            "Stand-ins must reproduce Unity's null comparison for destroyed tracked objects");
        fixture.Restore();
        Assert(!fixture.InactivePath.activeSelf && fixture.PathUnderInactiveBranch.activeSelf,
            "Disposed native views must be skipped while surviving views still restore");
        fixture.Restore();
        fixture.View.HideExtras(fixture.Container.transform, removedMarathon);
        fixture.Restore();
        Assert(fixture.Maps.All(map => map.activeInHierarchy),
            "Re-entry after native disposal must not resurrect dead objects or affect surviving maps");
    }

    private static void OtherLayoutsAndNullInputs()
    {
        var fixture = new Fixture();
        fixture.Container.AddComponent<LocationPath>();
        fixture.Hide();
        AssertUntouched(fixture.Container, true, 0,
            "A LocationPath component on the container itself must not allow the shared container to be hidden");
        Assert(fixture.Maps.All(map => map.activeInHierarchy),
            "Container-level components must not hide descendant maps");
        fixture.Restore();

        var separate = new Fixture();
        var pathsOnly = Child(separate.Screen, "Separate paths container");
        var separatePath = Path(pathsOnly, "Standalone generated path");
        separate.View.HideExtras(pathsOnly.transform, separate.Marathon);
        Assert(!separatePath.activeSelf && separate.ActivePath.activeSelf && separate.Maps.All(map => map.activeInHierarchy),
            "A separate path container must hide only its own paths without touching another container");
        AssertUntouched(pathsOnly, true, 0, "A paths-only container must not be deactivated either");
        separate.Restore();
        Assert(separatePath.activeSelf && separate.Marathon.activeSelf,
            "A separate container layout must restore its paths and Marathon");

        var view = new ArenaLocationSelectionView();
        var marathonOnly = new GameObject("Marathon only");
        view.HideExtras(null, marathonOnly);
        Assert(!marathonOnly.activeSelf, "Missing path container must still allow Marathon to be hidden");
        view.HideExtras(null, null);
        Assert(marathonOnly.activeSelf, "Null re-entry must first restore the previously tracked objects");
        view.Restore();
        view.HideExtras(fixture.Container.transform, null);
        Assert(!fixture.ActivePath.activeSelf && fixture.Maps.All(map => map.activeInHierarchy),
            "A missing Marathon object must still allow paths to hide without affecting maps");
        view.Restore();
    }

    private static void FactoryNightCatalogAndUpgrade()
    {
        Assert(ArenaMapCatalog.IsSupportedMap("factory4_day")
               && ArenaMapCatalog.IsSupportedMap("FACTORY4_NIGHT")
               && ArenaMapCatalog.IsSupportedMap("labyrinth")
               && ArenaMapCatalog.IsSupportedMap("laboratory"),
            "The fixed Arena catalog must contain both Factory variants and the other two maps");
        Assert(!ArenaMapCatalog.IsSupportedMap("Woods")
               && !ArenaMapCatalog.IsSupportedMap(null),
            "The fixed Arena catalog must reject normal and missing locations");
        Assert(ArenaMapCatalog.AllowlistContains(
                " factory4_day, FACTORY4_NIGHT ", "factory4_night"),
            "The client allowlist must match trimmed location ids case-insensitively");
        Assert(!ArenaMapCatalog.AllowlistContains("factory4_day,Woods", "Woods"),
            "A configured unsupported map must not escape the fixed catalog");

        Assert(ArenaMapCatalog.UpgradeLegacyClientAllowlist(
                   ArenaMapCatalog.LegacyDefaultClientAllowlist)
               == ArenaMapCatalog.DefaultClientAllowlist,
            "The exact old BepInEx default must gain Factory night on upgrade");
        const string customized = "factory4_day, labyrinth,laboratory";
        Assert(ArenaMapCatalog.UpgradeLegacyClientAllowlist(customized) == customized,
            "A customized client allowlist must remain user-owned during upgrade");
    }

    private static void FactoryNightNativeSelectionPolicy()
    {
        bool day = true;
        bool night = true;
        bool labyrinth = true;
        bool Available(string id)
            => id == ArenaMapCatalog.FactoryDayId ? day
                : id == ArenaMapCatalog.FactoryNightId ? night
                : id == ArenaMapCatalog.LabyrinthId && labyrinth;

        Assert(ArenaMapCatalog.ShouldDisplayNativeLocation(
                   ArenaMapCatalog.FactoryDayId, Available)
               && !ArenaMapCatalog.ShouldDisplayNativeLocation(
                   ArenaMapCatalog.FactoryNightId, Available),
            "Both Factory variants must share the one native Factory-day tile");
        Assert(ArenaMapCatalog.ShouldDisplayNativeLocation(
                   ArenaMapCatalog.LabyrinthId, Available)
               && !ArenaMapCatalog.ShouldDisplayNativeLocation("Woods", _ => true),
            "Other supported tiles must follow availability without exposing normal maps");
        Assert(ArenaMapCatalog.CanSelectNativeLocation(
                ArenaMapCatalog.FactoryDayId, Available),
            "The shared Factory tile must be selectable when both variants are enabled");
        Assert(!ArenaMapCatalog.ShouldSelectFactoryNight(
                   requestedNight: false, dayAvailable: day, nightAvailable: night)
               && ArenaMapCatalog.ShouldSelectFactoryNight(
                   requestedNight: true, dayAvailable: day, nightAvailable: night),
            "With both variants enabled, EFT's requested day/night choice must be preserved");
        Assert(ArenaMapCatalog.HasFactoryTimeChoice(day, night),
            "The native time choice must remain visible when both variants are enabled");

        day = false;
        Assert(ArenaMapCatalog.ShouldDisplayNativeLocation(
                   ArenaMapCatalog.FactoryDayId, Available)
               && ArenaMapCatalog.CanSelectNativeLocation(
                   ArenaMapCatalog.FactoryDayId, Available),
            "Night-only configuration must still expose and accept the shared Factory tile");
        Assert(ArenaMapCatalog.ShouldSelectFactoryNight(
                requestedNight: false, dayAvailable: day, nightAvailable: night),
            "Night-only configuration must redirect a day request to the enabled night location");
        Assert(!ArenaMapCatalog.HasFactoryTimeChoice(day, night),
            "A single enabled Factory variant must not offer a dead time toggle");

        day = true;
        night = false;
        Assert(!ArenaMapCatalog.ShouldSelectFactoryNight(
                requestedNight: true, dayAvailable: day, nightAvailable: night),
            "Day-only configuration must redirect a night request to the enabled day location");

        day = false;
        Assert(!ArenaMapCatalog.ShouldDisplayNativeLocation(
                   ArenaMapCatalog.FactoryDayId, Available)
               && !ArenaMapCatalog.CanSelectNativeLocation(
                   ArenaMapCatalog.FactoryDayId, Available),
            "Factory must disappear and reject selection when neither variant is enabled");
        Assert(ArenaMapCatalog.AnyMapAvailable(Available),
            "Another enabled supported map must still admit Arena when Factory is disabled");
        labyrinth = false;
        Assert(!ArenaMapCatalog.AnyMapAvailable(Available),
            "Arena admission must fail when all supported variants are disabled");
    }

    public static void Main()
    {
        var originalBug = new Fixture();
        originalBug.Container.SetActive(false);
        Assert(originalBug.Maps.All(map => map.activeSelf && !map.activeInHierarchy),
            "Deactivating the shared container must reproduce the original empty Arena map screen");

        SharedContainerAndRestoration();
        InactiveContainerAndMarathon();
        ReentryAndChangedNativeViews();
        LifecycleAndDuplicateReferences();
        OtherLayoutsAndNullInputs();
        FactoryNightCatalogAndUpgrade();
        FactoryNightNativeSelectionPolicy();
        Console.WriteLine($"PASS: {_checks} native map-view regression checks (production helper linked).");
    }
}
