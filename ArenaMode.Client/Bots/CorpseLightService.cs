using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Switches the tactical devices off on corpses AND on the weapons they leave
/// lying around — flashlights, lasers, helmet lamps.
///
/// The weapons matter more than the bodies: a corpse's weapon is a separate
/// scene object hanging off the ragdoll, so hiding the body leaves its lit
/// weapon on the floor. Over a long raid that is dozens of shadow-casting
/// lights from bodies the player can no longer even see.
///
/// A dead bot leaves behind a shadow-casting spot light that, unlike map lamps,
/// is exempt from the distance-based shadow culling (the engine flags weapon
/// flashlights so their volumetric cone is drawn at ANY range). An active laser
/// additionally burns a physics raycast plus two mesh draws EVERY frame. Nothing
/// in the game switches any of it off when the owner dies, and the corpse trim
/// cannot help: it hides MESHES, and a light is not a mesh.
///
/// POOL SAFETY — the whole design rests on one fact. The only thing written here
/// is the visual controller's own on/off state, applied through the controller's
/// own UpdateBeams(). The engine re-runs exactly that call whenever a
/// light-bearing item is built from the pool, so the state is re-derived per
/// body rather than being something the pool has to restore. Deeper writes
/// (Light.enabled, Light.shadows, deactivating a light's own GameObject) would
/// survive pooling and hand a later bot a dead flashlight — the failure class
/// this project has already paid for twice.
///
/// The ITEM's state is deliberately restored afterwards: it is diffable, travels
/// with the weapon into the looter's inventory and is read by the bot AI. A
/// looted flashlight therefore still works normally.
/// </summary>
internal static class CorpseLightService
{
    private const float CadenceSeconds = 1f;

    /// <summary>
    /// A corpse enters the loot list in the frame its owner dies, but the held
    /// weapon is only attached to the ragdoll a frame later — so the first pass
    /// can legitimately miss it. Retry a few times before giving up.
    /// </summary>
    private const int MaxAttempts = 5;

    private static readonly Dictionary<LootItem, int> Attempts = new Dictionary<LootItem, int>();
    private static readonly HashSet<LootItem> Done = new HashSet<LootItem>();

    private static float _nextSweepAt;

    public static void Reset()
    {
        _nextSweepAt = 0f;
        Attempts.Clear();
        Done.Clear();
    }

    public static void Tick()
    {
        if (!ArenaSession.IsActive || Time.time < _nextSweepAt)
            return;

        _nextSweepAt = Time.time + CadenceSeconds;

        if (!Settings.CorpseLightsOff.Value || !Singleton<GameWorld>.Instantiated)
            return;

        try
        {
            var lootList = Singleton<GameWorld>.Instance.LootList;
            for (var i = 0; i < lootList.Count; i++)
            {
                var loot = lootList[i] as LootItem;

                // Unity lifetime check: a destroyed object compares equal to null.
                if (loot == null || Done.Contains(loot))
                    continue;

                var attempt = Attempts.TryGetValue(loot, out var seen) ? seen + 1 : 1;
                Attempts[loot] = attempt;

                if (DouseLoot(loot) || attempt >= MaxAttempts)
                {
                    Done.Add(loot);
                    Attempts.Remove(loot);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse light sweep failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles any lit loot: a corpse (worn gear plus the weapon hanging off its
    /// ragdoll) or a weapon lying on the ground in its own right. Returns true
    /// once nothing more can be found, so the entry can be retired.
    /// </summary>
    private static bool DouseLoot(LootItem loot)
    {
        var complete = true;

        try
        {
            // Anything parented to the object itself. For a corpse that covers the
            // worn gear, because slot views are parented onto bones — a helmet lamp
            // and a slung weapon ARE descendants. For a dropped weapon it is the
            // weapon's own mods.
            foreach (var controller in loot.GetComponentsInChildren<TacticalComboVisualController>(true))
                Douse(controller);

            if (!(loot is Corpse corpse))
                return true;

            // The weapon in a corpse's hands is a scene ROOT, not a child — it
            // hangs off the ragdoll, which is why it keeps lying there (and lit)
            // after the body itself was hidden. Taking it from the ragdoll rather
            // than from the dead player's weapon-root pointer matters: that
            // pointer is reassigned to whatever weapon is built next, so on a
            // looted corpse it can aim into a prefab a LIVING bot is holding.
            var weapon = corpse.Ragdoll?.WeaponRigidbody;
            if (weapon != null)
            {
                foreach (var controller in weapon.GetComponentsInChildren<TacticalComboVisualController>(true))
                    Douse(controller);
            }
            else if (corpse.ItemInHands?.Value != null)
            {
                // Held item exists but the ragdoll has not taken it yet.
                complete = false;
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena loot light pass failed: {ex.Message}");
        }

        return complete;
    }

    /// <summary>
    /// Drives the controller's own refresh with the light reported as off, then
    /// puts the item state back. The visuals stay dark; the item is untouched.
    /// </summary>
    private static void Douse(TacticalComboVisualController controller)
    {
        try
        {
            var light = controller?.LightMod;
            if (light == null || !light.IsActive)
                return;

            light.IsActive = false;
            controller.UpdateBeams();
            light.IsActive = true;
        }
        catch (Exception)
        {
            // One stubborn device must never stop the sweep.
        }
    }
}
