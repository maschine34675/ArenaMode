using System.Reflection;

namespace UnityEngine { public class MonoBehaviour { } public static class Time { public static float realtimeSinceStartup; } }
namespace ArenaMode.Config
{
    public sealed class Setting<T>(T value) { public T Value = value; }
    public static class Settings { public static Setting<bool> MaxSkills = new(true); }
}
namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
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
    public static class Priority { public const int First = 800, Last = 0; }
    public sealed class HarmonyMethod(Type type, string name)
    {
        public int priority;
        public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class PatchInfo { public List<Patch> Prefixes = new(), Postfixes = new(); }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static readonly Dictionary<MethodBase, PatchInfo> Bound = new();
        public static bool FailPatch, FailUnpatch;
        public void Patch(MethodInfo target, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (!Bound.TryGetValue(target, out var info)) Bound[target] = info = new();
            if (prefix != null) info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
            if (postfix != null) info.Postfixes.Add(new() { owner = id, PatchMethod = postfix.Method });
            if (FailPatch) throw new InvalidOperationException("injected patch failure after partial registration");
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("injected unpatch failure");
            foreach (var info in Bound.Values)
            { info.Prefixes.RemoveAll(p => p.owner == id); info.Postfixes.RemoveAll(p => p.owner == id); }
        }
        public static PatchInfo GetPatchInfo(MethodBase target) => Bound.GetValueOrDefault(target);
    }
    public static class AccessTools
    {
        public static bool MissingSettings;
        public static FieldInfo Field(Type type, string name)
            => MissingSettings ? null : type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace JsonType { public enum ESideType { Pmc, Savage } }
namespace EFT
{
    public enum GameStatus { Stopped, Running, Runned, Starting, Started, Stopping, SoftStopping }
    public interface IBotGame { }
    public class AbstractGame { }
    public class LocalGame : AbstractGame, IBotGame
    {
        public GameWorld GameWorld;
        public GameStatus Status = GameStatus.Started;
        public LocalRaidSettings _raidSettings;
        public Profile Profile;
        public Player LocalPlayer;
    }
    public class HideoutGame : AbstractGame, IBotGame { }
    public class GameWorld
    {
        public string LocationId;
        public Player MainPlayer;
        public void OnGameStarted() { }
        public void Dispose() { }
    }
    public class HideoutGameWorld : GameWorld { }
    public class Profile { public string Id; public SkillManager Skills = new(); }
    public class Player
    {
        public GameWorld GameWorld;
        public bool IsYourPlayer = true;
        public bool IsAI;
        public Profile Profile;
        public HealthSystem.PlayerHealthController HealthController { get; } = new();
        public HealthSystem.ActiveHealthController ActiveHealthController => HealthController;
        public MovementContext MovementContext { get; } = new();
        public delegate void DamageDelegate(float damage, EBodyPart part, EDamageType type, float absorbed, Ballistics.MaterialType material);
        public event DamageDelegate OnDamageReceived;
        public int DamageSubscribers => OnDamageReceived?.GetInvocationList().Length ?? 0;
        public void Damage() => OnDamageReceived?.Invoke(1, EBodyPart.LeftLeg, EDamageType.Bullet, 0, 0);
        public SkillManager Skills => Profile?.Skills;
        public object HandsController;
        public void RecalculateEquipmentParams() { }
        public class FirearmController { public void SyncWithCharacterSkills() { } }
    }
    public class HideoutPlayer : Player { }
    public class MovementContext { public bool Limited; }
    public class Skill
    {
        public int Level = 1, Writes;
        private int _buff;
        public Action<int> OnWrite, BeforeWrite;
        public int Buff { get => _buff; set { BeforeWrite?.Invoke(value); _buff = value; Writes++; OnWrite?.Invoke(value); } }
    }
    public enum ESkillId { Strength, Endurance }
    public enum EBodyPart { Common, LeftLeg }
    public enum EDamageType { Bullet }
    public sealed class Mastering { public float Current; public int Lvl1 = 100, Lvl2 = 200; }
    public sealed class SkillManager
    {
        public Skill[] DisplayList = { new() };
        public Skill RecoilControl = new();
        public Dictionary<string, Mastering> Mastering = new() { ["entry"] = new() { Current = 17 } };
        public FloatBuff ImmunityAvoidPoisonChance = new(), ImmunityAvoidMiscEffectsChance = new(),
            CharismaEliteScavCaseDiscount = new(), CharismaEliteFenceRepPenaltyReduction = new(),
            CharismaEliteAdditionalDailyQuests = new();
        public class FloatBuff { public float Value; }
        public event Action<Mastering> OnMasteringExperienceChanged;
        public int Subscribers => OnMasteringExperienceChanged?.GetInvocationList().Length ?? 0;
        public void Fire()
        {
            foreach (var callback in OnMasteringExperienceChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
                Program.Call(callback.Method, callback.Target, new Mastering());
        }
    }
    public class LocalRaidSettings { public string location, serverId; public JsonType.ESideType playerSide; }
}
namespace EFT.HealthSystem
{
    public interface IHealthEffect { }
    public sealed class Effect : IHealthEffect { }
    public interface IHealthController
    {
        bool IsAlive { get; }
        IEnumerable<IHealthEffect> GetAllEffects(EFT.EBodyPart part = EFT.EBodyPart.Common);
    }
    public class PlayerHealthController : ActiveHealthController { }
    public class ActiveHealthController : IHealthController
    {
        public bool IsAlive { get; set; } = true;
        public List<IHealthEffect> Effects = new();
        public Dictionary<EFT.ESkillId, EFT.Skill> Skills = new() { [EFT.ESkillId.Strength] = new() };
        public IEnumerable<IHealthEffect> GetAllEffects(EFT.EBodyPart part = EFT.EBodyPart.Common) => Effects;
        public event Action<IHealthEffect> EffectAddedEvent;
        public event Action<IHealthEffect> EffectStartedEvent;
        public event Action<IHealthEffect> EffectUpdatedEvent;
        public event Action<IHealthEffect> EffectResidualEvent;
        public event Action<IHealthEffect> EffectRemovedEvent;
        public event Action<EFT.EBodyPart, EFT.EDamageType> BodyPartDestroyedEvent;
        public int Subscribers => new Delegate[] { EffectAddedEvent, EffectStartedEvent, EffectUpdatedEvent,
            EffectResidualEvent, EffectRemovedEvent, BodyPartDestroyedEvent }.Sum(d => d?.GetInvocationList().Length ?? 0);
        public void Fire()
        {
            var effect = new Effect();
            EffectAddedEvent?.Invoke(effect); EffectStartedEvent?.Invoke(effect);
            EffectUpdatedEvent?.Invoke(effect); EffectResidualEvent?.Invoke(effect); EffectRemovedEvent?.Invoke(effect);
            BodyPartDestroyedEvent?.Invoke(EFT.EBodyPart.LeftLeg, EFT.EDamageType.Bullet);
        }
    }
}
namespace EFT.Ballistics { public enum MaterialType { Body } }
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static bool IsReplacing;
        public static event Action<EFT.Player, EFT.Player, string> Replaced;
        public static event Action RaidEnded;
        public static void Fire(EFT.Player old, EFT.Player next) => Replaced?.Invoke(old, next, "test");
        public static void End() => RaidEnded?.Invoke();
    }
}
namespace Redeploy.Core.GameAccess
{
    public static class LocalGameAccessor
    {
        public static bool FailRead;
        public static EFT.LocalRaidSettings GetLocalRaidSettings(EFT.LocalGame game)
            => FailRead ? throw new InvalidOperationException("injected settings read failure") : game._raidSettings;
    }
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
        public List<string> Info = new(), Debug = new(), Warnings = new();
        public void LogInfo(string text) => Info.Add(text);
        public void LogDebug(string text) => Debug.Add(text);
        public void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool ArmedForNextRaid, IsActive;
        public static string ArmedMap;
        public static EFT.AbstractGame ActiveGame;
        public static EFT.GameWorld ActiveWorld;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(ActiveGame, game) && ReferenceEquals(ActiveWorld, world);
    }
    public static class ArenaServerClient { public static bool HasPendingArm; public static string PendingArmToken; }
}
