using System.Reflection;

// Boundary doubles only. The production effects, pool policy and administrative
// gate are linked above; no alternate effects implementation lives in this file.
public enum EBodyPart { Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg, Common }
namespace UnityEngine { public static class Time { public static float time; } }
namespace Comfort.Common { public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT
{
    [Flags] public enum EDamageType { Undefined = 1, Fall = 2, Explosion = 4, Barbed = 8, Flame = 16,
        GrenadeFragment = 32, Impact = 64, Existence = 128, Medicine = 256, Bullet = 512,
        Melee = 1024, Landmine = 2048, Sniper = 4096, Blunt = 8192, LightBleeding = 16384,
        HeavyBleeding = 32768, Dehydration = 65536, Exhaustion = 131072, RadExposure = 262144,
        Stimulator = 524288, Poison = 1048576, LethalToxin = 2097152, Btr = 4194304,
        Artillery = 8388608, HotGases = 16777216, ThermobaricExplosion = 33554432, Environment = 67108864 }
    public interface IPlayer { string ProfileId { get; } }
    public interface IObserverToPlayerBridge { IPlayer iPlayer { get; } bool IsAI { get; } AIData AIData { get; } }
    public sealed class Bridge : IObserverToPlayerBridge
    {
        public IPlayer iPlayer { get; set; }
        public bool IsAI => (iPlayer as Player)?.AIData?.IsAI == true;
        public AIData AIData => (iPlayer as Player)?.AIData;
    }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; }
        public int Team;
        public AIData AIData = new();
        public HealthSystem.ActiveHealthController ActiveHealthController;
        public Player(string id) { ProfileId = id; ActiveHealthController = new() { Player = this }; }
        public virtual void OnDead(EDamageType type) { }
    }
    public class AIData { public bool IsAI; public BotOwner BotOwner = new(); }
    public class BotOwner { public BotSettings Settings = new(); }
    public class BotSettings { public BotFile FileSettings = new(); }
    public class BotFile { public MindSettings Mind = new(); }
    public class MindSettings { public bool MUTUAL_IGNORE_FRIENDLY_FIRE; }
    public class GameWorld
    {
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Ever = new();
        public Player GetEverExistedPlayerByID(string id) => id != null && Ever.TryGetValue(id, out var p) ? p : null;
    }
}
namespace EFT.Ballistics
{
    public struct DamageInfo { public EFT.EDamageType DamageType; public float Damage; public EFT.IObserverToPlayerBridge Player; public EBodyPart? OverDamageFrom; public EFT.InventoryLogic.Item Weapon; }
}
namespace EFT.InventoryLogic { public class Item { } public class ThrowWeap : Item { } public class Weapon : Item { public bool IsGrenadeLauncher; } }
namespace EFT.HealthSystem
{
    public class ActiveHealthController
    {
        public EFT.Player Player;
        public bool IsAlive = true;
        public float DamageCoeff = 1f, DamageMultiplier = 1f;
        public float ApplyDamage(EBodyPart bodyPart, float damage, EFT.Ballistics.DamageInfo info) => throw new NotSupportedException("Native health is an explicit boundary, not simulated proof");
    }
}
namespace ArenaMode.Session { public static class ArenaSession { public static bool IsActive = true; } }
namespace ArenaMode.Modes
{
    public sealed class RoundsMode { public bool FreeForAll = true; public bool Block; public bool ShouldBlockMatchDamage(EFT.Player victim) => Block; }
    public sealed class TeamDeathmatchMode { public bool Block; public bool ShouldBlockMatchDamage(EFT.Player victim) => Block; }
    public sealed class ArenaMatch { public object Mode = new RoundsMode(); }
    public static class ArenaMatchService { public static ArenaMatch Current = new(); }
}
namespace ArenaMode.Bots
{
    public static class TeamDeathmatchRelations { public static bool AreOpponents(EFT.IPlayer a, EFT.IPlayer b) => a is EFT.Player x && b is EFT.Player y && x.Team != y.Team; }
}
namespace ArenaMode.Pickups
{
    public static class ArenaPickupRuntime
    {
        public static int Generation;
        public static bool Live = true;
        public static readonly HashSet<EFT.Player> Registered = new(ReferenceEqualityComparer.Instance);
        public static bool IsLiveActor(EFT.Player p) => Live && p != null && Registered.Contains(p) && p.ActiveHealthController?.IsAlive == true;
    }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly Logger ModLogger = new(); }
    public class Logger { public void LogWarning(string value) { } }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefixAttribute : Attribute { }
    public abstract class ModulePatch
    {
        public static readonly List<(Type Patch, MethodBase Target)> Enabled = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable() { var target = GetTargetMethod(); if (target == null) throw new MissingMethodException(); Enabled.Add((GetType(), target)); }
    }
}
namespace HarmonyLib
{
    public class HarmonyPriority : Attribute { public readonly int priority; public HarmonyPriority(int value) => priority = value; }
    public static class Priority { public const int Last = 0; }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters)
            => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, parameters, null);
    }
}
