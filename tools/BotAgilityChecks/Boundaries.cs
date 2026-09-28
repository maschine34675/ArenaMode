using System.Reflection;
using System.Reflection.Emit;
using EFT;

namespace UnityEngine
{
    public struct Vector2(float x, float y) { public float x = x, y = y; }
    public static class Mathf
    { public static float InverseLerp(float a, float b, float v) => a == b ? 0 : Math.Clamp((v-a)/(b-a),0,1); }
}
public class PhysicalBase
{
    public float Inertia = .6f, SprintAcceleration = .4f, PreSprintAcceleration = 2.22f;
    public float Side = 1.4f, Diagonal = 1.16f;
}
namespace EFT
{
    public partial class Player { public PhysicalBase Physical = new(); }
    public partial class MovementContext
    {
        public float WalkInertia { get; set; } = .203f;
        public float Inertia => _player.Physical.Inertia;
        public float MoveSideInertia => _player.Physical.Side;
        public float MoveDiagonalInertia => _player.Physical.Diagonal;
        public virtual void PreSprintAcceleration(float deltaTime) { }
        public void SprintAcceleration(float deltaTime) { }
    }
    public class MovePlayerState(MovementContext context) : BaseMovementState
    {
        public MovementContext MovementContext { get; } = context;
        public float _directionSpeedAcceleration = 1;
        public void UpdateMovementDirection(float deltaTime) { }
        public void ProcessDirection(float deltaTime, float smoothTime) { }
    }
    public class GlobalConfiguration
    {
        public InertiaSettings Inertia = new();
        public class InertiaSettings
        {
            public UnityEngine.Vector2 WalkInertia = new(.005f,.335f), SideTime = new(2,1), DiagonalTime = new(1.4f,1),
                PreSprintAccelerationLimits = new(3,1.7f), SprintAccelerationLimits = new(1,0);
            public float SuddenChangesSmoothness = 5;
        }
    }
}
namespace ArenaMode { public static partial class Plugin { public const string PluginGuid = "test.arena"; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown; } }
namespace HarmonyLib
{
    public class HarmonyMethod(Type type, string name)
    { public MethodInfo Method = type.GetMethod(name, BindingFlags.Static|BindingFlags.NonPublic); public int priority; }
    public class CodeInstruction(OpCode opcode, object operand = null)
    { public OpCode opcode = opcode; public object operand = operand; public List<Label> labels = new(); }
    public class Harmony
    {
        public Harmony(string id) { }
        public static readonly Dictionary<MethodBase, (HarmonyMethod Prefix, HarmonyMethod Finalizer, List<CodeInstruction> Code)> Hooks = new();
        public static int FailAt, Calls; public static bool BadIl;
        public void Patch(MethodBase method, HarmonyMethod prefix = null, HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
        {
            if (++Calls == FailAt) throw new Exception("patch failed");
            var code = (IEnumerable<CodeInstruction>)transpiler.Method.Invoke(null, [Il(method), method]);
            Hooks.Add(method, (prefix, finalizer, code.ToList()));
        }
        public void UnpatchSelf() => Hooks.Clear();
        // Only boundary operand inventory, not a copied native method body.
        public static List<CodeInstruction> Il(MethodBase method)
        {
            var code = new List<CodeInstruction> { new(OpCodes.Ldc_R4, 42f) };
            void Get(string name) => code.Add(new(OpCodes.Call, typeof(MovementContext).GetProperty(name).GetMethod));
            void Field(Type type, string name) => code.Add(new(OpCodes.Ldfld, type.GetField(name)));
            switch (method.Name)
            {
                case "UpdateMovementDirection":
                    Get("MoveSideInertia"); Get("MoveDiagonalInertia"); Get("Inertia"); Get("WalkInertia"); Get("WalkInertia");
                    Field(typeof(GlobalConfiguration.InertiaSettings), "SuddenChangesSmoothness"); break;
                case "ProcessDirection": Field(typeof(MovePlayerState), "_directionSpeedAcceleration"); break;
                case "PreSprintAcceleration": Field(typeof(PhysicalBase), "PreSprintAcceleration"); break;
                case "SprintAcceleration": Field(typeof(PhysicalBase), "SprintAcceleration"); Field(typeof(PhysicalBase), "Inertia"); break;
            }
            if (BadIl) code.RemoveAt(code.Count - 1);
            return code;
        }
    }
}
