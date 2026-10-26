// Native seams only. Production owns all eligibility, queue, rotation, lease,
// async continuation and restoration decisions. This is not an audio/GPU test.
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object { }
    public struct Vector3 { }
    public struct Quaternion { }
    public class AudioClip { }
    public class AudioSource
    {
        public AudioClip clip;
        public int Stops;
        public void Stop() { Stops++; }
    }
    public static class Time { public static float time; }
    public static class Random { public static int Value; public static int Range(int a, int b) => Math.Clamp(Value, a, b - 1); }
}
public enum EPhraseTrigger { None, PhraseNone, OnWeaponReload, OnGrenade, OnOutOfAmmo, EnemyDown, OnDeath, OnBeingHurt }
[Flags] public enum ETagStatus { Healthy = 1, Combat = 2, Usec = 4, Bear = 8 }
public class TaggedClip { public UnityEngine.AudioClip Clip = new(); public float Length = 1f; }
public class TagBank { public EPhraseTrigger Trigger; }
public class Voice { public TagBank[] Banks; }
public static class InGameBundles { public static string TakePhrasePath(string voice) => voice; }
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace Diz.DependencyManager
{
    public class DependencyGraph<T>
    {
        public class Token
        {
            public int Releases;
            public string Voice;
            public TaskCompletionSource<bool> Completion = new();
            public Task Task => Completion.Task;
            public void Release() { Releases++; }
        }
    }
}
namespace Diz.Resources
{
    public interface IEasyBundle { }
    public class IEasyAssets
    {
        public readonly List<Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token> Tokens = new();
        public bool CompleteImmediately = true, Missing;
        public static Voice MakeVoice() => new() { Banks = new[] { EPhraseTrigger.OnWeaponReload, EPhraseTrigger.OnGrenade,
            EPhraseTrigger.OnOutOfAmmo, EPhraseTrigger.EnemyDown, EPhraseTrigger.OnDeath, EPhraseTrigger.OnBeingHurt }
            .Select(x => new TagBank { Trigger = x }).ToArray() };
        public Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token Retain(string[] paths)
        {
            var token = new Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token { Voice = paths.Single() };
            Tokens.Add(token); if (CompleteImmediately) token.Completion.SetResult(true); return token;
        }
        public bool TryGetAsset<T>(out T asset, string key) where T : class
        { asset = Missing ? null : MakeVoice() as T; return asset != null; }
    }
    public static class EasyAssetsExtensions
    {
        public static Task LoadBundles(Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token token) => token.Task;
    }
}
namespace EFT
{
    public enum GameStatus { Started, Stopping, Stopped }
    public enum EPlayerSide { Usec, Bear, Savage }
    public class AbstractGame { public GameStatus Status; }
    public class Profile { public string Id = "local"; public EPlayerSide Side = EPlayerSide.Usec; public string Voice = "Original"; }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public Profile Profile = new(); }
    public class GameWorld
    {
        public Player MainPlayer, Registered;
        public Player GetAlivePlayerByProfileID(string profile) => Registered?.ProfileId == profile ? Registered : null;
    }
    public class Grenade { }
    public class BetterSource { public UnityEngine.AudioSource source1 = new(); }
    public class BaseSpeaker
    {
        public readonly Dictionary<EPhraseTrigger, TagBank> PhrasesBanks = Diz.Resources.IEasyAssets.MakeVoice().Banks.ToDictionary(x => x.Trigger);
        public string PlayerVoice = "Original";
        public ETagStatus SideTag = ETagStatus.Usec;
        public float ReleaseTime, QueuedDelay;
        public bool Busy => UnityEngine.Time.time < ReleaseTime;
        public EPhraseTrigger QueuedEvent = EPhraseTrigger.PhraseNone;
        public TaggedClip Clip;
        public readonly List<EPhraseTrigger> Played = new();
        public Action AfterPlay;
        public event Action<EPhraseTrigger, TaggedClip, TagBank, BaseSpeaker> OnPhraseTold;
        public bool Reject;
        public Player Owner;
        public TagBank Play(EPhraseTrigger phrase, ETagStatus mask, bool demand)
        {
            if (Reject) return null;
            Played.Add(phrase); Clip = new TaggedClip(); ReleaseTime = UnityEngine.Time.time + Clip.Length;
            Owner.Source.source1.clip = Clip.Clip;
            var emitted = Clip;
            AfterPlay?.Invoke();
            OnPhraseTold?.Invoke(phrase, emitted, PhrasesBanks[phrase], this);
            return PhrasesBanks[phrase];
        }
    }
    public class Player
    {
        public class Health { public bool IsAlive = true; }
        public bool IsAI, IsYourPlayer = true;
        public string ProfileId = "local";
        public Health HealthController = new();
        public ETagStatus HealthStatus = ETagStatus.Healthy;
        public object HandsController;
        public BaseSpeaker Speaker;
        public BetterSource _speechSource = new();
        public BetterSource Source => _speechSource;
        public int SourceReads;
        public BetterSource SpeechSource { get { SourceReads++; return Source; } }
        public Player() { Speaker = new BaseSpeaker { Owner = this }; }
        public class FirearmController
        {
            public Player _player;
            public bool Reloading;
            public bool IsInReloadOperation() => Reloading;
            public void DryShot(int chamberIndex = 0, bool underbarrelShot = false)
                => HarmonyLib.Harmony.Invoke(nameof(DryShot), this);
        }
        public class BaseGrenadeHandsController
        {
            public Player _player;
            public Grenade CreateAndRegisterGrenade(UnityEngine.Vector3 p, UnityEngine.Quaternion r, UnityEngine.Vector3 f, float prewarm = 0)
            {
                var grenade = new Grenade(); HarmonyLib.Harmony.Invoke(nameof(CreateAndRegisterGrenade), this, grenade); return grenade;
            }
        }
    }
}
namespace HarmonyLib
{
    public class Harmony
    {
        private static readonly Dictionary<string, MethodInfo> Hooks = new();
        public Harmony(string id) { }
        public void Patch(MethodInfo target, HarmonyMethod postfix) => Hooks.Add(target.Name, postfix.Method);
        public void UnpatchSelf() => Hooks.Clear();
        public static void Invoke(string name, params object[] args) { if (Hooks.TryGetValue(name, out var method)) method.Invoke(null, args); }
    }
    public class HarmonyMethod
    {
        public MethodInfo Method;
        public HarmonyMethod(Type type, string name) { Method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static); }
    }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] args) => type.GetMethod(name, args);
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }
}
namespace Redeploy.Core.Engine
{
    public static class RaidLifecycle { public static event Action OnRaidEnded; public static void End() => OnRaidEnded?.Invoke(); }
}
namespace ArenaMode
{
    public static class Plugin
    {
        public const string PluginGuid = "fixture";
        public static readonly Logger ModLogger = new();
        public class Logger { public readonly List<string> Warnings = new(); public void LogWarning(string s) => Warnings.Add(s); }
    }
}
namespace ArenaMode.Config { public class Entry { public bool Value = true; } public static class Settings { public static Entry PmcVoiceEnabled = new(); } }
namespace ArenaMode.Diagnostics
{
    public static class ArenaLog { public static bool DeveloperEnabled = true; public static readonly List<string> Lines = new(); public static void Developer(string s) => Lines.Add(s); }
}
namespace ArenaMode.Audio { public static class AnnouncerService { public static bool IsPmcVoiceBlocked; } }
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static int ArmEpoch;
        public static EFT.AbstractGame Game;
        public static EFT.GameWorld World;
        public static EFT.EPlayerSide? Selected = EFT.EPlayerSide.Bear;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
        public static EFT.EPlayerSide? TeamDeathmatchLocalSide(EFT.LocalGame game, EFT.GameWorld world) => Selected;
    }
}
namespace ArenaMode.Modes
{
    public static class ArenaModeCatalog { public const string TeamDeathmatchId = "teamdeathmatch"; }
    public class Mode { public bool IsComplete; public string Id = ArenaModeCatalog.TeamDeathmatchId; }
    public class ArenaMatch
    {
        public Mode Mode = new();
        public event Action<ArenaMode.Combat.ArenaKill> OnKill;
        public void Kill(ArenaMode.Combat.ArenaKill kill) => OnKill?.Invoke(kill);
    }
    public static class ArenaMatchService { public static bool TeamStartAbortPending; public static ArenaMatch Current; }
}
namespace ArenaMode.Combat
{
    public class ArenaKill
    {
        public bool KilledByLocal = true, LocalIsVictim, AwardEligible = true, ScoreEligible = true;
        public bool? VictimIsFriendly;
        public long KillerBodyId;
    }
    public static class ArenaHitHistory { public static long BodyId(object body) => RuntimeHelpers.GetHashCode(body); }
}
namespace ArenaMode.Areas { public static class ArenaAreaRuntime { public static bool IsPending; } }
namespace ArenaMode.Respawn { public static class ArenaRespawnDriver { public static bool StandDown, IsRespawnInProgress; } }
namespace ArenaMode.Spectator
{
    public static class ArenaSpectatorService { public static bool IsActive; }
    public static class KillcamCompat { public static bool CameraReleased = true; }
}
