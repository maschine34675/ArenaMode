using System.Reflection;
using System.Reflection.Emit;

// External surfaces only. The production hooks and lifecycle code are linked,
// not reimplemented. The shared scope has its own root-owned actual-source tests.
internal static class Hook
{
    internal static object Call(string patch, string name, params object[] args)
    {
        var type = typeof(ArenaMode.Movement.ArenaHandlingPatches).GetNestedType(patch, BindingFlags.NonPublic);
        try { return type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
}
public interface IAnimator { float GetFloat(int key); void SetFloat(int key, float value); }
public sealed class NativeAnimator : IAnimator
{
    public Dictionary<int, float> Values = new() { [11] = 1.2f, [12] = 1.25f, [13] = 1.75f };
    public float GlobalSpeed = 1.1f, FireRate = 9f;
    public int Writes;
    public bool ThrowBeforeDraw, ThrowAfterDraw;
    public float GetFloat(int key) => Values.TryGetValue(key, out var value) ? value : 0;
    public void SetFloat(int key, float value)
    {
        if (key == 11 && ThrowBeforeDraw) { ThrowBeforeDraw = false; throw new InvalidOperationException("native draw before write"); }
        Values[key] = value; Writes++;
        if (key == 11 && ThrowAfterDraw) { ThrowAfterDraw = false; throw new InvalidOperationException("native draw after write"); }
    }
}
public static class AnimationControllerParametersTable
{
    public const int FLOAT_SPEEDDRAW = 11;
    public static float GetFloatSpeedDraw(IAnimator animator) => animator.GetFloat(FLOAT_SPEEDDRAW);
    public static void SetSpeedDraw(IAnimator animator, float speedDraw) => animator.SetFloat(FLOAT_SPEEDDRAW, speedDraw);
}
public sealed class FirearmsAnimator
{
    public IAnimator Animator { get; set; } = new NativeAnimator();
    public bool HasDraw = true;
    public bool ThrowReload;
    public bool HasParameter(int key) => HasDraw && key == 11;
    public void SetSpeedParameters(float reload = 1, float draw = 1)
    {
        object[] args = [this, draw, null];
        Hook.Call("SpeedParametersPatch", "Prefix", args);
        draw = (float)args[1];
        Exception failure = null;
        try
        {
            if (ThrowReload) { ThrowReload = false; throw new InvalidOperationException("native reload before draw"); }
            Animator.SetFloat(12, reload);
            Animator.SetFloat(11, draw);
        }
        catch (Exception ex) { failure = ex; throw; }
        finally { Hook.Call("SpeedParametersPatch", "Finalizer", failure, args[2]); }
    }
}
namespace EFT
{
    public class Player
    {
        public MovementContext MovementContext;
        public FirearmController HandsController;
        public enum ESpeedLimit { Fall, Aiming, Weight, Swamp }
        public enum EOperationState { Ready, Executing, Finished }
        public virtual void LandingAdjustments(float distance) { }
        public abstract class ObjectInHandsOperation
        {
            public EOperationState State;
            public void Start() { State = EOperationState.Executing; }
        }
        public class FirearmController
        {
            public FirearmsAnimator FirearmsAnimator = new();
            public FirearmOperation CurrentOperation;
            public bool Destroyed;
            public void ManualUpdate(float delta) => Hook.Call("ControllerUpdatePatch", "Prefix", this);
            public void Destroy() { Hook.Call("ControllerDestroyPatch", "Prefix", this); Destroyed = true; }
            public abstract class FirearmOperation : ObjectInHandsOperation
            {
                public Player Player;
                public FirearmController Controller;
                public FirearmsAnimator FirearmsAnimator;
                public Weapon Weapon = new();
            }
            public class SpawnOperation : FirearmOperation
            {
                public void Start(Action callback) => ArenaMode.Movement.ArenaHandlingPatches.BeginHandlingOperation(this);
                public void WeaponAppeared() { Hook.Call("DrawCompletePatch", "Prefix", this); State = EOperationState.Finished; }
            }
            public class Remove : FirearmOperation
            { public void Start(Action callback, bool fastDrop = false, InventoryLogic.Item item = null) => ArenaMode.Movement.ArenaHandlingPatches.BeginHandlingOperation(this); }
            public class Reload : FirearmOperation { }
            public class Fire : FirearmOperation { }
            public class Malfunction : FirearmOperation { }
            public class OtherDraw : SpawnOperation { }
        }
    }
    public sealed class Weapon { public bool IsStationaryWeapon; }
    public sealed class MovementContext
    {
        public float Speed, Duration;
        public Player.ESpeedLimit Cause;
        public int Changes;
        public void ChangeSpeedLimit(float speed, Player.ESpeedLimit cause, float duration)
        { Speed = speed; Cause = cause; Duration = duration; Changes++; }
    }
}
namespace EFT.InventoryLogic { public class Item { } }
namespace ArenaMode.Movement
{
    // Reload production behavior is independently linked by ArenaReloadChecks.
    internal static class ArenaReloadHandling
    {
        internal static void Enable() { }
        internal static void Shutdown() { }
        internal static void ResetPerRaidState() { }
        internal static void OnControllerUpdate(EFT.Player.FirearmController controller) { }
        internal static void Release(EFT.Player.FirearmController controller) { }
    }
    internal static class ArenaMovementScope
    {
        internal static EFT.Player Local;
        internal static bool Enabled = true;
        internal static bool IsLocal(EFT.Player player, bool requireInput = false) => Enabled && player != null && ReferenceEquals(player, Local);
        internal static bool TryGetPlayer(EFT.MovementContext context, out EFT.Player player, bool requireInput = false)
        { player = IsLocal(Local) && ReferenceEquals(Local.MovementContext, context) ? Local : null; return player != null; }
    }
}
namespace ArenaMode.Config
{
    public class Entry<T> { public T Value; }
    public static class Settings
    {
        public static Entry<float> LandingRecoveryMultiplier = new() { Value = .5f };
        public static Entry<float> WeaponDrawSpeedMultiplier = new() { Value = 1.5f };
        public static Entry<float> WeaponHolsterSpeedMultiplier = new() { Value = 1.5f };
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public class Logger { public List<string> Warnings = new(); public void LogWarning(string message) => Warnings.Add(message); }
}
namespace Redeploy.Core.Engine
{
    public static class RaidLifecycle { public static event Action OnRaidEnded; public static void End() => OnRaidEnded?.Invoke(); }
}
namespace HarmonyLib
{
    public class CodeInstruction(OpCode op, object value = null)
    { public OpCode opcode = op; public object operand = value; public List<Label> labels = new(); }
    public static class Instructions
    {
        public static bool Calls(this CodeInstruction code, MethodInfo method)
            => (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && Equals(code.operand, method);
    }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
            => parameters == null ? type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, parameters, null);
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        public static List<MethodBase> Targets = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() { var target = GetTargetMethod(); if (target == null) throw new Exception("Missing hook target"); Targets.Add(target); }
    }
    public sealed class PatchPrefixAttribute : Attribute { }
    public sealed class PatchPostfixAttribute : Attribute { }
    public sealed class PatchTranspilerAttribute : Attribute { }
    public sealed class PatchFinalizerAttribute : Attribute { }
}
namespace ArenaMode.Movement { internal static class ArenaBunnyhop { internal static void HardLanding(EFT.MovementContext c) { } } }
