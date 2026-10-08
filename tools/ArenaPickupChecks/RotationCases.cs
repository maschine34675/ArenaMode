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

internal static partial class Program
{
    private static void OccupiedPair(ArenaPickupKind second=ArenaPickupKind.Shield, bool delayWeapon=false,
        ArenaPickupKind firstKind=ArenaPickupKind.DoubleDamage)
    {
        Fixture();
        var first=Point(firstKind);first.Alternatives=[new(){X=10}];
        var other=Point(second);other.Id="two";other.X=10;other.Alternatives=[new(){X=0}];
        var layout=FileFor(first);layout.Layouts[0].Points.Add(other);
        System.IO.File.WriteAllText(LayoutPath,ArenaPickupLayouts.Serialize(layout));
        KitBundlePreloader.IsPreloading=delayWeapon;
        Step();Step();
        Check(ArenaPickupView.Created.Count(v=>!v.Disposed)==2,"occupied fixture has two safe slots and no spare");
    }

    private static void OccupiedRotationCases()
    {
        OccupiedPair();var a=ActiveView();var b=ActiveView("two");var sa=State();var sb=State(1);
        At(61);
        Check(ActiveView().Floor.x==b.Floor.x&&ActiveView("two").Floor.x==a.Floor.x,
            "two occupied available slots exchange without a spare position");
        Check(a.Disposed&&b.Disposed&&ReferenceEquals(sa,State())&&ReferenceEquals(sb,State(1))
            &&sa.Appearance==1&&sb.Appearance==1,"exchange preserves both state owners and advances each appearance once");
        Check(ArenaPickupView.Created.Count(v=>!v.Disposed)==2&&ArenaPowerups.Grants.Count==0,
            "exchange leaves exactly two markers and grants no effects");
        At(62);Check(sa.Appearance==1&&sb.Appearance==1,"exchange partner cannot move twice in one rotation interval");
        At(121);Check(ActiveView().Floor.x==a.Floor.x&&ActiveView("two").Floor.x==b.Floor.x,
            "occupied rotation repeats at the next interval");

        OccupiedPair();a=ActiveView();b=ActiveView("two");Move(_local,a.Floor.x);var deadline=State().ReadyAt;
        Move(_local,-3);At(deadline-.01f);
        Check(ReferenceEquals(a,ActiveView())&&ReferenceEquals(b,ActiveView("two")),"exchange never shortens collection cooldown");
        At(deadline);Check(ReferenceEquals(a,ActiveView())&&State().Phase==ArenaPickupPhase.Available,
            "cooldown rearm does not displace a newly available partner");
        At(deadline+60);Check(ActiveView().Floor.x==b.Floor.x&&State().Phase==ArenaPickupPhase.Available,
            "full cooldown eventually exchanges with a mature available occupied slot");

        OccupiedPair();a=ActiveView();b=ActiveView("two");_local.Position=b.Floor;At(61);
        Check(ArenaPowerups.Grants.Count==0,"exchange requires fresh entry for actors at the new destination");
        Move(_local,b.Floor.x+3);Move(_local,b.Floor.x);
        Check(ArenaPowerups.Grants.Count==1,"fresh reentry after exchange grants once");

        foreach(string blocked in new[]{"cooldown","pending","retired","preparing","unknownWeapon","nativePending","path","clearance","asymmetric"})
        {
            OccupiedPair(blocked is "preparing" or "unknownWeapon" or "nativePending"?ArenaPickupKind.Weapon:ArenaPickupKind.Shield);
            a=ActiveView();b=ActiveView("two");
            switch(blocked)
            {
                case "cooldown":int token=State(1).Reserve(.5f);State(1).Complete(token,true,1,600);break;
                case "pending":State(1).Reserve(60);break;
                case "retired":State(1).Retire();break;
                case "preparing":
                    var entries=(System.Collections.IList)typeof(ArenaPickupRuntime).GetField("Entries",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
                    entries[1].GetType().GetField("Preparing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(entries[1],true);break;
                case "unknownWeapon":ArenaWeaponPickup.Created.Single().IsAvailable=false;break;
                case "nativePending":ArenaWeaponPickup.Created.Single().IsPending=true;break;
                case "path":NavMesh.PathOk=false;break;
                case "clearance":Physics.Capsule=(x,y,r)=>x.x==a.Floor.x?[new Collider()]:[];break;
                case "asymmetric":
                    var rows=(System.Collections.IList)typeof(ArenaPickupRuntime).GetField("Entries",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
                    var candidates=(ArenaPickupPoint)rows[1].GetType().GetField("Candidates",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(rows[1]);
                    candidates.X=b.Floor.x;candidates.Alternatives=[new(){X=30}];NavMesh.AllowPath=(x,y)=>y.x!=30;break;
            }
            At(61);
            Check(ReferenceEquals(a,ActiveView())&&ReferenceEquals(b,ActiveView("two")),"unsafe exchange retains both original appearances: "+blocked);
        }

        OccupiedPair();a=ActiveView();b=ActiveView("two");
        ArenaPickupView.RefuseCreate=_=>ArenaPickupView.Created.Count>=3;At(61);
        Check(ReferenceEquals(a,ActiveView())&&ReferenceEquals(b,ActiveView("two"))&&State().Appearance==0&&State(1).Appearance==0
            &&ArenaPickupView.Created.Count(v=>!v.Disposed)==2,"second replacement failure leaves both originals and disposes staged first view");

        OccupiedPair(ArenaPickupKind.Weapon);a=ActiveView();b=ActiveView("two");var weapon=ArenaWeaponPickup.Created.Single();At(61);
        Check(weapon.Disposed&&ArenaWeaponPickup.Created.Count==2&&ArenaWeaponPickup.Created.Last().Position.x==a.Floor.x
            &&ActiveView().Floor.x==b.Floor.x,"occupied weapon partner moves and replaces exactly its owned source");
        Check(State().Appearance==1&&State(1).Appearance==1&&ArenaWeaponPickup.Created.Last().Kit.Id==weapon.Kit.Id,
            "weapon exchange preserves kit and both reservations");
        At(121);Check(ArenaWeaponPickup.Created.Count==3,"occupied weapon continues rotating after source preparation");

        OccupiedPair(ArenaPickupKind.Weapon,delayWeapon:true);a=ActiveView();b=ActiveView("two");
        At(58);KitBundlePreloader.IsPreloading=false;Step();At(61);
        Check(ReferenceEquals(a,ActiveView())&&ReferenceEquals(b,ActiveView("two"))&&ArenaWeaponPickup.Created.Count==1,
            "exchange respects a newly prepared weapon availability interval");
        At(119);Check(ActiveView("two").Floor.x==a.Floor.x&&ArenaWeaponPickup.Created.Count==2,
            "staggered availability cannot starve an occupied weapon exchange");

        OccupiedPair(ArenaPickupKind.Weapon,delayWeapon:true,firstKind:ArenaPickupKind.Weapon);
        a=ActiveView();b=ActiveView("two");At(60.6f);KitBundlePreloader.IsPreloading=false;At(60.7f);At(120.7f);
        Check(ActiveView().Floor.x==b.Floor.x&&ActiveView("two").Floor.x==a.Floor.x&&ArenaWeaponPickup.Created.Count==4,
            "partner availability accepts the exact float deadline without another minute delay");

        OccupiedPair(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();a=ActiveView();b=ActiveView("two");
        Move(_local,b.Floor.x);weapon.Settle(true);Move(_local,-3);At(State(1).ReadyAt);Step(60);
        Check(ArenaWeaponPickup.Created.Count==3&&ActiveView("two").Floor.x==a.Floor.x,
            "consumed weapon can exchange only after its confirmed full cooldown");

        OccupiedPair(ArenaPickupKind.Weapon);weapon=ArenaWeaponPickup.Created.Single();
        weapon.Disposing=()=>{ArenaPickupRuntime.Reset();ArenaPickupRuntime.Tick();};At(61);
        Check(ArenaPickupRuntime.IsLiveActor(_local)&&ArenaPickupView.Created.Count(v=>!v.Disposed)==2
            &&ArenaWeaponPickup.Created.Count==2&&_game.Coroutines.Count==0,
            "exchange source disposal callback cannot orphan or mutate replacement generation");

        OccupiedPair(ArenaPickupKind.Weapon,firstKind:ArenaPickupKind.Weapon);
        ArenaWeaponPickup.Created[1].Disposing=()=>{ArenaPickupRuntime.Reset();ArenaPickupRuntime.Tick();};At(61);
        Check(ArenaPickupRuntime.IsLiveActor(_local)&&ArenaPickupView.Created.Count(v=>!v.Disposed)==2
            &&ArenaWeaponPickup.Created.Count==4&&_game.Coroutines.Count==0,
            "two weapon exchange reentry does not prepare a stale initiating weapon");

        OccupiedPair(ArenaPickupKind.Weapon);int generation=ArenaPickupRuntime.Generation,resets=ArenaPowerups.Resets;
        ArenaWeaponPickup.Created.Single().Disposing=()=>throw new InvalidOperationException("exchange cleanup failed");At(61);
        Check(State().Phase==ArenaPickupPhase.Available&&State(1).Phase==ArenaPickupPhase.Retired
            &&ArenaPickupRuntime.Generation==generation&&ArenaPowerups.Resets==resets&&ArenaWeaponPickup.Created.Count==1,
            "exchange cleanup failure retires only uncertain partner without recreating source or resetting effects");

        OccupiedPair();sa=State();sb=State(1);generation=ArenaPickupRuntime.Generation;
        ArenaPickupView.RefuseCreate=_=>{ArenaPickupView.RefuseCreate=null;ArenaPickupRuntime.Reset();ArenaPickupRuntime.Tick();return false;};At(61);
        Check(ArenaPickupRuntime.Generation!=generation&&sa.Appearance==1&&sb.Appearance==1
            &&sa.Phase==ArenaPickupPhase.Retired&&sb.Phase==ArenaPickupPhase.Retired
            &&ArenaPickupView.Created.Count(v=>!v.Disposed)==2,
            "marker constructor generation change discards staged exchange without committing old state");

        RotationReady();NavMesh.PathOk=false;At(61);
        Check(Plugin.ModLogger.Lines.Any(x=>x.Contains("kept its rotation position")&&x.Contains("no complete native path")),
            "blocked rotation explains failed native geometry");
        int logs=Plugin.ModLogger.Lines.Count;At(62);
        Check(Plugin.ModLogger.Lines.Count==logs,"blocked rotation diagnostics stay bounded between deadlines");
        RotationReady();_local.MovementContext.IsGrounded=false;At(61);
        Check(Plugin.ModLogger.Lines.Any(x=>x.Contains("ready grounded navigation anchor")),"blocked rotation explains absent actor anchor");

        // All Factory sites except the SIX actually used by this fixture are
        // obstructed; every free-destination attempt must fail. Exercise the real
        // built-in fallback and every pickup kind, including M60, over two cycles.
        Fixture();System.IO.File.Delete(LayoutPath);Step();Step();
        var before=ArenaPickupView.Created.ToDictionary(v=>v.Point.Id,v=>v.Floor);
        Physics.Capsule=(x,y,r)=>before.Values.Any(v=>(new Vector3(x.x,x.y-.4f,x.z)-v).sqrMagnitude<.01f)?[]:[new Collider()];
        for(int cycle=1;cycle<=2;cycle++)
        {
            At(cycle*61);
            var views=ArenaPickupView.Created.Where(v=>!v.Disposed).ToArray();
            Check(views.Length==6&&views.All(v=>(v.Floor-before[v.Point.Id]).sqrMagnitude>=9),
                "all six occupied Factory identities rotate without any spare safe slot");
            Check(views.All(v=>views.All(other=>ReferenceEquals(v,other)||(v.Floor-other.Floor).sqrMagnitude>=9)),
                "occupied Factory permutation preserves separation");
            Check(Enumerable.Range(0,6).All(i=>State(i).Appearance==cycle),"each occupied appearance moves only once per interval");
            before=views.ToDictionary(v=>v.Point.Id,v=>v.Floor);
        }
#if PICKUP_OBJECTIVES
        AiReady();OccupiedPair();var seeker=Bot("exchange-seeker",-8);Step();At(59.9f);
        var targets=new List<ArenaPickupTarget>();ArenaPickupRuntime.GetBotTargets(seeker,targets);
        Check(targets.Count==2&&targets.All(t=>ArenaPickupRuntime.IsTargetCurrent(seeker,t)),
            "both occupied bot targets are current before exchange");
        At(60.1f);Check(targets.All(t=>!ArenaPickupRuntime.IsTargetCurrent(seeker,t)),
            "exchange invalidates both bot appearance targets immediately");
        var replacements=new List<ArenaPickupTarget>();ArenaPickupRuntime.GetBotTargets(seeker,replacements);
        Check(replacements.Count==2&&replacements.All(t=>targets.Any(old=>ReferenceEquals(t.Identity,old.Identity)
            &&t.Appearance!=old.Appearance&&(t.Position-old.Position).sqrMagnitude>=9)),
            "both exchanged bot targets keep identity with new positions and appearances");
#endif
        Console.WriteLine("ARENA_PICKUP_OCCUPIED_ROTATION_PASSED");
    }
}
