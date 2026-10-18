using UnityEngine;

namespace ArenaMode.Combat.Akimbo;

/// <summary>One geometry contract for the paid left shot and its aiming aid.</summary>
internal static class AkimboShotGeometry
{
    internal static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x)
        || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));

    internal static bool TryLeft(Vector3 camera, Vector3 nativeOrigin, Vector3 nativeDirection,
        Vector3 nativeMuzzle, Vector3 leftMuzzle, out Vector3 origin, out Vector3 direction)
    {
        Vector3 delta = leftMuzzle - nativeMuzzle;
        origin = nativeOrigin + delta;
        direction = nativeDirection;
        int mask = (int)LayersMaskController.HighPolyWithTerrainMask;
        bool clear = Finite(camera) && Finite(origin) && Finite(nativeOrigin) && Finite(nativeDirection)
            && Finite(leftMuzzle) && nativeDirection.sqrMagnitude > .9f && delta.sqrMagnitude <= .65f * .65f
            && !Physics.Linecast(nativeOrigin, origin, mask, QueryTriggerInteraction.Ignore)
            && !Physics.Linecast(camera, origin, mask, QueryTriggerInteraction.Ignore)
            && !Physics.Linecast(origin, leftMuzzle, mask, QueryTriggerInteraction.Ignore)
            && !Physics.CheckSphere(origin, .025f, mask, QueryTriggerInteraction.Ignore)
            && !Physics.CheckSphere(leftMuzzle, .025f, mask, QueryTriggerInteraction.Ignore);
        if (!clear) return false;
        direction = (nativeOrigin + nativeDirection.normalized * 50f - origin).normalized;
        return true;
    }
}
