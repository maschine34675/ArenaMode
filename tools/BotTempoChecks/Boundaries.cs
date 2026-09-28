using System.Reflection;
using System.Reflection.Emit;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Mover;

namespace UnityEngine
{
    public partial struct Vector3
    {
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b, a.y*b, a.z*b);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x + a.y*b.y + a.z*b.z;
        public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt((a-b).sqrMagnitude);
    }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status; public UnityEngine.Vector3[] corners = [new(0,0,0), new(20,0,0)]; }
}
namespace EFT
{
    public partial class BotOwner
    {
        public object Component;
        public object GetComponent(Type type) => type.IsInstanceOfType(Component) ? Component : null;
        public Weapons WeaponManager = new(); public Medicine Medecine = new(); public Flash FlashGrenade = new();
        public GrenadeGuard BewareGrenade = new(); public Artillery ArtilleryDangerPlace = new();
        public Btr BewareBTR = new(); public Memory Memory = new();
    }
    public class Weapons { public Reload Reload = new(); public Grenades Grenades = new(); }
    public class Reload { public bool Reloading; }
    public class Grenades { public bool ThrowindNow; }
    public class Medicine { public Use FirstAid = new(), SurgicalKit = new(), Stimulators = new(); }
    public class Use { public bool Using; }
    public class Flash { public bool _isFlashed; public bool IsFlashed => throw new Exception("mutative flash getter must not run"); }
    public class GrenadeGuard { public object GrenadeDangerPoint; }
    public class Artillery { public bool IsActive; }
    public class Btr { public bool Danger; public bool ShallRunAway() => Danger; }
    public class Memory { public bool IsUnderFire; }
}
namespace ArenaMode { public static partial class Plugin { public const string PluginGuid = "test.arena"; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown; } }
namespace HarmonyLib
{
    public class HarmonyMethod(Type type, string name) { public MethodInfo Method = type.GetMethod(name, BindingFlags.Static|BindingFlags.NonPublic); }
    public class CodeInstruction(OpCode opcode, object operand = null)
    { public OpCode opcode = opcode; public object operand = operand; public List<Label> labels = new(); }
    public class Harmony(string id)
    {
        public static Dictionary<string, (HarmonyMethod Prefix, HarmonyMethod Finalizer, HarmonyMethod Transpiler)> Hooks = new();
        public static int FailAt, Calls; public static bool BadIl;
        public void Patch(MethodBase target, HarmonyMethod prefix = null, HarmonyMethod finalizer = null, HarmonyMethod transpiler = null)
        {
            if (++Calls == FailAt) throw new InvalidOperationException("patch transaction failure");
            Hooks.Add(target.Name, (prefix, finalizer, transpiler));
            if (transpiler != null) transpiler.Method.Invoke(null, [Il()]);
        }
        public void UnpatchSelf() => Hooks.Clear();
        public static List<CodeInstruction> Il()
        {
            var field = typeof(MoveSettings).GetField("STRAFE_SPEED");
            var code = new List<CodeInstruction> {
                new(OpCodes.Ldc_R4, .9f), // unrelated chase constant must survive
                new(OpCodes.Ldfld, field), new(OpCodes.Callvirt, typeof(SAINMoverClass).GetMethod("SetTargetMoveSpeed")) };
            if (BadIl) code.Add(new(OpCodes.Ldfld, field));
            return code;
        }
    }
}
namespace SAIN.SAINComponent
{
    public class BotBase { public BotComponent Bot { get; set; } }
}
namespace SAIN.Components
{
    public class BotComponent
    {
        public EFT.BotOwner BotOwner { get; set; } public EFT.Player Player { get; set; }
        public bool SAINLayersActive { get; set; } = true;
        public SAIN.Layers.IBotAction CurrentAction { get; set; }
        public SAIN.Classes.Bot.Search.SearchClass Search { get; set; }
        public SAINMoverClass Mover { get; set; } = new();
        public Decision Decision { get; set; } = new(); public GrenadeManager Grenade { get; set; } = new();
        public Enemies EnemyController { get; set; } = new(); public Info Info { get; set; } = new();
    }
    public enum SelfAction { None, Reload, Heal }
    public class Decision { public SelfAction CurrentSelfDecision { get; set; } }
    public class GrenadeManager { public Reaction GrenadeReactionClass { get; set; } = new(); }
    public class Reaction { public object DangerGrenade { get; set; } }
    public class Enemies { public List<Enemy> VisibleEnemies { get; set; } = new(); public List<Enemy> EnemiesInLineOfSight { get; set; } = new(); }
    public class Info { public FileSettings FileSettings { get; set; } = new(); }
    public class FileSettings { public MoveSettings Move = new(); }
    public class MoveSettings { public float STRAFE_SPEED = .4f; }
}
namespace SAIN.Layers
{
    public interface IBotAction { }
    public class BotAction : IBotAction
    {
        private BotComponent _bot;
        public BotAction(BotComponent bot) { _bot = bot; }
        public BotComponent Bot => throw new InvalidOperationException("must not probe lazy action getter");
    }
}
namespace SAIN.Layers.Combat.Solo
{
    public class SearchAction(BotComponent bot) : SAIN.Layers.BotAction(bot) { }
    public class FightAction(BotComponent bot) : SAIN.Layers.BotAction(bot) { }
}
namespace SAIN.Classes.Bot.Search
{
    public enum State { None, DirectMove, Wait }
    public class SearchClass : SAIN.SAINComponent.BotBase
    {
        private float _waitAtPointTimer = -1;
        public bool SearchActive { get; set; } = true;
        public Enemy SearchTarget { get; set; }
        public State CurrentState { get; set; } = State.DirectMove;
        public float Deadline { get => _waitAtPointTimer; set => _waitAtPointTimer = value; }
        public void Search(bool shallSprint, Enemy enemy) { }
        // Minimal native boundary: native, not the adapter, owns path release.
        private bool WaitAtPoint()
        {
            if (_waitAtPointTimer >= UnityEngine.Time.time) return true;
            Bot.Mover.ActivePath.UnpauseCalls++; Bot.Mover.ActivePath.PausedRequested = false;
            _waitAtPointTimer = -1; return false;
        }
    }
}
namespace SAIN.SAINComponent.Classes.EnemyClasses
{
    public class Enemy : SAIN.SAINComponent.BotBase
    {
        public EFT.Player EnemyPlayer { get; set; } public bool IsVisible { get; set; } public bool InLineOfSight { get; set; }
        public Hearing Hearing { get; set; } = new(); public Path Path { get; set; } = new();
    }
    public class Hearing { public bool EnemyHeardFromPeace { get; set; } }
    public class Path { public UnityEngine.AI.NavMeshPath PathToEnemy { get; set; } = new(); }
}
namespace SAIN.SAINComponent.Classes.Mover
{
    public class SAINMoverClass
    {
        public DogFight DogFight { get; set; }
        public BotPathDataManual ActivePath { get; set; }
        public void SetTargetMoveSpeed(float speed) { }
    }
    public class DogFight : SAIN.SAINComponent.BotBase { public void DogFightMove(bool aggressive, Enemy enemy) { } }
    public class BotPathDataManual
    {
        protected readonly BotComponent Bot;
        private float _unpauseTime;
        public BotPathDataManual(BotComponent bot) { Bot = bot; PathFinder = bot.Mover; }
        public SAINMoverClass PathFinder { get; set; }
        public bool PausedRequested { get; set; }
        public bool CancelRequested { get; set; } public bool PathRecalcRequested { get; set; }
        public float TimeStarted { get; set; } = 90; public int Id { get; set; } = 1;
        public float Deadline { get => _unpauseTime; set => _unpauseTime = value; }
        public int UnpauseCalls;
    }
}
