using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;

public static class Program
{
    static int checks;
    static readonly string Token=new('1',32);
    static void Need(bool b,string s){checks++;if(!b)throw new InvalidOperationException("ASSERT: "+s);}
    static MethodInfo Hook(string n)=>typeof(ArenaRecoilLegacyCompat).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static);
    static void Init(Player body,bool adapter=true)
    {
        object[] a={body,null};bool run=!adapter||(bool)Hook("BeforeInit").Invoke(null,a);Exception fault=null;
        try{if(run)ForeignBehavior.Fixture.Init.Invoke(null,new object[]{body});}
        catch(Exception e){fault=e;}
        if(adapter)fault=(Exception)Hook("AfterInit").Invoke(null,new[]{fault,body,a[1]});
        if(fault!=null)throw fault;
    }
    static void Calculate(Player body,Player.FirearmController hands)
    {
        var effect=body.ProceduralWeaponAnimation.Shootingg.NewShotRecoil;
        object[] a={effect,hands,null};bool run=(bool)Hook("BeforeRecalculate").Invoke(null,a);Exception fault=null;
        try
        {
            if(run)ForeignBehavior.Fixture.Calc.Invoke(null,new object[]{effect,hands.Template,new GlobalConfiguration.AimingConfiguration(),hands,0f,0f,0f,0f});
        }
        catch(Exception e){fault=e;}
        fault=(Exception)Hook("AfterRecalculate").Invoke(null,new[]{fault,a[2]});
        if(fault!=null)throw fault;
    }
    static void Update(object component)
    {
        if((bool)Hook("BeforeComponentUpdate").Invoke(null,new[]{component}))ForeignBehavior.Update(component);
    }
    static Fixture Reset(string version="2.0.0",bool wrong=false,bool native=true)
    {
        ArenaRecoilLegacyCompat.Shutdown();Harmony.Bound.Clear();Harmony.DropReceipt=false;Chainloader.PluginInfos.Clear();
        ArenaSession.IsActive=ArenaSession.ArmedForNextRaid=false;ArenaServerClient.HasPendingArm=false;
        ArenaSession.ActiveGame=null;ArenaSession.ActiveWorld=null;ArenaSession.ArmedMap=null;ArenaServerClient.PendingArmToken=null;
        Singleton<AbstractGame>.Instance=null;Singleton<IBotGame>.Instance=null;Singleton<GameWorld>.Instance=null;
        RespawnApi.IsReplacing=false;ForeignBehavior.FailCamera=ForeignBehavior.FailCalc=ForeignBehavior.StaleCalc=false;ForeignBehavior.Calculations=0;
        var f=new Fixture(version,wrong);ForeignBehavior.Fixture=f;
        if(native)f.Bind();
        Chainloader.PluginInfos[ArenaRecoilLegacyContract.PluginGuid]=new(){Instance=(BaseUnityPlugin)Activator.CreateInstance(f.Plugin),
            Metadata=f.Plugin.GetCustomAttribute<BepInPlugin>()};
        ArenaRecoilLegacyCompat.Initialize();return f;
    }
    static LocalGame Game(bool arena=true,bool loading=false)
    {
        var g=new LocalGame{GameWorld=new(),Status=loading?GameStatus.Running:GameStatus.Started};
        Singleton<AbstractGame>.Instance=g;Singleton<GameWorld>.Instance=g.GameWorld;
        Singleton<IBotGame>.Instance=loading?null:g;
        ArenaSession.IsActive=arena&&!loading;ArenaSession.ActiveGame=g;ArenaSession.ActiveWorld=g.GameWorld;
        ArenaSession.ArmedForNextRaid=ArenaServerClient.HasPendingArm=arena&&loading;
        ArenaSession.ArmedMap="factory4_day";ArenaServerClient.PendingArmToken=Token;
        g.Settings.serverId=Protocol.BuildNativeEndIdentity("native",Token);
        return g;
    }
    static LocalPlayer Body(LocalGame g,float weapon=5)
    {
        var b=new GameObject().Attach(new LocalPlayer{GameWorld=g.GameWorld,Profile=g.Profile});
        g.GameWorld.MainPlayer=b; // Native Init registers before the foreign postfix.
        var hands=b.gameObject.Attach(new Player.FirearmController{Template=new(){Value=weapon}});
        b.HandsController=hands;
        b.ProceduralWeaponAnimation.Shootingg.Calculate=()=>Calculate(b,hands);
        return b;
    }
    static LocalPlayer Initial(LocalGame g)
    {
        g.Status=GameStatus.Running;var b=Body(g);Init(b);g.LocalPlayer=b;g.Status=GameStatus.Started;
        Singleton<IBotGame>.Instance=g;ArenaRecoilLegacyCompat.Tick();return b;
    }
    static void Main(string[] args)
    {
        var f=Reset();Need(ArenaRecoilLegacyCompat.GetRejectionReason()==null,"known identity admitted");
        var g=Game();var first=Initial(g);
        Need(!first.gameObject.Destroyed,"initial whole player survives");
        var firstR=first.GetComponent(f.Recoil);var firstC=first.GetComponent(f.Camera);
        Need(ReferenceEquals(f.Instance(f.Recoil),firstR)&&ReferenceEquals(f.Instance(f.Camera),firstC),"both initial owners");
        Need(((Behaviour)firstR).enabled&&((Behaviour)firstC).enabled,"current initialized components enabled");
        Need(ForeignBehavior.Calculations==1,"initial physical controller recalculated once");
        ArenaRecoilLegacyCompat.Tick();Need(ForeignBehavior.Calculations==1,"no per-frame recoil calculation");
        for(int i=0;i<5;i++)
        {
            var old=g.LocalPlayer;var oldR=old.GetComponent(f.Recoil);var oldC=old.GetComponent(f.Camera);
            RespawnApi.IsReplacing=true;var next=Body(g,10+i);
            try{Init(next);}catch(Exception){Need(false,"replacement initialized without singleton destruction "+i);}
            Need(!next.gameObject.Destroyed&&!old.gameObject.Destroyed,"replacement and old body survive "+i);
            Need(!((Behaviour)oldR).enabled&&!((Behaviour)oldC).enabled,"old pair disabled pending "+i);
            int rotations=old.Rotations;Update(oldR);Need(old.Rotations==rotations,"retired body cannot rotate "+i);
            var nr=next.GetComponent(f.Recoil);Update(nr);Need(next.Rotations==0,"uncommitted body cannot rotate "+i);
            Calculate(next,(Player.FirearmController)next.HandsController);
            Need(f.Cache("CurrentFirearmController")==null,"uncommitted cache cannot publish "+i);
            g.LocalPlayer=next;RespawnApi.Replace(old,next);RespawnApi.IsReplacing=false;
            Need(oldR.Destroyed&&oldC.Destroyed&&!old.gameObject.Destroyed,"only old components destroyed "+i);
            Need(ReferenceEquals(f.Instance(f.Recoil),nr),"singleton current "+i);
            Need(((Vector2)f.Cache("CurrentRecoilVals")).x==10+i,"recalculation uses incoming weapon "+i);
            int calculations=ForeignBehavior.Calculations;ArenaRecoilLegacyCompat.Tick();
            Calculate(old,(Player.FirearmController)old.HandsController);
            Need(ForeignBehavior.Calculations==calculations,"retired controller remains guarded after component release "+i);
            Update(nr);Need(next.Rotations==1,"current recoil active "+i);
            Init(next);Need(next.gameObject.Components.Count(c=>c.GetType()==f.Recoil&&!c.Destroyed)==1,"duplicate init idempotent "+i);
        }
        var body=g.LocalPlayer;var previous=(Player.FirearmController)body.HandsController;
        var newHands=body.gameObject.Attach(new Player.FirearmController{Template=new(){Value=77}});
        body.HandsController=newHands;Calculate(body,newHands);
        Need(((Vector2)f.Cache("CurrentRecoilVals")).x==77,"weapon switch does not use prior weapon");
        f.Cache("CurrentFirearmController",previous);f.Cache("CurrentRecoilVals",new Vector2(123,456));
        ForeignBehavior.FailCalc=true;try{Calculate(body,newHands);Need(false,"exception propagated");}catch(TargetInvocationException){}
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),previous)&&((Vector2)f.Cache("CurrentRecoilVals")).x==123,"exception restores complete cache");
        ForeignBehavior.FailCalc=false;ForeignBehavior.StaleCalc=true;Calculate(body,newHands);
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),previous),"stale recalc restores previous controller");
        g.GameWorld.MainPlayer=body;ForeignBehavior.StaleCalc=false;

        f=Reset();g=Game();body=Initial(g);
        var oldRecoil=body.GetComponent(f.Recoil);var oldCamera=body.GetComponent(f.Camera);
        RespawnApi.IsReplacing=true;var failed=Body(g);Init(failed);RespawnApi.Fail(body);
        ArenaRecoilLegacyCompat.Tick();Need(!oldRecoil.Destroyed,"failure notification does not destroy recoverable old component");
        g.GameWorld.MainPlayer=body;RespawnApi.IsReplacing=false;ArenaRecoilLegacyCompat.Tick();
        Need(ReferenceEquals(f.Instance(f.Recoil),oldRecoil)&&ReferenceEquals(f.Instance(f.Camera),oldCamera),"old singleton pair restored");
        Need(((Behaviour)oldRecoil).enabled&&!body.gameObject.Destroyed&&!failed.gameObject.Destroyed,"failed replacement preserves both native bodies");
        Need(failed.GetComponent(f.Recoil)==null&&failed.GetComponent(f.Camera)==null,"failed components retired");
        Update(oldRecoil);Need(body.Rotations==1,"recovered old body recoil active");

        f=Reset();g=Game();body=Initial(g);
        RespawnApi.IsReplacing=true;failed=Body(g);Init(failed);g.LocalPlayer=failed;RespawnApi.Fail(body);
        RespawnApi.IsReplacing=false;ArenaRecoilLegacyCompat.Tick();
        Need(ReferenceEquals(f.Instance(f.Recoil),failed.GetComponent(f.Recoil)),"rewire recovery can retain authoritative new body");
        Need(!failed.gameObject.Destroyed,"rewire recovery never destroys new GameObject");

        f=Reset();g=Game();body=Initial(g);oldRecoil=body.GetComponent(f.Recoil);
        RespawnApi.IsReplacing=true;ForeignBehavior.FailCamera=true;failed=Body(g);
        try{Init(failed);Need(false,"partial initializer must fault");}catch(TargetInvocationException){}
        ForeignBehavior.FailCamera=false;g.GameWorld.MainPlayer=body;RespawnApi.IsReplacing=false;ArenaRecoilLegacyCompat.Tick();
        Need(!failed.gameObject.Destroyed&&!body.gameObject.Destroyed,"partial Awake failure never destroys players");
        Need(ReferenceEquals(f.Instance(f.Recoil),oldRecoil),"partial failure restores previous singleton");

        f=Reset();g=Game(false);body=Body(g);Init(body);g.LocalPlayer=body;
        Need(ReferenceEquals(f.Instance(f.Recoil),body.GetComponent(f.Recoil)),"ordinary initializer unchanged");
        var ordinary=body;var duplicate=Body(g);Init(duplicate);
        Need(duplicate.gameObject.Destroyed,"negative control reproduces upstream whole-player destruction");
        f=Reset();g=Game();body=Initial(g);RespawnApi.IsReplacing=true;duplicate=Body(g);Init(duplicate,false);
        Need(duplicate.gameObject.Destroyed,"negative control disabling adapter breaks actual replacement");

        f=Reset();g=Game(loading:true);body=Initial(g);
        Need(!body.gameObject.Destroyed&&body.GetComponent(f.Recoil)!=null,"server-confirmed initial load accepted without IBotGame");
        f=Reset();g=Game(loading:true);g.Settings.serverId="ordinary";body=Body(g);Init(body);
        Need(body.GetComponent(f.Recoil)!=null,"unconfirmed ordinary loading remains original");
        var orphan=new GameObject().Attach(new LocalPlayer());ForeignBehavior.Init(orphan);
        Need(orphan.gameObject.Destroyed,"ordinary behavior not globally repaired");

        f=Reset();g=Game();body=Initial(g);
        var unrelated=body.gameObject.Attach(new Player.FirearmController());
        f.Cache("CurrentFirearmController",unrelated);RespawnApi.End();
        Need(f.Instance(f.Recoil)==null&&f.Instance(f.Camera)==null,"end clears only owned singleton pair");
        Need(!body.gameObject.Destroyed,"end never destroys native body");
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),unrelated),"end preserves unowned controller cache");
        int count=ForeignBehavior.Calculations;Calculate(body,(Player.FirearmController)body.HandsController);
        Need(ForeignBehavior.Calculations==count,"ended-raid controller remains guarded by weak tombstone");
        var ordinaryGame=Game(false);var ordinaryAfter=Body(ordinaryGame,90);Init(ordinaryAfter);ordinaryGame.LocalPlayer=ordinaryAfter;
        var ordinaryAfterRecoil=ordinaryAfter.GetComponent(f.Recoil);
        ArenaRecoilLegacyCompat.Tick();Update(ordinaryAfterRecoil);
        Need(ordinaryAfter.Rotations==1&&!ordinaryAfter.gameObject.Destroyed,"same-process subsequent ordinary player remains native");
        Calculate(ordinaryAfter,(Player.FirearmController)ordinaryAfter.HandsController);
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),ordinaryAfter.HandsController),"subsequent ordinary cache remains foreign-owned");
        count=ForeignBehavior.Calculations;Calculate(body,(Player.FirearmController)body.HandsController);
        Need(ForeignBehavior.Calculations==count&&ReferenceEquals(f.Cache("CurrentFirearmController"),ordinaryAfter.HandsController),"late Arena callback cannot overwrite new ordinary controller");
        RespawnApi.End();
        Need(ReferenceEquals(f.Instance(f.Recoil),ordinaryAfterRecoil)&&!ordinaryAfterRecoil.Destroyed,"ordinary raid end leaves foreign ordinary components intact");
        f=Reset();g=Game(false);ordinary=Body(g);Init(ordinary);g.LocalPlayer=ordinary;
        var otherSingleton=f.Instance(f.Recoil);g=Game();g.Status=GameStatus.Running;body=Body(g);Init(body);
        Need(!body.gameObject.Destroyed&&ReferenceEquals(f.Instance(f.Recoil),otherSingleton),"unrelated singleton never destroyed or cleared");
        Need(ArenaRecoilLegacyCompat.GetRejectionReason()!=null,"unrelated singleton rejects Arena safely");
        foreach(var bad in new[]{0,1,2,3,4,5})
        {
            bool exact=ArenaRecoilLegacyScope.ExactRaid(bad!=0,bad!=1,bad!=2,"factory4_day",bad==3?"laboratory":"factory4_day",
                false,true,true,"factory4_day",bad==4?"bad":Token,bad==5?"ordinary":Protocol.BuildNativeEndIdentity("native",Token));
            Need(!exact,"scope rejects invalid pair/lifecycle/side/map/token/native identity "+bad);
        }
        f=Reset("1.10.0");Need(ArenaRecoilLegacyCompat.GetRejectionReason()!=null,"old release rejected");
        f=Reset("2.1.0");Need(ArenaRecoilLegacyCompat.GetRejectionReason()==null,"forward API validated version admitted");
        f=Reset(wrong:true);Need(ArenaRecoilLegacyCompat.GetRejectionReason()!=null,"foreign type drift rejected");
        f=Reset(native:false);Need(ArenaRecoilLegacyCompat.GetRejectionReason()!=null,"missing native hook rejected");
        f=Reset();Harmony.GetPatchInfo(f.Init).Prefixes.Clear();
        Need(ArenaRecoilLegacyCompat.GetRejectionReason()!=null,"missing adapter receipt rejects admission");
        f=Reset();g=Game();body=Initial(g);var ordinaryWorld=new GameWorld();
        Singleton<GameWorld>.Instance=ordinaryWorld;Update(body.GetComponent(f.Recoil));
        Need(body.Rotations==0,"old world update does not cross new world");
        ArenaRecoilLegacyCompat.Shutdown();
        if(args.Length>0)PublishedMetadata.Verify(args[0],Need);
        Console.WriteLine("Recoil Legacy compatibility: "+checks+" checks passed (native Unity/Harmony integration not simulated).");
    }
}
