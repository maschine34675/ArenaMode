// These are deterministic boundary doubles, not an emulation or execution of Unity.
// A host's deactivation callback is deliberately marked hierarchy-mutation-locked.
// Any forbidden attempt is counted before throwing, even if production Safe swallows it.
using System.Reflection;
using ArenaMode.Session.Preview;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
            => (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed)
               || ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void DontDestroyOnLoad(Object _) { }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : Component
        {
            for (var cursor = transform; cursor != null; cursor = cursor.parent)
                if (cursor.GetComponent<T>() is T value) return value;
            return null;
        }
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public Transform parent;
        public readonly List<Transform> Children = new();
        public void SetParent(Transform next, bool _)
        {
            Lifecycle.Mutate("SetParent");
            parent?.Children.Remove(this);
            parent = next;
            next?.Children.Add(this);
        }
        public bool IsChildOf(Transform ancestor)
        {
            for (var cursor = this; cursor != null; cursor = cursor.parent)
                if (ReferenceEquals(cursor, ancestor)) return true;
            return false;
        }
    }
    public class RectTransform : Transform { }
    public class GameObject : Object
    {
        public readonly Transform transform;
        internal readonly List<Component> Components = new();
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name = "") { transform = new RectTransform { gameObject = this }; Components.Add(transform); }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(x => !x.Destroyed);
        public void SetActive(bool value)
        {
            Lifecycle.Mutate("SetActive");
            bool was = activeInHierarchy;
            activeSelf = value;
            if (was && !activeInHierarchy) Lifecycle.FireSubtree(this, "OnDisable");
        }
        public void DestroyHost()
        {
            SetActive(false);
            Lifecycle.FireSubtree(this, "OnDestroy");
            MarkDestroyed(this);
        }
        private static void MarkDestroyed(GameObject obj)
        {
            obj.Destroyed = true;
            foreach (var component in obj.Components) component.Destroyed = true;
            foreach (var child in obj.transform.Children.ToArray()) MarkDestroyed(child.gameObject);
        }
    }
    public sealed class Camera : MonoBehaviour { }
    public static class Lifecycle
    {
        public static int ForbiddenAttempts;
        public static int TotalMutations;
        private static int _locked;
        public static void ResetCounts() { ForbiddenAttempts = 0; TotalMutations = 0; }
        public static void Mutate(string operation)
        {
            ++TotalMutations;
            if (_locked > 0) { ++ForbiddenAttempts; throw new InvalidOperationException("SIMULATED_UNITY_MUTATION_LOCK: " + operation); }
        }
        public static void Invoke(Component component, string method)
        {
            try { component.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }
        internal static void FireSubtree(GameObject root, string callback)
        {
            ++_locked;
            try { Fire(root, callback); }
            finally { --_locked; }
        }
        private static void Fire(GameObject root, string callback)
        {
            foreach (var component in root.Components.ToArray()) Invoke(component, callback);
            foreach (var child in root.transform.Children.ToArray()) Fire(child.gameObject, callback);
        }
    }
}
namespace UnityEngine.UI
{
    public class Image : Component { public bool enabled = true; }
    public sealed class RawImage : Image { public RectTransform rectTransform => (RectTransform)transform; }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { public Delta delta = new(); }
    public class Delta { public float x; }
}
namespace EFT
{
    public sealed class RaidSettings { }
    public sealed class RaidPlayer { public string AccountId; }
    public sealed class Profile { public string AccountId = "local"; }
}
namespace EFT.UI
{
    public sealed class CameraImage : Component
    {
        public bool enabled = true;
        public int Recreated;
        public int NullCameraCalls;
        public void RecreateTexture() => ++Recreated;
        public void InitCamera(Camera camera) { if (camera == null) ++NullCameraCalls; }
    }
}
namespace EFT.UI.Matchmaker
{
    public class MatchMakerSideSelectionScreen : MonoBehaviour { }
    public class MatchMakerAcceptScreen : MonoBehaviour { public MatchMakerPlayerPreview _playerModelView; }
    public class MatchMakerPlayerPreview : MonoBehaviour
    {
        public bool _isMainCharacter = true;
        public UnityEngine.UI.RawImage _playerVisualImage;
        public UnityEngine.UI.Image _noVisualImage;
        public int NativeUpdates;
        public void UpdateVisual() => ++NativeUpdates;
    }
    public class MatchmakerTimeHasCome : MonoBehaviour
    {
        public Component _playerModelView;
        public readonly Rotator _rotator = new();
        public readonly DragTrigger _dragTrigger = new();
        public int NativeUpdates;
        public Task ShowPlayerModel() { ++NativeUpdates; return Task.CompletedTask; }
        public sealed class TimeHasComeScreenController
        {
            public bool IsReconnecting;
            public RaidSettings RaidSettings;
            public readonly FakeSession Session = new();
        }
    }
    public class FakeSession { public readonly Profile Profile = new(); }
    public class Rotator { public float _rotationSpeed; }
    public class DragTrigger
    {
        public event Action<UnityEngine.EventSystems.PointerEventData> onDrag;
        public void Fire() => onDrag?.Invoke(new());
    }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly Log ModLogger = new(); }
    public sealed class Log { public readonly List<string> Warnings = new(); public void LogWarning(object text) => Warnings.Add(text.ToString()); }
}
namespace ArenaMode.Session
{
    public static class ArenaMenuSelection
    {
        public static EFT.RaidSettings Selected;
        public static Eligibility CurrentEligibility = new();
        public sealed class Eligibility { public bool Allowed = true; public bool IsTeam; }
        public static bool IsSelectedFor(EFT.RaidSettings settings) => settings != null && ReferenceEquals(settings, Selected);
    }
    public static class ArenaSession
    {
        public static bool IsActive;
        public static bool ArmedForNextRaid;
        public static int ArmEpoch;
        public static string ArmedMode;
        public static bool ZombieOwnGearSelection;
        public static bool ZombieOwnGearArmed => Modes.ZombieOwnGearGate.IsSelectedFor(ArmedMode, ZombieOwnGearSelection);
    }
    internal static class ArenaSideSelectionLayout
    {
        public static ArenaCharacterPreview Pending;
        public static ArenaCharacterPreview DetachPreview(EFT.UI.Matchmaker.MatchMakerSideSelectionScreen _) { var result = Pending; Pending = null; return result; }
    }
    public sealed class ArenaPreviewLifetime : MonoBehaviour { public Action OnHidden; }
    internal sealed class ReentrantClose : MonoBehaviour
    {
        private void OnDisable() => ArenaPreviewFlow.BeforeScreenClose(transform);
    }
    // Structural members are generated from production, not copied here. Creation,
    // ShowAt and release are explicit test boundaries; actual state owns release timing.
    internal sealed partial class ArenaCharacterPreview : IDisposable
    {
        private GameObject _world = new("test world");
        private GameObject _view = new("test view");
        private EFT.UI.CameraImage _image;
        private ArenaPreviewLifetime _viewLifetime;
        private readonly ArenaPreviewLifetimeState _lifetime = new();
        private Action _onUnavailable;
        public int Shows;
        public int Releases;
        public int Rotations;
        public GameObject View => _view;
        public GameObject World => _world;
        public ArenaPreviewFlowGuard Guard => _world?.GetComponent<ArenaPreviewFlowGuard>();
        public ArenaCharacterPreview(bool loading = false)
        {
            _image = _view.AddComponent<EFT.UI.CameraImage>();
            _viewLifetime = _view.AddComponent<ArenaPreviewLifetime>();
            if (loading) _lifetime.TryBeginLoad();
        }
        public void ShowAt(Transform parent, RectTransform nativeImage = null)
        {
            if (!IsAvailable || parent == null) throw new InvalidOperationException("Test ShowAt unavailable");
            ++Shows;
            Suspend();
            _view.transform.SetParent(nativeImage ?? parent, false);
            _view.SetActive(true);
        }
        public void Rotate(float delta, float speed) => ++Rotations;
        public void Dispose() { if (_lifetime.DisposeAndClaimRelease()) ++Releases; }
        public void FinishLoad() { if (_lifetime.FinishLoadAndClaimRelease()) ++Releases; }
        public void RemoveViewBoundary() { _view.DestroyHost(); }
        public void RemoveWorldBoundary() { _world.DestroyHost(); }
    }
}
namespace ArenaMode.Modes
{
    internal static class ArenaModeCatalog { public const string TacticalId = "tactical"; }
}
namespace ArenaMode.Shared
{
    internal static class ArenaPopulationPolicy { public const string Zombie = "zombie"; }
}
namespace ArenaMode.Config
{
    internal static class Settings
    {
        public static string SelectedGameModeId = "deathmatch";
        public static bool OwnGear;
        public static bool GetEquipmentChoiceOwnGear(string mode) => OwnGear;
    }
}
namespace ArenaMode.Compat
{
    internal static class PitFireTeamSelectionPolicy
    {
        public static bool SupportsMode(string mode)
            => string.Equals(mode, "tactical", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "zombie", StringComparison.OrdinalIgnoreCase);
    }
    internal static class PitFireTeamCompat { public static bool HasFrozenTeamRoster; }
}
