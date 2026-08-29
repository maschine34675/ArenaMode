using ArenaMode.Config;
using ArenaMode.Loot;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.Interactive;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Corpse budget for arena performance: dozens of kills per raid pile up bodies
/// (renderers, LOD groups, colliders, ragdoll rigs). Oldest bodies beyond the
/// budget are TRIMMED — renderers and colliders off, ragdoll physics stopped —
/// never destroyed.
///
/// Destroying them mid-raid (the previous GameWorld.DestroyLoot approach) looked
/// clean but recycled the whole skeleton through the shared player asset pool
/// while its Player component had never been disposed. That produced a chain of
/// wreckage: stale sound-event delegates firing on recycled bodies
/// (PlaySoundBank NREs), a ragdoll coroutine outliving its PlayerBody
/// (KeyNotFoundException 'Body'), an NRE storm in BaseLocalGame.CleanUp at raid
/// end (one per destroyed corpse), and leaked spawner bookkeeping that starves
/// bot refills after a few minutes. Trimming keeps every registry intact and
/// still removes the per-frame render and physics cost, which is the whole point.
/// </summary>
internal static class CorpseCleanupService
{
    private const float CadenceSeconds = 5f;
    private const float MinDistanceMeters = 15f;
    private const int KeepOwnCorpses = 1;

    /// <summary>
    /// Bodies already trimmed (they stay in the world's loot list) with their
    /// renderer set, so the sweep can re-assert the disabled state cheaply —
    /// the culling system flips renderers back on when a body enters view.
    /// </summary>
    private static readonly Dictionary<Corpse, Renderer[]> Trimmed =
        new Dictionary<Corpse, Renderer[]>();

    private static float _nextSweepAt;

    public static void Reset()
    {
        _nextSweepAt = 0f;
        Trimmed.Clear();
    }

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

            ReassertTrimmed();

            // LootList is append-ordered (RegisterLoot) => already oldest-first.
            var corpses = world.LootList.OfType<Corpse>()
                .Where(c => c != null && c.HasRagdoll && !Trimmed.ContainsKey(c))
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

            var trimmed = 0;
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

                if (!TrimCorpse(corpse))
                    continue;

                overflow--;
                trimmed++;
            }

            if (trimmed > 0)
                Plugin.ModLogger.LogInfo($"Arena corpse cleanup: trimmed {trimmed} old corpse(s), {maxCorpses} kept visible.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Renderers and colliders off — the body object, its item tree, its ragdoll
    /// and every registry entry stay exactly as they are.
    ///
    /// Both primitives are the POOL-RESTORED ones on purpose: the asset pool
    /// resets forceRenderingOff and every StoreCollider'd collider when a body
    /// goes back in, so a trimmed body can never be handed out later as an
    /// invisible or non-collidable bot. Plain renderer.enabled/collider.enabled
    /// writes would leak exactly that way.
    ///
    /// NOTE renderers do NOT come from LootItem.MakeVisible: Corpse overrides
    /// CacheComponents() with an empty body, so a corpse's cached renderer/LOD
    /// lists are always empty and MakeVisible is a no-op on exactly this type.
    /// </summary>
    private static bool TrimCorpse(Corpse corpse)
    {
        try
        {
            // Only settled bodies. Disabling colliders under a live ragdoll drops
            // it through the floor, freezing one mid-flight looks broken, and
            // touching a running working cycle is what raced PlayerBody before.
            var ragdoll = corpse.Ragdoll;
            if (ragdoll != null && !ragdoll._isPhysicsDone)
                return false;

            var pool = corpse.GetComponent<AssetPoolObject>();

            // Always walk the hierarchy: the pool object caches its renderer list
            // when the role model is created, which is BEFORE PlayerBody.Init
            // instantiates the actual body skins — so the cached list misses
            // exactly the meshes that make a corpse visible. (Hiding those is
            // leak-free anyway: Dispose destroys the skins, the next body gets
            // fresh ones.)
            var renderers = corpse.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                if (renderer != null)
                    renderer.forceRenderingOff = true;
            }

            // The weapon in the corpse's hands is a separate scene object hanging
            // off the ragdoll, so hiding the body alone left a weapon floating in
            // mid-air where the hand used to be. Asking the corpse directly is not
            // enough: the ragdoll destroys the weapon link once the body stops
            // being visible, which our own hiding brings about — hence the
            // registry, filled by the light sweep while the link still exists.
            var weapon = CorpseWeaponRegistry.Get(corpse);
            if (weapon != null)
            {
                var weaponRenderers = weapon.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in weaponRenderers)
                {
                    if (renderer != null)
                        renderer.forceRenderingOff = true;
                }

                // Re-asserted with the body's, so the culling system cannot bring
                // either of them back when the spot comes into view.
                renderers = renderers.Concat(weaponRenderers).ToArray();
            }

            if (pool?.Colliders != null)
            {
                foreach (var collider in pool.Colliders)
                {
                    if (collider == null || !collider.enabled)
                        continue;

                    // Records the pre-trim state so the pool restores it.
                    pool.StoreCollider(collider);
                    collider.enabled = false;
                }
            }

            // A body nobody can see should not attract loot trips either.
            RemoveDeadBodyRecord(corpse);

            Trimmed[corpse] = renderers;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse trim failed: {ex.Message}");
            // Remember it anyway — retrying a failing body every 5s is worse.
            Trimmed[corpse] = new Renderer[0];
            return false;
        }
    }

    /// <summary>
    /// Re-disable renderers the culling system turned back on, and drop entries
    /// whose body the game destroyed (raid teardown, vanilla despawn).
    /// </summary>
    private static void ReassertTrimmed()
    {
        if (Trimmed.Count == 0)
            return;

        List<Corpse> stale = null;
        foreach (var entry in Trimmed)
        {
            // Unity's lifetime check: a destroyed corpse compares equal to null.
            if (entry.Key == null)
            {
                (stale ??= new List<Corpse>()).Add(entry.Key);
                continue;
            }

            var renderers = entry.Value;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer != null && !renderer.forceRenderingOff)
                    renderer.forceRenderingOff = true;
            }
        }

        if (stale == null)
            return;

        foreach (var corpse in stale)
            Trimmed.Remove(corpse);
    }

    /// <summary>
    /// The AI keeps its own body registry (bots path to bodies to loot them).
    /// Matching is by OBJECT IDENTITY, never by profile id: the local player
    /// keeps one profile id across every respawn, so an id match could drop the
    /// record of a completely different body.
    /// </summary>
    private static void RemoveDeadBodyRecord(Corpse corpse)
    {
        try
        {
            var spawner = (Singleton<IBotGame>.Instance)?.BotsController?.BotSpawner;
            var controller = spawner?._deadBodiesController;
            if (controller?.Bodies == null)
                return;

            for (var i = controller.Bodies.Count - 1; i >= 0; i--)
            {
                var body = controller.Bodies[i];
                if (body?.Player == null)
                    continue;

                if (ReferenceEquals(body.Player.gameObject, corpse.gameObject))
                    controller.RemoveBody(body);
            }
        }
        catch
        {
            // Best effort — a stale AI body record is cosmetic (bots inspect a ghost).
        }
    }
}
