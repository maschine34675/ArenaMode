using System.Reflection;
using System.Reflection.Emit;

namespace BepInEx.Configuration { public class ConfigEntry<T> { public T Value { get; set; } } }

namespace BepInEx
{
    public class BaseUnityPlugin { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    {
        public string GUID => guid;
        public string Name => name;
        public Version Version => new(version);
    }
    public sealed class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        public static MethodInfo Method(Type type, string name, Type[] parameters = null) => parameters == null
            ? type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
    }
    public sealed class HarmonyMethod(Type type, string name)
    {
        public int priority;
        public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class PatchInfo { public List<Patch> Prefixes = new(), Postfixes = new(), Transpilers = new(), Finalizers = new(), ILManipulators = new(); }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static readonly Dictionary<MethodBase, PatchInfo> Bound = new();
        public static bool FailUnpatch, DropReceipt;
        public static int FailPatchAt, PatchCalls;
        public void Patch(MethodInfo target, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null)
        {
            if (!Bound.TryGetValue(target, out var info)) Bound[target] = info = new();
            if (!DropReceipt) {
                if (prefix != null) info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
                if (postfix != null) info.Postfixes.Add(new() { owner = id, PatchMethod = postfix.Method });
                if (transpiler != null) info.Transpilers.Add(new() { owner = id, PatchMethod = transpiler.Method });
            }
            if (transpiler != null) transpiler.Method.Invoke(null, new object[] { PatchProcessor.GetOriginalInstructions(target), target });
            if (++PatchCalls == FailPatchAt) throw new InvalidOperationException("injected patch failure");
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("injected cleanup failure");
            foreach (var info in Bound.Values) {
                info.Prefixes.RemoveAll(p => p.owner == id); info.Postfixes.RemoveAll(p => p.owner == id);
                info.Transpilers.RemoveAll(p => p.owner == id); info.Finalizers.RemoveAll(p => p.owner == id);
            }
        }
        public static PatchInfo GetPatchInfo(MethodBase method) => Bound.GetValueOrDefault(method);
        public static IEnumerable<MethodBase> GetAllPatchedMethods() => Bound.Keys.ToArray();
    }
    public class CodeInstruction(OpCode operation, object argument = null)
    { public OpCode opcode = operation; public object operand = argument; public List<Label> labels = new(); public List<object> blocks = new(); }
    public static class PatchProcessor
    {
        public static IEnumerable<CodeInstruction> GetOriginalInstructions(MethodBase method)
        {
            var codes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);
            var bytes = method.GetMethodBody().GetILAsByteArray(); var result = new List<CodeInstruction>();
            for (int at = 0; at < bytes.Length;) {
                short value = bytes[at++]; if (value == 0xfe) value = (short)(0xfe00 | bytes[at++]);
                var op = codes[value]; object operand = null;
                if (op.OperandType == OperandType.InlineMethod) { operand = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, at)); at += 4; }
                else if (op.OperandType != OperandType.InlineNone) throw new InvalidOperationException("Fixture disassembler unsupported operand " + op);
                result.Add(new(op, operand));
            }
            return result;
        }
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
public enum EPlayerState { Idle, Run, Sprint, Jump, Sidestep, ProneIdle }
public enum GameStatus { Running, Starting, Started, Stopping, Stopped }
namespace JsonType
{
    public enum ESideType { Pmc, Savage }
    public class LocalRaidSettings { public string location = "factory4_day", serverId; public ESideType playerSide = ESideType.Pmc; }
}
namespace EFT
{
    public interface IBotGame { }
    public class AbstractGame { }
    public class LocalGame : AbstractGame, IBotGame
    {
        public GameWorld GameWorld;
        public LocalPlayer LocalPlayer;
        public object Profile;
        public GameStatus Status = GameStatus.Started;
        public JsonType.LocalRaidSettings _raidSettings = new();
    }
    public class HideoutGame : AbstractGame { }
    public interface IPlayer { }
    public class GameWorld {
        public string LocationId = "factory4_day"; public Player MainPlayer;
        public List<IPlayer> RegisteredPlayers = new();
        public void OnGameStarted() { }
        public void RegisterPlayer(IPlayer iPlayer) => RegisteredPlayers.Add(iPlayer);
    }
    public class Player : IPlayer
    {
        public GameWorld GameWorld;
        public bool IsYourPlayer;
        public object Profile = new();
        public MovementContext MovementContext;
        public EFT.HealthSystem.ActiveHealthController ActiveHealthController;
    }
    public class LocalPlayer : Player { }
    public class BaseMovementState { }
    public class NativeMovementState : BaseMovementState { }
    public class ForeignMovementState : BaseMovementState { }
    public class MovementContext
    {
        // Match native visibility: production must validate and reflect this field.
        protected Player _player;
        public readonly Dictionary<EPlayerState, BaseMovementState> Cache = new();
        public int ForeignStates, ForeignSpeeds, NativeSpeeds;
        public MovementContext(Player body) { _player = body; }
        public void SetOwner(Player body) { _player = body; }
        public BaseMovementState GetNewState(EPlayerState state, bool isAI = false) => new NativeMovementState();
        public void ManualUpdate(float deltaTime) { NativeSpeeds++; }
    }
}
public enum EBodyPart { Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg, Common }
namespace EFT { public enum EDamageType { Undefined, Bullet, HeavyBleeding, LightBleeding, Fall, Barbed, Explosion } }
namespace EFT.Ballistics { public struct DamageInfo { public EDamageType DamageType; } }
namespace EFT.HealthSystem {
    public class ActiveHealthController {
        public EFT.Player Player;
        public bool Alive = true, Destroyed;
        public float Health = 100;
        public int DeathEvents, EffectsRemoved;
        public void Kill(EDamageType type) { if (Alive) { Alive = false; DeathEvents++; } }
        public void DestroyBodyPart(EBodyPart part, EDamageType type) { Destroyed = true; }
        public float ApplyDamage(EBodyPart part, float damage, EFT.Ballistics.DamageInfo info) => damage;
        public void DoFracture(EBodyPart part) { }
    }
}
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    { public static JsonType.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game) => game._raidSettings; }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "com.maschine.ArenaMode";
        public static readonly Logger ModLogger = new();
    }
    public sealed class Logger
    {
        public readonly List<string> Debug = new(), Warnings = new();
        public void LogDebug(string text) => Debug.Add(text);
        public void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive, ArmedForNextRaid;
        public static string ArmedMap;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(ActiveGame, game) && ReferenceEquals(ActiveWorld, world);
    }
    public static class ArenaServerClient { public static bool HasPendingArm; public static string PendingArmToken; }
}
namespace ArenaMode.Diagnostics
{
    public static class ArenaLog
    {
        public static bool DeveloperEnabled;
        public static readonly List<string> Messages = new();
        public static void Developer(string text) { if (DeveloperEnabled) Messages.Add(text); }
    }
}
