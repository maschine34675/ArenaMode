namespace UnityEngine { static class Time { public static int frameCount; public static float unscaledTime; } }
namespace Comfort.Common { static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT
{
    enum GameStatus { Created, Running, Starting, Started, SoftStopping, Stopping, Stopped }
    enum ExitStatus { Killed, MissingInAction }
    class AbstractGame { }
    class Health { public bool IsAlive=true; }
    class Player { public string ProfileId="not-authoritative"; public Health HealthController=new(); }
    class GameWorld { public Player MainPlayer=new(); }
    class LocalGame : AbstractGame
    {
        public GameStatus Status=GameStatus.Started;
        public GameWorld GameWorld; public Player LocalPlayer=new(); public string ProfileId="raid-profile";
        public int StopCalls; public string StopId; public ExitStatus Exit; public Action OnStop;
        public void Stop(string profile,ExitStatus status,string name,float delay)
        { StopCalls++; StopId=profile; Exit=status; if(OnStop!=null)OnStop();else Status=GameStatus.Stopping; }
    }
}
namespace ArenaMode
{
    static class Plugin { public static Logger ModLogger=new(); }
    class Logger
    {
        public List<string> Info=new(),Warnings=new(),Errors=new();
        public void LogInfo(string s)=>Info.Add(s); public void LogWarning(string s)=>Warnings.Add(s);
        public void LogError(string s)=>Errors.Add(s);
    }
}
namespace ArenaMode.Session
{
    static class ArenaSession
    {
        public static bool IsActive,LastRaidWasArena;
        public static EFT.AbstractGame StartedGame; public static EFT.GameWorld StartedWorld;
        public static bool IsExactStartedRaid(EFT.AbstractGame game,EFT.GameWorld world)
            => game!=null&&world!=null&&ReferenceEquals(StartedGame,game)&&ReferenceEquals(StartedWorld,world);
    }
}
namespace ArenaMode.Respawn { static class ArenaRespawnDriver { public static bool StandDown; } }
namespace ArenaMode.Compat { static class PitFireTeamCompat { public static int FinalizeCalls; public static void BeginFinalizing()=>FinalizeCalls++; } }
namespace ArenaMode.Bots { static class BurstSpawnService { public static int Resets; public static void Reset()=>Resets++; } }
