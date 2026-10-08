using ArenaMode;
using ArenaMode.Kits;
using ArenaMode.Pickups;
using ArenaMode.Respawn;
using UnityEngine;
using UnityEngine.AI;

internal static partial class Program
{
    private static ArenaPickupView ActiveView(string id="one") => ArenaPickupView.Created.Single(v=>!v.Disposed&&v.Point.Id==id);
    private static void At(float seconds) { Time.time=seconds;Time.unscaledTime=seconds;Step(0); }
    private static void RotationReady(ArenaPickupKind kind=ArenaPickupKind.DoubleDamage)
    {
        Fixture(kind);
        var point=Point(kind);point.Alternatives=[new(){X=10,Yaw=90}];
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(FileFor(point)));
        NavMesh.AllowPath=(a,b)=>b.x==0;
        Step();Step();NavMesh.AllowPath=null;
        Check(ActiveView().Floor.x==0,"rotation fixture starts at known safe authored position");
    }

    private static void RotationCases()
    {
        var policy=new ArenaPickupState();int appearance=policy.Appearance;
        Check(policy.Relocate()&&policy.Appearance!=appearance,"relocation invalidates the previous appearance");
        int token=policy.Reserve(0);
        Check(!policy.Relocate()&&policy.Complete(token,true,1,10),"pending relocation cannot disturb native reservation");
        Check(!policy.Relocate()&&policy.ReadyAt==11,"cooling relocation cannot change cooldown");
        policy.Retire();Check(!policy.Relocate(),"retired appearance cannot relocate");

        RotationReady();var state=State();var original=ActiveView();appearance=state.Appearance;
        int generation=ArenaPickupRuntime.Generation,resets=ArenaPowerups.Resets;
        At(60);Check(ReferenceEquals(original,ActiveView()),"available position remains for sixty gameplay seconds");
        At(60.1f);
        Check(ActiveView().Floor.x==10&&original.Disposed,"unclaimed pickup rotates after sixty gameplay seconds");
        Check(ReferenceEquals(state,State())&&state.Appearance!=appearance&&state.Phase==ArenaPickupPhase.Available
            &&ArenaPickupRuntime.Generation==generation&&ArenaPowerups.Resets==resets,"rotation preserves entry state round and active effects");
        Check(ActiveView().Point.Id==original.Point.Id&&ActiveView().Point.Kind==original.Point.Kind
            &&ActiveView().Point.RespawnSeconds==original.Point.RespawnSeconds&&ActiveView().Point.Yaw==90,"rotation preserves identity type cooldown and authored yaw");
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(FileFor(new(){Id="replacement",X=50,Kind=ArenaPickupKind.Shield})));
        At(120.1f);Check(ActiveView().Floor.x==0,"in-round rotation uses frozen authored pool");

        RotationReady();state=State();Move(_local,0);float ready=state.ReadyAt;
        Move(_local,-3);At(ready-.01f);
        Check(ActiveView().Floor.x==0&&state.Phase==ArenaPickupPhase.Cooldown,"collected pickup does not move during cooldown");
        At(ready);Check(ActiveView().Floor.x==10&&state.Phase==ArenaPickupPhase.Available,"collected pickup chooses different safe spot after cooldown");
        Check(ArenaPowerups.Grants.Count==1,"relocation grants no extra effect");

        RotationReady();Move(_local,10);At(60.1f);
        Check(ArenaPowerups.Grants.Count==0&&State().Phase==ArenaPickupPhase.Available,"actor standing at new spot must leave before collection");
        Move(_local,12);Check(ArenaPowerups.Grants.Count==0,"leaving new spot cannot collect outward sweep");
        Move(_local,10);Check(ArenaPowerups.Grants.Count==1,"reentering relocated pickup collects once");
        RotationReady();Move(_local,8);At(59.9f);Move(_local,12,seconds:.2f);
        Check(ActiveView().Floor.x==10&&ArenaPowerups.Grants.Count==0,"relocation cannot collect a preappearance movement segment");

        foreach(string blocked in new[]{"clearance","path","floor","anchor","respawn","swap","pause","marker"})
        {
            RotationReady();original=ActiveView();state=State();appearance=state.Appearance;
            switch(blocked)
            {
                case "clearance":Physics.Capsule=(a,b,r)=>a.x==10?[new Collider()]:[];break;
                case "path":NavMesh.PathOk=false;break;
                case "floor":NavMesh.Sample=p=>p.x==10?p+Vector3.up:p;break;
                case "anchor":_local.MovementContext.IsGrounded=false;break;
                case "respawn":ArenaRespawnDriver.IsRespawnInProgress=true;break;
                case "swap":WeaponSwapService.IsSwapping=true;break;
                case "pause":Time.timeScale=0;break;
                case "marker":ArenaPickupView.ThrowOnCreate=true;break;
            }
            At(60.1f);
            Check(ReferenceEquals(ActiveView(),original)&&state.Appearance==appearance&&state.Phase==ArenaPickupPhase.Available,
                "unsafe or unavailable rotation retains old available position: "+blocked);
            int probes=NavMesh.Samples;At(61);
            Check(NavMesh.Samples==probes,"failed rotation probes are bounded between deadlines");
        }
        RotationReady();Physics.Capsule=(a,b,r)=>a.x==10?[new Collider()]:[];At(60.1f);
        Check(ActiveView().Floor.x==0,"runtime rotation does not invent nearby fallback positions");
        Physics.Capsule=null;At(120.1f);Check(ActiveView().Floor.x==10,"failed rotation can recover at later availability deadline");

        Fixture();var layout=FileFor();layout.Layouts[0].Points[0].Alternatives=[new(){X=10}];
        var fixedPoint=Point(ArenaPickupKind.Shield);fixedPoint.Id="fixed";fixedPoint.X=10;layout.Layouts[0].Points.Add(fixedPoint);
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(layout));Step();Step();At(61);
        Check(ActiveView().Floor.x==0&&ActiveView("fixed").Floor.x==10,"runtime rotation cannot overlap another pickup");
        Ready();original=ActiveView();At(121);Move(_local,0);At(State().ReadyAt);
        Check(ReferenceEquals(original,ActiveView())&&ActiveView().Floor.x==0,"single-position custom pickup remains fixed across age and cooldown");

        RotationReady(ArenaPickupKind.Weapon);var weapon=ArenaWeaponPickup.Created.Single();original=ActiveView();
        At(60.1f);
        Check(weapon.Disposed&&ArenaWeaponPickup.Created.Count==2&&ArenaWeaponPickup.Created.Last().Position.x==10
            &&State().Phase==ArenaPickupPhase.Available,"available weapon rotation replaces only its owned source model");
        Check(ArenaWeaponPickup.Created.Last().Kit.Id==weapon.Kit.Id,"weapon rotation preserves configured kit");

        RotationReady(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();weapon.IsAvailable=false;At(61);
        Check(ActiveView().Floor.x==0&&!weapon.Disposed&&ArenaWeaponPickup.Created.Count==1,
            "unavailable native weapon source cannot be replaced by rotation");

        RotationReady(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();At(59);Move(_local,0);At(61);
        Check(State().Phase==ArenaPickupPhase.Pending&&ActiveView().Floor.x==0&&!weapon.Disposed&&ArenaWeaponPickup.Created.Count==1,
            "pending weapon is never rotated or rebuilt at age deadline");
        weapon.Settle(true);Move(_local,-3);At(State().ReadyAt);
        Check(ActiveView().Floor.x==10&&ArenaWeaponPickup.Created.Count==2,"confirmed pending transfer rotates only after full cooldown");
        weapon.Callback(false);Check(State().Phase==ArenaPickupPhase.Available,"late callback cannot settle relocated appearance");

        foreach(bool ambiguous in new[]{false,true})
        {
            RotationReady(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();At(59);Move(_local,0);
            if(ambiguous)weapon.Settle(false,true);else At(70);
            At(121);Check(State().Phase==ArenaPickupPhase.Retired&&ActiveView().Floor.x==0&&ArenaWeaponPickup.Created.Count==1,
                "uncertain or timed-out weapon never rearms through rotation");
        }
        Fixture(ArenaPickupKind.Weapon);var waiting=Point(ArenaPickupKind.Weapon);waiting.Alternatives=[new(){X=10}];
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(FileFor(waiting)));
        KitBundlePreloader.IsPreloading=true;NavMesh.AllowPath=(a,b)=>b.x==0;Step();NavMesh.AllowPath=null;At(61);
        Check(ActiveView().Floor.x==0&&ArenaWeaponPickup.Created.Count==0,"preparing weapon retains captured position");
        KitBundlePreloader.IsPreloading=false;Step();At(120);
        Check(ActiveView().Floor.x==0,"weapon availability timer starts after preparation completes");
        At(122);Check(ActiveView().Floor.x==10&&ArenaWeaponPickup.Created.Count==2,"prepared weapon rotates after full available interval");

        RotationReady(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();
        weapon.Disposing=()=>{ArenaPickupRuntime.Reset();ArenaPickupRuntime.Tick();};At(60.1f);
        Check(ArenaPickupRuntime.IsLiveActor(_local)&&ArenaPickupView.Created.Count(v=>!v.Disposed)==1
            &&ArenaWeaponPickup.Created.Count==2&&_game.Coroutines.Count==0,
            "native disposal callback preserves replacement round and creates no orphan weapon");
        RotationReady(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();
        generation=ArenaPickupRuntime.Generation;resets=ArenaPowerups.Resets;
        weapon.Disposing=()=>throw new InvalidOperationException("native cleanup failure");At(61);
        Check(State().Phase==ArenaPickupPhase.Retired&&ArenaPickupRuntime.Generation==generation
            &&ArenaPowerups.Resets==resets&&ArenaWeaponPickup.Created.Count==1,
            "failed native source cleanup retires only its pickup without replacement or effect reset");

        Fixture();System.IO.File.Delete(LayoutPath);Step();Step();
        var before=ArenaPickupView.Created.ToDictionary(v=>v.Point.Id,v=>v.Floor);
        for(int round=1;round<=3;round++)
        {
            At(61*round);
            var views=ArenaPickupView.Created.Where(v=>!v.Disposed).ToArray();
            Check(views.Length==6&&views.Select(v=>v.Point.Kind).Distinct().Count()==6,"live Factory rotation preserves six pickup identities and types");
            Check(views.All(v=>(v.Floor-before[v.Point.Id]).sqrMagnitude>=9),"each Factory pickup changes position when spare safe candidates exist");
            Check(views.All(v=>views.All(other=>ReferenceEquals(v,other)||(v.Floor-other.Floor).sqrMagnitude>=9)),"rotated Factory pickups keep three metre separation");
            before=views.ToDictionary(v=>v.Point.Id,v=>v.Floor);
        }
        Console.WriteLine("ARENA_PICKUP_ROTATION_PASSED");
    }
}
