// Host-only API doubles. Production helpers and Harmony prefixes are linked or
// extracted verbatim by the two balance check projects; no Unity raid is simulated.
using System.Reflection;
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Clamp01(float x) => Math.Clamp(x, 0, 1);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int RoundToInt(float x) => (int)Math.Round(x);
        public static float Lerp(float a, float b, float t) => a + (b-a)*Clamp01(t);
    }
}
namespace EFT
{
    public enum BotDifficulty { easy, normal, hard, impossible }
    public enum WildSpawnType { assault, pmcUSEC, pmcBEAR }
    public enum GameStatus { Starting, Started, Stopping }
    public enum EBotState { PreActive, Active, NonActive }
    public enum EDamageType { Bullet, Fall, LightBleeding, HeavyBleeding, Poison, Dehydration }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class LocalGame : AbstractGame, IBotGame { public Player LocalPlayer; public BotsController BotsController { get; set; } = new(); }
    public class BotsController { public BotSpawner BotSpawner = new(); }
    public class BotSpawner { public BotCreatorClient _botCreator; }
    public class ClientBackendSession { public void LoadBots(List<CountTypeBotWave> conditions) { } }
    public class GameWorld
    {
        public Player MainPlayer;
        public Dictionary<string, Player> Alive = new();
        public Player GetAlivePlayerByProfileID(string id) => Alive.GetValueOrDefault(id);
    }
    public class Player
    {
        public string ProfileId = "local";
        public bool IsAI, IsYourPlayer = true;
        public AIData AIData;
        public HealthSystem.ActiveHealthController ActiveHealthController;
    }
    public class AIData { public BotOwner BotOwner; }
    public class BotOwner { public Player GetPlayer; public EBotState BotState = EBotState.Active; public bool IsDead; }
    public class DamageInfo { public EDamageType DamageType; public DamagePlayer Player; }
    public class DamagePlayer { public Player iPlayer; }
    public class CountTypeBotWave
    {
        public WildSpawnType Role; public int Limit; public BotDifficulty Difficulty;
        public CountTypeBotWave(int count, WildSpawnType role, BotDifficulty difficulty) { Limit=count; Role=role; Difficulty=difficulty; }
    }
    public class Profile { public Info Info = new(); }
    public class Info { public ProfileSettings Settings = new(); }
    public class ProfileSettings { public WildSpawnType Role; public BotDifficulty BotDifficulty; }
}
public interface IBotGame { EFT.BotsController BotsController { get; } }
public class BotCreatorClient { public IBotGame _game; public BotProfileClient _creatorProfile; }
public class BotProfileClient
{
    public EFT.ClientBackendSession _backEndSession = new();
    public BotProfileBackuploader _backupLoader;
    public List<EFT.Profile> _simpleProfiles = new();
    public void CreateProfile(BotCreationData data, CancellationToken cancellationToken, bool withDelete) { }
}
public class BotProfileBackuploader
{
    public BotProfileClient _profileCreator;
    public void AddProfileForBackup(BotCreationData data) { }
}
public class BotCreationData { public bool SpawnStopped; public ProfileData _profileData = new(); public BotCreatorClient _botCreator; }
public class ProfileData
{
    public EFT.WildSpawnType Role = EFT.WildSpawnType.pmcUSEC; public EFT.BotDifficulty Difficulty = EFT.BotDifficulty.hard;
    public bool Valid = true;
    public bool TryGetRole(out EFT.WildSpawnType role, out EFT.BotDifficulty difficulty) { role=Role; difficulty=Difficulty; return Valid; }
}
namespace EFT.HealthSystem
{
    public class ActiveHealthController { public EFT.Player Player; public bool IsAlive=true; public float DamageCoeff=1; }
}
namespace ArenaMode.Config
{
    public class Entry<T>(T value) { public T Value=value; }
    public static class Settings
    {
        public static Entry<float> BleedingDamageMultiplier = new(3);
        public static Entry<bool> ScaleDifficulty = new(true);
        public static Entry<float> PressureRampMinutes=new(12), PressureStreakPercent=new(35);
        public static Entry<int> PressurePmcBonus = new(4);
        public static Entry<EFT.BotDifficulty> MaxBotDifficulty=new(EFT.BotDifficulty.impossible);
    }
}
namespace ArenaMode.Modes
{
    public class Mode { public bool IsComplete; }
    public class TeamDeathmatchMode : Mode
    {
        public float Elapsed; public int Duration=300; public bool Current=true;
        public bool TryGetDifficultyClock(out float elapsed, out int duration) { elapsed=Elapsed; duration=Duration; return Current && !IsComplete; }
    }
    public class Match { public Mode Mode = new(); public float Elapsed; public int Streak; }
    public static class ArenaMatchService { public static Match Current; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive=true;
        public static object Game, World;
        public static bool IsExactActiveRaid(object game, object world) => IsActive && game!=null && world!=null && ReferenceEquals(Game,game) && ReferenceEquals(World,world);
    }
    public static class ArenaServerClient { public static string ActiveArmToken="arm-current"; }
}
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown, IsRespawnInProgress; } }
namespace ArenaMode.Compat { public static class PitFireTeamCompat { public static bool HasFrozenTeamRoster; } }
namespace ArenaMode.Bots
{
    public static class TeamDeathmatchRelations
    {
        public static bool Block;
        public static bool ShouldBlockFriendlyDamage(EFT.Player victim, EFT.Player attacker) => Block;
    }
}
namespace ArenaMode.Combat
{
    // The damage-prefix balance fixture treats team immunity as an external
    // decision; its exact live implementation has FriendlyFireRuntimeChecks.
    public static class ArenaFriendlyFire
    {
        public static bool ShouldBlockFriendlyDamage(EFT.Player victim, EFT.Player attacker)
            => ArenaMode.Bots.TeamDeathmatchRelations.ShouldBlockFriendlyDamage(victim, attacker);
    }
    public static class ArenaHitHistory
    {
        public static List<float> Damage=new();
        public static void ObserveHealthDamage(EFT.Player victim, EFT.Player attacker, bool bullet, float damage) => Damage.Add(damage);
    }
}
namespace ArenaMode { public static class Plugin { public static Log ModLogger = new(); } public class Log { public void LogWarning(object text) => Console.WriteLine(text); } }
namespace HarmonyLib
{
    public static class AccessTools { public static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, args); }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        public static int EnableCalls, FailEnableAt;
        public static bool FailDisable;
        protected abstract MethodBase GetTargetMethod();
        public void Enable() { if(++EnableCalls==FailEnableAt || GetTargetMethod()==null) throw new Exception("hook missing"); }
        public void Disable() { if(FailDisable) throw new Exception("rollback unavailable"); }
    }
    [AttributeUsage(AttributeTargets.Method)] public class PatchPrefixAttribute:Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class PatchPostfixAttribute:Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class PatchFinalizerAttribute:Attribute { }
}
