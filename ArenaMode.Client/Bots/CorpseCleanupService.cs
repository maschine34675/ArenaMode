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
/// Corpse budget for arena performance, in two steps.
///
/// A body beyond the budget is first TRIMMED — renderers and colliders off,
/// ragdoll physics stopped, gear back in its pools, Player component no longer
/// ticking. One sweep later, when nothing can reach it any more, it is
/// RELEASED: taken out of the scene for good.
///
/// Trimming alone was the whole of this service until 2026-09-17, and the
/// measuring raid recorded in ../Redeploy/docs/RESPAWN-API.md ("Messraid
/// 17.09.2026") showed what that costs. A hidden body is still a complete,
/// active player GameObject with every MonoBehaviour it was born with, and the
/// count only ever grows: 53 active LocalPlayer objects after three minutes,
/// 232 after fourteen, 415 after twenty-three, against 446 bodies this service
/// had "trimmed". Everything that searches the scene pays for them —
/// FindObjectsOfType&lt;LocalPlayer&gt; itself went from 95 to 303 ms over the
/// same raid — and that includes the game's own searches, not only ours. It is
/// what made Arena's respawn pipeline grow from 0.66 s to 1.66 s.
///
/// Destroying them was tried before and failed, which is why trimming was
/// written the way it was: the old attempt called GameWorld.DestroyLoot and
/// nothing else, so the skeleton went back through the shared player asset pool
/// while its Player component had never been disposed. That produced a chain of
/// wreckage — stale sound-event delegates firing on recycled bodies
/// (PlaySoundBank NREs), a ragdoll coroutine outliving its PlayerBody
/// (KeyNotFoundException 'Body'), an NRE storm in BaseLocalGame.CleanUp at raid
/// end, leaked spawner bookkeeping that starved bot refills.
///
/// The release below is the missing half of that, taken from the game's own two
/// ways of retiring a player. BaseLocalGame.CleanUp runs Player.Dispose and only
/// THEN returns the object; BaseLocalGame.BotDespawn, the mid-raid path for a bot
/// that leaves the map, first clears the AI's knowledge of it through
/// BotsController.DestroyInfo. Release does both, in that order, and only then
/// removes the loot — which is what disposes the PlayerBody and the corpse's own
/// registrations (Corpse.Kill).
///
/// It DESTROYS rather than pools, and that is deliberate. PlayerPoolObject is one
/// of the two pool types the pool keeps ACTIVE while idle
/// (AssetsPoolStaticConfig.IsGameObjectActiveInPool), so pooling a body neither
/// takes it out of the scene nor stops what is attached to it — and only the
/// components the game registered for cleaning are removed, which means a
/// recycled body would carry its old BotOwner, its old Corpse and whatever
/// another mod attached into a living bot. Destroying has no recycling hazard at
/// all, and it is the only release that actually shortens the scene.
///
/// Never released: PitFireTeam roster members, not even for a round-start
/// clear — that one keeps the old hide-only behaviour always. The local
/// player's own bodies are different since 2026-09-18 ("Own corpses release
/// too", CORPSE-CLEANUP-REVIEW.md): only the NEWEST is kept (Redeploy
/// respawns near it and the profile protection reads it). An older body of
/// the player's own releases exactly like anybody else's, once nothing still
/// needs it — see MayRelease's own-corpse branch and IsNewestOwnCorpse.
/// </summary>
internal static class CorpseCleanupService
{
    private const float CadenceSeconds = 5f;
    private const float MinDistanceMeters = 15f;
    private const int KeepOwnCorpses = 1;

    /// <summary>
    /// How many bodies one sweep may take out of the scene.
    ///
    /// A release is not cheap: clearing the AI's knowledge walks every BotsGroup
    /// and every member's memory of that person, and destroying the body tears
    /// down a whole player hierarchy. Doing it for everything that happens to be
    /// eligible would put a frame spike where the slow accumulation used to be —
    /// worst of all right after a round-start clear, which can hide fifty bodies
    /// at once. Draining them over a few cadences costs nothing, because they
    /// are already hidden while they wait.
    ///
    /// Eight per five seconds is well above the rate bodies are produced at: the
    /// 2026-09-17 measuring raid trimmed 446 bodies in 26 minutes, one every
    /// 3.5 seconds, so the queue drains rather than grows.
    /// </summary>
    private const int MaxReleasesPerSweep = 8;

    /// <summary>
    /// How long a requested full clear keeps overriding the ordinary budget.
    ///
    /// One sweep is not enough. A body whose ragdoll has not settled is refused
    /// by TrimCorpse on purpose, and the last casualties of a round are exactly
    /// the unsettled ones — so the request stays open across a few cadences
    /// instead of leaving the bodies that were still falling when it was made.
    /// </summary>
    private const float FullClearSeconds = 20f;

