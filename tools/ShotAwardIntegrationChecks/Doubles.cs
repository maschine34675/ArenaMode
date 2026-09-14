// Explicit boundary doubles only. No award/history/hook policy is reproduced.
// Native IL and source wiring are verified separately; this does not run Harmony or Unity.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnityEngine { public struct Vector3 { } }
namespace Comfort.Common
{
    internal static class Singleton<T>
    {
        internal static T Instance;
        internal static bool Instantiated => Instance != null;
    }
}
namespace EFT.InventoryLogic
{
    public class Item { public string Id; public string TemplateId; }
    public interface IWeapon { Item Item { get; } bool IsUnderbarrelWeapon { get; } }
    public class Weapon : Item, IWeapon
    {
        public bool BoltAction, IsGrenadeLauncher;
        public Item Item => this;
        public bool IsUnderbarrelWeapon { get; set; }
    }
    public class Ammo : Item { }
}
namespace EFT
{
    public enum EDamageType { Bullet, Blunt, Explosion, HeavyBleeding, Fall }
    public enum GameStatus { Started, Stopped }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameStatus Status; public Player LocalPlayer; }
    public class GameWorld { public Player MainPlayer; }
    public class ClientGameWorld : GameWorld { public virtual void ShotDelegate(Ballistics.Shot shotResult) { } }
    public class Health { public bool IsAlive = true; }
    public class MovementContext { public bool IsGrounded; }
    public class CharacterController { public bool isGrounded; }
    public class Player
    {
        public string ProfileId = "local-profile";
        public bool IsAI, IsYourPlayer = true;
        public Health HealthController = new();
        public MovementContext MovementContext = new();
        public CharacterController CharacterController = new();
        public ItemHandsController HandsController;
        public class ItemHandsController { public Player _player; }
        public class FirearmController : ItemHandsController
        {
            public InventoryLogic.Weapon Item;
            public bool IsAiming;
            public virtual void InitiateShot(InventoryLogic.IWeapon weapon, InventoryLogic.Ammo ammo,
                UnityEngine.Vector3 shotPosition, UnityEngine.Vector3 shotDirection,
                UnityEngine.Vector3 fireportPosition, int chamberIndex, float overheat) { }
        }
    }
    public class Bridge { public Player iPlayer; }
}
namespace EFT.Ballistics
{
    public class Shot
    {
        public EFT.Bridge Player;
        public InventoryLogic.Item Weapon;
        public InventoryLogic.Item Ammo;
        public Shot Parent;
        public int FireIndex, FragmentIndex;
        public bool IsFlyingOutOfTime;
        // Only target selection is exercised here. Full native signature is checked with Cecil.
        public static Shot Create() => new();
    }
    public readonly struct ShotId(string ammoId, int fragmentIndex)
    {
        public readonly string AmmoId = ammoId;
        public readonly int FragmentIndex = fragmentIndex;
        public bool Equals(ShotId other) => AmmoId == other.AmmoId && FragmentIndex == other.FragmentIndex;
    }
    public struct DamageInfo
    {
        public EFT.EDamageType DamageType;
        public EFT.Bridge Player;
        public InventoryLogic.Item Weapon;
        public int FireIndex;
        public string SourceId;
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref F FieldRef<T, F>(T instance);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name)
        {
            if (typeof(T) != typeof(EFT.Player.ItemHandsController) || typeof(F) != typeof(EFT.Player) || name != "_player")
                throw new InvalidOperationException("Unexpected native owner contract");
            return (T instance) => ref Unsafe.As<EFT.Player, F>(ref ((EFT.Player.ItemHandsController)(object)instance)._player);
        }
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly, null, parameters, null);
    }
}
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchFinalizerAttribute : Attribute { }
    public abstract class ModulePatch
    {
        internal static readonly List<MethodBase> Targets = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() => Targets.Add(GetTargetMethod() ?? throw new Exception("Unresolved hook target"));
    }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive = true;
        internal static EFT.LocalGame Game;
        internal static EFT.GameWorld World;
        internal static bool IsExactActiveRaid(EFT.LocalGame game, EFT.GameWorld world)
            => ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode.Modes
{
    internal sealed class Mode { internal bool IsComplete; }
    internal sealed class Match { internal Mode Mode = new(); }
    internal static class ArenaMatchService { internal static Match Current = new(); }
}
namespace ArenaMode
{
    internal static class Plugin { internal static Logger ModLogger = new(); }
    internal sealed class Logger
    {
        internal readonly List<string> Lines = new();
        internal void LogWarning(string message) => Lines.Add(message);
    }
}
