using EFT;
using UnityEngine;

namespace ArenaMode.Combat.Akimbo;

/// <summary>Read-only native shot geometry, compiled separately against installed EFT in checks.</summary>
internal static class AkimboNativeAim
{
    internal static (Vector3 Muzzle, Vector3 Origin, Vector3 Direction, Transform Body) Read(
        Player body, Player.FirearmController hands)
    {
        Vector3 muzzle = hands.CurrentFireport.position, origin = muzzle, direction = hands.WeaponDirection;
        hands.AdjustShotVectors(ref origin, ref direction);
        // Player.transform is deliberately obsolete/error in EFT. The native
        // bifacial transform's Original is the actual physics/body hierarchy.
        return (muzzle, origin, direction, body.Transform?.Original);
    }
}
