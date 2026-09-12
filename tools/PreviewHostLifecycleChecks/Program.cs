using ArenaMode;
using ArenaMode.Session;
using ArenaMode.Session.Preview;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static int Main()
    {
        try
        {
            CallbackDefersStructuralWork();
            PreCloseDetachesBeforeHierarchyMutation();
            FastSameHostRestoresBeforeResnapshot();
            LoadingSameFrameReshowRetainsLocalIdentity();
            OlderGenerationCannotUnsetNewHost();
            DestroyedHostFallsBackAndDefersRelease();
            DestroyedWorldIsUnavailable();
            SelectionAndForeignHostGuards();
            ResetDropsPendingHide();
            Console.WriteLine($"PREVIEW_HOST_LIFECYCLE_CHECKS_PASSED: {_assertions} assertions; simulated lifecycle boundary, no Unity rendering executed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("ASSERTION_FAILED: " + ex); return 1; }
        finally { ArenaPreviewFlow.Reset(); }
    }
    private static void Check(bool condition, string message)
    {
        ++_assertions;
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class Fixture
    {
        public readonly RaidSettings Settings = new();
        public readonly ArenaCharacterPreview Preview;
        public Fixture(bool loading = false)
        {
            ArenaPreviewFlow.Reset();
            ArenaSession.IsActive = false;
            Plugin.ModLogger.Warnings.Clear();
            ArenaMenuSelection.Selected = Settings;
            Preview = new(loading);
            ArenaSideSelectionLayout.Pending = Preview;
            ArenaPreviewFlow.KeepFromSide(new GameObject().AddComponent<MatchMakerSideSelectionScreen>(), Settings);
            Check(Preview.IsAvailable, "fixture preview kept");
            Check(Preview.Guard != null, "persistent guard installed by actual SetFlowGuard");
        }
        public void Tick() => Lifecycle.Invoke(Preview.Guard, "Update");
        public Widget Accept()
        {
            var result = new Widget();
            ArenaPreviewFlow.PrepareAccept(result.Screen, Settings);
            Check(ArenaPreviewFlow.TryShowAccept(result.Model), "local native accept image substituted");
            return result;
        }
        public LoadingWidget Loading()
        {
            var result = new LoadingWidget(Settings);
            ArenaPreviewFlow.PrepareLoading(result.Screen, result.Controller);
            Check(ArenaPreviewFlow.TryShowLoading(result.Screen), "local native loading image substituted");
            return result;
        }
    }
    private sealed class LoadingWidget
    {
        public readonly GameObject Root = new("loading screen");
        public readonly MatchmakerTimeHasCome Screen;
        public readonly MatchmakerTimeHasCome.TimeHasComeScreenController Controller;
        public readonly RawImage Raw;
        public readonly CameraImage Camera;
        public LoadingWidget(RaidSettings settings)
        {
            Screen = Root.AddComponent<MatchmakerTimeHasCome>();
            var raw = new GameObject("native loading image"); raw.transform.SetParent(Root.transform, false);
            Raw = raw.AddComponent<RawImage>();
            Camera = raw.AddComponent<CameraImage>();
            Screen._playerModelView = Raw;
            Controller = new() { RaidSettings = settings };
        }
    }
    private sealed class Widget
    {
        public readonly GameObject Root = new("accept screen");
        public readonly MatchMakerAcceptScreen Screen;
        public readonly MatchMakerPlayerPreview Model;
        public readonly RawImage Raw;
        public readonly CameraImage Camera;
        public readonly Image Placeholder;
        public Widget()
        {
            Screen = Root.AddComponent<MatchMakerAcceptScreen>();
            var host = new GameObject("model host"); host.transform.SetParent(Root.transform, false);
            Model = host.AddComponent<MatchMakerPlayerPreview>();
            var raw = new GameObject("native image"); raw.transform.SetParent(host.transform, false);
            Raw = raw.AddComponent<RawImage>();
            Camera = raw.AddComponent<CameraImage>();
            var placeholder = new GameObject("native placeholder"); placeholder.transform.SetParent(host.transform, false);
            Placeholder = placeholder.AddComponent<Image>();
            Model._playerVisualImage = Raw; Model._noVisualImage = Placeholder; Screen._playerModelView = Model;
        }
        public void CheckNative(bool restored, string reason)
        {
            Check(Raw.enabled == restored, reason + ": raw enabled");
            Check(Camera.enabled == restored, reason + ": camera enabled");
            Check(Placeholder.gameObject.activeSelf == restored, reason + ": placeholder active");
            Check(Raw.gameObject.activeSelf, reason + ": raw active snapshot");
        }
    }
    private static void CallbackDefersStructuralWork()
    {
        var fixture = new Fixture(); var widget = fixture.Accept();
        widget.Root.AddComponent<ReentrantClose>();
        widget.CheckNative(false, "initial suppression");
        Lifecycle.ResetCounts();
        widget.Root.SetActive(false);
        Check(Lifecycle.ForbiddenAttempts == 0, "OnDisable must not attempt SetActive/SetParent while parent hierarchy is mutating");
        Check(Lifecycle.TotalMutations == 1, "callback queues only; host SetActive is the sole structural operation");
        Check(fixture.Preview.View.transform.parent == widget.Raw.transform, "image remains structurally untouched until a safe turn");
        fixture.Tick();
        Check(Lifecycle.ForbiddenAttempts == 0, "guard tick outside callback may safely detach");
        Check(fixture.Preview.View.transform.parent == null, "actual Suspend detached after guard tick");
        Check(!fixture.Preview.View.activeSelf, "actual Suspend hid image after guard tick");
        widget.CheckNative(true, "deferred native flag restoration");
    }
    private static void PreCloseDetachesBeforeHierarchyMutation()
    {
        var fixture = new Fixture(); var widget = fixture.Accept();
        Lifecycle.ResetCounts();
        ArenaPreviewFlow.BeforeScreenClose(widget.Root.transform);
        Check(fixture.Preview.View.transform.parent == null, "pre-close ancestor match detaches before native Close");
        widget.CheckNative(true, "pre-close restores native flags");
        widget.Root.SetActive(false);
        Check(Lifecycle.ForbiddenAttempts == 0, "normal Close has no callbacks performing structural work");
        Check(fixture.Preview.IsAvailable, "pre-close retains reusable mannequin");
        var next = new GameObject("map");
        ArenaPreviewFlow.ShowCompact(next.transform, fixture.Settings);
        Check(fixture.Preview.View.transform.parent == next.transform, "retained mannequin reused by next compact screen");
        ArenaPreviewFlow.BeforeScreenClose(next.transform);
        Check(fixture.Preview.View.transform.parent == null, "exact host pre-close also detaches");
    }
    private static void FastSameHostRestoresBeforeResnapshot()
    {
        var fixture = new Fixture(); var widget = fixture.Accept();
        ArenaPreviewFlow.PrepareAccept(widget.Screen, fixture.Settings);
        widget.Root.SetActive(false);
        widget.Root.SetActive(true);
        // Native Show can close the old child AFTER PrepareAccept and before
        // the visual request, all within one frame before persistent Update.
        Check(ArenaPreviewFlow.TryShowAccept(widget.Model), "same-host immediate re-show admitted");
        Check(fixture.Preview.Shows == 2, "pending hide must flush before same-host shortcut");
        Check(ArenaPreviewFlow.TryShowAccept(widget.Model), "same-frame reattachment retains exact native widget ownership");
        Check(fixture.Preview.Shows == 2, "later local visual update uses same-host fast path without duplicate attachment");
        widget.CheckNative(false, "new attachment suppresses native after clean snapshot");
        ArenaPreviewFlow.BeforeScreenClose(widget.Root.transform);
        widget.CheckNative(true, "second close restores original flags rather than suppressed resnapshot");
        Check(fixture.Preview.View.transform.parent == null, "second close detached view");
    }
    private static void LoadingSameFrameReshowRetainsLocalIdentity()
    {
        var fixture = new Fixture(); var widget = fixture.Loading();
        var local = new RaidPlayer { AccountId = "local" };
        var teammate = new RaidPlayer { AccountId = "someone else" };
        Check(ArenaPreviewFlow.IsLocalLoadingPlayer(widget.Screen, local), "local account accepted initially");
        Check(!ArenaPreviewFlow.IsLocalLoadingPlayer(widget.Screen, teammate), "teammate account remains native initially");
        ArenaPreviewFlow.PrepareLoading(widget.Screen, widget.Controller);
        widget.Root.SetActive(false);
        widget.Root.SetActive(true);
        Check(ArenaPreviewFlow.TryShowLoading(widget.Screen), "same-frame loading re-show attaches");
        Check(fixture.Preview.Shows == 2, "loading hide flushed before fast path");
        Check(ArenaPreviewFlow.TryShowLoading(widget.Screen), "later loading request retains current screen ownership");
        Check(fixture.Preview.Shows == 2, "later loading request does not duplicate attachment");
        Check(ArenaPreviewFlow.IsLocalLoadingPlayer(widget.Screen, local), "same-frame re-show retains local account owner");
        Check(!ArenaPreviewFlow.IsLocalLoadingPlayer(widget.Screen, teammate), "re-show still excludes teammate account");
        widget.Screen._dragTrigger.Fire();
        Check(fixture.Preview.Rotations == 1, "only one active drag binding survives reattachment");
        ArenaPreviewFlow.ShowNativeLoading(widget.Screen);
        Check(widget.Raw.enabled && widget.Camera.enabled, "teammate native switch restores native flags");
        Check(fixture.Preview.View.transform.parent == null, "teammate native switch suspends mannequin");
        Check(ArenaPreviewFlow.IsLocalLoadingPlayer(widget.Screen, local), "native teammate view preserves local-click identity");
        Check(ArenaPreviewFlow.TryShowLoading(widget.Screen), "local click restores Arena mannequin after teammate");
        Check(fixture.Preview.View.transform.parent == widget.Raw.transform, "local click uses same owned loading image");
        ArenaPreviewFlow.BeforeScreenClose(widget.Root.transform);
        Check(widget.Raw.enabled && widget.Camera.enabled, "final loading close restores original native snapshot");
    }
    private static void OlderGenerationCannotUnsetNewHost()
    {
        var fixture = new Fixture(); var first = fixture.Accept();
        var oldCallback = first.Model.GetComponent<ArenaPreviewHostLifetime>().OnHidden;
        var second = fixture.Accept();
        Check(fixture.Preview.View.transform.parent == second.Raw.transform, "second host current");
        oldCallback();
        fixture.Tick();
        Check(fixture.Preview.View.transform.parent == second.Raw.transform, "old-generation deferred callback cannot detach new host");
        second.CheckNative(false, "new-host native flags remain owned");
        Check(ArenaPreviewFlow.TryShowAccept(second.Model), "old callback cannot unset current native widget identity");
        Check(fixture.Preview.Shows == 2, "new host fast path remains current after stale callback");
    }
    private static void DestroyedHostFallsBackAndDefersRelease()
    {
        var fixture = new Fixture(loading: true); var widget = fixture.Accept();
        Lifecycle.ResetCounts();
        widget.Root.DestroyHost();
        Check(Lifecycle.ForbiddenAttempts == 0, "unexpected OnDisable/OnDestroy never attempt hierarchy operations");
        Check(!fixture.Preview.IsAvailable, "Unity-null destroyed view invalidates actual IsAvailable");
        fixture.Tick();
        Check(fixture.Preview.Releases == 0, "pending body load defers release when guard resets unavailable owner");
        Check(!ArenaPreviewFlow.TryShowAccept(widget.Model), "destroyed host cannot suppress native fallback");
        fixture.Preview.FinishLoad();
        Check(fixture.Preview.Releases == 1, "actual lifetime releases once after pending load finishes");
        fixture.Preview.FinishLoad();
        Check(fixture.Preview.Releases == 1, "load finally cannot double release");
        var foreign = new Widget();
        ArenaPreviewFlow.PrepareAccept(foreign.Screen, fixture.Settings);
        Check(!ArenaPreviewFlow.TryShowAccept(foreign.Model), "disposed flow leaves newly-created native widget alone");
        foreign.CheckNative(true, "native fallback remains unchanged");
    }
    private static void DestroyedWorldIsUnavailable()
    {
        var fixture = new Fixture(); var widget = fixture.Accept();
        fixture.Preview.RemoveWorldBoundary();
        Check(!fixture.Preview.IsAvailable, "Unity-null world invalidates actual IsAvailable");
        Check(!ArenaPreviewFlow.TryShowAccept(widget.Model), "missing world must not claim native image");
    }
    private static void SelectionAndForeignHostGuards()
    {
        var fixture = new Fixture(); var widget = fixture.Accept();
        var foreignRoot = new GameObject("foreign screen");
        ArenaPreviewFlow.BeforeScreenClose(foreignRoot.transform);
        Check(fixture.Preview.View.transform.parent == widget.Raw.transform, "foreign screen close cannot detach owner");
        var foreign = new Widget();
        ArenaPreviewFlow.PrepareAccept(foreign.Screen, new RaidSettings());
        Check(!ArenaPreviewFlow.TryShowAccept(foreign.Model), "foreign raid settings leave native widget alone");
        foreign.CheckNative(true, "foreign settings native flags untouched");
        ArenaPreviewFlow.ShowCompact(foreignRoot.transform, new RaidSettings());
        Check(fixture.Preview.View.transform.parent == widget.Raw.transform, "foreign compact host ignored");
        ArenaPreviewFlow.PrepareAccept(widget.Screen, fixture.Settings);
        widget.Model._isMainCharacter = false;
        Check(!ArenaPreviewFlow.TryShowAccept(widget.Model), "teammate model remains native");
        widget.Model._isMainCharacter = true;
        ArenaMenuSelection.Selected = null;
        Check(!ArenaPreviewFlow.TryShowAccept(widget.Model), "unselected Arena leaves native rendering path active");
        fixture.Tick();
        Check(fixture.Preview.Releases == 1, "invalid selection triggers owned release through real guard");
        widget.CheckNative(true, "unselection restores native flags");
        Check(widget.Model.NativeUpdates == 1, "unselection schedules one native visual restoration");
    }
    private static void ResetDropsPendingHide()
    {
        var previous = new Fixture(); var old = previous.Accept();
        old.Root.SetActive(false);
        ArenaPreviewFlow.Reset();
        var current = new Fixture(); var next = current.Accept();
        current.Tick();
        Check(current.Preview.View.transform.parent == next.Raw.transform, "pending action from reset flow cannot suspend replacement");
        Check(previous.Preview.Releases == 1, "reset releases old lifetime once");
        Check(current.Preview.Releases == 0, "new owner survives old flow reset");
    }
}
