using ArenaMode.Config;
using ArenaMode.Loot;
using ArenaMode.Modes;
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
///
/// The overflow pass's own "near the player" exemption (MinDistanceMeters)
/// assumes kills are spread across the map and the player keeps moving — see
/// ActiveMinDistanceMeters for why Zombie mode, a horde converging on a
/// largely stationary player, is the opposite case and gets that exemption
/// dropped to zero instead.
///
/// Zombie mode goes further than that one exemption (2026-10-07): it also
/// sweeps every second instead of every five (see ZombieCadenceSeconds) and
/// drains the release itself between sweeps, up to ZombieMaxReleasesPerFrame
/// bodies a frame, rather than only at one (see DrainBetweenSweeps). A
/// watchdog clear-out can hide fifty-odd bodies at once, and a hidden body is
/// still a complete, active player GameObject hierarchy sitting in the scene
/// (ragdoll, disabled-but-still-registered renderers, GameWorld.LootList,
/// every scene search anybody runs) even though it already left the engine's
/// per-frame player tick at death, one frame before CorpseCleanupService ever
/// sees it (Player.OnDead starts OnDeadCoroutine, which calls
/// GameWorld.UnregisterPlayer and so removes it from AllAlivePlayersList —
/// see ZombieCadenceSeconds's own doc; corrected 2026-10-07, an earlier
/// version of this paragraph claimed the opposite). What releasing it sooner
/// buys is the same bounded-scene-growth case this class's own opening
/// paragraphs already measured, applied to a watchdog's 56-body backlog
/// instead of to the raid as a whole. Every other mode's cadence and release
/// cap, and the ragdoll-settled gate that keeps a body visibly falling before
/// any of this touches it, are completely unchanged; see
/// ZombieCadenceSeconds's own doc for the full decompile evidence and
/// ZOMBIE-MODE-REVIEW.md, "v3 (2026-10-07): faster corpse release in Zombie
/// mode".
/// </summary>
internal static class CorpseCleanupService
{
    /// <summary>
    /// How often a sweep runs: reassert the hidden state, trim the overflow,
    /// release what is due. Five seconds for every mode except Zombie — see
    /// <see cref="ZombieCadenceSeconds"/> and <see cref="ActiveCadenceSeconds"/>.
    /// </summary>
    private const float CadenceSeconds = 5f;

    /// <summary>
    /// Zombie's own cadence: one second, not five.
    ///
    /// CORRECTION (2026-10-07): an earlier version of this comment claimed a
    /// hidden-but-unreleased body kept paying for <c>GameWorld.PlayerTick</c>/
    /// <c>AfterPlayerTick</c> every frame through <c>Player.ComplexUpdate</c>,
    /// and read the 2026-10-06 measurement's 116.6 <c>ComplexUpdate</c> calls
    /// per frame (56 live infected) as "56 live plus ~60 dead ones still
    /// ticking". Both are wrong. <c>Player.OnDead</c> starts
    /// <c>OnDeadCoroutine</c> (EFT/Player.cs:30218), which — one frame after
    /// death, regardless of hiding or trimming — calls
    /// <c>GameWorld.UnregisterPlayer</c> (EFT/Player.cs:30247) and removes the
    /// body from <c>GameWorld.AllAlivePlayersList</c>
    /// (EFT/GameWorld.cs:2060-2076) before <c>SafePlayerOperation</c>
    /// (EFT/GameWorld.cs:1848-1861) — which <c>PlayerTick</c>,
    /// <c>AfterPlayerTick</c> and <c>DoOtherWorldTick</c>
    /// (EFT/GameWorld.cs:1811-1823, 1826-1832, 1782-1795) all go through —
    /// ever iterates it again; <c>Player.Dispose</c>'s own call to
    /// <c>UnregisterPlayer</c>
    /// (EFT/Player.cs:30933) is by then the game's own idempotent repeat (the
    /// <c>RegisteredPlayers.Contains</c> guard at EFT/GameWorld.cs:2062). The
    /// 116.6 figure was 56 LIVE bodies getting <c>ComplexUpdate</c> called
    /// about TWICE each per frame — once via <c>PlayerTick</c>'s call for the
    /// body's own queue, once via <c>DoOtherWorldTick</c>'s call for the
    /// other queue (EFT/Player.cs:28999-29013) — never once per corpse.
    ///
    /// What a hidden-but-unreleased body DOES still cost, and what this
    /// faster cadence is actually for, is bounded scene growth: it remains a
    /// complete, active player GameObject hierarchy (ragdoll, renderers
    /// forced off but still registered with the culling system, which keeps
    /// turning them back on — see <c>ReassertTrimmed</c>) sitting in
    /// <c>GameWorld.LootList</c> and <c>_allPlayersEverExisted</c>, and every
    /// scene search anybody runs pays for it — exactly what this class's own
    /// opening paragraphs measured on 2026-09-17 (415 active LocalPlayer
    /// objects, <c>FindObjectsOfType&lt;LocalPlayer&gt;</c> 95 -&gt; 303 ms,
    /// Arena's respawn pipeline 0.66 -&gt; 1.66 s). A watchdog clear-out can
    /// hide fifty-odd bodies at once; a faster cadence alone only shrinks the
    /// WAIT between sweeps, so that backlog spends less time sitting in the
    /// scene — see <see cref="ZombieMaxReleasesPerFrame"/> and
    /// <see cref="DrainBetweenSweeps"/> for the other half, which drains the
    /// release itself between sweeps instead of saving it for one. See
    /// ZOMBIE-MODE-REVIEW.md, "v3 (2026-10-07): faster corpse release in
    /// Zombie mode" and its 2026-10-07 correction, for the full evidence.
    ///
    /// This does not touch why a body visibly FALLS before any of this: the
    /// ragdoll gate in <see cref="TrimCorpse"/> and <see cref="MayRelease"/>
    /// (<c>corpse.Ragdoll._isPhysicsDone</c>, which
    /// <c>CorpseRagdoll.WorkingCycle</c>/<c>Corpse.CheckCorpseIsStill</c> only
    /// set once every rigidbody is asleep or 15 s have passed) is unchanged
    /// by either Zombie constant — a faster cadence only shortens how long an
    /// already-SETTLED body waits, never how long one still falling is left
    /// alone.
    /// </summary>
    private const float ZombieCadenceSeconds = 1f;

