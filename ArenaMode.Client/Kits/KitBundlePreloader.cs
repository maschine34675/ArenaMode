using ArenaMode.Config;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ArenaMode.Kits;

/// <summary>
/// Loads the asset bundles of every item a kit conjures. The game only pools
/// bundles for items that exist in the raid at start — a kit weapon without this
/// preload has no prefab pool, the hands-controller factory returns null, and the
/// auto-draw NREs (frozen camera at raid start; found in the first M2 playtest).
/// Mirrors the vanilla bot-profile preload (BotProfileClient.LoadBundles).
///
/// Two callers, two moments. The raid itself asks per kit, right before a swap,
/// and waits. The loading screen asks once for the raid ahead, through
/// <see cref="BeginRaidStart"/>, and does not wait: that load runs alongside
/// the rest of the level load, where the game's own loading force mode gives
/// it a multiple of the in-raid step budget (three, unless the app config says
/// otherwise) and the bundle lock is unlimited, and the raid collects it at
/// <see cref="AdoptRaidStart"/>. Measured before this existed: 22 s for the
/// start kit on the slow machine, 10 s on the fast one, paid on the first
/// second of the raid and shared with the first bot pair.
/// </summary>
public static class KitBundlePreloader
{
    /// <summary>
    /// What has already been warmed, tracked by TEMPLATE rather than by kit.
    ///
    /// Keying this on the kit's id looked equivalent and was not. Gun Game
    /// assembles its loadout — KitMerge names the result "basekit+rung" — and
    /// with random kits the base is redrawn every life, so the id was new every
    /// single time and the cache never once hit. Every respawn therefore paid
    /// for a full pool build, which is why death-to-respawn grew from under a
    /// second to thirteen over one raid while the respawn pipeline itself stayed
    /// flat at a third of a second.
    ///
    /// Templates are the honest unit anyway: kits share armour, magazines and
    /// ammunition, so warming by content skips work that warming by name could
    /// never see was already done.
    /// </summary>
    private static readonly HashSet<string> WarmedTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static int _raidEpoch;
    private static int _activePoolLoads;

    /// <summary>
    /// The loading-screen preload for the raid being loaded (or, once adopted,
    /// the raid running). Deliberately NOT cleared by <see cref="Reset"/>: that
    /// runs at the start of the very raid this was made for, before the raid
    /// adopts it. Its lifetime is begin → adopt or discard, nothing else.
    /// </summary>
    private static RaidStartPreload _raidStart;

    // Includes the live raid-start swap, whose preload precedes the respawn
    // driver's pipeline flag, and a loading-screen batch that is still running
    // after the raid began. Only used to defer optional catalog work and to let
    // the load boost see a load it should be helping.
    internal static bool IsPreloading
        => _activePoolLoads > 0
           || (_raidStart != null && _raidStart.Adopted && _raidStart.IsRunning);

    // Pools are per-raid (PoolsCategory.Raid) — cleared on raid end via ResetSession.
    public static void Reset()
    {
        unchecked { _raidEpoch++; }
        _activePoolLoads = 0;
        WarmedTemplates.Clear();
        KitOpticPreloadGuard.Reset();
    }

