using EFT;
namespace UnityEngine
{
    public class Vector3 { }
    public static class Time { public static float time; public static float unscaledTime => time; public static float timeScale = 1f; }
    public static class Mathf { public static int FloorToInt(float n) => (int)Math.Floor(n); }
}
namespace Comfort.Common
{
    public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum GameStatus { Started, Stopping }
    public enum ExitStatus { Killed, MissingInAction, Survived }
    public enum EBotState { PreActive, Active }
    public enum WildSpawnType { Pmc, Infected }
    public static class Roles { public static bool IsInfected(this WildSpawnType value) => value == WildSpawnType.Infected; }
    public interface IPlayer { string ProfileId { get; } Profile Profile { get; } }
    public class Profile { public string Id, AccountId, Nickname; public Info Info = new(); }
    public class Info { public BotSettings Settings = new(); }
    public class BotSettings { public WildSpawnType Role; }
    public class Health { public bool IsAlive = true; }
    public class AiData { public BotOwner BotOwner; }
    public class Player : IPlayer
    {
        public Profile Profile { get; set; } = new(); public string ProfileId => Profile.Id;
        public Health HealthController = new(); public bool IsYourPlayer; public bool IsAI;
        public AiData AIData; public UnityEngine.Vector3 Position = new();
    }
    public class LocalPlayer : Player { }
    public class BotOwner
    {
        public Player GetPlayer; public Profile Profile => GetPlayer?.Profile;
        public string ProfileId => Profile?.Id; public Health HealthController => GetPlayer?.HealthController;
        public bool IsDead => HealthController?.IsAlive == false; public EBotState BotState = EBotState.Active;
    }
    public class AbstractGame { }
    public class PlayerOwner { public Player Player; }
    public class LocalGame : AbstractGame
    {
        public PlayerOwner Owner;
        public LocalPlayer LocalPlayer; public Profile Profile => LocalPlayer.Profile;
        public string ProfileId => Profile.Id; public GameStatus Status = GameStatus.Started;
        public int Stops; public bool StopThrows; public ExitStatus LastStatus;
        public void Stop(string profile, ExitStatus status, string origin, float n)
        { Stops++; if (StopThrows) throw new Exception("stop rejected"); LastStatus = status; Status = GameStatus.Stopping; }
    }
    public class GameWorld
    {
        public Player MainPlayer; public List<IPlayer> RegisteredPlayers = new();
        public Dictionary<string, Player> Ever = new();
        public Player GetEverExistedPlayerByID(string id) => Ever.GetValueOrDefault(id);
        public Player GetAlivePlayerByProfileID(string id) => RegisteredPlayers.OfType<Player>().FirstOrDefault(p => p.ProfileId == id && p.HealthController.IsAlive);
    }
    public interface IBotGame { BotsController BotsController { get; } }
    public class BotGame : IBotGame { public BotsController BotsController { get; } = new(); }
    public class BotsController { public Bots Bots = new(); }
    public class Bots { public List<BotOwner> BotOwners = new(); }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger = new(); }
    public class Logger
    {
        public List<string> Lines = new(); public void LogInfo(object s) => Lines.Add(s.ToString()); public void LogDebug(object s) => Lines.Add(s.ToString());
        public void LogWarning(object s) => Lines.Add(s.ToString()); public void LogError(object s) => Lines.Add(s.ToString());
    }
}
#if !ZOMBIE_SOLO_CHECKS
namespace ArenaMode.Combat { public class ArenaKill { public bool KilledByLocal, LocalIsVictim; public string VictimRoleLabel; } }
#endif
namespace ArenaMode.Config
{
    public sealed class Entry<T> { public T Value; public Entry(T v) => Value = v; }
    public static class Settings
    {
        public static Entry<int> ZombieWaveStartSize = new(2), ZombieShooterPercent = new(0), ZombieMaxAlive = new(10), ZombieWaveGrowth = new(1);
        public static Entry<float> ZombieIntermissionSeconds = new(2), ZombieSpawnIntervalSeconds = new(.2f), ZombieStuckWaveTimeoutSeconds = new(100), SpawnProtectionSeconds = new(4);
    }
}
namespace ArenaMode.Events
{
    public static class ArenaEvents { public static event Action<ArenaMode.Kits.KitDefinition> OnKitApplied; public static void Apply() => OnKitApplied?.Invoke(new()); }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool Active = true, ZombieOwnGearActive = true; public static float ReplaySeconds;
        public static bool IsExactActiveRaid(LocalGame g, GameWorld w) => Active
            && ReferenceEquals(Comfort.Common.Singleton<AbstractGame>.Instance,g) && ReferenceEquals(Comfort.Common.Singleton<GameWorld>.Instance,w);
    }
}
namespace ArenaMode.Respawn
{
    public static class ArenaRespawnDriver
    {
        public static bool IsRespawnInProgress, StandDown;
        internal static bool IsSameActiveRaid(LocalGame game, int epoch) => ArenaMode.Session.ArenaSession.Active
            && ReferenceEquals(Comfort.Common.Singleton<AbstractGame>.Instance, game) && game.Status == GameStatus.Started;
    }
}
namespace ArenaMode.Kits
{
    public class KitDefinition { }
    public static class KitCatalog { public static object KitProvider; }
    public class TeamArenaPreparedLoadoutRestore { public int RoundEpoch; }
    public class TeamArenaLoadoutSnapshot
    {
        public static bool CaptureOkay = true, ArmOkay = true, PrepareOkay = true;
        public static int Captures, Arms, Prepares;
        public static bool TryCapture(Profile p, out TeamArenaLoadoutSnapshot s, out string problem)
        { Captures++; s = new(); problem = "capture test failure"; return CaptureOkay; }
        public bool TryArm(int n, out string problem) { Arms++; problem = "arm test failure"; return ArmOkay; }
        public bool TryPrepare(Profile p, int n, out TeamArenaPreparedLoadoutRestore s, out string problem)
        { Prepares++; s = new(){RoundEpoch=n}; problem="prepare test failure"; return PrepareOkay; }
    }
    public static class ZombieLoadoutRefill
    {
        public static int Calls;
        public static bool TryRefillOwnGear(LocalGame g, int n,float s,Func<bool> predicate) { Calls++; return true; }
        public static bool TryRefill(LocalGame g, KitDefinition k,int n,float s,Func<bool> predicate) { Calls++; return true; }
    }
}
namespace ArenaMode.Modes
{
#if !ZOMBIE_SOLO_CHECKS
    public interface IArenaMode { }
#endif
    public static class ArenaModeCatalog { public const string ZombieId = "zombie"; }
    public static class ArenaReplaySelection { public static float Seconds; }
#if !ZOMBIE_SOLO_CHECKS
    public class ArenaScoreboardActor { public string Id, Account; }
    public class Scoreboard
    {
        public void ResetAssists() { }
        public int Epoch; public ArenaScoreboardActor[] Friends;
        public bool TrySynchronizeRoster(int n,ArenaScoreboardActor local,IReadOnlyList<ArenaScoreboardActor> friends,IReadOnlyList<ArenaScoreboardActor> enemies,out string problem)
        { Epoch=n; Friends=friends.ToArray(); problem=null; return true; }
    }
    public class ArenaMatch
    {
        public IArenaMode Mode; public Scoreboard Scoreboard = new(); public float Elapsed => UnityEngine.Time.time;
        public List<string> Callouts = new(); public void ReportTextCallout(string t) => Callouts.Add(t);
        public void ReportTeamSurvivors(bool local,int allies,int enemies) { }
        public void ReportLocalDeath() { if (Mode is ZombieMode zombie && zombie.TryRecordLocalDeath()) zombie.OnLocalDeath(); }
    }
    internal static class TdmMatchExit
    {
        internal static bool PipelineRunning => false;
        internal static void TryStop(LocalGame g, GameWorld w, string id, Action<string> report) => g.Stop(id, ExitStatus.Killed, "Arena", 0f);
    }
#endif
    public static class ArenaMatchService
    {
        public static ArenaMatch Current;
        public static int GenericAborts;
        internal static void ScheduleFailedStartAbort(string problem) { GenericAborts++; ArenaMode.Respawn.ArenaRespawnDriver.StandDown = true; }
        public static ArenaScoreboardActor SnapshotActor(Player p,string account=null) => p==null?null:
#if ZOMBIE_SOLO_CHECKS
            new(p.ProfileId, p.Profile.Nickname, "PMC", p.HealthController.IsAlive, account);
#else
            new(){Id=p.ProfileId,Account=account};
#endif
    }
}
namespace ArenaMode.Bots
{
    // Crowd behavior is covered by ZombieCrowdChecks; this suite owns wave/team flow.
    public static class ZombieCrowdRuntime { public static void Reset() { } public static void Tick() { } }
    public static class ZombieNativeEventBridge { public static bool TryGetOpeningSnapshot(out int n,out int s) { n=s=0; return false; } }
    public static class ZombieMeleePatch { public static void Reset() { } }
    public static class ZombieMeleeDiagnostics { public static (int,int) ConsumeTotals() => (0,0); }
    public static class ZombieProfilePlan { public static object BackupTargets(int n,int s) => null; public static int BackupCountForWaveStartSize(int n) => n; }
    public static class ZombieRoleMix { public static (string Role,string Difficulty) Next(Random r,int s) => ("infected","normal"); }
    public static class ZombieSpawnService
    {
        public static int Alive, Pending, ConfirmedArrivals, Dispatched, Retired;
        public static int FailedDispatches;
        public static bool RejectRequest;
        public static string PendingFailure;
        public static void Reset() { Alive=Pending=ConfirmedArrivals=Dispatched=Retired=FailedDispatches=0; RejectRequest=false;PendingFailure=null; }
        public static int ConsumeFailedDispatches() { int result=FailedDispatches;FailedDispatches=0;return result; }
        public static bool TryGetPendingFailure(out string reason) { reason=PendingFailure; return reason!=null; }
        public static int CountAliveInfected() => Alive;
        public static void RegisterBackupTargets(object x) { }
        public static bool TryMapRole(string s,out int r) { r=0; return true; }
        public static bool TryMapDifficulty(string s,out int r) { r=0; return true; }
        public static bool TryRequest(int r,int d) { if(RejectRequest) return false; Dispatched++; Pending++; return true; }
        public static void RetireAllAliveInfected() { Retired+=Alive; Alive=0; }
        public static void NudgeIdleZombies(UnityEngine.Vector3 p) { }
    }
}
namespace ArenaMode.Effects
{
    public static class ArenaTracerTeams
    {
        public static int Binds; public static bool BindOkay=true;
        public static bool TryBindCooperativeWave(int n,int expectedCompanionCount,string local,IReadOnlyList<string> friends,out string problem)
        { Binds++; problem="test tracer failure"; return BindOkay; }
        public static bool RegisterCooperativeInfected(string id) => true;
    }
}
namespace ArenaMode.Spectator
{
    internal enum OptionalCameraOwnerState { Absent, Idle, Busy, Incompatible }
    internal static class KillcamReplayCompat
    {
        public static bool CameraReleased=true;
        internal static OptionalCameraOwnerState State=OptionalCameraOwnerState.Absent;
        internal static float Remaining;
        internal static OptionalCameraOwnerState ReadState(out float remaining, out string problem)
        { remaining=Remaining;problem=null;return State; }
    }
    public enum ArenaSpectatorSide { Ally }
    public class ArenaSpectatorTarget { public Player Player; public ArenaSpectatorTarget(Player p,string id,string name,ArenaSpectatorSide s) => Player=p; }
    public class ArenaSpectatorRequest
    {
        public int ExpectedCompanionCount; public int Epoch; public Func<bool> Eligible; public Func<IReadOnlyList<ArenaSpectatorTarget>> Targets;
        public ArenaSpectatorRequest(int e,int expectedCompanionCount,Func<int> read,Func<bool> eligible,Player dead,IReadOnlyList<string> ids,Func<IReadOnlyList<ArenaSpectatorTarget>> targets,string preferred,bool cooperative=false)
        { Epoch=e; ExpectedCompanionCount=expectedCompanionCount; Eligible=eligible; Targets=targets; }
    }
    public static class ArenaSpectatorService
    {
        public static bool OptionalKillcamReplayMayRun=true; public static ArenaSpectatorRequest Request;
        public static bool RequestStart(ArenaSpectatorRequest r,out string p) { Request=r;p=null;return true; }
        public static void Stop(string r) => Request=null;
    }
}
namespace ArenaMode.Compat
{
    public enum PitFireTeamRoundLifecycleStage { None, AwaitingRetirement, ReadyForLeader, Profiles, Followers, FollowersReady, Failed }
    public enum PitFireTeamRoundPollDecision { Pending, Ready, Failed }
    public class PitFireTeamRoundCapability { public bool CanStartAdditionalRound=true; }
    public class PitFireTeamRoundLifecycleSnapshot
    { public PitFireTeamRoundLifecycleStage Stage; public int Epoch; public string Diagnostic="test"; public PitFireTeamRoundCapability Capability=new(); }
    public class PitFireTeamRoundPolicyDecision { public PitFireTeamRoundPollDecision Decision; public string Reason="test"; }
    public static class PitFireTeamCompat
    {
        public static bool TeamArenaActive=true, PersistenceSuppressionActive=true, RoundTransitionActive, BindOkay=true, GetOkay=true, HostilityOkay=true, BeginOkay=true;
        public static int FrozenCompanionCount=4; public static int HostilityCalls; public static Player Leader; public static List<BotOwner> Followers=new();
        public static IReadOnlyDictionary<string,string> CurrentCompanionProfilesByAccountId => Followers.ToDictionary(b=>b.Profile.AccountId,b=>b.ProfileId);
        public static IReadOnlyDictionary<string,string> CanonicalCompanionProfilesByAccountId => CurrentCompanionProfilesByAccountId;
        public static bool TryBindLocalPlayerBody(Player p,out string problem) { Leader=p;problem="bind test failure";return BindOkay; }
        public static bool TryGetLiveFollowers(out IReadOnlyList<BotOwner> b,out string problem)
        { b=Followers.Where(f=>!f.IsDead).ToArray(); problem="roster test failure";return GetOkay; }
        public static bool EnsureHostility(IReadOnlyList<BotOwner> e,Player leader)
        { HostilityCalls++;return e.Count>0 && HostilityOkay; }
        public static bool BeginRoundTransition(out Player leader,out IReadOnlyList<BotOwner> followers,out string problem)
        { leader=Leader;followers=Followers.ToArray();problem="transition test failure";RoundTransitionActive=BeginOkay;return BeginOkay; }
        public static bool TryAdoptRoundRoster(Player leader,out IReadOnlyList<BotOwner> followers,out string problem)
        { followers=Followers;problem=null;RoundTransitionActive=false;return true; }
        public static void BeginFinalizing() { }
        public static void CancelFinalizingAfterFailedStop() { }
    }
    public static class PitFireTeamRoundLifecycle
    {
        public static PitFireTeamRoundLifecycleSnapshot Status=new(); public static string Diagnostic=>Status.Diagnostic;
        public static int Rebuilt, Teardowns, Cancelled; public static bool RetireReady=true, ProfilesReady=true, FollowersReady=true;
        public static Action Spawn;
        public static bool BeginTeardown(Player leader,IReadOnlyList<BotOwner> followers,IReadOnlyDictionary<string,string> accounts,bool protection,TimeSpan timeout,out string problem)
        {
            Teardowns++; Status.Epoch++;Status.Stage=PitFireTeamRoundLifecycleStage.AwaitingRetirement;problem=null;
            var w=Comfort.Common.Singleton<GameWorld>.Instance;
            foreach(var b in followers) { b.HealthController.IsAlive=false;w.RegisteredPlayers.Remove(b.GetPlayer);Comfort.Common.Singleton<IBotGame>.Instance.BotsController.Bots.BotOwners.Remove(b); }
            return true;
        }
        public static PitFireTeamRoundPolicyDecision PollRetirement(GameWorld w)
        { if(RetireReady) Status.Stage=PitFireTeamRoundLifecycleStage.ReadyForLeader;return new(){Decision=RetireReady?PitFireTeamRoundPollDecision.Ready:PitFireTeamRoundPollDecision.Pending}; }
        public static bool TryRebuildBoss(Player p,BotsController b,bool protection,out string problem)
        { Rebuilt++; problem=null;return true; }
        public static bool BeginFollowerProfilePreparation(bool protection,TimeSpan timeout,out string problem)
        { Status.Stage=PitFireTeamRoundLifecycleStage.Profiles;problem=null;return true; }
        public static PitFireTeamRoundPolicyDecision PollFollowerProfilePreparation(bool protection)
        { return new(){Decision=ProfilesReady?PitFireTeamRoundPollDecision.Ready:PitFireTeamRoundPollDecision.Pending}; }
        public static bool TryStartFollowerSpawn(bool protection,TimeSpan timeout,out string problem)
        { Spawn?.Invoke();Status.Stage=PitFireTeamRoundLifecycleStage.Followers;problem=null;return true; }
        public static PitFireTeamRoundPolicyDecision PollFollowers(GameWorld w,bool protection)
        { if(FollowersReady) Status.Stage=PitFireTeamRoundLifecycleStage.FollowersReady;return new(){Decision=FollowersReady?PitFireTeamRoundPollDecision.Ready:PitFireTeamRoundPollDecision.Pending}; }
        public static void CancelFollowerSpawnLease(string why) => Cancelled++;
    }
}

// Explicitly enabled for these diagnostic-observation scenarios; the real
// default-off BepInEx routing is exercised by tools/LoggingChecks.
namespace ArenaMode.Diagnostics
{
    internal static class ArenaLog
    {
        internal static bool DeveloperEnabled = true;
        internal static void Developer(string message)
        {
            if (DeveloperEnabled) ArenaMode.Plugin.ModLogger?.LogInfo("[dev] " + message);
        }
    }
}
