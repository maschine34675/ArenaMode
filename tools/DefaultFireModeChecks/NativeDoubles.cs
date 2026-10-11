// External engine seams only; the entire production gate, lifecycle, hook and
// mutation logic is linked from ArenaDefaultFireMode.cs. This is not GPU proof.
using EFT.InventoryLogic;
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum GameStatus { Started, Stopped }
    public class AbstractGame { public GameStatus Status; }
    public class LocalGame : AbstractGame { public Player LocalPlayer; }
    public class GameWorld { public Player MainPlayer; }
}
namespace EFT.InventoryLogic
{
    public class Weapon
    {
        public enum EFireMode { single, fullauto, burst, doublet, doubleaction }
        public enum EMalfunctionState { None, Jam }
        public sealed class Malfunction { public EMalfunctionState State; }
        public Malfunction MalfState = new();
        public FireModeComponent FireMode = new();
        public string ShortName = "fixture weapon", Id = "fixture item";
        public bool IsStationaryWeapon;
    }
    public class FireModeComponent
    {
        public Weapon.EFireMode[] AvailableEFireModes = { Weapon.EFireMode.single, Weapon.EFireMode.fullauto };
        public Weapon.EFireMode FireMode;
        public int Writes;
        public Action Changed;
        public void SetFireMode(Weapon.EFireMode mode) { Writes++; FireMode = mode; Changed?.Invoke(); }
    }
}
namespace EFT
{
    public class Player
    {
        public class Health { public bool IsAlive = true; }
        public bool IsAI, IsYourPlayer = true;
        public string ProfileId = "same profile across respawns";
        public Health HealthController = new();
        public object HandsController;
        public class FirearmController
        {
            public Player _player;
            public Weapon Item = new();
            public Animator FirearmsAnimator = new();
            public object CurrentOperation = new Idling();
            public class Idling { }
            public class SpawnOperation
            {
                public FirearmController Controller;
                public void WeaponAppeared()
                {
                    Controller.CurrentOperation = new Idling();
                    HarmonyLib.Harmony.Postfix?.Invoke(null, new object[] { this });
                }
            }
        }
    }
    public class Animator
    {
        public int Writes;
        public Weapon.EFireMode Mode;
        public bool SkipAnimation;
        public void SetFireMode(Weapon.EFireMode mode, bool skip) { Writes++; Mode = mode; SkipAnimation = skip; }
    }
}
namespace HarmonyLib
{
    public class Harmony
    {
        public static System.Reflection.MethodInfo Postfix;
        public Harmony(string id) { }
        public void Patch(System.Reflection.MethodInfo target, HarmonyMethod postfix) { Postfix = postfix.Method; }
        public void UnpatchSelf() { Postfix = null; }
    }
    public class HarmonyMethod
    {
        public System.Reflection.MethodInfo Method;
        public HarmonyMethod(Type type, string name)
        { Method = type.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); }
    }
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo DeclaredMethod(Type type, string name, Type[] args)
            => type.GetMethod(name, args);
    }
}
namespace Redeploy.Core.Engine
{
    public static class RaidLifecycle
    { public static event Action OnRaidEnded; public static void End() => OnRaidEnded?.Invoke(); }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "fixture";
        public static readonly Log ModLogger = new();
        public class Log
        { public void LogInfo(string text) { } public void LogWarning(string text) { } }
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static int ArmEpoch;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(Game, game) && ReferenceEquals(World, world);
    }
}
namespace ArenaMode.Modes { public static class ArenaMatchService { public static bool TeamStartAbortPending; } }
namespace ArenaMode.Areas { public static class ArenaAreaRuntime { public static bool IsPending; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown, IsRespawnInProgress; } }
namespace ArenaMode.Spectator { public static class ArenaSpectatorService { public static bool IsActive; } }
