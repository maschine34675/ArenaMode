using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Kits;

/// <summary>
/// Puts the medical items on the number keys, every life, without being asked.
///
/// The game clears quick-slot bindings with the body, which is a sensible rule
/// for a raid you enter once and a pointless chore in a mode built on dying: the
/// kit is new, the items inside it are new instances, and binding them by hand
/// costs more time than the round lasts. So the arena does it.
///
/// Only Item4 through Item10 are touched. ItemV, Item1, Item2 and Item3 are not
/// free slots at all — FastAccess wires them to Scabbard, Holster and the two
/// weapon slots — and ItemG belongs to the grenade selector, which has its own
/// picker. Writing to any of those would fight the game rather than help it.
///
/// The layout is deliberately FIXED rather than "whatever order they were found
/// in". A key that holds the medkit this life and a splint the next is worse
/// than no binding, because you reach for it without looking. Same kind of item,
/// same key, every round — that is the whole value.
/// </summary>
internal static class AutoQuickSlots
{
    /// <summary>
    /// The freely bindable slots. Keys 4-9 and 0, matching QuickUseSelectorUtil's
    /// own mapping.
    /// </summary>
    private static readonly EBoundItem[] FreeSlots =
    {
        EBoundItem.Item4,
        EBoundItem.Item5,
        EBoundItem.Item6,
        EBoundItem.Item7,
        EBoundItem.Item8,
        EBoundItem.Item9,
        EBoundItem.Item10,
    };

    /// <summary>
    /// Each purpose owns a KEY, not a position in a queue.
    ///
    /// Filling the slots in order looked equivalent and was not: kits carry
    /// different numbers of things, so a kit with two medkits pushed the
    /// heavy-bleed answer from key 5 to key 6 and the layout moved under the
    /// player between lives. With a home key per purpose, 5 is the heavy bleed
    /// whether or not this kit has one — and if it does not, the key is simply
    /// empty, which is information too.
    /// </summary>
    private static readonly Dictionary<Purpose, EBoundItem> HomeSlot = new Dictionary<Purpose, EBoundItem>
    {
        [Purpose.Healing] = EBoundItem.Item4,
        [Purpose.HeavyBleed] = EBoundItem.Item5,
        [Purpose.LightBleed] = EBoundItem.Item6,
        [Purpose.Fracture] = EBoundItem.Item7,
        [Purpose.Pain] = EBoundItem.Item8,
        [Purpose.Surgery] = EBoundItem.Item9,
    };

    /// <summary>
    /// What a medical item is FOR. Each of these has a home key; see HomeSlot.
    ///
    /// Ordered by how often it is reached for under fire rather than by how bad
    /// the condition is: the medkit is the thing you press most, so it gets the
    /// first key. Surgery is late because a destroyed limb is not something you
    /// fix mid-fight. Anything unclassified has no home and takes what is left.
    /// </summary>
    private enum Purpose
    {
        Healing,
        HeavyBleed,
        LightBleed,
        Fracture,
        Pain,
        Surgery,
        Other,
    }

    private const float CheckIntervalSeconds = 1f;

    private static float _nextCheckAt;
    private static bool _loggedFailure;

    /// <summary>
    /// What this service put where, so it can tell its own work from the
    /// player's. Without it there is no way to notice a deliberate unbind.
    /// </summary>
    private static readonly Dictionary<EBoundItem, Item> Ours = new Dictionary<EBoundItem, Item>();

    /// <summary>
    /// Keys the player emptied by hand. Left alone for the rest of the raid.
    ///
    /// Raid-scoped rather than per-life on purpose: the items are new instances
    /// every life, but "I want this key clear" is about the KEY, and re-asking
    /// the player to say so after every death is the chore this service exists
    /// to remove.
    /// </summary>
    private static readonly HashSet<EBoundItem> ClearedByPlayer = new HashSet<EBoundItem>();

    public static void Reset()
    {
        _nextCheckAt = 0f;
        _loggedFailure = false;
        Ours.Clear();
        ClearedByPlayer.Clear();
    }

