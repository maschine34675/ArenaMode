// Minimal native API surfaces only. The status and request implementations are
// linked production sources; this is not a Unity camera or live-raid simulator.
public enum EBodyPart { Common, Head }

namespace EFT
{
    public sealed class Player
    {
        public string ProfileId { get; set; }
        public bool IsYourPlayer { get; set; }
        public bool Destroyed { get; set; }
        public UnityEngine.GameObject gameObject { get; set; } = new();
        public HealthSystem.IHealthController HealthController { get; set; }

        public static bool operator ==(Player left, Player right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull && rightNull : ReferenceEquals(left, right);
        }

        public static bool operator !=(Player left, Player right) => !(left == right);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}

namespace EFT.HealthSystem
{
    public struct ValueStruct
    {
        public float Current;
        public float Maximum;
    }

    public interface IHealthController
    {
        bool IsAlive { get; }
        ValueStruct GetBodyPartHealth(EBodyPart bodyPart, bool rounded = false);
    }
}

namespace UnityEngine
{
    public sealed class GameObject
    {
        public bool activeInHierarchy = true;
    }
}
