// Only native boundaries are doubles. The complete exit coordinator/patch is
// compiled from production; these checks do not run EFT or its Unity renderer.
using System.Reflection;
namespace UnityEngine { public static class Time { public static float timeScale = 1; } }
namespace EFT
{
    public enum GameStatus { Starting, Started, Stopping, Stopped }
    public class AbstractGame { public GameStatus Status = GameStatus.Started; }
    public class BaseLocalGame<T> : AbstractGame { public void vmethod_4() { } }
    public class Player { public string ProfileId = "local"; }
    public class EftGamePlayerOwner { public Player Player; }
    public class GameWorld { public Player MainPlayer; }
    public class LocalGame : BaseLocalGame<EftGamePlayerOwner>
    {
        public GameWorld GameWorld;
        public string ProfileId = "local";
        public Player LocalPlayer = new();
        public EftGamePlayerOwner Owner;
        public int Calls;
        public Action Before, After;
        public bool Ignore;
        public void CG_method_18()
        {
            Calls++;
            Before?.Invoke();
            if (Ignore) return;
            if (!ArenaMode.Respawn.ArenaRespawnDriver.StandDown || Owner?.Player != LocalPlayer
                || Redeploy.Core.Engine.RespawnEngineState.IsPipelineRunning || UnityEngine.Time.timeScale <= 0)
                throw new InvalidOperationException("unsafe native continuation");
            Status = GameStatus.Stopping;
            After?.Invoke();
        }
    }
}
namespace EFT.UI
{
    public class PreloaderUI
    {
        public int Calls;
        public bool Visible = true;
        public void SetMenuTaskBarVisibility(bool value) { Calls++; Visible = value; }
    }
}
namespace EFT.Communications
{
    public static class NotificationManager
    {
        public static bool Throw;
        public static void DisplayMessageNotification(string message) { if (Throw) throw new Exception("notification fault"); }
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> { public static T Instance; }
    public static class MonoBehaviourSingleton<T> { public static T Instance; }
}
namespace Redeploy.Core.Engine { public static class RespawnEngineState { public static bool IsPipelineRunning; } }
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    {
        public static bool Throw;
        public static EFT.EftGamePlayerOwner GetPlayerOwner(EFT.LocalGame game)
            => Throw ? throw new Exception("owner fault") : game.Owner;
    }
}
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, Exact;
        public static EFT.AbstractGame StartedGame;
        public static EFT.GameWorld StartedWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => Exact && ReferenceEquals(game, StartedGame) && ReferenceEquals(world, StartedWorld);
    }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public static Log ModLogger = new();
        public class Log { public void LogInfo(string m) { } public void LogDebug(string m) { } public void LogWarning(string m) { } public void LogError(string m) { } }
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { } }
    public class PatchPrefixAttribute : Attribute { }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type t, string name, Type[] args)
            => t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, args);
    }
}
