using ArenaMode.Combat;
using ArenaMode.Combat.Translocator;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Reflection;

internal static class Program
{
    private static int _checks;
    private sealed record Fixture(Player Player,PortableRangeFinderController Hands,WeaponPrefab Prefab,FirearmsAnimator Firearms,TestAnimator Animator);
    private static void Require(bool valid,string message) { _checks++; if(!valid)throw new Exception("FAIL: "+message); }
    private static bool Fire(Fixture f)=>f.Animator.GetBool(AnimationControllerParametersTable.BOOL_FIRE);
    private static void Flag(Fixture f,int key,bool value)=>f.Animator.Values[key]=value;
    private static Fixture New()
    {
        // Isolate test cases, not a production seam. Normal cleanup is exercised
        // below through the public lifecycle and the actually installed prefixes.
        ArenaTranslocatorPose.Shutdown();
        foreach(var name in new[]{"_attempted","_ready","_stopped","_warned"})
            typeof(ArenaTranslocatorPose).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);
        Harmony.Hooks.Clear(); Harmony.PatchCount=Harmony.FailAt=Harmony.Unpatches=0; AccessTools.Missing=false;
        ArenaTranslocatorRuntime.Live=true; ArenaTranslocatorSupply.HasVerifiedTemplate=true; WeaponSwapService.IsSwapping=false;
        EFTHardSettings.Instance=new();
        var firearms=new FirearmsAnimator(); var prefab=new WeaponPrefab{FirearmsAnimator=firearms};
        var hands=new PortableRangeFinderController{FirearmsAnimator=firearms,Prefab=prefab};
        var player=new Player{HandsController=hands,HandsAnimator=firearms};
        hands._player=player;
        ((TestAnimator)firearms.Animator).Values[AnimationControllerParametersTable.BOOL_ACTIVE]=true;
        Singleton<GameWorld>.Instance=new(){MainPlayer=player};
        ArenaTranslocatorSupply.Owner=player; ArenaTranslocatorSupply.Item=hands.Item;
        return new(player,hands,prefab,firearms,(TestAnimator)firearms.Animator);
    }
    private static void Block(string name,Action<Fixture> change)
    { var f=New(); change(f); ArenaTranslocatorPose.Tick(); Require(f.Animator.Writes==0&&f.Firearms.NativeSets==0,"blocked "+name); }
    private static void Main()
    {
        try
        {
            var f=New(); ArenaTranslocatorPose.Tick();
            Require(Fire(f)&&f.Firearms.NativeSets==1,"native raised pose");
            Require(!f.Hands.CanInteract()&&ArenaTranslocatorPose.CanOperate(f.Hands),"raised phase remains operable despite native idle predicate");
            Require(!f.Hands.IsAiming&&f.Hands.AimWrites==0&&!f.Hands.PwaAiming&&!f.Hands.OpticEnabled&&!f.Hands.RangefinderEnabled,"no ADS or optics");
            var writes=f.Animator.Writes;
            ArenaTranslocatorPose.Tick(); Require(f.Animator.Writes==writes&&f.Firearms.NativeSets==1,"no repeated native setter");
            Require(Fire(f),"native nonidle raised phase does not lower pose");
            var diagnostic=ArenaTranslocatorPose.Diagnostic;
            Require(diagnostic.Contains("private; ready=True")&&diagnostic.Contains("canInteract=False")&&diagnostic.Contains("fire=True")&&diagnostic.Contains("phase=102/2")&&f.Animator.Writes==writes,"diagnostic reads native phase without writes");
            Require(Harmony.Hooks.Count==2,"both pool lifecycle hooks");
            ArenaTranslocatorPose.Reset(); Require(!Fire(f),"restore exact false flag");
            writes=f.Animator.Writes; ArenaTranslocatorPose.Reset(); Require(f.Animator.Writes==writes,"reset idempotent");

            f=New(); Flag(f,AnimationControllerParametersTable.BOOL_FIRE,true); ArenaTranslocatorPose.Tick(); ArenaTranslocatorPose.Reset();
            Require(Fire(f)&&f.Firearms.NativeSets==0&&f.Animator.Writes==0,"original true is untouched");

            Block("inactive",_=>ArenaTranslocatorRuntime.Live=false);
            Block("capability",_=>ArenaTranslocatorSupply.HasVerifiedTemplate=false);
            Block("ordinary item",x=>x.Hands.Item=new());
            Block("other player",_=>ArenaTranslocatorSupply.Owner=new());
            Block("no world",_=>Singleton<GameWorld>.Instance=null);
            Block("no player",_=>Singleton<GameWorld>.Instance.MainPlayer=null);
            Block("destroyed player",x=>x.Player.Destroyed=true);
            Block("other controller",x=>x.Player.HandsController=new object());
            Block("ADS",x=>x.Hands.IsAiming=true);
            Block("swapping",_=>WeaponSwapService.IsSwapping=true);
            Block("changing weapon",x=>x.Player.InventoryController.IsChangingWeapon=true);
            Block("missing inventory",x=>x.Player.InventoryController=null);
            Block("draw operation",x=>x.Hands.CurrentHandsOperation=new PortableRangeFinderController.DrawOperation());
            Block("missing operation",x=>x.Hands.CurrentHandsOperation=null);
            Block("finished operation",x=>x.Hands.CurrentHandsOperation.State=Player.EOperationState.Finished);
            Block("inactive hands",x=>Flag(x,AnimationControllerParametersTable.BOOL_ACTIVE,false));
            Block("missing Active parameter",x=>x.Firearms.HasActive=false);
            Block("hands action",x=>x.Player.InventoryController.HandsAction=true);
            foreach(var phase in new[]{"Hands.LOOK","Hands.CLOSE INVENTORY","Hands.OUT TO IDLE","Hands.AIM_TO_IDLE","Hands.FIRE"})
                Block("main phase "+phase,x=>x.Animator.PhaseOverride=phase);
            Block("next inspect phase",x=>{x.Animator.Transition=true;x.Animator.NextOverride="Hands.LOOK";});
            Block("strict interaction",x=>x.Hands.StrictInteraction=true);
            Block("missing native settings",_=>EFTHardSettings.Instance=null);
            Block("missing locomotion state",x=>x.Player.CurrentState=null);
            Block("native aim forbidden",_=>EFTHardSettings.Instance.CanAim=false);
            Block("overlap",x=>x.Hands._overlap=.5001f);
            Block("overlap NaN",x=>x.Hands._overlap=float.NaN);
            Block("missing firearms",x=>x.Hands.FirearmsAnimator=null);
            Block("other hands animator",x=>x.Player.HandsAnimator=new());
            Block("missing Fire parameter",x=>x.Firearms.HasFire=false);
            Block("inventory",x=>Flag(x,AnimationControllerParametersTable.BOOL_INVENTORY,true));
            Block("left hand interaction",x=>Flag(x,AnimationControllerParametersTable.BOOL_LEFT,true));
            Block("missing animator",x=>x.Firearms.Animator=null);
            Block("missing prefab",x=>x.Hands.Prefab=null);
            Block("pooled prefab",x=>x.Prefab.IsInPool=true);
            Block("stub prefab",x=>x.Prefab.IsStub=true);
            Block("inactive prefab",x=>x.Prefab.gameObject.activeInHierarchy=false);
            Block("other prefab animator",x=>x.Prefab.FirearmsAnimator=new());

            f=New(); f.Hands._overlap=.5f; ArenaTranslocatorPose.Tick(); Require(Fire(f),"native overlap threshold inclusive");
            f=New(); ArenaTranslocatorPose.Tick(); f.Animator.PhaseOverride="Hands.IDLE"; f.Animator.NextOverride="Hands.AIMING"; f.Animator.Transition=true; ArenaTranslocatorPose.Tick();
            Require(Fire(f)&&f.Firearms.NativeSets==1&&ArenaTranslocatorPose.CanOperate(f.Hands),"raise transition preserves lease");
            f.Animator.PhaseOverride="Hands.AIMING";f.Animator.NextOverride="Hands.IDLE";ArenaTranslocatorPose.Tick();Require(Fire(f)&&f.Firearms.NativeSets==1,"idle transition preserves lease");
            f=New(); ArenaTranslocatorPose.Tick(); ArenaTranslocatorRuntime.Live=false; ArenaTranslocatorPose.Tick(); Require(!Fire(f),"inactive restores");
            f=New(); ArenaTranslocatorPose.Tick(); f.Player.HandsController=new object(); ArenaTranslocatorPose.Tick(); Require(!Fire(f),"weapon change restores");
            f=New(); ArenaTranslocatorPose.Tick(); f.Hands.Item=new(); ArenaTranslocatorPose.Tick(); Require(!Fire(f),"item ownership loss restores");
            f=New(); ArenaTranslocatorPose.Tick(); f.Hands.CurrentHandsOperation=new PortableRangeFinderController.DrawOperation(); ArenaTranslocatorPose.Tick(); Require(!Fire(f),"transition restores");
            f=New(); ArenaTranslocatorPose.Tick(); Flag(f,AnimationControllerParametersTable.BOOL_LEFT,true); ArenaTranslocatorPose.Tick();
            Require(!Fire(f)&&f.Animator.GetBool(AnimationControllerParametersTable.BOOL_LEFT)&&f.Firearms.NativeSets==1,"restore preserves new left hand interaction");
            f=New(); ArenaTranslocatorPose.Tick(); f.Hands.Item=new(); ArenaTranslocatorSupply.Item=f.Hands.Item; ArenaTranslocatorPose.Tick();
            Require(Fire(f)&&f.Firearms.NativeSets==2,"new private instance gets new lease");
            ArenaTranslocatorPose.Reset(); Require(!Fire(f),"new private instance restores original");

            f=New(); ArenaTranslocatorPose.Tick(); Flag(f,AnimationControllerParametersTable.BOOL_FIRE,false); writes=f.Animator.Writes; ArenaTranslocatorPose.Tick(); ArenaTranslocatorPose.Reset();
            Require(!Fire(f)&&f.Animator.Writes==writes&&f.Firearms.NativeSets==1,"native lowering is not overwritten");
            f=New(); ArenaTranslocatorPose.Tick(); f.Hands.IsAiming=true; ArenaTranslocatorPose.Reset(); Require(Fire(f)&&f.Hands.IsAiming,"external ADS owner preserved");
            f=New(); ArenaTranslocatorPose.Tick(); var foreign=new TestAnimator(); foreign.Values[AnimationControllerParametersTable.BOOL_FIRE]=true; f.Firearms.Animator=foreign; ArenaTranslocatorPose.Reset();
            Require(Fire(f)&&foreign.GetBool(AnimationControllerParametersTable.BOOL_FIRE)&&foreign.Writes==0,"rebound animator untouched");
            f=New(); ArenaTranslocatorPose.Tick(); f.Prefab.FirearmsAnimator=new(); ArenaTranslocatorPose.Reset(); Require(Fire(f),"rebound prefab wrapper untouched");
            f=New(); ArenaTranslocatorPose.Tick(); f.Prefab.Destroyed=true; writes=f.Animator.Writes; ArenaTranslocatorPose.Reset(); Require(f.Animator.Writes==writes,"destroyed prefab untouched");

            f=New(); ArenaTranslocatorPose.Tick(); new WeaponPrefab().ReturnToPool(); Require(Fire(f),"foreign pool event preserves lease");
            f.Prefab.ReturnToPool(); Require(!Fire(f)&&f.Prefab.IsInPool&&f.Prefab.FireBeforeNativeReset==false,"pool return restores before native reset");
            f.Prefab.OnGetFromPool(); f.Hands.Item=new(); ArenaTranslocatorPose.Tick(); Require(!Fire(f),"ordinary reused device remains native");
            f=New(); ArenaTranslocatorPose.Tick(); f.Prefab.OnGetFromPool(); Require(!Fire(f),"pool get restores");

            f=New(); f.Firearms.ThrowAfterSet=true; ArenaTranslocatorPose.Tick(); Require(!Fire(f)&&f.Firearms.NativeSets==1,"partial native setter restores");
            f=New(); Harmony.FailAt=2; ArenaTranslocatorPose.Tick(); Require(!Fire(f)&&f.Animator.Writes==0&&Harmony.Hooks.Count==0,"failed pool install fails closed");
            ArenaTranslocatorPose.Tick(); Require(f.Animator.Writes==0&&Harmony.PatchCount==2,"failed hook install not retried every frame");
            f=New(); AccessTools.Missing=true; ArenaTranslocatorPose.Tick(); Require(f.Animator.Writes==0&&Harmony.Hooks.Count==0,"missing pool seam fails closed");
            f=New(); f.Hands.Item=new(); Require(ArenaTranslocatorPose.Diagnostic=="inactive"&&f.Animator.Writes==0,"ordinary diagnostic inactive");
            f=New(); ArenaTranslocatorRuntime.Live=false; Require(ArenaTranslocatorPose.Diagnostic=="inactive","normal raid diagnostic inactive");
            f=New(); ArenaTranslocatorPose.Tick(); ArenaTranslocatorPose.Shutdown(); Require(!Fire(f)&&Harmony.Hooks.Count==0,"shutdown restores and unpatches");
            writes=f.Animator.Writes; ArenaTranslocatorPose.Tick(); Require(f.Animator.Writes==writes,"shutdown inert");
            f=New(); ArenaTranslocatorPose.Shutdown(); ArenaTranslocatorPose.Tick(); Require(f.Animator.Writes==0,"shutdown inert before first use");
            _checks+=NativeContracts.Verify(Path.Combine(Environment.GetEnvironmentVariable("SPT_GAME_ROOT")??"D:/SPT41","EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"));
            Console.WriteLine($"TRANSLOCATOR_POSE_PRODUCTION_PASSED {_checks} assertions");
        }
        catch(Exception error) {Console.Error.WriteLine(error); Environment.ExitCode=1;}
    }
}
