using System.Reflection;

namespace HarmonyLib
{
    public sealed class Harmony
    {
        public static readonly List<(MethodBase target, HarmonyMethod prefix)> Registered = new();
        public static int FailAt = -1;
        public Harmony(string id) { }
        public void Patch(MethodBase target, HarmonyMethod prefix)
        {
            if (Registered.Count == FailAt) throw new InvalidOperationException("hook unavailable");
            Registered.Add((target, prefix));
        }
        public void UnpatchSelf() => Registered.Clear();
    }
    public sealed class HarmonyMethod
    {
        public readonly MethodInfo Method;
        public HarmonyMethod(Type type, string name) => Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] args)
            => type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, args, null);
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
    public enum GameStatus { Running, Starting, Started, SoftStopping, Stopping, Stopped }
    public enum ExitStatus { Survived, Runner, Left, Killed, MissingInAction }
    public sealed class EftGamePlayerOwner { }
    public sealed class Profile { public string Id = "local"; }
    public sealed class Health { public bool IsAlive = true; }
    public sealed class Player
    {
        public string ProfileId = "local";
        public bool IsAI;
        public Health HealthController = new();
    }
    public class AbstractGame { }
    public class BaseLocalGame<T> : AbstractGame, CommonAssets.Scripts.Game.EndByExitTrigerScenario.IGame
    {
        public int Stops;
        public ExitStatus LastExit;
        void CommonAssets.Scripts.Game.EndByExitTrigerScenario.IGame.StopSession(string profileId, ExitStatus exitStatus, string exitName)
            => Stop(profileId, exitStatus, exitName);
        public virtual void Stop(string profileId, ExitStatus exitStatus, string exitName)
        { Stops++; LastExit = exitStatus; }
    }
    public sealed class LocalGame : BaseLocalGame<EftGamePlayerOwner>
    {
        public GameWorld GameWorld;
        public Profile Profile = new();
        public GameStatus Status = GameStatus.Started;
        public LocalRaidSettings Settings = new();
    }
    public sealed class LocalRaidSettings { public string location, serverId; }
    public sealed class GameWorld
    {
        public string LocationId;
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Players = new();
        public CommonAssets.Scripts.Game.ExfiltrationController ExfiltrationController = new();
        public Player GetAlivePlayerByProfileID(string id) => Players.TryGetValue(id, out var player) ? player : null;
    }
}
namespace EFT.Interactive
{
    public sealed class ExitSettings { public string Name; }
    public class ExfiltrationPoint
    {
        public ExitSettings Settings = new();
        public int NativeProceeds;
        public virtual void Proceed(EFT.Player player, bool manuallyStarted = false) => NativeProceeds++;
    }
    public sealed class ScavExfiltrationPoint : ExfiltrationPoint { }
}
namespace EFT.Interactive.SecretExfiltrations
{
    public sealed class SecretExfiltrationPoint : EFT.Interactive.ExfiltrationPoint
    {
        public override void Proceed(EFT.Player player, bool manuallyStarted = false) => NativeProceeds++;
    }
}
namespace CommonAssets.Scripts.Game
{
    public sealed class ExfiltrationController
    {
        public EFT.Interactive.ExfiltrationPoint[] ExfiltrationPoints = Array.Empty<EFT.Interactive.ExfiltrationPoint>();
        public EFT.Interactive.ScavExfiltrationPoint[] ScavExfiltrationPoints = Array.Empty<EFT.Interactive.ScavExfiltrationPoint>();
        public EFT.Interactive.SecretExfiltrations.SecretExfiltrationPoint[] SecretExfiltrationPoints = Array.Empty<EFT.Interactive.SecretExfiltrations.SecretExfiltrationPoint>();
    }
    public sealed class EndByExitTrigerScenario
    {
        public interface IGame { void StopSession(string profileId, EFT.ExitStatus exitStatus, string exitName); }
    }
}
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    {
        public static bool Throw;
        public static EFT.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game)
            => Throw ? throw new InvalidOperationException("settings unavailable") : game.Settings;
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly Logger ModLogger = new();
    }
    public sealed class Logger
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public void LogError(string text) => Errors.Add(text);
        public void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid;
        public static string ArmedMap, ArmedMode;
        public static EFT.AbstractGame ExactGame;
        public static EFT.GameWorld ExactWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(ExactGame, game) && ReferenceEquals(ExactWorld, world);
    }
    public static class ArenaServerClient { public static string PendingArmToken; }
}
