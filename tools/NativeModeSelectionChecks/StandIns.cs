using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;

        public static bool operator ==(Object left, Object right)
        {
            if (ReferenceEquals(left, null))
                return ReferenceEquals(right, null) || right.Destroyed;
            if (ReferenceEquals(right, null))
                return left.Destroyed;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => other is Object value && this == value;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        public static void Destroy(Object target)
        {
            if (target is not GameObject gameObject || gameObject == null)
                return;

            foreach (var child in gameObject.transform.Children.ToArray())
                Destroy(child.gameObject);
            gameObject.transform.SetParent(null, false);
            gameObject.Destroyed = true;
            foreach (var component in gameObject.Components)
                component.Destroyed = true;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
    }

    public class Transform : Component
    {
        private Transform _parent;
        internal readonly List<Transform> Children = new();

        public Transform parent => _parent;
        public int childCount => Children.Count;

        public void SetParent(Transform parent, bool worldPositionStays)
        {
            _parent?.Children.Remove(this);
            _parent = parent;
            _parent?.Children.Add(this);
        }

        public void SetAsLastSibling()
        {
            if (_parent == null)
                return;
            _parent.Children.Remove(this);
            _parent.Children.Add(this);
        }
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;
        public Vector2 offsetMin;
        public Vector2 offsetMax;
    }

    public sealed class GameObject : Object
    {
        internal readonly List<Component> Components = new();

        public GameObject(string name, params Type[] componentTypes)
        {
            this.name = name;
            var transformType = componentTypes.Contains(typeof(RectTransform))
                ? typeof(RectTransform)
                : typeof(Transform);
            transform = (Transform)AddComponent(transformType);
            foreach (var type in componentTypes)
            {
                if (type != typeof(RectTransform) && type != typeof(Transform))
                    AddComponent(type);
            }
        }

        public string name { get; }
        public Transform transform { get; }
        public bool activeSelf { get; private set; } = true;

        public T AddComponent<T>() where T : Component, new()
            => (T)AddComponent(typeof(T));

        private Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type);
            component.gameObject = this;
            Components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
            => Components.OfType<T>().FirstOrDefault(component => component != null);

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            void Visit(GameObject node)
            {
                if (node == null || (!includeInactive && !node.activeSelf))
                    return;
                result.AddRange(node.Components.OfType<T>().Where(component => component != null));
                foreach (var child in node.transform.Children)
                    Visit(child.gameObject);
            }
            Visit(this);
            return result.ToArray();
        }

        public void SetActive(bool value) => activeSelf = value;
    }

    public sealed class CanvasRenderer : Component { }

    public readonly struct Vector2
    {
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public readonly float x;
        public readonly float y;
        public static Vector2 zero => new(0f, 0f);
        public static Vector2 one => new(1f, 1f);
    }

    public readonly struct Color
    {
        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }
        public readonly float r;
        public readonly float g;
        public readonly float b;
        public readonly float a;
    }
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Component
    {
        public UnityEngine.Color color;
        public bool raycastTarget;
    }

    public sealed class Image : Graphic { }

    public struct ColorBlock
    {
        public UnityEngine.Color normalColor;
        public UnityEngine.Color highlightedColor;
        public UnityEngine.Color pressedColor;
        public UnityEngine.Color selectedColor;
        public UnityEngine.Color disabledColor;
        public float colorMultiplier;
        public float fadeDuration;
    }

    public sealed class Button : UnityEngine.Component
    {
        public sealed class ButtonClickedEvent
        {
            private event Action Clicked;
            public void AddListener(Action listener) => Clicked += listener;
            public void Invoke() => Clicked?.Invoke();
        }

        public bool interactable = true;
        public Graphic targetGraphic;
        public ColorBlock colors;
        public ButtonClickedEvent onClick { get; } = new();
    }
}

namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public enum FontStyles { Normal, Bold }
    public enum TextOverflowModes { Overflow }

    public sealed class TMP_FontAsset : UnityEngine.Object { }

    public class TMP_Text : UnityEngine.Component
    {
        public TMP_FontAsset font;
        public float fontSize;
        public TextAlignmentOptions alignment;
        public FontStyles fontStyle;
        public bool raycastTarget;
        public bool enableWordWrapping;
        public TextOverflowModes overflowMode;
        public UnityEngine.Color color;
        public string text;
        public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform;
    }

    public sealed class TextMeshProUGUI : TMP_Text { }

    public static class TMP_Settings
    {
        public static TMP_FontAsset defaultFontAsset = new();
    }
}
