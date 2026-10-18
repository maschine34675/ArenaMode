// Boundary-only EFT doubles. Entry builder and the complete commit/rollback /
// metadata sanitation methods are production source, not reimplementations.
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
public static class Localization { public static string Localized(this string s) => s; }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace HarmonyLib { public static class AccessTools { public delegate ref F FieldRef<T,F>(T value); } }
namespace ArenaMode
{
    public static class Plugin { public static readonly Logger ModLogger = new(); }
    public class Logger { public void LogWarning(string s) { } public void LogInfo(string s) { } }
}
namespace Redeploy.Core.Inventory
{
    public static class EquipmentSurgeon
    {
        public static int Calls;
        public static void ReplaceEquipment(Profile p, LocalPlayer b, Action<InventoryEquipment,ItemFactory> populate, bool requireCorpse)
        { Calls++; throw new Exception("The entry path must never call the body surgeon"); }
    }
    public static class ProfileInventorySanitizer
    {
        public static bool Fail; public static int Calls;
        public static void Sanitize(Profile p)
        {
            Calls++;
            p.Inventory.FavoriteItemsStorage.Clear(); p.Inventory.DiscardLimits.Clear();
            p.CheckedMagazines.Clear(); p.CheckedChambers.Clear();
            p.Inventory.FastAccess = new(p.Inventory.Equipment, new());
            if (Fail) throw new Exception("injected sanitizer failure after metadata mutation");
        }
    }
}
namespace EFT
{
    public readonly record struct MongoID(string Value)
    {
        public override string ToString() => Value;
        public static implicit operator MongoID(string text) => new(text);
        public static implicit operator string(MongoID id) => id.Value;
    }
    public enum EPlayerItems { Equipment, All }
    public enum EBoundItem { Four, Five }
    public class LocalPlayer { }
    public class Profile
    {
        public Inventory Inventory = new();
        public Dictionary<MongoID,int> CheckedMagazines = new();
        public List<MongoID> CheckedChambers = new();
        public Dictionary<MongoID,bool> Encyclopedia = new();
        public bool Examined(Item item) => Encyclopedia == null || Encyclopedia.ContainsKey(item.TemplateId) || item.ExaminedByDefault;
    }
    public class ItemFactory
    {
        public Dictionary<MongoID,Func<Item>> ItemTemplates = new();
        public List<string> Created = new();
        public string FailTemplate;
        public bool ReuseIds;
        private int _next;
        public MongoID NextId => ReuseIds ? "original-gun" : "fresh-" + ++_next;
        public Item CreateItem(MongoID id, MongoID tpl, object ignored)
        {
            Created.Add(tpl);
            if (tpl == FailTemplate) throw new Exception("injected template failure");
            Item item = ItemTemplates[tpl](); item.Id=id; item.TemplateId=tpl; return item;
        }
    }
}
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { FirstPrimaryWeapon,SecondPrimaryWeapon,Holster,ArmorVest,TacticalVest,Pockets,Backpack,Headwear,Earpiece,FaceCover,Eyewear,SecuredContainer,Scabbard,ArmBand,Dogtag }
    public enum EErrorHandlingType { Ignore }
    public readonly record struct Result(bool Failed=false, string Error=null);
    public class DirtyValue
    {
        public int Calls, FailAt;
        public void SetDirty() { Calls++; if (Calls==FailAt) throw new Exception("injected weight failure"); }
    }
    public class Inventory
    {
        public InventoryEquipment Equipment;
        public FastAccess FastAccess;
        public HashSet<MongoID> FavoriteItemsStorage = new();
        public Dictionary<MongoID,int> DiscardLimits = new();
        public DirtyValue TotalWeight = new(), TotalWeightEliteSkill = new();
        public IEnumerable<Item> GetPlayerItems(EPlayerItems part) => Equipment.GetAllItems();
    }
    public class FastAccess
    {
        public Dictionary<EBoundItem,Item> BoundItems = new();
        public FastAccess(InventoryEquipment equipment, Dictionary<EBoundItem,MongoID> bindings)
        { foreach (var binding in bindings) { if (!equipment.TryFindItem(binding.Value,out Item item)) throw new Exception("invalid binding"); BoundItems.Add(binding.Key,item); } }
    }
    public class Item
    {
        public MongoID Id,TemplateId;
        public object CurrentAddress;
        public bool ExaminedByDefault = true;
        public int StackObjectsCount=1,StackMaxSize=60;
        public string ShortName => TemplateId;
        private readonly Dictionary<Type,object> _components = new();
        public T GetItemComponent<T>() where T:class,new()
        { if (!_components.TryGetValue(typeof(T),out var value)) _components[typeof(T)]=value=new T(); return (T)value; }
        public (int X,int Y) CalculateCellSize() => (1,1);
        public IEnumerable<Item> GetAllItems()
        {
            yield return this;
            if (this is CompoundItem compound)
                foreach (var child in compound.Slots.Select(s=>s.ContainedItem).Where(i=>i!=null).Concat(compound.Grids.SelectMany(g=>g.Items)))
                    foreach (var item in child.GetAllItems()) yield return item;
            if (this is Magazine magazine)
                foreach (var item in magazine.Cartridges.Items) yield return item;
        }
    }
    public class CompoundItem:Item
    {
        public Slot[] Slots=Array.Empty<Slot>(); public Grid[] Grids=Array.Empty<Grid>();
        public IEnumerable<Slot> AllSlots => Slots;
        public IEnumerable<Slot> MissingVitalParts => AllSlots.Where(s=>s.Required && s.ContainedItem==null);
    }
    public class Weapon:CompoundItem
    {
        public enum EReloadMode { ExternalMagazine,InternalMagazine }
        public enum EFireMode { single,fullauto,burst }
        public Slot[] Chambers => Slots.Where(s=>s.ID.StartsWith("patron_in_weapon")).ToArray();
    }
    public class Magazine:CompoundItem
    { public int MaxCount=30; public Weapon.EReloadMode ReloadMagType; public StackSlot Cartridges=new(); }
    public class ThrowWeap:Item { }
    public class Slot
    {
        public string ID; public bool IsSpecial,Required; public Item ContainedItem;
        public bool FailAdd,FailRemove;
        public Result AddWithoutRestrictions(Item item)
        {
            if (FailAdd || ContainedItem!=null || item.StackObjectsCount!=1) return new(true,"injected/occupied/stack");
            ContainedItem=item; item.CurrentAddress=this; return new();
        }
        public Result RemoveItemWithoutRestrictions()
        {
            if (FailRemove || ContainedItem==null) return new(true,"injected/empty");
            ContainedItem.CurrentAddress=null; ContainedItem=null; return new();
        }
    }
    public class StackSlot
    {
        public List<Item> Items = new(); public int Count => Items.Sum(i=>i.StackObjectsCount);
        public Result Add(Item item,bool simulate) { if (!simulate) { Items.Add(item); item.CurrentAddress=this; } return new(); }
    }
    public class Grid
    {
        public string ID="main"; public int Capacity=100; public List<Item> Items=new();
        public Result AddAnywhere(Item item,EErrorHandlingType handling)
        { if (Items.Count>=Capacity) return new(true,"space"); Items.Add(item); item.CurrentAddress=this; return new(); }
    }
    public class InventoryEquipment:CompoundItem
    {
        public InventoryEquipment() { Slots=Enum.GetNames<EquipmentSlot>().Select(name=>new Slot{ID=name}).ToArray(); }
        public Slot GetSlot(EquipmentSlot slot) => Slots.FirstOrDefault(s=>s.ID==slot.ToString());
        public bool TryFindItem(MongoID id,out Item item) { item=GetAllItems().FirstOrDefault(i=>i.Id==id); return item!=null; }
    }
    public class RepairableComponent { public float Durability=100,MaxDurability=100; }
    public class FireModeComponent { public void SetFireMode(Weapon.EFireMode mode) {} }
    public class FoldableComponent { public bool Folded; }
    public class MedKitComponent { public float HpResource=400; }
}
