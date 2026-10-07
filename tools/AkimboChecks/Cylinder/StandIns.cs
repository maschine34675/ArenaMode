using System.Reflection;
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated=>Instance!=null; } }
namespace HarmonyLib {
    public static class AccessTools {public static MethodInfo DeclaredMethod(Type t,string n)=>t.GetMethod(n);public static MethodInfo Method(Type t,string n)=>t.GetMethod(n);}
    public class HarmonyPriority(int priority):Attribute {} public static class Priority { public const int Last=0; }
}
namespace SPT.Reflection.Patching {
    public class PatchPrefixAttribute:Attribute {} public class PatchFinalizerAttribute:Attribute {}
    public abstract class ModulePatch {protected abstract MethodBase GetTargetMethod();public void Enable(){if(GetTargetMethod()==null)throw new Exception("missing native seam");}}
}
namespace EFT.InventoryLogic {
    public class Ammo { public string Id;public object CurrentAddress;public int StackObjectsCount=1;public bool IsUsed;public static bool Throw;
        public Ammo Clone(string id){if(Throw)throw new Exception("clone");return new(){Id=id};} }
    public class Slot {public Ammo ContainedItem;}
    public class Weapon {public enum EFireMode {single,doubleaction} public bool CylinderHammerClosed;public EFireMode SelectedFireMode;
        public CylinderMagazine Magazine;public CylinderMagazine GetCurrentMagazine()=>Magazine;}
    public class CylinderMagazine {public int CurrentCamoraIndex;public Slot[] Camoras;public int Count=>Camoras.Count(s=>s.ContainedItem!=null&&!s.ContainedItem.IsUsed);
        public Ammo GetFirstAmmo(bool single)=>Camoras[single?CurrentCamoraIndex:(CurrentCamoraIndex+1)%Camoras.Length].ContainedItem; }
}
namespace EFT {
    using EFT.InventoryLogic;
    public class ItemFactory {private int _id;public string NextId=>(++_id).ToString();}
    public class Firearms {public void MoveAmmoFromChamberToShellPort(bool ammoIsUsed,int chamberIndex=0){} }
    public class Animator {public bool Throw;public int Mag;public float Double;
        public void SetCamoraIndex(int i){if(Throw)throw new Exception("animator");}public void SetFire(bool v){}public void SetAmmoOnMag(int n)=>Mag=n;public void SetDoubleAction(float n)=>Double=n; }
    public class Player {public void MouseLook(){} public class FirearmController {
        public Firearms Firearms=new();public bool IsTriggerPressed=true;
        public class FireOperationBase {public struct SingleShotData {public Ammo AmmoToFire;} }
        public class FireCylinderMagOperation:FireOperationBase {
            public CylinderMagazine _cylinderMagazine;public int _camoraIndex;public Weapon Weapon;public FirearmController Controller=new();
            public Player Player=new();public Animator FirearmsAnimator=new();public SingleShotData _singleShotData;
            public void PrepareShot(){}public void OnFireEvent(){}public void Reset(){}public void OnAddAmmoInChamber(){} public void ShowIncompatibleNotification(){}
        }
    } }
}
namespace ArenaMode.Combat.Akimbo {
    internal static class ArenaAkimbo { internal static bool Eligible=true,Reserved,Failed;
        internal static bool FreeShot(EFT.Player.FirearmController.FireOperationBase op)=>Eligible&&!Reserved;
        internal static bool ReserveFreeShot(EFT.Player.FirearmController.FireOperationBase op,EFT.InventoryLogic.Ammo shot){if(!FreeShot(op))return false;Reserved=true;return true;}
        internal static void AmmoFailed(Exception ex){Failed=true;Eligible=false;}
    }
}
