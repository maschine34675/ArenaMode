using System.Reflection;
using System.Reflection.Emit;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b)
            || (ReferenceEquals(a, null) && !ReferenceEquals(b, null) && b.Destroyed)
            || (ReferenceEquals(b, null) && !ReferenceEquals(a, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public float sqrMagnitude => x*x+y*y+z*z;
    }
    public class AudioClip : Object { }
    public class AudioSource : Object { public AudioClip clip; public bool isPlaying; }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
public enum EPhraseTrigger { OnMutter, OnEnemyConversation, OnFight, OnAgony, OnDeath, OnBeingHurt, Other }
public class TaggedClip { public UnityEngine.AudioClip Clip = new(); public float Length = 3; }
public class TagBank { }
public class BetterSource : UnityEngine.Object { public UnityEngine.AudioSource source1 = new(); }
public class InfectedLayersStrategy { }
public class CustomInfectedBrain : InfectedLayersStrategy { }
public class StandartBotBrain { public object BaseBrain = new InfectedLayersStrategy(); }
public class BotFollower { public bool HaveBoss; }
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
    public class BaseSpeaker { public bool Busy; public int OtherSubscriberCalls, ChainCalls; }
    public class Player : UnityEngine.Object
    {
        protected BetterSource _speechSource;
        public string ProfileId;
        public bool IsAI = true, IsYourPlayer;
        public Profile Profile = new();
        public Health HealthController = new();
        public AIData AIData = new();
        public BaseSpeaker Speaker = new();
        public GameWorld GameWorld;
        public UnityEngine.Vector3 Position;
        public bool NativeInRange = true, NativeCanPlay = true, ThrowOriginal;
        public int SourceCreations, GlobalPhraseEvents, OriginalCalls;
        public BetterSource ExistingSource => _speechSource;
        public BetterSource SpeechSource
        {
            get { if (_speechSource == null) { SourceCreations++; _speechSource = new(); } return _speechSource; }
            set => _speechSource = value;
        }
        public virtual void OnPhraseTold(EPhraseTrigger phrase, TaggedClip clip, TagBank bank, BaseSpeaker speaker)
        {
            OriginalCalls++;
            if (ThrowOriginal) throw new InvalidOperationException("native playback failed");
            var source = SpeechSource;
            if (NativeInRange && NativeCanPlay) { source.source1.clip = clip.Clip; source.source1.isPlaying = true; }
        }
        // Faithful ordering of Player.Say -> BaseSpeaker.Play -> OnPhraseTold.
        // Harness invokes the actual patch methods via its admission adapter.
        public bool Say(EPhraseTrigger phrase, TaggedClip clip = null)
        {
            clip ??= new();
            GlobalPhraseEvents++;
            Speaker.Busy = true;
            bool run = ArenaMode.Bots.ZombieVoiceBudget.BeforePhrase(this, phrase, clip, Speaker, out var state);
            if (run) OnPhraseTold(phrase, clip, new(), Speaker);
            ArenaMode.Bots.ZombieVoiceBudget.AfterPhrase(state);
            Speaker.OtherSubscriberCalls++;
            Speaker.ChainCalls++;
            return run;
        }
    }
    public class BotOwner : UnityEngine.Object
    {
        public Player GetPlayer;
        public bool IsDead, Follower;
        public EBotState BotState = EBotState.Active;
        public StandartBotBrain Brain = new();
        public BotFollower BotFollower = new();
        public bool IsFollower() => Follower;
    }
    public class GameWorld : UnityEngine.Object
    {
        public Player MainPlayer;
        public Dictionary<string, Player> Alive = new();
        public bool ThrowLookup;
        public Player GetAlivePlayerByProfileID(string id)
        {
            if (ThrowLookup) throw new InvalidOperationException("native lookup failed");
            return Alive.TryGetValue(id, out var body) ? body : null;
        }
    }
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
namespace ArenaMode.Compat
{
    public static class PitFireTeamCompat
    {
        public static HashSet<EFT.Player> Team = new();
        public static bool IsTeamPlayer(EFT.Player player) => Team.Contains(player);
    }
}
namespace ArenaMode.Config
{
    public class Setting { public bool Value = true; }
    public static class Settings { public static Setting ZombieVoiceBudgetEnabled = new(); }
}
namespace ArenaMode.Bots
{
    public static class ZombieNativeEventBridge
    { public static bool IsActiveZombieRaid() => Session.ArenaSession.IsActive && Session.ArenaSession.ActiveMode == "zombie"; }
}
namespace ArenaMode
{
    public static class Plugin { public static Log ModLogger = new(); }
    public class Log
    {
        public List<string> Infos = new(), Warnings = new();
        public void LogInfo(string message) => Infos.Add(message); public void LogDebug(string message) => Infos.Add(message);
        public void LogWarning(string message) => Warnings.Add(message);
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref F FieldRef<T,F>(T instance);
        public static FieldRef<T,F> FieldRefAccess<T,F>(string name)
        {
            var field = typeof(T).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(name);
            var method = new DynamicMethod("FieldAccess", typeof(F).MakeByRefType(), new[] { typeof(T) }, typeof(T), true);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, field); il.Emit(OpCodes.Ret);
            return (FieldRef<T,F>)method.CreateDelegate(typeof(FieldRef<T,F>));
        }
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] args)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, args);
    }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefixAttribute : Attribute { }
    public class PatchPostfixAttribute : Attribute { }
    public abstract class ModulePatch
    {
        public static List<MethodBase> Installed = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() => Installed.Add(GetTargetMethod() ?? throw new InvalidOperationException("missing method"));
    }
}
