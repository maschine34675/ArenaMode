using System.Reflection;

// Test boundary only. The separate NativeApi project compiles the same production
// source against installed EFT/Unity, and NativeContracts validates the seam IL.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a,Object b) => ReferenceEquals(a,b) ||
            (ReferenceEquals(a,null) && !ReferenceEquals(b,null) && b.Destroyed) ||
            (ReferenceEquals(b,null) && !ReferenceEquals(a,null) && a.Destroyed);
        public static bool operator !=(Object a,Object b) => !(a==b);
        public override bool Equals(object obj)=>ReferenceEquals(this,obj);
        public override int GetHashCode()=>base.GetHashCode();
    }
    public class GameObject { public bool activeInHierarchy=true; }
}
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
    public readonly Dictionary<int,bool> Values=new();
    public string PhaseOverride, NextOverride;
    public bool Transition;
    public int Writes;
    public bool GetBool(int key)=>Values.TryGetValue(key,out var value)&&value;
    public void SetBool(int key,bool value) { Writes++; Values[key]=value; }
    public AnimatorStateInfoWrapper GetCurrentAnimatorStateInfo(int layer)=>new(){Name=PhaseOverride??(GetBool(AnimationControllerParametersTable.BOOL_FIRE)?"Hands.AIMING":"Hands.IDLE"),fullPathHash=GetBool(AnimationControllerParametersTable.BOOL_FIRE)?102:101,shortNameHash=GetBool(AnimationControllerParametersTable.BOOL_FIRE)?2:1};
    public AnimatorStateInfoWrapper GetNextAnimatorStateInfo(int layer)=>NextOverride==null?GetCurrentAnimatorStateInfo(layer):new(){Name=NextOverride};
    public bool IsInTransition(int layer)=>Transition;
}
public static class AnimationControllerParametersTable
{
    public const int BOOL_FIRE=1, BOOL_LEFT=2, BOOL_INVENTORY=3, BOOL_ACTIVE=4;
    public static bool GetBoolFire(IAnimator animator)=>animator.GetBool(BOOL_FIRE);
    public static void SetFire(IAnimator animator,bool value)=>animator.SetBool(BOOL_FIRE,value);
    public static bool GetBoolActive(IAnimator animator)=>animator.GetBool(BOOL_ACTIVE);
}
public class FirearmsAnimator
{
    public IAnimator Animator=new TestAnimator();
    public bool HasFire=true, HasActive=true, ThrowAfterSet;
    public string FullIdleStateName=>"Hands.IDLE";
    public string FullFireStateName=>"Hands.FIRE";
    public int NativeSets;
    public bool HasParameter(int key)=>key==AnimationControllerParametersTable.BOOL_FIRE?HasFire:key==AnimationControllerParametersTable.BOOL_ACTIVE&&HasActive;
    public bool IsIdling()=>Animator.GetCurrentAnimatorStateInfo(1).IsName(FullIdleStateName);
    public bool IsInInventory=>Animator.GetBool(AnimationControllerParametersTable.BOOL_INVENTORY);
    public bool IsInInteraction=>Animator.GetBool(AnimationControllerParametersTable.BOOL_LEFT);
    public void SetFire(bool value)
    {
        NativeSets++;
        AnimationControllerParametersTable.SetFire(Animator,value);
        Animator.SetBool(AnimationControllerParametersTable.BOOL_LEFT,false);
        if(ThrowAfterSet)throw new InvalidOperationException("native partial setter");
    }
}
public sealed class WeaponPrefab : EFT.AssetsManager.AssetPoolObject
{
    public FirearmsAnimator FirearmsAnimator;
    public bool IsStub;
    public UnityEngine.GameObject gameObject=new();
    public bool? FireBeforeNativeReset;
    public override void ReturnToPool()
    {
        HarmonyLib.Harmony.Prefix(typeof(WeaponPrefab),nameof(ReturnToPool),this);
        FireBeforeNativeReset=FirearmsAnimator?.Animator?.GetBool(AnimationControllerParametersTable.BOOL_FIRE);
        // Native override resets the animator and clears the wrapper before base.
        if(FirearmsAnimator?.Animator!=null)AnimationControllerParametersTable.SetFire(FirearmsAnimator.Animator,false);
        FirearmsAnimator=null;
        base.ReturnToPool();
    }
}
public sealed class EFTHardSettings
{
    public static EFTHardSettings Instance=new();
    public bool CanAim=true;
    public float STOP_AIMING_AT=.5f;
    public bool CanAimInState(string name)=>CanAim;
}
namespace Comfort.Common
{
    public static class Singleton<T> where T:class
    { public static T Instance; public static bool Instantiated=>Instance!=null; }
}
namespace EFT.InventoryLogic { public sealed class Item { } }
namespace EFT
{
    public sealed class GameWorld { public Player MainPlayer; }
    public sealed class InventoryController { public bool IsChangingWeapon, HandsAction; public bool HasAnyHandsAction()=>HandsAction; }
    public sealed class State { public string Name="Idle"; }
    public class Player : UnityEngine.Object
    {
        public object HandsController;
        public FirearmsAnimator HandsAnimator;
        public InventoryController InventoryController=new();
        public State CurrentState=new();
        public enum EOperationState { Ready, Executing, Finished }
        public class Operation { public EOperationState State=EOperationState.Executing; }
        public class UsableItemController
        {
            public Player _player;
            public static WeaponPrefab GetWeaponPrefab(PortableRangeFinderController controller)=>controller.Prefab;
        }
    }
    public class PortableRangeFinderController : Player.UsableItemController
    {
        public class PortableRangeFinderIdling : Player.Operation { }
        public class DrawOperation : Player.Operation { }
        public FirearmsAnimator FirearmsAnimator;
        public WeaponPrefab Prefab;
        public InventoryLogic.Item Item=new();
        public Player.Operation CurrentHandsOperation=new PortableRangeFinderIdling();
        public bool StrictInteraction, OpticEnabled, RangefinderEnabled, PwaAiming;
        private bool _aiming;
        public int AimWrites;
        public bool IsAiming {get=>_aiming;set{AimWrites++; _aiming=value; PwaAiming=value;}}
        public float _overlap;
        public bool CanInteract()=>FirearmsAnimator.IsIdling()&&AnimationControllerParametersTable.GetBoolActive(FirearmsAnimator.Animator);
        public bool IsInInteractionStrictCheck()=>StrictInteraction;
    }
}
namespace EFT.AssetsManager
{
    public class AssetPoolObject : UnityEngine.Object
    {
        public bool IsInPool;
        public virtual void ReturnToPool() { HarmonyLib.Harmony.Prefix(typeof(AssetPoolObject),nameof(ReturnToPool),this); IsInPool=true; }
        public virtual void OnGetFromPool() { HarmonyLib.Harmony.Prefix(typeof(AssetPoolObject),nameof(OnGetFromPool),this); IsInPool=false; }
    }
}
namespace ArenaMode
{
    internal static class Plugin
    {
        internal const string PluginGuid="tests.pose";
        internal static readonly Logger ModLogger=new();
        internal sealed class Logger {internal int Warnings; internal void LogWarning(string message)=>Warnings++;}
    }
}
namespace ArenaMode.Combat { internal static class WeaponSwapService { internal static bool IsSwapping; } }
namespace ArenaMode.Combat.Translocator
{
    internal static class ArenaTranslocatorRuntime
    {
        internal static bool Live=true;
        internal static bool IsLivePlayer(EFT.Player player)=>Live;
    }
    internal static class ArenaTranslocatorSupply
    {
        internal static bool HasVerifiedTemplate=true;
        internal static EFT.Player Owner;
        internal static EFT.InventoryLogic.Item Item;
        internal static bool Owns(EFT.Player player,EFT.InventoryLogic.Item item)=>ReferenceEquals(Owner,player)&&ReferenceEquals(Item,item);
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        internal readonly MethodInfo Method;
        public HarmonyMethod(Type type,string name)=>Method=type.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic);
    }
    public sealed class Harmony
    {
        public static readonly Dictionary<(Type,string),MethodInfo> Hooks=new();
        public static int PatchCount, FailAt, Unpatches;
        public Harmony(string name) { }
        public void Patch(MethodInfo method,HarmonyMethod prefix=null)
        { if(++PatchCount==FailAt)throw new InvalidOperationException("patch rejected"); Hooks[(method.DeclaringType,method.Name)]=prefix.Method; }
        public void UnpatchSelf() { Unpatches++; Hooks.Clear(); }
        public static void Prefix(Type type,string name,object instance)
        { if(Hooks.TryGetValue((type,name),out var method))method.Invoke(null,new[]{instance}); }
    }
    public static class AccessTools
    {
        public static bool Missing;
        public static MethodInfo Method(Type type,string name,Type[] parameters)=>Missing?null:type.GetMethod(name,parameters);
    }
}
