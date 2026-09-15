// Native boundaries only: the whole production post-raid coordinator is linked.
using System.Reflection;
namespace UnityEngine
{
    public class GameObject { public bool active = true; public void SetActive(bool value) => active = value; }
}
namespace EFT
{
    public class AbstractGame { }
    public class GameWorld { }
    public class LocalRaidSettings { }
    public class LocalGame : AbstractGame { public string ProfileId = "pmc"; public LocalRaidSettings Settings = new(); }
    public enum ESideType { Pmc, Savage }
    public class RaidSettings { public bool Local = true; public ESideType Side; }
    public class Profile { public string Id = "pmc"; }
    public class Session { public Profile Profile = new(); }
    public class TarkovApplication { public RaidSettings CurrentRaidSettings; public void OnGameEnd() { } }
    public class SessionResultShowOperation
    {
        public RaidSettings _raidSettings;
        public Session _session = new();
        public string _matchProfileId = "pmc";
        public Profile _savageProfile = new() { Id = "scav" };
        public UI.SessionEnd.SessionResultExitStatus.ExitStatusScreenController _exitStatusScreenController = new();
        public UI.Screens.Controller _killListScreenController = new(), _statisticsScreenController = new(),
            _experienceScreenController = new(), _healthTreatmentScreenController = new();
        public int HealthCalls;
        public void ShowHealthTreatmentScreen() => HealthCalls++;
        public void ProceedFromStatistics() { }
        public void Init() { }
        public void ConcludeOperation() { }
    }
}
namespace EFT.UI.SessionEnd
{
    public class SessionResultExitStatus
    {
        public class ExitStatusScreenController { }
        public UnityEngine.GameObject _warningPanel = new();
        public void Show(ExitStatusScreenController c) { }
    }
}
namespace EFT.UI.Screens
{
    public enum EEftScreenType { ExitStatus, SessionExperience }
    public interface IBaseScreenController<T> { }
    public class Controller : IBaseScreenController<EEftScreenType> { }
    public class ScreenManager<T> { public void ShowScreenSequence(bool rootFirstScreen, bool queueLastScreen, IBaseScreenController<T>[] controllers) { } }
}
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    {
        public static bool Throw;
        public static EFT.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game)
            => Throw ? throw new Exception("native settings fault") : game.Settings;
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, LastRaidWasArena, ArmedForNextRaid;
        public static int ArmEpoch;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && game != null && world != null && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Log ModLogger = new(); }
    public class Log { public void LogWarning(string m) { } public void LogInfo(string m) { } }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); public void Enable() { } }
    public class PatchPrefixAttribute : Attribute { }
    public class PatchPostfixAttribute : Attribute { }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type t, string name, Type[] args = null)
            => args == null ? t.GetMethod(name) : t.GetMethod(name, args);
    }
}
