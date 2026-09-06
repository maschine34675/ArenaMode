// External-object models. The actual native enemy-list source and production
// prefixes are linked by the project; no Unity/Harmony detours run here.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool emptyA = ReferenceEquals(a, null) || a.Destroyed;
            bool emptyB = ReferenceEquals(b, null) || b.Destroyed;
            return emptyA || emptyB ? emptyA == emptyB : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public readonly Dictionary<Type, Component> Components = new();
        public Component GetComponent(Type type) => Components.GetValueOrDefault(type);
    }
    public struct Vector3
    {
        public static Vector3 up => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => default;
    }
    public struct Color { public static Color red, yellow, green; }
    public static class Gizmos { public static Color color; public static void DrawLine(Vector3 a, Vector3 b) { } }
    public static class Time { public static float time; }
}
namespace JetBrains.Annotations { public class NotNullAttribute : Attribute { } }
namespace EFT.Ballistics { public class DamageInfo { } }
public enum EBodyPart { Head }
namespace EFT
{
    public enum WildSpawnType { pmcUSEC, bossBoar, bossZryachiy, followerZryachiy }
    public class AbstractGame : UnityEngine.Object { }
    public class AIData { public bool ShallPursuit; }
    public interface IPlayer { AIData AIData { get; } int Id { get; } string ProfileId { get; } }
    public class Player : UnityEngine.Component, IPlayer
    {
        public bool IsAI = true;
        public int Id { get; set; }
        public string ProfileId { get; set; } = Guid.NewGuid().ToString();
        public AIData AIData { get; } = new();
    }
    public class BotOwner : UnityEngine.Component
    {
        public Player GetPlayer { get; set; }
        public string ProfileId => GetPlayer?.ProfileId;
        public BotEnemiesController EnemiesController;
        public Profile Profile = new();
        public Axe PriorityAxeTarget = new();
        public Memory Memory = new();
        public BotSettings Settings = new();
        public UnityEngine.Vector3 Position;
        public void PreActivate() { }
    }
    public class Axe { public bool AllPursuit; }
    public class BotSettings { public Files FileSettings = new(); }
    public class Files { public MindSettings Mind = new(); }
    public class MindSettings { public bool WILL_PERSUE_AXEMAN; }
    public class Memory { public EnemyInfo GoalEnemy; public bool HaveEnemy; }
    public class Profile { public ProfileInfo Info = new(); }
    public class ProfileInfo { public ProfileSettings Settings = new(); }
    public class ProfileSettings { public WildSpawnType Role; }
    public class GameWorld : UnityEngine.Object
    {
        public string LocationId = "labyrinth";
        public readonly Dictionary<string, Player> Players = new();
        public Player GetEverExistedPlayerByID(string id) => Players.GetValueOrDefault(id);
    }
}
public class BotsGroup { }
public class BotGroupEnemyInfo { }
public class EnemyInfo
{
    public EFT.IPlayer Person;
    public int PriorityIndex, DisposeCount;
    public float Distance, LastDoHitTime, FirstTimeShoot;
    public UnityEngine.Vector3 CurrPosition;
    public EnemyInfo(BotsGroup group, EFT.IPlayer player, EFT.BotOwner owner, BotGroupEnemyInfo info) { Person = player; }
    public void Dispose() => DisposeCount++;
}
public class BotEnemiesMissController
{
    public BotEnemiesMissController(BotEnemiesController c, EFT.BotOwner o) { }
    public void Activate() { }
    public void Dispose() { }
}
public class BoarEnemyController(EFT.BotOwner owner) : BotEnemiesController(owner) { }
public class ZryachiyEnemyController(EFT.BotOwner owner) : BotEnemiesController(owner) { }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid;
        public static string ArmedMap;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly Log ModLogger = new();
    }
    public class Log
    {
        public readonly List<string> Messages = new();
        public void LogInfo(object value) => Messages.Add(value.ToString());
        public void LogWarning(object value) => Messages.Add(value.ToString());
    }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public sealed class HarmonyMethod(MethodInfo method) { public MethodInfo method = method; public int priority; }
    public sealed class Harmony(string id)
    {
        public static readonly Dictionary<MethodInfo, (string Id, HarmonyMethod Prefix)> Hooks = new();
        public static int FailPatchNumber;
        public void Patch(MethodInfo original, HarmonyMethod prefix = null)
        {
            if (FailPatchNumber > 0 && --FailPatchNumber == 0) throw new InvalidOperationException("patch fixture failure");
            Hooks[original] = (id, prefix);
        }
        public void UnpatchSelf()
        {
            foreach (var method in Hooks.Where(p => p.Value.Id == id).Select(p => p.Key).ToArray()) Hooks.Remove(method);
        }
    }
}
namespace SAIN.Components
{
    public class BotComponent : UnityEngine.Component { }
    public class BotManagerComponent : UnityEngine.Component
    {
        public static BotManagerComponent Instance { get; set; } = new();
        public readonly Dictionary<string, BotComponent> Bots = new();
        public bool ThrowLookup;
        public bool GetSAIN(EFT.BotOwner owner, out BotComponent bot)
        {
            if (ThrowLookup) throw new InvalidOperationException("uncertain registry");
            bot = Bots.GetValueOrDefault(owner.ProfileId);
            return bot != null;
        }
    }
}
namespace SAIN.Layers
{
    public abstract class BotAction(EFT.BotOwner owner)
    {
        public EFT.BotOwner BotOwner { get; } = owner;
        private SAIN.Components.BotComponent _bot;
        public int NativeStops;
        public bool ThrowOriginal;
        public void Cache(SAIN.Components.BotComponent bot) => _bot = bot;
        public void NativeStop()
        {
            NativeStops++;
            if (ThrowOriginal) throw new InvalidOperationException("original action failure");
            if (_bot == null)
            {
                SAIN.Components.BotManagerComponent.Instance.GetSAIN(BotOwner, out _bot);
                if (_bot == null) _bot = (SAIN.Components.BotComponent)BotOwner.GetComponent(typeof(SAIN.Components.BotComponent));
            }
            if (_bot == null) throw new NullReferenceException("missing SAIN component");
        }
        public virtual void Stop() { }
    }
}
namespace SAIN.Layers.Combat.Solo
{
    public sealed class DogFightAction(EFT.BotOwner owner) : SAIN.Layers.BotAction(owner)
    { public override void Stop() => NativeStop(); }
}
namespace SAIN.Layers.Combat.Solo.Cover
{
    public sealed class SeekCoverAction(EFT.BotOwner owner) : SAIN.Layers.BotAction(owner)
    { public override void Stop() => NativeStop(); }
}