    /// <summary>
    /// Per-frame from the controller, doing real work only when a binding is
    /// actually missing.
    ///
    /// Polled rather than hooked to the paths that hand out gear. There are
    /// several of those now — the raid-start swap, the respawn, a Gun Game
    /// promotion, a Tactical re-issue — and more will arrive; a poll is correct
    /// for all of them including the ones that do not exist yet.
    ///
    /// It costs one walk of the carried items a second. NOT zero once everything
    /// is bound: a kit carries four medical items into seven keys, so the
    /// free-slot early-out never fires. A few dozen items once a second, in a
    /// mode that spawns two dozen bots.
    /// </summary>
    public static void Tick()
    {
        if (!ArenaSession.IsActive || !Settings.AutoQuickSlots.Value || Time.time < _nextCheckAt)
            return;

        _nextCheckAt = Time.time + CheckIntervalSeconds;

        try
        {
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            var inventory = player.InventoryController;
            var bound = inventory?.Inventory?.FastAccess?.BoundItems;
            if (bound == null)
                return;

            Assign(inventory, bound);
        }
        catch (Exception ex)
        {
            // Once per raid: a binding that will not take is a nuisance, a log
            // line every second is a different problem.
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.ModLogger.LogWarning($"Arena quick slots: binding failed (silenced for this raid): {ex}");
            }
        }
    }

    private static void Assign(InventoryController inventory, Dictionary<EBoundItem, Item> bound)
    {
        var carried = inventory.Inventory?.GetPlayerItems(EPlayerItems.Equipment);
        if (carried == null)
            return;

        var onBody = new HashSet<Item>(carried);
        NoteWhatThePlayerCleared(bound, onBody);

        var free = FreeSlots
            .Where(slot => !bound.ContainsKey(slot) && !ClearedByPlayer.Contains(slot))
            .ToList();

        if (free.Count == 0)
            return;

        // Already-bound items are skipped wherever they sit: re-binding one would
        // move it off the key the player has been using all round.
        var alreadyBound = new HashSet<Item>(bound.Values);

        var candidates = onBody
            .OfType<Meds>()
            .Where(item => !alreadyBound.Contains(item))
            .Where(inventory.IsAtBindablePlace)
            .GroupBy(PurposeOf)
            .ToDictionary(group => group.Key, group => group.ToList());

        if (candidates.Count == 0)
            return;

        // Home keys first, one item each, so the layout is the same every life.
        var spare = new List<Item>();
        var taken = new HashSet<EBoundItem>();

        foreach (Purpose purpose in Enum.GetValues(typeof(Purpose)))
        {
            if (!candidates.TryGetValue(purpose, out var items) || items.Count == 0)
                continue;

            if (HomeSlot.TryGetValue(purpose, out var home) && free.Contains(home))
            {
                if (Bind(inventory, items[0], home))
                {
                    taken.Add(home);
                    spare.AddRange(items.Skip(1));
                    continue;
                }
            }

            // No home, or the home key is already spoken for: queue the whole
            // group for whatever is left over.
            spare.AddRange(items);
        }

        // Second medkits, stimulants, anything a content mod added. Keys no
        // purpose claimed go first, so a home key stays empty — and therefore
        // still means what it means — until nothing else is left.
        //
        // Indexed rather than searched with FirstOrDefault: EBoundItem's default
        // is ItemG, the grenade slot, so a "nothing found" result would be
        // indistinguishable from a real value and this would quietly bind a
        // medkit over the grenade picker.
        var leftover = free
            .Where(candidate => !taken.Contains(candidate))
            .OrderBy(candidate => HomeSlot.ContainsValue(candidate) ? 1 : 0)
            .ToList();

        var next = 0;
        foreach (var item in spare)
        {
            if (next >= leftover.Count)
                return;

            if (Bind(inventory, item, leftover[next]))
                next++;
        }
    }

    /// <summary>
    /// Spots the keys the player emptied on purpose.
    ///
    /// A binding this service made can only leave BoundItems through
    /// UnbindResult.Run, which is the player dragging the item out of the quick
    /// slot. Without noticing that, the next poll put it straight back inside a
    /// second and the key could never be cleared at all: the service would be
    /// arguing with the person it is meant to be helping.
    ///
    /// A key emptied because the ITEM is gone -- used up, or replaced along with
    /// the kit -- is not a clear, and says nothing about what the player wants
    /// on that key.
    /// </summary>
    private static void NoteWhatThePlayerCleared(Dictionary<EBoundItem, Item> bound, HashSet<Item> onBody)
    {
        if (Ours.Count == 0)
            return;

        List<EBoundItem> gone = null;

        foreach (var pair in Ours)
        {
            if (bound.ContainsKey(pair.Key))
                continue;

            if (gone == null)
                gone = new List<EBoundItem>();

            gone.Add(pair.Key);

            // Still on the body means the player took it off the key. Gone means
            // the item was used up or swapped out with the kit, which is not a
            // statement about the key at all.
            if (pair.Value != null && onBody.Contains(pair.Value))
                ClearedByPlayer.Add(pair.Key);
        }

        if (gone == null)
            return;

        foreach (var slot in gone)
            Ours.Remove(slot);
    }

    private static bool Bind(InventoryController inventory, Item item, EBoundItem index)
    {
        var result = BindResult.Run(inventory, item, index, simulate: false);
        if (result.Failed)
            return false;

        // The dictionary is already written by Run; this is what tells the HUD.
        // Skipping it leaves a binding that works on the keyboard and shows an
        // empty slot on screen — the same trap as a removal whose events are
        // never raised.
        result.Value.RaiseEvents(inventory, CommandStatus.Succeed);
        Ours[index] = item;

        Plugin.ModLogger.LogInfo(
            $"Arena quick slots: bound {item.ShortName?.Localized() ?? item.TemplateId.ToString()} to {index}.");

        return true;
    }

    /// <summary>
    /// What the item is for, read from the template's own effects rather than
    /// from a list of template ids — so a medical item from a content mod lands
    /// on a sensible key without this file knowing about it.
    ///
    /// One purpose per item. A Salewa both heals and stops bleeding; it counts as
    /// the medkit, because four hundred hit points is what it is carried for and
    /// the bleed has a dedicated answer in the same kit anyway.
    /// </summary>
    private static Purpose PurposeOf(Meds item)
    {
        var template = item.Template as MedsTemplate;
        var effects = template?.DamageEffects;

        // Surgery first: a field surgical kit also carries a few hit points and a
        // fracture cure, and neither is what anyone opens it for.
        if (effects != null && effects.ContainsKey(EDamageEffectType.DestroyedPart))
            return Purpose.Surgery;

        // A hundred is a wide gap, not a fine line. The smallest real medkit is
        // the AI-2 at a hundred; the largest thing that merely happens to carry a
        // few points is the Surv12 at nine. Anything in between does not exist,
        // so the threshold does not have to be argued over.
        if (template != null && template.MaxHpResource >= 100)
            return Purpose.Healing;

        if (effects != null)
        {
            if (effects.ContainsKey(EDamageEffectType.HeavyBleeding))
                return Purpose.HeavyBleed;

            if (effects.ContainsKey(EDamageEffectType.LightBleeding))
                return Purpose.LightBleed;

            if (effects.ContainsKey(EDamageEffectType.Fracture))
                return Purpose.Fracture;

            if (effects.ContainsKey(EDamageEffectType.Pain))
                return Purpose.Pain;
        }

        return Purpose.Other;
    }
}