    /// <summary>
    /// How long nothing may have named a body as a damage source before it may
    /// go. See DamageSourcesQuiet: it covers the one deferral that
    /// CorpseDamageSources cannot read — BotMemory.GetHit running GetHitInner
    /// from a timer, which SAIN sets to 0.4 s at most — five times over, and it
    /// is shorter than the cadence, so a body nothing names still goes one sweep
    /// after it was hidden.
    /// </summary>
    private const float DamageSourceQuietSeconds = 2f;

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

        /// <summary>
        /// The sweep that trimmed this body. Release waits for a later one, so
        /// the reassert pass and the optional map-marker removal have both run
        /// on it before it can go away — and so no body is hidden and destroyed
        /// inside the same frame.
        /// </summary>
        public int Sweep;

        /// <summary>
        /// Decided at trim time, while the answer is still reliable. Both are
        /// still read at release, but they no longer mean the same thing.
        ///
        /// <c>Protected</c> means what it always did: "hide this body but
        /// never take it away" — a dead PitFireTeam companion is still part
        /// of the protected roster, forever. A round-start clear lifts only
        /// the HIDING exemption (see ClearAllSoon); it never lifts this one.
        ///
        /// <c>Own</c> now means only "this corpse is the local player's".
        /// Release-time gating moved to a live re-read, MayRelease's
        /// IsNewestOwnCorpse, because "is this still the newest own corpse"
        /// can only be answered against the CURRENT world, not a trim-time
        /// snapshot — a round-start clear can trim the very corpse that is
        /// about to be the sole/newest one, and a later own death can make an
        /// already-trimmed corpse stop being the newest without retrimming
        /// it. <c>Own</c> itself stays trustworthy to cache, though: a
        /// corpse's PlayerProfileID never changes after Corpse.CreateCorpse,
        /// and the local player keeps one profile id across every respawn —
        /// see ReleaseTrimmed's own-release count, which still relies on
        /// exactly that.
        /// </summary>
        public bool Own, Protected;

        /// <summary>A release that threw is never retried — see Release.</summary>
        public bool ReleaseFailed;

        /// <summary>
        /// When CorpseDamageSources first found nothing naming this body as a
        /// damage source, or NaN while something still did at the last reading.
        /// See DamageSourcesQuiet.
        /// </summary>
        public float SourcesQuietSince = float.NaN;

        /// <summary>Whether the sweep last said it is holding this body for that reason.</summary>
        public bool HeldAsSource;

        /// <summary>
        /// Own corpses only. Once true, the world's profile-id registry no
        /// longer names this exact body — a newer own body registered — so
        /// DamageSourcesQuiet's own-corpse refinement bypasses its hold for
        /// good. See CorpseDamageSources.StillNamedInRegistry.
        ///
        /// Cached rather than re-asked every sweep because the answer is
        /// monotonic: a corpse cannot re-register (a player cannot un-die),
        /// so once the registry has moved on it can never move back. Starts
        /// false and is only ever flipped to true, never back — including
        /// when StillNamedInRegistry itself cannot be read: that answers
        /// "still named" (fails closed), which correctly leaves this false
        /// and makes the next sweep ask again instead of caching a guess.
        /// </summary>
        public bool LeftRegistry;

        /// <summary>
        /// Own corpses only. Whether the "left the profile-id registry" line
        /// has already been said for this body — separate from
        /// <see cref="HeldAsSource"/>, which this branch now also uses, but
        /// for a DIFFERENT thing: whether a shot THIS body fired is still in
        /// flight (see DamageSourcesQuiet). Said exactly once per corpse, on
        /// first entry into the LeftRegistry branch, regardless of whether a
        /// shot happens to be in flight at that moment — unlike HeldAsSource,
        /// this does not need to flip back, since leaving the registry is
        /// itself a one-way, one-time event for a given corpse.
        /// </summary>
        public bool LeftRegistryAnnounced;
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

    /// <summary>
    /// Raid totals, kept apart from Trimmed.Count because that table now SHRINKS
    /// as bodies are released and would otherwise under-report the work done.
    ///
    /// <c>_ownReleasedTotal</c> is a subset of <c>_releasedTotal</c>, split out
    /// since 2026-09-18 so the sweep line can show the GPU raid that this is
    /// actually happening — it is read from TrimState.Own, which stays
    /// trustworthy for the reason documented there.
    /// </summary>
    private static int _trimmedTotal, _releasedTotal, _ownReleasedTotal, _releaseFailures;

    private static int _sweep;
    private static bool _reportedReleaseFailure;

