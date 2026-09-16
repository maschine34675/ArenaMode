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
    /// How long a requested full clear keeps overriding the ordinary budget.
    ///
    /// One sweep is not enough. A body whose ragdoll has not settled is refused
    /// by TrimCorpse on purpose, and the last casualties of a round are exactly
    /// the unsettled ones — so the request stays open across a few cadences
    /// instead of leaving the bodies that were still falling when it was made.
    /// </summary>
    private const float FullClearSeconds = 20f;

    /// <summary>What was hidden for one body, so the sweep can maintain it.</summary>
    private sealed class TrimState
    {
        public Renderer[] Body;

        // A failed trim is also remembered to avoid repeated failures. Only a
        // completed trim may suppress an optional map marker, in this world.
        public bool Completed;
        public GameWorld World;

        /// <summary>Tracked apart from the body: a weapon can change hands.</summary>
        public Renderer[] Weapon;

        public GameObject WeaponObject;
    }

    /// <summary>
    /// Bodies already trimmed — they stay in the world's loot list, so the sweep
    /// re-asserts the hidden state cheaply (the culling system turns renderers
    /// back on when a spot comes into view).
    /// </summary>
    private static readonly Dictionary<Corpse, TrimState> Trimmed =
        new Dictionary<Corpse, TrimState>();

    private static float _nextSweepAt;
    private static float _fullClearUntil;

    public static void Reset()
    {
        _nextSweepAt = 0f;
        _fullClearUntil = 0f;
        Trimmed.Clear();
        Compat.DynamicMapsCorpseMarkers.Reset();
    }

    /// <summary>
    /// Clears the field for a round start: every corpse is trimmed, not just the
    /// overflow, and the three standing exemptions are lifted for the request.
    ///
    /// The exemptions exist for a live round, not for the boundary between two.
    /// Own corpses are kept so a player can walk back to their gear; PitFireTeam
    /// companion bodies are kept because a dead companion is still part of the
    /// protected five-player roster — and that keeps a HISTORY, so by round four
    /// every teammate who ever died is still lying there. Both readings expire
    /// the moment the next round's bodies exist: the previous round's actors have
    /// been retired and replaced by then, which is why this is only ever
    /// requested after that, never during a rebuild.
    ///
    /// A player who turned corpse cleanup off (MaxCorpses = 0, "keep all") is
    /// taken at their word here as everywhere else.
    /// </summary>
    public static void ClearAllSoon(string reason)
    {
        if (!ArenaSession.IsActive || Settings.MaxCorpses.Value <= 0)
            return;

        _fullClearUntil = Time.time + FullClearSeconds;

        // Do not wait out the remainder of the ordinary cadence for a clear the
        // player is about to look at.
        _nextSweepAt = 0f;
        Plugin.ModLogger.LogInfo($"Arena corpse cleanup: clearing every corpse ({reason}).");
    }

    internal static bool IsSuccessfullyTrimmed(Corpse corpse)
        => ArenaSession.IsActive && corpse != null
            && Singleton<GameWorld>.Instantiated
            && Trimmed.TryGetValue(corpse, out var state) && state.Completed
            && ReferenceEquals(state.World, Singleton<GameWorld>.Instance);

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
            Compat.DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();

            // LootList is append-ordered (RegisterLoot) => already oldest-first.
            var corpses = world.LootList.OfType<Corpse>()
                .Where(c => c != null && c.HasRagdoll && !Trimmed.ContainsKey(c))
                .ToList();

            var clearEverything = Time.time < _fullClearUntil;
            var overflow = clearEverything ? corpses.Count : corpses.Count - maxCorpses;
            if (overflow <= 0)
                return;

            var ownProfileId = mainPlayer.ProfileId;
            var protectedOwn = clearEverything
                ? new HashSet<Corpse>()
                : new HashSet<Corpse>(
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

                // A selected PitFireTeam companion remains part of the protected
                // five-player roster after death. Its profile id is cached before
                // the body can leave PFT's live-follower list, so neither its
                // renderers nor its equipment are trimmed by Arena. A round-start
                // clear lifts this: see ClearAllSoon.
                if (!clearEverything
                    && Compat.PitFireTeamCompat.IsTeamProfileId(corpse.PlayerProfileID))
                {
                    continue;
                }

                // Never taken from under a native interaction, not even for a
                // full clear: the player is holding this body's loot window open.
                if (ReferenceEquals(mainPlayer.InteractableObject, corpse))
                    continue;

                if (OpenLootTracker.IsOpen(corpse.ItemOwner?.RootItem))
                    continue;

                if (!clearEverything
                    && corpse.transform != null
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
                Plugin.ModLogger.LogInfo(clearEverything
                    ? $"Arena corpse cleanup: cleared {trimmed} corpse(s) for the round start, {Trimmed.Count} trimmed in total."
                    : $"Arena corpse cleanup: trimmed {trimmed} old corpse(s), {maxCorpses} kept visible, {Trimmed.Count} trimmed in total.");
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
            // Tracked separately from the body: the registry only hands it over
            // while the corpse still owns it, and if it later changes hands the
            // sweep has to make it visible again.
            var weapon = CorpseWeaponRegistry.Get(corpse);
            Renderer[] weaponRenderers = null;
            if (weapon != null)
            {
                weaponRenderers = weapon.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in weaponRenderers)
                {
                    if (renderer != null)
                        renderer.forceRenderingOff = true;
                }
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

            // And it should stop being asked what it is doing sixty times a
            // second. Nothing in the game ever disables a corpse's Player
            // component: the body leaves the alive list, so the world stops
            // ticking it by hand, but Unity keeps dispatching FixedUpdate and
            // LateUpdate to the component itself for the rest of the raid. Worse,
            // that LateUpdate is not gated on being alive — it still runs the
            // hands controller's event and ballistic updates, because the
            // controller is only released when somebody loots the body.
            //
            // At fifty fixed updates a second across two hundred bodies, this is
            // the cost that actually grows with a long raid. Renderers and
            // physics were already dealt with; this is what was left, and it is
            // safe because everything reached through the corpse afterwards —
            // looting, the inventory, the ragdoll — goes through fields and
            // other components rather than through this one's update loop.
            StopTicking(corpse);

            // And it should stop carrying a wardrobe. This is the part that grew
            // worst: every piece of gear on a corpse is an instantiated model,
            // and the bot gear ramp means late-raid bodies wear far more of them
            // than early ones. Taking the gear off hands each model back to the
            // pool it came from, where a living bot can use it again.
            StripGear(corpse);

            // AFTER the strip, and this matters: the captured list included the
            // renderers of every worn model, and those models have just gone
            // back to their pools for living bots to use. Re-asserting the
            // hidden state on them later would blank a piece of armour on
            // somebody who is still walking around — the same recycling trap the
            // weapon registry exists for. What is left belongs to the corpse.
            Trimmed[corpse] = new TrimState
            {
                Completed = true,
                World = Singleton<GameWorld>.Instance,
                Body = Settings.StripCorpseGear.Value
                    ? corpse.GetComponentsInChildren<Renderer>(true)
                    : renderers,
                Weapon = weaponRenderers,
                WeaponObject = weapon,
            };
            Compat.DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse trim failed: {ex.Message}");
            // Remember it anyway — retrying a failing body every 5s is worse.
            Trimmed[corpse] = new TrimState { Body = new Renderer[0] };
            return false;
        }
    }

    /// <summary>
    /// Keeps the hidden state honest: re-disables renderers the culling system
    /// turned back on, hands a weapon back its visibility the moment it stops
    /// belonging to the corpse (somebody looted it — carrying an invisible gun
    /// would be a fine bug to ship), and drops bodies the game destroyed.
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

            var state = entry.Value;
            Hide(state.Body);

            if (state.Weapon == null)
                continue;

            if (ReferenceEquals(CorpseWeaponRegistry.Get(entry.Key), state.WeaponObject)
                && state.WeaponObject != null)
            {
                Hide(state.Weapon);
                continue;
            }

            Show(state.Weapon);
            state.Weapon = null;
            state.WeaponObject = null;
        }

        if (stale == null)
            return;

        foreach (var corpse in stale)
            Trimmed.Remove(corpse);
    }

    /// <summary>
    /// Takes the gear off a corpse nobody can see any more.
    ///
    /// The models are the growth term. A body wears a helmet, armour, a rig, a
    /// backpack and a weapon, each an instantiated prefab, and the bot gear
    /// ramp means a body from minute twenty carries considerably more of them
    /// than one from minute two. Removing an item makes the body's own slot
    /// view destroy the model and hand the prefab back to its pool — so this
    /// does not merely hide the cost, it returns something living bots can use.
    ///
    /// Chosen over destroying the corpse outright, which is the obvious move and
    /// the one that failed before: this touches no registry, leaves the Player
    /// component in place and cannot produce the wreckage that attempt did. The
    /// bodies are already invisible and already dropped from the loot records,
    /// so nothing is taken away that anyone could still have reached — and bots
    /// no longer loot corpses at all.
    /// </summary>
    private static void StripGear(Corpse corpse)
    {
        if (!Settings.StripCorpseGear.Value)
            return;

        try
        {
            var controller = corpse.ItemOwner;
            if (!(corpse.Item is EFT.InventoryLogic.InventoryEquipment equipment) || controller == null)
                return;

            // The first level only: the gear itself, not what is inside it.
            foreach (var slot in equipment.Slots)
            {
                var item = slot?.ContainedItem;
                if (item == null)
                    continue;

                // Left alone: the secure container is special-cased throughout
                // the game, and the weapon the ragdoll is holding belongs to the
                // registry that keeps it from floating in mid-air.
                if (slot.ID == "SecuredContainer" || ReferenceEquals(item, corpse.ItemInHands?.Value))
                    continue;

                try
                {
                    // ProcessActivity matches completion by event type + item,
                    // not by event ID. Do not consume another operation's Begin
                    // (HasForeignEvents deliberately ignores the same owner).
                    if (controller.SelectEvents(item).Any())
                        continue;

                    var result = EFT.InventoryLogic.ItemManipulator
                        .RemoveWithoutRestrictions(item, controller);

                    // Taking the item out of the slot is only half of it. The
                    // container is changed immediately, but the NOTIFICATION
                    // rides on the returned result — and the body's slot view
                    // destroys its model and hands the prefab back only when it
                    // hears about it. Discarding the result, which is what this
                    // did at first, detaches the gear and frees nothing at all.
                    if (result.Succeeded)
                    {
                        // Native immediate operations notify Begin/Succeed AFTER
                        // mutation. Pair both: Succeed alone logs a missing
                        // inventory activity for every stripped item. Complete
                        // even if a Begin observer throws after registration;
                        // removal already happened, so Failed/rollback is wrong.
                        try
                        {
                            result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);
                        }
                        finally
                        {
                            result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);
                        }
                    }
                    else
                        Plugin.ModLogger.LogWarning($"Arena corpse strip: {item.TemplateId} stayed on: {result.Error}");
                }
                catch (Exception inner)
                {
                    Plugin.ModLogger.LogWarning($"Arena corpse strip: {item.TemplateId} removal/notification failed: {inner.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse strip failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops Unity dispatching update messages to a corpse. Deliberately after
    /// the ragdoll has settled — the trim only runs on a finished body — so no
    /// coroutine hosted on the component is cut off half way.
    /// </summary>
    private static void StopTicking(Corpse corpse)
    {
        try
        {
            var player = corpse.GetComponent<Player>();
            if (player != null && player.enabled)
                player.enabled = false;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse tick stop failed: {ex.Message}");
        }
    }

    private static void Hide(Renderer[] renderers)
    {
        if (renderers == null)
            return;

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer != null && !renderer.forceRenderingOff)
                renderer.forceRenderingOff = true;
        }
    }

    private static void Show(Renderer[] renderers)
    {
        if (renderers == null)
            return;

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer != null && renderer.forceRenderingOff)
                renderer.forceRenderingOff = false;
        }
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
