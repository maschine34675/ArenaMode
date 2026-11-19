// Host-only API doubles. Production helpers and Harmony prefixes are linked or
// extracted verbatim by the two balance check projects; no Unity raid is simulated.
using System.Reflection;
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance is not null; } }
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
    public static class Time { public static float unscaledTime; }
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
    public class GameWorld
    {
        public string LocationId;
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
    public class Profile { public Info Info = new(); }
    public class Info { public ProfileSettings Settings = new(); public bool Poison; int _level; public int Level { get => Poison ? throw new InvalidOperationException("level") : _level; set => _level = value; } }
    public class ProfileSettings { public WildSpawnType Role; public BotDifficulty BotDifficulty; }
}
public interface IBotGame { EFT.BotsController BotsController { get; } }
// The client-side bot profile pool, in the minimal form the Gun Game fresh gear patch reads.
public class BotCreatorClient { public IBotGame _game; public BotProfileClient _creatorProfile; }
public class BotProfileClient
{
    public List<EFT.Profile> _simpleProfiles = new();
    public void CreateProfile(BotCreationData data, CancellationToken cancellationToken, bool withDelete) { }
}
public class BotCreationData { public bool SpawnStopped; public ProfileData _profileData = new(); public BotCreatorClient _botCreator; }
public class ProfileData
{
    public EFT.WildSpawnType Role = EFT.WildSpawnType.pmcUSEC; public EFT.BotDifficulty Difficulty = EFT.BotDifficulty.impossible;
    public bool Valid = true;
    public bool TryGetRole(out EFT.WildSpawnType role, out EFT.BotDifficulty difficulty) { role=Role; difficulty=Difficulty; return Valid; }
}
namespace HarmonyLib { public static class AccessTools { public static MethodBase Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters); } }
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
        // The one difficulty setting; the frozen copy lives in ArenaSession.Difficulty.
        public static Entry<ArenaMode.Shared.ArenaDifficultyStep> DifficultyStep = new(ArenaMode.Shared.ArenaDifficultyStep.Maximum);
    }
}
namespace ArenaMode.Modes
{
    public class Mode { public bool IsComplete; }
    public class Match { public Mode Mode = new(); public float Elapsed; public int Streak; }
    public static class ArenaMatchService { public static Match Current; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive=true;
        public static object Game, World;
        public static ArenaMode.Shared.ArenaDifficultyStep? Difficulty;
        public static string ActiveMode, ArmedMap;
        public static bool IsExactActiveRaid(object game, object world) => IsActive && game!=null && world!=null && ReferenceEquals(Game,game) && ReferenceEquals(World,world);
    }
    // The per-map ramp length the server config carries; 0 or less means absent (the reader falls back).
    public static class ArenaClientConfig
    {
        public static int RampMinutes=15, StartLevel=1, EndLevel=66; public static bool ScaleBotTier=true, Throw;
        public static int GetBotTierRampMinutes(string map, int fallback=15) => RampMinutes>0 ? RampMinutes : fallback;
        public static int GetBotTierStartLevel(string map, int fallback=1) => Throw ? throw new InvalidOperationException("config") : StartLevel;
        public static int GetBotTierEndLevel(string map, int fallback=66) => EndLevel;
        public static bool GetScaleBotTier(string map, bool fallback=true) => Throw ? throw new InvalidOperationException("config") : ScaleBotTier;
    }
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
    // The staging damage prefix reports the attacker to the spawn protection (a374295); the balance
    // fixture only needs the call to compile, the protection itself has TeamDeathmatchRuntimeChecks.
    public static class TeamSpawnProtectionHooks { public static void Attacked(EFT.Player attacker) { } }
}
namespace ArenaMode { public static class Plugin { public static Log ModLogger = new(); } public class Log { public static int Warnings; public static List<string> Debugs = new(); public void LogWarning(object text) { Warnings++; Console.WriteLine(text); } public void LogDebug(object text) => Debugs.Add(text.ToString()); } }
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch
    {
        public static bool FailEnable; public static int Enabled;
        protected abstract MethodBase GetTargetMethod();
        public void Enable() { if (FailEnable) throw new InvalidOperationException("target"); GetTargetMethod(); Enabled++; }
    }
    [AttributeUsage(AttributeTargets.Method)] public class PatchPrefixAttribute:Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class PatchPostfixAttribute:Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class PatchFinalizerAttribute:Attribute { }
}
