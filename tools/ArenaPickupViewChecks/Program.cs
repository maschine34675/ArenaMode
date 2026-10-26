using ArenaMode.Pickups;
using EFT.CameraControl;
using TMPro;
using UnityEngine;

// Runs the real ArenaPickupView constructor and Tick against boundary doubles. What it pins:
// the TMP label is created while the marker root is still active (native Awake must have run
// before the font is configured), the finished marker is hidden until Tick has seen a camera, and
// a constructor that fails after the active label exists destroys its whole private tree.
internal static class Program
{
    private static int _checks;

    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception("FAIL: " + label);
    }

    private static void Reset()
    {
        GameObject.All.Clear();
        Material.All.Clear();
        CameraManager.Instance = null;
        TextMeshPro.FailText = false;
        Time.time = Time.unscaledTime = 0f;
        ArenaMode.Modes.ArenaMatchService.Current = null;
    }

    private static GameObject Root(string id) => GameObject.All.Single(g => g.name == "Arena pickup marker " + id);

    private static ArenaPickupView Build(ArenaPickupPoint point)
    {
        // The label double throws a NullReferenceException when its font is set before native Awake.
        try { return new ArenaPickupView(point, new Vector3(0, 0, 0)); }
        catch (NullReferenceException ex)
        { throw new Exception("FAIL: native TMP awakens before font configuration", ex); }
    }

    private static void Tick(ArenaPickupView view, ArenaPickupPhase phase, string unknownCameraLabel = null)
    {
        try { view.Tick(new ArenaPickupState { Phase = phase }, false, null); }
        catch (NullReferenceException ex) when (unknownCameraLabel != null)
        { throw new Exception("FAIL: " + unknownCameraLabel, ex); }
    }

    private static void Main()
    {
        // Every kind builds a label; the weapon marker has no symbol, the others do.
        foreach (var kind in Enum.GetValues<ArenaPickupKind>())
        {
            Reset();
            var view = Build(new ArenaPickupPoint { Id = "kind-" + kind, Kind = kind });
            var root = Root("kind-" + kind);
            var label = root.GetComponentsInChildren<TextMeshPro>(true).Single();
            Check(label.Awakened, "native TMP cache initialized at marker construction");
            Check(label.font != null && !string.IsNullOrEmpty(label.text), "label is configured when the constructor returns");
            Check(!root.activeSelf, "constructed marker stays hidden until Tick has checked a camera");
            view.Dispose();
            Check(root.Destroyed, "dispose destroys the private marker tree");
        }

        // Tick owns visibility: no camera, a far camera, a retired marker and a near camera.
        {
            Reset();
            var view = Build(new ArenaPickupPoint { Id = "tick", Kind = ArenaPickupKind.Shield });
            var root = Root("tick");
            Tick(view, ArenaPickupPhase.Available, "unknown camera keeps the marker hidden");
            Check(!root.activeSelf, "unknown camera keeps the marker hidden");

            var camera = new GameObject("camera").AddComponent<Camera>();
            CameraManager.Instance = new CameraManager { Camera = camera };
            camera.transform.position = new Vector3(0, 0, 5);
            Tick(view, ArenaPickupPhase.Available);
            Check(root.activeSelf, "a near camera shows the marker");
            camera.transform.position = new Vector3(100, 0, 0);
            Tick(view, ArenaPickupPhase.Available);
            Check(!root.activeSelf, "a camera beyond 80 m hides the marker");
            camera.transform.position = new Vector3(0, 0, 5);
            Tick(view, ArenaPickupPhase.Retired);
            Check(!root.activeSelf, "a retired marker stays hidden");
            view.Dispose();
        }

        // Only the tall beacon yields near a live control point; pickups remain identifiable.
        {
            Reset();
            var floor = new Vector3(7, 2, -5);
            var view = new ArenaPickupView(new ArenaPickupPoint { Id = "objective", Kind = ArenaPickupKind.Shield }, floor);
            var camera = new GameObject("camera").AddComponent<Camera>();
            CameraManager.Instance = new CameraManager { Camera = camera };
            var beacon = GameObject.All.Single(g => g.name == "Pickup beacon");
            var symbol = GameObject.All.Single(g => g.name == "Pickup symbol");
            var ring = GameObject.All.Single(g => g.name == "Pickup ring");
            var label = GameObject.All.Single(g => g.name == "Pickup label");
            Tick(view, ArenaPickupPhase.Available);
            Check(beacon.activeInHierarchy, "ordinary pickups keep their beacon");
            var mode = new ArenaMode.Modes.TeamDeathmatchMode();
            ArenaMode.Modes.ArenaMatchService.Current = new() { Mode = mode };
            Tick(view, ArenaPickupPhase.Available);
            Check(beacon.activeInHierarchy, "team mode without a nearby objective keeps the beacon");
            mode.NearControlPoint = true;
            Tick(view, ArenaPickupPhase.Available);
            Check(mode.LastFloor == floor, "objective proximity uses the pickup floor");
            Check(!beacon.activeInHierarchy, "nearby objective suppresses the tall beacon");
            Check(symbol.activeInHierarchy && ring.activeInHierarchy && label.activeInHierarchy,
                "objective overlap preserves pickup symbol ring and label");
            mode.NearControlPoint = false;
            Tick(view, ArenaPickupPhase.Available);
            Check(beacon.activeInHierarchy, "leaving objective context restores the beacon");
            Tick(view, ArenaPickupPhase.Cooldown);
            Check(!beacon.activeInHierarchy && !symbol.activeInHierarchy, "unavailable pickup stays hidden outside objectives");
            view.Dispose();
        }

        // A failure after the active label exists must not leave the tree or the materials alive.
        {
            Reset();
            var foreign = new GameObject("unrelated pooled model") { layer = 7 };
            int materialsBefore = Material.All.Count;
            TextMeshPro.FailText = true;
            bool failed = false;
            try { new ArenaPickupView(new ArenaPickupPoint { Id = "late-fault", Kind = ArenaPickupKind.Shield }, new Vector3(0, 0, 0)); }
            catch (InvalidOperationException ex) when (ex.Message == "injected late label failure") { failed = true; }
            finally { TextMeshPro.FailText = false; }
            Check(failed, "a late constructor error reaches the caller");
            var failedRoot = Root("late-fault");
            var tree = failedRoot.GetComponentsInChildren<Transform>(true);
            Check(tree.Length > 5, "the failed constructor had built its marker tree");
            Check(failedRoot.Destroyed && !failedRoot.activeSelf && tree.All(t => t.gameObject.Destroyed),
                "failed constructor hides and destroys its complete private tree");
            Check(Material.All.Count > materialsBefore && Material.All.Skip(materialsBefore).All(m => m.Destroyed),
                "failed constructor releases private materials");
            Check(!foreign.Destroyed && foreign.layer == 7, "failed constructor leaves unrelated objects alone");
        }

        Console.WriteLine($"ARENA_PICKUP_VIEW_BASELINE_PASSED checks={_checks}");
    }
}
