// Explicit native/mode boundary doubles. The adapter, damage DTO, body identity,
// combat source allow-list and administrative gate are linked production code.
using System.Reflection;
using ArenaMode.Combat;

public enum EBodyPart { Common, Head, Chest, LeftLeg }
namespace Comfort.Common
{
    internal static class Singleton<T>
    {
        internal static T Instance;
        internal static bool Instantiated => Instance != null;
    }
}
namespace EFT
{
    public enum EDamageType { Bullet, GrenadeFragment, Explosion, Blunt, Landmine, LightBleeding, HeavyBleeding, Fall, Undefined, Melee, Medicine, Flame }
    public enum EPlayerSide { Usec, Bear }
    public enum GameStatus { Started, Stopped }
    public interface IPlayer { string ProfileId { get; } }
    public class Bridge { public IPlayer iPlayer; }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameStatus Status; public Player LocalPlayer; }
    public class Health { public bool IsAlive = true; }
    public class GameWorld
    {
        public Player MainPlayer;
        public readonly Dictionary<string, Player> Ever = new(), Alive = new();
        public Player GetEverExistedPlayerByID(string id) => Ever.GetValueOrDefault(id);
        public Player GetAlivePlayerByProfileID(string id) => Alive.GetValueOrDefault(id);
    }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; }
        public bool IsAI, IsYourPlayer;
        public Health HealthController = new();
        public AIData AIData;
        public void OnHealthChanged(EBodyPart bodyPart, float diff, Ballistics.DamageInfo damageInfo) { }
        public virtual void OnDead(EDamageType damageType) { }
    }
    public class AIData { public BotOwner BotOwner; }
    public class BotOwner { public Player GetPlayer; public BotsGroup BotsGroup; }
    public class BotsGroup
    {
        private readonly Dictionary<IPlayer, object> _enemies = new();
        public bool EnemiesReadThrows;
        public Dictionary<IPlayer, object> Enemies => EnemiesReadThrows ? throw new Exception("native group unavailable") : _enemies;
        public readonly List<IPlayer> Allies = new();
    }
}
namespace EFT.Ballistics
{
    public struct DamageInfo
    {
        public EFT.EDamageType DamageType;
        public EFT.Bridge Player;
        public float Damage, DidArmorDamage;
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
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
namespace ArenaMode.Areas { internal static class ArenaAreaRuntime { internal static bool IsPending; } }
namespace ArenaMode.Modes
{
    public interface IArenaMode { bool IsComplete { get; } }
    internal class Mode : IArenaMode { public bool IsComplete { get; set; } }
    internal class RoundsMode : Mode { internal bool AssistScoringOpen = true, FreeForAll = true; }
    internal class TeamDeathmatchMode : Mode { internal bool AssistScoringOpen = true; }
    internal class GunGameMode : Mode { internal bool AssistScoringOpen = true; }
    internal class ZombieMode : Mode
    {
        internal bool AssistScoringOpen = true, IsTeamArena;
        internal readonly Dictionary<EFT.IPlayer, int> Teams = new();
        internal bool IsOpposingAwardKill(EFT.Player victim, EFT.IPlayer attacker)
            => AssistScoringOpen && Teams.TryGetValue(victim, out int a)
                && Teams.TryGetValue(attacker, out int b) && a != b;
        internal bool IsCurrentFriendlyMarkerBody(EFT.Player player)
            => player.HealthController.IsAlive && Teams.TryGetValue(player, out int side) && side == 0;
    }
    internal class TacticalMode : Mode
    {
        internal bool AssistScoringOpen = true;
        internal readonly Dictionary<EFT.IPlayer, int> Teams = new();
        internal bool IsOpposingAwardKill(EFT.Player victim, EFT.IPlayer attacker)
            => AssistScoringOpen && Teams.TryGetValue(victim, out int a)
                && Teams.TryGetValue(attacker, out int b) && a != b;
    }
    internal static class ArenaMatchService { internal static ArenaMatch Current; internal static bool TeamStartAbortPending; }
    internal sealed class ArenaMatch
    {
        internal IArenaMode Mode;
        internal readonly List<ArenaAssistDamage> Damage = new();
        internal readonly List<long> Forgotten = new();
        internal void RecordAssistDamage(ArenaAssistDamage damage) => Damage.Add(damage);
        internal void ForgetAssistVictim(long body) => Forgotten.Add(body);
    }
}
namespace ArenaMode.Bots
{
    internal static class TeamDeathmatchRelations
    {
        internal static readonly Dictionary<EFT.IPlayer, EFT.EPlayerSide> Affiliations = new();
        internal static EFT.EPlayerSide? Faction(EFT.IPlayer p)
            => Affiliations.TryGetValue(p, out var side) ? side : null;
        internal static bool AreOpponents(EFT.IPlayer a, EFT.IPlayer b)
            => Faction(a).HasValue && Faction(b).HasValue && Faction(a) != Faction(b);
    }
    internal static class ArenaHostility
    {
        internal static bool CountsAsAlly(EFT.BotsGroup group, EFT.IPlayer player)
            => group.Allies.Any(p => ReferenceEquals(p, player));
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
namespace ArenaMode.Combat.Fire
{
    internal static class ArenaFireRuntime
    {
        internal static EFT.Player Victim, Owner;
        internal static bool OwnsDamage(EFT.Player victim, EFT.IPlayer source, EFT.EDamageType type)
            => type.ToString() == "Flame" && Owner != null && ReferenceEquals(victim, Victim) && ReferenceEquals(source, Owner);
    }
}
