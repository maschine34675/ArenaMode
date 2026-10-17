using System;
using EFT;
using Redeploy.Core.Spawning;
using UnityEngine;

namespace ArenaMode.Bots;

// This existing area suite models other maps. Equator's placement owner is an
// external seam; its own suite links the actual policy and runtime separately.
internal static class ArenaTeamBoxRuntime
{
    internal static bool IsRequired => false;
    internal static (Vector3 position, Quaternion rotation) PickRespawn(LocalGame game, Vector3 avoid)
        => throw new InvalidOperationException("Equator placement leaked into another map's fixture.");
    internal static bool TryPrepareRespawn(LocalGame game, ISpawnSelector original, out ISpawnSelector prepared)
        => throw new InvalidOperationException("Equator preparation leaked into another map's fixture.");
}
