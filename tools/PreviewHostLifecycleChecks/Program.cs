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
            MapHidesButRetainsOwnedPreview();
            FastSameHostRestoresBeforeResnapshot();
            LoadingSameFrameReshowRetainsLocalIdentity();
            OlderGenerationCannotUnsetNewHost();
            DestroyedHostFallsBackAndDefersRelease();
            DestroyedWorldIsUnavailable();
            SelectionAndForeignHostGuards();
            ResetDropsPendingHide();
            EquipmentChoiceFollowsActualModes();
            EquipmentChangesRestoreNativeAndKeepPendingLoad();
            LoadingArmFreezesEquipmentAndRejectsStaleArm();
            TeammateAndReconnectKeepNativeEquipment();
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
            ArenaSession.ArmedForNextRaid = false;
            ArenaSession.ArmEpoch = 0;
            ArenaSession.ArmedMode = null;
            ArenaSession.ZombieOwnGearSelection = false;
            ArenaMode.Config.Settings.SelectedGameModeId = "deathmatch";
            ArenaMode.Config.Settings.OwnGear = false;
            ArenaMenuSelection.CurrentEligibility = new();
            ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = false;
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
        var next = new GameObject("offline setup");
        ArenaPreviewFlow.ShowCompact(next.transform, fixture.Settings);
        Check(fixture.Preview.View.transform.parent == next.transform, "retained mannequin reused by next compact screen");
        ArenaPreviewFlow.BeforeScreenClose(next.transform);
        Check(fixture.Preview.View.transform.parent == null, "exact host pre-close also detaches");
    }
    private static void MapHidesButRetainsOwnedPreview()
    {
        var fixture = new Fixture();
        ArenaPreviewFlow.HideForMap(fixture.Settings);
        Check(!fixture.Preview.View.activeSelf && fixture.Preview.View.transform.parent == null,
            "map hides freshly transferred preview");
        Check(fixture.Preview.IsAvailable && fixture.Preview.Releases == 0,
            "map keeps owned body for subsequent screens");

        var widget = fixture.Accept();
        var oldCallback = widget.Model.GetComponent<ArenaPreviewHostLifetime>().OnHidden;
        ArenaPreviewFlow.HideForMap(new RaidSettings());
        Check(fixture.Preview.View.transform.parent == widget.Raw.transform,
            "foreign map settings cannot hide current owner");
        widget.Root.SetActive(false); // Queued fallback hide before map Show.
        ArenaPreviewFlow.HideForMap(fixture.Settings);
        Check(!fixture.Preview.View.activeSelf && fixture.Preview.View.transform.parent == null,
            "map suspends previous accept surface");
        widget.CheckNative(true, "map restores preceding native flags");
        widget.Root.SetActive(true);
        Check(!ArenaPreviewFlow.TryShowAccept(widget.Model), "map revokes stale accept request");
        ArenaPreviewFlow.HideForMap(fixture.Settings);
        Check(fixture.Preview.IsAvailable && fixture.Preview.Releases == 0,
            "repeated map show retains owner");

        var next = fixture.Accept();
        oldCallback(); fixture.Tick();
        Check(fixture.Preview.View.transform.parent == next.Raw.transform,
            "map-cleared pending hide cannot detach subsequent preview");
        Check(fixture.Preview.Releases == 0, "accept after map reuses original body");

        var loading = fixture.Loading();
        ArenaPreviewFlow.HideForMap(fixture.Settings);
        Check(!ArenaPreviewFlow.TryShowLoading(loading.Screen), "map revokes stale loading request");
        Check(!ArenaPreviewFlow.IsLocalLoadingPlayer(loading.Screen, new RaidPlayer { AccountId = "local" }),
            "map clears loading account ownership");
        Check(loading.Raw.enabled && loading.Camera.enabled, "map restores loading native image");
        var offline = new GameObject("offline setup");
        ArenaPreviewFlow.ShowCompact(offline.transform, fixture.Settings);
        Check(fixture.Preview.View.transform.parent == offline.transform,
            "offline setup still receives retained preview after map");
        ArenaMenuSelection.Selected = null;
        ArenaPreviewFlow.HideForMap(fixture.Settings);
        Check(fixture.Preview.View.transform.parent == offline.transform,
            "unselected map cannot mutate prior preview");
        fixture.Tick();
        Check(fixture.Preview.Releases == 1, "usual selection guard releases retained map flow");
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

    private static void EquipmentChoiceFollowsActualModes()
    {
        foreach (string mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch", "zombie", " ZOMBIE " })
        foreach (bool own in new[] { false, true })
        foreach (bool team in new[] { false, true })
        foreach (bool allowed in new[] { false, true })
        {
            var fixture = new Fixture();
            ArenaMode.Config.Settings.SelectedGameModeId = mode;
            ArenaMode.Config.Settings.OwnGear = own;
            ArenaMenuSelection.CurrentEligibility = new() { Allowed = allowed, IsTeam = team };
            bool native = mode.Trim().Equals("zombie", StringComparison.OrdinalIgnoreCase) && own
                || (mode == "tactical" || mode == "zombie") && allowed && team;
            var accept = new Widget();
            ArenaPreviewFlow.PrepareAccept(accept.Screen, fixture.Settings);
            Check(ArenaPreviewFlow.TryShowAccept(accept.Model) == !native,
                $"confirmation selects actual equipment path: {mode}/{own}/{team}/{allowed}");
            accept.CheckNative(native, "mode selection confirmation flags");
            var loading = new LoadingWidget(fixture.Settings);
            ArenaPreviewFlow.PrepareLoading(loading.Screen, loading.Controller);
            Check(ArenaPreviewFlow.TryShowLoading(loading.Screen) == !native,
                $"loading selects actual equipment path: {mode}/{own}/{team}/{allowed}");
            Check(loading.Raw.enabled == native && loading.Camera.enabled == native,
                "loading native flags reflect equipment owner");
            Check(fixture.Preview.Releases == 0, "equipment selection keeps detached renderer available");
        }
    }

    private static void EquipmentChangesRestoreNativeAndKeepPendingLoad()
    {
        var fixture = new Fixture(loading: true); var accept = fixture.Accept();
        var oldCallback = accept.Model.GetComponent<ArenaPreviewHostLifetime>().OnHidden;
        ArenaMode.Config.Settings.SelectedGameModeId = "zombie";
        ArenaMode.Config.Settings.OwnGear = true;
        fixture.Tick();
        accept.CheckNative(true, "own gear restores native confirmation");
        Check(accept.Model.NativeUpdates == 1 && accept.Camera.Recreated == 1,
            "own gear transition requests native visual and recreates released texture");
        Check(!fixture.Preview.View.activeSelf && fixture.Preview.View.transform.parent == null,
            "own gear hides representative Arena equipment");
        Check(fixture.Preview.Releases == 0, "pending body init survives source change");
        fixture.Preview.FinishLoad();
        Check(fixture.Preview.Releases == 0 && !fixture.Preview.View.activeSelf,
            "late Arena load cannot replace the native own-gear view");
        fixture.Tick();
        Check(accept.Model.NativeUpdates == 1, "unchanged own gear does not reload each frame");
        ArenaMode.Config.Settings.OwnGear = false;
        fixture.Tick();
        accept.CheckNative(false, "Arena kits restore retained figure");
        Check(fixture.Preview.Shows == 2, "source choice reuses the single body");
        oldCallback(); fixture.Tick();
        Check(fixture.Preview.View.transform.parent == accept.Raw.transform,
            "stale callback from prior equipment source cannot hide new surface");
        ArenaMode.Config.Settings.OwnGear = true;
        var loading = new LoadingWidget(fixture.Settings);
        ArenaPreviewFlow.PrepareLoading(loading.Screen, loading.Controller);
        Check(!ArenaPreviewFlow.TryShowLoading(loading.Screen), "own loading remains native");
        accept.CheckNative(true, "new own-gear host restores previous provided-gear surface");
        ArenaPreviewFlow.Reset();
        Check(fixture.Preview.Releases == 1, "source transitions still release single renderer once");
    }

    private static void LoadingArmFreezesEquipmentAndRejectsStaleArm()
    {
        var fixture = new Fixture();
        ArenaSession.ArmedForNextRaid = true;
        ArenaSession.ArmedMode = "zombie";
        ArenaSession.ZombieOwnGearSelection = true;
        ArenaSession.ArmEpoch = 12;
        var loading = fixture.Loading();
        Check(!loading.Raw.enabled, "stale earlier arm cannot override current provided-kit selection");
        ArenaSession.ArmEpoch++;
        fixture.Tick();
        Check(loading.Raw.enabled && loading.Camera.enabled && loading.Camera.Recreated == 1,
            "new current arm switches loading to frozen own equipment");
        Check(loading.Screen.NativeUpdates == 1, "new arm initiates one native loading update");
        ArenaMode.Config.Settings.SelectedGameModeId = "deathmatch";
        ArenaMode.Config.Settings.OwnGear = false;
        fixture.Tick();
        Check(!ArenaPreviewFlow.TryShowLoading(loading.Screen), "later F12 edit cannot replace frozen own gear");
        Check(loading.Screen.NativeUpdates == 1, "unchanged frozen source does not retry native load");
        ArenaPreviewFlow.PrepareLoading(loading.Screen, loading.Controller);
        Check(!ArenaPreviewFlow.TryShowLoading(loading.Screen), "same loading screen re-show keeps frozen source");
        ArenaPreviewFlow.HideForMap(fixture.Settings);
        var next = fixture.Loading();
        Check(!next.Raw.enabled, "return from aborted load uses new menu selection, not old arm");
        ArenaSession.ArmedMode = "tactical";
        ArenaSession.ZombieOwnGearSelection = false;
        ArenaMode.Compat.PitFireTeamCompat.HasFrozenTeamRoster = true;
        ArenaSession.ArmEpoch++;
        fixture.Tick();
        Check(next.Raw.enabled && next.Screen.NativeUpdates == 1, "current frozen PFT roster selects own gear");

        fixture = new Fixture();
        ArenaMode.Config.Settings.SelectedGameModeId = "zombie";
        ArenaMode.Config.Settings.OwnGear = true;
        next = new LoadingWidget(fixture.Settings);
        ArenaPreviewFlow.PrepareLoading(next.Screen, next.Controller);
        Check(!ArenaPreviewFlow.TryShowLoading(next.Screen), "initial own selection uses native");
        ArenaSession.ArmedForNextRaid = true;
        ArenaSession.ArmedMode = "zombie";
        ArenaSession.ZombieOwnGearSelection = false;
        ArenaSession.ArmEpoch++;
        fixture.Tick();
        Check(!next.Raw.enabled && fixture.Preview.Shows == 1,
            "frozen provided-kit decision overrides earlier own-gear menu choice");
    }

    private static void TeammateAndReconnectKeepNativeEquipment()
    {
        var fixture = new Fixture(); var loading = fixture.Loading();
        ArenaPreviewFlow.ShowNativeLoading(loading.Screen);
        ArenaMode.Config.Settings.SelectedGameModeId = "zombie";
        ArenaMode.Config.Settings.OwnGear = true;
        fixture.Tick();
        ArenaMode.Config.Settings.OwnGear = false;
        fixture.Tick();
        Check(loading.Raw.enabled && loading.Screen.NativeUpdates == 0,
            "equipment refresh cannot replace explicitly selected teammate visual");
        Check(ArenaPreviewFlow.TryShowLoading(loading.Screen), "local click resumes provided-kit preview");
        ArenaMode.Config.Settings.OwnGear = true;
        Check(ArenaPreviewFlow.TryShowLoadingPlayer(loading.Screen, new RaidPlayer { AccountId = "local" }),
            "own-gear local click uses native local-profile rendering");
        Check(loading.Screen.NativeUpdates == 1 && loading.Raw.enabled,
            "own-gear local click requests native equipment exactly once");
        Check(!ArenaPreviewFlow.TryShowLoadingPlayer(loading.Screen, new RaidPlayer { AccountId = "someone else" }),
            "teammate click is never intercepted as local equipment");
        ArenaMode.Config.Settings.OwnGear = false;
        fixture.Tick();
        Check(!loading.Raw.enabled, "local own-gear click retains later equipment reconciliation");
        ArenaPreviewFlow.BeforeScreenClose(loading.Root.transform);
        loading.Controller.IsReconnecting = true;
        ArenaPreviewFlow.PrepareLoading(loading.Screen, loading.Controller);
        Check(!ArenaPreviewFlow.TryShowLoading(loading.Screen), "reconnect remains native");
        Check(!ArenaPreviewFlow.IsLocalLoadingPlayer(loading.Screen, new RaidPlayer { AccountId = "local" }),
            "reconnect does not capture local-player clicks");
        ArenaMode.Config.Settings.OwnGear = true;
        fixture.Tick();
        Check(loading.Screen.NativeUpdates == 1, "reconnect does not acquire a native refresh owner");
    }
}