    private const float MinDistanceMeters = 15f;
    private const int KeepOwnCorpses = 1;

    /// <summary>
    /// Zombie's own "near the player" distance exemption: 0, not 15.
    ///
    /// MinDistanceMeters was designed for a map-spanning PMC raid, where a
    /// kill near the player's CURRENT spot is recent and a kill far away is
    /// old — the 15m bubble is a small, temporary exclusion while the player
    /// keeps moving through the map. Zombie mode's entire premise is the
    /// opposite: InfectedAttackLayer's melee range is 4m and its Shooting
    /// engagement range is 15m (decompile-verified), and the design expects
    /// the player to hold a position and fight an incoming horde rather than
    /// roam — exactly the geometry that keeps every freshly dead zombie
    /// inside the ordinary 15m exemption indefinitely, since ZombieSpawnService
    /// also pulls every idle infected toward wherever the player currently
    /// is (NudgeIdleZombies) rather than spreading kills across the map. For
    /// this mode "near the player" is not a proxy for "still relevant" the
    /// way it is in every other mode — if anything it is the opposite, since
    /// the oldest bodies accumulate exactly where the fight is happening and
    /// never reach the overflow/trim pass below. Zero removes the exemption
    /// entirely for Zombie, letting the overflow count (oldest corpse first,
    /// LootList is append-ordered) decide what to trim instead, which every
    /// other safeguard in this sweep (ragdoll-settled check, open-loot-window
    /// check, held-interactable check) already protects independently of
    /// distance.
    /// </summary>
    private const float ZombieMinDistanceMeters = 0f;

    /// <summary>Zombie's own mode check, shared by every Zombie-only branch below.</summary>
    private static bool IsZombieRaid()
        => string.Equals(ArenaSession.ActiveMode, ArenaModeCatalog.ZombieId, StringComparison.Ordinal);

    private static float ActiveMinDistanceMeters()
        => IsZombieRaid() ? ZombieMinDistanceMeters : MinDistanceMeters;

    /// <summary>
    /// <see cref="ZombieCadenceSeconds"/> for Zombie, <see cref="CadenceSeconds"/>
    /// for every other mode. See <see cref="ZombieCadenceSeconds"/>'s own doc
    /// for why Zombie needs its own.
    /// </summary>
    private static float ActiveCadenceSeconds()
        => IsZombieRaid() ? ZombieCadenceSeconds : CadenceSeconds;

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
    ///
    /// Zombie mode uses a different cap entirely — see
    /// <see cref="ZombieMaxReleasesPerFrame"/> — for both its sweep's own
    /// release pass and the between-sweep drain.
    /// </summary>
    private const int MaxReleasesPerSweep = 8;

