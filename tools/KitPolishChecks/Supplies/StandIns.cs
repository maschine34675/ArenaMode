// Explicit boundary doubles. Production builder, merge, preload and server
// catalog service are linked unchanged. Grid packing here is an independent
// bounded occupancy oracle fed by real DB dimensions/filters, not Unity code.
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using System.Collections;

public readonly record struct MongoID(string Value);
public readonly record struct IntVec2(int X, int Y);
public readonly record struct ResourceKey(string Value) { public static ResourceKey EmptyResourceKey => default; }
public static class Localization { public static string Localized(this string s) => s; }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace Diz.Jobs { public enum JobYieldPriority { General } }
namespace ArenaMode
{
    public static class Plugin { public static readonly TestLogger ModLogger = new(); }
    public class TestLogger
    {
        public List<string> Warnings = [], Infos = [];
        public void LogWarning(string s) => Warnings.Add(s);
        public void LogInfo(string s) => Infos.Add(s);
    }
}
namespace ArenaMode.Events { public static class ArenaEvents { public static void RaiseKitApplied(ArenaMode.Kits.KitDefinition kit) { } } }
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive = true; } }
namespace ArenaMode.Modes
{
    public class GunGameMode { }
    public class ArenaMatchService { public static ArenaMatchService Current; public object Mode; }
}
namespace ArenaMode.Kits
{
    public static class KitCatalog { public static List<KitDefinition> Kits; }
    public static class LadderCatalog { public static List<KitDefinition> Rungs; }
}
namespace Redeploy.Core.Inventory
{
    public static class EquipmentSurgeon
    {
        public static void ReplaceEquipment(Profile profile, LocalPlayer body, Action<InventoryEquipment,ItemFactory> populate, bool requireCorpse)
        {
            var eq = new InventoryEquipment();
            populate(eq, Comfort.Common.Singleton<ItemFactory>.Instance);
            profile.Inventory.Equipment = eq;
        }
    }
}
namespace EFT
{
    public class LocalPlayer { }
    public enum EPlayerItems { Equipment }
    public class Profile
    {
        public Inventory Inventory = new();
        public Dictionary<string,int> CheckedMagazines = [];
        public List<string> CheckedChambers = [];
    }
    public class Inventory
    {
        public InventoryEquipment Equipment;
        public IEnumerable<Item> GetPlayerItems(EPlayerItems part) => Equipment.Descendants();
    }
    public class ObjectsFactory
    {
        public enum PoolsCategory { Raid }
        public enum AssemblyType { Local }
        public static CancellationToken DefaultCancellationToken => default;
        public readonly HashSet<string> Loaded = [];
        public Task LoadBundlesAndCreatePools(PoolsCategory a, AssemblyType b, ResourceKey[] keys, Diz.Jobs.JobYieldPriority priority, object progress, CancellationToken token)
        { foreach(var key in keys) Loaded.Add(key.Value); return Task.CompletedTask; }
    }
    public class ItemFactory
    {
        public Dictionary<MongoID,ItemTemplate> ItemTemplates = [];
        public List<string> Created = [];
        private int _next;
        public string NextId => "created" + ++_next;
        public Item CreateItem(string id, string tpl, object ignored)
        {
            Created.Add(tpl);
            var t=ItemTemplates[new MongoID(tpl)];
            Item item = Boundary.IsA(tpl,"5448bc234bdc2d3c308b4569") ? new Magazine()
                : Boundary.IsA(tpl,"5422acb9af1c889c16000029") ? new Weapon()
                : t.Properties["ThrowType"] != null ? new ThrowWeap() : new CompoundItem();
            item.Id=id;item.TemplateId=tpl;item.Template=t;
            item.StackMaxSize=t.Properties["StackMaxSize"]?.Value<int>() ?? 1;
            if(item is CompoundItem c)
            {
                c.Slots=(t.Properties["Slots"] as JArray ?? []).Concat(t.Properties["Chambers"] as JArray ?? [])
                    .Select(s=>new Slot { ID=s["_name"].Value<string>() }).ToArray();
                c.Grids=(t.Properties["Grids"] as JArray ?? []).Select(g=>new Grid(g)).ToArray();
            }
            if(item is Magazine m)
            {
                m.MaxCount=t.Properties["Cartridges"]?[0]?["_max_count"]?.Value<int>() ?? 1;
                m.ReloadMagType=t.Properties["ReloadMagType"]?.Value<string>()=="InternalMagazine" ? Weapon.EReloadMode.InternalMagazine : Weapon.EReloadMode.ExternalMagazine;
            }
            return item;
        }
    }
}
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { FirstPrimaryWeapon,SecondPrimaryWeapon,Holster,ArmorVest,TacticalVest,Pockets,Backpack,Headwear,Earpiece,FaceCover,Eyewear,SecuredContainer,Scabbard,ArmBand,Dogtag }
    public enum EErrorHandlingType { Ignore }
    public class Result { public bool Failed; public object Error; }
    public class ItemTemplate { public JObject Properties; public ResourceKey[] AllResources; }
    public class Item
    {
        public string Id,TemplateId;
        public ItemTemplate Template;
        public string ShortName => TemplateId;
        public int StackObjectsCount=1,StackMaxSize=1;
        private readonly Dictionary<Type,object> _components=[];
        public T GetItemComponent<T>() where T:class,new()
        { if(!_components.TryGetValue(typeof(T),out var value))_components[typeof(T)]=value=new T(); return (T)value; }
        public IntVec2 CalculateCellSize()=>new(Template?.Properties["Width"]?.Value<int>() ?? 1,Template?.Properties["Height"]?.Value<int>() ?? 1);
        public IEnumerable<Item> Descendants()
        {
            yield return this;
            if(this is CompoundItem c)
                foreach(var child in c.Slots.Select(s=>s.ContainedItem).Where(i=>i!=null).Concat(c.Grids.SelectMany(g=>g.Items)))
                    foreach(var nested in child.Descendants()) yield return nested;
            if(this is Magazine m) foreach(var ammo in m.Cartridges.Items) yield return ammo;
        }
    }
    public class CompoundItem:Item { public Slot[] Slots=[]; public Slot[] AllSlots=>Slots; public Grid[] Grids=[]; }
    public class Weapon:CompoundItem
    {
        public enum EReloadMode { ExternalMagazine,InternalMagazine }
        public enum EFireMode { single,fullauto,burst }
        public Slot[] Chambers=>Slots.Where(s=>s.ID.StartsWith("patron_in_weapon")).ToArray();
    }
    public class ThrowWeap:Item { }
    public class Magazine:CompoundItem { public int MaxCount;public Weapon.EReloadMode ReloadMagType;public StackSlot Cartridges=new(); }
    public class StackSlot
    {
        public List<Item> Items=[]; public int Count=>Items.Sum(i=>i.StackObjectsCount);
        public Result Add(Item item,bool simulate){if(!simulate)Items.Add(item);return new();}
    }
    public class Slot
    {
        public string ID; public Item ContainedItem;
        public Result AddWithoutRestrictions(Item item)
        { if(ContainedItem!=null)return new(){Failed=true,Error="occupied"};ContainedItem=item;return new(); }
    }
    public class InventoryEquipment:CompoundItem
    {
        public InventoryEquipment() { Slots=Enum.GetNames<EquipmentSlot>().Select(n=>new Slot{ID=n}).ToArray(); }
        public Slot GetSlot(EquipmentSlot slot)=>Slots.Single(s=>s.ID==slot.ToString());
        public IReadOnlyList<Slot> GrenadeSlotsOverride;
        public IReadOnlyList<Slot> GrenadeThrowingSlots=>GrenadeSlotsOverride??[GetSlot(EquipmentSlot.TacticalVest),GetSlot(EquipmentSlot.Pockets)];
    }
    public class RepairableComponent { public float Durability,MaxDurability; }
    public class FireModeComponent { public void SetFireMode(Weapon.EFireMode mode){} }
    public class FoldableComponent { public bool Folded; }
    public class MedKitComponent { public float HpResource; }
    public class Grid
    {
        public readonly string ID;
        public readonly JObject Props;
        public readonly List<Item> Items=[];
        public readonly List<Item> Attempts=[];
        private readonly bool[,] _used;
        public Grid(JToken definition)
        {
            ID=definition["_name"].Value<string>();Props=(JObject)definition["_props"].DeepClone();
            _used=new bool[Props["cellsH"].Value<int>(),Props["cellsV"].Value<int>()];
        }
        public Result AddAnywhere(Item item,EErrorHandlingType handling)
        {
            Attempts.Add(item);
            if(!Allows(item))return new(){Failed=true,Error="filter"};
            var size=item.CalculateCellSize();
            if(Pack(size.X,size.Y)|| (size.X!=size.Y&&Pack(size.Y,size.X))) {Items.Add(item);return new();}
            return new(){Failed=true,Error="space"};
        }
        private bool Allows(Item item)=>(Props["filters"] as JArray ?? []).All(f=>
            (f["Filter"] as JArray ?? []).Any(t=>Boundary.Matches(item.TemplateId,t.Value<string>()))
            && !(f["ExcludedFilter"] as JArray ?? []).Any(t=>item.Descendants().Any(i=>Boundary.Matches(i.TemplateId,t.Value<string>()))));
        private bool Pack(int w,int h)
        {
            for(int y=0;y+h<=_used.GetLength(1);y++) for(int x=0;x+w<=_used.GetLength(0);x++)
            {
                bool free=true;
                for(int a=x;a<x+w;a++) for(int b=y;b<y+h;b++)free &= !_used[a,b];
                if(!free)continue;
                for(int a=x;a<x+w;a++) for(int b=y;b<y+h;b++)_used[a,b]=true;
                return true;
            }
            return false;
        }
    }
}
internal static class Boundary
{
    public static JObject Database;
    public static HashSet<string> NativeCategories=[];
    public static void Install(JObject db,string nativeTypes)
    {
        Database=db;
        string types=File.ReadAllText(nativeTypes);
        types=types[types.IndexOf("Dictionary<string, Type> TypeTable")..];
        types=types[..types.IndexOf("CustomizationTypeTable")];
        NativeCategories=System.Text.RegularExpressions.Regex.Matches(types,"\"([0-9a-f]{24})\"\\s*,\\s*typeof\\(").Select(m=>m.Groups[1].Value).ToHashSet();
        var factory=new ItemFactory();
        foreach(var p in db.Properties())factory.ItemTemplates[new MongoID(p.Name)]=new(){Properties=p.Value["_props"] as JObject ?? new(),AllResources=[new ResourceKey(p.Name)]};
        Comfort.Common.Singleton<ItemFactory>.Instance=factory;
        Comfort.Common.Singleton<ObjectsFactory>.Instance=new();
    }
    public static bool Matches(string tpl,string filter)=>tpl==filter || (NativeCategories.Contains(filter)&&IsA(tpl,filter));
    public static bool IsA(string tpl,string ancestor)
    {
        var seen=new HashSet<string>();
        while(!string.IsNullOrEmpty(tpl)&&seen.Add(tpl)&&seen.Count<64)
        {if(tpl==ancestor)return true;tpl=Database[tpl]?["_parent"]?.Value<string>();}
        return false;
    }
}