    /// <summary>Coroutine: resolves all kit templates and awaits the bundle/pool load.</summary>
    public static IEnumerator Preload(KitDefinition kit)
    {
        int epoch = _raidEpoch;
        if (kit == null || !IsCurrentRaid(epoch))
            yield break;

        var templates = KitPreloadPlan.TemplatesOf(kit);

        // A loading-screen batch that covers part of this kit is on its way.
        // Waiting for it is the whole point of having started it early; loading
        // the same bundles a second time alongside would only slow both.
        while (IsCurrentRaid(epoch) && RaidStartStillLoading(templates))
            yield return null;

        if (!IsCurrentRaid(epoch))
            yield break;

        var cold = ColdTemplates(templates);
        if (cold.Count == 0)
            yield break;

        Task task = null;
        try
        {
            task = PreloadAsync(cold);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Kit '{kit.Id}': bundle preload could not start: {ex.Message}");
            yield break;
        }

        while (task != null && !task.IsCompleted)
        {
            if (!IsCurrentRaid(epoch))
                yield break;

            yield return null;
        }

        if (!IsCurrentRaid(epoch) || task?.IsCanceled == true)
        {
            // Raid teardown cancels the request only while it is still
            // retaining bundles; once it is filling pools the game swallows
            // the cancellation and the task ends as if it had succeeded. The
            // epoch check above is what keeps those templates out of the next
            // raid's warm set, not the IsCanceled flag.
            yield break;
        }

        if (task != null && task.IsFaulted)
        {
            Plugin.ModLogger.LogWarning($"Kit '{kit.Id}': bundle preload failed: {task.Exception?.InnerException?.Message}");
        }
        else
        {
            foreach (var tpl in cold)
                WarmedTemplates.Add(tpl);

            Plugin.ModLogger.LogInfo($"Kit '{kit.Id}': {cold.Count} new item bundles preloaded.");
        }
    }

    /// <summary>The given templates that have not been warmed yet.</summary>
    private static List<string> ColdTemplates(List<string> templates)
    {
        var cold = new List<string>();
        foreach (var tpl in templates)
        {
            if (!WarmedTemplates.Contains(tpl))
                cold.Add(tpl);
        }

        return cold;
    }

    /// <summary>
    /// Warms every catalog kit in the background, one after another.
    ///
    /// Random kits are drawn per life, and a kit is only preloaded the first time
    /// it comes up — which would stall that respawn while its bundles load. The
    /// controller schedules this optional work after initial TDM assembly and
    /// current native kit loading. Loads are sequential and already-warm
    /// templates are skipped, but pool creation still shares resources with
    /// other loading work.
    /// </summary>
    public static IEnumerator PreloadAll()
    {
        int epoch = _raidEpoch;
        if (!IsCurrentRaid(epoch))
            yield break;

        var kits = KitCatalog.Kits;
        if (kits != null)
        {
            foreach (var kit in kits)
            {
                if (!IsCurrentRaid(epoch))
                    yield break;

                yield return Preload(kit);
            }
        }

        // Gun Game is the one mode that meets a cold bundle mid-fight: every
        // rung is a weapon this raid has never seen, and the promotion waits for
        // it. Warmed only for that mode — two dozen weapons is a lot of pool to
        // build for a raid that will never draw them.
        if (!(Modes.ArenaMatchService.Current?.Mode is Modes.GunGameMode))
            yield break;

        var rungs = LadderCatalog.Rungs;
        if (rungs == null)
            yield break;

        foreach (var rung in rungs)
        {
            if (!IsCurrentRaid(epoch))
                yield break;

            yield return Preload(rung);
        }
    }

    private static bool IsCurrentRaid(int epoch)
        => epoch == _raidEpoch && Session.ArenaSession.IsActive;

