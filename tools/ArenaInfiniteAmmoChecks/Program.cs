using ArenaMode.Pickups;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using System.Reflection;
using Op=EFT.Player.FirearmController.FireOperationBase;
static class Program
{
    static int n;static MethodInfo prefix;static Player player;static Op op;static Ammo source;
    static void Check(bool ok,string why){n++;if(!ok)throw new Exception("ASSERT: "+why);}
    static void Fresh(bool chamberless=false)
    {
        ArenaMode.Combat.Akimbo.ArenaAkimbo.Allowed=ArenaMode.Combat.Akimbo.ArenaAkimbo.Reserved=false;
        ArenaSession.IsActive=true;ArenaSession.ArmEpoch++;ArenaPowerups.Live=true;ArenaPowerups.Now=100;
        ArenaPowerups.State=new();ArenaPowerups.State.GrantAmmo(100,10);player=new();ArenaPowerups.Owner=player;
        Singleton<ItemFactory>.Instance=new();Ammo.OnClone=null;Ammo.ThrowOnClone=Ammo.ReturnSelf=false;
        var weapon=new Weapon();if(chamberless)weapon.Chambers=[];
        source=new(){Id="original",CurrentAddress=new(),StackObjectsCount=chamberless?40:1};
        weapon.Magazine.Cartridges.Items.Add(chamberless?source:new Ammo{Id="next",CurrentAddress=new(),StackObjectsCount=30});
        if(!chamberless)weapon.Chambers[0].ContainedItem=source;
        var fc=new Player.FirearmController{Item=weapon};op=new(){Player=player,Controller=fc,Weapon=weapon};fc.CurrentOperation=op;player.HandsController=fc;
    }
    static (bool Native,Op.SingleShotData Shot) Fire(Weapon.EMalfunctionState malf=Weapon.EMalfunctionState.None,bool native=true)
    {object[] args={op,malf,native,new Op.SingleShotData{AmmoCountInMagAfterShot=-7}};bool run=(bool)prefix.Invoke(null,args);return(run,(Op.SingleShotData)args[3]);}
    static int Main()
    {
        try
        {
            ArenaInfiniteAmmo.Enable();Check(ArenaInfiniteAmmo.Enabled&&ModulePatch.Enabled.Count==1,"one ammo patch installed");ArenaInfiniteAmmo.Enable();Check(ModulePatch.Enabled.Count==1,"enable idempotent");
            prefix=ModulePatch.Enabled.Single().Patch.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
            Check(prefix.GetCustomAttribute<HarmonyLib.HarmonyPriority>().priority==HarmonyLib.Priority.Last,"ammo hook respects earlier prefixes");
            foreach(bool chamberless in new[]{false,true})
            {
                Fresh(chamberless);var ids=new HashSet<string>();var original=source;int count=op.Weapon.Magazine.Count;
                for(int i=0;i<200;i++)
                {
                    var r=Fire();Check(!r.Native,"loaded gun bypasses only consumption");Check(ids.Add(r.Shot.AmmoToFire.Id),"each shot has fresh id");
                    Check(!ReferenceEquals(r.Shot.AmmoToFire,source)&&r.Shot.AmmoToFire.CurrentAddress==null&&r.Shot.AmmoToFire.StackObjectsCount==1&&r.Shot.AmmoToFire.IsUsed,"shot is detached one-round spent clone");
                    Check(ReferenceEquals(r.Shot.AmmoToFire.Template,source.Template),"same loaded ammo template");
                    Check(!source.IsUsed&&source.CurrentAddress!=null&&op.Weapon.Magazine.Count==count,"original cartridge and magazine remain unchanged");
                    Check(r.Shot.AmmoCountInMagAfterShot==count&&r.Shot.AmmoCountInMagBeforeShot==count,"native counters retain magazine rounds");
                    Check(r.Shot.AmmoCountInChamberAfterShot==(chamberless?0:1)&&ReferenceEquals(r.Shot.AmmoWillBeLoadedToChamber,chamberless?null:source)&&r.Shot.FedAmmo==null,"native chamber and model data coherent");
                }
                ArenaPowerups.Now=110;Check(Fire().Native,"exact expiry restores native consumption");
            }
            Fresh();op.Weapon.Magazine.Cartridges.Items.Clear();Check(!Fire().Native,"last chamber round works with empty inserted magazine");
            Fresh(true);op.Weapon.Magazine.Cartridges.Items.Clear();Check(Fire().Native,"empty gun receives no invented ammo");
            Fresh();op.Weapon.ReloadMode=Weapon.EReloadMode.InternalMagazine;Check(!Fire().Native,"tube and internal magazines supported");
            Fresh();Check(Fire(native:false).Shot.AmmoCountInMagAfterShot==-7,"earlier veto leaves result untouched");
            foreach(var malf in new[]{Weapon.EMalfunctionState.Misfire,Weapon.EMalfunctionState.Feed,Weapon.EMalfunctionState.Jam}){Fresh();Check(Fire(malf).Native,"malfunctions remain native");}
            foreach(Action deny in new Action[]{()=>ArenaSession.IsActive=false,()=>ArenaPowerups.Live=false,()=>player.IsAI=true,()=>player.IsYourPlayer=false,()=>player.HealthController.IsAlive=false,()=>ArenaPowerups.Owner=new(),()=>player.HandsController=new object(),()=>op.Controller.CurrentOperation=new(),()=>op.Controller.Item=new(),()=>op.Weapon.IsStationaryWeapon=true,()=>op.Weapon.IsGrenadeLauncher=true,()=>op.Weapon.ReloadMode=Weapon.EReloadMode.OnlyBarrel,()=>op.Weapon.Chambers=[new(),new()],()=>op.Weapon.Magazine=new CylinderMagazine(),()=>op.Weapon.Magazine.Compatible=false,()=>source.IsUsed=true,()=>source.Template.HasGrenaderComponent=true,()=>op.Weapon.Magazine=null,()=>Singleton<ItemFactory>.Instance=null})
            {Fresh();deny();Check(Fire().Native,"unsupported or foreign scope uses native consumption");}
            Fresh();Ammo.OnClone=()=>ArenaPowerups.Now=110;Check(Fire().Native&&!source.IsUsed,"expired cloning callback cannot publish bonus");
            Fresh();Ammo.ThrowOnClone=true;Check(Fire().Native&&!source.IsUsed,"clone failure is mutation-free vanilla fallback");
            Fresh();Ammo.ReturnSelf=true;Check(Fire().Native&&!source.IsUsed,"aliased clone cannot spend real cartridge");
            Fresh();Ammo.OnClone=()=>{var next=new Weapon{Chambers=op.Weapon.Chambers,Magazine=op.Weapon.Magazine};op.Weapon=op.Controller.Item=next;};
            Check(Fire().Native,"weapon replacement during cloning cannot publish stale shot");
            foreach(Action special in new Action[]{()=>op.Weapon.IsFlareGun=true,()=>op.Weapon.IsOneOff=true,
                ()=>op.Weapon=op.Controller.Item=new Revolver{Magazine=op.Weapon.Magazine,Chambers=op.Weapon.Chambers},
                ()=>op.Weapon=op.Controller.Item=new RocketLauncher{Magazine=op.Weapon.Magazine,Chambers=op.Weapon.Chambers}})
            {Fresh();special();Check(Fire().Native,"special weapons remain explicitly native");}
            foreach(bool inserted in new[]{true,false})
            {
                Fresh(); ArenaPowerups.Now=110;
                if(!inserted)op.Weapon.Magazine=null;
                ArenaMode.Combat.Akimbo.ArenaAkimbo.Allowed=true;
                var free=Fire(); Check(!free.Native && ReferenceEquals(op.Weapon.Chambers[0].ContainedItem,source)
                    && !source.IsUsed,"akimbo reserves free loaded chamber without consuming it");
                Check(ArenaMode.Combat.Akimbo.ArenaAkimbo.Reserved && Fire().Native,"akimbo second half resumes native consumption");
            }
            Console.WriteLine($"ARENA_INFINITE_AMMO_BEHAVIOR_PASSED: {n} assertions; source-linked adapter, explicit native doubles");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException());return 1;}
    }
}