    /// <summary>
    /// Zombie's own per-frame release cap: used by the sweep's own release
    /// pass AND by the between-sweep drain (<see cref="DrainBetweenSweeps"/>)
    /// — never <see cref="MaxReleasesPerSweep"/> while
    /// <see cref="IsZombieRaid"/> is true.
    ///
    /// Two, not eight: the sweep itself now runs every
    /// <see cref="ZombieCadenceSeconds"/> (1 s) instead of every
    /// <see cref="CadenceSeconds"/> (5 s), so the same bounded-work argument
    /// MaxReleasesPerSweep's own doc makes keeps the total release rate in
    /// the same range by releasing fewer bodies per frame, more often,
    /// instead of widening the burst any one frame may do. A watchdog
    /// clear-out (<c>ZombieSpawnService.RetireAllAliveInfected</c>) can still
    /// hide fifty-odd bodies at once; draining two of them on every frame
    /// instead of eight every five seconds empties the same backlog in a few
    /// seconds rather than roughly 35 (ZOMBIE-MODE-REVIEW.md, "Optimisation
    /// candidates, ordered", item (a)) without ever enlarging a single
    /// frame's worth of release work beyond what the ordinary cap already
    /// allows.
    /// </summary>
    private const int ZombieMaxReleasesPerFrame = 2;

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

        /// <summary>
        /// Own corpses only, and only meaningful once <see cref="LeftRegistry"/>
        /// is true. When <see cref="CorpseDamageSources.ShotStillInFlightFromBody"/>
        /// first found nothing in flight, or NaN while it still does at the
        /// last reading — the same DamageSourceQuietSeconds bridge the
        /// ordinary path below uses, applied on its own timer here because a
        /// shot landing (leaving the flight list) is not the end of the
        /// hazard: BotMemory.GetHit can defer the hit it just delivered by up
        /// to ~0.4 s (see DamageSourcesQuiet's own doc), and that deferred
        /// DamageInfo holds a frozen reference to this exact body if it is
        /// what fired the shot. Kept separate from <see cref="SourcesQuietSince"/>,
        /// which TrimCorpse already writes once at trim time against a
        /// different question (a profile-id source reading, taken before
        /// LeftRegistry was even known) — reusing it here would start this
        /// clock from the wrong moment, or skip it outright for a corpse
        /// trimmed long ago.
        /// </summary>
        public float ShotQuietSince = float.NaN;
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
    /// Zombie only. What <see cref="DrainBetweenSweeps"/> has released since
    /// the last working sweep's Report line, folded into that line's own
    /// `released`/`ownReleased` by <see cref="Tick"/> and zeroed right after —
    /// so the per-sweep Info line stays the one cheap read of the body
    /// population (one line per working sweep, never one per frame) while
    /// still accounting for every release the drain made in between. Always
    /// zero for every non-Zombie raid, since DrainBetweenSweeps is never
    /// called for one.
    /// </summary>
    private static int _drainReleasedSinceReport, _drainOwnReleasedSinceReport;

    /// <summary>Same policy as <see cref="_reportedReleaseFailure"/>, for the drain's own catch.</summary>
    private static bool _drainFailureReported;

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
    ///
    /// That invariant assumed every call happened inside one synchronous
    /// Tick() call, which was true before <see cref="DrainBetweenSweeps"/>
    /// existed — a drain frame calls this (through ReleaseTrimmed/MayRelease)
    /// while deliberately leaving <c>_sweep</c> unchanged for up to
    /// <see cref="ZombieCadenceSeconds"/>, across many real frames, so an own
    /// death that lands between two drain frames of the SAME <c>_sweep</c>
    /// value would otherwise read a stale answer.
    ///
    /// <see cref="_newestOwnCorpseLootCount"/> is the cheap half of the fix
    /// (2026-10-07): <see cref="DrainBetweenSweeps"/> no longer forces a
    /// rescan on every one of its up to ~59 calls per real second regardless
    /// of whether anything changed — it forces one only when
    /// <c>world.LootList.Count</c> has moved since the count this cache was
    /// last actually filled at. Nothing in this file inserts into or removes
    /// from the MIDDLE of <c>world.LootList</c> (an own death only ever
    /// appends; <see cref="Release"/> is the only remover, via
    /// <c>GameWorld.DestroyLoot</c>, which shrinks it), so an unchanged Count
    /// proves no corpse — own or otherwise — was added or removed, and the
    /// cached answer is still exactly the one a full rescan would return. A
    /// Count that returns to its old value after an add-then-remove pair
    /// inside one untracked window is the one case this cannot see through;
    /// it is already covered by the existing fail-closed guarantee (a stale
    /// answer only ever OVER-protects whichever corpse the cache already
    /// points at — the true newest body is never even asked about, since it
    /// is still falling and so never reaches <see cref="Trimmed"/> — see
    /// <see cref="DrainBetweenSweeps"/>'s own doc), not a new risk.
    /// </summary>
    private static int _newestOwnCorpseSweep = -1;
    private static Corpse _newestOwnCorpseCache;

