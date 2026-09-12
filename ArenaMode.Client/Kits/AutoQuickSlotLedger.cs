#nullable disable
using System;
using System.Collections.Generic;

namespace ArenaMode.Kits;

/// <summary>
/// The memory behind AutoQuickSlots: which keys the service filled, and which
/// keys the player emptied on purpose. Pure so the tests can drive it with
/// plain ids; the Unity-facing half — the poll, the bind calls, the item
/// classification — lives in AutoQuickSlots.
///
/// The service owns KEYS, not items. Per owned key it remembers the id of the
/// item sitting on it, for exactly one purpose: when the key goes empty, to
/// tell "the player took the item off the key" (the item is still on the
/// body) from "the item left the body" (used up, dropped, or replaced along
/// with the kit). The first is a deliberate clear and the key is left alone
/// for the rest of the raid; the second says nothing about the key.
///
/// Two rules here were learned from the protected 5v5 raid, where they were
/// visible as an asymmetry: a key holding a medical item from the rig
/// refilled itself after the player cleared it, while a key the player had
/// filled themselves from a protected special pocket slot stayed cleared.
/// TeamArenaLoadoutSnapshot rebuilds the rig, pockets and backpack between
/// rounds from JSON with FRESH ids and carries the quick-slot bindings over
/// to the new instances; the protected slots are transferred as the same
/// objects, and a key the player filled was never this service's to refill.
/// A ledger that remembered the objects it had bound then judged a later
/// clear against instances that were on no body any more — "item gone", not
/// a clear — and refilled the key within the second.
///
/// So, first: ids rather than object references, and an occupied key ADOPTS
/// whatever sits on it now. The key is still the service's; the thing to
/// judge a later clear against is its current occupant, whether the rebuild
/// put it there under a fresh id or the player put a different item there
/// by hand. Second: a reissue that announces itself (the rebuild knows its
/// previous-to-next id map) settles the ledger against the dying body first
/// — the poll stops at the death, and the last second before it is still
/// unjudged — and then remaps the remembered ids exactly, so the keys still
/// owned are owned under their new ids from the first poll on the new body.
///
/// Neither rule changes a solo raid, where the body is replaced with a new
/// kit and every binding with it: no key is occupied afterwards, no id maps
/// to anything, and a key that emptied with the body is not a clear, as
/// before.
/// </summary>
internal sealed class AutoQuickSlotLedger<TSlot> where TSlot : struct
{
    /// <summary>Owned keys, each with the id of the item sitting on it as last seen.</summary>
    private readonly Dictionary<TSlot, string> _occupants = new Dictionary<TSlot, string>();

    /// <summary>
    /// Keys the player emptied by hand. Left alone for the rest of the raid.
    ///
    /// Raid-scoped rather than per-life on purpose: the items are new
    /// instances every life, but "I want this key clear" is about the KEY,
    /// and re-asking the player to say so after every death is the chore the
    /// service exists to remove.
    /// </summary>
    private readonly HashSet<TSlot> _clearedByPlayer = new HashSet<TSlot>();

    public int OwnedKeyCount => _occupants.Count;

    public int ClearedKeyCount => _clearedByPlayer.Count;

    public bool IsOwned(TSlot slot) => _occupants.ContainsKey(slot);

    public bool IsClearedByPlayer(TSlot slot) => _clearedByPlayer.Contains(slot);

    /// <summary>The id the ledger currently holds for an owned key; null when the key is not owned.</summary>
    public string OccupantOf(TSlot slot)
        => _occupants.TryGetValue(slot, out var id) ? id : null;

    public void Reset()
    {
        _occupants.Clear();
        _clearedByPlayer.Clear();
    }

    /// <summary>
    /// Records a binding the service just made. An item without an id cannot
    /// be told apart from anything and is not watched; every EFT item carries
    /// one, so this is a guard, not a path.
    /// </summary>
    public void NoteBound(TSlot slot, string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return;

        _occupants[slot] = itemId;
    }

    /// <summary>
    /// One poll. Call before deciding which keys are free.
    /// </summary>
    /// <param name="boundNow">Every key that holds a binding right now, with the id of the item on it.</param>
    /// <param name="onBody">The ids of everything the player carries.</param>
    public void Observe(IReadOnlyDictionary<TSlot, string> boundNow, ICollection<string> onBody)
    {
        if (_occupants.Count == 0)
            return;

        List<TSlot> emptied = null;
        List<KeyValuePair<TSlot, string>> adopted = null;

        foreach (var pair in _occupants)
        {
            if (boundNow != null && boundNow.TryGetValue(pair.Key, out var current))
            {
                // Still occupied, so still ours. A different item under the key
                // — the rebuild's fresh instance, or one the player put there —
                // is what a later clear is judged against.
                if (!string.IsNullOrEmpty(current) && !string.Equals(current, pair.Value, StringComparison.Ordinal))
                {
                    if (adopted == null)
                        adopted = new List<KeyValuePair<TSlot, string>>();

                    adopted.Add(new KeyValuePair<TSlot, string>(pair.Key, current));
                }

                continue;
            }

            if (emptied == null)
                emptied = new List<TSlot>();

            emptied.Add(pair.Key);

            // Still on the body means the player took it off the key. Gone
            // means the item was used up, dropped or swapped out with the
            // kit, which is not a statement about the key at all.
            if (onBody != null && onBody.Contains(pair.Value))
                _clearedByPlayer.Add(pair.Key);
        }

        // Applied after the walk: the dictionary cannot change under its own
        // enumerator.
        if (adopted != null)
        {
            foreach (var pair in adopted)
                _occupants[pair.Key] = pair.Value;
        }

        if (emptied != null)
        {
            foreach (var slot in emptied)
                _occupants.Remove(slot);
        }
    }

    /// <summary>
    /// A reissue announced itself: every remembered id that the map knows
    /// becomes its successor. Ids the map does not know are left alone — the
    /// secure container, scabbard, armband and special pocket slots keep
    /// their objects and ids through the rebuild. Keys and clears are
    /// untouched; only the ids move.
    ///
    /// Only meaningful right after an Observe of the body the map was built
    /// from. The map names every ordinary item the match started with, gone
    /// or not, so a key whose item was used up but never observed as gone
    /// would be renamed to the item's recreated successor and read as a
    /// clear on the next poll. The caller settles first; see
    /// AutoQuickSlots.NoteItemsReissued.
    /// </summary>
    public void RemapItems(IReadOnlyDictionary<string, string> nextIdByPreviousId)
    {
        if (nextIdByPreviousId == null || nextIdByPreviousId.Count == 0 || _occupants.Count == 0)
            return;

        List<KeyValuePair<TSlot, string>> moved = null;

        foreach (var pair in _occupants)
        {
            if (!nextIdByPreviousId.TryGetValue(pair.Value, out var next) || string.IsNullOrEmpty(next))
                continue;

            if (moved == null)
                moved = new List<KeyValuePair<TSlot, string>>();

            moved.Add(new KeyValuePair<TSlot, string>(pair.Key, next));
        }

        if (moved == null)
            return;

        foreach (var pair in moved)
            _occupants[pair.Key] = pair.Value;
    }
}
