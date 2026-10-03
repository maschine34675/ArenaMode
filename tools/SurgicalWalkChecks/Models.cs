using System.Reflection;
using System.Reflection.Emit;

namespace ArenaMode.Config {
    public sealed class Setting<T>(T value) { public T Value = value; }
    public static class Settings { public static Setting<bool> MovementEnabled = new(true); }
}
namespace ArenaMode.Hud { public static class ArenaInputBlocker { public static bool Blocked; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown, IsRespawnInProgress; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
namespace ArenaMode {
    public static class Plugin { public static Logger ModLogger = new(); }
    public sealed class Logger { public void LogWarning(object value) { } public void LogInfo(object value) { } }
}
namespace ArenaMode.Session {
    public static class ArenaSession {
        public static bool IsActive;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(Game, game) && ReferenceEquals(World, world);
    }
}
namespace Comfort.Common {
    public static class Singleton<T> where T:class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace UnityEngine {
    public struct Vector2 { public float x,y; }
    public struct Vector3(float x,float y,float z) { public float x=x,y=y,z=z; }
}
namespace EFT {
    public enum GameStatus { Started, Stopping }
    [Flags] public enum EPhysicalCondition { HealingLegs=1, UsingMeds=2 }
    public class AbstractGame { }
    public class LocalGame:AbstractGame { public Player LocalPlayer; public GameStatus Status = GameStatus.Started; }
    public class GameWorld { public Player MainPlayer; }
    public class ObstacleFacade { public bool Allowed=true; public int Reads; public bool CanMove() { Reads++; return Allowed; } }
    public class MovementContext {
        public EPhysicalCondition Flags=EPhysicalCondition.HealingLegs|EPhysicalCondition.UsingMeds;
        public bool IsInPronePose, IsInMountedState, Vaulting;
        public object OverridenState, OverridenControlsState;
        public MovementState CurrentState = new IdlePlayerState();
        public ObstacleFacade ObstacleCollisionFacade=new();
        public bool CanWalk => !PhysicalConditionIs(EPhysicalCondition.HealingLegs) && ObstacleCollisionFacade.CanMove();
        public bool PhysicalConditionIs(EPhysicalCondition flag) => (Flags & flag)!=0;
        public bool PlayerAnimatorGetIsVaulting() => Vaulting;
    }
    public class MovementState { }
    public class IdlePlayerState:MovementState { public void Move(UnityEngine.Vector2 direction) { } }
    public class MovePlayerState:MovementState { public void ManualAnimatorMoveUpdate(float deltaTime) { } }
    public class DerivedMove:MovePlayerState { }
    public class Player {
        public bool IsYourPlayer=true, IsAI, IsInventoryOpened;
        public HealthSystem.ActiveHealthController HealthController = new();
        public HealthSystem.ActiveHealthController ActiveHealthController => HealthController;
        public MovementContext MovementContext=new();
        public object HandsController;
        public class MedsController { public InventoryLogic.Meds Item=new(); public object GetItem()=>Item; }
    }
}
namespace EFT.InventoryLogic {
    public enum EDamageEffectType { DestroyedPart }
    public class HealthEffectsComponent { public bool Surgical=true; public bool AffectsAny(EDamageEffectType kind)=>Surgical; }
    public class Meds { public HealthEffectsComponent HealthEffectsComponent=new(); }
}
namespace EFT.HealthSystem {
    public enum EEffectState { Added, Started, Residued, Removed }
    public class ActiveHealthController {
        public bool IsAlive=true;
        public MedEffect Effect;
        public T FindExistingEffect<T>() where T:class => Effect?.Existing == true ? Effect as T : null;
        public class MedEffect {
            public EEffectState State=EEffectState.Started;
            public bool Existing=>State==EEffectState.Added || State==EEffectState.Started;
            public object MedItem;
            public ActiveHealthController HealthController;
        }
    }
}
namespace SPT.Reflection.Patching {
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchTranspilerAttribute:Attribute { }
    public abstract class ModulePatch {
        public static List<MethodBase> Hooks=new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() {
            var method=GetTargetMethod() ?? throw new Exception("missing hook"); Hooks.Add(method);
            var input=new[]{new HarmonyLib.CodeInstruction(OpCodes.Callvirt,
                HarmonyLib.AccessTools.PropertyGetter(typeof(EFT.MovementContext), "CanWalk"))};
            GetType().GetMethod("Transpiler",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{input});
        }
    }
}
// Reflection/IL carriers, not a replacement simulation of Unity or medical operations.
namespace HarmonyLib {
    public static class AccessTools {
        public static MethodInfo DeclaredMethod(Type type,string name,Type[] args)
            => type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly,null,args,null);
        public static MethodInfo Method(Type type,string name)
            => type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
        public static MethodInfo PropertyGetter(Type type,string name)=>type.GetProperty(name)?.GetMethod;
    }
    public sealed class CodeInstruction {
        public OpCode opcode; public object operand; public List<Label> labels=new();
        public CodeInstruction(OpCode code,object value=null) { opcode=code; operand=value; }
        public CodeInstruction(CodeInstruction other) { opcode=other.opcode; operand=other.operand; labels=new(other.labels); }
        public bool Calls(MethodInfo method)=>(opcode==OpCodes.Call||opcode==OpCodes.Callvirt)&&Equals(operand,method);
    }
}