    /// <summary>
    /// One scan of <c>world.LootList</c> per sweep, not one per own corpse per
    /// call site. IsNewestOwnCorpse used to re-scan the whole list on every
    /// call, and this sweep can call it once from ReleaseTrimmed's own-corpse
    /// branch AND once from DescribeOwnHeld for every own body still in
    /// Trimmed — redundant work the class doc's own "these figures come from
    /// lists and counters instead" goal argues against. Keyed on <c>_sweep</c>
    /// rather than recomputed eagerly: nothing in Tick() adds a corpse to
    /// world.LootList (that only happens on death, outside this service), so
    /// the answer cannot change between the first and a later call within the
    /// same sweep, and most sweeps never ask at all.
    /// </summary>
    private static int _newestOwnCorpseSweep = -1;
    private static Corpse _newestOwnCorpseCache;

    public static void Reset()
    {
        _nextSweepAt = 0f;
        _fullClearUntil = 0f;
        _trimmedTotal = _releasedTotal = _ownReleasedTotal = _releaseFailures = _sweep = 0;
        _reportedReleaseFailure = false;
        _newestOwnCorpseSweep = -1;
        _newestOwnCorpseCache = null;
        Trimmed.Clear();
        CorpseAggressors.Reset();
        CorpseDamageSources.Reset();
        CorpseTriggerZones.Reset();
        Compat.DynamicMapsCorpseMarkers.Reset();

        // Reset where it is written, so there is one owner: the ledger only ever
        // records what Release below confirmed.
        Diagnostics.CorpseReleaseLedger.Reset();
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

    /// <summary>
    /// One field for a damage-exception report
    /// (<see cref="Diagnostics.ArenaDamageExceptionLog"/>): is a body of this
    /// profile still lying here hidden, or is this table done with it? It exists
    /// to separate the two states the tripwire finding had to tell apart — a
    /// HIDDEN body, which this service has produced since it was written, from a
    /// RELEASED one, which is new since 2026-09-17. Reads only.
    /// </summary>
    internal static string DescribeCorpseState(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
            return "corpse=?";

        foreach (var entry in Trimmed)
        {
            // Unity lifetime check: a destroyed key is a body this table has not
            // pruned yet, and it is not the hidden body being asked about.
            var corpse = entry.Key;
            if (corpse == null
                || !string.Equals(corpse.PlayerProfileID, profileId, StringComparison.Ordinal))
            {
                continue;
            }

            var state = entry.Value;
            return "corpse=hidden"
                + (state.Completed ? string.Empty : "/incomplete")
                + (state.Own ? "/own" : string.Empty)
                + (state.Protected ? "/pft" : string.Empty)
                + (state.ReleaseFailed ? "/release-failed" : string.Empty);
        }

        return "corpse=not-hidden";
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

            _sweep++;

            ReassertTrimmed();
            Compat.DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();

            // LootList is append-ordered (RegisterLoot) => already oldest-first.
            var corpses = world.LootList.OfType<Corpse>()
                .Where(c => c != null && c.HasRagdoll && !Trimmed.ContainsKey(c))
                .ToList();

            var clearEverything = Time.time < _fullClearUntil;
            var overflow = clearEverything ? corpses.Count : corpses.Count - maxCorpses;
            var trimmed = 0;

            if (overflow > 0)
            {
                var ownProfileId = mainPlayer.ProfileId;
                var protectedOwn = clearEverything
                    ? new HashSet<Corpse>()
                    : new HashSet<Corpse>(
                        corpses.Where(c => c.PlayerProfileID == ownProfileId)
                            .AsEnumerable()
                            .Reverse()
                            .Take(KeepOwnCorpses));

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

                    if (!TrimCorpse(corpse, mainPlayer))
                        continue;

                    overflow--;
                    trimmed++;
                }
            }

            // After the trim pass, and TrimState.Sweep is what keeps that from
            // mattering: a body hidden in THIS sweep is not eligible until the
            // next one. The overflow above is unaffected either way, because
            // every releasable body is already in Trimmed and so excluded from
            // the corpses list.
            var (released, ownReleased) = ReleaseTrimmed(world, mainPlayer);

            if (trimmed > 0 || released > 0)
                Plugin.ModLogger.LogInfo(Report(clearEverything, trimmed, released, ownReleased, maxCorpses, world, mainPlayer));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// One line per working sweep, and the raid's only cheap read of how the body
    /// population is doing.
    ///
    /// The numbers matter because the measurement this change answers to used a
    /// whole-scene FindObjectsOfType to count active player objects, which cost up
    /// to 303 ms per reading — Redeploy.Core 1.0.4 removed that probe for exactly
    /// that reason. These figures come from lists and counters instead: the player
    /// pool's own TotalCreatedObjectsCount is every body ever handed out, released
    /// bodies are destroyed rather than pushed back, so
    /// created - pooled - released is the number still in the scene. That is the
    /// figure the 2026-09-17 raid found at 415 and this service is meant to bound.
    /// </summary>
    private static string Report(
        bool clearEverything, int trimmed, int released, int ownReleased, int maxCorpses, GameWorld world, Player mainPlayer)
    {
        var inWorld = world.LootList.OfType<Corpse>().Count(c => c != null);
        var failures = _releaseFailures > 0 ? $", {_releaseFailures} release failure(s)" : string.Empty;
        var ownHeld = DescribeOwnHeld(world, mainPlayer);

        return (clearEverything
                ? $"Arena corpse cleanup: cleared {trimmed} corpse(s) for the round start"
                : $"Arena corpse cleanup: trimmed {trimmed} old corpse(s), {maxCorpses} kept visible")
            + $", released {released} ({ownReleased} own)"
            + $"; {_trimmedTotal} trimmed and {_releasedTotal} released in total ({_ownReleasedTotal} own)"
            + $", {inWorld} corpse(s) in the world{ownHeld}{DescribeBodyPool()}{failures}.";
    }

    /// <summary>
    /// A live snapshot for the sweep line, split by why an own body is still
    /// hidden: still the newest (permanent, by design — see
    /// IsNewestOwnCorpse) versus anything else (transient: DamageSourcesQuiet,
    /// an open loot window, a pending aggressor, a settling ragdoll, a held
    /// map marker — the per-body log lines elsewhere say which). Read from
    /// Trimmed, not world.LootList, because only a trimmed body is a release
    /// candidate at all; a live re-read rather than a counter, so it is
    /// correct even when nothing happened to trigger the sweep line this
    /// cadence.
    /// </summary>
    private static string DescribeOwnHeld(GameWorld world, Player mainPlayer)
    {
        if (mainPlayer == null || Trimmed.Count == 0)
            return string.Empty;

        var newest = 0;
        var other = 0;
        foreach (var entry in Trimmed)
        {
            if (entry.Key == null || !entry.Value.Own)
                continue;

            if (IsNewestOwnCorpse(entry.Key, world, mainPlayer.ProfileId))
                newest++;
            else
                other++;
        }

        if (newest == 0 && other == 0)
            return string.Empty;

        return other > 0
            ? $", {newest} own held as newest + {other} own held for another reason"
            : $", {newest} own held as newest";
    }

    /// <summary>The player asset pool's own counters — no scene search.</summary>
    private static string DescribeBodyPool()
    {
        try
        {
            if (!Singleton<ObjectsFactory>.Instantiated)
                return string.Empty;

            var bodies = Singleton<ObjectsFactory>.Instance
                .GetPools(ObjectsFactory.PoolsCategory.Raid)?.PlayerAssetPool;

            return bodies == null
                ? string.Empty
                : $", player bodies {bodies.TotalCreatedObjectsCount} created / {bodies.CurrentPoolSize} pooled";
        }
        catch (Exception)
        {
            // A diagnostic must never be the reason a sweep fails.
            return string.Empty;
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
    private static bool TrimCorpse(Corpse corpse, Player mainPlayer)
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
                Sweep = _sweep,

                // Recorded here, not at release: a round-start clear lifts the
                // hiding exemptions above, and this must survive that. It is
                // also the moment PFT's answer is still reliable — the body is
                // about to leave its live-follower list.
                Own = corpse.PlayerProfileID == mainPlayer?.ProfileId,
                Protected = Compat.PitFireTeamCompat.IsTeamProfileId(corpse.PlayerProfileID),

                // The damage-source guard's first reading, taken here so that a
                // body nothing names has already been quiet for a whole cadence
                // by the sweep that may release it — the quiet period then costs
                // it nothing. See DamageSourcesQuiet.
                SourcesQuietSince = CorpseDamageSources.NamesAsSource(
                        corpse.PlayerProfileID, Singleton<GameWorld>.Instance)
                    ? float.NaN
                    : Time.time,
            };
            _trimmedTotal++;
            Compat.DynamicMapsCorpseMarkers.RemoveTrimmedMarkers();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse trim failed: {ex.Message}");
            // Remember it anyway — retrying a failing body every 5s is worse.
            // Completed stays false, so this body is never released either.
            Trimmed[corpse] = new TrimState { Body = new Renderer[0], Sweep = _sweep };
            return false;
        }
    }

