// External UI surfaces only. The production notice is compiled unchanged;
// this verifies callback ownership and admission, not native rendering.
namespace UnityEngine
{
    public class Object { public static void Destroy(GameObject go) { go.SetActive(false); go.transform.SetParent(null, false); } }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : Component => gameObject.GetComponentsInChildren<T>(true).FirstOrDefault();
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public Transform parent;
        public Vector3 localPosition;
        public readonly List<Transform> Children = new();
        public void SetParent(Transform next, bool world) { parent?.Children.Remove(this); parent = next; parent?.Children.Add(this); }
        public void SetAsLastSibling() { }
        public Vector3 TransformPoint(Vector2 value) => new(value.x, value.y, 0);
        public Vector3 InverseTransformPoint(Vector3 value) => value;
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition, offsetMin, offsetMax;
        public Rect rect => new();
    }
    public readonly record struct Rect { public Vector2 center => new(); }
    public readonly record struct Vector2(float x, float y) { public static Vector2 zero => new(); public static Vector2 one => new(1, 1); }
    public readonly record struct Vector3(float x, float y, float z);
    public readonly record struct Color(float r, float g, float b, float a) { public static Color white => new(1, 1, 1, 1); }
    public static class Time { public static float realtimeSinceStartup; }
    public class GameObject : Object
    {
        public string name;
        public Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        private readonly List<Component> _components = new();
        public GameObject(string label, params Type[] types)
        {
            name = label;
            transform = types.Contains(typeof(RectTransform)) ? new RectTransform() : new Transform();
            transform.gameObject = this; _components.Add(transform);
            foreach (var type in types.Where(t => !typeof(Transform).IsAssignableFrom(t)))
            { var item = (Component)Activator.CreateInstance(type); item.gameObject = this; _components.Add(item); }
        }
        public void SetActive(bool value) => activeSelf = value;
        public T AddComponent<T>() where T : Component, new() { var item = new T { gameObject = this }; _components.Add(item); return item; }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component => _components.OfType<T>()
            .Concat(transform.Children.SelectMany(t => t.gameObject.GetComponentsInChildren<T>(inactive))).ToArray();
    }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component { public UnityEngine.Color color; public bool raycastTarget; }
    public struct ColorBlock { public UnityEngine.Color normalColor, highlightedColor, pressedColor; }
    public class Button : UnityEngine.Component
    {
        public bool interactable = true;
        public Image targetGraphic;
        public ColorBlock colors;
        public readonly ButtonEvent onClick = new();
        public class ButtonEvent
        {
            private readonly List<Action> _callbacks = new();
            public void AddListener(Action callback) => _callbacks.Add(callback);
            public void RemoveAllListeners() => _callbacks.Clear();
            public Action[] Snapshot() => _callbacks.ToArray();
            public void Invoke() { foreach (var callback in Snapshot()) callback(); }
        }
    }
}
namespace TMPro
{
    public enum FontStyles { Normal, Bold }
    public enum TextAlignmentOptions { TopLeft, Center }
    public class TextMeshProUGUI : UnityEngine.Component
    {
        public object font, fontSharedMaterial;
        public float fontSize;
        public FontStyles fontStyle;
        public UnityEngine.Color color;
        public string text;
        public TextAlignmentOptions alignment;
        public bool enableWordWrapping, richText, raycastTarget;
        public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform;
        public UnityEngine.Vector2 GetPreferredValues(string value, float width, float height) => new(width, 50f);
    }
}
namespace EFT.UI
{
    public class HoverTooltipArea : UnityEngine.Component
    {
        public string Message;
        public bool RawText;
        public void SetMessageText(string text, bool rawText) { Message = text; RawText = rawText; }
    }
}
namespace EFT.UI.Matchmaker
{
    public class MatchMakerSideSelectionScreen : UnityEngine.Component
    {
        public TMPro.TextMeshProUGUI _sideDescriptionText;
        public UnityEngine.Component _randomButton;
    }
}
namespace ArenaMode
{
    internal static class Plugin { internal static readonly Logger ModLogger = new(); }
    internal class Logger { internal void LogWarning(string text) => throw new Exception(text); }
}
namespace ArenaMode.Session
{
    // The actual two-click timing is covered by ArenaSessionClientChecks.
    internal sealed class ArenaTwoClickGate
    {
        internal const float WindowSeconds = 3f;
        private bool _armed;
        internal bool Click(float now) { bool confirmed = _armed; _armed = !confirmed; return confirmed; }
        internal bool IsArmed(float now) => _armed;
        internal void Reset() => _armed = false;
    }
}
