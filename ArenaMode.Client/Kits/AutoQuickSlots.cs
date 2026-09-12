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
    /// SettleBeforeReissue ran for the reissue that NoteItemsReissued is about
    /// to announce. The remap is only exact on top of a settled ledger; see
    /// NoteItemsReissued for what goes wrong without it.
    /// </summary>
    private static bool _settledForReissue;

    /// <summary>
    /// What this service put where, and which keys the player emptied by
    /// hand, so it can tell its own work from the player's. Without it there
    /// is no way to notice a deliberate unbind. Pure and tested on its own;
    /// the rules — ids rather than objects, the current occupant of a key
    /// rather than the item first bound there — are argued at the ledger.
    /// </summary>
    private static readonly AutoQuickSlotLedger<EBoundItem> Ledger = new AutoQuickSlotLedger<EBoundItem>();

    public static void Reset()
    {
        _nextCheckAt = 0f;
        _loggedFailure = false;
        _settledForReissue = false;
        Ledger.Reset();
    }

    /// <summary>
    /// The dead body's last second, which the poll never saw.
    ///
    /// Tick stops at the death — IsAlive latches false at the kill — so
    /// whatever happened to the keys in the second before it is still
    /// unjudged: a key the player cleared by hand, a medkit used up, an item
    /// dropped. The protected 5v5 rebuild is about to rename every ordinary
    /// item, after which none of that can be judged any more. So it is judged
    /// here, against the dead body's own bindings and items while both are
    /// still exact, with the same rules as any poll. Afterwards every key the
    /// ledger owns is still bound, which is what makes the remap that follows
    /// a pure rename.
    ///
    /// Called from the respawn engine's equipment callback before the
    /// transaction commits, so it must not throw: a failure here is a key
    /// that may refill once, not a failed respawn.
    /// </summary>
    internal static void SettleBeforeReissue(Profile profile)
    {
        _settledForReissue = false;

        try
        {
            var inventory = profile?.Inventory;
            var bound = inventory?.FastAccess?.BoundItems;
            var carried = inventory?.GetPlayerItems(EPlayerItems.Equipment);
            if (bound == null || carried == null)
                return;

            if (Ledger.OwnedKeyCount > 0)
                Ledger.Observe(IdsOf(bound), IdsOf(carried));

            _settledForReissue = true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena quick slots: could not settle the keys before the reissue: {ex}");
        }
    }

    /// <summary>
    /// The protected 5v5 rebuild reissued the ordinary items under fresh ids
    /// and carried the quick-slot bindings over to them. The ledger follows,
    /// so a key still owned reads as owned under its new id at once. The map
    /// is previous runtime id to next; protected items are not in it and
    /// keep their ids.
    ///
    /// Only on top of a settled ledger. The map covers every ordinary item
    /// the match STARTED with, not what the body died with: a medkit used up
    /// in the last second before the death is recreated by the rebuild and
    /// is in the map. Renaming an unsettled ledger would then point the
    /// emptied key at the recreated medkit, the next poll would find that on
    /// the body, and the key would be recorded as cleared by a player who
    /// only used their medkit — the original bug, inverted. Settled, the
    /// emptied key was already judged against the dead body and is no longer
    /// owned; the remap has nothing to rename.
    ///
    /// Called from the respawn engine's equipment callback after the
    /// transaction has committed, so it must not throw: a failure here is a
    /// key that may refill once, not a failed respawn.
    /// </summary>
    internal static void NoteItemsReissued(IReadOnlyDictionary<string, string> nextIdByPreviousId)
    {
        var settled = _settledForReissue;
        _settledForReissue = false;

        if (!settled || nextIdByPreviousId == null)
            return;

        try
        {
            Ledger.RemapItems(nextIdByPreviousId);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena quick slots: could not follow the reissued items: {ex}");
        }
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
        if (Ledger.OwnedKeyCount > 0)
            Ledger.Observe(IdsOf(bound), IdsOf(onBody));

        var free = FreeSlots
            .Where(slot => !bound.ContainsKey(slot) && !Ledger.IsClearedByPlayer(slot))
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

        // Second medkits, stimulants, anything a content mod added — into the
        // keys no purpose owns, before any home key.
        //
        // This went back and forth once. Filling the nearest key first is better
        // ergonomics and worse for the thing this file exists for: with random
        // kits on, key 6 would hold a bandage one life and a stimulant the next,
        // because the kits that HAVE a spare are exactly the ones whose bleed
        // injector leaves the light-bleed key unused. A key you reach for
        // without looking has to mean one thing. The spare goes to 0.
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
        Ledger.NoteBound(index, item.Id);

        Plugin.ModLogger.LogInfo(
            $"Arena quick slots: bound {item.ShortName?.Localized() ?? item.TemplateId.ToString()} to {index}.");

        return true;
    }

    /// <summary>
    /// The ledger speaks in ids. Item.Id is already the string the item was
    /// made with — the same text TeamArenaLoadoutSnapshot's id map carries —
    /// so this is a walk, not a formatting pass.
    /// </summary>
    private static Dictionary<EBoundItem, string> IdsOf(Dictionary<EBoundItem, Item> bound)
    {
        var ids = new Dictionary<EBoundItem, string>(bound.Count);
        foreach (var pair in bound)
            ids[pair.Key] = pair.Value?.Id;

        return ids;
    }

    private static HashSet<string> IdsOf(IEnumerable<Item> items)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item?.Id != null)
                ids.Add(item.Id);
        }

        return ids;
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
