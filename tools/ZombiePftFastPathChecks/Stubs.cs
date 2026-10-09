using System.Reflection;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) ||
            (ReferenceEquals(a, null) && !ReferenceEquals(b, null) && b.Destroyed) ||
            (ReferenceEquals(b, null) && !ReferenceEquals(a, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace EFT
{
    public enum WildSpawnType { infectedAssault, infectedCivil, infectedLaborant, infectedPmc, infectedTagilla, pmcUSEC, assault }
    public enum EBotState { NonActive, PreActive, Active, Disposed }
    public enum GameStatus { Starting, Started, Stopping }
    public class AbstractGame : UnityEngine.Object { }
    public class LocalGame : AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class Health { public bool IsAlive = true; }
    public class RoleSettings { public WildSpawnType Role; }
    public class ProfileInfo { public RoleSettings Settings = new(); }
    public class Profile { public ProfileInfo Info = new(); }
    public class AIData { public BotOwner BotOwner; }
    public class Player : UnityEngine.Object
    {
        public string ProfileId;
        public bool IsAI = true, IsYourPlayer;
        public Profile Profile = new();
        public Health HealthController = new();
        public AIData AIData = new();
    }
    public class Follower { public bool HaveBoss; }
    public class BotOwner : UnityEngine.Object
    {
        public Player GetPlayer;
        public bool IsDead, PftFollower, Hostile = true;
        public EBotState BotState = EBotState.Active;
        public StandartBotBrain Brain = new();
        public BotStandBy StandBy = new();
        public Follower BotFollower = new();
    }
    public class GameWorld : UnityEngine.Object
    {
        public Player MainPlayer;
        public Dictionary<string, Player> Alive = new();
        public bool ThrowLookup;
        public Player GetAlivePlayerByProfileID(string id)
        {
            if (ThrowLookup) throw new InvalidOperationException("lookup fixture failure");
            return Alive.TryGetValue(id, out var player) ? player : null;
        }
    }
}
public enum BotLogicDecision { test }
public class CoreActionResultParams { }
public struct AICoreActionResult<T, U> { }
public class AICoreLayer<T> { }
public class AICoreStrategy<T>
{
    public List<AICoreLayer<T>> _activeLayers = new();
    public void Update(AICoreActionResult<T, CoreActionResultParams> result) { }
}
public class BaseBrain : AICoreStrategy<BotLogicDecision> { public EFT.BotOwner _owner; }
public class InfectedLayersStrategy : BaseBrain { }
public class CustomBrain : InfectedLayersStrategy { }
public class BaseLogicLayerSimple : AICoreLayer<BotLogicDecision> { public EFT.BotOwner _owner; }
public class InfectedAttackLayer : BaseLogicLayerSimple { }
public class InfectedTargetLayer : BaseLogicLayerSimple { }
public class InfectedPatrolLayer : BaseLogicLayerSimple { }
public class InfectedWaitLayer : BaseLogicLayerSimple { }
public class InfectedFightRequestLayer : BaseLogicLayerSimple { }
public class InfectedPeacefulRequestLayer : BaseLogicLayerSimple { }
public class DebugLogicLayer : BaseLogicLayerSimple { }
public class CustomCombatLayer : InfectedAttackLayer { }
public class UnknownLayer : BaseLogicLayerSimple { }
public class StandartBotBrain { public BaseBrain BaseBrain; }
public class AIBossPlayer { }
public enum BotStandByType { none, paused, goToSave, active }
public class BotStandBy
{
    public BotStandByType StandByType = BotStandByType.active;
    public int Wakes;
    public void Activate() { Wakes++; StandByType = BotStandByType.active; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool Exact = true;
        public static bool IsExactActiveRaid(EFT.LocalGame game, EFT.GameWorld world) => Exact;
    }
}
namespace ArenaMode.Bots
{
    public static class ZombieNativeEventBridge
    {
        public static bool Active = true;
        public static bool IsActiveZombieRaid() => Active;
    }
}
namespace ArenaMode.Config
{
    public class Entry { public bool Value = true; }
    public static class Settings { public static Entry ZombiePftFastPathEnabled = new(); }
}
namespace ArenaMode
{
    public class Log
    {
        public List<string> Lines = new();
        public void LogInfo(object value) => Lines.Add(value.ToString());
        public void LogWarning(object value) => Lines.Add(value.ToString());
    }
    public static class Plugin { public const string PluginGuid = "com.maschine.ArenaMode"; public static Log ModLogger = new(); }
}
namespace BepInEx.Bootstrap
{
    public class Metadata { public Version Version = new(0, 10, 3); }
    public class Info { public object Instance = new pitTeam.Fixture(); public Metadata Metadata = new(); }
    public static class Chainloader { public static Dictionary<string, Info> PluginInfos = new(); }
}
namespace HarmonyLib
{
    public class HarmonyMethod
    {
        public MethodInfo Method;
        public HarmonyMethod(Type type, string name) { Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic); }
    }
    public class Patch { public MethodInfo PatchMethod; }
    public class Patches { public List<Patch> Prefixes = new(); }
    public class Harmony
    {
        public static bool Registered = true;
        public static MethodInfo InstalledHook, InstalledTarget;
        public Harmony(string id) { }
        public static Patches GetPatchInfo(MethodBase method) => Registered ? new Patches
        { Prefixes = new() { new Patch { PatchMethod = pitTeam.Patches.HostileNonCombatActiveLayerFilterPatch.PrefixMethod } } } : null;
        public void Patch(MethodBase original, HarmonyMethod prefix)
        { InstalledTarget = (MethodInfo)original; InstalledHook = prefix.Method; }
    }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] args) => type.GetMethod(name, args);
    }
}
namespace pitTeam
{
    public class Fixture { }
}
namespace pitTeam.Modules
{
    public static class BossPlayers
    {
        public static bool Throw;
        public static bool IsFollower(EFT.BotOwner owner, AIBossPlayer boss = null)
        { if (Throw) throw new InvalidOperationException("PFT registry fixture failure"); return owner.PftFollower; }
    }
}
namespace pitTeam.Patches
{
    // Deliberately small fixture, not vendor source. It exposes the inspected
    // signature and observable wake/filter/restore effects to test our guard.
    public class HostileNonCombatActiveLayerFilterPatch
    {
        private class FilterState { }
        private static void PatchPrefix(object __instance, AICoreActionResult<BotLogicDecision, CoreActionResultParams> prevResult, ref FilterState __state) { }
        public static MethodInfo PrefixMethod => typeof(HostileNonCombatActiveLayerFilterPatch).GetMethod("PatchPrefix", BindingFlags.NonPublic | BindingFlags.Static);
    }
}
