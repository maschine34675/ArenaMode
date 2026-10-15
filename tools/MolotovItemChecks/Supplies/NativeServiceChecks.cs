using ArenaMode.Server.Services;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Path = System.IO.Path;

internal static partial class Program
{
    // Read-only execution of the installed service's actual guards/overrides.
    // Do not start SPT, construct SaveServer or read/write any profile. Its load
    // boundary comes from the real SaveCallbacks metadata, not a copied constant.
    private static void NativeItemServiceContract(TemplateItem donor,NewItemFromCloneDetails definition)
    {
        string runtime=Environment.GetEnvironmentVariable("ARENA_MOLOTOV_TEST_RUNTIME")
            ?? Path.GetFullPath(Path.Combine(Environment.GetCommandLineArgs()[1],"../../SPT_Runtime"));
        string path=Path.Combine(runtime,"SPTarkov.Server.Core.dll");
        Check(File.Exists(path),"installed SPT Core required for item lifecycle contract");
        var context=new NativeSptContext(runtime);
        try
        {
            var core=context.LoadFromAssemblyPath(path);
            Type Required(string name)=>core.GetType("SPTarkov.Server.Core."+name,true);
            int Priority(Type type)
            {
                var attribute=type.GetCustomAttributesData().Single(a=>a.AttributeType.FullName=="SPTarkov.DI.Annotations.Injectable");
                return (int)attribute.NamedArguments.Single(a=>a.MemberName=="TypePriority").TypedValue.Value;
            }
            int ours=Priority(typeof(ArenaMolotovRegistration));
            int profileSnapshot=Priority(Required("Callbacks.SaveCallbacks"));
            var serviceType=Required("Services.Modding.Custom.CustomItemService");
            var service=RuntimeHelpers.GetUninitializedObject(serviceType);
            var loaded=serviceType.GetProperty("ProfilesLoaded")??throw new InvalidOperationException("Installed SPT has no inspected ProfilesLoaded contract.");
            var guard=serviceType.GetMethod("EnsureItemsCanStillBeAdded",BindingFlags.NonPublic|BindingFlags.Instance)
                ??throw new InvalidOperationException("Installed SPT item lifecycle guard changed.");
            object nativeId=Activator.CreateInstance(Required("Models.Common.MongoId"),ArenaMode.Shared.ArenaMolotovItem.TemplateId);
            bool GuardAllows(bool profilesLoaded)
            {
                loaded.SetValue(service,profilesLoaded);
                try {guard.Invoke(service,[nativeId]);return true;}
                catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException error
                    &&error.Message.Contains("items must be added before profiles load")){return false;}
            }
            Check(!GuardAllows(true),"actual installed SPT guard rejects late item registration");
            Check(GuardAllows(ours>=profileSnapshot),"native item registration precedes profile snapshot");
            Check(ours<Priority(typeof(ArenaKitSuppliesPostLoad)),"registration precedes late kit snapshot");

            // Also run CreateItemFromClone itself in the forbidden state: it must
            // reach the profile guard before touching any unavailable service.
            var nativeRequest=Activator.CreateInstance(Required("Models.Spt.Mod.NewItemFromCloneDetails"));
            nativeRequest.GetType().GetProperty("NewId").SetValue(nativeRequest,nativeId);
            loaded.SetValue(service,true);bool refused=false;
            try {serviceType.GetMethod("CreateItemFromClone").Invoke(service,[nativeRequest,null]);}
            catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException error
                &&error.Message.Contains("items must be added before profiles load")){refused=true;}
            Check(refused,"actual clone API enforces the profile-load guard before publication");

