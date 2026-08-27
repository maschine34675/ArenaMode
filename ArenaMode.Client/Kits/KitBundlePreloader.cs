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
    // Pools are per-raid (PoolsCategory.Raid) — cleared on raid end via ResetSession.
    private static readonly HashSet<string> PreloadedThisRaid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static void Reset() => PreloadedThisRaid.Clear();

    /// <summary>Coroutine: resolves all kit templates and awaits the bundle/pool load.</summary>
    public static IEnumerator Preload(KitDefinition kit)
    {
        if (kit == null || PreloadedThisRaid.Contains(kit.Id))
            yield break;

        Task task = null;
        try
        {
            task = PreloadAsync(kit);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Kit '{kit.Id}': bundle preload could not start: {ex.Message}");
            yield break;
        }

        while (task != null && !task.IsCompleted)
            yield return null;

        if (task != null && task.IsFaulted)
        {
            Plugin.ModLogger.LogWarning($"Kit '{kit.Id}': bundle preload failed: {task.Exception?.InnerException?.Message}");
        }
        else
        {
            PreloadedThisRaid.Add(kit.Id);
            Plugin.ModLogger.LogInfo($"Kit '{kit.Id}': item bundles preloaded.");
        }
    }

    private static Task PreloadAsync(KitDefinition kit)
    {
        var factory = Singleton<ItemFactory>.Instance;
        var objectsFactory = Singleton<ObjectsFactory>.Instance;
        if (factory == null || objectsFactory == null)
            return Task.CompletedTask;

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

        foreach (var node in kit.Items)
        {
            if (!string.IsNullOrEmpty(node.ParentId))
                Collect(node.Tpl);
        }

        foreach (var entry in kit.Fill?.RigContents ?? new List<KitStackedItem>())
            Collect(entry.Tpl);

        var keys = resources
            .Where(resource => resource != ResourceKey.EmptyResourceKey)
            .Distinct()
            .ToArray();

        if (keys.Length == 0)
            return Task.CompletedTask;

        return objectsFactory.LoadBundlesAndCreatePools(
            ObjectsFactory.PoolsCategory.Raid,
            ObjectsFactory.AssemblyType.Local,
            keys,
            JobYieldPriority.General,
            null,
            ObjectsFactory.DefaultCancellationToken);
    }
}
