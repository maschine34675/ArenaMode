using EFT.Interactive;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Remembers which weapon object belongs to which corpse, while that link still
/// exists.
///
/// A corpse holds its weapon through the ragdoll, but the ragdoll DESTROYS the
/// weapon's rigidbody at the end of its working cycle — and that cycle ends once
/// the body is no longer visible. From then on the corpse cannot name its own
/// weapon any more, and the weapon just hangs wherever the hand was, with its
/// physics gone.
///
/// The bitter part: hiding a body is itself what makes it invisible, so the
/// corpse trim was accelerating exactly the condition that took the weapon out
/// of its reach. Hence this registry — filled by the light sweep, which visits
/// every corpse within a second of its death, long before the link is cut.
/// </summary>
internal static class CorpseWeaponRegistry
{
    private static readonly Dictionary<Corpse, GameObject> Weapons = new Dictionary<Corpse, GameObject>();

    public static void Reset() => Weapons.Clear();

    /// <summary>Cheap and idempotent — safe to call on every sweep.</summary>
    public static void Observe(Corpse corpse)
    {
        if (corpse == null || Weapons.ContainsKey(corpse))
            return;

        var rigidbody = corpse.Ragdoll?.WeaponRigidbody;
        if (rigidbody == null)
            return;

        Weapons[corpse] = rigidbody.gameObject;
    }

    /// <summary>The weapon object, or null once it is gone for good.</summary>
    public static GameObject Get(Corpse corpse)
    {
        if (corpse == null)
            return null;

        // The live link first — it is authoritative while it lasts.
        var rigidbody = corpse.Ragdoll?.WeaponRigidbody;
        if (rigidbody != null)
            return rigidbody.gameObject;

        if (!Weapons.TryGetValue(corpse, out var remembered))
            return null;

        // Unity lifetime check: the object may have been destroyed since.
        return remembered == null ? null : remembered;
    }
}