    /// <summary>
    /// Takes the bodies that are ready out of the scene. Returns how many
    /// left in total, and how many of those were the local player's own —
    /// read from TrimState.Own, which stays trustworthy after release for the
    /// reason documented on that field.
    /// </summary>
    private static (int Released, int OwnReleased) ReleaseTrimmed(GameWorld world, Player mainPlayer)
    {
        if (!Settings.ReleaseTrimmedCorpses.Value || Trimmed.Count == 0)
            return (0, 0);

        // Snapshot: Release mutates Trimmed, and a destroyed body invalidates the
        // enumerator's key as well.
        List<Corpse> due = null;
        foreach (var entry in Trimmed)
        {
            if (!MayRelease(entry.Key, entry.Value, world, mainPlayer))
                continue;

            (due ??= new List<Corpse>()).Add(entry.Key);
            if (due.Count >= MaxReleasesPerSweep)
                break;
        }

        if (due == null)
            return (0, 0);

        var released = 0;
        var ownReleased = 0;
        foreach (var corpse in due)
        {
            if (!Trimmed.TryGetValue(corpse, out var state))
                continue;

            // Read before Release: on success it removes the entry, but the
            // TrimState object itself stays valid to read from afterwards.
            var isOwn = state.Own;
            if (Release(corpse, state, world))
            {
                released++;
                if (isOwn)
                    ownReleased++;
            }
        }

        _releasedTotal += released;
        _ownReleasedTotal += ownReleased;
        return (released, ownReleased);
    }

