// Native boundary doubles only. Taunt, origin/epoch/body history, admin guard,
// and the extracted actual ArenaMatch text dispatch implementation are linked production.
using System.Reflection;
using System.Runtime.CompilerServices;

public enum EBodyPart { Head, Chest }
public enum EBodyPartColliderType { Head, Chest }
namespace UnityEngine { public struct Vector3 { } public struct Quaternion { } }
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
    public class Weapon : Item { }
    public class ThrowWeap : Item { public string FragmentType; }
}
namespace EFT
{
    public enum EDamageType { Bullet, GrenadeFragment, Explosion, Blunt, Landmine, HeavyBleeding, Fall, Undefined, Melee }
    public enum GameStatus { Started, Stopped }
    public interface IPlayer { string ProfileId { get; } }
    public class Bridge { public IPlayer iPlayer; }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameStatus Status; public Player LocalPlayer; }
    public class GameWorld { public Player MainPlayer; }
    public class Health { public bool IsAlive = true; }
    public class Grenade { }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; } = "local-profile";
        public bool IsAI, IsYourPlayer = true;
        public Health HealthController = new();
        public ItemHandsController HandsController;
        public class ItemHandsController { public Player _player; }
        public class BaseGrenadeHandsController : ItemHandsController
        {
            public InventoryLogic.ThrowWeap Item;
            public Grenade CreateAndRegisterGrenade(UnityEngine.Vector3 position,
                UnityEngine.Quaternion rotation, UnityEngine.Vector3 force, float prewarm = 0) => new();
        }
        public virtual void ApplyDamageInfo(Ballistics.DamageInfo damageInfo, EBodyPart bodyPartType,
            EBodyPartColliderType colliderType, float absorbed) { }
    }
}
namespace EFT.Ballistics
{
    public struct DamageInfo
    {
        public EFT.EDamageType DamageType;
        public EFT.Bridge Player;
        public EFT.InventoryLogic.Item Weapon;
        public string SourceId;
        public int FireIndex;
        public bool DelayedDamage;
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
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchFinalizerAttribute : Attribute { }
    public abstract class ModulePatch
    {
        internal static readonly List<MethodBase> Targets = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() => Targets.Add(GetTargetMethod() ?? throw new Exception("Unresolved native hook target"));
    }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static bool IsActive;
        internal static EFT.LocalGame Game;
        internal static EFT.GameWorld World;
        internal static bool IsExactActiveRaid(EFT.LocalGame game, EFT.GameWorld world)
            => ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode.Modes
{
    internal class Mode { internal bool IsComplete; }
    internal static class ArenaMatchService { internal static ArenaMatch Current; }
    internal sealed partial class ArenaMatch
    {
        internal Mode Mode = new();
        internal int Score = 27, Deaths = 3, Kills = 12, LocalDeathCalls, KillCalls;
        internal event Action<string> OnCallout;
        internal event Action<string> OnAnnouncerCue;
        internal event Action OnScoreChanged;
        internal void ReportKill(object kill) { KillCalls++; Score++; OnScoreChanged?.Invoke(); }
        internal void ReportLocalDeath() { LocalDeathCalls++; Deaths++; OnScoreChanged?.Invoke(); }
        // Unused policy boundaries remain observable if the extracted actual method
        // ever changes routing. These methods are not substitutes for production text dispatch.
        internal void ReportAnnouncerCue(string label) => OnAnnouncerCue?.Invoke(label);
        internal void PublishCombatLabel(string label) => OnCallout?.Invoke(label);
    }
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
