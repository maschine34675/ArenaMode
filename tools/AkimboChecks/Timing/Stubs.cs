using System.Reflection;

public interface IAnimator { }
public sealed class FirearmsAnimator { public IAnimator Animator; }
namespace UnityEngine { public sealed class Animator { public float speed = 1; } }
namespace AnimationSystem
{
    public sealed class UnityAnimatorWrapper : IAnimator
    {
        public UnityEngine.Animator Animator = new();
        public float speed { get => Animator.speed; set => Animator.speed = value; }
    }
}
namespace FastAnimatorSystem { public sealed class FastAnimatorProcessor : IAnimator { public void Update(float deltaTime) { } } }
namespace EFT
{
    public sealed class Player
    {
        public enum EOperationState { Executing, Finished }
        public enum EUpdateMode { Auto, Manual }
        public EUpdateMode ArmsUpdateMode;
        public IAnimator ArmsAnimatorCommon;
        public class ObjectInHandsOperation { public EOperationState State { get; set; } }
        public sealed class FirearmController
        {
            public FirearmsAnimator FirearmsAnimator = new();
            public void ManualUpdate(float deltaTime) { }
            public class FireOperationBase : ObjectInHandsOperation { public FirearmController Controller; }
            public sealed class FireOperation : FireOperationBase { public void Update(float deltaTime) { } }
            public sealed class AutomaticFireOperation : FireOperationBase { public void Update(float deltaTime) { } }
        }
    }
}
namespace ArenaMode.Combat.Akimbo
{
    internal static class ArenaAkimbo
    {
        internal static bool Active;
        internal static EFT.Player Body;
        internal static EFT.Player.FirearmController.FireOperationBase Operation;
        internal static bool TryCadence(out EFT.Player body, out EFT.Player.FirearmController.FireOperationBase operation)
        { body = Body; operation = Operation; return Active && operation?.State == EFT.Player.EOperationState.Executing; }
        internal static bool OwnsHands(EFT.Player.FirearmController hands) => hands != null && ReferenceEquals(hands, Operation?.Controller);
        internal static IAnimator OwnedAnimator => Operation?.Controller.FirearmsAnimator.Animator;
        internal static void AmmoFailed(Exception ex) => throw new Exception("Unexpected adapter failure", ex);
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] args = null) => type.GetMethod(name);
        public static MethodInfo Method(Type type, string name, Type[] args = null) => type.GetMethod(name);
        public static MethodInfo PropertySetter(Type type, string name) => type.GetProperty(name).SetMethod;
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { if (GetTargetMethod() == null) throw new Exception("Missing target"); } }
    public sealed class PatchPrefixAttribute : Attribute { }
    public sealed class PatchPostfixAttribute : Attribute { }
    public sealed class PatchFinalizerAttribute : Attribute { }
}
