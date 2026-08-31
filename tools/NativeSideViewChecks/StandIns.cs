using System.Collections.Generic;
using System.Linq;

// Minimal external dependencies, not copies of EFT's implementation. The actual
// production view helper is linked into this executable by the project file.
namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) => value != null;
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public sealed class Transform : Component
    {
        private Transform _parent;
        private readonly List<Transform> _children = new();
        public Transform parent
        {
            get => _parent;
            set
            {
                _parent?._children.Remove(this);
                _parent = value;
                _parent?._children.Add(this);
            }
        }
        public int childCount => _children.Count;
        public Transform GetChild(int index) => _children[index];
        public bool IsChildOf(Transform possibleParent)
            => this == possibleParent || (parent != null && parent.IsChildOf(possibleParent));
    }

    public sealed class GameObject : Object
    {
        private readonly List<Component> _components = new();
        public string name;
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf
            && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public Transform transform { get; }

        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            _components.Add(transform);
        }

        public void SetActive(bool active) => activeSelf = active;
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();
    }

    public sealed class CanvasGroup : Behaviour
    {
        public float alpha = 1f;
        public bool interactable = true;
        public bool blocksRaycasts = true;
        public bool ignoreParentGroups;
    }
}

namespace EFT.UI
{
    using UnityEngine;

    public sealed class HoverTooltipArea : Behaviour
    {
        public string _message;
    }

    public sealed class AnimatedToggle : Behaviour
    {
        public bool interactable;
        public object group;
    }

    public sealed class UIAnimatedToggleSpawner : Component
    {
        public CanvasGroup _canvasGroup;
        public string _headerCaption = "RANDOM";
        public int _headerFontSize = 26;
        public bool _unavailable = true;
        public object _toggleGroup = new();
        private AnimatedToggle _spawned;
        public int SpawnCount { get; private set; }

        public AnimatedToggle SpawnedObject
        {
            get
            {
                if (_spawned == null)
                {
                    var child = new GameObject("Spawned toggle");
                    child.transform.parent = transform;
                    _spawned = child.AddComponent<AnimatedToggle>();
                    _spawned.interactable = !_unavailable;
                    _spawned.group = _toggleGroup;
                    SpawnCount++;
                }
                return _spawned;
            }
        }

        public void vmethod_0(string caption, int size)
        {
            _headerCaption = caption;
            _ = SpawnedObject;
        }
    }
}