    /// <summary>
    /// <c>world.LootList.Count</c> as of the scan that last filled
    /// <see cref="_newestOwnCorpseCache"/>, so <see cref="DrainBetweenSweeps"/>
    /// can tell "nothing changed" from "something might have" without
    /// rescanning the list itself. See <see cref="_newestOwnCorpseSweep"/>'s
    /// own doc for the argument.
    /// </summary>
    private static int _newestOwnCorpseLootCount = -1;

    public static void Reset()
    {
        _nextSweepAt = 0f;
        _fullClearUntil = 0f;
        _trimmedTotal = _releasedTotal = _ownReleasedTotal = _releaseFailures = _sweep = 0;
        _reportedReleaseFailure = false;
        _drainReleasedSinceReport = _drainOwnReleasedSinceReport = 0;
        _drainFailureReported = false;
        _newestOwnCorpseSweep = -1;
        _newestOwnCorpseCache = null;
        _newestOwnCorpseLootCount = -1;
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
        if (!ArenaSession.IsActive)
            return;

        if (Time.time < _nextSweepAt)
        {
            // Zombie only (one added ordinal string comparison; every other
            // mode takes this early return exactly as before). Drains the
            // release itself between sweeps instead of waiting out the rest
            // of the cadence for the sweep's own release pass — see
            // DrainBetweenSweeps.
            if (IsZombieRaid())
                DrainBetweenSweeps();

            return;
        }

        _nextSweepAt = Time.time + ActiveCadenceSeconds();

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
            var minDistanceMeters = ActiveMinDistanceMeters();

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
                        && Vector3.Distance(mainPlayer.Position, corpse.transform.position) < minDistanceMeters)
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
            var maxReleases = IsZombieRaid() ? ZombieMaxReleasesPerFrame : MaxReleasesPerSweep;
            var (released, ownReleased) = ReleaseTrimmed(world, mainPlayer, maxReleases);

            // Fold in whatever DrainBetweenSweeps already released since the
            // last working sweep line - see _drainReleasedSinceReport's own
            // doc. Both are always zero outside Zombie.
            released += _drainReleasedSinceReport;
            ownReleased += _drainOwnReleasedSinceReport;
            _drainReleasedSinceReport = 0;
            _drainOwnReleasedSinceReport = 0;

