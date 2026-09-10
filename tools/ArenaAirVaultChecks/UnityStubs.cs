using EFT;
using UnityEngine;
using System.Reflection;

namespace UnityEngine
{
    public class RuntimeAnimatorController { }
    public struct AnimatorStateInfo { public int fullPathHash; public float normalizedTime; }
    public class StateMachineBehaviour
    {
        public virtual void OnStateEnter(Animator a,AnimatorStateInfo s,int l) { }
    }
    // Engine seam only. Real Unity graph/evaluation is intentionally not claimed.
    public class Animator
    {
        public MovementContext Context;
        public RuntimeAnimatorController runtimeAnimatorController=new();
        public bool isActiveAndEnabled=true, isInitialized=true;
        public float speed=1;
        public bool Landing, Falling;
        public int Current=232800732, Queued, CrossFades, Evaluations;
        public float Offset, Blend;
        public Action OnCrossFade;
        public PlayerStateContainer[] Containers;
        public HashSet<int> States=new() {232800732,1889265959,1056621489,1332272753,-285230529,262098243};
        public bool HasState(int layer,int hash)=>layer==0 && States.Contains(hash);
        public T[] GetBehaviours<T>()=>Containers.OfType<T>().ToArray();
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer)=>new() {fullPathHash=Current, normalizedTime=.35f};
        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer)=>new() {fullPathHash=Queued};
        public bool IsInTransition(int layer)=>Context.Transition;
        public bool GetBool(int hash)=>hash==PlayerAnimator.LANDING_PARAM_HASH?Landing:hash==PlayerAnimator.FALLING_DOWN_PARAM_HASH?Falling:Context.JumpSet;
        public void CrossFade(int target,float duration,int layer,float offset)
        { CrossFades++; Queued=target; Blend=duration; Offset=offset; OnCrossFade?.Invoke(); }
        public void Update(float dt)
        {
            Evaluations++;
            if(Queued!=0)
            {
                Current=Queued; Queued=0;
                foreach(var behavior in Containers.Where(x=>x.StateFullNameHash==Current).ToArray())
                    behavior.OnStateEnter(this,new() {fullPathHash=Current,normalizedTime=Offset},0);
            }
            Context.CurrentState.OnStateMove(Offset);
        }
    }
}
namespace AnimationSystem
{
    public class UnityAnimatorWrapper
    {
        public Animator Animator;
        public UnityAnimatorWrapper(Animator animator)=>Animator=animator;
        public void Update(float dt)=>Animator.Update(dt);
    }
}
public partial class PlayerStateContainer : StateMachineBehaviour
{
    public BaseMovementState EncapsulatedState;
    public int StateFullNameHash,AdditionalDirectionInfo,Type;
    public string StateFullName;
    public float RotationSpeedClamp, StateSensitivity;
    public object SmoothLerpAnimCurve,FirstPersonPose;
    public bool DisableRootMotion,CanInteract;
    public override void OnStateEnter(Animator animator,AnimatorStateInfo stateInfo,int layerIndex)
    {
        Exception error=null;
        try { NativeOnStateEnter(animator,stateInfo,layerIndex); }
        catch(Exception ex) {error=ex;}
        error=Harness.UnityEntry(error,this,animator,stateInfo,layerIndex);
        if(error!=null) throw error;
    }
}
namespace EFT
{
    public enum EUpdateMode { Auto, Manual }
    public static class AppEnvironment { public static Options Config=new(); public class Options {public bool UseBodyFastAnimator;} }
    public partial class Player
    {
        public EUpdateMode BodyUpdateMode;
        public bool IsVisible;
        public Health HealthController=new();
        public class Health {public bool IsAlive=true;}
        public Bones PlayerBones=new(); public class Bones { public Playable PlayableAnimator=new(); }
        public class Playable { public void Process(bool visible,float dt) {} }
        public AnimationSystem.UnityAnimatorWrapper BodyAnimatorCommon=>(AnimationSystem.UnityAnimatorWrapper)MovementContext.PlayerAnimator.Animator;
        public virtual void BodyUpdate(float deltaTime,int loop=1) { Harness.UnityBody(this); NativeBodyUpdate(deltaTime,loop); }
    }
}
internal static partial class Harness
{
    public static void UnityBody(Player player)=>Method("UnityBodyPatch","Prefix").Invoke(null,new object[]{player});
    public static Exception UnityEntry(Exception error,PlayerStateContainer c,Animator a,AnimatorStateInfo s,int layer)
        =>(Exception)Method("UnityEntryPatch","Finalizer").Invoke(null,new object[]{error,c,a,s,layer});
}
internal static class UnityFixture
{
    public static Animator Create(MovementContext context)
    {
        var a=new Animator {Context=context};
        a.Containers=new[] {
            new PlayerStateContainer {StateFullNameHash=232800732,StateFullName="Base Layer.JUMP.Jump_Idle",EncapsulatedState=context.CurrentState},
            new PlayerStateContainer {StateFullNameHash=-285230529,StateFullName="Base Layer.Vaulting.Climb_on",EncapsulatedState=new ClimbState {MovementContext=context}},
            new PlayerStateContainer {StateFullNameHash=262098243,StateFullName="Base Layer.Vaulting.Vault",EncapsulatedState=new VaultState {MovementContext=context}},
            new PlayerStateContainer {StateFullNameHash=1056621489,StateFullName="Base Layer.JUMP.Jump_Move",EncapsulatedState=context.CurrentState},
            new PlayerStateContainer {StateFullNameHash=1332272753,StateFullName="Base Layer.JUMP.Jump_Sprint",EncapsulatedState=context.CurrentState}
        };
        context.PlayerAnimator.Animator=new AnimationSystem.UnityAnimatorWrapper(a);
        return a;
    }
}
