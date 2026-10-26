using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Pickups;

/// <summary>Separate a pickup's one-metre collection area and marker from objective space.</summary>
internal static class ArenaObjectivePickupSpacing
{
    internal const float HorizontalClearance = 2f;
    internal const float VerticalClearance = 1f;

    internal static bool Allows(Vector3 floor, IReadOnlyList<Vector3> centers, float radius, float heightTolerance)
    {
        if (!Finite(floor) || centers == null || centers.Count == 0 || !Finite(radius) || radius <= 0
            || !Finite(heightTolerance) || heightTolerance < 0) return false;
        float distance = radius + HorizontalClearance;
        foreach (var center in centers)
        {
            if (!Finite(center)) return false;
            var delta = floor - center;
            if (Math.Abs(delta.y) <= heightTolerance + VerticalClearance
                && delta.x * delta.x + delta.z * delta.z <= distance * distance) return false;
        }
        return true;
    }

    private static bool Finite(Vector3 point) => Finite(point.x) && Finite(point.y) && Finite(point.z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
