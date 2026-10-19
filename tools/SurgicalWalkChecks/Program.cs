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
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

static class Program {
    static int checks;
    static void Check(bool value,string reason) { checks++; if(!value) throw new Exception("ASSERT: "+reason); }
    sealed class Fixture {
        public Player Player=new(); public LocalGame Game; public GameWorld World;
        public MovementContext Context=>Player.MovementContext;
        public Player.MedsController Controller=new();
        public Fixture(bool splint=false) {
            ArenaSession.IsActive=Settings.MovementEnabled.Value=true;
            ArenaRespawnDriver.StandDown=ArenaRespawnDriver.IsRespawnInProgress=ArenaSpectatorService.IsActive=ArenaInputBlocker.Blocked=false;
            Game=new(){LocalPlayer=Player}; World=new(){MainPlayer=Player};
            ArenaSession.Game=Singleton<AbstractGame>.Instance=Game; ArenaSession.World=Singleton<GameWorld>.Instance=World;
            Player.HandsController=Controller;
            Player.HealthController.Effect=new(){MedItem=Controller.Item,HealthController=Player.HealthController};
            if (splint) {
                Controller.Item.HealthEffectsComponent.Surgical=false;
                Controller.Item.HealthEffectsComponent.Fracture=true;
                Player.HealthController.Effect.BodyPart=EBodyPart.LeftLeg;
            }
        }
        public bool Allows()=>ArenaSurgicalWalk.CanWalk(Context);
        public Vector3 Motion() { var v=new Vector3(3,7,4); ArenaSurgicalWalk.LimitMotion(Context,ref v,0.1f); return v; }
    }
    static void Main() {
        ArenaSurgicalWalk.Enable();
        Check(SPT.Reflection.Patching.ModulePatch.Hooks.Count==2,"two native walking sites only");
        var positive=new Fixture();
        Check(positive.Allows(),"surgical idle allows walking");
        positive.Context.CurrentState=new MovePlayerState(); Check(positive.Allows(),"surgical move remains allowed");
        positive.Player.HealthController.Effect.State=EEffectState.Added; Check(positive.Allows(),"initial surgery delay allows walking");
        Check(positive.Context.Flags==(EPhysicalCondition.HealingLegs|EPhysicalCondition.UsingMeds),"native flags preserved");
        foreach (var leg in new[]{EBodyPart.LeftLeg,EBodyPart.RightLeg}) {
            var splint=new Fixture(true); splint.Player.HealthController.Effect.BodyPart=leg;
            Check(splint.Allows(),"leg splint idle allows walking");
            splint.Context.CurrentState=new MovePlayerState(); Check(splint.Allows(),"leg splint move allows walking");
            var v=splint.Motion(); Check(Math.Abs(v.x-0.06f)<1e-6 && Math.Abs(v.z-0.08f)<1e-6 && v.y==7,"leg splint capped at 1 m/s with gravity intact");
        }
        foreach(var part in new[]{EBodyPart.Common,EBodyPart.Head,EBodyPart.Chest,EBodyPart.Stomach,EBodyPart.LeftArm,EBodyPart.RightArm}) {
            var splint=new Fixture(true); splint.Player.HealthController.Effect.BodyPart=part;
            Check(!splint.Allows(),"reject non-leg splint exception");
            splint.Context.Flags=EPhysicalCondition.UsingMeds;
            Check(splint.Allows(),"native non-leg medical walking preserved");
            var v=splint.Motion(); Check(v.x==3 && v.z==4,"native non-leg treatment has no added cap");
        }
        foreach(bool splint in new[]{false,true}) {
        foreach(var test in new (string,Action<Fixture>)[] {
            ("ordinary raid",f=>ArenaSession.IsActive=false), ("master off",f=>Settings.MovementEnabled.Value=false),
            ("bot",f=>f.Player.IsAI=true), ("remote",f=>f.Player.IsYourPlayer=false), ("dead",f=>f.Player.HealthController.IsAlive=false),
            ("foreign world",f=>Singleton<GameWorld>.Instance=new(){MainPlayer=f.Player}),
            ("foreign body",f=>f.World.MainPlayer=new()), ("owner handover",f=>f.Game.LocalPlayer=new()),
            ("stopping",f=>f.Game.Status=GameStatus.Stopping), ("respawning",f=>ArenaRespawnDriver.IsRespawnInProgress=true),
            ("stand down",f=>ArenaRespawnDriver.StandDown=true), ("spectating",f=>ArenaSpectatorService.IsActive=true),
            ("hands changed",f=>f.Player.HandsController=new Player.MedsController()),
            ("not surgical or splint",f=>{f.Controller.Item.HealthEffectsComponent.Surgical=false;f.Controller.Item.HealthEffectsComponent.Fracture=false;}),
            ("wrong effect owner",f=>f.Player.HealthController.Effect.HealthController=new()),
            ("wrong effect item",f=>f.Player.HealthController.Effect.MedItem=new()),
            ("no effect",f=>f.Player.HealthController.Effect=null),
            ("finished",f=>f.Player.HealthController.Effect.State=EEffectState.Residued),
            ("cancelled",f=>f.Player.HealthController.Effect.State=EEffectState.Removed),
            ("using flag gone",f=>f.Context.Flags=EPhysicalCondition.HealingLegs)
        }) {
            var f=new Fixture(splint); test.Item2(f); Check(!f.Allows(),"reject "+test.Item1);
            var v=f.Motion(); Check(v.x==3 && v.y==7 && v.z==4,"no retained cap: "+test.Item1);
            f.Context.Flags=0; Check(f.Allows(),"native true preserved: "+test.Item1);
        }
        foreach(var test in new (string,Action<Fixture>)[] {
            ("inventory",f=>f.Player.IsInventoryOpened=true), ("blocked input",f=>ArenaInputBlocker.Blocked=true),
            ("obstacle",f=>f.Context.ObstacleCollisionFacade.Allowed=false), ("prone",f=>f.Context.IsInPronePose=true),
            ("mounted",f=>f.Context.IsInMountedState=true), ("vaulting",f=>f.Context.Vaulting=true),
            ("state override",f=>f.Context.OverridenState=new()), ("controls override",f=>f.Context.OverridenControlsState=new()),
            ("other state",f=>f.Context.CurrentState=new MovementState()), ("derived move",f=>f.Context.CurrentState=new DerivedMove())
        }) { var f=new Fixture(splint); test.Item2(f); Check(!f.Allows(),"reject "+test.Item1); }
        }
        var cap=new Fixture(); var limited=cap.Motion();
        Check(Math.Abs(limited.x-0.06f)<1e-6 && Math.Abs(limited.z-0.08f)<1e-6 && limited.y==7,"cap actual planar motion and preserve gravity");
        foreach(float dt in new[]{0f,-1f,float.NaN,float.PositiveInfinity}) {
            var motion=new Vector3(3,9,4); ArenaSurgicalWalk.ClampHorizontal(ref motion,dt);
            Check(motion.x==0 && motion.z==0 && motion.y==9,"invalid timestep cannot add horizontal movement");
        }
        var slow=new Vector3(0.01f,-2,0.02f); ArenaSurgicalWalk.ClampHorizontal(ref slow,0.1f);
        Check(slow.x==0.01f && slow.y==-2 && slow.z==0.02f,"already slow movement untouched");
        Transpiler();
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        string source=File.ReadAllText(Path.Combine(root,"ArenaMode.Client/Movement/ArenaMovementPatches.cs"));
        int slide=source.IndexOf("motion += slide.SlideVelocity * deltaTime;");
        int limit=source.IndexOf("ArenaSurgicalWalk.LimitMotion(__instance, ref motion, deltaTime);");
        int early=source.IndexOf("if (!touched)");
        Check(slide>=0 && limit>slide && early>limit,"cap integrated after slide and before unity-scale early return");
        Check(File.ReadAllText(Path.Combine(root,"ArenaMode.Client/Plugin.cs")).Contains("Movement.ArenaSurgicalWalk.Enable();"),"plugin registers surgical walking");
        Console.WriteLine($"SURGICAL_WALK_CHECKS_PASSED ({checks} assertions)");
    }
    static void Transpiler() {
        var method=typeof(ArenaSurgicalWalk).GetMethod("Rewrite",BindingFlags.Static|BindingFlags.NonPublic);
        var getter=AccessTools.PropertyGetter(typeof(MovementContext),"CanWalk");
        foreach(int count in new[]{0,1,2}) {
            var input=new List<CodeInstruction>{new(OpCodes.Ldarg_0)};
            input.AddRange(Enumerable.Range(0,count).Select(_=>new CodeInstruction(OpCodes.Callvirt,getter))); input.Add(new(OpCodes.Ret));
            if(count==1) input[1].labels.Add(new DynamicMethod("label",typeof(void),Type.EmptyTypes).GetILGenerator().DefineLabel());
            object[] args={input,false}; var output=((IEnumerable<CodeInstruction>)method.Invoke(null,args)).ToArray();
            Check((bool)args[1]==(count==1),"only one native call binds "+count);
            if(count!=1) { Check(output.Count(x=>x.Calls(getter))==count,"drift kept native "+count); continue; }
            Check(input[1].Calls(getter) && output[1].labels.SequenceEqual(input[1].labels),"rewrite keeps input and branch labels");
            var dynamic=new DynamicMethod("walk",typeof(bool),new[]{typeof(MovementContext)},typeof(Program),true);
            var il=dynamic.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(output[1].opcode,(MethodInfo)output[1].operand); il.Emit(OpCodes.Ret);
            var actual=(Func<MovementContext,bool>)dynamic.CreateDelegate(typeof(Func<MovementContext,bool>)); var f=new Fixture();
            Check(actual(f.Context),"rewritten native call executes production permission");
            f.Context.ObstacleCollisionFacade.Allowed=false; Check(!actual(f.Context),"rewritten native call preserves collision veto");
        }
    }
}