            if (trimmed > 0 || released > 0)
                Plugin.ModLogger.LogInfo(Report(clearEverything, trimmed, released, ownReleased, maxCorpses, world, mainPlayer));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Zombie mode only, called from <see cref="Tick"/> on every frame that is
    /// NOT a sweep. Releases up to <see cref="ZombieMaxReleasesPerFrame"/>
    /// already-trimmed bodies without running a sweep: no
    /// <see cref="ReassertTrimmed"/>, no trim pass, and <see cref="_sweep"/>
    /// is never incremented here — so <see cref="MayRelease"/>'s
    /// <c>state.Sweep &lt; _sweep</c> gate is exactly as strict in a drain
    /// frame as in the sweep itself. A body hidden in the CURRENT sweep still
    /// waits for the NEXT sweep's reassert pass before anything reachable
    /// from here can touch it; every other MayRelease hold (damage sources,
    /// own-corpse, loot window, aggressor, map marker, ragdoll) applies
    /// exactly as it does to the sweep's own release pass, because this
    /// calls the exact same ReleaseTrimmed/MayRelease path the sweep does —
    /// with one deliberate exception: it keeps <see cref="IsNewestOwnCorpse"/>'s
    /// per-sweep cache honest across the many real frames it spans under one
    /// unchanged <see cref="_sweep"/> value, where a fresh own death would
    /// otherwise go unseen until the next real sweep. It does this cheaply
    /// (see the Count check right before ReleaseTrimmed below, and
    /// <see cref="_newestOwnCorpseLootCount"/>'s own doc) rather than forcing
    /// a full <c>world.LootList</c> rescan on every one of its calls — up to
    /// ~59 a second — regardless of whether anything actually changed; no
    /// test/mutant covers the difference between the two, for the same
    /// reason <see cref="Release"/>'s 2026-10-07 fix needed none: a stale
    /// read here only ever over-protects whichever corpse the cache already
    /// points at (fail-closed, see this method's own closing paragraph), and
    /// <c>DamageSourceQuietSeconds</c> (2 s) outlasts
    /// <see cref="ZombieCadenceSeconds"/> (1 s), so the next real sweep's own
    /// <c>_sweep</c>-keyed refresh always lands before a quiet timer started
    /// inside a drain window could finish — no sequence of calls here can
    /// produce an outcome a full-rescan-every-frame version would not.
    ///
    /// Guarded on the same conditions the sweep checks before it would call
    /// ReleaseTrimmed at all — MaxCorpses &gt; 0, a GameWorld, a main player,
    /// and (inside ReleaseTrimmed itself) ReleaseTrimmedCorpses/Trimmed.Count
    /// — so turning any of them off silently turns the drain off too; there
    /// is nothing wrong to report for any of those four.
    ///
    /// A throw is caught and reported once per raid, the same policy every
    /// other fault in this file uses, and leaves nothing released for that
    /// frame: a failed drain frame costs the backlog one frame of draining,
    /// never a wrongly released body — the method fails safe, not open.
    /// </summary>
    private static void DrainBetweenSweeps()
    {
        try
        {
            var maxCorpses = Settings.MaxCorpses.Value;
            if (maxCorpses <= 0 || !Singleton<GameWorld>.Instantiated)
                return;

            var world = Singleton<GameWorld>.Instance;
            var mainPlayer = world.MainPlayer;
            if (mainPlayer == null)
                return;

            // Only force IsNewestOwnCorpse to re-derive its answer when
            // world.LootList has actually moved since the scan that filled
            // the cache — see _newestOwnCorpseLootCount's own doc for why
            // that is enough to prove the cached answer still matches a full
            // rescan. Every MayRelease call this drain frame makes afterwards
            // still shares one answer either way, exactly as the cache's own
            // doc intends, because nothing within this single ReleaseTrimmed
            // call appends to world.LootList.
            if (world.LootList.Count != _newestOwnCorpseLootCount)
                _newestOwnCorpseSweep = -1;

            var (released, ownReleased) = ReleaseTrimmed(world, mainPlayer, ZombieMaxReleasesPerFrame);
            _drainReleasedSinceReport += released;
            _drainOwnReleasedSinceReport += ownReleased;
        }
        catch (Exception ex)
        {
            if (!_drainFailureReported)
            {
                _drainFailureReported = true;
                Plugin.ModLogger.LogWarning($"Arena corpse cleanup drain failed: {ex.Message}");
            }
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
    /// Cheap pre-check for ReleaseTrimmed's loop (review pass, 2026-10-07):
    /// true when the quiet timer <see cref="DamageSourcesQuiet"/> would
    /// consult is already known and has not elapsed yet, which means
    /// DamageSourcesQuiet is guaranteed to answer false WITHOUT being called
    /// — so MayRelease would answer false too, regardless of what
    /// CorpseAggressors.IsPendingAggressor (an O(AllAlivePlayersList.Count)
    /// scan) or DamageSourcesQuiet's own Describe/StillNamedInRegistry reads
    /// (four more list scans) would say, since every one of MayRelease's
    /// checks is AND'ed together and this one is already known to fail.
    ///
    /// Sound by DamageSourcesQuiet's own documented invariant: a dead body
    /// cannot plant, throw or fire, so once a reading goes quiet nothing can
    /// un-quiet it before the window elapses — the timestamp this reads can
    /// only ever make the same body MORE quiet, never less, while it is
    /// unchanged. Concretely, for the field actually in play: if quietSince
    /// is non-NaN, a fresh call into DamageSourcesQuiet either finds no
    /// source (leaves quietSince as-is, since it is already non-NaN, and
    /// returns the SAME elapsed-time comparison this check already makes) or
    /// finds one (resets quietSince to NaN and returns false) — false in
    /// both cases, exactly what skipping already assumes. This only ever
    /// skips a body that is NOT YET due; it never causes an early RELEASE —
    /// every other gate in MayRelease still runs in full once this check
    /// finds the window has elapsed or the timer is still unset.
    ///
    /// Picks the SAME field DamageSourcesQuiet would: ShotQuietSince once an
    /// own corpse has left the profile-id registry (TrimState.LeftRegistry's
    /// own doc), SourcesQuietSince otherwise — re-deriving isOwnCorpse the
    /// identical way MayRelease does (a fresh PlayerProfileID comparison, not
    /// the cached TrimState.Own, which a null mainPlayer at trim time could
    /// have recorded wrong) rather than trusting a cache this method has no
    /// other reason to read.
    ///
    /// NEVER skips for an own corpse while LeftRegistry is still false
    /// (review pass, 2026-10-07, round 2) — that is not "SourcesQuietSince
    /// governs instead", it is "LeftRegistry has not been DECIDED yet", and
    /// LeftRegistry is only ever decided by DamageSourcesQuiet's own
    /// StillNamedInRegistry read. TrimCorpse stamps SourcesQuietSince for
    /// EVERY body, own or not, the instant nothing names it at trim — for a
    /// genuine own, non-newest corpse that reading is operationally
    /// meaningless (DamageSourcesQuiet's own doc: LeftRegistry is already
    /// true the FIRST time such a corpse ever reaches it, so its
    /// SourcesQuietSince-governed bottom path is "not a path real gameplay
    /// exercises today") — but it is still non-NaN, and skipping on it
    /// would never let that first real call happen at all, so LeftRegistry
    /// would stay false and the correct ShotQuietSince clock would never
    /// start. Falling through here costs nothing extra: MayRelease's own
    /// IsNewestOwnCorpse check already short-circuits the newest own corpse
    /// (the common repeat case) before IsPendingAggressor/DamageSourcesQuiet
    /// are ever reached, and every other own corpse determines LeftRegistry
    /// on its first real call, after which this check skips on ShotQuietSince
    /// exactly as it does for anything else.
    ///
    /// This is exactly the scenario the v3 Zombie drain targets: a ~46-56-body
    /// watchdog clear-out leaves every one of them sweep-eligible but still
    /// inside the 2 s DamageSourceQuietSeconds window for about two seconds,
    /// during which DrainBetweenSweeps calls ReleaseTrimmed up to ~59
    /// times/sec — without this check, every one of those calls paid the full
    /// gauntlet for every entry that was not yet due, on top of (not instead
    /// of) the scene-footprint cost a body already carries just by staying
    /// hidden-but-unreleased (bounded growth — see this class's own opening
    /// paragraphs; corrected 2026-10-07, see ZombieCadenceSeconds's own doc —
    /// not a per-frame Player.ComplexUpdate cost, which a dead body stops
    /// paying at death, well before release). Zombie infected are never own
    /// corpses, so every one of them gets the full benefit from the very
    /// first reading.
    /// </summary>
    private static bool QuietWindowKnownOpen(Corpse corpse, TrimState state, Player mainPlayer)
    {
        var isOwnCorpse = corpse.PlayerProfileID == mainPlayer.ProfileId;
        if (isOwnCorpse && !state.LeftRegistry)
            return false;

        var quietSince = isOwnCorpse ? state.ShotQuietSince : state.SourcesQuietSince;
        return !float.IsNaN(quietSince) && Time.time - quietSince < DamageSourceQuietSeconds;
    }

    /// <summary>
    /// Takes the bodies that are ready out of the scene, at most
    /// <paramref name="maxReleases"/> of them. Returns how many left in
    /// total, and how many of those were the local player's own — read from
    /// TrimState.Own, which stays trustworthy after release for the reason
    /// documented on that field.
    ///
    /// The cap is passed in rather than read from a constant because it
    /// differs by caller: the sweep itself passes MaxReleasesPerSweep (8) for
    /// every mode except Zombie, which passes ZombieMaxReleasesPerFrame (2)
    /// both from its own sweep frame and from every frame DrainBetweenSweeps
    /// calls this between sweeps — see that constant's own doc for why.
    /// </summary>
    private static (int Released, int OwnReleased) ReleaseTrimmed(GameWorld world, Player mainPlayer, int maxReleases)
    {
        if (!Settings.ReleaseTrimmedCorpses.Value || Trimmed.Count == 0)
            return (0, 0);

        // Snapshot: Release mutates Trimmed, and a destroyed body invalidates the
        // enumerator's key as well.
        List<Corpse> due = null;
        foreach (var entry in Trimmed)
        {
            // Before paying for IsPendingAggressor/DamageSourcesQuiet at all -
            // see QuietWindowKnownOpen's own doc for why this is safe.
            if (QuietWindowKnownOpen(entry.Key, entry.Value, mainPlayer))
                continue;

            if (!MayRelease(entry.Key, entry.Value, world, mainPlayer))
                continue;

            (due ??= new List<Corpse>()).Add(entry.Key);
            if (due.Count >= maxReleases)
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
            _newestOwnCorpseLootCount = world.LootList.Count;
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
    ///
    /// A shot leaving the flight list is not the end of that hazard, either:
    /// BotMemory.GetHit(DamageInfo) (EFT/BotMemory.cs:679-700) can defer the
    /// hit it just delivered by a timer — up to ~0.4 s under SAIN — before
    /// calling GetHitInner(damageInfo), and that captured DamageInfo carries
    /// the same frozen source reference ShotStillInFlightFromBody checks
    /// (BotWeaponManager.OnBulletNear/OnGetHit then read source.Position on
    /// it, unprotected by Unity's null check — the same NRE class this
    /// file's class doc and CorpseDamageSources describe for the ordinary
    /// path). So once ShotStillInFlightFromBody stops finding anything, this
    /// branch waits out the same DamageSourceQuietSeconds the ordinary path
    /// below does, on its own timer (TrimState.ShotQuietSince) — cheap,
    /// since the body is already hidden either way.
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
                if (CorpseDamageSources.ShotStillInFlightFromBody(player, world)
                    || Combat.Fire.ArenaFireRuntime.HoldsBody(player, world))
                {
                    if (!state.HeldAsSource)
                    {
                        state.HeldAsSource = true;
                        Plugin.ModLogger.LogInfo(
                            $"Arena corpse release: keeping {corpse.PlayerProfileID} hidden — a shot it fired is still in flight, even though it left the profile-id registry.");
                    }

                    // Reset: a shot back in flight means an earlier quiet
                    // reading no longer applies once it lands again.
                    state.ShotQuietSince = float.NaN;
                    return false;
                }

                if (state.HeldAsSource)
                {
                    state.HeldAsSource = false;
                    Plugin.ModLogger.LogInfo(
                        $"Arena corpse release: nothing {corpse.PlayerProfileID} fired is still in flight; it may go after {DamageSourceQuietSeconds:0} s quiet, now that it has left the profile-id registry.");
                }

                // Bridges BotMemory.GetHit's deferred read for this branch —
                // see this method's own doc. Same shape as the ordinary
                // path's SourcesQuietSince handling below, on its own field.
                if (float.IsNaN(state.ShotQuietSince))
                    state.ShotQuietSince = Time.time;

                return Time.time - state.ShotQuietSince >= DamageSourceQuietSeconds;
            }
        }

        // For a real own corpse this line is not reached: MayRelease only
        // calls this method with isOwnCorpse=true once IsNewestOwnCorpse has
        // already said false, which needs a LATER own body to already be
        // registered — and registering it is exactly what makes
        // StillNamedInRegistry say "no" for this one, on the very first
        // asking (GameWorld.RegisterPlayer overwrites the profile-id
        // registry for every own respawn, verified above and in
        // CorpseDamageSources' own doc). So state.LeftRegistry is already
        // true the first time an own corpse gets here, and every later
        // sweep short-circuits inside the block above. What follows is kept
        // as fail-closed insurance for a StillNamedInRegistry read that
        // could not be answered (it returns "still named" rather than
        // guessing) or for a future respawn path that stops going through
        // RegisterPlayer without this comment being updated — not a path
        // real gameplay exercises today. The test suite's `stillNamed` case
        // (tools/CorpseCleanupChecks/Program.cs) reaches it deliberately, by
        // overriding the registry the way only a test can.
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