    private static async Task PreloadAsync(List<string> templates)
    {
        var factory = Singleton<ItemFactory>.Instance;
        var objectsFactory = Singleton<ObjectsFactory>.Instance;
        if (factory == null || objectsFactory == null)
            return;

        var keys = CollectKeys(factory, templates);
        if (keys.Length == 0)
            return;

        // Keep ownership in this async operation, not in the yielding coroutine:
        // a disposed coroutine must not leak ownership into another request, and
        // a native pool job may still finish after its caller stopped polling.
        int epoch = _raidEpoch;
        _activePoolLoads++;
        try
        {
            using (KitOpticPreloadGuard.Begin(objectsFactory, keys))
            {
                await objectsFactory.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid,
                    ObjectsFactory.AssemblyType.Local,
                    keys,
                    JobYieldPriority.General,
                    null,
                    ObjectsFactory.DefaultCancellationToken);
            }
        }
        finally
        {
            // Disposing a polling coroutine does not stop its native Task.
            // An old completion must not release a new raid's active load.
            if (epoch == _raidEpoch)
                _activePoolLoads--;
        }
    }

    /// <summary>Every prefab resource behind the given templates, distinct.</summary>
    private static ResourceKey[] CollectKeys(ItemFactory factory, IEnumerable<string> templates)
    {
        var resources = new List<ResourceKey>();

        foreach (var tpl in templates)
        {
            if (string.IsNullOrEmpty(tpl))
                continue;

            try
            {
                if (factory.ItemTemplates.TryGetValue(new MongoID(tpl), out var template))
                    resources.AddRange(template.AllResources);
            }
            catch
            {
                // Unknown tpl — the kit validator reports it; nothing to preload.
            }
        }

        return resources
            .Where(resource => resource != ResourceKey.EmptyResourceKey)
            .Distinct()
            .ToArray();
    }

    // ------------------------------------------------------------------ loading screen

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
    /// which waits for any batch still in flight instead of loading twice.
    ///
    /// This runs while <c>ArenaSession.IsActive</c> is still false, and so does
    /// the optic guard it engages — the second deliberate bend of the IsActive
    /// rule, recorded in AGENTS.md beside the first. Everything it creates
    /// lives in this raid's pools and dies with them; if the raid then never
    /// becomes an arena raid, nothing of it is ever handed to the player.
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
        var opening = KitPreloadPlan.OpeningKit(
            mode,
            Session.ArenaMenuSelection.CompatibilityTeamArenaActive,
            KitCatalog.ReserveBaseKit,
            () => Modes.TacticalMode.OpeningKit(KitCatalog.Kits, Settings.TacticalStartMoney.Value),
            GunGameOpening);

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
                $"Arena: nothing to preload in the loading screen for {mode ?? "<no mode>"} "
                + "(no kit to hand out, or the raid keeps the entry loadout).");
            // A reservation without a batch would still be honoured by the
            // swap, which is fine, but there is nothing for it to wait for.
            return;
        }

        var preload = new RaidStartPreload(Session.ArenaSession.ArmEpoch, world, batches);
        _raidStart = preload;
        Plugin.ModLogger.LogInfo(
            $"Arena: preloading {Describe(batches)} in the loading screen.");

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
    /// now on its finished batches count as warm and its running ones are
    /// waited for. Called by ArenaSession right after ResetSession, before the
    /// raid-start swap asks for its kit. A preload made for a different arming
    /// or a different GameWorld is thrown away instead — its pools belong to a
    /// raid that never happened.
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

        int done = 0, running = 0, failed = 0;
        foreach (var batch in preload.Batches)
        {
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
            + $"{running} still loading, {failed} failed.");
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
                + $"{(preload.IsRunning ? "a batch was still loading" : "all batches had finished")}.");
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
        // told this is the loading screen so it can act before IsActive. The
        // token is the one the batch captured, so this call and the check
        // after it speak about the same raid.
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
    /// templates is still in flight.
    /// </summary>
    private static bool RaidStartStillLoading(List<string> templates)
    {
        var preload = _raidStart;
        if (preload == null || !preload.Adopted)
            return false;

        foreach (var batch in preload.Batches)
        {
            if (!batch.Done && batch.Overlaps(templates))
                return true;
        }

        return false;
    }

    private static string Describe(IReadOnlyList<KitPreloadPlan.Batch> batches)
    {
        var parts = new List<string>(batches.Count);
        foreach (var batch in batches)
            parts.Add($"{batch.Label} ({batch.Templates.Count} templates)");

        return string.Join(", then ", parts);
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

        public bool IsRunning
        {
            get
            {
                foreach (var batch in Batches)
                {
                    if (!batch.Done)
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
            public bool Done { get; set; }
            public bool Succeeded { get; set; }
            public bool Published { get; set; }
            public float StartedAt { get; set; }
            public float FinishedAt { get; set; }

            /// <summary>The raid pools' cancellation token as read when the batch began.</summary>
            public System.Threading.CancellationToken Token { get; set; }

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
