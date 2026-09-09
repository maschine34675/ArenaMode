// Independent minimal API doubles, not copied SAIN/EFT code. They expose only
// the contracts used by linked production and controllable callback/readback
// outcomes. No pathfinding, Unity physics, Harmony detours or AI is simulated.
using System.Reflection;
using EFT;
using UnityEngine;

namespace UnityEngine
{
    public static class Time { public static float time; }
    public readonly struct Vector3(float x, float y = 0, float z = 0)
    {
        public readonly float x = x, y = y, z = z;
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum WildSpawnType { assault, pmcUSEC, pmcBEAR }
    public enum EBotState { PreActive, Active, NonActive }
    public enum EEnemyPartVisibleType { Sence }
    public enum EBotEnemyCause { initial, bossKillArena }
    public enum GameStatus { Starting, Started, Stopping, Stopped }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class GameWorld
    {
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Players = new();
        public Player GetAlivePlayerByProfileID(string id) => Players.GetValueOrDefault(id);
    }
    public class Health { public bool IsAlive = true; }
    public class Profile { public Info Info = new(); }
    public class Info { public EPlayerSide Side; public Settings Settings = new(); }
    public class Settings { public WildSpawnType Role; }
    public class AIData { public BotOwner BotOwner; }
    public interface IPlayer
    { string ProfileId { get; } bool IsAI { get; } Health HealthController { get; } }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; }
        public bool IsAI { get; set; }
        public Health HealthController { get; set; } = new();
        public Profile Profile = new();
        public AIData AIData = new();
        public Vector3 Position;
    }
    public class BotOwner : IPlayer
    {
        public Player GetPlayer;
        public string ProfileId => GetPlayer.ProfileId;
        public bool IsAI => true;
        public Health HealthController => GetPlayer.HealthController;
        public EBotState BotState = EBotState.Active;
        public Vector3 Position => GetPlayer.Position;
        public BotsGroup BotsGroup;
        public BotMemory Memory;
        public BotEnemiesController EnemiesController = new();
        public object Component;
        public Mover Mover = new();
        public StandBy StandBy = new();
        public object GetComponent(Type type) => Component != null && type.IsInstanceOfType(Component) ? Component : null;
    }
    public class Mover { public bool IsMoving; }
    public class StandBy { public string StandByType = "active"; }
    public class BotMemory
    {
        public BotOwner _owner;
        public EnemyInfo GoalEnemy { get; set; }
        public bool IsPeace = true;
        public void AddEnemy(IPlayer enemy, BotGroupEnemyInfo groupInfo, bool onActivation)
            => _owner.EnemiesController.EnemyInfos.TryAdd(enemy, new EnemyInfo
            { Person = enemy, GroupInfo = groupInfo, Owner = _owner, GroupOwner = _owner.BotsGroup });
        public void DeleteInfoAboutEnemy(IPlayer enemy) => _owner.EnemiesController.EnemyInfos.Remove(enemy);
    }
}
public class BotsGroup
{
    public AbstractGame BotGame;
    public BotOwner _initialBot;
    public EPlayerSide Side;
    public WildSpawnType InitialBotType;
    public List<BotOwner> Members = new();
    public int MembersCount => Members.Count;
    public BotOwner Member(int i) => Members[i];
    public Dictionary<IPlayer, BotGroupEnemyInfo> Enemies = new(), Neutrals = new();
    public List<IPlayer> Allies = new();
    public int PositionReports;
    public bool AddEnemy(IPlayer person, EBotEnemyCause cause)
    {
        var info = new BotGroupEnemyInfo { Player = person as Player, _botGroup = this };
        Enemies.TryAdd(person, info);
        foreach (var owner in Members) owner.Memory.AddEnemy(person, Enemies[person], false);
        return true;
    }
    public void RemoveEnemy(IPlayer person)
    { Enemies.Remove(person); foreach (var owner in Members) owner.Memory.DeleteInfoAboutEnemy(person); }
    public void AddAlly(Player player) { if (!Allies.Contains(player)) Allies.Add(player); }
    public void ReportAboutEnemy(IPlayer player, EEnemyPartVisibleType sense, BotOwner owner) => PositionReports++;
}
public class BotGroupEnemyInfo
{ public Player Player; public BotsGroup _botGroup; public bool IsLastPositionChecked = true; }
public class EnemyInfo
{
    public IPlayer Person;
    public BotOwner Owner;
    public BotsGroup GroupOwner;
    public BotGroupEnemyInfo GroupInfo;
    public bool IgnoreUntilAggression = true;
}
public class BotEnemiesController { public Dictionary<IPlayer, EnemyInfo> EnemyInfos = new(); }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Method)] public class PatchPrefix : Attribute { }
    public class Harmony(string id)
    {
        public static readonly List<(string Id, MethodBase Original, MethodInfo Prefix)> Hooks = new();
        public static string ThrowOnMethod;
        public void Patch(MethodBase method, HarmonyMethod prefix)
        {
            if (method.Name == ThrowOnMethod) throw new InvalidOperationException("modeled patch failure");
            Hooks.Add((id, method, prefix.Method));
        }
        public void UnpatchSelf() => Hooks.RemoveAll(h => h.Id == id);
    }
    public class HarmonyMethod(Type type, string name)
    { public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
    public static class AccessTools
    {
        public static MethodBase Method(Type type, string name, Type[] args) => type.GetMethod(name, args);
        public static MethodBase PropertySetter(Type type, string name) => type.GetProperty(name).SetMethod;
    }
}
namespace SPT.Reflection.Patching
{ public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { } } }
namespace ArenaMode
{
    internal static class Plugin
    { internal const string PluginGuid = "test.arena"; internal static readonly Logger ModLogger = new(); }
    internal class Logger
    {
        internal readonly List<string> Lines = new();
        internal void LogInfo(object line) => Lines.Add(line.ToString());
        internal void LogWarning(object line) => Lines.Add(line.ToString());
    }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static string ActiveMode;
        internal static AbstractGame Game;
        internal static GameWorld World;
        internal static bool IsExactActiveRaid(AbstractGame game, GameWorld world)
            => IsActive && ReferenceEquals(Game, game) && ReferenceEquals(World, world);
    }
}
