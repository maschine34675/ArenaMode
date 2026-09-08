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

    // Pools are per-raid (PoolsCategory.Raid) — cleared on raid end via ResetSession.
    public static void Reset()
    {
        unchecked { _raidEpoch++; }
        WarmedTemplates.Clear();
        KitOpticPreloadGuard.Reset();
    }

    /// <summary>Coroutine: resolves all kit templates and awaits the bundle/pool load.</summary>
    public static IEnumerator Preload(KitDefinition kit)
    {
        int epoch = _raidEpoch;
        if (kit == null || !IsCurrentRaid(epoch))
            yield break;

        var cold = ColdTemplates(kit);
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
            // GameWorld disposal cancels the raid pool request. Never publish
            // its templates into the next raid's warmed cache.
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

    /// <summary>The kit's templates that have not been warmed yet.</summary>
    private static List<string> ColdTemplates(KitDefinition kit)
    {
        var cold = new List<string>();

        void Consider(string tpl)
        {
            if (!string.IsNullOrEmpty(tpl) && !WarmedTemplates.Contains(tpl) && !cold.Contains(tpl))
                cold.Add(tpl);
        }

        foreach (var node in kit.Items)
        {
            // The root maps onto the live equipment and has nothing to load.
            if (!string.IsNullOrEmpty(node.ParentId))
                Consider(node.Tpl);
        }

        foreach (var entry in kit.Fill?.RigContents ?? new List<KitStackedItem>())
            Consider(entry.Tpl);

        return cold;
    }

    /// <summary>
    /// Warms every catalog kit in the background, one after another.
    ///
    /// Random kits are drawn per life, and a kit is only preloaded the first time
    /// it comes up — which would stall that respawn while its bundles load. Doing
    /// it up front costs nothing noticeable: the loads are sequential, already-warm
    /// kits are skipped, and the raid is playable throughout.
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

        var resources = new List<ResourceKey>();

        void Collect(string tpl)
        {
            if (string.IsNullOrEmpty(tpl))
                return;

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

        foreach (var tpl in templates)
            Collect(tpl);

        var keys = resources
            .Where(resource => resource != ResourceKey.EmptyResourceKey)
            .Distinct()
            .ToArray();

        if (keys.Length == 0)
            return;

        // Keep ownership in this async operation, not in the yielding coroutine:
        // a disposed coroutine must not leak ownership into another request, and
        // a native pool job may still finish after its caller stopped polling.
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
}
