// Host doubles for graph data and Unity-facing services. ProcessState and the
// MovementState/MovementContext callback ordering are extracted unchanged by
// Verify.ps1. This cannot simulate collision geometry or visual clip blending.
using EFT;
using FastAnimatorSystem;
using AnimationEventSystem;
using System.Reflection;

namespace AnimationEventSystem
{
    public class FastAnimationPlayerStateContainerBehaviour { public BaseMovementState EncapsulatedState; }
}
namespace FastAnimatorSystem
{
    public enum ETransitionInterruptionSource { None, Source, Destination, SourceThenDestination, DestinationThenSource }
    public class TransitionCondition { }
    public class AbstractState
    {
        public string Name; public int Hash;
        public FastAnimationPlayerStateContainerBehaviour Behavior=new();
        public int BehaviorsCount=>Behavior==null?0:1;
        public T GetBehavior<T>() where T:class => Behavior as T;
    }
    public class AbstractAnimatorControllerState : AbstractState
    {
        public AbstractState ParentStateMachine;
        public float MotionDuration=1f; public bool Loop;
        public List<AnimatorControllerTransition> Edges=new();
        public AnimatorControllerTransition NativeResult,InterruptedResult;
        public Action BeforeEnter, AfterEnter;
        public IEnumerable<AnimatorControllerTransition> GetOutTransitions()=>Edges;
        public void MarkTransitionDirty() { }
        public AnimatorControllerTransition GetTriggeredTransition(float start,float end)
            => Harness.Transition(this,NativeResult);
        public AnimatorControllerTransition GetInterruptedTriggeredTransition(AbstractAnimatorControllerState current,
            AbstractAnimatorControllerState next,float currentTime,float nextTime,float dt,AnimatorControllerTransition transition)
            => InterruptedResult;
        public void OnStateEnter(FastAnimatorProcessor p,in AnimatorStateInfoWrapper info,int layer)
        { BeforeEnter?.Invoke(); Behavior?.EncapsulatedState?.OnStateEnter(info.normalizedTime); AfterEnter?.Invoke(); }
        public void OnStateMove(FastAnimatorProcessor p,in AnimatorStateInfoWrapper info,int layer)
            => Behavior?.EncapsulatedState?.OnStateMove(info.normalizedTime);
        public void OnStateExit(FastAnimatorProcessor p,in AnimatorStateInfoWrapper info,int layer)
            => Behavior?.EncapsulatedState?.OnStateExit(info.normalizedTime);
        public void OnStateUpdate(FastAnimatorProcessor p,in AnimatorStateInfoWrapper info,int layer) { }
    }
    public class AnimatorControllerTransition : AbstractState
    {
        public AbstractAnimatorControllerState SourceState,DestinationState;
        public bool IsMuted,IsSolo,IsFixedDuration,HasExitTime,IsOrderedInterruption;
        public float ExitTimeNormalized,Duration,NormalizedDestinationStateOffset;
        public TransitionCondition[] Conditions;
        public ETransitionInterruptionSource InterruptionSource;
        public Func<bool> ConditionCheck=()=>true;
        public AnimatorControllerTransition(string name,int hash,AbstractAnimatorControllerState sourceState,
            AbstractAnimatorControllerState destinationState,bool isMuted,bool isSolo,bool isFixedDuration,
            float exitTimeNormalized,float duration,bool hasExitTime,float normalizedDestinationStateOffset,
            TransitionCondition[] conditions,ETransitionInterruptionSource interruptionSource,bool isOrderedInterruption)
        { Name=name;Hash=hash;SourceState=sourceState;DestinationState=destinationState;IsMuted=isMuted;IsSolo=isSolo;
          IsFixedDuration=isFixedDuration;ExitTimeNormalized=exitTimeNormalized;Duration=duration;HasExitTime=hasExitTime;
          NormalizedDestinationStateOffset=normalizedDestinationStateOffset;Conditions=conditions;
          InterruptionSource=interruptionSource;IsOrderedInterruption=isOrderedInterruption; }
        public bool InternalCheckConditions()=>ConditionCheck();
        public void MarkDependentParameterDirty() { }
    }
    public class FastLayerInfo
    {
        public AbstractState ForcePlayState; public float ForcePlayStateNT;
        public AbstractAnimatorControllerState CurrentState;
        public AnimatorControllerTransition Transition;
        public float CurrentStateAbsoluteTime,TransitionAbsTime,DestinationStateAbsoluteTime;
        public AbstractAnimatorControllerState DestinationState=>Transition?.DestinationState;
        public float CurrentStateNormalizedTime=>CurrentStateAbsoluteTime/CurrentState.MotionDuration;
        public float DestinationStateNormalizedTime=>DestinationStateAbsoluteTime/DestinationState.MotionDuration;
        public float TransitionAbsDuration=>Transition.IsFixedDuration?Transition.Duration:Transition.Duration*Transition.SourceState.MotionDuration;
    }
    public class FastControllerInfo { public FastLayerInfo Layer=new(); public FastLayerInfo GetStateInfo(int layer)=>Layer; }
    public class FastAnimatorController { public List<AbstractState> AllStates=new(); }
    public struct AnimatorStateInfoWrapper { public float normalizedTime; }
    public partial class FastAnimatorProcessor
    {
        public FastControllerInfo FastControllerInfo=new();
        public FastAnimatorController FastAnimatorController=new();
        public bool bool_0=true;
        public Log Logger=new(); public Playable _playableAnimator;
        public class Log { public void LogTriggInfo(params object[] args) { } public void LogTriggeredTransition(object t) { } }
        public class Playable { public void RaiseImmediateTransitionHappened(int layer) { } }
        public void Play(int hash,int layer,float nt)=>throw new Exception("Unexpected force play");
        public void ResetTriggeredTriggers(AnimatorControllerTransition t) { }
        public AnimatorStateInfoWrapper CreateAnimatorStateInfoWrapper(AbstractAnimatorControllerState state,float nt)=>new(){normalizedTime=nt};
        public AnimatorControllerTransition GetTriggeredTransition(AbstractAnimatorControllerState s,float a,float b)=>s.GetTriggeredTransition(a,b);
        public void ProcessState(FastLayerInfo layerInfo,float deltaTime,int layerIndex,int recursionLevel=0)
        {
            object previous=Harness.Begin(this,layerInfo,layerIndex,recursionLevel);
            Exception error=null;
            try { NativeProcessState(layerInfo,deltaTime,layerIndex,recursionLevel); }
            catch(Exception ex) { error=ex; }
            error=Harness.End(error,previous);
            if(error!=null) throw error;
        }
    }
}
internal static partial class Harness
{
    public static object Begin(FastAnimatorProcessor p,FastLayerInfo layer,int index,int recursion)
    { object[] args={p,layer,index,recursion,null}; Method("AnimatorFramePatch","Prefix").Invoke(null,args); return args[4]; }
    public static Exception End(Exception error,object previous)
        =>(Exception)Method("AnimatorFramePatch","Finalizer").Invoke(null,new[]{error,previous});
    public static AnimatorControllerTransition Transition(AbstractAnimatorControllerState s,AnimatorControllerTransition result)
    { object[] args={s,result}; Method("AnimatorTransitionPatch","Postfix").Invoke(null,args); return (AnimatorControllerTransition)args[1]; }
}
internal static class AnimatorFixture
{
    public static FastAnimatorProcessor Create(MovementContext c)
    {
        var p=new FastAnimatorProcessor();
        var jump=new AbstractAnimatorControllerState {Name="Base Layer.JUMP.Jump_Idle",Hash=232800732,MotionDuration=1.1f,ParentStateMachine=new(){Name="Base Layer.JUMP",Hash=-1804865118}};
        jump.Behavior.EncapsulatedState=c.CurrentState;
        var move=new AbstractAnimatorControllerState {Name="Base Layer.StateMachine_Move.MOVE",Hash=886311417};
        var climb=new AbstractAnimatorControllerState {Name="Base Layer.Vaulting.Climb_on",Hash=-285230529};
        climb.Behavior.EncapsulatedState=new ClimbState {MovementContext=c};
        var vault=new AbstractAnimatorControllerState {Name="Base Layer.Vaulting.Vault",Hash=262098243};
        vault.Behavior.EncapsulatedState=new VaultState {MovementContext=c};
        foreach(var target in new[]{climb,vault})
        {
            bool isClimb=ReferenceEquals(target,climb);
            var edge=new AnimatorControllerTransition("native",1,move,target,false,false,true,0,
                isClimb?.15f:.1f,false,isClimb?0:.1f,Array.Empty<TransitionCondition>(),
                isClimb?ETransitionInterruptionSource.None:ETransitionInterruptionSource.Destination,true);
            edge.ConditionCheck=()=>!c.Vaulting && (isClimb?c.PlayerAnimator.Climb:c.PlayerAnimator.Vault);
            move.Edges.Add(edge);
        }
        p.FastControllerInfo.Layer.CurrentState=jump;
        p.FastAnimatorController.AllStates.AddRange(new[]{jump,move,climb,vault}); return p;
    }
}
