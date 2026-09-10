using ArenaMode.Config;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArenaMode.Kits;

/// <summary>
/// The loading screen's half of the preloader: warms the raid's kit bundles
/// while the level is still loading, where the game's own loading force mode
/// gives every load a multiple of the in-raid step budget (three, unless the
/// app config says otherwise), the bundle lock is unlimited, and nothing else
/// is asking for the loader yet. Measured before this existed: 22 s for the
/// start kit on the slow machine, 10 s on the fast one, paid on the first
/// second of the raid and shared with the first bot pair.
///
/// Lifetime is begin → adopt or discard, nothing else. <see cref="Reset"/> in
/// the other half runs at the start of the very raid this is made for and
/// leaves it alone on purpose; ArenaSession adopts it right after that reset
/// and discards it on every path where the raid does not become this arena
/// raid. What was started can not be stopped — there is no cancelling a native
/// pool build — but a discarded batch publishes nothing and dies with the
/// pools of the raid it was started for.
/// </summary>
public static partial class KitBundlePreloader
{
    private static RaidStartPreload _raidStart;

    private static bool IsRaidStartRunning => _raidStart != null && _raidStart.Adopted && _raidStart.IsRunning;

    /// <summary>
    /// Starts loading the raid's kit bundles from inside the loading screen.
    ///
    /// Called from the armed LocalRaidStarted continuation, which is the first
    /// moment all of this is true at once: the arena is armed and its mode
    /// frozen, the map's GameWorld exists and has registered the raid pools,
    /// the kit catalogue has been fetched, and the game's own force mode is on.
    /// What gets loaded is decided by <see cref="KitPreloadPlan"/>; what the
    /// raid opens on is asked the way the raid-start swap will ask it, and for
    /// the random draw that means drawing NOW and reserving the result
    /// (<see cref="KitCatalog.ReserveBaseKit"/>) so the swap hands out the kit
    /// that was warmed rather than a fresh one.
    ///
    /// Fire and forget on purpose. Awaiting it here would serialise the kit
    /// behind nothing and everything else behind the kit; started and left
    /// alone, it shares the loader with the level load and is usually done by
    /// the time the raid asks. The raid asks through <see cref="Preload"/>,
    /// which waits for a batch still in flight instead of loading twice. Only
    /// the opening batch is ever started after the raid has begun; the rest of
    /// a whole-catalogue plan is left to the in-raid warm-up, which knows when
    /// the raid can afford it (after the first bots, after Team Deathmatch has
    /// assembled its teams).
    ///
    /// This runs while <c>ArenaSession.IsActive</c> is still false, and so does
    /// the optic guard it engages — the second deliberate bend of the IsActive
    /// rule, recorded in AGENTS.md beside the first. Everything it creates
    /// lives in this raid's pools and dies with them; if the raid then never
    /// becomes an arena raid, nothing of it is ever handed to the player, and
    /// the guard stays on those pools' role models until the batch is done.
    /// </summary>
    public static void BeginRaidStart()
    {
        DiscardRaidStart("a newer loading screen took over");

        var scope = Settings.KitPreloadInLoadingScreen.Value;
        if (scope == KitPreloadScope.Off || !Session.ArenaSession.ArmedForNextRaid)
            return;

        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var objectsFactory = Singleton<ObjectsFactory>.Instance;
        if (world == null || objectsFactory == null
            || !objectsFactory.IsPoolReady(ObjectsFactory.PoolsCategory.Raid)
            || Singleton<ItemFactory>.Instance == null)
        {
            Plugin.ModLogger.LogWarning(
                "Arena: the loading-screen kit preload found no raid to load into — the kit loads when the raid asks for it.");
            return;
        }

        var mode = Session.ArenaSession.ArmedMode;

        // A protected PitFireTeam raid keeps the PMC's entry loadout: the
        // raid-start swap is skipped and the in-raid warm-up never runs
        // either, so there is nothing to warm and no draw to make. Decided
        // before the plan is asked, so that a whole-catalogue plan does not
        // build twenty kits of pools for a raid that hands out none.
        if (Session.ArenaMenuSelection.CompatibilityTeamArenaActive)
        {
            Plugin.ModLogger.LogInfo(
                "Arena: nothing to preload in the loading screen — the protected 5v5 raid keeps its entry loadout.");
            return;
        }

        var opening = KitPreloadPlan.OpeningKit(mode, false, KitCatalog.ReserveBaseKit, GunGameOpening);

        var wholeCatalogue = scope == KitPreloadScope.WholeCatalogue;
        var gunGame = string.Equals(mode, KitPreloadPlan.GunGameModeId, StringComparison.OrdinalIgnoreCase);
        var batches = KitPreloadPlan.Batches(
            scope,
            opening,
            wholeCatalogue ? KitCatalog.Kits : null,
            wholeCatalogue && gunGame ? LadderCatalog.Rungs : null);

        if (batches.Count == 0)
        {
            Plugin.ModLogger.LogInfo(
                $"Arena: nothing to preload in the loading screen for {mode ?? "<no mode>"} (no kit to hand out).");
            return;
        }

        var preload = new RaidStartPreload(Session.ArenaSession.ArmEpoch, world, batches);
        _raidStart = preload;
        Plugin.ModLogger.LogInfo($"Arena: preloading {Describe(batches)} in the loading screen.");

        // Not awaited, and every failure is caught inside: an unobserved fault
        // on this task would surface as a process-level warning at best, and
        // there is no caller to hand it to.
        _ = RunRaidStartAsync(preload);
    }

