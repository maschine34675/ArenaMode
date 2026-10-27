using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    public sealed class CodeInstruction(OpCode opcode, object operand = null)
    { public OpCode opcode = opcode; public object operand = operand; public List<Label> labels = new(); }
    public sealed class HarmonyMethod(Type type, string name)
    { public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic); }
    public sealed class Harmony(string id)
    {
        public static List<MethodInfo> Hooks = new();
        public static Func<SAIN.SAINComponent.Classes.Talk.EnemyTalk, bool> Patched;
        public static bool BreakContract;
        public void UnpatchSelf() { Hooks.Clear(); Patched = null; }
        public void Patch(MethodInfo method, HarmonyMethod transpiler)
        {
            var code = Program.NativeOperand(out var dynamic, BreakContract);
            var rewritten = ((IEnumerable<CodeInstruction>)transpiler.Method.Invoke(null, [code])).ToList();
            var il = dynamic.GetILGenerator();
            foreach (var c in rewritten)
            {
                foreach (var l in c.labels) il.MarkLabel(l);
                if (c.operand == null) il.Emit(c.opcode);
                else if (c.operand is FieldInfo field) il.Emit(c.opcode, field);
                else if (c.operand is MethodInfo call) il.Emit(c.opcode, call);
                else if (c.operand is Label label) il.Emit(c.opcode, label);
                else throw new InvalidOperationException("Unknown emitted operand");
            }
            Patched = (Func<SAIN.SAINComponent.Classes.Talk.EnemyTalk, bool>)dynamic.CreateDelegate(typeof(Func<SAIN.SAINComponent.Classes.Talk.EnemyTalk, bool>));
            Hooks.Add(method);
        }
    }
}
namespace Comfort.Common
{ public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace UnityEngine { public static class Time { public static float timeScale = 1; } }
namespace EFT
{
    public enum WildSpawnType { pmcUSEC, pmcBEAR, assault, infectedAssault }
    public enum EBotState { Active, NonActive }
    public enum GameStatus { Started, Starting, Stopped }
    public class AbstractGame { }
    public sealed class LocalGame : AbstractGame { public GameStatus Status = GameStatus.Started; }
    public sealed class Health { public bool IsAlive = true; }
    public sealed class ProfileSettings { public WildSpawnType Role = WildSpawnType.pmcUSEC; }
    public sealed class ProfileInfo { public ProfileSettings Settings = new(); }
    public sealed class Profile { public ProfileInfo Info = new(); }
    public sealed class Ai { public BotOwner BotOwner; }
    public sealed class Follower { public bool HaveBoss; }
    public sealed class Player
    { public bool IsAI = true, IsYourPlayer; public string ProfileId = "pmc"; public Health HealthController = new(); public Ai AIData = new(); public Profile Profile = new(); }
    public sealed class BotOwner
    {
        public EBotState BotState = EBotState.Active; public Player GetPlayer; public object Component;
        public bool FollowerFlag; public Follower BotFollower = new();
        public bool IsFollower() => FollowerFlag;
        public object GetComponent(Type type) => Component;
    }
    public sealed class GameWorld
    {
        public Player MainPlayer = new() { IsAI = false, IsYourPlayer = true };
        public Dictionary<string, Player> Alive = new(), Ever = new();
        public Player GetAlivePlayerByProfileID(string id) => Alive.GetValueOrDefault(id);
        public Player GetEverExistedPlayerByID(string id) => Ever.GetValueOrDefault(id);
    }
}
namespace ArenaMode
{
    public static class Plugin { public const string PluginGuid = "test"; public static Logger ModLogger = new(); }
    public sealed class Logger
    { public List<string> Warnings = new(); public void LogDebug(object line) { } public void LogWarning(object line) => Warnings.Add(line.ToString()); }
}
namespace ArenaMode.Diagnostics
{ public static class ArenaLog { public static bool DeveloperEnabled; public static List<string> Lines = new(); public static void Developer(string text) { if (DeveloperEnabled) Lines.Add(text); } } }
namespace ArenaMode.Areas { public static class ArenaAreaRuntime { public static bool IsPending; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown; } }
namespace ArenaMode.Compat
{ public static class PitFireTeamCompat { public static HashSet<EFT.Player> Team = new(); public static bool IsTeamPlayer(EFT.Player body) => Team.Contains(body); } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive; public static EFT.GameWorld World; public static EFT.AbstractGame Game;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world) => ReferenceEquals(Game, game) && ReferenceEquals(World, world);
    }
}
namespace ArenaMode.Modes
{
    public interface IArenaMode { bool IsComplete { get; } }
    public class TestMode : IArenaMode { public bool Complete; public bool IsComplete => Complete; public bool AssistScoringOpen = true; }
    public sealed class RoundsMode : TestMode { }
    public sealed class TacticalMode : TestMode { }
    public sealed class TeamDeathmatchMode : TestMode { }
    public sealed class GunGameMode : TestMode { }
    public sealed class ZombieMode : IArenaMode { public bool IsComplete => false; }
    public sealed class Match { public IArenaMode Mode = new RoundsMode(); }
    public static class ArenaMatchService { public static Match Current = new(); public static bool TeamStartAbortPending; }
}
namespace SAIN.SAINComponent.Classes.Info
{
    public sealed class MindSettings { public bool BotTaunts = true, CanTalk = true; public float TalkFrequency = 1; }
    public sealed class FileSettings { public MindSettings Mind = new(); }
    public sealed class PersonalitySettings { public bool CanTaunt; public float TauntFrequency = 10, TauntChance = 50; }
    public sealed class SAINBotInfoClass
    {
        public SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality Personality { get; set; }
        public FileSettings FileSettings { get; set; } = new();
        public PersonalitySettings Settings = new();
    }
}
namespace SAIN.Components
{
    public sealed class BotComponent
    {
        public EFT.BotOwner BotOwner { get; set; } public EFT.Player Player { get; set; }
        public SAIN.SAINComponent.Classes.Info.SAINBotInfoClass Info { get; set; } = new();
        public SAIN.SAINComponent.Classes.Talk.SAINBotTalkClass Talk { get; set; } = new();
    }
}
namespace SAIN.SAINComponent.Classes.Talk
{
    public sealed class SAINBotTalkClass { public EnemyTalk EnemyTalk { get; set; } public bool CanTalk = true; }
    public sealed class EnemyTalk
    {
        public SAIN.Components.BotComponent Bot { get; set; }
        private bool CanTaunt;
        public bool Cached { get => CanTaunt; set => CanTaunt = value; }
        public bool Cooldown = true, Chance = true, HasEnemy = true, EnemyAllowsTaunt = true;
        public int RemainingCalls;
        public void ManualUpdate() { }
        public bool NativeRemainingChecks() { RemainingCalls++; return Cooldown && Chance && HasEnemy && EnemyAllowsTaunt; }
        // The outer native SAIN talk owner retains its own CanTalk guard.
        public bool Execute() => Bot.Talk.CanTalk && HarmonyLib.Harmony.Patched(this);
    }
}
