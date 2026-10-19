using ArenaMode.Bots;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using System.Reflection;
using UnityEngine;

// Controlled boundaries only: roster, policy, dispatch scopes, Harmony prefix
// bodies and managed async Burst method are compiled from production sources.
namespace UnityEngine
{
    internal static class Time { internal static float time, realtimeSinceStartup; }
    internal struct Vector3 { }
}
namespace Comfort.Common
{
    internal static class Singleton<T> { internal static T Instance; internal static bool Instantiated => Instance != null; }
}
namespace EFT
{
    internal enum WildSpawnType { pmcUSEC, pmcBEAR, assault, bossTagillaAgro, bossKillaAgro }
    internal enum EPlayerSide { Usec, Bear, Savage }
    internal enum BotDifficulty { hard }
    internal enum EDamageType { Undefined }
    internal enum GameStatus { Started, Stopping }
    internal sealed class BotSpawnParams { public string Id_spawn; }
    internal sealed class GetProfileDataParams
    {
        internal readonly WildSpawnType Role;
        internal GetProfileDataParams(EPlayerSide side, WildSpawnType role, BotDifficulty difficulty, float at, BotSpawnParams p) { Role = role; }
    }
    internal class AbstractGame { }
    internal sealed class LocalGame : AbstractGame, IBotGame
    { internal GameStatus Status = GameStatus.Started; public BotsController BotsController { get; } = new(); }
    internal interface IBotGame { BotsController BotsController { get; } }
    internal sealed class BotsController { internal BotSpawner BotSpawner = new(); }
    internal sealed class GameWorld { internal string LocationId = "factory4_day"; internal Player MainPlayer = new(); }
    internal sealed class Profile
    {
        internal Info Info = new();
        internal string Id = Guid.NewGuid().ToString();
        internal static Profile For(WildSpawnType role)
            => new() { Info = new() { Settings = new() { Role = role }, Side = role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec
                : role == WildSpawnType.pmcBEAR ? EPlayerSide.Bear : EPlayerSide.Savage } };
    }
    internal sealed class Info { internal ProfileSettings Settings = new(); internal EPlayerSide Side; }
    internal sealed class ProfileSettings { internal WildSpawnType Role; }
    internal sealed class Health
    {
        internal bool IsAlive = true;
        internal void Kill(EDamageType damage) { if (!Harness.FailRetirement) IsAlive = false; }
    }
    internal sealed class Player
    {
        internal Profile Profile = new(); internal string ProfileId => Profile.Id;
        internal EPlayerSide Side => Profile.Info.Side;
        internal Health HealthController = new();
        internal Health ActiveHealthController => HealthController;
    }
    internal sealed class BotOwner
    {
        internal Boss Boss = new();
        internal Player GetPlayer;
        internal Profile Profile => GetPlayer.Profile;
        internal string ProfileId => GetPlayer.ProfileId;
        internal Health HealthController => GetPlayer.HealthController;
        internal string BotState = "PreActive";
        internal BotOwner(Profile profile) { GetPlayer = new Player { Profile = profile }; }
    }
    internal sealed class Boss
    {
        internal bool IamBoss;
        internal int Followers;
        internal void SetBoss(int followers) { if (Harness.FailBossInitialization) throw new InvalidOperationException("boss init"); IamBoss = true; Followers = followers; }
    }
    internal sealed class BotZone { internal string NameZone = "test"; }
    internal sealed class BotsGroup { }
    internal sealed class Registry { internal readonly List<BotOwner> BotOwners = new(); }
    internal sealed class BotCreationData
    {
        internal readonly List<Profile> Profiles = new();
        internal int Count => Profiles.Count;
        internal int Stops;
        internal void StopSpawn() { Stops++; }
        internal static Task<BotCreationData> Create(GetProfileDataParams args, object creator, int count, object token)
        {
            var data = new BotCreationData();
            for (int i = 0; i < Harness.ProfileCount; i++) data.Profiles.Add(Profile.For(args.Role));
            Harness.LastData = data;
            var pending = new TaskCompletionSource<BotCreationData>();
            Harness.Preparations.Enqueue((pending, data));
            if (!Harness.DelayPreparation) { Harness.Preparations.Dequeue(); pending.SetResult(data); }
            return pending.Task;
        }
    }
    internal sealed class BotSpawner
    {
        internal BotCreatorClient _botCreator = new();
        internal Registry _bots = new();
        internal IEnumerable<BotZone> SpawnZones(bool v) => new[] { new BotZone() };
        internal void SpawnBotsInZoneOnPositions(List<ISpawnPoint> points, BotZone botZone, BotCreationData data, Action<BotOwner> callback)
        {
            if (!Harness.Call("ArenaBotSpawnDispatchPatch", this, points, botZone, data)) return;
            Harness.NativeCalls++;
            if (Harness.ThrowNative) throw new InvalidOperationException("uncertain native handoff");
            if (!Harness.Call("ArenaBotSpawnSoloActivationPatch", _botCreator, data, null)) return;
            if (!Harness.Call("ArenaBotSpawnSoloCreationPatch", _botCreator, data.Profiles[0], null)) return;
            var body = new BotOwner(data.Profiles[0]);
            Harness.Arrivals.Add((body, callback));
            if (!Harness.DelayRegistration) _bots.BotOwners.Add(body);
            if (!Harness.DelayArrival) callback(body);
        }
    }
    internal sealed class SpawnSystem { internal void SelectAISpawnPoints() { } }
    internal enum ActionIfNotEnoughPoints { None }
}
internal sealed class PositionNote { }
internal sealed class BotCreatorClient
{
    internal Task ActivateBot(BotCreationData data, BotZone z, bool g, Func<BotOwner, BotZone, BotsGroup> group, Action<BotOwner> done, CancellationToken token) => Task.CompletedTask;
    internal Task ActivateBot(Profile p, PositionNote pos, BotZone z, bool g, Func<BotOwner, BotZone, BotsGroup> group, Action<BotOwner> done, CancellationToken token) => Task.CompletedTask;
    internal Task CreateBot(Profile p, PositionNote pos, Action<BotOwner> done, bool local, CancellationToken token) => Task.CompletedTask;
}
namespace EFT.Game.Spawning { internal interface ISpawnPoint { } internal sealed class Point : ISpawnPoint { } }
namespace HarmonyLib
{
    internal static class Priority { internal const int First = 800, Last = 0; }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPriority : Attribute { internal HarmonyPriority(int i) { } }
    internal static class AccessTools
    {
        internal static MethodInfo Method(Type type, string name, Type[] types = null)
            => types == null ? type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault(m => m.Name == name)
            : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, types, null);
    }
}
namespace SPT.Reflection.Patching
{
    internal abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); internal void Enable() { } }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class PatchPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class PatchPostfix : Attribute { }
}
namespace ArenaMode
{
    internal static class Plugin { internal static Logger ModLogger = new(); }
    internal sealed class Logger
    {
        internal void LogInfo(string text) => Harness.Log.Add(text); internal void LogDebug(string text) => Harness.Log.Add(text);
        internal void LogWarning(string text) => Harness.Log.Add(text);
        internal void LogError(string text) => Harness.Log.Add(text);
    }
}
namespace ArenaMode.Config
{
    internal static class Settings { internal static Setting PmcFloor = new(); }
    internal sealed class Setting { internal int Value = 4; }
}
namespace ArenaMode.Shared
{
    internal static class ArenaParticipantPolicy { internal static bool IsSolo(string mode) => mode == "deathmatch" || mode == "gungame"; }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive = true, ArmedForNextRaid, ParticipantBosses;
        internal static int? Participants = 4;
        internal static string ActiveMode = "deathmatch", ArmedMode = "deathmatch", ArmedMap = "factory4_day";
        internal static bool IsExactActiveRaid(AbstractGame game, GameWorld world) => ReferenceEquals(game, Harness.Game) && ReferenceEquals(world, Harness.World);
    }
}
namespace ArenaMode.Modes
{
    internal static class ArenaMatchService { internal static Match Current = new(); internal static bool TeamStartAbortPending; }
    internal sealed class Match { internal Mode Mode = new(); }
    internal sealed class Mode { internal bool ControlsOwnSpawns; }
}
namespace ArenaMode.Areas { internal static class ArenaAreaRuntime { internal static bool ShouldBlockLoadingBotDispatch() => false; } }
namespace ArenaMode.Combat
{
    internal static class ArenaAdministrativeDeathGate { internal static IDisposable Enter(string id) => new Gate(); }
    internal sealed class Gate : IDisposable { public void Dispose() { } }
}
namespace ArenaMode.Bots
{
    internal static class ArenaPressure { internal static int PmcFloor(int value) => value; internal static BotDifficulty DifficultyFor(bool pmc) => BotDifficulty.hard; }
    internal static class ArenaBotSpawnFilter
    {
        internal static bool CanDispatch(BotZone z, List<ISpawnPoint> p) => !Harness.RejectPlacement;
        internal static bool IsExcludedZone(BotZone z) => false;
        internal static IEnumerable<BotZone> FilterZones(IEnumerable<BotZone> z) => z;
        internal static List<ISpawnPoint> FilterBotPoints(List<ISpawnPoint> p) => p;
    }
    internal sealed class ArenaTeamSpawnPointsPatch { internal void Enable() { } }
    internal static class TeamDeathmatchSpawnDispatch
    { internal static bool CanDispatch(BotSpawner b, BotCreationData d) => true; internal static void MarkRejected(BotSpawner b, BotCreationData d) { } }
    internal static class BurstSpawnPointSelector
    {
        internal static List<ISpawnPoint> PickPoints(BotZone zone, Vector3? position, int count, ref int cursor,
            out int preferred, out int distant, bool grouped)
        { preferred = 1; distant = 0; return Harness.PlacementAvailable ? new() { new Point() } : new(); }
    }
    internal static partial class BurstSpawnService
    {
        private static int _pointCursor;
        private static BotZone PickZone(BotSpawner spawner, int count) => Harness.PlacementAvailable ? new BotZone() : null;
        private static Vector3? GetPlayerPosition() => new Vector3();
        private static void OnBotSpawned(BotOwner bot, List<float> ledger, WildSpawnType role, bool includeLifecycleIdentity) { Harness.Observed++; }
        private static void NotifyDispatchFinished(Action<bool> callback, bool handed) => callback?.Invoke(handed);
        internal static void Reset() { _pointCursor = 0; }
    }
}
internal static class Harness
{
    internal static LocalGame Game;
    internal static GameWorld World;
    internal static readonly List<string> Log = new();
    internal static readonly Queue<(TaskCompletionSource<BotCreationData> Task, BotCreationData Data)> Preparations = new();
    internal static readonly List<(BotOwner Body, Action<BotOwner> Callback)> Arrivals = new();
    internal static bool DelayPreparation, DelayArrival, DelayRegistration, FailRetirement, ThrowNative, RejectPlacement, PlacementAvailable, FailBossInitialization;
    internal static int ProfileCount, NativeCalls, Observed;
    internal static BotCreationData LastData;
    internal static BotSpawner Spawner => Game.BotsController.BotSpawner;
    internal static bool Call(string nested, params object[] args)
    {
        var type = typeof(ArenaBotSpawnPatches).GetNestedType(nested, BindingFlags.NonPublic);
        return (bool)type.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
    internal static void Reset(int participants = 4, bool boss = false, string mode = "deathmatch")
    {
        ArenaSoloPopulation.Reset();
        Game = new(); World = new();
        Singleton<AbstractGame>.Instance = Game; Singleton<IBotGame>.Instance = Game; Singleton<GameWorld>.Instance = World;
        ArenaMode.Session.ArenaSession.IsActive = true;
        ArenaMode.Session.ArenaSession.ArmedForNextRaid = false;
        ArenaMode.Session.ArenaSession.ActiveMode = mode;
        ArenaMode.Session.ArenaSession.Participants = participants;
        ArenaMode.Session.ArenaSession.ParticipantBosses = boss;
        ArenaMode.Modes.ArenaMatchService.Current = new();
        ArenaMode.Modes.ArenaMatchService.TeamStartAbortPending = false;
        ArenaMode.Config.Settings.PmcFloor.Value = 4;
        DelayPreparation = DelayArrival = DelayRegistration = FailRetirement = ThrowNative = RejectPlacement = false;
        FailBossInitialization = false;
        PlacementAvailable = true; ProfileCount = 1; NativeCalls = Observed = 0;
        Log.Clear(); Arrivals.Clear(); Preparations.Clear(); LastData = null;
        Time.time = Time.realtimeSinceStartup = 0f;
    }
    internal static void Tick(float dt = 1f) { Time.time += dt; Time.realtimeSinceStartup += dt; ArenaSoloPopulation.Tick(); }
    internal static void CompleteOne() { var pending = Preparations.Dequeue(); pending.Task.SetResult(pending.Data); }
    internal static int Alive => Spawner._bots.BotOwners.Count(b => b.HealthController.IsAlive);
}
