// This is a deliberately small behavioral model, NOT EFT/Unity runtime proof.
// Native method ordering is modeled only where the linked production code uses
// it: actual Harmony prefix methods run through the dispatcher below; native
// groups carry separate Player/BotOwner keys; callbacks can be controlled by tests.
using System.Reflection;
using ArenaMode.Combat;
using Comfort.Common;
using EFT;

namespace UnityEngine
{
    public static class Time { public static float time; }
    public readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }
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
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum WildSpawnType { pmcUSEC, pmcBEAR, assault }
    public enum EBotState { PreActive, Active, NonActive, ActiveFail, Disposed }
    public enum EDamageType { Undefined, Bullet, Explosion }
    public enum EEnemyPartVisibleType { Sence }
    public enum EBotEnemyCause { initial, byKill, addPlayer, bossKillArena, AddEnemyToAllGroupsInBotZone }
    public enum BotDifficulty { normal }
    public class AbstractGame { }
    public sealed class LocalGame : AbstractGame, IBotGame
    { public BotsController BotsController { get; } = new(); }
    public interface IBotGame { BotsController BotsController { get; } }
    public sealed class BotsController { public BotSpawner BotSpawner = new(); }
    public sealed class BotSpawner
    {
        public BotStorage _bots = new();
        public int MaxBots;
        public void SetMaxBots(int value) => MaxBots = value;
    }
    public sealed class BotStorage { public List<BotOwner> BotOwners = new(); }
    public sealed class GameWorld
    {
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Players = new(StringComparer.Ordinal);
        public Player GetAlivePlayerByProfileID(string id) => Players.GetValueOrDefault(id);
    }
    public sealed class Profile { public ProfileInfo Info = new(); }
    public sealed class ProfileInfo
    { public EPlayerSide Side; public ProfileSettings Settings = new(); }
    public sealed class ProfileSettings { public WildSpawnType Role; }
    public sealed class AIData { public BotOwner BotOwner; }
    public interface IPlayer
    {
        string ProfileId { get; }
        bool IsAI { get; }
        Health HealthController { get; }
    }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; }
        public bool IsAI { get; set; }
        public Profile Profile = new();
        public AIData AIData = new();
        public Health HealthController { get; set; }
        public Health ActiveHealthController => HealthController;
        public UnityEngine.Vector3 Position;
    }
    public sealed class Health
    {
        public bool IsAlive = true, SuppressKill, ThrowOnKill;
        public int KillCalls;
        public Player Player;
        public Action OnKilled;
        public void Kill(EDamageType type)
        {
            KillCalls++;
            Model.Events.Add("kill:" + Player.ProfileId);
            if (!ArenaAdministrativeDeathGate.IsActive(Player.ProfileId))
                throw new InvalidOperationException("retirement must be administrative");
            if (ThrowOnKill) throw new InvalidOperationException("modeled native Kill failure");
            if (!SuppressKill)
            {
                IsAlive = false;
                OnKilled?.Invoke();
            }
        }
    }
    public sealed class BotOwner : IPlayer
    {
        public Player GetPlayer;
        public string ProfileId => GetPlayer.ProfileId;
        public Profile Profile => GetPlayer.Profile;
        public bool IsAI => true;
        public Health HealthController => GetPlayer.HealthController;
        public EBotState BotState = EBotState.PreActive;
        public UnityEngine.Vector3 Position => GetPlayer.Position;
        public BotsGroup BotsGroup;
        public BotMemory Memory;
        public BotEnemiesController EnemiesController = new();
        public object GetComponent(Type type) => null; // SAIN optional path is absent here.
    }
    public sealed class BotMemory
    {
        public BotOwner _owner;
        private EnemyInfo _goal;
        public EnemyInfo GoalEnemy
        {
            get => _goal;
            set
            {
                object result = null;
                if (NativeHooks.Prefix(GetType().GetProperty(nameof(GoalEnemy)).SetMethod,
                    this, new object[] { value }, ref result)) _goal = value;
            }
        }
        public EnemyInfo LastEnemy;
        public bool IsPeace = true;
        public void AddEnemy(IPlayer enemy, BotGroupEnemyInfo groupInfo, bool onActivation)
        {
            object result = null;
            if (!NativeHooks.Prefix((MethodInfo)MethodBase.GetCurrentMethod(), this,
                new object[] { enemy, groupInfo, onActivation }, ref result)) return;
            _owner.EnemiesController.EnemyInfos.TryAdd(enemy, new EnemyInfo
            { Person = enemy, GroupInfo = groupInfo, Owner = _owner, GroupOwner = _owner.BotsGroup });
        }
        public void DeleteInfoAboutEnemy(IPlayer enemy)
        {
            _owner.EnemiesController.EnemyInfos.Remove(enemy);
            if (ReferenceEquals(_goal?.Person, enemy)) GoalEnemy = null;
            if (ReferenceEquals(LastEnemy?.Person, enemy)) LastEnemy = null;
        }
    }
    public sealed class BotDeadBodyWork
    {
        public BotOwner _owner;
        public int NativeStarts;
        public void StartLook()
        {
            object result = null;
            if (NativeHooks.Prefix((MethodInfo)MethodBase.GetCurrentMethod(), this, Array.Empty<object>(), ref result))
                NativeStarts++;
        }
    }
}