    /// <summary>
    /// Whether this own corpse is the local player's NEWEST — the one
    /// exemption from release that survives (see CorpseCleanupService's class
    /// doc and MayRelease's own-corpse branch, "Own corpses release too",
    /// 2026-09-18).
    ///
    /// A live re-derivation from <c>world.LootList</c>, not the cached
    /// <c>TrimState.Own</c> flag: "is this still the newest" can only be
    /// answered against the CURRENT world. <c>world.LootList</c> is
    /// append-ordered (<c>RegisterLoot</c>) — see the comment at Tick's own
    /// read of it — i.e. already oldest-first, so the LAST match is the
    /// newest, and this is a query, not a cache: it is correct on every call
    /// regardless of clears, respawns, or how long ago a corpse was trimmed.
    /// A round-start clear (<c>ClearAllSoon</c>) trims the sole/current own
    /// corpse right along with everything else — it is still the only entry
    /// this loop can find, so it still comes back "newest" and stays
    /// protected until a genuinely later own death appends one after it.
    /// </summary>
    private static bool IsNewestOwnCorpse(Corpse corpse, GameWorld world, string ownProfileId)
    {
        // Cached per sweep, not per call - see _newestOwnCorpseCache's own doc.
        if (_newestOwnCorpseSweep != _sweep)
        {
            Corpse newest = null;
            foreach (var loot in world.LootList)
            {
                if (loot is Corpse c && c.PlayerProfileID == ownProfileId)
                    newest = c;
            }

            _newestOwnCorpseCache = newest;
            _newestOwnCorpseSweep = _sweep;
        }

        return ReferenceEquals(_newestOwnCorpseCache, corpse);
    }

    /// <summary>
    /// Every condition a body has to meet before Arena may destroy it. All of
    /// them fail closed: anything unknown or unreadable leaves the body hidden,
    /// which is the behaviour this service had before the release existed.
    ///
    /// The trim's own exclusions are not repeated here — a body that reaches this
    /// table was already past them. What IS repeated is everything that can have
    /// changed in the seconds since, plus the protection a round-start clear
    /// deliberately lifts for hiding and must never lift for destroying.
    /// </summary>
    private static bool MayRelease(Corpse corpse, TrimState state, GameWorld world, Player mainPlayer)
    {
        // A failed trim is not evidence of anything, and a body from an earlier
        // raid's world must never be touched with this world's owners.
        // state.Own is NOT read here any more — see IsNewestOwnCorpse below,
        // and TrimState.Own's own doc, for why that moved to a live re-read.
        if (!state.Completed || state.ReleaseFailed || state.Protected)
            return false;

        if (!ReferenceEquals(state.World, world) || state.Sweep >= _sweep)
            return false;

        // Unity lifetime check: already destroyed, ReassertTrimmed prunes it.
        if (corpse == null)
            return false;

        // Same rule as the trim: a body still falling must not be interfered
        // with, and a released one cannot be put back if it turns out it was.
        if (corpse.Ragdoll != null && !corpse.Ragdoll._isPhysicsDone)
            return false;

        // Re-read rather than trusting the record: the local player keeps one
        // profile id across every Redeploy respawn, so this covers every body
        // that was ever theirs. Only the NEWEST is kept forever — an older
        // own body is released like anybody else's, subject to every check
        // below (including the DamageSourcesQuiet refinement further down).
        var isOwnCorpse = corpse.PlayerProfileID == mainPlayer.ProfileId;
        if (isOwnCorpse && IsNewestOwnCorpse(corpse, world, mainPlayer.ProfileId))
            return false;

        if (Compat.PitFireTeamCompat.IsTeamProfileId(corpse.PlayerProfileID))
            return false;

        // Somebody is holding this body's loot window open right now.
        if (ReferenceEquals(mainPlayer.InteractableObject, corpse)
            || OpenLootTracker.IsOpen(corpse.ItemOwner?.RootItem))
        {
            return false;
        }

        // Body identity, and the only identity there is: Corpse.CreateCorpse
        // attaches the component to the dead player's own GameObject, so the
        // Player on it IS this body. No Player at all means a map-placed still
        // corpse (Corpse.CreateStillCorpse), which has no owner to dispose and
        // is none of Arena's business. player.IsYourPlayer is NOT checked any
        // more: it is set once at LocalPlayer creation and never reset in the
        // decompile, so it cannot distinguish "newest" from "not newest" —
        // that already happened above. ReferenceEquals(player, mainPlayer)
        // stays: a corpse can never BE the live player's current body, own or
        // not — that would mean releasing somebody who is still playing.
        var player = corpse.GetComponent<Player>();
        if (player == null || ReferenceEquals(player, mainPlayer))
            return false;

        // Never a living body. A corpse's owner is dead by construction; an
        // unreadable health controller is refused rather than assumed.
        if (player.HealthController?.IsAlive != false)
            return false;

        // Nobody may still be owed a death by this body. See CorpseAggressors:
        // a released body that is somebody's pending aggressor makes that
        // somebody's OnDead throw, which abandons their whole death.
        // Reference-keyed, so this stays in force for an own corpse exactly
        // as for a foreign one.
        if (CorpseAggressors.IsPendingAggressor(player, world))
            return false;

        // Nor may anything still name it as a damage SOURCE. See
        // CorpseDamageSources: every ApplyDamageInfo and ShotDelegate exception
        // the 2026-09-17 raids logged was a tripwire a dead Labyrinth boss had
        // planted, going off after Arena had released him. For an own corpse
        // there is a refinement — see DamageSourcesQuiet.
        if (!DamageSourcesQuiet(corpse, state, world, player, isOwnCorpse))
            return false;

        // The optional map has to have let go first: its own map-change handler
        // re-adds every key it still holds, reading the body's nickname and
        // position on the way, and a destroyed body answers neither.
        return !Compat.DynamicMapsCorpseMarkers.HoldsMarker(player);
    }

