using UnityEngine;

namespace ArenaMode.Combat.Translocator;

internal static class ArenaTranslocatorTeleportPolicy
{
    internal const float ContactTolerance = .002f;
    internal const float ArrivalTolerance = .025f;
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z)
        && Finite(value.sqrMagnitude);
    internal static bool Exact(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    internal static bool Arrived(Vector3 actual, Vector3 requested) => Finite(actual) && Finite(requested)
        && (actual - requested).sqrMagnitude <= ArrivalTolerance * ArrivalTolerance;
}
