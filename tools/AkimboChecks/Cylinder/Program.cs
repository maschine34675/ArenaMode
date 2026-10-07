using ArenaMode.Combat.Akimbo;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System.Reflection;
using Op=EFT.Player.FirearmController.FireCylinderMagOperation;
int checks=0;
void Need(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
object Call(string patch,string method,params object[] args)=>typeof(AkimboAmmo).GetNestedType(patch,BindingFlags.NonPublic).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
Op Fresh(bool hammer=false){ArenaAkimbo.Eligible=true;ArenaAkimbo.Reserved=ArenaAkimbo.Failed=false;Ammo.Throw=false;Singleton<ItemFactory>.Instance=new();
    var cylinder=new CylinderMagazine {Camoras=Enumerable.Range(0,6).Select(i=>new Slot{ContainedItem=new Ammo{Id="native"+i,CurrentAddress=new()}}).ToArray()};
    var weapon=new Weapon{Magazine=cylinder,CylinderHammerClosed=hammer,SelectedFireMode=Weapon.EFireMode.doubleaction};
    return new Op{_cylinderMagazine=cylinder,_camoraIndex=hammer?1:0,Weapon=weapon};}
AkimboAmmo.Enable();
foreach(bool hammer in new[]{false,true}) {
    var op=Fresh(hammer);var source=op._cylinderMagazine.GetFirstAmmo(!hammer);
    Need(!(bool)Call("PreparePatch","Prefix",op,true),"free cylinder preparation replaces consumption");
    Need(ArenaAkimbo.Reserved&&op._singleShotData.AmmoToFire.IsUsed&&!ReferenceEquals(source,op._singleShotData.AmmoToFire),"free clone reserved and detached");
    Need(!source.IsUsed&&source.CurrentAddress!=null&&op._cylinderMagazine.Count==6&&op._cylinderMagazine.CurrentCamoraIndex==0,"free cylinder retains native source and index");
    object[] fire={op,null};Call("FirePatch","Prefix",fire);
    Need(!(bool)Call("ShellPatch","Prefix",op.Controller.Firearms,op._camoraIndex),"free shell visual suppressed");
    Need((bool)Call("ShellPatch","Prefix",new Firearms(),op._camoraIndex),"foreign firearm visual untouched");
    Call("FirePatch","Finalizer",op,fire[1],null);
    Need((bool)Call("ShellPatch","Prefix",op.Controller.Firearms,op._camoraIndex),"shell scope ends with native fire");
    Need(!(bool)Call("ChamberPatch","Prefix",op)&&op.Weapon.CylinderHammerClosed==hammer,"free chamber completion retains hammer selection");
    Need(ReferenceEquals(source,op._cylinderMagazine.GetFirstAmmo(!op.Weapon.CylinderHammerClosed)),"paid half sees same source chamber");
    Call("ResetPatch","Prefix",op);Need((bool)Call("ChamberPatch","Prefix",op),"operation reset removes receipt");
}
var denied=Fresh();ArenaAkimbo.Eligible=false;Need((bool)Call("PreparePatch","Prefix",denied,true),"ordinary or paid preparation stays native");
var veto=Fresh();Need((bool)Call("PreparePatch","Prefix",veto,false)&&!ArenaAkimbo.Reserved,"earlier veto cannot reserve free shot");
var broken=Fresh();broken.FirearmsAnimator.Throw=true;
Need((bool)Call("PreparePatch","Prefix",broken,true)&&ArenaAkimbo.Failed&&ArenaAkimbo.Reserved,"post reservation failure falls back without refunding credit");
Need(broken._cylinderMagazine.Count==6,"failed prepare never edits native inventory");
var cloneFail=Fresh();Ammo.Throw=true;Need((bool)Call("PreparePatch","Prefix",cloneFail,true)&&ArenaAkimbo.Failed&&!ArenaAkimbo.Reserved,"clone exception fails open before reservation");
Console.WriteLine($"AKIMBO_CYLINDER_CHECKS_PASSED ({checks} assertions; native boundaries modeled, no Unity execution)");
