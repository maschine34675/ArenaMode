using ArenaMode.Server.Services;
using ArenaMode.Shared;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Utils.Json;

internal static partial class Program
{
    private static JsonSerializerOptions SptOptions()
    {
        var result=new JsonSerializerOptions{DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
        foreach(var converter in new SptJsonConverterRegistrator().GetJsonConverters())result.Converters.Add(converter);
        return result;
    }
    private static T Clone<T>(T value)=>System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value,SptOptions()),SptOptions());
    private static string Json<T>(T value)=>System.Text.Json.JsonSerializer.Serialize(value,SptOptions());
    private static readonly MongoId BottleId=new(ArenaMolotovItem.TemplateId);
    private static readonly MongoId DonorId=new(ArenaMolotovItem.DonorTemplateId);

    // Real SPT DTOs, donor data and documented service override semantics. Only
    // cache/locale publication at the service boundary is doubled; registration
    // ownership, integrity validation, rollback and all kit paths are production.
    private static CreateItemResult Publish(IDictionary<MongoId,TemplateItem> items,NewItemFromCloneDetails request)
    {
        Check(!request.AddToHandbook&&!request.AddToFleaPriceDb&&!request.AddToWeaponShelf,"no global handbook/flea/shelf entry");
        Check(request.Locales["en"].Name==ArenaMolotovItem.Name&&request.Locales["ge"].Description.Length>0,"locales supplied");
        var clone=Clone(items[request.ItemTplToClone]);
        clone.Id=request.NewId;clone.Parent=request.ParentId;clone.Name=request.NewItemName;
        foreach(var property in typeof(TemplateItemProperties).GetProperties())
        {
            object value=property.GetValue(request.OverrideProperties);
            if(value!=null&&property.CanWrite)property.SetValue(clone.Properties,value);
        }
        items.Add(clone.Id,clone);
        return new(){Success=true,ItemId=clone.Id,Errors=[]};
    }

    static partial void RegisterMolotov(Dictionary<string,TemplateItem> templates,JObject database)
    {
        var donor=templates[ArenaMolotovItem.DonorTemplateId];string originalDonor=Json(donor);
        var definition=ArenaMolotovRegistration.Definition(donor);
        Check(!definition.AddToHandbook&&!definition.AddToFleaPriceDb&&!definition.AddToWeaponShelf,"no global handbook/flea/shelf entry");
        NativeItemServiceContract(donor,definition);
        Dictionary<MongoId,TemplateItem> Fresh()=>new(){{DonorId,Clone(donor)}};
        var otherBlockedId=new MongoId("a12e00000000000000000002");
        var blacklist=new HashSet<MongoId>{otherBlockedId};
        bool Register(Dictionary<MongoId,TemplateItem> rows,Func<NewItemFromCloneDetails,CreateItemResult> boundary=null)
            =>ArenaMolotovRegistration.TryRegister(rows,boundary??(request=>Publish(rows,request)),blacklist,out _);

        var rows=Fresh();Check(Register(rows),"real donor registers");
        Check(blacklist.SetEquals(new[]{otherBlockedId,BottleId}),"successful registration protects only private flea ID and retains existing exclusions");
        var bottle=rows[BottleId];
        Check(ArenaMolotovRegistration.RegisteredTemplate==bottle,"only exact successful clone published");
        Check(ArenaMolotovRegistration.IsAvailable,"successful native registration authorizes client grant");
        rows.Remove(BottleId);
        Check(!ArenaMolotovRegistration.IsAvailable,"removed registered template revokes client availability");
        rows[BottleId]=Clone(bottle);
        Check(!ArenaMolotovRegistration.IsAvailable,"lookalike replacement revokes client availability");
        rows[BottleId]=bottle;
        Check(ArenaMolotovRegistration.IsAvailable,"exact registered database object restores availability");
        bottle.Properties.Strength=1;
        Check(!ArenaMolotovRegistration.IsAvailable,"unsafe registered template revokes client availability");
        bottle.Properties.Strength=0;
        Check(ArenaMolotovRegistration.IsAvailable,"safe original registered object remains available");
        Check(Json(rows[DonorId])==originalDonor,"native donor unchanged");
        var p=bottle.Properties;
        Check(p.Strength==0&&p.MinExplosionDistance==0&&p.MaxExplosionDistance==0&&p.FragmentsCount==0,"native blast and fragment damage disabled");
        Check(p.Contusion.Value.Y==0&&p.Blindness.Value.Y==0&&p.ArmorDistanceDistanceDamage.Value.Z==0,"native concussion flash armor damage disabled");
        Check(p.ExplosionEffectType==""&&p.CanPlantOnGround==false&&!p.PlayFuzeSound,"native blast presentation and planting disabled");
        Check(p.ExaminedByDefault==true&&p.CanSellOnRagfair==false&&p.CanRequireOnRagfair==false,"item is examined and not flea merchandise");
        Check(p.Prefab.Path==donor.Properties.Prefab.Path&&bottle.Parent==donor.Parent&&p.ThrowType==donor.Properties.ThrowType,"native rig and grenade inheritance retained");
        Check(Register(rows,_=>throw new Exception("must not duplicate")),"registration repeat is idempotent");
        Check(blacklist.Count==2,"registration repeat does not duplicate or replace flea exclusions");
        blacklist.Remove(BottleId);
        Check(Register(rows,_=>throw new Exception("must not duplicate"))&&blacklist.Contains(BottleId),"verified repeated registration restores missing private flea exclusion");
        blacklist.Remove(BottleId);
        var collision=Fresh();collision.Add(BottleId,Clone(bottle));string foreign=Json(collision[BottleId]);
        Check(!Register(collision,_=>throw new Exception("must not call"))&&Json(collision[BottleId])==foreign,"foreign ID collision stays untouched");
        Check(blacklist.SetEquals(new[]{otherBlockedId}),"foreign collision does not blacklist unowned ID");
        Check(ArenaMolotovRegistration.RegisteredTemplate==null,"foreign clone cannot authorize supply");
        Check(!ArenaMolotovRegistration.IsAvailable,"foreign collision cannot authorize client grant");
        foreach(string kind in new[]{"false","throw-before","throw-after","ignored-overrides","wrong-id"})
        {
            rows=Fresh();
            bool accepted=Register(rows,request=>
            {
                if(kind=="throw-before")throw new Exception("before");
                if(kind=="false")return new(){Success=false,ItemId=request.NewId,Errors=["rejected"]};
                var result=Publish(rows,request);
                if(kind=="throw-after")throw new Exception("after");
                if(kind=="ignored-overrides")rows[BottleId].Properties.Strength=donor.Properties.Strength;
                if(kind=="wrong-id")result.ItemId=DonorId;
                return result;
            });
            Check(!accepted&&ArenaMolotovRegistration.RegisteredTemplate==null,"failed registration does not authorize supply "+kind);
            Check(!ArenaMolotovRegistration.IsAvailable,"failed registration has no client availability "+kind);
            Check(!rows.ContainsKey(BottleId),"failed registration leaves no partial explosive clone "+kind);
            Check(Json(rows[DonorId])==originalDonor,"failed registration leaves donor intact "+kind);
            Check(blacklist.SetEquals(new[]{otherBlockedId}),"failed registration preserves native flea exclusions "+kind);
        }
        rows=Fresh();
        Check(!ArenaMolotovRegistration.TryRegister(rows,_=>throw new Exception("must reject before service"),null,out var missingBlacklist)
            &&!rows.ContainsKey(BottleId)&&missingBlacklist.Contains("blacklist"),"missing blacklist fails closed before publication");
        Check(!ArenaMolotovRegistration.IsAvailable,"missing blacklist has no client availability");
        foreach(string kind in new[]{"missing","impact","wrong-type","no-prefab","no-size"})
        {
            rows=Fresh();
            if(kind=="missing")rows.Remove(DonorId);
            if(kind=="impact")rows[DonorId].Properties.MinTimeToContactExplode=0.5;
            if(kind=="wrong-type")rows[DonorId].Properties.ThrowType=ThrowWeapType.smoke_grenade;
            if(kind=="no-prefab")rows[DonorId].Properties.Prefab=null;
            if(kind=="no-size")rows[DonorId].Properties.Width=0;
            Check(!Register(rows,_=>throw new Exception("must reject before service"))&&!rows.ContainsKey(BottleId),"incompatible donor rejected "+kind);
        }
        // Independent per-field refusal checks catch an accidentally weakened
        // read-back verifier even when the normal clone request remains safe.
        var unsafeEdits=new Action<TemplateItemProperties>[] {
            x=>x.Strength=1,x=>x.MinExplosionDistance=1,x=>x.MaxExplosionDistance=1,x=>x.FragmentsCount=1,
            x=>x.Blindness=new(0,1,0),x=>x.Contusion=new(0,1,0),x=>x.ArmorDistanceDistanceDamage=new(0,0,1),
            x=>x.ExplosionEffectType="grenade",x=>x.CanPlantOnGround=true,x=>x.PlayFuzeSound=true,
            x=>x.ExaminedByDefault=false,x=>x.CanSellOnRagfair=true,x=>x.CanRequireOnRagfair=true };
        foreach(var edit in unsafeEdits)
        {
            rows=Fresh();Check(!Register(rows,request=>{var result=Publish(rows,request);edit(rows[BottleId].Properties);return result;}),"unsafe published property rejected");
            Check(!rows.ContainsKey(BottleId),"unsafe published item removed");
        }
        rows=Fresh();Check(Register(rows),"production registration restored");bottle=rows[BottleId];
        templates.Add(ArenaMolotovItem.TemplateId,bottle);
        database[ArenaMolotovItem.TemplateId]=JObject.Parse(Json(bottle));
        CatalogCases(templates,bottle);
    }

    private static void CatalogCases(Dictionary<string,TemplateItem> templates,TemplateItem bottle)
    {
        const string root="55d7217a4bdc2d86028b456d";
        string Kit(string existing=null,bool rawItem=false)=>new JsonArray(new JsonObject{
            ["id"]="fixture",["price"]=123,["items"]=rawItem?new JsonArray(new JsonObject{["_tpl"]=existing}):new JsonArray(new JsonObject{["_tpl"]=root}),
            ["fill"]=new JsonObject{["rigContents"]=existing==null||rawItem?new JsonArray():new JsonArray(new JsonObject{["tpl"]=existing,["count"]=1})}
        }).ToJsonString();
        var supplies=new ArenaKitSupplies(templates.Values);string source=Kit();
        string output=supplies.CreateCatalog(source,out int count);
        var content=JsonNode.Parse(output)[0]["fill"]["rigContents"].AsArray();
        Check(!output.Contains(ArenaMolotovItem.TemplateId),"server catalog never adds a second Molotov supply");
        Check(count==2&&content.Count==2&&content.All(x=>x["tpl"].GetValue<string>()==ArenaKitSupplies.Frag),"server keeps exactly two ordinary RGD-5 supplies");
        Check(content.All(x=>x["optionalQuickUseOnly"].GetValue<bool>()&&x["count"].GetValue<int>()==1),"every optional request remains single and quick-use only");
        Check(output==supplies.CreateCatalog(output,out count)&&count==0,"historical frag transform remains idempotent");
        var absent=new ArenaKitSupplies(templates.Values.Where(t=>t.Id.ToString()!=ArenaMolotovItem.TemplateId));
        Check(absent.CreateCatalog(source,out count)==output&&count==2,"missing private item does not change server supply");
        var unsafeBottle=Clone(bottle);unsafeBottle.Properties.Strength=1;
        var unsafeCatalog=new ArenaKitSupplies(templates.Values.Where(t=>t.Id.ToString()!=ArenaMolotovItem.TemplateId).Append(unsafeBottle));
        Check(unsafeCatalog.CreateCatalog(source,out count)==output&&count==2,"server never supplies unsafe private item either");
        var changedBottle=Clone(bottle);changedBottle.Properties.CanSellOnRagfair=true;
        var changedCatalog=new ArenaKitSupplies(templates.Values.Where(t=>t.Id.ToString()!=ArenaMolotovItem.TemplateId).Append(changedBottle));
        Check(changedCatalog.CreateCatalog(source,out count)==output&&count==2,"late private template mutation cannot add server supply");
        foreach(string grenade in templates.Values.Where(t=>t.Properties?.ThrowType!=null).Select(t=>t.Id.ToString()))
        foreach(bool raw in new[]{false,true})
        {
            source=Kit(grenade,raw);output=supplies.CreateCatalog(source,out count);
            int before=source.Split(ArenaMolotovItem.TemplateId).Length,after=output.Split(ArenaMolotovItem.TemplateId).Length;
            Check(before==after,"authored throwable never gains a Molotov "+grenade);
            if(templates[grenade].Properties.ThrowType==ThrowWeapType.frag_grenade)Check(source==output,"authored frag kit remains byte identical");
        }
        const string emptyItems="[{\"items\":[],\"fill\":{}}]";
        var emptyOutput=supplies.CreateCatalog(emptyItems,out count);
        Check(count==2&&!emptyOutput.Contains(ArenaMolotovItem.TemplateId),"historical empty-items fixture still gains only native frags");
        foreach(string malformed in new[]{"{broken","[]","null","[1]","[{\"items\":[],\"fill\":null}]","[{\"items\":[4],\"fill\":{}}]","[{\"items\":[],\"fill\":{\"rigContents\":[null]}}]",Kit("unknown")})
            Check(supplies.CreateCatalog(malformed,out count)==malformed&&count==0,"malformed/unknown catalog remains byte identical");
    }
}
