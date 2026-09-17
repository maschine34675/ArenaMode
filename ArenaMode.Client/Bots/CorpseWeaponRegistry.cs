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
///
/// A remembered object is NEVER trusted on its own. Weapon objects are pooled:
/// once someone loots the weapon it goes back into the pool and can be handed
/// straight to another bot — or to the player, who would then be carrying an
/// invisible gun. Every lookup therefore has to prove the corpse still owns the
/// thing before anyone is allowed to touch it.
/// </summary>
internal static class CorpseWeaponRegistry
{
    private sealed class Entry
    {
        public GameObject Weapon;
        public string ItemId;
    }

    private static readonly Dictionary<Corpse, Entry> Weapons = new Dictionary<Corpse, Entry>();

    public static void Reset() => Weapons.Clear();

    /// <summary>
    /// Drops a body Arena has released. Reference-null on purpose: a DESTROYED
    /// corpse compares equal to null under Unity's operator, so the ordinary
    /// `corpse != null` guard would skip exactly the key that has to go — and
    /// the entry keeps the whole destroyed corpse and its weapon object alive
    /// for the rest of the raid.
    /// </summary>
    public static void Forget(Corpse corpse)
    {
        if (!ReferenceEquals(corpse, null))
            Weapons.Remove(corpse);
    }

    /// <summary>Cheap and idempotent — safe to call on every sweep.</summary>
    public static void Observe(Corpse corpse)
    {
        if (corpse == null || Weapons.ContainsKey(corpse))
            return;

        var rigidbody = corpse.Ragdoll?.WeaponRigidbody;
        if (rigidbody == null)
            return;

        Weapons[corpse] = new Entry
        {
            Weapon = rigidbody.gameObject,
            ItemId = corpse.ItemInHands?.Value?.Id,
        };
    }

    /// <summary>
    /// The weapon object while it still belongs to this corpse — null once it
    /// has been looted, destroyed, or recycled into somebody else's hands.
    /// </summary>
    public static GameObject Get(Corpse corpse)
    {
        if (corpse == null)
            return null;

        // Looting nulls the held item, which is the earliest and cleanest signal
        // that the weapon is no longer the corpse's to hide.
        var heldId = corpse.ItemInHands?.Value?.Id;
        if (heldId == null)
            return null;

        // The live link first — authoritative while it lasts.
        var rigidbody = corpse.Ragdoll?.WeaponRigidbody;
        if (rigidbody != null)
            return rigidbody.gameObject;

        if (!Weapons.TryGetValue(corpse, out var entry) || entry.Weapon == null)
            return null;

        // The remembered object must still carry the very item this corpse
        // reports holding; a pooled rig handed to someone else carries another.
        if (entry.ItemId != null && entry.ItemId != heldId)
            return null;

        var loot = entry.Weapon.GetComponent<LootItem>();
        if (loot != null && loot.Item != null && loot.Item.Id != heldId)
            return null;

        return entry.Weapon;
    }
}
