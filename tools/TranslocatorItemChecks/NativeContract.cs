using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using ArenaMode.Tests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Path = System.IO.Path;

internal static partial class Program
{
    // Execute installed SPT's late-registration guard, override implementation
    // and flea-unlock pass on isolated native DTOs. No server, SaveServer,
    // profile, trader or live config is constructed or changed.
    private static void NativeContract(string runtime, string database)
    {
        var path = Path.Combine(runtime, "SPTarkov.Server.Core.dll");
        Check(File.Exists(path), "installed SPT Core required");
        using var document = JsonDocument.Parse(File.ReadAllText(database));
        var donor = JsonSerializer.Deserialize<TemplateItem>(document.RootElement.GetProperty(ArenaTranslocatorItem.DonorTemplateId).GetRawText(), TranslocatorRegistrationTests.JsonOptions())!;
        var rows = new Dictionary<MongoId, TemplateItem> { [TranslocatorRegistrationTests.DonorId] = donor };
        var exclusions = new HashSet<MongoId> { TranslocatorRegistrationTests.OtherId };
        Check(TranslocatorRegistrationTests.Register(rows, exclusions), "actual installed donor registers");
        var load = new NativeSptContext(runtime);
        try
        {
            var core = load.LoadFromAssemblyPath(path);
            Type Required(string name) => core.GetType("SPTarkov.Server.Core." + name, true)!;
            int Priority(Type type) => (int)type.GetCustomAttributesData()
                .Single(attribute => attribute.AttributeType.FullName == "SPTarkov.DI.Annotations.Injectable")
                .NamedArguments.Single(argument => argument.MemberName == "TypePriority").TypedValue.Value!;
            int ours = Priority(typeof(ArenaTranslocatorRegistration));
            Check(ours < Priority(Required("Callbacks.SaveCallbacks")), "registration precedes native profile snapshot");
            Check(ours < Priority(Required("Callbacks.GameCallbacks")), "registration precedes native flea unlock");
            var serviceType = Required("Services.Modding.Custom.CustomItemService");
            var service = RuntimeHelpers.GetUninitializedObject(serviceType);
            var loaded = serviceType.GetProperty("ProfilesLoaded")!;
            var guard = serviceType.GetMethod("EnsureItemsCanStillBeAdded", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var nativeId = Activator.CreateInstance(Required("Models.Common.MongoId"), ArenaTranslocatorItem.TemplateId)!;
            bool Guard(bool state)
            {
                loaded.SetValue(service, state);
                try { guard.Invoke(service, [nativeId]); return true; }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException failure
                    && failure.Message.Contains("items must be added before profiles load")) { return false; }
            }
            Check(Guard(false) && !Guard(true), "native registration guard distinguishes pre/post profile load");
            var nativeRequest = Activator.CreateInstance(Required("Models.Spt.Mod.NewItemFromCloneDetails"))!;
            nativeRequest.GetType().GetProperty("NewId")!.SetValue(nativeRequest, nativeId);
            var cloneRefused = false;
            try { serviceType.GetMethod("CreateItemFromClone")!.Invoke(service, [nativeRequest, null]); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException failure
                && failure.Message.Contains("items must be added before profiles load")) { cloneRefused = true; }
            Check(cloneRefused, "native clone API reaches profile guard before publication");

            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            var registrator = Required("Utils.Json.SptJsonConverterRegistrator");
            foreach (var converter in (IEnumerable<JsonConverter>)registrator.GetMethod("GetJsonConverters")!.Invoke(Activator.CreateInstance(registrator), null)!)
                options.Converters.Add(converter);
            var itemType = Required("Models.Eft.Common.Tables.TemplateItem");
            var nativeItem = JsonSerializer.Deserialize(TranslocatorRegistrationTests.Json(donor), itemType, options)!;
            itemType.GetProperty("Id")!.SetValue(nativeItem, nativeId);
            var propertiesType = Required("Models.Eft.Common.Tables.TemplateItemProperties");
            var nativeOverrides = JsonSerializer.Deserialize(TranslocatorRegistrationTests.Json(ArenaTranslocatorRegistration.Definition().OverrideProperties), propertiesType, options)!;
            serviceType.GetMethod("UpdateBaseItemPropertiesWithOverrides", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [nativeOverrides, nativeItem]);
            var roundtrip = JsonSerializer.Deserialize<TemplateItem>(JsonSerializer.Serialize(nativeItem, itemType, options), TranslocatorRegistrationTests.JsonOptions());
            Check(ArenaTranslocatorRegistration.IsDevice(roundtrip), "installed native overrides produce verified reusable device");
            Check(roundtrip!.Properties!.Prefab!.Path == donor.Properties!.Prefab!.Path, "native overrides retain device rig");

            var bulkType = Required("Services.Server.PostDbLoadService");
            var bulk = RuntimeHelpers.GetUninitializedObject(bulkType);
            var tableType = Required("Models.Spt.Tables.TemplateTable");
            var table = Activator.CreateInstance(tableType)!;
            var nativeRows = (System.Collections.IDictionary)Activator.CreateInstance(tableType.GetProperty("Items")!.PropertyType)!;
            tableType.GetProperty("Items")!.SetValue(table, nativeRows);
            var configType = Required("Models.Spt.Config.RagfairConfig");
            var config = Activator.CreateInstance(configType)!;
            object Child(object parent, string name)
            {
                var member = parent.GetType().GetProperty(name)!;
                var value = Activator.CreateInstance(member.PropertyType)!; member.SetValue(parent, value); return value;
            }
            var custom = Child(Child(Child(config, "Dynamic"), "Blacklist"), "Custom");
            foreach (var pair in new[] { (tableType, table), (configType, config) })
                bulkType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(field => field.FieldType == pair.Item1).SetValue(bulk, pair.Item2);
            var bulkMethod = bulkType.GetMethod("SetAllDbItemsAsSellableOnFlea", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var nativeProperties = itemType.GetProperty("Properties")!.GetValue(nativeItem)!;
            var sell = nativeProperties.GetType().GetProperty("CanSellOnRagfair")!;
            nativeRows.Add(nativeId, nativeItem);
            bulkMethod.Invoke(bulk, null);
            Check((bool?)sell.GetValue(nativeProperties) == true, "unprotected positive control is changed by native flea unlock");
            sell.SetValue(nativeProperties, false);
            foreach (var id in exclusions)
                custom.GetType().GetMethod("Add")!.Invoke(custom, [Activator.CreateInstance(nativeId.GetType(), id.ToString())]);
            object AddControl(string id)
            {
                var item = JsonSerializer.Deserialize(TranslocatorRegistrationTests.Json(donor), itemType, options)!;
                var key = Activator.CreateInstance(nativeId.GetType(), id)!;
                itemType.GetProperty("Id")!.SetValue(item, key);
                var properties = itemType.GetProperty("Properties")!.GetValue(item)!;
                sell.SetValue(properties, false); nativeRows.Add(key, item); return properties;
            }
            var ordinary = AddControl(ArenaTranslocatorItem.DonorTemplateId);
            var existing = AddControl(TranslocatorRegistrationTests.OtherId.ToString());
            bulkMethod.Invoke(bulk, null);
            Check((bool?)sell.GetValue(nativeProperties) == false, "private device survives actual native flea unlock");
            Check((bool?)sell.GetValue(existing) == false && (bool?)sell.GetValue(ordinary) == true, "native flea unlock preserves existing exclusions and ordinary behavior");
            Console.WriteLine($"Installed item contract: Core={core.GetName().Version}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}; profile-load and flea boundaries executed.");
        }
        finally { load.Unload(); }
    }

    private sealed class NativeSptContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name?.StartsWith("SPTarkov.", StringComparison.Ordinal) != true) return null;
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
