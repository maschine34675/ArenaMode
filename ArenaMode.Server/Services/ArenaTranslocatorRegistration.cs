using ArenaMode.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace ArenaMode.Server.Services;

/// <summary>
/// Registers only the private reusable device. Its native rangefinder parent/rig
/// selects persistent usable-item hands; neither a grenade nor a firearm is
/// converted. Supply remains client-owned after the protected raid snapshot.
/// </summary>
// CustomItemService freezes its database before SaveCallbacks loads profiles.
// This metadata-only registration therefore precedes that boundary; it adds no
// inventory, trader assortment, ordinary loot, handbook or flea-price entry.
[Injectable(TypePriority = OnLoadOrder.Preload)]
public sealed class ArenaTranslocatorRegistration(TemplateTable templates, CustomItemService customItems,
    RagfairConfig ragfairConfig, ISptLogger<ArenaTranslocatorRegistration> logger) : IOnLoad
{
    internal static TemplateItem? RegisteredTemplate { get; private set; }
    private static IDictionary<MongoId, TemplateItem>? _registeredItems;

    internal static bool IsAvailable => RegisteredTemplate != null && _registeredItems != null
        && _registeredItems.TryGetValue(new MongoId(ArenaTranslocatorItem.TemplateId), out var current)
        && ReferenceEquals(current, RegisteredTemplate) && IsDevice(current);

    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        if (TryRegister(templates.Items, details => customItems.CreateItemFromClone(details),
            ragfairConfig.Dynamic?.Blacklist?.Custom, out var problem))
            logger.Debug("[ArenaMode] Private Translocator device registered; no normal supply or commerce entry.");
        else
            logger.Warning($"[ArenaMode] Translocator unavailable ({problem}); existing equipment retained.");
        return Task.CompletedTask;
    }

    internal static bool TryRegister(IDictionary<MongoId, TemplateItem> items,
        Func<NewItemFromCloneDetails, CreateItemResult> create, HashSet<MongoId>? customFleaBlacklist,
        out string problem)
    {
        if (customFleaBlacklist == null)
        { Forget(); problem = "native custom flea blacklist is unavailable"; return false; }
        var id = new MongoId(ArenaTranslocatorItem.TemplateId);
        if (items.TryGetValue(id, out var occupied))
        {
            if (ReferenceEquals(occupied, RegisteredTemplate) && IsDevice(occupied))
                return CompleteRegistration(occupied, items, customFleaBlacklist, out problem);
            Forget();
            problem = "private template ID is already occupied or changed";
            return false; // A lookalike from another owner is not our registration.
        }
        Forget();
        if (!items.TryGetValue(new MongoId(ArenaTranslocatorItem.DonorTemplateId), out var donor)
            || donor.Type != "Item" || donor.Parent.ToString() != ArenaTranslocatorItem.ParentTemplateId
            || !HasNativeDeviceRig(donor.Properties))
        { problem = "native rangefinder donor is missing or incompatible"; return false; }

        try
        {
            var result = create(Definition());
            if (result?.Success != true || result.ItemId != id
                || !items.TryGetValue(id, out var registered) || !IsDevice(registered)
                || ReferenceEquals(registered, donor) || ReferenceEquals(registered.Properties, donor.Properties))
                throw new InvalidOperationException("SPT did not publish the verified private device clone");
            return CompleteRegistration(registered, items, customFleaBlacklist, out problem);
        }
        catch (Exception ex)
        {
            // The private ID was absent before this synchronous call. Native
            // publication can throw after inserting it; do not leave a partial
            // item which a later arm could mistake for a usable device.
            items.Remove(id);
            Forget();
            problem = ex.Message;
            return false;
        }
    }

    private static bool CompleteRegistration(TemplateItem item, IDictionary<MongoId, TemplateItem> items,
        HashSet<MongoId> customFleaBlacklist, out string problem)
    {
        // SPT's later global flea-unlock pass respects this native per-item set.
        // Only the verified private ID is added, never the donor or broad flags.
        customFleaBlacklist.Add(new MongoId(ArenaTranslocatorItem.TemplateId));
        RegisteredTemplate = item;
        _registeredItems = items;
        problem = "";
        return true;
    }

    private static void Forget() { RegisteredTemplate = null; _registeredItems = null; }

    internal static NewItemFromCloneDetails Definition() => new()
    {
        ItemTplToClone = new MongoId(ArenaTranslocatorItem.DonorTemplateId),
        ParentId = new MongoId(ArenaTranslocatorItem.ParentTemplateId),
        NewId = new MongoId(ArenaTranslocatorItem.TemplateId),
        NewItemName = "arena_private_translocator",
        AddToHandbook = false,
        AddToFleaPriceDb = false, // Native service also excludes it from PMC loot.
        AddToWeaponShelf = false,
        Locales = new Dictionary<string, LocaleDetails>
        {
            ["en"] = new() { Name = ArenaTranslocatorItem.Name, ShortName = "Translocator",
                Description = "Fire: throw or recall. Aim: teleport to the disc." },
            ["ge"] = new() { Name = ArenaTranslocatorItem.Name, ShortName = "Translocator",
                Description = "Feuern: werfen oder zurückrufen. Zielen: zur Scheibe teleportieren." }
        },
        OverrideProperties = new TemplateItemProperties
        {
            Name = ArenaTranslocatorItem.Name, ShortName = "Translocator",
            Description = "Arena teleport device.",
            Width = 1, Height = 1, StackMaxSize = 1,
            ExaminedByDefault = true, CanSellOnRagfair = false, CanRequireOnRagfair = false
        }
    };

    internal static bool IsDevice(TemplateItem? item) => item?.Id.ToString() == ArenaTranslocatorItem.TemplateId
        && item.Type == "Item" && item.Parent.ToString() == ArenaTranslocatorItem.ParentTemplateId
        && HasNativeDeviceRig(item.Properties) && item.Properties!.ExaminedByDefault == true
        && item.Properties.CanSellOnRagfair == false && item.Properties.CanRequireOnRagfair == false;

    private static bool HasNativeDeviceRig(TemplateItemProperties? properties) => properties != null
        && properties.Width == 1 && properties.Height == 1 && properties.StackMaxSize == 1
        && properties.Prefab?.Path == ArenaTranslocatorItem.PrefabPath
        && string.IsNullOrEmpty(properties.UsePrefab?.Path);
}
