using ArenaMode.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SptVector3 = SPTarkov.Server.Core.Models.Eft.Common.Vector3;

namespace ArenaMode.Server.Services;

/// <summary>
/// A private, natively harmless throwable. The donor supplies its existing rig,
/// animation and physics, not its damage. No trader, profile or loot pool is edited.
/// Only a verified object created by this registration may enter the kit snapshot.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 97999)]
public sealed class ArenaMolotovRegistration(TemplateTable templates, CustomItemService customItems,
    ISptLogger<ArenaMolotovRegistration> logger) : IOnLoad
{
    internal static TemplateItem? RegisteredTemplate { get; private set; }

    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        if (TryRegister(templates.Items, details => customItems.CreateItemFromClone(details), out var problem))
            logger.Info("[ArenaMode] Private Molotov supply registered; native blast disabled, no trader/flea/loot entry.");
        else
            logger.Warning($"[ArenaMode] Molotov supply unavailable ({problem}); existing kit supplies retained.");
        return Task.CompletedTask;
    }

    // The delegate is the single SPT boundary. Tests execute this same admission,
    // verification and rollback path using real SPT DTOs without starting a server.
    internal static bool TryRegister(IDictionary<MongoId, TemplateItem> items,
        Func<NewItemFromCloneDetails, CreateItemResult> create, out string problem)
    {
        var id = new MongoId(ArenaMolotovItem.TemplateId);
        if (items.TryGetValue(id, out var occupied))
        {
            if (ReferenceEquals(occupied, RegisteredTemplate) && IsHarmless(occupied))
            { problem = ""; return true; }
            RegisteredTemplate = null;
            problem = "private template ID is already occupied or changed";
            return false; // Never overwrite another mod's item, even an inert lookalike.
        }
        RegisteredTemplate = null;
        if (!items.TryGetValue(new MongoId(ArenaMolotovItem.DonorTemplateId), out var donor)
            || donor.Type != "Item" || donor.Properties?.ThrowType != ThrowWeapType.frag_grenade
            || !(donor.Properties.MinTimeToContactExplode < 0)
            || string.IsNullOrWhiteSpace(donor.Properties.Prefab?.Path)
            || !(donor.Properties.Width > 0) || !(donor.Properties.Height > 0))
        { problem = "native timed RGD-5 donor is missing or incompatible"; return false; }

        try
        {
            var result = create(Definition(donor));
            if (result?.Success != true || result.ItemId != id
                || !items.TryGetValue(id, out var registered) || !IsHarmless(registered)
                || registered.Parent != donor.Parent
                || registered.Properties!.Prefab?.Path != donor.Properties.Prefab.Path
                || ReferenceEquals(registered, donor) || ReferenceEquals(registered.Properties, donor.Properties))
                throw new InvalidOperationException("SPT did not publish the verified harmless clone");
            RegisteredTemplate = registered;
            problem = "";
            return true;
        }
        catch (Exception ex)
        {
            // The ID was absent immediately before this synchronous registration.
            // SPT can throw after inserting it: never leave an explosive partial clone.
            items.Remove(id);
            problem = ex.Message;
            return false;
        }
    }

    internal static NewItemFromCloneDetails Definition(TemplateItem donor) => new()
    {
        ItemTplToClone = new MongoId(ArenaMolotovItem.DonorTemplateId),
        ParentId = donor.Parent,
        NewId = new MongoId(ArenaMolotovItem.TemplateId),
        NewItemName = "arena_private_molotov",
        AddToHandbook = false,
        AddToFleaPriceDb = false, // SPT also adds this ID to its PMC loot blacklist.
        AddToWeaponShelf = false,
        Locales = new Dictionary<string, LocaleDetails>
        {
            ["en"] = new() { Name = ArenaMolotovItem.Name, ShortName = "Molotov",
                Description = "Arena incendiary. Creates a short-lived fire on impact. Supplied only for Arena matches." },
            ["ge"] = new() { Name = ArenaMolotovItem.Name, ShortName = "Molotov",
                Description = "Arena-Brandflasche. Erzeugt beim Aufprall kurzzeitig Feuer. Nur in Arena-Matches erhältlich." }
        },
        OverrideProperties = new TemplateItemProperties
        {
            Name = ArenaMolotovItem.Name, ShortName = "Molotov",
            Description = "Arena incendiary; native blast disabled.",
            ExaminedByDefault = true, CanSellOnRagfair = false, CanRequireOnRagfair = false,
            Strength = 0, MinExplosionDistance = 0, MaxExplosionDistance = 0, FragmentsCount = 0,
            Contusion = new SptVector3(0, 0, 0), Blindness = new SptVector3(0, 0, 0),
            ArmorDistanceDistanceDamage = new SptVector3(0, 0, 0), ExplosionEffectType = "",
            ExplDelay = 3.5, MinTimeToContactExplode = -1,
            CanPlantOnGround = false, PlayFuzeSound = false
        }
    };

    internal static bool IsHarmless(TemplateItem? item)
    {
        var p = item?.Properties;
        return item?.Id.ToString() == ArenaMolotovItem.TemplateId && item.Type == "Item"
            && p?.ThrowType == ThrowWeapType.frag_grenade && p.Strength == 0
            && p.MinExplosionDistance == 0 && p.MaxExplosionDistance == 0 && p.FragmentsCount == 0
            && Zero(p.Contusion) && Zero(p.Blindness) && Zero(p.ArmorDistanceDistanceDamage)
            && p.ExplosionEffectType == "" && p.CanPlantOnGround == false && !p.PlayFuzeSound
            && p.MinTimeToContactExplode < 0 && p.ExplDelay > 0 && p.ExplDelay <= 10
            && p.ExaminedByDefault == true && p.CanSellOnRagfair == false && p.CanRequireOnRagfair == false
            && p.Width > 0 && p.Height > 0 && !string.IsNullOrWhiteSpace(p.Prefab?.Path);
    }

    private static bool Zero(SptVector3? value) => value is { X: 0, Y: 0, Z: 0 };
}
