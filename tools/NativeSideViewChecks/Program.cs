using System;
using System.Text;
using ArenaMode.Session;
using EFT.UI;
using UnityEngine;

// Fixture values were inspected in this installation's actual Unity level48:
// MatchMaker Side Selection Screen / AlphaBlocker / RandomToggleSpawner.
// This checks the linked production helper against inactive ancestry and canvas
// eligibility. It cannot verify rendering, Harmony binding, events or gameplay.
internal static class Program
{
    private static int _checks;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    private static void MustReject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { _checks++; return; }
        throw new Exception(message);
    }

    private sealed class Fixture
    {
        public readonly GameObject Screen = new("MatchMaker Side Selection Screen");
        public readonly GameObject Wrapper;
        public readonly CanvasGroup WrapperGroup;
        public readonly HoverTooltipArea Tooltip;
        public readonly UIAnimatedToggleSpawner Arena;
        public readonly GameObject Pmc;
        public readonly GameObject Scav;

        public Fixture(bool addWrapperGroup = true)
        {
            Screen.AddComponent<CanvasGroup>();
            Wrapper = Child(Screen, "AlphaBlocker");
            Wrapper.SetActive(false);
            if (addWrapperGroup)
            {
                WrapperGroup = Wrapper.AddComponent<CanvasGroup>();
                WrapperGroup.alpha = .3f;
                WrapperGroup.interactable = false;
            }
            Tooltip = Wrapper.AddComponent<HoverTooltipArea>();
            Tooltip._message = "Not available in alpha";
            Arena = Child(Wrapper, "RandomToggleSpawner").AddComponent<UIAnimatedToggleSpawner>();
            _ = Arena.SpawnedObject; // Native screen Awake creates the toggle.
            Arena.gameObject.SetActive(false); // Vanilla screen Show hides it.
            Pmc = Child(Screen, "PMCs");
            Pmc.AddComponent<CanvasGroup>();
            var pmcButton = Child(Pmc, "AnimatedToggleSpawner").AddComponent<UIAnimatedToggleSpawner>();
            pmcButton._unavailable = false;
            _ = pmcButton.SpawnedObject;
            Scav = Child(Screen, "Savage");
            var scavBlocker = Child(Scav, "SavageBlocker");
            var scavGroup = scavBlocker.AddComponent<CanvasGroup>();
            scavGroup.alpha = .3f;
            scavGroup.interactable = false;
            var scavButton = Child(scavBlocker, "AnimatedToggleSpawner").AddComponent<UIAnimatedToggleSpawner>();
            _ = scavButton.SpawnedObject;
        }

        public void Show(bool canSelect = true) => ArenaSideSelectionView.Show(Screen.transform, Arena, canSelect);
        public void Hide() => ArenaSideSelectionView.Hide(Screen.transform, Arena);
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.parent = parent.transform;
        return child;
    }

    private static string Snapshot(GameObject root)
    {
        var text = new StringBuilder();
        void Visit(GameObject node)
        {
            text.Append(node.name).Append(':').Append(node.activeSelf).Append(':').Append(node.transform.childCount);
            foreach (var group in node.GetComponents<CanvasGroup>())
                text.Append(" canvas=").Append(group.enabled).Append(',').Append(group.alpha)
                    .Append(',').Append(group.interactable).Append(',').Append(group.blocksRaycasts)
                    .Append(',').Append(group.ignoreParentGroups);
            foreach (var tooltip in node.GetComponents<HoverTooltipArea>())
                text.Append(" tooltip=").Append(tooltip.enabled).Append(',').Append(tooltip._message);
            foreach (var spawner in node.GetComponents<UIAnimatedToggleSpawner>())
                text.Append(" spawner=").Append(spawner._headerCaption).Append(',').Append(spawner.SpawnCount);
            foreach (var toggle in node.GetComponents<AnimatedToggle>())
                text.Append(" toggle=").Append(toggle.interactable);
            text.AppendLine();
            for (var i = 0; i < node.transform.childCount; i++)
                Visit(node.transform.GetChild(i).gameObject);
        }
        Visit(root);
        return text.ToString();
    }

    public static void Main()
    {
        var originalBug = new Fixture();
        originalBug.Arena.gameObject.SetActive(true);
        originalBug.Arena.SpawnedObject.interactable = true;
        Assert(!originalBug.Arena.gameObject.activeInHierarchy,
            "Button-only activation must reproduce the invisible original UI");
        Assert(!originalBug.WrapperGroup.interactable && originalBug.WrapperGroup.alpha == .3f,
            "Original bug must also retain the parent's input and opacity blockers");

        var fixture = new Fixture();
        var pmcBefore = Snapshot(fixture.Pmc);
        var scavBefore = Snapshot(fixture.Scav);
        var nativeToggle = fixture.Arena.SpawnedObject;
        var nativeGroup = nativeToggle.group;
        Assert(fixture.Arena._canvasGroup == null, "Recorded Random spawner has no CanvasGroup reference");
        fixture.Show();
        Assert(fixture.Arena.gameObject.activeInHierarchy,
            "Show must activate the inactive owned wrapper, not just RandomToggleSpawner");
        Assert(fixture.WrapperGroup.alpha == 1f && fixture.WrapperGroup.interactable
            && fixture.WrapperGroup.blocksRaycasts,
            "Show must make the parent fully opaque and permit input");
        Assert(nativeToggle.interactable, "Solo Arena must enable the native toggle");
        Assert(!fixture.Tooltip.enabled, "Arena must not show the obsolete alpha-unavailable tooltip");
        Assert(fixture.Arena._headerCaption == "Arena", "Native Random caption must become Arena");
        Assert(fixture.Arena.SpawnedObject == nativeToggle && nativeToggle.group == nativeGroup
            && fixture.Arena.SpawnCount == 1, "Native toggle identity and group must be preserved");
        Assert(Snapshot(fixture.Pmc) == pmcBefore && Snapshot(fixture.Scav) == scavBefore,
            "Showing Arena must not unlock or alter PMC and Scav controls");
        var shownState = Snapshot(fixture.Screen);
        fixture.Show();
        Assert(Snapshot(fixture.Screen) == shownState && fixture.Arena.SpawnCount == 1,
            "Repeated Show must preserve the hierarchy without duplicate toggles");

        fixture.Show(false);
        Assert(fixture.Arena.gameObject.activeInHierarchy && fixture.WrapperGroup.alpha == 1f,
            "Ineligible Arena remains visible rather than disappearing");
        Assert(!nativeToggle.interactable && !fixture.WrapperGroup.interactable,
            "Ineligible Arena must block both toggle and wrapper input");
        Assert(fixture.WrapperGroup.blocksRaycasts && !fixture.Tooltip.enabled,
            "Ineligibility must not restore the obsolete alpha tooltip or click-through behavior");
        fixture.Show();
        Assert(nativeToggle.interactable && fixture.WrapperGroup.interactable,
            "Reopening in solo mode must recover from an ineligible display");
        fixture.Hide();
        Assert(!fixture.Wrapper.activeSelf && !fixture.Arena.gameObject.activeSelf,
            "Hide must deactivate the owned wrapper and its Arena spawner");
        Assert(fixture.Screen.activeSelf && Snapshot(fixture.Pmc) == pmcBefore
            && Snapshot(fixture.Scav) == scavBefore, "Hide must leave the screen, PMC and Scav unchanged");
        fixture.Hide();
        fixture.Show();
        Assert(fixture.Arena.gameObject.activeInHierarchy && fixture.Arena.SpawnCount == 1,
            "Hide and repeated reopen must reuse the original native toggle");

        var canvasOnly = new Fixture();
        canvasOnly.Wrapper.SetActive(true);
        canvasOnly.Show();
        Assert(canvasOnly.WrapperGroup.alpha == 1f && canvasOnly.WrapperGroup.interactable,
            "An already active wrapper must still have its canvas blockers repaired");

        var sharedWrapper = new Fixture();
        Child(sharedWrapper.Wrapper, "Another control");
        var sharedBefore = Snapshot(sharedWrapper.Screen);
        MustReject(() => sharedWrapper.Show(), "Shared wrapper must not be accepted as Arena-owned");
        Assert(Snapshot(sharedWrapper.Screen) == sharedBefore,
            "Rejecting a shared wrapper must happen before any caption, canvas or activation mutation");
        sharedWrapper.Wrapper.SetActive(true);
        sharedWrapper.Arena.gameObject.SetActive(true);
        sharedWrapper.Hide();
        Assert(sharedWrapper.Wrapper.activeSelf && sharedWrapper.Screen.activeSelf
            && !sharedWrapper.Arena.gameObject.activeSelf,
            "Hide on an unsupported shared wrapper must hide only Arena");

        var noWrapper = new Fixture();
        noWrapper.Arena.transform.parent = noWrapper.Screen.transform;
        var noWrapperBefore = Snapshot(noWrapper.Screen);
        MustReject(() => noWrapper.Show(), "Direct screen child must not cause the screen itself to be unlocked");
        Assert(Snapshot(noWrapper.Screen) == noWrapperBefore,
            "Rejecting a missing wrapper must leave the shared screen untouched");
        noWrapper.Arena.gameObject.SetActive(true);
        noWrapper.Hide();
        Assert(noWrapper.Screen.activeSelf && !noWrapper.Arena.gameObject.activeSelf,
            "Hide must not deactivate the screen when Arena is a direct child");

        var foreignParent = new Fixture();
        var extraAncestor = Child(foreignParent.Screen, "Unverified container");
        foreignParent.Wrapper.transform.parent = extraAncestor.transform;
        var foreignBefore = Snapshot(foreignParent.Screen);
        MustReject(() => foreignParent.Show(), "Unverified extra ancestor must not be unlocked");
        Assert(Snapshot(foreignParent.Screen) == foreignBefore,
            "Rejecting unknown ancestry must happen before mutation");

        var missingGroup = new Fixture(addWrapperGroup: false);
        var missingGroupBefore = Snapshot(missingGroup.Screen);
        MustReject(() => missingGroup.Show(), "Missing wrapper canvas must not silently leave a partial Arena UI");
        Assert(Snapshot(missingGroup.Screen) == missingGroupBefore,
            "Missing canvas failure must happen before mutation");

        Console.WriteLine($"PASS: {_checks} native side-view regression checks (production helper linked).");
    }
}