    /// <summary>
    /// True once nothing has named this body as a damage source for
    /// DamageSourceQuietSeconds — a reading of CorpseDamageSources now, AND an
    /// earlier one that was already clear.
    ///
    /// A single reading is not enough, because the last damage a source dealt
    /// can outlive the source by a moment: a bot hit while it has no goal enemy
    /// runs BotMemory.GetHitInner from a timer with the DamageInfo captured
    /// (EFT/BotMemory.cs:679-700), after the grenade and its fragments are
    /// gone. SAIN sets that delay to 0.4 s at most. What makes a quiet period
    /// sufficient is that it can only shrink: a dead body cannot plant, throw or
    /// fire, so once a reading finds nothing, nothing new can start naming it,
    /// and every hit still in the air at that reading has landed within the
    /// delay. The stamp therefore survives sweeps that did not evaluate this
    /// body, and is cleared by any reading that finds a source.
    ///
    /// It says so when it starts and stops holding a body, once each: a
    /// Labyrinth raid has one planter, so these are a couple of lines, and they
    /// are what shows a raid actually exercised the guard rather than passing
    /// because nobody planted anything. An unreadable world is not repeated per
    /// body; CorpseDamageSources has already warned once.
    ///
    /// Refinement for an OWN corpse ("Own corpses release too", 2026-09-18):
    /// CorpseDamageSources matches by profile id only, and the local player's
    /// own current shot, grenade or tripwire shares its profile id with every
    /// one of their own hidden bodies — so once an own corpse becomes
    /// releasable at all (it is not the newest, see MayRelease), a reading
    /// that finds a source can no longer tell "this corpse's own trailing
    /// source" apart from "the live player is still fighting", and would hold
    /// every hidden own body at once for as long as combat continues. That
    /// cannot cause a wrongful early release (the guard still only ever holds
    /// a body longer), but it can erase the whole point of releasing at all
    /// during exactly the fast, continuous-combat periods this service
    /// targets — the self-interference the design's critic review found.
    ///
    /// CorpseDamageSources.StillNamedInRegistry answers the question that
    /// actually decides it: is this corpse's Player still what a profile-id
    /// lookup resolves to at all? A tripwire's explosion and a flare's
    /// impact both go through GameWorld's own ever-existed tables LAZILY, at
    /// the moment they land, and GameWorld.RegisterPlayer overwrites both of
    /// them with whichever body registers under a profile id — which every
    /// own respawn does, death or live-kit swap alike. So once a newer own
    /// body has registered, neither can resolve THIS corpse through its
    /// profile id any more, only the live player — and holding it hidden for
    /// a source that can only ever reach somebody else defers its release
    /// for nothing. If the registry still names this exact body, the
    /// ordinary hold below still applies, unchanged.
    ///
    /// A directly-fired SHOT is the one exception left, because it does NOT
    /// resolve lazily: Shot.Player is a frozen reference, set once at the
    /// moment of firing (see CorpseDamageSources.ShotInFlight's own doc), so
    /// a bullet this body fired while still alive can still be travelling —
    /// and still pointing at exactly this body — after a newer own body has
    /// registered. CorpseDamageSources.ShotStillInFlightFromBody is what
    /// still holds a corpse for exactly that: a REFERENCE check against this
    /// body, not a profile-id one, so it cannot be fooled by the live
    /// player's own current shots the way a profile-id match would be. The
    /// window this actually closes is narrow — MayRelease only reaches an
    /// own corpse's DamageSourcesQuiet call once a LATER own death already
    /// exists, which needs an intervening respawn, several seconds at
    /// minimum against any bullet's flight time — but the guard is cheap
    /// enough that there is no reason to rely on that margin alone.
    /// </summary>
    private static bool DamageSourcesQuiet(Corpse corpse, TrimState state, GameWorld world, Player player, bool isOwnCorpse)
    {
        if (isOwnCorpse)
        {
            if (!state.LeftRegistry)
                state.LeftRegistry = !CorpseDamageSources.StillNamedInRegistry(player, corpse.PlayerProfileID, world);

            if (state.LeftRegistry)
            {
                if (!state.LeftRegistryAnnounced)
                {
                    state.LeftRegistryAnnounced = true;
                    Plugin.ModLogger.LogInfo(
                        $"Arena corpse release: {corpse.PlayerProfileID} left the profile-id registry (a newer own body replaced it); a source that only shares its profile id can no longer reach it.");
                }

                // The one resolution that does not go through the registry
                // lazily. See this method's own doc and
                // CorpseDamageSources.ShotStillInFlightFromBody.
                if (CorpseDamageSources.ShotStillInFlightFromBody(player, world))
                {
                    if (!state.HeldAsSource)
                    {
                        state.HeldAsSource = true;
                        Plugin.ModLogger.LogInfo(
                            $"Arena corpse release: keeping {corpse.PlayerProfileID} hidden — a shot it fired is still in flight, even though it left the profile-id registry.");
                    }

                    return false;
                }

                if (state.HeldAsSource)
                {
                    state.HeldAsSource = false;
                    Plugin.ModLogger.LogInfo(
                        $"Arena corpse release: nothing {corpse.PlayerProfileID} fired is still in flight; releasing it now that it has left the profile-id registry.");
                }

                return true;
            }
        }

        var source = CorpseDamageSources.Describe(corpse.PlayerProfileID, world);
        if (source != null)
        {
            if (!state.HeldAsSource && source != CorpseDamageSources.Unreadable)
            {
                state.HeldAsSource = true;
                Plugin.ModLogger.LogInfo(
                    $"Arena corpse release: keeping {corpse.PlayerProfileID} hidden while {source} still names it as a damage source.");
            }

            state.SourcesQuietSince = float.NaN;
            return false;
        }

        if (state.HeldAsSource)
        {
            state.HeldAsSource = false;
            Plugin.ModLogger.LogInfo(
                $"Arena corpse release: nothing names {corpse.PlayerProfileID} as a damage source any more; it may go after {DamageSourceQuietSeconds:0} s quiet.");
        }

        if (float.IsNaN(state.SourcesQuietSince))
            state.SourcesQuietSince = Time.time;

        return Time.time - state.SourcesQuietSince >= DamageSourceQuietSeconds;
    }

