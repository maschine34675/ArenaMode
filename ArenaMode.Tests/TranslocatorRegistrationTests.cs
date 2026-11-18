using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;

namespace ArenaMode.Tests;

[CollectionDefinition("Translocator registration", DisableParallelization = true)]
public sealed class TranslocatorRegistrationCollection { }

[Collection("Translocator registration")]
public sealed class TranslocatorRegistrationTests
{
    internal static readonly MongoId DeviceId = new(ArenaTranslocatorItem.TemplateId);
    internal static readonly MongoId DonorId = new(ArenaTranslocatorItem.DonorTemplateId);
    internal static readonly MongoId OtherId = new("a12e00000000000000000077");

    internal static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters()) options.Converters.Add(converter);
        return options;
    }
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions());
    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(Json(value), JsonOptions())!;
    internal static Dictionary<MongoId, TemplateItem> Fresh() => new()
    {
        [DonorId] = JsonSerializer.Deserialize<TemplateItem>("""
        { "_id":"61605e13ffa6e502ac5e7eef", "_type":"Item", "_name":"item_ranger_1500",
          "_parent":"61605ddea09d851a0a0c1bbc", "_props": {
          "Width":1,"Height":1,"StackMaxSize":1,"ExaminedByDefault":true,
          "CanSellOnRagfair":true,"CanRequireOnRagfair":false,
          "Prefab":{"path":"assets/content/weapons/usable_items/item_ranger_1500/item_ranger_1500_container.bundle","rcid":""},
          "UsePrefab":{"path":"","rcid":""},"Weight":0.218,"Unlootable":true} }
        """, JsonOptions())!
    };

    // Only the native clone/publication boundary is doubled. Registration,
    // ownership/readback/refusal and rollback below are linked production code.
    internal static CreateItemResult Publish(IDictionary<MongoId, TemplateItem> items, NewItemFromCloneDetails request)
    {
        var item = Clone(items[request.ItemTplToClone]);
        item.Id = request.NewId; item.Parent = request.ParentId; item.Name = request.NewItemName;
        foreach (var property in typeof(TemplateItemProperties).GetProperties())
        {
            var value = property.GetValue(request.OverrideProperties);
            if (value != null && property.CanWrite) property.SetValue(item.Properties, value);
        }
        items.Add(item.Id, item);
        return new CreateItemResult { Success = true, ItemId = item.Id, Errors = [] };
    }
    internal static bool Register(Dictionary<MongoId, TemplateItem> items, HashSet<MongoId>? exclusions = null,
        Func<NewItemFromCloneDetails, CreateItemResult>? publish = null)
        => ArenaTranslocatorRegistration.TryRegister(items, publish ?? (request => Publish(items, request)),
            exclusions ?? [OtherId], out _);

    [Fact]
    public void DefinitionHasNoOrdinaryCommerceOrLootEntry()
    {
        var definition = ArenaTranslocatorRegistration.Definition();
        Assert.Equal(DonorId, definition.ItemTplToClone);
        Assert.Equal(DeviceId, definition.NewId);
        Assert.Equal(ArenaTranslocatorItem.ParentTemplateId, definition.ParentId.ToString());
        Assert.False(definition.AddToHandbook);
        Assert.False(definition.AddToFleaPriceDb);
        Assert.False(definition.AddToWeaponShelf);
        Assert.Equal(ArenaTranslocatorItem.Name, definition.Locales["en"].Name);
        Assert.NotEmpty(definition.Locales["ge"].Description!);
    }

    [Fact]
    public void SuccessfulRegistrationPreservesDonorAndExistingExclusions()
    {
        var items = Fresh(); var original = Json(items[DonorId]);
        var exclusions = new HashSet<MongoId> { OtherId };
        Assert.True(Register(items, exclusions));
        Assert.Equal(original, Json(items[DonorId]));
        Assert.Equal(2, items.Count);
        Assert.True(ArenaTranslocatorRegistration.IsDevice(items[DeviceId]));
        Assert.True(ArenaTranslocatorRegistration.IsAvailable);
        Assert.True(exclusions.SetEquals([OtherId, DeviceId]));
        Assert.NotSame(items[DonorId], items[DeviceId]);
        Assert.NotSame(items[DonorId].Properties, items[DeviceId].Properties);
        Assert.Equal(items[DonorId].Properties!.Prefab!.Path, items[DeviceId].Properties!.Prefab!.Path);
        Assert.True(Register(items, exclusions, _ => throw new Exception("idempotence must not clone")));
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void ForeignIdIsNeverAdopted()
    {
        var first = Fresh(); Assert.True(Register(first));
        var items = Fresh(); items.Add(DeviceId, Clone(first[DeviceId]));
        var foreign = items[DeviceId]; var before = Json(foreign);
        var exclusions = new HashSet<MongoId> { OtherId };
        Assert.False(Register(items, exclusions, _ => throw new Exception("foreign ID must stop before native service")));
        Assert.Same(foreign, items[DeviceId]); Assert.Equal(before, Json(foreign));
        Assert.True(exclusions.SetEquals([OtherId]));
        Assert.False(ArenaTranslocatorRegistration.IsAvailable);
    }

    [Fact]
    public void ReplacedOrChangedRegisteredObjectRevokesAvailability()
    {
        var items = Fresh(); Assert.True(Register(items));
        items[DeviceId] = Clone(items[DeviceId]);
        Assert.False(ArenaTranslocatorRegistration.IsAvailable);
        items = Fresh(); Assert.True(Register(items));
        items[DeviceId].Properties!.CanSellOnRagfair = true;
        Assert.False(ArenaTranslocatorRegistration.IsAvailable);
        items = Fresh(); Assert.True(Register(items));
        items.Remove(DeviceId);
        Assert.False(ArenaTranslocatorRegistration.IsAvailable);
    }

    [Fact]
    public void MissingBlacklistRefusesBeforeNativePublication()
    {
        var items = Fresh(); Assert.True(Register(items));
        items = Fresh();
        var calls = 0;
        Assert.False(ArenaTranslocatorRegistration.TryRegister(items, _ => { calls++; throw new Exception("must not clone"); }, null, out _));
        Assert.Equal(0, calls);
        Assert.False(items.ContainsKey(DeviceId));
        Assert.False(ArenaTranslocatorRegistration.IsAvailable);
    }

    [Fact]
    public void IncompatibleDonorsAreRejected()
    {
        var edits = new Action<Dictionary<MongoId, TemplateItem>>[]
        {
            x => x.Remove(DonorId), x => x[DonorId].Type = "Node",
            x => x[DonorId].Parent = OtherId, x => x[DonorId].Properties!.Width = 2,
            x => x[DonorId].Properties!.Height = 0, x => x[DonorId].Properties!.StackMaxSize = 2,
            x => x[DonorId].Properties = null,
            x => x[DonorId].Properties!.Prefab = null,
            x => x[DonorId].Properties!.Prefab!.Path = "unexpected.bundle",
            x => x[DonorId].Properties!.UsePrefab!.Path = "foreign-use.bundle"
        };
        foreach (var edit in edits)
        {
            var items = Fresh(); edit(items);
            var calls = 0;
            Assert.False(Register(items, publish: _ => { calls++; throw new Exception("invalid donor must not clone"); }));
            Assert.Equal(0, calls);
            Assert.False(items.ContainsKey(DeviceId));
            Assert.False(ArenaTranslocatorRegistration.IsAvailable);
        }
    }

    [Fact]
    public void RejectedOrPartialPublicationIsRemoved()
    {
        foreach (var failure in new[] { "throw-before", "throw-after", "reject", "wrong-id", "missing", "null-result", "ignored-overrides" })
        {
            var items = Fresh(); var before = Json(items[DonorId]); var exclusions = new HashSet<MongoId> { OtherId };
            Assert.False(Register(items, exclusions, definition =>
            {
                if (failure == "throw-before") throw new Exception("before publication");
                if (failure == "null-result") return null!;
                if (failure == "missing") return new() { Success = true, ItemId = DeviceId, Errors = [] };
                var result = Publish(items, definition);
                if (failure == "throw-after") throw new Exception("after publication");
                if (failure == "reject") result.Success = false;
                if (failure == "wrong-id") result.ItemId = OtherId;
                if (failure == "ignored-overrides") items[DeviceId].Properties!.CanSellOnRagfair = true;
                return result;
            }));
            Assert.False(items.ContainsKey(DeviceId));
            Assert.Equal(before, Json(items[DonorId]));
            Assert.True(exclusions.SetEquals([OtherId]));
            Assert.False(ArenaTranslocatorRegistration.IsAvailable);
        }
    }

    [Fact]
    public void EveryDeviceInvariantIsReadBackAfterPublication()
    {
        var edits = new Action<TemplateItem>[]
        {
            x => x.Id = OtherId, x => x.Parent = OtherId, x => x.Type = "Node", x => x.Properties!.Width = 0,
            x => x.Properties = null,
            x => x.Properties!.Height = 2, x => x.Properties!.StackMaxSize = 2,
            x => x.Properties!.ExaminedByDefault = false, x => x.Properties!.CanRequireOnRagfair = true,
            x => x.Properties!.CanSellOnRagfair = true, x => x.Properties!.Prefab = null,
            x => x.Properties!.Prefab!.Path = "foreign.bundle", x => x.Properties!.UsePrefab!.Path = "foreign-use.bundle"
        };
        foreach (var edit in edits)
        {
            var items = Fresh();
            Assert.False(Register(items, publish: definition => { var result = Publish(items, definition); edit(items[DeviceId]); return result; }));
            Assert.False(items.ContainsKey(DeviceId)); Assert.False(ArenaTranslocatorRegistration.IsAvailable);
        }
    }

    [Fact]
    public void SharedPropertiesWithDonorAreNeverAccepted()
    {
        var items = Fresh();
        // An otherwise compatible donor may itself already be excluded from flea.
        // That must not let a shallow clone share writable template properties.
        items[DonorId].Properties!.CanSellOnRagfair = false;
        var original = Json(items[DonorId]);
        Assert.False(Register(items, publish: definition =>
        {
            var result = Publish(items, definition);
            items[DeviceId].Properties = items[DonorId].Properties;
            return result;
        }));
        Assert.Equal(original, Json(items[DonorId]));
        Assert.False(items.ContainsKey(DeviceId));
        Assert.False(ArenaTranslocatorRegistration.IsAvailable);
    }

    [Fact]
    public void PrivateSupplyCannotEnterRandomEquipmentSelection()
    {
        Assert.True(ArenaTemplateExclusions.IsExcluded(ArenaTranslocatorItem.TemplateId, ArenaTranslocatorItem.Name, []));
        Assert.Contains(ArenaTranslocatorItem.TemplateId, ArenaTemplateExclusions.ExclusionSet(null));
        Assert.False(ArenaTemplateExclusions.IsExcluded(ArenaTranslocatorItem.DonorTemplateId, "Rangefinder", []));
    }
}