    /// <summary>
    /// What Gun Game will open on: the first rung of the ladder it will build,
    /// merged onto the base kit the same way its own CurrentKit merges. Asked
    /// quietly so the mode's own log lines are not printed twice. A ladder
    /// with nothing in it leaves the mode inert and the swap on the base kit,
    /// which is mirrored here too.
    /// </summary>
    private static KitDefinition GunGameOpening()
    {
        var ladder = Modes.GunGameMode.BuildLadder(out var weaponsOnly, quiet: true);
        var first = ladder.Count > 0 ? ladder[0] : null;
        if (first == null)
            return KitCatalog.ReserveBaseKit();

        return weaponsOnly ? KitMerge.WithPrimaryWeapon(KitCatalog.ReserveBaseKit(), first) : first;
    }

    /// <summary>
    /// The raid this preload was started for has become the arena raid: from
    /// now on its finished batches count as warm, the one in flight is waited
    /// for, and the ones it never reached are handed to the in-raid warm-up.
    /// Called by ArenaSession right after ResetSession, before the raid-start
    /// swap asks for its kit. A preload made for a different arming or a
    /// different GameWorld is thrown away instead — its pools belong to a raid
    /// that never happened.
    /// </summary>
    public static void AdoptRaidStart(GameWorld world)
    {
        var preload = _raidStart;
        if (preload == null)
            return;

        if (preload.ArmEpoch != Session.ArenaSession.ArmEpoch || !ReferenceEquals(preload.World, world))
        {
            DiscardRaidStart("it was started for a different raid");
            return;
        }

        preload.Adopted = true;
        preload.AdoptedAt = Time.realtimeSinceStartup;

        int done = 0, running = 0, failed = 0, handedOver = 0;
        foreach (var batch in preload.Batches)
        {
            if (!batch.Started)
            {
                // Never started before the raid began: the in-raid warm-up
                // owns it now, with its own sense of when the raid can afford
                // a load. Marked done so nothing waits for it here.
                batch.Done = true;
                handedOver++;
                continue;
            }

            if (!batch.Done)
                running++;
            else if (batch.Succeeded)
                done++;
            else
                failed++;

            Publish(preload, batch);
        }

        Plugin.ModLogger.LogInfo(
            $"Arena: the loading-screen kit preload is this raid's — {done} batch(es) finished before the raid began, "
            + $"{running} still loading, {handedOver} left to the in-raid warm-up, {failed} failed.");
    }

