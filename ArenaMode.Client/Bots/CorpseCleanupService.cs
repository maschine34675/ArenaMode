using ArenaMode.Config;
using ArenaMode.Loot;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.Interactive;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Corpse cap for arena performance: dozens of kills per raid pile up bodies
/// (renderers, colliders, loot registries). Evicts the OLDEST ragdoll corpses
/// beyond the cap through the game's own GameWorld.DestroyLoot path — the exact
/// teardown the engine runs for every corpse at raid end — never a raw Destroy.
/// Keeps the player's most recent corpse (kit retrieval) and anything close,
/// aimed at, or currently open in the loot screen.
/// </summary>
internal static class CorpseCleanupService
{
    private const float CadenceSeconds = 5f;
    private const float MinDistanceMeters = 15f;
    private const int KeepOwnCorpses = 1;

    private static float _nextSweepAt;

    public static void Reset() => _nextSweepAt = 0f;

    public static void Tick()
    {
        if (!ArenaSession.IsActive || Time.time < _nextSweepAt)
            return;

        _nextSweepAt = Time.time + CadenceSeconds;

        var maxCorpses = Settings.MaxCorpses.Value;
        if (maxCorpses <= 0 || !Singleton<GameWorld>.Instantiated)
            return;

        try
        {
            var world = Singleton<GameWorld>.Instance;
            var mainPlayer = world.MainPlayer;
            if (mainPlayer == null)
                return;

            // LootList is append-ordered (RegisterLoot) => already oldest-first.
            // Snapshot before destroying: DestroyLoot mutates the list.
            var corpses = world.LootList.OfType<Corpse>()
                .Where(c => c != null && c.HasRagdoll)
                .ToList();

            var overflow = corpses.Count - maxCorpses;
            if (overflow <= 0)
                return;

            var ownProfileId = mainPlayer.ProfileId;
            var protectedOwn = new HashSet<Corpse>(
                corpses.Where(c => c.PlayerProfileID == ownProfileId)
                    .AsEnumerable()
                    .Reverse()
                    .Take(KeepOwnCorpses));

            var removed = 0;
            foreach (var corpse in corpses)
            {
                if (overflow <= 0)
                    break;

                if (protectedOwn.Contains(corpse))
                    continue;

                if (ReferenceEquals(mainPlayer.InteractableObject, corpse))
                    continue;

                if (OpenLootTracker.IsOpen(corpse.ItemOwner?.RootItem))
                    continue;

                if (corpse.transform != null
                    && Vector3.Distance(mainPlayer.Position, corpse.transform.position) < MinDistanceMeters)
                {
                    continue;
                }

                SanitizePooledBodyState(corpse);
                RemoveFromDeadBodiesController(corpse.PlayerProfileID);
                world.DestroyLoot(corpse);
                overflow--;
                removed++;
            }

            if (removed > 0)
                Plugin.ModLogger.LogInfo($"Arena corpse cleanup: removed {removed} old corpse(s), {maxCorpses} kept.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// DestroyLoot pushes the whole skeleton — ProceduralWeaponAnimation included —
    /// back onto the SHARED player asset pool, and the pool reset never touches the
    /// PWA effectors. A bot body carries a DrawDown-only procedural mask
    /// (BotOwner.Create) that would kill ALL recoil for a local player respawning
    /// onto it later, plus possibly a latched lame walk preset and breath tremor
    /// from bot health. Scrub before returning: bots re-strip their own mask on
    /// spawn, so a clean body is correct for every future pool consumer.
    /// </summary>
    private static void SanitizePooledBodyState(Corpse corpse)
    {
        try
        {
            var weaponAnimation = corpse.GetComponentInChildren<ProceduralWeaponAnimation>(true);
            if (weaponAnimation == null)
                return;

            // HandShake stays clear: vanilla steady state is off (raised only
            // transiently by StartHandShake, lowered only by its coroutine).
            weaponAnimation.Mask =
                EProceduralAnimationMask.Breathing
                | EProceduralAnimationMask.Walking
                | EProceduralAnimationMask.MotionReaction
                | EProceduralAnimationMask.ForceReaction
                | EProceduralAnimationMask.Shooting
                | EProceduralAnimationMask.DrawDown
                | EProceduralAnimationMask.Aiming;

            if (weaponAnimation.Walk != null)
                weaponAnimation.Walk.CurrentWalkPreset = WalkEffector.EWalkPreset.normal;

            if (weaponAnimation.Breath != null)
            {
                weaponAnimation.Breath.TremorOn = false;
                weaponAnimation.Breath.Fracture = false;
            }
        }
        catch
        {
            // Best effort — the respawn-side mask restore covers a missed scrub.
        }
    }

    /// <summary>
    /// The AI keeps its own body registry (bots path to bodies to loot/inspect
    /// them) — drop the matching record so nobody walks to a ghost.
    /// </summary>
    private static void RemoveFromDeadBodiesController(string profileId)
    {
        try
        {
            // Direct field on the spawner — reachable even when zero bots are alive
            // (which is exactly when the corpse pile is largest).
            var spawner = (Singleton<IBotGame>.Instance)?.BotsController?.BotSpawner;
            var controller = spawner?._deadBodiesController;
            if (controller?.Bodies == null)
                return;

            var body = controller.Bodies.FirstOrDefault(b => b?.Player != null && b.Player.ProfileId == profileId);
            if (body != null)
                controller.RemoveBody(body);
        }
        catch
        {
            // Best effort — a stale AI body record is cosmetic (bots inspect a ghost).
        }
    }
}
