namespace UnityEngine
{
    static class Time { public static int frameCount; public static float unscaledTime; public static float timeScale=1; }
    class MonoBehaviour { }
    class Coroutine { public Action Run; }
}
static class Utils
{
    public static UnityEngine.Coroutine WaitSeconds(UnityEngine.MonoBehaviour component,float seconds,Action action)
        => new(){Run=action};
}
static class MonoBehaviourSingleton<T> { public static T Instance; }
namespace EFT.UI
{
    class PreloaderUI : UnityEngine.MonoBehaviour
    {
        public void ShowRaidStartInfo() { }
        public void FadeBlackScreen() { }
    }
}
namespace SPT.Reflection.Patching
{
    abstract class ModulePatch { protected abstract System.Reflection.MethodBase GetTargetMethod(); public void Enable(){} }
    class PatchPrefix : Attribute { }
    class PatchFinalizer : Attribute { }
    class PatchTranspiler : Attribute { }
}
// Reflection/IL contract doubles only; the staged build and GPU startup prove
// actual SPT/Harmony installation (the Unity Harmony binary cannot run on net10).
namespace HarmonyLib
{
    static class AccessTools
    {
        public static System.Reflection.MethodInfo DeclaredMethod(Type type,string name,Type[] parameters=null)
            => parameters==null?type.GetMethod(name,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly)
                :type.GetMethod(name,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly,null,parameters,null);
    }
    class CodeInstruction
    {
        public object operand; public System.Reflection.Emit.OpCode opcode;
        public List<System.Reflection.Emit.Label> labels=new();
        public CodeInstruction(System.Reflection.Emit.OpCode op,object arg){opcode=op;operand=arg;}
        public bool Calls(System.Reflection.MethodInfo method)=>Equals(operand,method)&&opcode==System.Reflection.Emit.OpCodes.Call;
    }
}
namespace Redeploy.Core.Engine { static class RespawnEngineState { public static bool IsPipelineRunning; } }
namespace Redeploy.Core.GameAccess
{
    class PlayerOwner { public EFT.Player Player; }
    static class LocalGameAccessor
    {
        public static PlayerOwner Owner;
        public static PlayerOwner GetPlayerOwner(EFT.LocalGame game)=>Owner;
    }
}
namespace Comfort.Common { static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT
{
    enum GameStatus { Created, Running, Starting, Started, SoftStopping, Stopping, Stopped }
    enum ExitStatus { Killed, MissingInAction, Survived }
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
        public List<string> Info=new(),Warnings=new(),Errors=new(),Debug=new();
        public void LogDebug(string s)=>Debug.Add(s);
        public void LogInfo(string s)=>Info.Add(s); public void LogWarning(string s)=>Warnings.Add(s);
        public void LogError(string s)=>Errors.Add(s);
    }
}
namespace ArenaMode.Session
{
    static class ArenaSession
    {
        public static bool IsActive,LastRaidWasArena;
        public static int ArmEpoch;
        public static EFT.AbstractGame StartedGame; public static EFT.GameWorld StartedWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game,EFT.GameWorld world)
            => IsActive && IsExactStartedRaid(game,world);
        public static bool IsExactStartedRaid(EFT.AbstractGame game,EFT.GameWorld world)
            => game!=null&&world!=null&&ReferenceEquals(StartedGame,game)&&ReferenceEquals(StartedWorld,world);
    }
}
namespace ArenaMode.Respawn { static class ArenaRespawnDriver { public static bool StandDown; } }
namespace ArenaMode.Compat { static class PitFireTeamCompat { public static int FinalizeCalls; public static void BeginFinalizing()=>FinalizeCalls++; } }
namespace ArenaMode.Bots { static class BurstSpawnService { public static int Resets; public static void Reset()=>Resets++; } }
namespace ArenaMode.Modes
{
    // Mode-policy boundary only. The lease, Stop coordinator and intro callback
    // are production sources; the real result dispatch has TdmMatchChecks coverage.
    class ResultMode { public bool Owns; }
    class ArenaMatch { public ResultMode Mode; }
    internal static partial class ArenaMatchService { internal static ArenaMatch Current; }
    static class ArenaMatchResult { internal static bool OwnsRaid(ResultMode mode) => mode?.Owns == true; }
}