            // Execute SPT's real override method with its own DTOs and converters,
            // rather than assuming that zero/false/empty values overwrite donor values.
            var options=new JsonSerializerOptions{DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
            var converters=Required("Utils.Json.SptJsonConverterRegistrator");
            foreach(var converter in (IEnumerable<JsonConverter>)converters.GetMethod("GetJsonConverters").Invoke(Activator.CreateInstance(converters),null))
                options.Converters.Add(converter);
            var itemType=Required("Models.Eft.Common.Tables.TemplateItem");
            var nativeItem=JsonSerializer.Deserialize(Json(donor),itemType,options);
            itemType.GetProperty("Id").SetValue(nativeItem,nativeId);
            var nativeOverrides=JsonSerializer.Deserialize(Json(definition.OverrideProperties),Required("Models.Eft.Common.Tables.TemplateItemProperties"),options);
            serviceType.GetMethod("UpdateBaseItemPropertiesWithOverrides",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(service,[nativeOverrides,nativeItem]);
            var roundTrip=JsonSerializer.Deserialize<TemplateItem>(JsonSerializer.Serialize(nativeItem,itemType,options),SptOptions());
            Check(ArenaMolotovRegistration.IsHarmless(roundTrip),"actual installed SPT overrides produce harmless native DTO");
            Check(roundTrip.Properties.Prefab.Path==donor.Properties.Prefab.Path&&roundTrip.Parent==donor.Parent,"actual native override preserves donor rig and parent");

            // Execute the installed bulk flea mutation against temporary native
            // tables. First reproduce the regression without our exclusion, then
            // use the list produced by real registration. No live config changes.
            Check(ours<Priority(Required("Callbacks.GameCallbacks")),"registration protects flea ID before native post-database actions");
            var bulkType=Required("Services.Server.PostDbLoadService");
            var bulk=RuntimeHelpers.GetUninitializedObject(bulkType);
            var tableType=Required("Models.Spt.Tables.TemplateTable");
            var table=Activator.CreateInstance(tableType);
            var nativeRows=(System.Collections.IDictionary)Activator.CreateInstance(tableType.GetProperty("Items").PropertyType);
            tableType.GetProperty("Items").SetValue(table,nativeRows);
            var configType=Required("Models.Spt.Config.RagfairConfig");
            var config=Activator.CreateInstance(configType);
            object NewChild(object parent,string name)
            {
                var member=parent.GetType().GetProperty(name);
                var value=Activator.CreateInstance(member.PropertyType);member.SetValue(parent,value);return value;
            }
            var dynamic=NewChild(config,"Dynamic");
            var nativeBlacklist=NewChild(dynamic,"Blacklist");
            var custom=NewChild(nativeBlacklist,"Custom");
            void AddExcluded(object id)=>custom.GetType().GetMethod("Add").Invoke(custom,[id]);
            foreach(var pair in new[]{(tableType,table),(configType,config)})
                bulkType.GetFields(BindingFlags.NonPublic|BindingFlags.Instance).Single(f=>f.FieldType==pair.Item1).SetValue(bulk,pair.Item2);
            var bulkMethod=bulkType.GetMethod("SetAllDbItemsAsSellableOnFlea",BindingFlags.NonPublic|BindingFlags.Instance);
            var nativeProperties=itemType.GetProperty("Properties").GetValue(nativeItem);
            var sell=nativeProperties.GetType().GetProperty("CanSellOnRagfair");
            nativeRows.Add(nativeId,nativeItem);
            bulkMethod.Invoke(bulk,null);
            Check((bool?)sell.GetValue(nativeProperties)==true,"actual native bulk flea pass reproduces missing-exclusion mutation");
            sell.SetValue(nativeProperties,false);

            var existingBlockedId=new SPTarkov.Server.Core.Models.Common.MongoId("a12e00000000000000000002");
            var exclusions=new HashSet<SPTarkov.Server.Core.Models.Common.MongoId>{existingBlockedId};
            var registeredRows=new Dictionary<SPTarkov.Server.Core.Models.Common.MongoId,TemplateItem>{{DonorId,Clone(donor)}};
            Check(ArenaMolotovRegistration.TryRegister(registeredRows,request=>Publish(registeredRows,request),exclusions,out _),"registration before native flea pass succeeds");
            foreach(var id in exclusions)AddExcluded(Activator.CreateInstance(nativeId.GetType(),id.ToString()));
            object AddControl(string id)
            {
                var control=JsonSerializer.Deserialize(Json(donor),itemType,options);
                var controlId=Activator.CreateInstance(nativeId.GetType(),id);
                itemType.GetProperty("Id").SetValue(control,controlId);
                var props=itemType.GetProperty("Properties").GetValue(control);
                sell.SetValue(props,false);nativeRows.Add(controlId,control);return props;
            }
            var ordinary=AddControl(donor.Id.ToString());
            var alreadyBlocked=AddControl(existingBlockedId.ToString());
            bulkMethod.Invoke(bulk,null);
            Check((bool?)sell.GetValue(nativeProperties)==false,"production private exclusion survives actual native bulk flea pass");
            Check((bool?)sell.GetValue(ordinary)==true,"ordinary unsellable control still becomes sellable in native bulk pass");
            Check((bool?)sell.GetValue(alreadyBlocked)==false,"existing third-party exclusion survives native bulk pass");
            roundTrip=JsonSerializer.Deserialize<TemplateItem>(JsonSerializer.Serialize(nativeItem,itemType,options),SptOptions());
            Check(ArenaMolotovRegistration.IsHarmless(roundTrip),"native bulk pass retains full harmless private template");
            Console.WriteLine($"Native item contract: Core {core.GetName().Version}; SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}; registration {ours} before profile snapshot {profileSnapshot}.");
        }
        finally {context.Unload();}
    }

    private sealed class NativeSptContext(string directory):AssemblyLoadContext(isCollectible:true)
    {
        protected override Assembly Load(AssemblyName name)
        {
            if(name.Name?.StartsWith("SPTarkov.",StringComparison.Ordinal)!=true)return null;
            string candidate=Path.Combine(directory,name.Name+".dll");
            return File.Exists(candidate)?LoadFromAssemblyPath(candidate):null;
        }
    }
}
