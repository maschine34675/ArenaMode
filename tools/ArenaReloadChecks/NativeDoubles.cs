using System.Reflection;
using System.Reflection.Emit;

// External APIs only. The complete production lease and patch code is linked.
internal static class Hook
{
    internal static object Call(string patch, string method, params object[] arguments)
    {
        var type = typeof(ArenaMode.Movement.ArenaReloadHandling).GetNestedType(patch, BindingFlags.NonPublic);
        try { return type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
}
public interface IAnimator
{
    float GetFloat(int key); void SetFloat(int key, float value); bool HasParameter(int key);
}
public class NativeAnimator(float reload = 1.2f) : IAnimator
{
    public Dictionary<int, float> Values = new() { [12] = reload, [11] = 1.1f, [13] = 1.7f };
    public float GlobalSpeed = 1.05f, FireRate = 9f;
    public bool HasReload = true, ThrowBefore, ThrowAfter, ThrowRead;
    public int Writes;
    public float GetFloat(int key)
    {
        if (ThrowRead) { ThrowRead = false; throw new InvalidOperationException("native read failed"); }
        return Values.TryGetValue(key, out var value) ? value : 0;
    }
    public void SetFloat(int key, float value)
    {
        if (key == 12 && ThrowBefore) { ThrowBefore = false; throw new InvalidOperationException("native write failed before"); }
        Values[key] = value; Writes++;
        if (key == 12 && ThrowAfter) { ThrowAfter = false; throw new InvalidOperationException("native write failed after"); }
    }
    public bool HasParameter(int key) => key != 12 || HasReload;
}
public static class AnimationControllerParametersTable
{
    public const int FLOAT_SPEEDRELOAD = 12;
    public static float GetFloatSpeedReload(IAnimator animator) => animator.GetFloat(12);
    public static void SetSpeedReload(IAnimator animator, float value) => animator.SetFloat(12, value);
}
public sealed class FirearmsAnimator
{
    public IAnimator Animator = new NativeAnimator();
    public bool ThrowAfterReload;
    public bool HasParameter(int key) => Animator.HasParameter(key);
    public void SetSpeedParameters(float reload = 1, float draw = 1)
    {
        Animator.SetFloat(12, reload);
        if (ThrowAfterReload) { ThrowAfterReload = false; throw new InvalidOperationException("native draw write failed"); }
        Animator.SetFloat(11, draw);
    }
}
namespace EFT
{
    public class PlayerAnimator
    {
        public IAnimator Animator = new NativeAnimator(1.4f);
        public void SetSpeedParameters(float reload = 1, float draw = 1)
        { Animator.SetFloat(12, reload); Animator.SetFloat(11, draw); }
    }
    public class MovementContext { public PlayerAnimator PlayerAnimator = new(); }
    public class Weapon { public bool IsStationaryWeapon; public string TemplateId = "weapon-template"; }
    public class Player
    {
        public MovementContext MovementContext = new();
        public FirearmController HandsController;
        public enum EOperationState { Ready, Executing, Finished }
        public class FirearmController
        {
            public FirearmsAnimator FirearmsAnimator = new();
            public FirearmOperation CurrentOperation;
            public void SetAnimatorAndProceduralValues() { }
            public Action NativeDeath;
            public void OnPlayerDead() { Hook.Call("DeathPatch", "Prefix", this); NativeDeath?.Invoke(); }
            public abstract class FirearmOperation
            {
                public Player Player;
                public FirearmController Controller;
                public FirearmsAnimator FirearmsAnimator;
                public Weapon Weapon = new();
                public virtual EOperationState State { get; set; }
            }
            public abstract class BaseReloadOperation : FirearmOperation
            {
                public Action BeforeNativeStateChanged;
                public override EOperationState State
                {
                    get => base.State;
                    set
                    {
                        Hook.Call("ReloadStatePatch", "Prefix", this, value);
                        BeforeNativeStateChanged?.Invoke();
                        base.State = value;
                    }
                }
            }
            public class ReloadExternalMagOperation : BaseReloadOperation { }
            public class ReloadInternalMagOperation : BaseReloadOperation { }
            public class ReloadInternalMagWithOpenBoltOperation : BaseReloadOperation { }
            public class ReloadCylinderMagOperation : BaseReloadOperation { }
            public class ReloadSingleBarrelOperation : BaseReloadOperation { }
            public class ReloadMultiBarrelOperation : BaseReloadOperation { }
            public class LauncherReload : FirearmOperation { }
            public class FireOperation : FirearmOperation { }
            public class BoltActionFireOperation : FirearmOperation { }
            public class Malfunction : FirearmOperation { }
            public class InsertMagOperation : FirearmOperation
            {
                public Action NativeContinuation;
                public void SwitchToIdlingState() { Hook.Call("MagazineBoundaryPatch", "Prefix", this); State=EOperationState.Finished; NativeContinuation?.Invoke(); }
                public void HideWeapon() { Hook.Call("MagazineBoundaryPatch", "Prefix", this); State=EOperationState.Finished; NativeContinuation?.Invoke(); }
                public void Reset() { Hook.Call("MagazineBoundaryPatch", "Prefix", this); State=EOperationState.Ready; NativeContinuation?.Invoke(); }
            }
            public class PullOutMagOperation : FirearmOperation
            {
                public Action NativeContinuation;
                public void OnMagPuttedToRig() { Hook.Call("MagazineBoundaryPatch", "Prefix", this); State=EOperationState.Finished; NativeContinuation?.Invoke(); }
                public void Reset() { Hook.Call("MagazineBoundaryPatch", "Prefix", this); State=EOperationState.Ready; NativeContinuation?.Invoke(); }
            }
            public class UnknownInsert : InsertMagOperation { }
            public class UnknownPullOut : PullOutMagOperation { }
            public class LoadCartridgeToChamber : FirearmOperation { }
            public class Remove : FirearmOperation { }
            public class UnknownReload : ReloadExternalMagOperation { }
        }
    }
}
namespace ArenaMode.Movement
{
    internal static class ArenaMovementScope
    {
        // Actual body/session/stand-down guards are linked by the existing
        // movement-scope checks. This suite exercises the consumer's boundary.
        internal static EFT.Player Local;
        internal static bool Enabled = true;
        internal static bool IsLocal(EFT.Player player, bool requireInput = false)
            => Enabled && player != null && ReferenceEquals(player, Local);
    }
}
namespace ArenaMode.Config
{
    public class Entry<T> { public T Value; }
    public static class Settings
    { public static Entry<float> WeaponReloadSpeedMultiplier = new() { Value = 1.5f }; }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger; }
    public class Logger
    {
        public List<string> Warnings = new(), Infos = new();
        public bool Throw;
        public void LogWarning(string value) { if (Throw) throw new Exception("log sink"); Warnings.Add(value); }
        public void LogInfo(string value) { if (Throw) throw new Exception("log sink"); Infos.Add(value); } public void LogDebug(string value) { if (Throw) throw new Exception("log sink"); Infos.Add(value); }
    }
}
namespace AnimationSystem
{
    public sealed class UnityAnimatorWrapper : NativeAnimator
    {
        public UnityAnimatorDouble Animator = new();
    }
    public sealed class UnityAnimatorDouble
    {
        public int layerCount = 2;
        public bool ThrowState;
        public State GetCurrentAnimatorStateInfo(int layer)
        { if (ThrowState) throw new Exception("state getter"); return new(); }
        public struct State
        {
            public int fullPathHash => 456;
            public float normalizedTime => .4f;
            public float speed => .73f;
            public float speedMultiplier => 2.5f;
        }
    }
}
namespace HarmonyLib
{
    public class CodeInstruction(OpCode opcode, object operand = null)
    { public OpCode opcode = opcode; public object operand = operand; public List<Label> labels = new(); }
    public static class InstructionExtensions
    { public static bool Calls(this CodeInstruction instruction, MethodInfo target) => (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, target); }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name) => type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        public static MethodInfo PropertySetter(Type type, string name) => type.GetProperty(name).SetMethod;
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
            => parameters == null ? type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        protected ModulePatch(string name = null) { }
        public static readonly List<MethodBase> Targets = new();
        public static int FailEnableAt = -1, FailDisableCount;
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            var target = GetTargetMethod() ?? throw new Exception("missing target");
            Targets.Add(target);
            if (Targets.Count == FailEnableAt) throw new InvalidOperationException("install failed after patching");
        }
        public void Disable()
        {
            if (FailDisableCount-- > 0) throw new InvalidOperationException("patch removal failed");
            Targets.Remove(GetTargetMethod());
        }
    }
    public sealed class PatchPrefixAttribute : Attribute { }
    public sealed class PatchTranspilerAttribute : Attribute { }
}

// Explicitly enabled for these diagnostic-observation scenarios; the real
// default-off BepInEx routing is exercised by tools/LoggingChecks.
namespace ArenaMode.Diagnostics
{
    internal static class ArenaLog
    {
        internal static bool DeveloperEnabled = true;
        internal static void Developer(string message)
        {
            if (DeveloperEnabled) ArenaMode.Plugin.ModLogger?.LogInfo("[dev] " + message);
        }
    }
}
