// Independent test doubles for native I/O only. Production policy and native
// exit coordinator are linked verbatim; this is not an EFT runtime simulation.
namespace UnityEngine { public static class Time { public static float timeScale = 1f; } }
namespace EFT
{
    public enum GameStatus { Started, Stopping, Stopped }
    public enum ExitStatus { Survived, Killed }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class Player { public string ProfileId = "local"; public Health HealthController = new(); }
    public class Health { public bool IsAlive = true; }
    public class Owner { public Player Player; }
    public class LocalGame : AbstractGame
    {
        public string ProfileId = "local";
        public Player LocalPlayer = new();
        public Owner Owner;
        public int StopCalls;
        public Action BeforeStop, AfterCommit;
        public bool IgnoreStop;
        public ExitStatus LastStatus;
        public void Stop(string profileId, ExitStatus status, string name, float delay)
        {
            StopCalls++;
            if (!ArenaMode.Respawn.ArenaRespawnDriver.StandDown) throw new Exception("death guard not released before native stop");
            BeforeStop?.Invoke();
            if (IgnoreStop) return;
            if (profileId != ProfileId || Owner?.Player != LocalPlayer) throw new Exception("unsafe native stop identity");
            LastStatus = status;
            Status = GameStatus.Stopping;
            AfterCommit?.Invoke();
        }
    }
    public class GameWorld { public Player MainPlayer; }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace Redeploy.Core.Engine { public static class RespawnEngineState { public static bool IsPipelineRunning; } }
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    {
        public static bool Throw;
        public static EFT.Owner GetPlayerOwner(EFT.LocalGame game) => Throw ? throw new Exception("owner inaccessible") : game.Owner;
    }
}
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool Exact, IsActive = true;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => Exact;
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public static readonly Log ModLogger = new();
        public sealed class Log
        {
            public readonly List<string> Entries = new();
            public void LogInfo(string text) => Entries.Add(text);
            public void LogWarning(string text) => Entries.Add(text);
            public void LogError(string text) => Entries.Add(text);
        }
    }
}
namespace ArenaMode.Modes
{
    public interface IArenaMode { }
    public class OtherMode : IArenaMode { }
    public class RoundsMode : IArenaMode
    {
        public bool HasResult, ResultOwnsCurrentRaid;
        public string ResultHeading, ResultDetail, ResultExitStatus;
        public void RequestResultExit() { }
        public void ReportResultExitProblem(string value) { }
    }
    public class TeamDeathmatchMode : IArenaMode
    {
        public bool HasResult;
        public string ResultHeading, ResultDetail, ResultExitStatus;
        public void RequestResultExit() { }
        public bool ResultOwnsCurrentRaid;
        public string ExitProblem;
        public void ReportResultExitProblem(string problem) => ExitProblem = problem;
    }
    public class ArenaMatch { public IArenaMode Mode; }
    public static class ArenaMatchService { public static ArenaMatch Current; }
}
