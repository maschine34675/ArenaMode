// Explicit boundary models, not an EFT/Unity execution. The actual adapters and
// timer policy are source-linked; installed IL contracts pin the native seams.
using System.Reflection;
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated=>Instance!=null; } }
namespace BepInEx.Configuration { public class ConfigEntry<T>(T value) { public T Value=value; } }
namespace ArenaMode.Config { public static class Settings { public static BepInEx.Configuration.ConfigEntry<bool> UseItemsAnywhere=new(true); } }
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive=true, Exact=true; public static int ArmEpoch=1; public static bool IsExactActiveRaid(object game,object world)=>Exact; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown,IsRespawnInProgress; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode { public static class Plugin { public static Logger ModLogger=new(); } public class Logger { public List<string> Warnings=[]; public void LogWarning(string value)=>Warnings.Add(value); } }
namespace ArenaMode.Pickups
{
    internal static class ArenaPowerups
    {
        internal static EFT.Player Owner; internal static bool Live=true;internal static float Now;
        internal static ArenaPowerupState State=new();
        internal static ArenaPowerupSnapshot Read(EFT.Player p)=>Session.ArenaSession.IsActive&&Live&&ReferenceEquals(p,Owner)&&p.HealthController.IsAlive?State.Read(Now):default;
    }
}
namespace EFT.InventoryLogic
{
    public class Item { public string Id; public object CurrentAddress; public int StackObjectsCount=1; public virtual T GetTemplate<T>()=>default; }
    public class AmmoTemplate { public bool HasGrenaderComponent; }
    public class ThrowWeap:Item { }
    public class Ammo:Item
    {
        public bool IsUsed;public AmmoTemplate Template=new();public static Action OnClone;public static bool ThrowOnClone,ReturnSelf;
        public override T GetTemplate<T>()=>(T)(object)Template;
        public Ammo Clone(string id) { OnClone?.Invoke();if(ThrowOnClone)throw new Exception("clone failed");return ReturnSelf?this:new(){Id=id,IsUsed=IsUsed,Template=Template}; }
    }
    public class Slot { public Item ContainedItem; }
    public class Cartridges { public List<Item> Items=[]; public Item Last=>Items.LastOrDefault(); }
    public class Magazine:Item { public bool Compatible=true;public Cartridges Cartridges=new();public int Count=>Cartridges.Items.Sum(a=>a.StackObjectsCount);public bool IsAmmoCompatible(Slot[] chambers)=>Compatible; }
    public class CylinderMagazine:Magazine { }
    public class Weapon:Item
    {
        public enum EMalfunctionState { None,Misfire,Feed,Jam }
        public enum EReloadMode { ExternalMagazine,InternalMagazine,OnlyBarrel }
        public bool IsStationaryWeapon,IsGrenadeLauncher,IsFlareGun,IsOneOff;public EReloadMode ReloadMode;
        public Slot[] Chambers=[new()];public bool HasChambers=>Chambers.Length>0;
        public int ChamberAmmoCount=>Chambers.Count(s=>s.ContainedItem is Ammo a&&!a.IsUsed);
        public Magazine Magazine=new();public Magazine GetCurrentMagazine()=>Magazine;
    }
    public class RocketLauncher:Weapon { } public class Revolver:Weapon { }
    public enum EquipmentSlot { Pockets,TacticalVest,Backpack,SecuredContainer }
    public class ContainerCollection:Item { public List<Item> Items=[]; }
    public class Equipment { public Dictionary<EquipmentSlot,Slot> Slots=Enum.GetValues<EquipmentSlot>().ToDictionary(s=>s,s=>new Slot());public Slot GetSlot(EquipmentSlot slot)=>Slots[slot]; }
    public class Inventory { public Equipment Equipment=new(); }
    public class InventoryController
    {
        public Inventory Inventory=new();
        public void GetReachableItemsOfTypeNonAlloc<T>(IList<T> preAllocatedList,Predicate<T> predicate=null) where T:Item
            => GetAcceptableItemsNonAlloc(new[]{EquipmentSlot.Pockets,EquipmentSlot.TacticalVest},preAllocatedList,predicate);
        public IEnumerable<T> GetReachableItemsOfType<T>(Predicate<T> predicate=null) where T:Item
        {var list=new List<T>();GetReachableItemsOfTypeNonAlloc(list,predicate);return list;}
        public virtual void GetAcceptableItemsNonAlloc<T>(EquipmentSlot[] equipmentSlots,IList<T> preAllocatedList,Predicate<T> predicate=null,Predicate<ContainerCollection> goDeeperPredicate=null) where T:Item
        {foreach(var slot in equipmentSlots)if(Inventory.Equipment.GetSlot(slot).ContainedItem is ContainerCollection c&&(goDeeperPredicate==null||goDeeperPredicate(c)))foreach(var item in c.Items)if(item is T found&&(predicate==null||predicate(found)))preAllocatedList.Add(found);}
    }
}
namespace EFT
{
    using EFT.InventoryLogic;
    public class FirearmHandsInputTranslator { public bool ReloadExternalMagazine(Weapon weapon,bool quick)=>false; }
    public class Health { public bool IsAlive=true; }
    public class Player
    {
        public bool IsAI,IsYourPlayer=true;public Health HealthController=new();public object HandsController;public InventoryController InventoryController=new();
        public class FirearmController
        {
            public Weapon Item;public FireOperationBase CurrentOperation;
            public class FireOperationBase
            {
                public Player Player;public FirearmController Controller;public Weapon Weapon;
                public struct SingleShotData { public Ammo AmmoToFire,AmmoWillBeLoadedToChamber,FedAmmo; public int AmmoCountInChamberBeforeShot,AmmoCountInChamberAfterShot,AmmoCountInMagBeforeShot,AmmoCountInMagAfterShot; public bool IsAmmoCompatible; }
                public SingleShotData ProcessInventoryOperationsForSingleShot(Weapon.EMalfunctionState malfState)=>default;
            }
        }
    }
    public class ItemFactory { private int _id;public string NextId=>(++_id).ToString(); }
    public class GameWorld { public Player MainPlayer; }
    public class AbstractGame { }
    public enum GameStatus { Started,Stopped }
    public class LocalGame:AbstractGame { public Player LocalPlayer;public GameWorld GameWorld;public GameStatus Status=GameStatus.Started; }
}
namespace EFT.UI
{
    public class AmmoSelector { public class CG_FindAmmoForWeapon { public IEnumerable<EFT.InventoryLogic.Item> method_0()=>null; } }
    public class ItemUiContext { public EFT.InventoryLogic.Magazine FindSuitableMagazine(EFT.InventoryLogic.Slot slot,IEnumerable<EFT.InventoryLogic.ContainerCollection> collections)=>null; }
}
#if !BACKPACK_REAL_HARMONY
namespace HarmonyLib
{
    public class HarmonyPriority(int priority):Attribute { public int priority=priority; }
    public static class Priority { public const int Last=0; }
    public static class AccessTools { public static MethodInfo Method(Type t,string name)=>t.GetMethod(name); }
}
#endif
namespace SPT.Reflection.Patching
{
    public class PatchPrefixAttribute:Attribute { } public class PatchPostfixAttribute:Attribute { } public class PatchTranspilerAttribute:Attribute { }
    public abstract class ModulePatch { public static List<(MethodBase Target,Type Patch)> Enabled=[];protected abstract MethodBase GetTargetMethod();public void Enable(){var m=GetTargetMethod()??throw new Exception("missing target");Enabled.Add((m,GetType()));} }
}
