using ArenaMode.Kits;
using ArenaMode.Server.Services;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TemplateItem = SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem;
using SPTarkov.Server.Core.Utils.Json;
using Grid = EFT.InventoryLogic.Grid;

internal static partial class Program
{
    private static int _checks;
    private static void Check(bool condition,string message)
    { _checks++;if(!condition)throw new InvalidOperationException("ASSERT: "+message); }
    private static readonly MethodInfo Populate=typeof(KitBuilder).GetMethod("Populate",BindingFlags.NonPublic|BindingFlags.Static)!;
    private static readonly MethodInfo Fill=typeof(KitBuilder).GetMethod("FillGrids",BindingFlags.NonPublic|BindingFlags.Static)!;
    // A second throwable type is retained only for filter-boundary fixtures.
    private static readonly string[] Grenades=[ArenaKitSupplies.Frag,"5a0c27731526d80618476ac4"];
    private static void Main(string[] args)
    {
        string root=args[0],database=args[1];
        string nativeTypes=Path.Combine(root,"../_decompiled/EFT.InventoryLogic/JsonTypes.cs");
        string raw=File.ReadAllText(database);
        var db=JObject.Parse(raw);
        var options=new JsonSerializerOptions{DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
        foreach(var converter in new SptJsonConverterRegistrator().GetJsonConverters())options.Converters.Add(converter);
        var templates=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,TemplateItem>>(raw,options)!;
        RegisterMolotov(templates,db);
        Boundary.Install(db,nativeTypes);
        bool hasMolotov=ArenaMolotovRegistration.RegisteredTemplate!=null;
        var supplies=new ArenaKitSupplies(templates.Values,ArenaMolotovRegistration.RegisteredTemplate);
        foreach(string path in new[]{Path.Combine(root,"ArenaMode.Server/defaults/kits.json"),Path.GetFullPath(Path.Combine(Path.GetDirectoryName(database)!,"../../../user/mods/ArenaMode/config/kits.json"))})
        {
            Check(File.Exists(path),"required real catalog exists "+path);
            string source=File.ReadAllText(path),output=supplies.CreateCatalog(source,out int added);
            var before=JsonConvert.DeserializeObject<List<KitDefinition>>(source)!;
            var after=JsonConvert.DeserializeObject<List<KitDefinition>>(output)!;
            Check(added==(hasMolotov?60:40),"real 20-kit catalog gets expected optional requests");
            int packed=0,molotovPacked=0;
            for(int k=0;k<before.Count;k++)
            {
                var original=Build(before[k]);
                var enhanced=Build(after[k]);
                var baseline=original.Descendants().Where(i=>!string.IsNullOrEmpty(i.TemplateId)).GroupBy(i=>i.TemplateId).ToDictionary(g=>g.Key,g=>g.Count());
                var actual=enhanced.Descendants().Where(i=>!string.IsNullOrEmpty(i.TemplateId)).GroupBy(i=>i.TemplateId).ToDictionary(g=>g.Key,g=>g.Count());
                foreach(var (tpl,count) in baseline)Check(actual.TryGetValue(tpl,out int n)&&n==count,"authored item/ammo count preserved "+before[k].Id+"/"+tpl);
                var quick=enhanced.GrenadeThrowingSlots.Select(s=>s.ContainedItem).OfType<CompoundItem>().SelectMany(c=>c.Grids).SelectMany(g=>g.Items).ToList();
                int here=quick.Count(i=>i.TemplateId==ArenaKitSupplies.Frag);packed+=here;
                Check(here==2,"real kit packs two RGD-5s in quick slots "+before[k].Id);
                if(hasMolotov)
                {
                    int bottles=quick.Count(i=>i.TemplateId==ArenaMode.Shared.ArenaMolotovItem.TemplateId);
                    molotovPacked+=bottles;
                    Check(bottles<=1,"Molotov never duplicated in quick slots");
                    Check(!enhanced.GetSlot(EquipmentSlot.Backpack).ContainedItem.Descendants().Any(i=>i.TemplateId==ArenaMode.Shared.ArenaMolotovItem.TemplateId),"Molotov never added to backpack");
                }
                Check(!enhanced.Descendants().Any(i=>i.TemplateId==Grenades[1]),"real kit has no added flashbang "+before[k].Id);
                Check(!enhanced.GetSlot(EquipmentSlot.Backpack).ContainedItem.Descendants().Any(i=>Grenades.Contains(i.TemplateId)),"grenades never added to backpack");
                Preload(after[k]);
                Check(Singleton<ObjectsFactory>.Instance.Loaded.Contains(ArenaKitSupplies.Frag),"real production preload includes RGD-5");
                Check(!Singleton<ObjectsFactory>.Instance.Loaded.Contains(Grenades[1]),"removed flashbang needs no preload");
                if(hasMolotov)Check(Singleton<ObjectsFactory>.Instance.Loaded.Contains(ArenaMode.Shared.ArenaMolotovItem.TemplateId),"real production preload includes Molotov");
            }
            Console.WriteLine($"Supplies real catalog: {before.Count} kits, {added} requests, {packed} packed in native quick-use container layout: {path}");
            Check(source==File.ReadAllText(path),"authored catalog file untouched");
            Check(output==supplies.CreateCatalog(output,out added)&&added==0,"catalog idempotent");
            if(hasMolotov)Check(molotovPacked>0,"real kits can actually pack the Molotov supply");
            GunGameChecks(after[0],JsonConvert.DeserializeObject<List<KitDefinition>>(File.ReadAllText(Path.Combine(root,"ArenaMode.Server/defaults/ladder.json")))!);
        }
        PlacementFixtures();
        Console.WriteLine($"KIT_SUPPLIES_ASSERTIONS_PASSED: {_checks}");
    }
    static partial void RegisterMolotov(Dictionary<string,TemplateItem> templates,JObject database);
    private static InventoryEquipment Build(KitDefinition kit)
    {
        var equipment=new InventoryEquipment();
        Populate.Invoke(null,[equipment,Singleton<ItemFactory>.Instance,kit]);
        return equipment;
    }
    private static void Preload(KitDefinition kit)
    {
        KitBundlePreloader.Reset();ArenaMode.Session.ArenaSession.IsActive=true;
        Singleton<ObjectsFactory>.Instance.Loaded.Clear();
        var preload=KitBundlePreloader.Preload(kit);while(preload.MoveNext()) { }
    }
    private static void GunGameChecks(KitDefinition baseKit,List<KitDefinition> pool)
    {
        string original=JsonConvert.SerializeObject(baseKit);
        foreach(var rung in new[]{pool[0],pool[pool.Count/2],pool[^1]})
        {
            var merged=KitMerge.WithPrimaryWeapon(baseKit,rung);
            Check(ReferenceEquals(merged.Fill.RigContents,baseKit.Fill.RigContents),"Gun Game retains exact base supplies");
            Check(merged.Fill.RigContents.Count(x=>x.OptionalQuickUseOnly)==(ArenaMolotovRegistration.RegisteredTemplate!=null?3:2),"Gun Game does not multiply requests");
            Preload(merged);
            Check(Singleton<ObjectsFactory>.Instance.Loaded.Contains(ArenaKitSupplies.Frag),"merged kit RGD-5 preload");
            Check(!Singleton<ObjectsFactory>.Instance.Loaded.Contains(Grenades[1]),"merged kit does not restore flashbang");
            Singleton<ItemFactory>.Instance.Created.Clear();
            KitBuilder.BuildPrimaryWeapon(merged,merged.Fill.SpareMagazines,out _);
            Check(!Singleton<ItemFactory>.Instance.Created.Any(t=>Grenades.Contains(t)||t==ArenaMode.Shared.ArenaMolotovItem.TemplateId),"live Gun Game promotion never replenishes grenades");
        }
        Check(JsonConvert.SerializeObject(baseKit)==original,"Gun Game merge never mutates base kit");
    }
    private static Grid Grid(int width,int height,JArray filters=null)=>new(new JObject
    { ["_name"]="main",["_props"]=new JObject{["cellsH"]=width,["cellsV"]=height,["filters"]=filters??new JArray()} });
    private static InventoryEquipment Equipment(Grid rig,Grid pockets,Grid backpack)
    {
        var eq=new InventoryEquipment();
        if(rig!=null)eq.GetSlot(EquipmentSlot.TacticalVest).ContainedItem=new CompoundItem{Grids=[rig]};
        if(pockets!=null)eq.GetSlot(EquipmentSlot.Pockets).ContainedItem=new CompoundItem{Grids=[pockets]};
        if(backpack!=null)eq.GetSlot(EquipmentSlot.Backpack).ContainedItem=new CompoundItem{Grids=[backpack]};
        return eq;
    }
    private static KitDefinition Request(params KitStackedItem[] entries)=>new(){Id="fixture",Fill=new(){SpareMagazines=0,RigContents=entries.ToList()}};
    private static KitStackedItem Optional(string tpl)=>new(){Tpl=tpl,Count=1,OptionalQuickUseOnly=true};
    private static void RunFill(InventoryEquipment eq,KitDefinition kit,Dictionary<string,Item> created=null)
        =>Fill.Invoke(null,[eq,Singleton<ItemFactory>.Instance,kit,created??new Dictionary<string,Item>()]);
    private static void PlacementFixtures()
    {
        var factory=Singleton<ItemFactory>.Instance;
        const string med="544fb25a4bdc2dfb738b4567";
        var rig=Grid(2,1);var backpack=Grid(4,4);var eq=Equipment(rig,null,backpack);
        // Deliberately put the optional request FIRST to challenge the sorting
        // contract. Authored contents retain priority even for custom ordering.
        var kit=Request(Optional(Grenades[0]),new(){Tpl=med,Count=1},Optional(Grenades[1]));
        int warnings=ArenaMode.Plugin.ModLogger.Warnings.Count;
        RunFill(eq,kit);
        Check(rig.Items[0].TemplateId==med,"authored supplies placed before optional grenades");
        Check(rig.Items.Count==2&&rig.Items[1].TemplateId==Grenades[0],"only actual remaining grid space used");
        Check(backpack.Items.Count==0,"optional supplies never spill to backpack");
        Check(ArenaMode.Plugin.ModLogger.Warnings.Count==warnings,"expected optional no-space is not warning");
        Check(ArenaMode.Plugin.ModLogger.Infos.Any(s=>s.Contains("optional grenade")&&s.Contains("skipped")),"optional skip diagnostic");

        // UseItemsAnywhere 1.4.0 patches the getter to put Backpack before rig
        // and pockets (also includes secure/armband). Arena placement must remain
        // useful in vanilla regardless of that global quick-throw policy.
        rig=Grid(1,1);var patchedPockets=Grid(1,1);backpack=Grid(4,4);eq=Equipment(rig,patchedPockets,backpack);
        eq.GrenadeSlotsOverride=[eq.GetSlot(EquipmentSlot.Backpack),eq.GetSlot(EquipmentSlot.TacticalVest),eq.GetSlot(EquipmentSlot.Pockets)];
        RunFill(eq,Request(Optional(Grenades[0]),Optional(Grenades[0])));
        Check(backpack.Items.Count==0,"patched quick-throw list cannot redirect grenades to backpack");
        Check(rig.Items.Count==1&&patchedPockets.Items.Count==1
              &&rig.Items[0].TemplateId==Grenades[0]&&patchedPockets.Items[0].TemplateId==Grenades[0],"two RGD-5s use rig then pockets despite patched getter");
        RunFill(eq,Request(Optional(Grenades[0])));
        Check(backpack.Items.Count==0,"patched quick-throw list cannot bypass full rig and pockets");

        var filter=new JArray(new JObject{["Filter"]=new JArray(Grenades[1])});
        rig=Grid(1,1,filter);backpack=Grid(3,3);eq=Equipment(rig,null,backpack);
        RunFill(eq,Request(Optional(Grenades[0]),Optional(Grenades[1])));
        Check(rig.Items.Single().TemplateId==Grenades[1],"native placement rejection respected before next grenade");
        Check(backpack.Items.Count==0,"filter refusal never reroutes optional supplies to backpack");

        rig=Grid(1,1);var pockets=Grid(1,1);backpack=Grid(3,3);eq=Equipment(rig,pockets,backpack);
        var oldRig=factory.CreateItem(factory.NextId,med,null);var oldPocket=factory.CreateItem(factory.NextId,med,null);
        rig.AddAnywhere(oldRig,EErrorHandlingType.Ignore);pockets.AddAnywhere(oldPocket,EErrorHandlingType.Ignore);
        RunFill(eq,Request(Optional(Grenades[0]),Optional(Grenades[1])));
        Check(ReferenceEquals(rig.Items.Single(),oldRig)&&ReferenceEquals(pockets.Items.Single(),oldPocket),"full containers preserve actual existing items");
        Check(backpack.Items.Count==0,"full quick slots skip without repacking");

        // A 1x2 loaded M4 magazine occupies the only quick-use grid before an
        // optional grenade is even attempted; spare reserve has priority.
        const string magazineTpl="55d4887d4bdc2d962f8b4570",ammoTpl="54527ac44bdc2d36668b4567";
        var magazine=(Magazine)factory.CreateItem(factory.NextId,magazineTpl,null);
        var ammo=factory.CreateItem(factory.NextId,ammoTpl,null);ammo.StackObjectsCount=30;magazine.Cartridges.Add(ammo,false);
        var size=magazine.CalculateCellSize();rig=Grid(size.X,size.Y);backpack=Grid(4,4);eq=Equipment(rig,null,backpack);
        kit=Request(Optional(Grenades[0]),Optional(Grenades[1]));kit.Fill.SpareMagazines=1;
        RunFill(eq,kit,new(){{"loaded",magazine}});
        Check(rig.Items.Single() is Magazine,"spare ammunition reserves space before optional grenades");
        Check(backpack.Items.Count==0,"ammo-first does not put optional grenades in backpack");
        Check(magazine.Cartridges.Count==30,"existing loaded magazine remains unchanged");

        // The server marker cannot turn an arbitrary changed client item into
        // a throwable, even when a compatible empty grid is available.
        rig=Grid(2,2);eq=Equipment(rig,null,null);
        RunFill(eq,Request(Optional(med)));
        Check(rig.Items.Count==0,"optional non-throwable refused");
        // Two records are AND; an exclusion wins despite the Item inclusion.
        var reject=new JArray(new JObject{["Filter"]=new JArray("54009119af1c881c07000029")},new JObject{["Filter"]=new JArray(Grenades[1])});
        rig=Grid(2,2,reject);eq=Equipment(rig,null,null);
        RunFill(eq,Request(Optional(Grenades[0]),Optional(Grenades[1])));
        Check(rig.Items.Count==1&&rig.Items[0].TemplateId==Grenades[1],"multiple native grid filter records require all matches");
        if(ArenaMolotovRegistration.RegisteredTemplate!=null)
        {
            rig=Grid(1,1);pockets=Grid(1,1);backpack=Grid(4,4);eq=Equipment(rig,pockets,backpack);
            RunFill(eq,Request(Optional(Grenades[0]),Optional(Grenades[0]),Optional(ArenaMode.Shared.ArenaMolotovItem.TemplateId)));
            Check(rig.Items.Count==1&&pockets.Items.Count==1,"existing two frags keep priority over Molotov");
            Check(backpack.Items.Count==0,"no-room Molotov does not spill to backpack");
            rig=Grid(1,1);eq=Equipment(rig,null,backpack);
            RunFill(eq,Request(Optional(ArenaMode.Shared.ArenaMolotovItem.TemplateId)));
            Check(rig.Items.Single().TemplateId==ArenaMode.Shared.ArenaMolotovItem.TemplateId,"native ThrowWeap path packs Molotov");
        }
    }
}
