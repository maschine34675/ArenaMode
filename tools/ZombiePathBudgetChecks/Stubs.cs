using System.Reflection;
using System.Reflection.Emit;
using EFT;
using UnityEngine;
namespace UnityEngine
{
    public struct Vector3 { public float x,y,z; }
    public static class Time { public static int frameCount; public static float time=10; }
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace EFT
{
    public enum WildSpawnType { infectedAssault, assault }
    public static class Roles { public static bool IsInfected(this WildSpawnType role)=>role==WildSpawnType.infectedAssault; }
    public sealed class Health { public bool IsAlive=true; }
    public sealed class Profile { public Info Info=new(); }
    public sealed class Info { public RoleSettings Settings=new(); }
    public sealed class RoleSettings { public WildSpawnType Role=WildSpawnType.infectedAssault; }
    public sealed class Player { public Health HealthController=new(); public AIData AIData=new(); public MovementContext MovementContext=new(); }
    public sealed class AIData { public BotOwner BotOwner; public Player Player; }
    public sealed class MovementContext { public bool IsInPronePose; }
    public sealed class GameWorld { }
    public sealed class BotOwner
    {
        public Player GetPlayer=new(); public Health HealthController=>GetPlayer.HealthController;
        public Profile Profile=new(); public BotMemory Memory=new(); public BotMover Mover {get;set;}=new();
        public WeaponManager WeaponManager=new(); public BotLay BotLay=new(); public Steering Steering=new();
        public BotSettings Settings=new(); public bool Owned=true,ThrowOwnership; public object World,Mode;
        public int PoseCalls,SprintCalls,Stops,PathSets;
        public BotOwner(){GetPlayer.AIData.BotOwner=this;GetPlayer.AIData.Player=GetPlayer;WeaponManager.Melee=new(){_owner=this};}
        public void SetPose(float p){PoseCalls++;} public void Sprint(bool val){SprintCalls++;}
        public void StopMove(){Stops++;Mover.HasPathAndNoComplete=false;}
        public void GoToByWay(Vector3[] p){PathSets++;Mover.Path=p;Mover.HasPathAndNoComplete=true;}
    }
    public sealed class BotMemory { public EnemyInfo GoalEnemy=new(); }
    public sealed class WeaponManager { public bool IsMelee=true; public WeaponSelector Selector=new(); public BotMeleeWeaponData Melee; }
    public sealed class WeaponSelector { public bool CanChangeToMeleeWeapons=true; public void ChangeToMelee(){} }
    public sealed class BotLay { public bool IsLay; public void GetUp(bool withCheck){IsLay=false;} }
    public sealed class Steering { public int Calls; public void LookToPoint(Vector3 p){Calls++;} public void LookToMovingDirection(){Calls++;} }
    public sealed class BotSettings { public FileSettings FileSettings=new(); }
    public sealed class FileSettings { public ShootSettings Shoot=new(); }
    public sealed class ShootSettings { public float MELEE_STOP_MOVE_DISTANCE=.1f; }
}
public class BotData { public BotOwner _owner; }
public sealed class BotMover { public bool HasPathAndNoComplete {get;set;} public Vector3[] Path; }
public sealed class EnemyInfo { public Player Person; public float Distance=.5f; public Vector3 GetBodyPartPosition()=>new(); }
public sealed class BotMeleeWeaponData : BotData
{
    public bool ShallEndRun,Running=true,_useZigZag,MeleeWeaponEquipped=true,RouteResult=true,ThrowRoute;
    public float DIST_TO_HIT=1,DIST_TO_STOP_SPRINT=2,_nextTryHitTime,TRY_HIT_PERIOD_FALSE=.1f,_runPathCheck,FAR_DIST=20,MID_DIST=10;
    public float farRecalc=1,midRecalc=.5f,closeRecalc=.1f,midRecalcZZ=.5f,closeRecalcZZ=.1f;
    public int Hits,Routes; public readonly Vector3[] NativePath=new Vector3[2];
    public bool TryHit(EnemyInfo enemy){Hits++;return true;}
    public void ResetHitTime(float delay){_nextTryHitTime=Time.time+delay;}
    public bool CanRunToEnemyToHit(EnemyInfo enemy,out Vector3[] path){Routes++;path=NativePath;if(ThrowRoute)throw new InvalidOperationException("native route fault");return RouteResult;}
    public bool RunToEnemyUpdate()=>true;
}
public class MoveToEnemyZigZag : BotData
{
    public float _nextCheck;public int _lastEnemyId=-1;public Vector3 _lastEnemyPos;public int NativeCalls,NestedPaths;
    public void NotMovingCheck(){if(_nextCheck>Time.time)return;_nextCheck=Time.time+1;_lastEnemyId++;NativeCalls++;NestedPaths+=8;}
}
public enum DoorInteractionStatus { CanRun,Blocked }
public sealed class OneMeleeAttackNode : BotData { public DoorInteractionStatus DoorOpen()=>DoorInteractionStatus.CanRun;public void UpdateNodeByBrain(){} }
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid="fixture";public static Logger ModLogger=new(); }
    public sealed class Logger { public int Warnings; public void LogDebug(string s){} public void LogWarning(string s){Warnings++;} }
}
namespace ArenaMode.Diagnostics { public static class ArenaLog { public static bool DeveloperEnabled;public static void Developer(string s){} } }
namespace ArenaMode.Respawn { }
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive=true; } }
namespace ArenaMode.Modes { public sealed class Match { public object Mode; }public static class ArenaMatchService {public static Match Current=new();} }
namespace ArenaMode.Bots
{
    public static class ZombieRecoveryRuntime
    {
        public static bool OwnsCurrentBody(BotOwner owner)=>owner.ThrowOwnership ? throw new InvalidOperationException("vanishing native body") : owner.Owned && owner.GetPlayer.HealthController.IsAlive
            && ReferenceEquals(owner.GetPlayer.AIData.BotOwner,owner)
            && ReferenceEquals(owner.World,Comfort.Common.Singleton<GameWorld>.Instance)
            && ReferenceEquals(owner.Mode,ArenaMode.Modes.ArenaMatchService.Current.Mode);
    }
    public static class ZombieNativeEventBridge { public static bool IsActiveZombieRaid()=>true; }
}
namespace SPT.Reflection.Patching
{
    public sealed class PatchPrefixAttribute:Attribute { }
    public abstract class ModulePatch { public static List<string> Enabled=new();protected abstract MethodBase GetTargetMethod();public void Enable(){if(GetTargetMethod()==null)throw new Exception("target absent");Enabled.Add(GetType().Name);} }
}
namespace HarmonyLib
{
    public enum HarmonyReversePatchType { Original,Snapshot }
    public sealed class HarmonyMethod {public MethodInfo method;public HarmonyReversePatchType reversePatchType;public HarmonyMethod(MethodInfo method){this.method=method;} }
    public sealed class Harmony
    {
        public static MethodInfo ReversePatch(MethodBase original,HarmonyMethod standin,MethodInfo transpiler,MethodInfo manipulator)
            => throw new NotSupportedException("Fixture invokes production fallback and emits transformed IL separately");
    }
    public static class AccessTools
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        public static MethodInfo Method(Type type,string name)=>type.GetMethod(name,Flags);
        public static MethodInfo PropertyGetter(Type type,string name)=>type.GetProperty(name,Flags)?.GetGetMethod(true);
        public static FieldInfo Field(Type type,string name)=>type.GetField(name,Flags);
    }
    public sealed class CodeInstruction
    {
        public OpCode opcode;public object operand;public List<Label> labels=new();public List<object> blocks=new();
        public CodeInstruction(OpCode opcode,object operand=null){this.opcode=opcode;this.operand=operand;}
    }
}
