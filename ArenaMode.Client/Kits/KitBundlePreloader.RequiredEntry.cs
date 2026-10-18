using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ArenaMode.Kits;

public static partial class KitBundlePreloader
{
    /// <summary>
    /// Required first-body assets, independent of the optional warmup setting.
    /// Native PMC prefab loading already ran on the original gear before arming.
    /// No active-raid cache is trusted across this loading boundary.
    /// </summary>
    internal static async Task PreloadEntryEquipment(InventoryEquipment equipment, Func<bool> stillCurrent)
    {
        var factory = Singleton<ItemFactory>.Instance;
        var pools = Singleton<ObjectsFactory>.Instance;
        var token = ObjectsFactory.DefaultCancellationToken;
        bool Current() => stillCurrent() && !token.IsCancellationRequested
            && token == ObjectsFactory.DefaultCancellationToken
            && ReferenceEquals(factory, Singleton<ItemFactory>.Instance)
            && ReferenceEquals(pools, Singleton<ObjectsFactory>.Instance)
            && pools != null && pools.IsPoolReady(ObjectsFactory.PoolsCategory.Raid);
        if (equipment == null || factory == null || !Current())
            throw new InvalidOperationException("Tactical kit entry has no current raid pools.");
        var templates = equipment.GetAllItems().Select(item => item.TemplateId.ToString()).Distinct().ToArray();
        if (templates.Any(tpl => !factory.ItemTemplates.ContainsKey(new MongoID(tpl))))
            throw new InvalidOperationException("Tactical kit entry contains an unknown item template.");
        // This is first-body readiness, not best-effort background warmup:
        // malformed resource metadata must fail the protected start as well.
        var keys = templates.SelectMany(tpl => factory.ItemTemplates[new MongoID(tpl)].AllResources)
            .Where(resource => resource != ResourceKey.EmptyResourceKey).Distinct().ToArray();
        if (keys.Length == 0) throw new InvalidOperationException("Tactical kit entry has no prefab resources.");
        using (KitOpticPreloadGuard.Begin(pools, keys, loadingScreen: true))
            await pools.LoadBundlesAndCreatePools(ObjectsFactory.PoolsCategory.Raid,
                ObjectsFactory.AssemblyType.Local, keys, JobYieldPriority.General, null, token);
        if (!Current())
            throw new InvalidOperationException("Tactical kit entry was cancelled while loading its required assets.");
    }
}
