using ArenaMode.Areas;
using ArenaMode.Combat.Translocator;
using ArenaMode.Movement;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using UnityEngine;

static class Program
{
    private static int _checks;
    private static Vector3 Target = new(5,3,0);
    private static ArenaAreaDefinition Area(float half=20,float maxY=20)=>new("test","test","factory4_day",new ArenaAreaBounds(-half,-20,-half,half,maxY,half),0,1,2);
    private static void Check(bool condition,string text){_checks++;if(!condition)throw new Exception("FAIL: "+text);}
    private static Player New(MovementState state=null,bool spent=true)
    {
        Physics.Reset();ArenaAreaNavigationPatches.IsReady=true;ArenaAreaRuntime.IsReady=true;
        ArenaAreaRuntime.CurrentArea=Area();ArenaAreaRuntime.OrdinaryTeleportAllowed=false;
        Target=new Vector3(5,3,0);
        var p=new Player();p.transform.position=new(0,1,0);state??=new IdlePlayerState();
        p.CurrentManagedState=state;p.MovementContext.CurrentState=state;
        state.MovementContext=p.MovementContext;p.MovementContext.Player=p;
        p.MovementContext.FreefallTime=.7f;p.MovementContext._lastPlayerHeight=.8f;p.MovementContext._hasLastHeight=true;
        p.MovementContext.IsGrounded=state is not JumpPlayerState;
        state.Destination=new Vector3(.2f,1.3f,.4f);
        if(state is JumpPlayerState j){j._potentialH=3;j._lastPlayerHeight=.75f;j._hasLastHeight=true;j._stateTime=.8f;j._currentMagnitude=6;j._currentLiftforce=new(0,-2,0);}
        var capsule=new CapsuleCollider{transform=p.transform};
        p.CharacterController=new SimpleCharacterController{_mainCollider=capsule,_colliders=new Collider[]{capsule},velocity=new(4,-2,0),isGrounded=p.MovementContext.IsGrounded};
        p.PlayerBones.BodyPartCollidersHashSet.Add(capsule);
        var world=Singleton<GameWorld>.Instance=new GameWorld{MainPlayer=p};world.AllAlivePlayersList.Add(p);
        var game=Singleton<AbstractGame>.Instance=new AbstractGame();
        ArenaTranslocatorRuntime.Owner=p;ArenaTranslocatorRuntime.World=world;ArenaTranslocatorRuntime.Game=game;
        ArenaTranslocatorRuntime.Destination=Target;ArenaTranslocatorRuntime.Enabled=ArenaTranslocatorRuntime.Owns=true;
        ArenaJumpReliability.Seed(p);ArenaBunnyhop.Seed(p);ArenaDoubleJump.Seed(p,spent);ArenaSlideTransitions.Seed(p);ArenaAirVault.Seed(p);ArenaAirControl.Seed(p);
        return p;
    }
    private static bool Teleport(Player p,out string reason)=>ArenaTranslocatorTeleport.TryTeleport(p,Target,out reason);
    private static BoxCollider Box(Vector3 center)=>new(){transform=new Transform{position=center},half=new(.4f,.4f,.4f)};
    private static void Reject(Player p,string name){var before=p.Position;Check(!Teleport(p,out _),name);Check(p.Position==before,name+" keeps body in place");}
    private static void Movement()
    {
        foreach(var factory in new Func<MovementState>[] {()=>new IdlePlayerState(),()=>new MovePlayerState(),()=>new SprintPlayerState(),()=>new JumpPlayerState(),()=>new FallDown()})
        foreach(float targetY in new[]{3f,-6f})
        foreach(bool spent in new[]{false,true})
        {
            var state=factory();var p=New(state,spent);Target.y=targetY;ArenaTranslocatorRuntime.Destination=Target;
            bool grounded=p.MovementContext.IsGrounded;
            var applied=state is JumpPlayerState?ArenaAirControl.Applied(p):Vector3.zero;
            var oldWitness=ArenaBunnyhop.Witness;
            Check(Teleport(p,out var reason),"supported ground air and roof relocation succeeds without bot navigation: "+reason);
            Check(p.Position==Target&&p.Teleports==1,"one exact native teleport reaches destination");
            Check(p.MovementContext.FreefallTime==.7f&&((SimpleCharacterController)p.CharacterController).velocity==new Vector3(4,-2,0)
                &&!p.MovementContext.IsGrounded&&!((SimpleCharacterController)p.CharacterController).isGrounded,"native momentum freefall and new unsupported contact are preserved");
            Check(ArenaTranslocatorTeleportPolicy.Arrived(state.Destination.Value,Target+new Vector3(.2f,.3f,.4f)),"native destination history rebased");
            Check(Math.Abs(p.MovementContext._lastPlayerHeight-(Target.y-.2f))<.0001f,"context height history rebased");
            if(state is JumpPlayerState j)
            {
                Check(j._potentialH==Target.y+2&&j._lastPlayerHeight==Target.y-.25f,"jump and fall potential heights rebased without false lift");
                Check(j._stateTime==.8f&&j._currentMagnitude==6&&j._currentLiftforce==new Vector3(0,-2,0),"jump phase and instantaneous motion preserved");
                Check((ArenaAirControl.Applied(p)-applied).sqrMagnitude<.00000001f,"applied airborne carry and steered heading preserved");
                Check(ArenaBunnyhop.LaunchY==Target.y&&!ReferenceEquals(oldWitness,ArenaBunnyhop.Witness),"bunnyhop launch height rebased and old motion witness retired");
            }
            Check(!ArenaJumpReliability.Pending&&!ArenaBunnyhop.Pending&&!ArenaSlideTransitions.Pending&&!ArenaAirVault.Observing,"old positional contact slide and mantle observations cancelled");
            Check(!ArenaDoubleJump.Pending&&ArenaDoubleJump.Spent==spent&&ArenaDoubleJump.Airborne,"jump requests cancelled without granting credit");
            Check(p.Slide.enabled,"slide component remains available after relocation");
            Check(!ArenaAirControl.Pending(p),"old sampled air input retired without resetting heading");
            p.Teleport(new Vector3(9,2,0));Check(p.Position==Target,"one call authorization never survives native return");
        }
    }
    private static void Revalidation()
    {
        foreach(bool wholeMap in new[]{false,true})
        foreach(string mutation in new[]{"world","game","rebound-world","rebound-game","context","controller","area","disc","destination","dead","native-target"})
        {
            var p=New();if(wholeMap)ArenaAreaRuntime.CurrentArea=null;
            p.BeforePrefix=()=>{
                switch(mutation)
                {
                    case "world":Singleton<GameWorld>.Instance=new GameWorld{MainPlayer=p};break;
                    case "game":Singleton<AbstractGame>.Instance=new AbstractGame();break;
                    case "rebound-world":ArenaTranslocatorRuntime.World=Singleton<GameWorld>.Instance=new GameWorld{MainPlayer=p};break;
                    case "rebound-game":ArenaTranslocatorRuntime.Game=Singleton<AbstractGame>.Instance=new AbstractGame();break;
                    case "context":p.MovementContext=new MovementContext{CurrentState=p.CurrentManagedState};break;
                    case "controller":p.CharacterController=new UnknownController();break;
                    case "area":ArenaAreaRuntime.CurrentArea=Area();break;
                    case "disc":ArenaTranslocatorRuntime.Owns=false;break;
                    case "destination":ArenaTranslocatorRuntime.Destination+=Vector3.right;break;
                    case "dead":p.HealthController.IsAlive=false;break;
                    case "native-target":ArenaTranslocatorRuntime.Destination=Target+Vector3.right;break;
                }
            };
            if(mutation=="native-target")p.NativeTarget=Target+Vector3.right;
            Reject(p,"changed world body controller area disc and target deny the owned native call including whole maps");
            Check(p.Teleports==0,"changed authorization cannot fall through to ordinary teleport");
        }
        {
            var p=New();using var ticket=ArenaAreaTranslocatorGate.Begin(p,Target);
            Check(ticket!=null,"valid ticket can be acquired");
            p.Teleport(Target);p.Teleport(Target);Check(p.Teleports==1,"same target cannot reuse consumed authorization");
            var second=Target+Vector3.right;ArenaTranslocatorRuntime.Destination=second;ArenaAreaRuntime.OrdinaryTeleportAllowed=true;
            p.Teleport(second);Check(p.Position==Target&&p.Teleports==1,"consumed ticket rejects nested same body teleport even with ordinary allowance");
            var other=new Player();other.Teleport(second);Check(other.Position==second,"unrelated native body does not borrow or lose ordinary admission");
        }
        {
            var p=New();ArenaAreaRuntime.CurrentArea=null;Check(Teleport(p,out _),"whole map uses the same exact consumed authorization");
        }
    }
    private static void Geometry()
    {
        var p=New();Physics.Obstacles.Add(Box(Target+Vector3.up));Reject(p,"solid wall blocks actual body clearance");
        p=New();Physics.OnSync=()=>Physics.Obstacles.Add(Box(Target+Vector3.up));Reject(p,"moved door synchronized before collision proof");
        p=New();Physics.Saturated=true;Reject(p,"saturated overlap query is not clearance");
        p=New();Physics.ThrowQuery=true;Reject(p,"native query exception fails closed");
        p=New();Physics.Obstacles.Add(((SimpleCharacterController)p.CharacterController)._mainCollider);Check(Teleport(p,out _),"own physical collider is excluded");
        p=New();var extra=Box(p.Position+Vector3.up*2.5f);extra.transform=p.transform;extra.localCenter=Vector3.up*2.5f;extra.half=new(.2f,.2f,.2f);
        ((SimpleCharacterController)p.CharacterController)._colliders=new Collider[]{((SimpleCharacterController)p.CharacterController)._mainCollider,extra};
        Physics.Obstacles.Add(Box(Target+Vector3.up*2.5f));Reject(p,"additional controller shape must clear the ceiling");
        p=New();var actor=new Player();var body=Box(Target+Vector3.up);body.isTrigger=true;actor.PlayerBones.BodyPartCollidersHashSet.Add(body);Singleton<GameWorld>.Instance.AllAlivePlayersList.Add(actor);
        Reject(p,"living trigger hurt volume blocks occupancy without telefrag");Check(actor.HealthController.IsAlive,"occupied actor is never killed");
        actor.HealthController.IsAlive=false;Check(Teleport(p,out _),"retired actor does not claim living occupancy");
        p=New();actor=new Player();body=Box(Target+Vector3.up*2.5f);body.isTrigger=true;actor.PlayerBones.BodyPartCollidersHashSet.Add(body);Singleton<GameWorld>.Instance.AllAlivePlayersList.Add(actor);
        extra=Box(p.Position+Vector3.up*2.5f);extra.transform=p.transform;extra.localCenter=Vector3.up*2.5f;extra.half=new(.2f,.2f,.2f);
        ((SimpleCharacterController)p.CharacterController)._colliders=new Collider[]{((SimpleCharacterController)p.CharacterController)._mainCollider,extra};
        Reject(p,"additional controller shape also respects actor occupancy");
        p=New();ArenaAreaRuntime.CurrentArea=Area(Target.x+ArenaAreaCatalog.NavigationInset+.15f);Reject(p,"full capsule radius respects area inset");
        p=New();ArenaAreaRuntime.CurrentArea=Area(maxY:Target.y+1.7f);Reject(p,"full capsule head respects area ceiling");
        var concave=new ArenaAreaBounds(new[]{new ArenaAreaVertex(0,0),new ArenaAreaVertex(8,0),new ArenaAreaVertex(8,8),new ArenaAreaVertex(5,8),new ArenaAreaVertex(5,3),new ArenaAreaVertex(3,3),new ArenaAreaVertex(3,8),new ArenaAreaVertex(0,8)},-20,20);
        ArenaAreaRuntime.CurrentArea=new("concave","concave","factory4_day",concave,0,1,2);
        Check(!ArenaAreaTranslocatorGate.ContainsCapsule(new(2,3,5),new(6,3,5),.2f),"rotated capsule cannot bridge concave area gap");
        p=New();ArenaAreaRuntime.IsReady=false;Reject(p,"area readiness is mandatory");
        p=New();ArenaAreaNavigationPatches.IsReady=false;Reject(p,"confirmed native guard is mandatory");
        p=New();bool nestedAccepted=true;Physics.OnQuery=()=>nestedAccepted=ArenaTranslocatorTeleport.TryValidateDestination(p,Target,out _);
        Check(Teleport(p,out _)&&!nestedAccepted,"reentrant physics validation cannot borrow scratch buffers");
        p=New();Physics.OnQuery=()=>p.CharacterController=new UnknownController();Reject(p,"shape owner changed during geometry rejects admission");
    }
    private static void RefusalAndCleanup()
    {
        foreach(string fault in new[]{"refuse","skip-prefix","throw-before","throw-after","wrong-target","lost-disc","lost-world"})
        {
            var state=new JumpPlayerState();var p=New(state);float oldPotential=state._potentialH;
            switch(fault){case "refuse":p.Refuse=true;break;case "skip-prefix":p.SkipPrefix=true;break;case "throw-before":p.ThrowBefore=true;break;case "throw-after":p.ThrowAfter=true;break;
                case "wrong-target":p.AfterTeleport=()=>p.transform.position+=Vector3.right;break;case "lost-disc":p.AfterTeleport=()=>ArenaTranslocatorRuntime.Owns=false;break;
                case "lost-world":p.AfterTeleport=()=>Singleton<GameWorld>.Instance=new GameWorld();break;}
            Check(!Teleport(p,out _),"native refusal exception target ownership and readback control success");
            if(fault is "throw-after" or "skip-prefix" or "wrong-target")Check(state._potentialH==oldPotential+p.Position.y-1,"moved but unconfirmed native call still rebases owned height safely");
            if(fault=="lost-world")Check(state._potentialH==oldPotential,"detached body never receives post callback writes");
            var fresh=New();Check(Teleport(fresh,out _),"exception and refusal always release native authorization and execution locks");
        }
        var player=New();player.BeforePrefix=()=>Check(!Teleport(player,out _),"nested feature execution rejected");Check(Teleport(player,out _),"outer feature survives rejected nested execution");
        player=New();player.AfterTeleport=()=>player.transform.position+=Vector3.right*.03f;Check(!Teleport(player,out _),"arrival readback tolerance is bounded");
        player=New();player.AfterTeleport=()=>player.transform.position+=Vector3.right*.02f;Check(Teleport(player,out _),"tiny native contact correction accepted");
    }
    private static void Admission()
    {
        foreach(string change in new[]{"unowned","disabled","foreign","nonfinite","prone","slide","vault","state","platform","async","override"})
        {
            var p=New();switch(change){case "unowned":ArenaTranslocatorRuntime.Owns=false;break;case "disabled":ArenaTranslocatorRuntime.Enabled=false;break;
                case "foreign":ArenaTranslocatorRuntime.Owner=new Player();break;case "nonfinite":Target.x=float.NaN;ArenaTranslocatorRuntime.Destination=Target;break;
                case "prone":p.MovementContext.IsInPronePose=true;break;case "slide":p.Slide.IsSliding=true;break;case "vault":ArenaAirVault.Seed(p,true);break;
                case "state":p.CurrentManagedState=p.MovementContext.CurrentState=new UnknownState();break;case "platform":p.MovementContext.Platform=new object();break;
                case "async":p.MovementContext.ImpostorCC=new(){UseAsyncMoveValidation=true};break;case "override":p.MovementContext.OverridenState=new object();break;}
            Reject(p,"unsupported scope state and fabricated destination rejected");
        }
        var owner=New();var other=new Player();ArenaJumpReliability.OnRelocated(other);ArenaBunnyhop.OnRelocated(other,Vector3.one);ArenaDoubleJump.OnRelocated(other);ArenaSlideTransitions.OnRelocated(other);ArenaAirVault.OnRelocated(other);ArenaAirControl.OnRelocated(other);
        Check(ArenaJumpReliability.Pending&&ArenaBunnyhop.Pending&&ArenaDoubleJump.Pending&&ArenaSlideTransitions.Pending&&ArenaAirVault.Observing,"cleanup cannot cancel another physical body");
        Check(!ArenaTranslocatorTeleportPolicy.Arrived(new(float.PositiveInfinity,0,0),Target),"nonfinite native arrival is rejected");
    }
    private static void ContactRefresh()
    {
        var p=New();var context=p.MovementContext;context.FreefallTime=0;
        context.UpdateGroundCollision(0);
        var stalled=Vector3.zero;context.ApplyGravity(ref stalled,.02f,true);
        Check(context.IsGrounded&&stalled==Vector3.zero&&context.FreefallTime==0,"native positive control reproduces stale SimpleCC idle float");
        context.ContactUpdates=0;context.SupportedAt=position=>position.y<2;
        Check(Teleport(p,out _),"ground to air relocation succeeds with native contact refresh");
        Check(context.ContactUpdates==1&&!context.IsGrounded&&!p.CharacterController.isGrounded&&context._groundHit.collider==null,"fresh native ground query invalidates old contact before return");
        Check(context.LastProbeOrigin==Target+((SimpleCharacterController)p.CharacterController)._mainCollider.center,"ground query runs at the relocated capsule");
        Check(context.FreefallTime==0,"contact refresh does not invent falling time");
        p.CurrentManagedState.Jump();Check(context.JumpRequests==0,"same input batch cannot jump from the old grounded witness");
        float began=p.Position.y;
        for(int i=0;i<50;i++){var motion=Vector3.zero;context.ApplyGravity(ref motion,.02f,true);p.transform.position+=motion;}
        Check(p.Position.y<began-1&&context.FreefallTime>.9f,"native idle gravity falls with no additional input");

        p=New();context=p.MovementContext;context.FreefallTime=0;context.SupportedAt=position=>position==Target;
        Check(Teleport(p,out _)&&context.IsGrounded&&context._groundHit.collider!=null,"fresh real support is retained by the native ground query");
        Check(!p.CharacterController.isGrounded,"proximity support is not fabricated physical controller contact");
        p.CurrentManagedState.Jump();Check(context.JumpRequests==1,"real destination support still allows a native jump");
        var still=Vector3.zero;context.ApplyGravity(ref still,.02f,true);Check(still==Vector3.zero&&context.FreefallTime==0,"real support stays stable without an injected impulse");

        foreach(string fault in new[]{"world","body-context","controller","position","state","throw"})
        {
            p=New(new JumpPlayerState());context=p.MovementContext;var captured=p;
            context.AfterContact=()=>{switch(fault){case "world":Singleton<GameWorld>.Instance=new GameWorld();break;case "body-context":captured.MovementContext=new MovementContext();break;case "controller":captured.CharacterController=new UnknownController();break;case "position":captured.transform.position+=Vector3.right;break;case "state":captured.CurrentManagedState=context.CurrentState=new FallDown{MovementContext=context};break;case "throw":throw new Exception("injected contact event failure");}};
            bool result=Teleport(p,out _);
            Check(result==(fault=="state"),"native contact callbacks keep identity and position confirmation exact");
            var fresh=New();Check(Teleport(fresh,out _),"contact failure releases all ownership and execution locks");
        }
        p=New();context=p.MovementContext;context.GroundQueryThrows=true;
        Check(!Teleport(p,out _)&&!p.CharacterController.isGrounded&&!context.IsGrounded,"failed native ground query cannot retain old physical ground contact");
        p.CurrentManagedState.Jump();Check(context.JumpRequests==0,"failed contact query also refuses same batch stale ground jump");
        var recovery=New();Check(Teleport(recovery,out _),"ground query failure does not retain authorization");
        p=New();context=p.MovementContext;p.AfterTeleport=()=>Physics.OnSync=()=>throw new Exception("injected sync failure");
        Check(!Teleport(p,out _)&&!p.CharacterController.isGrounded&&!context.IsGrounded,"failed transform sync cannot retain old physical ground contact");

        foreach(string fault in new[]{"world","context","controller"})
        {
            p=New();context=p.MovementContext;var captured=p;
            p.AfterTeleport=()=>Physics.OnSync=()=>{switch(fault){case "world":Singleton<GameWorld>.Instance=new GameWorld();break;case "context":captured.MovementContext=new MovementContext();break;case "controller":captured.CharacterController=new UnknownController();break;}};
            Check(!Teleport(p,out _)&&context.ContactUpdates==0,"changed owner during transform synchronization cannot refresh a stale context");
        }
        p=New();context=p.MovementContext;p.Refuse=true;
        Check(!Teleport(p,out _)&&context.ContactUpdates==0&&p.CharacterController.isGrounded,"refused native teleport leaves existing contact intact");
    }
    public static void Main(){Movement();Revalidation();Geometry();RefusalAndCleanup();Admission();ContactRefresh();Console.WriteLine($"TRANSLOCATOR_TELEPORT_CHECKS_PASSED assertions={_checks}; native physics and callbacks are doubled, no gameplay claim");}
}