public sealed class BotsGroup
{
    public readonly List<BotOwner> Members = new();
    public BotOwner _initialBot;
    public IBotGame BotGame;
    public EPlayerSide Side;
    public WildSpawnType InitialBotType;
    public readonly Dictionary<IPlayer, BotGroupEnemyInfo> Enemies = new();
    public readonly Dictionary<IPlayer, BotGroupEnemyInfo> Neutrals = new();
    public readonly List<IPlayer> Allies = new();
    public int MembersCount => Members.Count;
    public BotOwner Member(int index) => Members[index];
    public bool AddEnemy(IPlayer person, EBotEnemyCause cause)
    {
        object result = false;
        if (!NativeHooks.Prefix((MethodInfo)MethodBase.GetCurrentMethod(), this, new object[] { person, cause }, ref result))
            return (bool)result;
        var body = person as Player ?? (person as BotOwner)?.GetPlayer;
        if (!Enemies.TryGetValue(person, out var info))
        {
            info = new BotGroupEnemyInfo { Player = body, _botGroup = this };
            Enemies.Add(person, info);
            Allies.Remove(person);
            Neutrals.Remove(person);
        }
        foreach (var bot in Members) bot.Memory.AddEnemy(person, info, false);
        return true;
    }
    public void RemoveEnemy(IPlayer person, EBotEnemyCause cause = EBotEnemyCause.initial)
    {
        Enemies.Remove(person);
        Neutrals.Remove(person);
        foreach (var bot in Members) bot.Memory.DeleteInfoAboutEnemy(person);
    }
    public void AddAlly(Player player) { if (!Allies.Contains(player)) Allies.Add(player); }
    public void ReportAboutEnemy(IPlayer player, EEnemyPartVisibleType sense, BotOwner reporter)
    {
        if (!Enemies.TryGetValue(player, out var info)) throw new InvalidOperationException("awareness requires exact group key");
        info.IsHaveSeen = true;
        Model.Events.Add("awareness:" + reporter.ProfileId + ":" + player.ProfileId);
    }
}
public sealed class BotGroupEnemyInfo
{
    public Player Player;
    public BotsGroup _botGroup;
    public bool IsLastPositionChecked = true, IsHaveSeen;
}
public sealed class EnemyInfo
{
    public IPlayer Person;
    public BotGroupEnemyInfo GroupInfo;
    public BotOwner Owner;
    public BotsGroup GroupOwner;
    public bool IgnoreUntilAggression = true;
}
public sealed class BotEnemiesController { public readonly Dictionary<IPlayer, EnemyInfo> EnemyInfos = new(); }

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefix : Attribute { }
    public static class AccessTools
    {
        public static MethodBase Method(Type type, string name, Type[] arguments = null)
            => arguments == null ? type.GetMethod(name) : type.GetMethod(name, arguments);
        public static MethodBase PropertySetter(Type type, string name) => type.GetProperty(name).SetMethod;
    }
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(MethodBase method, HarmonyMethod prefix) => throw new InvalidOperationException("SAIN cannot be loaded in modeled engine");
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            var prefix = GetType().GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.GetCustomAttribute<HarmonyLib.PatchPrefix>() != null);
            NativeHooks.Register((MethodInfo)GetTargetMethod(), prefix);
        }
    }
}
internal static class NativeHooks
{
    private static readonly Dictionary<MethodInfo, MethodInfo> Methods = new();
    internal static void Register(MethodInfo method, MethodInfo prefix) => Methods[method] = prefix;
    internal static bool Prefix(MethodInfo method, object instance, object[] values, ref object result)
    {
        if (!Methods.TryGetValue(method, out var prefix)) return true;
        var signature = method.GetParameters();
        var parameters = prefix.GetParameters();
        object originalResult = result;
        var args = parameters.Select(parameter => parameter.Name switch
        {
            "__instance" => instance,
            "__result" => originalResult,
            _ => values[Array.FindIndex(signature, native => native.Name == parameter.Name)]
        }).ToArray();
        object allowed;
        try { allowed = prefix.Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
        int output = Array.FindIndex(parameters, parameter => parameter.Name == "__result");
        if (output >= 0) result = args[output];
        return allowed is not bool skip || skip;
    }
}

namespace ArenaMode
{
    internal static class Plugin
    {
        internal const string PluginGuid = "test.arena";
        internal static readonly ModelLogger ModLogger = new();
    }
    internal sealed class ModelLogger
    {
        internal readonly List<string> Lines = new();
        internal void LogInfo(object value) => Lines.Add(value.ToString());
        internal void LogWarning(object value) => Lines.Add(value.ToString());
        internal void LogError(object value) => Lines.Add(value.ToString());
    }
}
namespace ArenaMode.Config
{
    internal sealed class Entry<T> { internal T Value; internal Entry(T value) => Value = value; }
    internal static partial class Settings
    {
        internal static readonly Entry<int> TeamDeathmatchSize = new(5);
        internal static readonly Entry<bool> NoCorpseLooting = new(true);
    }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static string ActiveMode;
        internal static AbstractGame OwnedGame;
        internal static GameWorld OwnedWorld;
        internal static bool IsExactActiveRaid(AbstractGame game, GameWorld world)
            => IsActive && ReferenceEquals(game, OwnedGame) && ReferenceEquals(world, OwnedWorld);
    }
}
namespace ArenaMode.Compat { internal static class PitFireTeamCompat { internal static bool HasFrozenTeamRoster; } }
namespace ArenaMode.Respawn { internal static class ArenaRespawnDriver { internal static bool IsRespawnInProgress; } }
namespace ArenaMode.Effects
{
    internal static class ArenaTracerPatches { internal static void ResetDiagnostics() { } }
    internal static class ArenaToasts { internal static void Show(string text) => Model.Events.Add("toast:" + text); }
}
namespace ArenaMode.Modes
{
#if !TEAM_GIVE_UP_CHECKS
    public sealed class ArenaMatch
    {
        public IArenaMode Mode;
        public ArenaScoreboard Scoreboard;
        internal readonly List<ArenaAnnouncerCue> Cues = new();
        public void ReportAnnouncerCue(ArenaAnnouncerCue cue) => Cues.Add(cue);
    }
    public enum ArenaAnnouncerCue { RoundStart }
#endif
    public static class ArenaModeCatalog { public const string TeamDeathmatchId = "teamdeathmatch"; }
    public static class ArenaMatchService
    {
        public static ArenaMatch Current;
        public static ArenaScoreboardActor SnapshotActor(Player player)
            => new(player.ProfileId, player.ProfileId, player.IsAI ? "PMC" : "You", player.HealthController.IsAlive);
    }
}
namespace ArenaMode.Bots
{
    internal static class ArenaPressure { internal static BotDifficulty DifficultyFor(bool pmc) => BotDifficulty.normal; }
    internal static class ArenaHostility
    {
        internal static bool CountsAsAlly(BotsGroup group, IPlayer player) => group?.Allies.Contains(player) == true;
    }
    internal static class BurstSpawnService
    {
        // Deliberate test seam: mode timing is real, profile/native creation is
        // controlled externally. The full production burst service is reviewed
        // against its native counter/callback ordering separately.
        internal static readonly List<SpawnRequest> Requests = new();
        internal static Action<SpawnRequest> OnRequest;
        internal static int SpawnFactionBot(WildSpawnType role, BotDifficulty difficulty,
            Action<BotOwner> observer, Func<bool> requestStillCurrent,
            Action<bool> dispatchFinished, Action arrivalRejected)
        {
            var request = new SpawnRequest(role, observer, requestStillCurrent, dispatchFinished, arrivalRejected);
            Requests.Add(request);
            Model.Events.Add("request:" + role);
            OnRequest?.Invoke(request);
            return request.RejectedBeforePlan ? 0 : 1;
        }
    }
}
internal sealed class SpawnRequest
{
    internal readonly WildSpawnType Role;
    internal readonly Action<BotOwner> Observer;
    internal readonly Func<bool> Current;
    internal readonly Action<bool> DispatchFinished;
    internal readonly Action Rejected;
    internal bool RejectedBeforePlan;
    internal bool Delivered;
    internal readonly GameWorld World = Singleton<GameWorld>.Instance;
    internal readonly LocalGame Game = Singleton<AbstractGame>.Instance as LocalGame;
    internal SpawnRequest(WildSpawnType role, Action<BotOwner> observer, Func<bool> current,
        Action<bool> dispatchFinished, Action rejected)
    { Role = role; Observer = observer; Current = current; DispatchFinished = dispatchFinished; Rejected = rejected; }
}
internal static class Model
{
    internal static readonly List<string> Events = new();
    internal static int NextId;
    internal static Player Body(string id, EPlayerSide side, bool ai = false,
        GameWorld world = null, LocalGame game = null)
    {
        world ??= Singleton<GameWorld>.Instance;
        game ??= Singleton<AbstractGame>.Instance as LocalGame;
        var body = new Player { ProfileId = id, IsAI = ai, Position = new UnityEngine.Vector3(++NextId * 3) };
        body.Profile.Info.Side = side;
        body.Profile.Info.Settings.Role = side == EPlayerSide.Usec ? WildSpawnType.pmcUSEC : WildSpawnType.pmcBEAR;
        body.HealthController = new Health { Player = body };
        world.Players[id] = body;
        if (!ai) world.MainPlayer = body;
        else
        {
            var owner = new BotOwner { GetPlayer = body };
            body.AIData.BotOwner = owner;
            owner.Memory = new BotMemory { _owner = owner };
            owner.BotsGroup = new BotsGroup
            { _initialBot = owner, Side = side, InitialBotType = body.Profile.Info.Settings.Role, BotGame = game };
            owner.BotsGroup.Members.Add(owner);
            game.BotsController.BotSpawner._bots.BotOwners.Add(owner);
        }
        return body;
    }
}