    /// <summary>
    /// The release itself. Order is the whole content of this method.
    ///
    /// 1. The AI's knowledge of this person, through the game's own mid-raid
    ///    retirement call (BaseLocalGame.BotDespawn uses the same one): every
    ///    BotsGroup's enemy and neutral entry, every member's memory of it, the
    ///    spawner's player list, and its own AIData.
    /// 2. The body, through Player.Dispose — what BaseLocalGame.CleanUp runs
    ///    before it returns a player object. It unregisters from GameWorld, drops
    ///    the speaker group, ends the audio subscriptions, disposes the
    ///    conditional controllers and the movement context, and destroys the
    ///    hands controller: that last step is what hands the corpse's WEAPON
    ///    model back to its own pool (FirearmController.Destroy). It deliberately
    ///    leaves the PlayerBody alone while Corpse is set, because Corpse.Kill
    ///    owns that one.
    /// 3. The dead-body record, AFTER 1 and 2 and never before. Clearing the AI's
    ///    knowledge is itself what re-creates it: BotsGroup.RemoveEnemy adds a
    ///    DeadBody for a person whose health controller says dead, so removing
    ///    the record first would leave the body registered for bots to walk to.
    /// 4. Out of the world, through GameWorld.DestroyLoot — the loot list, the
    ///    net-id table, the item-owner registry, every player's interaction
    ///    raycast, then Corpse.Kill: PlayerBody.Dispose (skins unskinned, slot
    ///    models back to their pools), the culling registration, and the return.
    ///    IsStub redirects that return: it is the pool's own switch for "this
    ///    object did not come from a pool", and it makes AssetPoolObject return
    ///    the nested pool objects to their pools and DESTROY the body itself
    ///    instead of pushing it back. Without it the body would be pushed into
    ///    the player pool, which keeps its GameObject active and hands a later
    ///    bot every component added to it at runtime.
    /// 5. Our own tables last, because until step 4 has run they are what keeps
    ///    the hidden state honest and the optional map marker suppressed.
    ///
    /// Nothing here puts the hidden renderers back, and it does not have to.
    /// Both of the things the trim wrote are the pool-restored primitives (the
    /// argument is at TrimCorpse): the corpse's own object is destroyed, and
    /// every model that outlives it — the weapon pooled by the hands controller
    /// in step 2, the slot models pooled by PlayerBody.Dispose in step 4 — has
    /// its forceRenderingOff cleared by the pool as it goes back in. Writing
    /// `false` here instead would mean writing over renderers that may already
    /// belong to a living bot.
    /// </summary>
    private static bool Release(Corpse corpse, TrimState state, GameWorld world)
    {
        var player = corpse.GetComponent<Player>();
        var pool = corpse.GetComponent<AssetPoolObject>();

        // MayRelease proved the Player; the pool object is the body's own and
        // cannot be missing on a pooled player, but a body without one must not
        // be destroyed by hand — the nested models would go with it.
        if (player == null || pool == null)
        {
            state.ReleaseFailed = true;
            return false;
        }

        // Read while the body is still whole, for the release ledger below.
        var profileId = corpse.PlayerProfileID;

        try
        {
            // No bot game means no bot groups, so there is no knowledge to
            // clear; asked through Instantiated rather than a bare Instance so
            // that stays a fact rather than a swallowed null.
            if (Singleton<IBotGame>.Instantiated)
                Singleton<IBotGame>.Instance?.BotsController?.DestroyInfo(player);

            player.Dispose();

            // Refused, not ignored: a destroyed body the AI still has a record
            // of is the one outcome worse than a hidden body. The catch below
            // then leaves this one hidden and disposed.
            if (!RemoveDeadBodyRecord(corpse))
                throw new InvalidOperationException("the AI's dead-body record could not be cleared");

            // The one native coroutine that outlives the body it reads, and the
            // only thing the 2026-09-17 raid found wrong with destroying one.
            CorpseTriggerZones.Release(player);

            pool.IsStub = true;
            try
            {
                world.DestroyLoot(corpse);
            }
            finally
            {
                // Only if the destroy did not happen: leaving IsStub set on a
                // live pooled body would destroy it the next time anything
                // returned it, instead of pooling it.
                if (!ReferenceEquals(pool, null) && pool != null)
                    pool.IsStub = false;
            }

            CorpseWeaponRegistry.Forget(corpse);
            CorpseLightService.Forget(corpse);
            Trimmed.Remove(corpse);

            // Confirmed releases only, and only while the damage diagnostic is
            // on. It is what lets a later exception say whether the object on its
            // stack is a body this method took away — how the tripwire finding
            // in CORPSE-CLEANUP-REVIEW.md ("Settled") was pinned, and how a
            // regression of CorpseDamageSources would announce itself.
            Diagnostics.CorpseReleaseLedger.Record(profileId, _sweep);
            return true;
        }
        catch (Exception ex)
        {
            // Not retried, and the record is deliberately KEPT: whatever state
            // the body is in, the reassert pass is what stops it reappearing,
            // and a second attempt on a half-released body is the one thing
            // that could make it worse.
            state.ReleaseFailed = true;
            _releaseFailures++;
            if (!_reportedReleaseFailure)
            {
                _reportedReleaseFailure = true;
                Plugin.ModLogger.LogWarning($"Arena corpse release failed: {ex}");
            }
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
    ///
    /// Returns whether it can say the registry is clean. For a body that is only
    /// being HIDDEN the answer is cosmetic and ignored — bots would inspect a
    /// ghost. For one about to be DESTROYED it is not: a DeadBody resolves its
    /// Player through GameWorld's ever-existed table, which nothing ever cleans,
    /// so a record left behind points at a destroyed body and its position is
    /// read from PlayerBones. Release refuses rather than leave that.
    /// </summary>
    private static bool RemoveDeadBodyRecord(Corpse corpse)
    {
        try
        {
            var spawner = (Singleton<IBotGame>.Instance)?.BotsController?.BotSpawner;
            var controller = spawner?._deadBodiesController;
            if (controller?.Bodies == null)
                return true;

            for (var i = controller.Bodies.Count - 1; i >= 0; i--)
            {
                var body = controller.Bodies[i];

                // Unity lifetime check on Player: a record whose body is already
                // gone must be skipped, not dereferenced — reading gameObject on
                // a destroyed component throws.
                if (body?.Player == null)
                    continue;

                if (ReferenceEquals(body.Player.gameObject, corpse.gameObject))
                    controller.RemoveBody(body);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
