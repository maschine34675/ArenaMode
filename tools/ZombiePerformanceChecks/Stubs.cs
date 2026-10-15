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
    public struct Vector3 { }
    public static class Time { public static float time, realtimeSinceStartup; }
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
    public interface IBotGame { }
    public class AbstractGame : UnityEngine.Object { }
    public class LocalGame : AbstractGame, IBotGame { public GameStatus Status = GameStatus.Started; }
    public interface IPlayer { string ProfileId { get; } int Id { get; } AIData AIData { get; } }
    public class Health { public bool IsAlive = true; }
    public class RoleSettings { public WildSpawnType Role; }
    public class ProfileInfo { public RoleSettings Settings = new(); }
    public class Profile { public ProfileInfo Info = new(); }
    public class AIData { public BotOwner BotOwner; public bool IsMute; }
    public class Player : UnityEngine.Object, IPlayer
    {
        public string ProfileId { get; set; }
        public int Id { get; set; }
        public bool IsAI = true, IsYourPlayer;
        public Profile Profile = new();
        public Health HealthController = new();
        public AIData AIData { get; set; } = new();
    }
    public class EnemyInfo { public IPlayer Person; }
    public class GoalTarget { public bool HasPlace; public bool HavePlaceTarget() => HasPlace; }
    public class BotMemory { public EnemyInfo GoalEnemy; public GoalTarget GoalTarget = new(); }
    public class Hearing { public float HEAR_DELAY_WHEN_HAVE_SMT = .3f, HEAR_DELAY_WHEN_PEACE = .5f; }
    public class FileSettings { public Hearing Hearing = new(); }
    public class BotSettings { public FileSettings FileSettings = new(); }
    public class BotOwner : UnityEngine.Object, IPlayer
    {
        public Player GetPlayer;
        public string ProfileId => GetPlayer.ProfileId;
        public int Id => GetPlayer.Id;
        public AIData AIData => GetPlayer.AIData;
        public bool IsDead;
        public EBotState BotState = EBotState.Active;
        public BotsGroup BotsGroup;
        public BotMemory Memory = new();
        public BotSettings Settings = new();
        public BotHearingSensor HearingSensor;
        public BotFriendChecker FriendChecker;
    }
    public class GameWorld : UnityEngine.Object
    {
        public Player MainPlayer;
        public Dictionary<string, Player> Alive = new();
        public bool ThrowLookup;
        public int LookupCalls;
        public Player GetAlivePlayerByProfileID(string id)
        {
            LookupCalls++;
            if (ThrowLookup) throw new InvalidOperationException("lookup fixture failure");
            return Alive.TryGetValue(id, out var player) ? player : null;
        }
    }
}
public enum AISoundType { step, gun, silencedGun, other }
public class BotGroupEnemyInfo { public EFT.Player Player; }
public class BotsGroup
{
    public EFT.IBotGame BotGame;
    public EFT.WildSpawnType InitialBotType;
    public Dictionary<EFT.IPlayer, BotGroupEnemyInfo> Enemies = new(), Neutrals = new();
    public List<EFT.BotOwner> Members = new();
    public List<EFT.IPlayer> Allies = new();
    public HashSet<EFT.WildSpawnType> Suitable = new();
    public bool Contains(EFT.BotOwner owner) => Members.Contains(owner);
    public bool IsAlly(EFT.IPlayer player) => Allies.Contains(player);
    public bool IsSuitable(EFT.WildSpawnType role) => Suitable.Contains(role);
    public bool IsEnemy(EFT.IPlayer player)
    {
        foreach (var enemy in Enemies)
            if (enemy.Value.Player != null && enemy.Value.Player.Id == player.Id) return true;
        return false;
    }
}
public class BotHearingSensor
{
    public EFT.BotOwner _botOwner;
    public void OnSoundPlayed(EFT.IPlayer player, UnityEngine.Vector3 position, float power, AISoundType type) { }
}
public class BotFriendChecker
{
    public EFT.BotOwner _owner;
    public float _nextTimeCheck;
    public void ManualUpdate() { }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static string ActiveMode;
        public static EFT.LocalGame StartedGame;
        public static EFT.GameWorld StartedWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, StartedGame) && ReferenceEquals(world, StartedWorld);
    }
}
namespace ArenaMode.Bots
{
    public static class ZombieNativeEventBridge
    {
        public static bool IsActiveZombieRaid() => Session.ArenaSession.IsActive && Session.ArenaSession.ActiveMode == "zombie";
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Log ModLogger = new(); }
    public class Log
    {
        public readonly List<string> Infos = new(), Warnings = new();
        public void LogInfo(string value) => Infos.Add(value); public void LogDebug(string value) => Infos.Add(value);
        public void LogWarning(string value) => Warnings.Add(value);
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] args)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, args);
    }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefixAttribute : Attribute { }
    public abstract class ModulePatch
    {
        public static readonly List<MethodBase> Installed = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            var method = GetTargetMethod() ?? throw new InvalidOperationException("Target missing");
            Installed.Add(method);
        }
    }
}
