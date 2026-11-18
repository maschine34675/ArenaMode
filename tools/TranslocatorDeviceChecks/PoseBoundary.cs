using System.Reflection;

// Native animation boundary for linked Input+Pose integration. Fire changes the
// real donor's IDLE predicate to AIMING; operation and interactions are separate.
public interface IAnimator
{
    bool GetBool(int key); void SetBool(int key,bool value);
    AnimatorStateInfoWrapper GetCurrentAnimatorStateInfo(int layer);
    AnimatorStateInfoWrapper GetNextAnimatorStateInfo(int layer);
    bool IsInTransition(int layer);
}
public struct AnimatorStateInfoWrapper
{
    public int fullPathHash,shortNameHash;
    public string Name;
    public bool IsName(string name)=>Name==name;
}
public sealed class TestAnimator : IAnimator
{
    public readonly Dictionary<int,bool> Values=new() { [AnimationControllerParametersTable.BOOL_ACTIVE]=true };
    public string PhaseOverride,NextOverride;
    public bool Transition;
    public bool GetBool(int key)=>Values.TryGetValue(key,out var value)&&value;
    public void SetBool(int key,bool value)=>Values[key]=value;
    public AnimatorStateInfoWrapper GetCurrentAnimatorStateInfo(int layer)=>new(){Name=PhaseOverride??(GetBool(AnimationControllerParametersTable.BOOL_FIRE)?"Hands.AIMING":"Hands.IDLE")};
    public AnimatorStateInfoWrapper GetNextAnimatorStateInfo(int layer)=>NextOverride==null?GetCurrentAnimatorStateInfo(layer):new(){Name=NextOverride};
    public bool IsInTransition(int layer)=>Transition;
}
public static class AnimationControllerParametersTable
{
    public const int BOOL_FIRE=1,BOOL_ACTIVE=2,BOOL_LEFT=3,BOOL_INVENTORY=4;
    public static bool GetBoolFire(IAnimator animator)=>animator.GetBool(BOOL_FIRE);
    public static bool GetBoolActive(IAnimator animator)=>animator.GetBool(BOOL_ACTIVE);
    public static void SetFire(IAnimator animator,bool value)=>animator.SetBool(BOOL_FIRE,value);
}
public sealed class FirearmsAnimator
{
    public IAnimator Animator=new TestAnimator();
    public bool HasActive=true;
    public string FullIdleStateName=>"Hands.IDLE";
    public string FullFireStateName=>"Hands.FIRE";
    public bool HasParameter(int key)=>key!=AnimationControllerParametersTable.BOOL_ACTIVE||HasActive;
    public bool IsIdling()=>Animator.GetCurrentAnimatorStateInfo(1).IsName(FullIdleStateName);
    public bool IsInInventory=>Animator.GetBool(AnimationControllerParametersTable.BOOL_INVENTORY);
    public bool IsInInteraction=>Animator.GetBool(AnimationControllerParametersTable.BOOL_LEFT);
    public void SetFire(bool value){AnimationControllerParametersTable.SetFire(Animator,value);Animator.SetBool(AnimationControllerParametersTable.BOOL_LEFT,false);}
}
public sealed class EFTHardSettings
{
    public static EFTHardSettings Instance=new();
    public float STOP_AIMING_AT=.5f;
    public bool CanAimInState(string name)=>true;
}
namespace EFT
{
    public class PortableRangeFinderController : Player.UsableItemController
    {
        public class PortableRangeFinderIdling : Player.Operation { }
        public class DrawOperation : Player.Operation { }
        public WeaponPrefab Prefab;
        public float _overlap;
        public PortableRangeFinderController(){CurrentHandsOperation=new PortableRangeFinderIdling();}
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod { public HarmonyMethod(Type type,string name){} }
    public sealed class Harmony
    {
        public Harmony(string name){}
        public void Patch(MethodInfo method,HarmonyMethod prefix=null){}
        public void UnpatchSelf(){}
    }
}
