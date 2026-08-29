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
/// Switches the tactical devices off on corpses — weapon flashlights, lasers,
/// helmet lamps.
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

    private static readonly Dictionary<Corpse, int> Attempts = new Dictionary<Corpse, int>();
    private static readonly HashSet<Corpse> Done = new HashSet<Corpse>();

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
                // Unity lifetime check: a destroyed corpse compares equal to null.
                if (!(lootList[i] is Corpse corpse) || corpse == null || Done.Contains(corpse))
                    continue;

                var attempt = Attempts.TryGetValue(corpse, out var seen) ? seen + 1 : 1;
                Attempts[corpse] = attempt;

                if (DouseCorpse(corpse) || attempt >= MaxAttempts)
                {
                    Done.Add(corpse);
                    Attempts.Remove(corpse);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse light sweep failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true once the body is fully handled — i.e. the held weapon was
    /// reachable (or there is none), so a later pass would find nothing new.
    /// </summary>
    private static bool DouseCorpse(Corpse corpse)
    {
        var complete = true;

        try
        {
            // Gear worn on the body: slot views are parented onto bones, so a
            // helmet lamp or a slung weapon IS a descendant of the corpse.
            foreach (var controller in corpse.GetComponentsInChildren<TacticalComboVisualController>(true))
                Douse(controller);

            // The weapon in the hands is a scene ROOT, not a child — it hangs off
            // the ragdoll. Taking it from the ragdoll rather than from the dead
            // player's weapon-root pointer matters: that pointer is reassigned to
            // whatever weapon is built next, so on a looted corpse it can aim
            // straight into a prefab a LIVING bot is holding.
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
            Plugin.ModLogger.LogWarning($"Arena corpse light pass failed: {ex.Message}");
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