    /// <summary>
    /// Forgets the loading-screen preload and the base kit drawn for it. A
    /// batch still in flight keeps running to completion inside the pools of
    /// the raid it was started for — there is no cancelling a native pool
    /// build — but its templates are never published anywhere.
    /// </summary>
    public static void DiscardRaidStart(string reason)
    {
        var preload = _raidStart;
        _raidStart = null;
        KitCatalog.ClearReservedBaseKit();

        if (preload != null)
        {
            Plugin.ModLogger.LogInfo(
                $"Arena: loading-screen kit preload discarded ({reason}); "
                + $"{(preload.IsRunning ? "a batch was still loading" : "no batch was loading")}.");
        }
    }

    private static async Task RunRaidStartAsync(RaidStartPreload preload)
    {
        try
        {
            foreach (var batch in preload.Batches)
            {
                // Discarded meanwhile: start nothing further. The batch that
                // was already running when the discard came completes on its
                // own, inside the pools of the raid it was started for.
                if (!ReferenceEquals(_raidStart, preload))
                    return;

                // Handed to the in-raid warm-up at adoption.
                if (batch.Done)
                    continue;

                batch.Started = true;
                batch.StartedAt = Time.realtimeSinceStartup;
                // The token is read at call time on purpose: raid teardown
                // cancels and REPLACES the factory's source, so a token captured
                // now is exactly this raid's. Read again below to tell a load
                // that finished from one the teardown quietly truncated.
                batch.Token = ObjectsFactory.DefaultCancellationToken;
                try
                {
                    await LoadInLoadingScreenAsync(batch);
                    // A teardown during the pool-filling phase does not fault
                    // the task; the game logs, skips the rest and returns as if
                    // done. Such a batch has pools missing and must not count
                    // as warm, or the raid-start swap would draw a weapon with
                    // no prefab pool — the hands softlock the preloader exists
                    // to prevent.
                    batch.Succeeded = !batch.Token.IsCancellationRequested;
                    if (!batch.Succeeded)
                    {
                        Plugin.ModLogger.LogWarning(
                            $"Arena: loading-screen preload of {batch.Label} was cut short by a raid teardown "
                            + "and does not count as warm.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.ModLogger.LogWarning(
                        $"Arena: loading-screen preload of {batch.Label} failed ({ex.GetBaseException().Message}) "
                        + "— the raid loads it again when it needs it.");
                }

                batch.Done = true;
                batch.FinishedAt = Time.realtimeSinceStartup;
                if (batch.Succeeded)
                    unchecked { _loadsCompleted++; }

                if (!ReferenceEquals(_raidStart, preload))
                    continue;

                var took = batch.FinishedAt - batch.StartedAt;
                if (batch.Succeeded)
                {
                    Plugin.ModLogger.LogInfo(preload.Adopted
                        ? $"Arena: loading-screen preload of {batch.Label} ({batch.Templates.Count} templates) finished "
                          + $"{batch.FinishedAt - preload.AdoptedAt:0.0} s after the raid began, {took:0.0} s in all."
                        : $"Arena: loading-screen preload of {batch.Label} ({batch.Templates.Count} templates) finished "
                          + $"in {took:0.0} s, before the raid began.");
                }

                Publish(preload, batch);
            }
        }
        catch (Exception ex)
        {
            // Bookkeeping only can throw here; the loads have their own catch.
            // Mark every batch finished so nothing waits on this preload again.
            foreach (var batch in preload.Batches)
                batch.Done = true;

            Plugin.ModLogger.LogWarning($"Arena: the loading-screen kit preload stopped: {ex}");
        }
    }

    private static async Task LoadInLoadingScreenAsync(RaidStartPreload.Batch batch)
    {
        var factory = Singleton<ItemFactory>.Instance;
        var objectsFactory = Singleton<ObjectsFactory>.Instance;
        if (factory == null || objectsFactory == null)
            return;

        var keys = CollectKeys(factory, batch.Templates);
        if (keys.Length == 0)
            return;

        // Same request the raid makes, same pools, same guard. The guard is
        // told this is the loading screen so it can act before IsActive and
        // keep acting until this call returns. The token is the one the batch
        // captured, so this call and the check after it speak about the same
        // raid.
        using (KitOpticPreloadGuard.Begin(objectsFactory, keys, loadingScreen: true))
        {
            await objectsFactory.LoadBundlesAndCreatePools(
                ObjectsFactory.PoolsCategory.Raid,
                ObjectsFactory.AssemblyType.Local,
                keys,
                JobYieldPriority.General,
                null,
                batch.Token);
        }
    }

    /// <summary>
    /// Counts a finished batch as warm — once, and only for an adopted preload.
    /// Before adoption the templates stay with the batch: the raid that will
    /// own them has not started yet, and Reset clears the warm set when it does.
    /// A batch whose raid token has been cancelled since is refused here too,
    /// whatever its own flags say: its pools are gone or going.
    /// </summary>
    private static void Publish(RaidStartPreload preload, RaidStartPreload.Batch batch)
    {
        if (!preload.Adopted || !batch.Done || !batch.Succeeded || batch.Published
            || batch.Token.IsCancellationRequested)
            return;

        batch.Published = true;
        WarmedTemplates.UnionWith(batch.Templates);
    }

    /// <summary>
    /// Whether an adopted loading-screen batch that covers any of these
    /// templates is in flight right now. Batches that never started do not
    /// count: they belong to the in-raid warm-up from adoption on.
    /// </summary>
    private static bool RaidStartStillLoading(List<string> templates)
    {
        var preload = _raidStart;
        if (preload == null || !preload.Adopted)
            return false;

        foreach (var batch in preload.Batches)
        {
            if (batch.Started && !batch.Done && batch.Overlaps(templates))
                return true;
        }

        return false;
    }

    private static string Describe(IReadOnlyList<KitPreloadPlan.Batch> batches)
    {
        var first = batches[0];
        var text = $"{first.Label} ({first.Templates.Count} templates)";
        if (batches.Count == 1)
            return text;

        var rest = 0;
        for (var i = 1; i < batches.Count; i++)
            rest += batches[i].Templates.Count;

        return $"{text}, then {batches.Count - 1} more batch(es) ({rest} templates) while the loading screen lasts";
    }

    private sealed class RaidStartPreload
    {
        public RaidStartPreload(int armEpoch, GameWorld world, IReadOnlyList<KitPreloadPlan.Batch> batches)
        {
            ArmEpoch = armEpoch;
            World = world;
            Batches = new List<Batch>(batches.Count);
            foreach (var batch in batches)
                Batches.Add(new Batch(batch));
        }

        public int ArmEpoch { get; }
        public GameWorld World { get; }
        public List<Batch> Batches { get; }
        public bool Adopted { get; set; }
        public float AdoptedAt { get; set; }

        /// <summary>A batch has started and not finished.</summary>
        public bool IsRunning
        {
            get
            {
                foreach (var batch in Batches)
                {
                    if (batch.Started && !batch.Done)
                        return true;
                }

                return false;
            }
        }

        public sealed class Batch
        {
            public Batch(KitPreloadPlan.Batch plan)
            {
                Label = plan.Label;
                Templates = new HashSet<string>(plan.Templates, StringComparer.OrdinalIgnoreCase);
            }

            public string Label { get; }
            public HashSet<string> Templates { get; }
            public bool Started { get; set; }
            public bool Done { get; set; }
            public bool Succeeded { get; set; }
            public bool Published { get; set; }
            public float StartedAt { get; set; }
            public float FinishedAt { get; set; }

            /// <summary>The raid pools' cancellation token as read when the batch began.</summary>
            public CancellationToken Token { get; set; }

            public bool Overlaps(List<string> templates)
            {
                foreach (var tpl in templates)
                {
                    if (Templates.Contains(tpl))
                        return true;
                }

                return false;
            }
        }
    }
}
