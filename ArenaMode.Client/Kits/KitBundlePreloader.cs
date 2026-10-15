using Comfort.Common;
using Diz.Jobs;
using EFT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArenaMode.Kits;

/// <summary>
/// Loads the asset bundles of every item a kit conjures. The game only pools
/// bundles for items that exist in the raid at start — a kit weapon without this
/// preload has no prefab pool, the hands-controller factory returns null, and the
/// auto-draw NREs (frozen camera at raid start; found in the first M2 playtest).
/// Mirrors the vanilla bot-profile preload (BotProfileClient.LoadBundles).
///
/// Two halves. This file is the raid's: it asks per kit, right before a swap,
/// and waits. KitBundlePreloader.LoadingScreen.cs is the loading screen's: it
/// asks once for the raid ahead and does not wait, and the raid collects that
/// work when it starts. The split is deliberate — the offline check tools link
/// this file with stand-ins for the game, and the other half's dependencies
/// (settings, session, modes) would drag half the mod in with it.
/// </summary>
public static partial class KitBundlePreloader
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
    private static int _loadsCompleted;

    /// <summary>
    /// Moves every time a load finishes, in the raid or in the loading screen.
    /// The load boost folds it into the signature it watches: a load that is
    /// genuinely progressing must look different from a counter that is stuck,
    /// or a long kit warm-up would be released as "stuck" after ninety seconds.
    /// Process-lifetime on purpose — it is only ever compared for change, and
    /// a reset would itself read as a change.
    /// </summary>
    internal static int LoadsCompleted => _loadsCompleted;

    /// <summary>Successful pool warm-up in the current raid, including adopted loading work.</summary>
    internal static bool IsTemplateWarmed(string templateId)
        => templateId != null && WarmedTemplates.Contains(templateId);

    // Includes the live raid-start swap, whose preload precedes the respawn
    // driver's pipeline flag, and a loading-screen batch that is still running
    // after the raid began. Only used to defer optional catalog work and to let
    // the load boost see a load it should be helping.
    internal static bool IsPreloading => _activePoolLoads > 0 || IsRaidStartRunning;

    /// <summary>
    /// Pools are per-raid (PoolsCategory.Raid). Called through
    /// ArenaRespawnDriver.ResetSession at raid START — before the raid-start
    /// swap, in the ordinary and the arena branch of OnRaidStarted — and again
    /// at raid end. The loading-screen preload is exempt on purpose: it is made
    /// for the raid that is starting, and adopted right after this runs.
    /// </summary>
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

        // A loading-screen batch that covers part of this kit is in flight.
        // Waiting is bounded by that one batch — one kit or one rung — and
        // avoids asking the factory for bundles it is already retaining, which
        // leaks a retain token per overlapped bundle (vanilla does this to
        // itself; it need not be added to).
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

            unchecked { _loadsCompleted++; }
            Plugin.ModLogger.LogDebug($"Kit '{kit.Id}': {cold.Count} new item bundles preloaded.");
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
    /// other loading work. Whatever the loading screen warmed already is
    /// skipped here the same way.
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
}
