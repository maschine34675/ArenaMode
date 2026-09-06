// Independent behavior seams. No PFT, EFT or SAIN implementation is copied.
using EFT;
using Comfort.Common;
namespace Comfort.Common
{
    public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum GameStatus { Started, Stopping, Stopped }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class LocalGame : AbstractGame { }
    public enum EBotState { Active, PreActive, NonActive, Disposed }
    public interface IPlayer { string ProfileId { get; } HealthController HealthController { get; } }
    public class HealthController { public bool IsAlive = true; }
    public class AIData { public BotOwner BotOwner; }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; }
        public HealthController HealthController { get; } = new();
        public AIData AIData = new();
    }
    public class GameWorld
    {
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Alive = new(), Ever = new();
        public readonly List<IPlayer> RegisteredPlayers = new();
        public Player GetAlivePlayerByProfileID(string id) => Alive.GetValueOrDefault(id);
        public Player GetEverExistedPlayerByID(string id) => Ever.GetValueOrDefault(id);
    }
    public class BotOwner : IPlayer
    {
        public string ProfileId { get; set; }
        public Player GetPlayer;
        public HealthController HealthController => GetPlayer?.HealthController;
        public bool IsDead => HealthController?.IsAlive != true;
        public EBotState BotState = EBotState.Active;
        public BotsGroup BotsGroup = new();
        public BotMemory Memory;
        public BotEnemiesController EnemiesController = new();
        public object Mover = new();
        public BotMedecine Medecine = new();
        public BotWeapons WeaponManager = new();
        public DoorOpener DoorOpener = new();
        public Danger BewareGrenade = new(), BewareBTR = new(), ArtilleryDangerPlace = new();
        public Flash FlashGrenade = new();
        public bool Follower;
    }
    public class BotsGroup { public Dictionary<IPlayer, BotGroupEnemyInfo> Enemies = new(); }
    public class BotGroupEnemyInfo { public Player Player; public BotsGroup _botGroup; }
    public class BotEnemiesController { public Dictionary<IPlayer, EnemyInfo> EnemyInfos = new(); }
    public class EnemyInfo
    {
        public IPlayer Person;
        public BotOwner Owner;
        public BotsGroup GroupOwner;
        public BotGroupEnemyInfo GroupInfo;
        public bool IsVisible, CanShoot;
    }
    public class BotMemory
    {
        public EnemyInfo Value;
        public BotOwner Owner;
        public int SetCalls;
        public bool MissionVeto, ThrowOnSet;
        public Action OnSet;
        public readonly List<string> Reasons = new();
        public EnemyInfo GoalEnemy
        {
            get => Value;
            set
            {
                SetCalls++;
                var reason = pitTeam.Modules.FollowerGoalEnemyTracker.CurrentReason;
                Reasons.Add(reason);
                // The seam exposes the two independent decisions under test:
                // a deliberate scoped request versus a still-authoritative veto.
                if (Owner.Follower && (reason == "unscopedSetter" || MissionVeto)) return;
                if (ThrowOnSet) throw new InvalidOperationException("native setter failed");
                var world = Singleton<GameWorld>.Instance;
                if (Value != null && world.GetEverExistedPlayerByID(Value.Person.ProfileId) == null)
                    throw new InvalidOperationException("previous subscription body missing");
                if (value != null && world.GetAlivePlayerByProfileID(value.Person.ProfileId) == null)
                    throw new InvalidOperationException("next subscription body missing");
                Value = value;
                OnSet?.Invoke();
            }
        }
    }
    public class UsingOperation { public bool Using; }
    public class BotMedecine { public UsingOperation FirstAid = new(), SurgicalKit = new(), Stimulators = new(); }
    public class Reload { public bool Reloading; }
    public class Grenades { public bool ThrowindNow; }
    public class BotWeapons { public Reload Reload = new(); public Grenades Grenades = new(); }
    public class DoorOpener { public bool Interacting; }
    public class Danger { public bool ShallRunAway() => false; }
    public class Flash { public bool IsFlashed; }
}
namespace ArenaMode.Compat
{
    internal static class PitFireTeamCompat { public static bool TeamArenaActive = true; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static AbstractGame Game;
        public static GameWorld World;
        public static bool IsExactActiveRaid(AbstractGame game, GameWorld world)
            => IsActive && game != null && world != null && ReferenceEquals(Game, game) && ReferenceEquals(World, world);
    }
}
namespace ArenaMode.Modes
{
    public sealed partial class TacticalMode
    {
        internal const int TeamCompanionCount = 4, TeamEnemyCount = 5;
        public enum Phase { Live, Staging, Ended }
        internal bool _teamArena = true;
        internal Phase _phase = Phase.Live;
        internal bool IsComplete;
        internal int _teamRoundEpoch = 3;
        internal LocalGame _teamGame;
        internal ArenaMatch _match;
        internal Player _boundLocalPlayer;
        internal string _boundLocalProfileId;
        internal BotOwner[] _boundTeamFollowers, _boundTeamEnemies;
        internal string[] _boundTeamFollowerProfileIds, _boundTeamEnemyProfileIds;
    }
    public class ArenaMatch { public object Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
}
namespace ArenaMode.Bots
{
    public readonly record struct TeamArenaPursuitLease(string SourceProfileId, string TargetProfileId, int Epoch, long Generation);
    public static class TeamArenaPursuitLeaseBridge
    {
        public static TeamArenaPursuitLease Current;
        public static bool HasExactTeamArenaContext() => ArenaMode.Session.ArenaSession.IsActive;
        public static bool TryGet(string id, out TeamArenaPursuitLease lease)
        { lease = Current; return id != null && id == Current.SourceProfileId; }
        public static bool TryGet(string id, int epoch, long generation, out TeamArenaPursuitLease lease)
            => TryGet(id, out lease) && epoch == lease.Epoch && generation == lease.Generation;
    }
}
namespace pitTeam.Modules
{
    // Test-authored reflection target. It models a scoped token, not PFT's AI.
    public static class FollowerGoalEnemyTracker
    {
        [ThreadStatic] public static string Token;
        public static int Entered, Exited;
        public static string CurrentReason => Token ?? "unscopedSetter";
        public static GoalEnemySetScope Begin(string source, string reason)
        { var saved = Token; Token = reason; Entered++; return new GoalEnemySetScope(saved); }
        public readonly struct GoalEnemySetScope : IDisposable
        {
            private readonly string _saved;
            public GoalEnemySetScope(string saved) { _saved = saved; }
            public void Dispose() { Token = _saved; Exited++; }
        }
    }
}
