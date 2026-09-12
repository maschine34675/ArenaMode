using System.Reflection;

namespace UnityEngine { public static class Time { public static float time; } }
namespace JetBrains.Annotations { [AttributeUsage(AttributeTargets.All)] public sealed class CanBeNullAttribute : Attribute { } }
namespace HarmonyLib
{
    public static class Priority { public const int Last = 0; }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPriorityAttribute(int priority) : Attribute { }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null)
            => parameters == null ? type.GetMethod(name) : type.GetMethod(name, parameters);
    }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfixAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchFinalizerAttribute : Attribute { }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace ArenaMode
{
    public static class Plugin { public static readonly LogSpy ModLogger = new(); }
    public sealed class LogSpy
    {
        public readonly List<string> Warnings = new();
        public void LogWarning(string message) => Warnings.Add(message);
    }
}
namespace ArenaMode.Modes
{
    public static class ArenaModeCatalog { public const string DeathmatchId = "deathmatch"; }
    public sealed class DeathmatchOptions { public bool FreeForAll; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid;
        public static string ActiveMode, ArmedMode, ArmedMap;
        public static Modes.DeathmatchOptions DeathmatchSelection;
        public static EFT.AbstractGame ExactGame;
        public static EFT.GameWorld ExactWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => ReferenceEquals(game, ExactGame) && ReferenceEquals(world, ExactWorld);
    }
}
namespace ArenaMode.Bots
{
    public static class ArenaBotSpawnPatches { public static void ResetRaidState() { } }
    public static class ArenaHostility
    {
        public static bool CountsAsAlly(BotsGroup group, EFT.IPlayer player) => group.Allies.Contains(player);
    }
    public static class TeamDeathmatchRelations
    {
        public static bool Enabled;
        public static bool TryGroupRelationship(BotsGroup group, EFT.IPlayer player, out bool enemy) { enemy = false; return false; }
    }
}
namespace EFT
{
    public enum WildSpawnType { assault, marksman, pmcUSEC, pmcBEAR }
    public enum EPlayerSide { Savage, Usec, Bear }
    public enum EBotState { NonActive, PreActive, Active, Disposed }
    public enum EBotEnemyCause { initial, addPlayer, addPlayerToBoss, addBotAtGroup, addBotNoGroup }
    public sealed class HealthSpy { public bool IsAlive = true; }
    public sealed class AIData { public BotOwner BotOwner; }
    public interface IPlayer
    {
        bool IsAI { get; }
        AIData AIData { get; }
        string GroupId { get; }
        EPlayerSide Side { get; }
    }
    public sealed class Player : IPlayer
    {
        public bool IsAI { get; set; } = true;
        public AIData AIData { get; set; } = new();
        public string GroupId { get; set; } = Guid.NewGuid().ToString();
        public EPlayerSide Side { get; set; }
    }
    public sealed class BotOwner : IPlayer
    {
        public string Name;
        public WildSpawnType Role;
        public BotsGroup BotsGroup;
        public EBotState BotState = EBotState.PreActive;
        public HealthSpy HealthController = new();
        public Player GetPlayer = new();
        public bool IsDead;
        public bool IsAI => true;
        public AIData AIData => GetPlayer.AIData;
        public string GroupId => GetPlayer.GroupId;
        public EPlayerSide Side => GetPlayer.Side;
        public BotOwner(string name, WildSpawnType role)
        {
            Name = name; Role = role; GetPlayer.AIData.BotOwner = this;
            GetPlayer.Side = role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : role == WildSpawnType.pmcBEAR ? EPlayerSide.Bear : EPlayerSide.Savage;
        }
    }
    public sealed class BotZone { public int MaxPersons = 64; }
    public interface IBotGame { BotsController BotsController { get; } }
    public class AbstractGame { }
    public sealed class TestGame : AbstractGame, IBotGame { public BotsController BotsController { get; } = new(); }
    public sealed class GameWorld { public string LocationId = "factory4_day"; }
    public sealed class BotsController
    {
        public static WildSpawnType[] AllTypes = Enum.GetValues<WildSpawnType>();
        public BotSpawner BotSpawner;
    }
    public sealed class BotSpawner
    {
        public bool _freeForAll;
        public BotZoneGroupsDictionary Groups = new();
        public IBotGame BotGame;
        public void GetGroupAndSetEnemies() { }
        public void AfterCreation() { }
    }
}

// Deliberately observable native I/O only: no production registry/cleanup or
// relationship-reconciliation logic lives here. The full native registry classes
// themselves are extracted from the installed decompiled game for each run.
public sealed class BotsGroup
{
    public EFT.WildSpawnType InitialBotType;
    public EFT.EPlayerSide Side;
    public EFT.IBotGame BotGame;
    public EFT.BotOwner _initialBot;
    public bool Locked, ForcedAggressiveForNewPlayers;
    public int MembersCount;
    public int DisposeCalls, AddEnemyCalls, AddMemberCalls;
    public bool ThrowOnDispose, ThrowOnAddEnemy;
    public Action<EFT.IPlayer> OnAddEnemy;
    public readonly List<EFT.IPlayer> Allies = new();
    public Dictionary<EFT.IPlayer, object> Enemies = new();
    public BotsGroup(EFT.BotOwner bot, EFT.IBotGame game)
    { _initialBot = bot; InitialBotType = bot.Role; Side = bot.Side; BotGame = game; bot.BotsGroup = this; }
    public void Lock() => Locked = true;
    public bool IsSuitable(EFT.WildSpawnType role) => role == InitialBotType;
    public bool IsPlayerEnemy(EFT.IPlayer player) => false;
    public bool ContainsEnemy(EFT.IPlayer player) => Enemies.ContainsKey(player);
    public bool AddEnemy(EFT.IPlayer player, EFT.EBotEnemyCause cause)
    {
        AddEnemyCalls++;
        OnAddEnemy?.Invoke(player);
        if (ThrowOnAddEnemy) throw new InvalidOperationException("native enemy admission failure");
        Enemies[player] = new object();
        return true;
    }
    public void AddEnemyGroupIfAllowed(string group, EFT.EPlayerSide side) { }
    public void AddNeutral(EFT.IPlayer player) { }
    public void AddMember(EFT.BotOwner bot, bool onActivation) { AddMemberCalls++; MembersCount++; }
    public void Dispose() { DisposeCalls++; if (ThrowOnDispose) throw new InvalidOperationException("native dispose failure"); }
    public string MessageInfo() => InitialBotType.ToString();
    public string MessageInfoWide() => MessageInfo();
}

internal static class GroupProbe
{
    private static readonly Type Production = typeof(ArenaMode.Bots.ArenaBotPatches);
    internal static void Call(string patch, string method, params object[] args) => Invoke(patch, method, args);
    internal static object Invoke(string patch, string method, params object[] args)
    {
        var nested = Production.GetNestedType(patch, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing actual production patch " + patch);
        try { return nested.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args); }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); }
        throw new InvalidOperationException("Unreachable reflection continuation");
    }
    internal static bool? Enter(string patch, EFT.BotSpawner spawner)
    { object[] args = [spawner, null]; Call(patch, "Prefix", args); return (bool?)args[1]; }
    internal static void Finish(string patch, EFT.BotSpawner spawner, bool? state) => Call(patch, "Finalizer", spawner, state);
    internal static int Tracked => ((System.Collections.IDictionary)Production.GetField("IndividualGroups", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Count;
}
