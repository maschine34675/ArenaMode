using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Config;
using ArenaMode.Hud;
using ArenaMode.Movement;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using HarmonyLib;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("ASSERT: " + message); }
    sealed class Fixture
    {
        public Player Player = new();
        public Player.FirearmController Firearm;
        public LocalGame Game;
        public GameWorld World;
        public JumpPlayerState Jump;
        public Fixture()
        {
            ArenaSession.IsActive = Settings.MovementEnabled.Value = Settings.AirHipFireEnabled.Value = Settings.SprintToAds.Value = true;
            SprintAdsPatches.ClearBypassSafety();
            ArenaRespawnDriver.StandDown = ArenaRespawnDriver.IsRespawnInProgress = ArenaSpectatorService.IsActive = false;
            ArenaInputBlocker.Blocked = ArenaAirVault.Pending = false;
            Firearm = new() { _player = Player }; Player.HandsController = Firearm;
            Firearm.CurrentOperation = new Player.FirearmController.Idling { Controller = Firearm };
            Jump = new(Player.MovementContext) { Name = EPlayerState.Jump };
            Player.CurrentManagedState = Player.CurrentState = Jump;
            Game = new() { LocalPlayer = Player }; World = new() { MainPlayer = Player };
            ArenaSession.Game = Singleton<AbstractGame>.Instance = Game;
            ArenaSession.World = Singleton<GameWorld>.Instance = World;
        }
        public bool Allows() => ArenaAirFire.SuitableForAirFire(Firearm);
        public float Authority() => ArenaAirFire.HipFireWeaponAuthority(Player, PlayerAnimator.WEAPON_ROOT_3RD);
    }
    static void Scope()
    {
        foreach (var test in new (string, Action<Fixture>)[] {
            ("ordinary raid", f => ArenaSession.IsActive=false),
            ("master off", f => Settings.MovementEnabled.Value=false),
            ("AI", f => f.Player.IsAI=true), ("remote", f => f.Player.IsYourPlayer=false),
            ("dead", f => f.Player.HealthController.IsAlive=false), ("missing health", f => f.Player.HealthController=null),
            ("missing world", f => Singleton<GameWorld>.Instance=null), ("missing game", f => Singleton<AbstractGame>.Instance=null),
            ("foreign game", f => Singleton<AbstractGame>.Instance=new LocalGame()),
            ("foreign world", f => Singleton<GameWorld>.Instance=new GameWorld { MainPlayer=f.Player }),
            ("foreign current body", f => f.World.MainPlayer=new Player()),
            ("owner handover", f => f.Game.LocalPlayer=new Player()),
            ("stopping", f => f.Game.Status=GameStatus.Stopping), ("stopped", f => f.Game.Status=GameStatus.Stopped),
            ("respawning", f => ArenaRespawnDriver.IsRespawnInProgress=true), ("stand down", f => ArenaRespawnDriver.StandDown=true),
            ("spectating", f => ArenaSpectatorService.IsActive=true), ("inventory", f => f.Player.IsInventoryOpened=true),
            ("blocked UI", f => ArenaInputBlocker.Blocked=true), ("missing context", f => f.Player.MovementContext=null)
        }) {
            var f = new Fixture(); test.Item2(f);
            Check(!ArenaMovementScope.IsLocal(f.Player, true), "scope rejects " + test.Item1);
            Check(!f.Allows(), "fire rejects " + test.Item1);
            Check(f.Authority()==1f, "pose rejects " + test.Item1);
            if (test.Item1 != "inventory" && test.Item1 != "blocked UI")
                Check(!ArenaAirFire.PreventAirAim(f.Firearm), "air ADS scope rejects " + test.Item1);
            f.Firearm.NativeSuitable=true;
            Check(f.Allows(), "native true remains unchanged: " + test.Item1);
        }
        var yes = new Fixture();
        Check(ArenaMovementScope.TryGetPlayer(yes.Player.MovementContext, out var local) && ReferenceEquals(local,yes.Player), "current context resolves exact body");
        Check(!ArenaMovementScope.TryGetPlayer(new MovementContext(), out _), "foreign context rejected");
        yes.Player.IsInventoryOpened=true; ArenaInputBlocker.Blocked=true;
        Check(ArenaMovementScope.IsLocal(yes.Player) && !ArenaMovementScope.IsLocal(yes.Player,true), "input-only restrictions do not suppress passive handling cleanup");
    }
    static void AirStates()
    {
        foreach (var test in new (string,Action<Fixture>)[] {
            ("feature off", f=>Settings.AirHipFireEnabled.Value=false),
            ("aiming", f=>f.Firearm.IsAiming=true), ("not current hands", f=>f.Player.HandsController=new Player.FirearmController()),
            ("not jump", f=>f.Player.CurrentManagedState=new MovementState()),
            ("derived state", f=>f.Player.CurrentManagedState=new DerivedJump(f.Player.MovementContext)),
            ("foreign jump context", f=>f.Jump.MovementContext=new MovementContext()),
            ("falling state", f=>f.Player.CurrentState=new MovementState { Name=EPlayerState.FallDown }),
            ("animator transition", f=>f.Player.CurrentState=new MovementState { Name=EPlayerState.Transition }),
            ("grounded", f=>f.Player.MovementContext.IsGrounded=true),
            ("pushing", f=>f.Jump._jumpState=JumpPlayerState.EJumpState.PushingFromTheGround),
            ("bumped", f=>f.Jump._jumpState=JumpPlayerState.EJumpState.Bumbped),
            ("prone", f=>f.Player.MovementContext.IsInPronePose=true),
            ("mounted", f=>f.Player.MovementContext.IsInMountedState=true),
            ("firearms blocked", f=>f.Player.MovementContext.BlockFirearms=true),
            ("vault animation", f=>f.Player.MovementContext.Vaulting=true),
            ("pending mantle", f=>ArenaAirVault.Pending=true)
        }) { var f=new Fixture(); test.Item2(f); Check(!f.Allows(), "air rejects " + test.Item1); }
        var good=new Fixture(); Check(good.Allows(), "genuine local jump permits hip fire");
        Check(good.Firearm.Reads==1, "native predicate evaluated once");
        good.Jump.Name=EPlayerState.Idle; good.Firearm.NativeSuitable=true;
        Check(good.Allows(), "ordinary suitable state remains native");
        var nonfire=new Player.ItemHandsController { _player=good.Player };
        good.Player.HandsController=nonfire;
        Check(!ArenaAirFire.SuitableForAirFire(nonfire), "other hand types never gain permission");
    }
    static MethodInfo Hook(Type owner,string patch,string method) => owner.GetNestedType(patch,BindingFlags.NonPublic)
        .GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static);
    static object AirHook(string patch,string method,params object[] args)
        => Hook(typeof(ArenaAirFire),patch,method).Invoke(null,args);
    static object AdsHook(string patch,string method,params object[] args)
        => Hook(typeof(SprintAdsPatches),patch,method).Invoke(null,args);
    static void Pose()
    {
        var f=new Fixture();
        f.Player.MovementContext.IsSprintEnabled=f.Player.MovementContext.AnimatorSprint=true;
        Check(f.Authority()==0f, "sprint jump uses ready first-person weapon authority");
        Check(f.Player.CurveReads==1, "native curve read exactly once");
        Check(f.Player.MovementContext.IsSprintEnabled && f.Player.MovementContext.AnimatorSprint
            && f.Player.MovementContext.SprintWrites==0 && f.Player.MovementContext.AnimatorWrites==0, "pose keeps native sprint and motion state");
        f.Firearm.CurrentOperation=new Player.FirearmController.FireOperationBase {Controller=f.Firearm};
        Check(f.Authority()==0f, "hip fire pose persists during real firing operation");
        foreach(var test in new (string,Action<Fixture>)[] {
            ("third person",x=>x.Player.FirstPersonPointOfView=false),
            ("idle jump leaf",x=>x.Player.BodyAnimatorCommon.CurrentHash=232800732),
            ("moving jump leaf",x=>x.Player.BodyAnimatorCommon.CurrentHash=1056621489),
            ("missing animator",x=>x.Player.BodyAnimatorCommon=null),
            ("feature off",x=>Settings.AirHipFireEnabled.Value=false),
            ("grounded",x=>x.Player.MovementContext.IsGrounded=true),
            ("landing state",x=>x.Player.CurrentState=new MovementState {Name=EPlayerState.Transition}),
            ("push phase",x=>x.Jump._jumpState=JumpPlayerState.EJumpState.PushingFromTheGround),
            ("blocked firearms",x=>x.Player.MovementContext.BlockFirearms=true),
            ("pending mantle",x=>ArenaAirVault.Pending=true),
            ("vaulting",x=>x.Player.MovementContext.Vaulting=true),
            ("foreign firearm owner",x=>x.Firearm._player=new Player()),
            ("meds or knife",x=>x.Player.HandsController=new Player.ItemHandsController()),
            ("ADS",x=>x.Firearm.IsAiming=true), ("blindfire",x=>x.Firearm.Blindfire=true),
            ("interaction",x=>x.Firearm.Interaction=true),
            ("foreign operation",x=>x.Firearm.CurrentOperation.Controller=new Player.FirearmController()),
            ("reload",x=>x.Firearm.CurrentOperation=new Player.FirearmController.BaseReloadOperation {Controller=x.Firearm}),
            ("launcher",x=>x.Firearm.CurrentOperation=new Player.FirearmController.LauncherFire {Controller=x.Firearm}),
            ("missing operation",x=>x.Firearm.CurrentOperation=null)
        }) { f=new Fixture(); test.Item2(f); Check(f.Authority()==1f,"pose rejects " + test.Item1); }
        f=new Fixture();
        f.Player.BodyAnimatorCommon.CurrentHash=1; f.Player.BodyAnimatorCommon.NextHash=1332272753;
        Check(f.Authority()==1f,"stale destination hash does not admit pose");
        f.Player.BodyAnimatorCommon.Transition=true;
        Check(f.Authority()==0f,"native incoming sprint-jump blend keeps weapon ready");
        f.Player.BodyAnimatorCommon.CurrentHash=1332272753; f.Player.BodyAnimatorCommon.NextHash=1;
        Check(f.Authority()==0f,"native outgoing sprint-jump blend keeps weapon ready while airborne");
        f.Player.BodyAnimatorCommon.Transition=false;
        Check(ArenaAirFire.HipFireWeaponAuthority(f.Player,PlayerAnimator.OTHER_CURVE)==1f,"unrelated visual curve unchanged");
        Check(f.Authority()==0f,"pose acquired without retained state");
        Settings.MovementEnabled.Value=false;
        Check(f.Authority()==1f,"toggle immediately releases pose without cleanup tick");
        Settings.MovementEnabled.Value=true;
        f.Player.HealthController.IsAlive=false;
        Check(f.Authority()==1f,"death immediately releases pose without cleanup tick");
        f=new Fixture(); Check(f.Authority()==0f,"respawn replacement independently acquires pose");
        ArenaSession.IsActive=false; Check(f.Authority()==1f,"raid end releases pose without cleanup tick");
    }
    static void Aim()
    {
        var f=new Fixture();
        foreach(var state in new[]{EPlayerState.Jump,EPlayerState.FallDown,EPlayerState.Sprint,EPlayerState.Transition,EPlayerState.Idle}) {
            f.Player.CurrentState.Name=state;
            object[] args={f.Firearm,true};
            AirHook("ArenaAirFireAimValuePatch","Prefix",args);
            Check(!(bool)args[1],"air ADS setter rejects " + state);
            args[1]=false; AirHook("ArenaAirFireAimValuePatch","Prefix",args);
            Check(!(bool)args[1],"native aim release retained " + state);
        }
        f.Firearm.IsAiming=true;
        AirHook("ArenaAirFireStateAimPatch","Postfix",f.Firearm);
        Check(!f.Firearm.IsAiming,"air entry clears already-held ADS during firing");
        Settings.AirHipFireEnabled.Value=false;
        Check(ArenaAirFire.PreventAirAim(f.Firearm),"air ADS denied even when hip-fire option disabled");
        f.Player.MovementContext.IsGrounded=true; f.Player.CurrentState.Name=EPlayerState.Sprint;
        object[] ground={f.Firearm,true}; AirHook("ArenaAirFireAimValuePatch","Prefix",ground);
        Check((bool)ground[1],"grounded ADS setter remains native");
        f.Player.CurrentState.Name=EPlayerState.Jump;
        Check(ArenaAirFire.PreventAirAim(f.Firearm),"jump state during ground-contact edge still denies ADS");
        f.Player.HandsController=new Player.FirearmController();
        Check(!ArenaAirFire.PreventAirAim(f.Firearm),"stale firearm receives no aim override");
    }
    static object[] BeginAim(Fixture f)
    {
        object[] args={f.Firearm,null}; AdsHook("ArenaSprintAdsAimCancelsSprintPatch","Prefix",args); return args;
    }
    static bool AimGate(Fixture f,bool isAiming=true) => (bool)AdsHook("ArenaSprintAdsAimStateGatePatch","Prefix",f.Firearm.CurrentOperation,isAiming);
    static void EndAim(object[] begin,Exception error=null)
    {
        Check(ReferenceEquals(AdsHook("ArenaSprintAdsAimCancelsSprintPatch","Finalizer",begin[1],error),error),"aim finalizer preserves native exception");
    }
    static Fixture GroundSprint()
    {
        var f=new Fixture(); f.Player.CurrentManagedState=f.Player.CurrentState=new MovementState {Name=EPlayerState.Sprint};
        f.Player.MovementContext.IsGrounded=f.Player.MovementContext.IsSprintEnabled=f.Player.MovementContext.AnimatorSprint=true;
        return f;
    }
    static void GroundAds()
    {
        var f=GroundSprint(); var request=BeginAim(f);
        Check(!f.Player.MovementContext.IsSprintEnabled && !f.Player.MovementContext.AnimatorSprint,"grounded ADS still cancels sprint");
        Check(!AimGate(f) && f.Firearm.IsAiming,"grounded ADS still bypasses sprint animation delay");
        Check(((Player.FirearmController.Idling)f.Firearm.CurrentOperation)._idlingTime==0f,"grounded ADS clears native idling time");
        Check(!(bool)AdsHook("ArenaSprintAdsKeepAimThroughSprintExitPatch","Prefix",f.Firearm,EPlayerState.Transition),"grounded aim survives sprint-exit transition");
        EndAim(request,new InvalidOperationException("native"));
        f.Firearm.IsAiming=false;
        Check(AimGate(f),"throw releases bypass synchronously");
        foreach(var test in new (string,Action<Fixture>)[] {
            ("airborne transition",x=>x.Player.MovementContext.IsGrounded=false),
            ("jump contact",x=>x.Player.CurrentState.Name=EPlayerState.Jump),
            ("managed jump contact",x=>x.Player.CurrentManagedState=x.Jump),
            ("missing state",x=>x.Player.CurrentState=null),
            ("ordinary raid",x=>ArenaSession.IsActive=false),
            ("master off",x=>Settings.MovementEnabled.Value=false),
            ("feature off",x=>Settings.SprintToAds.Value=false),
            ("AI",x=>x.Player.IsAI=true), ("dead",x=>x.Player.HealthController.IsAlive=false),
            ("foreign body",x=>x.World.MainPlayer=new Player()),
            ("foreign game",x=>ArenaSession.Game=new LocalGame()),
            ("foreign hands",x=>x.Player.HandsController=new Player.FirearmController()),
            ("respawn",x=>ArenaRespawnDriver.IsRespawnInProgress=true)
        }) {
            f=GroundSprint(); test.Item2(f); request=BeginAim(f); AimGate(f);
            Check(f.Player.MovementContext.IsSprintEnabled && f.Player.MovementContext.AnimatorSprint && !f.Firearm.IsAiming,
                "sprint ADS rejects " + test.Item1);
            EndAim(request);
        }
        foreach(var test in new (string,Action<Fixture>)[] {
            ("blindfire",x=>x.Firearm.Blindfire=true),
            ("overlap",x=>x.Firearm._overlapValue=1f),
            ("barrel reload",x=> {x.Firearm.FirearmsAnimator.Idling=false;x.Firearm.Item.ReloadMode=EFT.InventoryLogic.Weapon.EReloadMode.OnlyBarrel;}),
            ("belt animation",x=> {x.Firearm.FirearmsAnimator.Idling=false;x.Firearm.Item.IsBeltMachineGun=true;})
        }) { f=GroundSprint(); test.Item2(f); request=BeginAim(f); Check(AimGate(f) && !f.Firearm.IsAiming,"native ADS restriction retained " + test.Item1); EndAim(request); }
        f=GroundSprint(); request=BeginAim(f);
        f.Player.MovementContext.IsGrounded=false;
        Check(!AimGate(f) && !f.Firearm.IsAiming,"ground-to-air during bypass denies ADS");
        f.Firearm.IsAiming=true;
        Check((bool)AdsHook("ArenaSprintAdsKeepAimThroughSprintExitPatch","Prefix",f.Firearm,EPlayerState.Transition),"airborne transition does not preserve ADS");
        EndAim(request);
        f=GroundSprint(); request=BeginAim(f);
        var other=new Player.FirearmController {_player=f.Player,CurrentOperation=new Player.FirearmController.Idling()};
        other.CurrentOperation.Controller=other; f.Player.HandsController=other;
        Check((bool)AdsHook("ArenaSprintAdsAimStateGatePatch","Prefix",other.CurrentOperation,true) && !other.IsAiming,"bypass belongs to exact initiating firearm");
        EndAim(request);
    }
    static List<CodeInstruction> Rewrite(List<CodeInstruction> input)
        => ((IEnumerable<CodeInstruction>)typeof(ArenaAirFire).GetNestedType("ArenaAirFireTriggerStatePatch",BindingFlags.NonPublic)
            .GetMethod("Transpiler",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{input})).ToList();
    static void Transpiler()
    {
        var get=AccessTools.PropertyGetter(typeof(Player.ItemHandsController),"SuitableForHandInput");
        var label=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator().DefineLabel();
        var source=new List<CodeInstruction>{new(OpCodes.Ldarg_0),new(OpCodes.Callvirt,get),new(OpCodes.Ret)};
        source[1].labels.Add(label);
        var output=Rewrite(source);
        Check(source[1].operand.Equals(get), "transpiler leaves incoming instructions intact");
        Check(output.Count==source.Count && output[0].opcode==OpCodes.Ldarg_0 && output[2].opcode==OpCodes.Ret, "only intended predicate changes");
        Check(output[1].opcode==OpCodes.Call && ((MethodInfo)output[1].operand).Name=="SuitableForAirFire" && output[1].labels.SequenceEqual(source[1].labels), "call replacement preserves branch target and static signature");
        var dynamic=new DynamicMethod("airPredicate",typeof(bool),new[]{typeof(Player.ItemHandsController)},typeof(Program),true);
        var il=dynamic.GetILGenerator(); il.Emit(output[0].opcode); il.Emit(output[1].opcode,(MethodInfo)output[1].operand); il.Emit(output[2].opcode);
        var actual=(Func<Player.ItemHandsController,bool>)dynamic.CreateDelegate(typeof(Func<Player.ItemHandsController,bool>));
        var f=new Fixture(); Check(actual(f.Firearm), "rewritten trigger predicate executes production gate");
        f.Player.MovementContext.IsGrounded=true; Check(!actual(f.Firearm), "rewritten predicate retains excluded context");
        foreach(int count in new[]{0,2}) {
            var drift=Enumerable.Range(0,count).Select(_=>new CodeInstruction(OpCodes.Callvirt,get)).ToList();
            Check(Rewrite(drift).All(x=>x.Calls(get)), "ambiguous or missing native call fails open " + count);
        }
        Check(Rewrite(output).All(x=>x.opcode!=OpCodes.Callvirt), "another rewrite never compounds an existing patch");
    }
    static List<CodeInstruction> RewritePose(List<CodeInstruction> source)
        => ((IEnumerable<CodeInstruction>)AirHook("ArenaAirFireWeaponPosePatch","Transpiler",source)).ToList();
    static void PoseTranspiler()
    {
        var curve=AccessTools.Method(typeof(Player),nameof(Player.GetCurveValue));
        var authority=AccessTools.Field(typeof(PlayerAnimator),nameof(PlayerAnimator.WEAPON_ROOT_3RD));
        var other=AccessTools.Field(typeof(PlayerAnimator),nameof(PlayerAnimator.OTHER_CURVE));
        var source=new List<CodeInstruction>{new(OpCodes.Ldarg_0),new(OpCodes.Ldsfld,authority),new(OpCodes.Call,curve),new(OpCodes.Ret)};
        var label=new DynamicMethod("poseLabel",typeof(void),Type.EmptyTypes).GetILGenerator().DefineLabel(); source[2].labels.Add(label);
        var output=RewritePose(source);
        Check(source[2].Calls(curve),"pose rewrite leaves native instructions intact");
        Check(output.Count==source.Count && Equals(output[1].operand,authority) && output[2].labels.SequenceEqual(source[2].labels)
            && ((MethodInfo)output[2].operand).Name=="HipFireWeaponAuthority","pose replacement preserves exact read stack and labels");
        var dm=new DynamicMethod("visualAuthority",typeof(float),new[]{typeof(Player)},typeof(Program),true);
        var il=dm.GetILGenerator(); il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldsfld,authority);il.Emit(output[2].opcode,(MethodInfo)output[2].operand);il.Emit(OpCodes.Ret);
        var actual=(Func<Player,float>)dm.CreateDelegate(typeof(Func<Player,float>));
        var f=new Fixture(); Check(actual(f.Player)==0f,"rewritten VisualPass read executes hip-fire pose gate");
        f.Player.MovementContext.IsGrounded=true;Check(actual(f.Player)==1f,"rewritten VisualPass read restores grounded pose");
        foreach(int count in new[]{0,2}) {
            var drift=new List<CodeInstruction>{new(OpCodes.Ldsfld,other),new(OpCodes.Call,curve)};
            for(int i=0;i<count;i++){drift.Add(new(OpCodes.Ldsfld,authority));drift.Add(new(OpCodes.Call,curve));}
            Check(RewritePose(drift).Count(x=>x.Calls(curve))==count+1,"pose rewrite rejects missing or ambiguous target " + count);
        }
        var mixed=source.Concat(new[]{new CodeInstruction(OpCodes.Ldsfld,other),new CodeInstruction(OpCodes.Call,curve)}).ToList();
        Check(RewritePose(mixed).Count(x=>x.Calls(curve))==1,"pose rewrite changes only weapon-authority call");
    }
    static void Main()
    {
        ArenaAirFire.Enable();
        SprintAdsPatches.Enable();
        if (SPT.Reflection.Patching.ModulePatch.Hooks.Count == 0)
            throw new Exception(string.Join("; ", ArenaMode.Plugin.ModLogger.Warnings));
        Check(SPT.Reflection.Patching.ModulePatch.Hooks.Count==7
            && SPT.Reflection.Patching.ModulePatch.Hooks.Count(x=>x.Name=="get_SuitableForHandInput")==0,"only reviewed firearm and visual hooks installed");
        Scope(); AirStates(); Pose(); Aim(); GroundAds(); Transpiler(); PoseTranspiler();
        Console.WriteLine($"ARENA_AIR_FIRE_PRODUCTION_PASSED ({checks} assertions)");
    }
}
