using System.Runtime.CompilerServices;

// Deliberate native boundaries: these doubles do not model movement, Unity
// component discovery or SAIN decisions. Only reference/null behavior needed by
// the production read-only reflection adapter is represented.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull && rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    public class Component : Object
    {
        public Component Attached;
        public Component GetComponent(Type type)
            => !ReferenceEquals(Attached, null) && type.IsInstanceOfType(Attached) ? Attached : null;
    }

    public class MonoBehaviour : Component { }
}

namespace EFT
{
    public sealed class Player : UnityEngine.MonoBehaviour { }
    public sealed class BotOwner : UnityEngine.MonoBehaviour
    {
        public Player GetPlayer { get; set; }
    }
}

namespace ArenaMode
{
    internal static class Plugin
    {
        internal static readonly TestLogger ModLogger = new();
    }
    internal sealed class TestLogger
    {
        internal readonly List<string> Warnings = new();
        public void LogWarning(string message) => Warnings.Add(message);
    }
}
