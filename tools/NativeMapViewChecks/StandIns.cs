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
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null) || right.Destroyed;
            if (ReferenceEquals(right, null)) return left.Destroyed;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => other is Object value && this == value;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        public static void Destroy(GameObject target)
        {
            if (target == null) return;
            foreach (var child in target.transform.Children.ToArray()) Destroy(child.gameObject);
            target.transform.parent = null;
            target.Destroyed = true;
            foreach (var component in target.Components) component.Destroyed = true;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
            => gameObject.GetComponentsInChildren<T>(includeInactive);
    }

    public sealed class Transform : Component
    {
        private Transform _parent;
        internal readonly List<Transform> Children = new();

        public Transform parent
        {
            get => _parent;
            set
            {
                _parent?.Children.Remove(this);
                _parent = value;
                _parent?.Children.Add(this);
            }
        }
    }

    public sealed class GameObject : Object
    {
        internal readonly List<Component> Components = new();
        public readonly string name;
        public Transform transform { get; }
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public int SetActiveCalls { get; private set; }

        public GameObject(string name)
        {
            this.name = name;
            transform = AddComponent<Transform>();
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }

        public void SetActive(bool active)
        {
            if (Destroyed) throw new InvalidOperationException("Attempted to activate a destroyed Unity object: " + name);
            SetActiveCalls++;
            activeSelf = active;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            void Visit(GameObject node, bool isRoot)
            {
                if (node.Destroyed || (!isRoot && !includeInactive && !node.activeInHierarchy)) return;
                result.AddRange(node.Components.OfType<T>().Where(component => component != null));
                foreach (var child in node.transform.Children) Visit(child.gameObject, false);
            }
            Visit(this, true);
            return result.ToArray();
        }
    }
}

namespace EFT.UI.Matchmaker
{
    public sealed class LocationPath : UnityEngine.Component { }
}
