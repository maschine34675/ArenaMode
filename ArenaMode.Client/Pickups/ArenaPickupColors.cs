using UnityEngine;

namespace ArenaMode.Pickups;

/// <summary>One palette for pickup beacons and the matching active bot effects.</summary>
internal static class ArenaPickupColors
{
    internal static Color DoubleDamage => new(1f, .25f, .7f);
    internal static Color Shield => new(.25f, .8f, 1f);
}
