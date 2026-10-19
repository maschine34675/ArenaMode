using ArenaMode.Bots;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

// These stand-ins expose native boundaries only. The complete production
// SpawnFactionBot, SpawnInfected and async SpawnPair are compiled unchanged by Run.ps1.
namespace UnityEngine
{
    internal static class Time { internal static float time, realtimeSinceStartup; }
    internal struct Vector3 { }
}
namespace Comfort.Common
{
    internal static class Singleton<T>
    {
        internal static T Instance;
        internal static bool Instantiated => Instance != null;
    }
}
namespace EFT
{
    internal enum EPlayerSide { Savage, Usec, Bear }
    internal enum WildSpawnType { pmcUSEC, pmcBEAR, infectedAssault, infectedPmc, infectedCivil, infectedLaborant }
    internal static class WildSpawnTypeExtensions
    {
        internal static bool IsInfected(this WildSpawnType role)
            => role is WildSpawnType.infectedAssault or WildSpawnType.infectedPmc
                or WildSpawnType.infectedCivil or WildSpawnType.infectedLaborant;
    }
    internal enum BotDifficulty { normal, hard }
    internal sealed class BotSpawnParams { public string Id_spawn; }
    internal sealed class GetProfileDataParams
    {
        internal GetProfileDataParams(EPlayerSide side, WildSpawnType role, BotDifficulty difficulty, float time, BotSpawnParams parameters) { }
    }
    internal class AbstractGame { }
    internal sealed class LocalGame : AbstractGame, IBotGame
    {
        public BotsController BotsController { get; } = new();
    }
    internal sealed class GameWorld { }
    internal interface IBotGame { BotsController BotsController { get; } }
    internal sealed class BotsController { internal BotSpawner BotSpawner = new(); }
    internal sealed class Player { internal Vector3 Position = new(); internal string ProfileId = "test"; internal AiData AIData = new(); }
    internal sealed class AiData { internal BotOwner BotOwner; }
    internal sealed class Health { }
    internal sealed class BotOwner
    {
        internal Player GetPlayer = new();
        internal Profile Profile = new();
        internal string ProfileId => Profile.Id;
        internal Health HealthController = new();
        internal WildSpawnType Role { get => Profile.Info.Settings.Role; set => Profile.Info.Settings.Role = value; }
        internal bool Retired;
        internal BotOwner() { GetPlayer.AIData.BotOwner = this; }
    }
    internal sealed class BotZone { internal string NameZone = "test"; }
    internal sealed class Profile
    {
        internal string Id = "test";
        internal Info Info = new();
    }
    internal sealed class Info { internal Settings Settings = new(); }
    internal sealed class Settings { internal WildSpawnType Role = WildSpawnType.pmcUSEC; }
    internal sealed class BotCreationData
    {
        internal static TaskCompletionSource<BotCreationData> Pending;
        internal List<Profile> Profiles = new() { new Profile() };
        internal int Count => Profiles.Count;
        internal int Stops;
        internal void StopSpawn() { Stops++; Harness.Events.Add("stop"); }
        internal static Task<BotCreationData> Create(GetProfileDataParams profile, object creator, int count, object token)
        { Harness.Events.Add("create"); return Pending.Task; }
    }
    internal sealed class BotSpawner
    {
        internal object _botCreator = new();
        internal Action<BotOwner> Arrival;
        internal BotZone[] SpawnZones(bool value) => new[] { new BotZone() };
        internal void SpawnBotsInZoneOnPositions(List<ISpawnPoint> points, BotZone zone, BotCreationData data, Action<BotOwner> arrival)
        {
            Harness.Events.Add("native");
            Harness.NativeCalls++;
            Harness.NativeOwned = TeamDeathmatchSpawnDispatch.CanDispatch(this, data);
            if (Harness.RejectNative) { TeamDeathmatchSpawnDispatch.MarkRejected(this, data); return; }
            if (Harness.ThrowNative) throw new InvalidOperationException("native uncertainty");
            Arrival = arrival;
            if (!Harness.DelayArrival) arrival(Harness.Body);
        }
    }
}
namespace EFT.Game.Spawning
{
    internal interface ISpawnPoint { }
    internal sealed class SpawnPoint : ISpawnPoint { }
}
namespace ArenaMode
{
    internal static class Plugin { internal static Logger ModLogger = new(); }
    internal sealed class Logger
    {
        internal void LogDebug(string value) => Harness.Log.Add(value);
        internal void LogInfo(string value) => Harness.Log.Add(value);
        internal void LogWarning(string value) => Harness.Log.Add(value);
    }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive = true;
        internal static string ActiveMode = "teamdeathmatch";
        internal static bool IsExactActiveRaid(AbstractGame game, GameWorld world) => Harness.LeaseCurrent;
    }
}
namespace ArenaMode.Areas
{
    internal static class ArenaAreaRuntime
    {
        internal static bool HasTeamStarts = true;
        internal sealed class TeamSpawnLease : IDisposable
        {
            internal string PointId => "marker";
            internal Vector3 Position = new();
            internal bool Disposed;
            internal bool Accept(Player player) => Harness.AcceptPlacement;
            public void Dispose() { Disposed = true; }
        }
        internal static bool TryPlanTeamBot(BotZone[] zones, bool usec, bool reserve,
            out BotZone zone, out ISpawnPoint point, out TeamSpawnLease lease)
        {
            zone = zones[0]; point = new SpawnPoint(); lease = null;
            Harness.Events.Add(reserve ? "plan" : "preflight");
            if (reserve)
            {
                Harness.RealPlans++;
                if (!Harness.PlacementAvailable) return false;
                lease = Harness.LastLease = new TeamSpawnLease();
            }
            return Harness.PlacementAvailable;
        }
    }
}
namespace ArenaMode.Modes
{
    // Mode ownership/admission is an external boundary here. ZombieSoloChecks
    // and ZombieTeamChecks execute the real mode's solo/team policy.
    internal sealed class ZombieMode { internal bool IsSpawnAdmissionOpen = true; }
    internal sealed class ArenaMatch { internal object Mode; }
    internal static class ArenaMatchService { internal static ArenaMatch Current; }
}
namespace ArenaMode.Bots
{
    // External placement seam: this fixture executes BurstSpawnService's actual
    // dispatch/arrival lifecycle. The separate team-box suite owns geometry.
    internal static class ArenaTeamBoxRuntime
    {
        internal static bool IsRequired;
        internal static Lease LastLease;
        internal sealed class Lease : IDisposable
        {
            internal string PointId => "box-marker";
            internal Vector3 Position = new();
            internal bool Disposed;
            internal bool Accept(Player player) => Harness.AcceptPlacement;
            public void Dispose() => Disposed = true;
        }
        internal static bool TryPlanBot(IEnumerable<BotZone> zones, bool usec, bool reserve,
            out BotZone zone, out ISpawnPoint point, out Lease lease)
        {
            Harness.Events.Add(reserve ? "box-plan" : "box-preflight");
            zone = zones.First(); point = new SpawnPoint(); lease = null;
            if (!Harness.PlacementAvailable) return false;
            if (reserve) { Harness.RealPlans++; lease = LastLease = new Lease(); }
            return true;
        }
    }
    internal static class BurstSpawnPointSelector
    {
        internal static int CountUsable(BotZone zone, Vector3? position) => 1;
        internal static List<ISpawnPoint> PickPoints(BotZone zone, Vector3? position, int count, ref int cursor,
            out int preferred, out int distant, bool grouped = false)
        { preferred = 1; distant = 0; return new() { new SpawnPoint() }; }
    }
    internal static partial class BurstSpawnService
    {
        private const int BotsPerBurst = 2;
        private static int _operationGeneration = 0, _pointCursor;
        private static readonly List<float> RecentPmcInjections = new(), RecentScavInjections = new();
        private static bool TryCaptureSpawnLease(BotSpawner spawner, int generation,
            out LocalGame game, out GameWorld world, out BotsController controller, out int epoch)
        {
            game = Harness.Game; world = Harness.World; controller = game.BotsController; epoch = 1;
            return Harness.LeaseCurrent;
        }
        private static bool IsSpawnLeaseCurrent(int generation, LocalGame game, GameWorld world,
            int epoch, BotsController controller, BotSpawner spawner) => Harness.LeaseCurrent;
        private static void ReleaseReservedIfCurrent(List<float> ledger, int count, int generation)
        {
            Harness.ReservationReleases += count;
            ledger.RemoveRange(0, Math.Min(count, ledger.Count));
        }
        private static void OnBotSpawned(BotOwner bot, List<float> ledger, WildSpawnType role, bool includeLifecycleIdentity)
        {
            Harness.AcceptedBodies++;
            if (ledger.Count > 0) ledger.RemoveAt(0);
        }
        private static void RetireStaleSpawn(BotOwner bot, WildSpawnType role) { if (bot != null) bot.Retired = true; }
        private static void NotifyArrivalRejected(Action callback) => callback?.Invoke();
        private static void NotifyDispatchFinished(Action<bool> callback, bool handed) => callback?.Invoke(handed);
        private static void RetrySoon() { }
        private static bool HasRequestedFaction(BotOwner bot, WildSpawnType role) => bot.Role == role;
        private static BotZone PickZone(BotSpawner spawner, int count) => new();
        private static Vector3? GetPlayerPosition() => new Vector3();
        internal static int PendingScavReservations => RecentScavInjections.Count;
        internal static void ResetTestLedgers() { RecentPmcInjections.Clear(); RecentScavInjections.Clear(); }
        internal static void Direct(Func<bool> current, Func<bool> begin, Action<bool> finished)
            => SpawnPair(Harness.Game.BotsController.BotSpawner, new BotZone(), WildSpawnType.pmcUSEC,
                BotDifficulty.normal, true, 1, explicitFaction: true, requestStillCurrent: current,
                beginNativeDispatch: begin, dispatchFinished: finished);
    }
}
internal static class Harness
{
    internal static readonly List<string> Events = new(), Log = new();
    internal static bool LeaseCurrent, PlacementAvailable, AcceptPlacement, DelayArrival, RejectNative, ThrowNative, NativeOwned;
    internal static int NativeCalls, RealPlans, ReservationReleases, AcceptedBodies;
    internal static LocalGame Game;
    internal static GameWorld World;
    internal static BotOwner Body;
    internal static ArenaMode.Areas.ArenaAreaRuntime.TeamSpawnLease LastLease;
    internal static void Reset()
    {
        Events.Clear(); Log.Clear();
        LeaseCurrent = PlacementAvailable = AcceptPlacement = true;
        DelayArrival = RejectNative = ThrowNative = NativeOwned = false;
        NativeCalls = RealPlans = ReservationReleases = AcceptedBodies = 0; LastLease = null;
        ArenaTeamBoxRuntime.IsRequired = false; ArenaTeamBoxRuntime.LastLease = null;
        ArenaMode.Areas.ArenaAreaRuntime.HasTeamStarts = true;
        BurstSpawnService.ResetTestLedgers();
        ArenaMode.Modes.ArenaMatchService.Current = null;
        Game = new LocalGame(); World = new GameWorld(); Body = new BotOwner();
        Comfort.Common.Singleton<AbstractGame>.Instance = Game;
        Comfort.Common.Singleton<IBotGame>.Instance = Game;
        Comfort.Common.Singleton<GameWorld>.Instance = World;
        BotCreationData.Pending = new TaskCompletionSource<BotCreationData>();
        Time.time = Time.realtimeSinceStartup = 0f;
    }
}
