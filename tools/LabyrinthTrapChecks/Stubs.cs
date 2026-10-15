using System.Reflection;

namespace UnityEngine
{
    public readonly record struct Scene(string path, int handle = 1, bool isLoaded = true)
    { public bool IsValid() => handle > 0; }
    public class GameObject { public string name; public Scene scene; }
    public class Coroutine { public Action Cancel; }
    public class MonoBehaviour
    {
        public GameObject gameObject = new();
        public void StopCoroutine(Coroutine coroutine) => coroutine.Cancel?.Invoke();
    }
    public class Animator : MonoBehaviour
    {
        public Dictionary<string, bool> Values = new();
        public int Calls;
        public void SetBool(string name, bool value) { Values[name] = value; Calls++; }
    }
    public static class Time { public static float unscaledTime; }
}
namespace EFT.GameTriggers
{
    public interface ITriggerEntity { }
    public class TriggerEvent { }
    public class TriggersEmitter
    {
        public HashSet<string> Ignored = new();
        public void SetTriggerState(string id, bool state)
        { if (state) Ignored.Remove(id); else Ignored.Add(id); }
        public void Emit(string id, string origin) => throw new Exception("Global events must not be emitted");
    }
    public class LocalTriggersEmitter : TriggersEmitter { }
    public class HandlerDamage : UnityEngine.MonoBehaviour, ITriggerEntity
    {
        public string _triggerIdStartDamage, _triggerIdFinishDamage, ConfigurationId;
        public bool DamageRunning = true;
        public int Finishes;
        public void OnTriggerStartDamage(TriggerEvent e) { }
        public void OnTriggerFinishDamage(TriggerEvent e) { DamageRunning = false; Finishes++; }
    }
    public class HandlerShoot : UnityEngine.MonoBehaviour, ITriggerEntity
    {
        public string _triggerId, ConfigurationId;
        public void OnShootEvent(TriggerEvent e) { }
    }
    public class HandlerEffect : UnityEngine.MonoBehaviour, ITriggerEntity
    {
        public string _triggerId;
        public void HandleEvent(TriggerEvent e) { }
    }
    public class HandlerTriggerState : UnityEngine.MonoBehaviour, ITriggerEntity
    {
        public string _triggerId;
        public string[] _controlledTriggerId;
        public bool _targetState;
        public int Calls;
        public void OnTrigger()
        {
            Calls++;
            foreach (var id in _controlledTriggerId)
                Comfort.Common.Singleton<EFT.GameWorld>.Instance.TriggersEmitter.SetTriggerState(id, _targetState);
        }
    }
    public class HandlerAnimator : UnityEngine.MonoBehaviour, ITriggerEntity
    {
        public struct BoolAction { public string Name; public bool Value; }
        public struct IntAction { public string Name; }
        public struct FloatAction { public string Name; }
        public struct TriggerAction { public string TriggerName; }
        public struct AnimatorAction
        {
            public string TriggerId;
            public BoolAction BoolAction;
            public IntAction IntAction;
            public FloatAction FloatAction;
            public TriggerAction AnimationTriggerAction;
            public void Execute(UnityEngine.Animator target) => target.SetBool(BoolAction.Name, BoolAction.Value);
        }
        public AnimatorAction[] _animatorActions;
        public UnityEngine.Animator _target;
    }
    public class HandlerPlaySoundSequenced : UnityEngine.MonoBehaviour, ITriggerEntity
    {
        public string _playTriggerId, _stopTriggerId;
        public bool _playOnSoundSystemInit, _playOnce;
        public bool PendingStart = true, PendingEnd, LoopSource = true, OneShotSource = true;
        private UnityEngine.Coroutine _playCoroutine;
        private bool _isPlayed;
        public bool Fading;
        public bool FailNextStop;
        public int LoopReleases, OneShotReleases;
        public int Stops, CoroutineStops;
        public HandlerPlaySoundSequenced()
        {
            _playCoroutine = new() { Cancel = () => { PendingStart = false; CoroutineStops++; } };
        }
        public void PlaySound() { }
        public void StopAllCoroutines() { PendingStart = PendingEnd = false; CoroutineStops++; }
        public void StopSound()
        {
            if (FailNextStop) { FailNextStop = false; throw new InvalidOperationException("transient stop failure"); }
            Stops++;
            if (_isPlayed) return;
            Fading = LoopSource;
            PendingEnd = OneShotSource;
            _isPlayed = true;
        }
        public void OnLoopSourceFadeOut()
        {
            if (!LoopSource) return;
            // Native Stop() invokes a still-pending fade callback synchronously.
            // Calling the release callback by hand while fading re-enters it.
            if (Fading) { Fading = false; OnLoopSourceFadeOut(); }
            if (!LoopSource) throw new NullReferenceException("recursive native loop release");
            LoopSource = false; LoopReleases++;
        }
        public void OnEndSoundPlayed()
        {
            if (OneShotSource) { OneShotSource = false; OneShotReleases++; }
        }
        public void AdvanceNativeStop()
        {
            if (Fading) { Fading = false; OnLoopSourceFadeOut(); }
            if (PendingEnd) { PendingEnd = false; OnEndSoundPlayed(); }
        }
    }
}
namespace EFT
{
    public class GameWorld
    {
        public string LocationId = "labyrinth";
        public GameTriggers.TriggersEmitter TriggersEmitter = new GameTriggers.LocalTriggersEmitter();
    }
    public class AbstractGame { }
}
public static class LocationScene
{
    public static int Reads;
    public static List<EFT.GameTriggers.ITriggerEntity> Entities = new();
    public static IEnumerable<T> GetAll<T>() { Reads++; return Entities.OfType<T>(); }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive;
        public static EFT.GameWorld World;
        public static EFT.AbstractGame Game;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode.Config
{
    public class Entry<T> { public T Value; }
    public static class Settings { public static Entry<bool> DisableLabyrinthSpawnTraps = new(); }
}
namespace ArenaMode
{
    public class Logger
    {
        public List<string> Lines = new();
        public void LogInfo(string text) => Lines.Add(text); public void LogDebug(string text) => Lines.Add(text);
        public void LogWarning(string text) => Lines.Add(text);
    }
    public static class Plugin { public static Logger ModLogger = new(); }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, args);
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefix : Attribute { }
    public abstract class ModulePatch
    {
        public static Dictionary<Type, MethodInfo> Hooks = new();
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            var target = GetTargetMethod() ?? throw new Exception("Hook not found");
            Hooks[target.DeclaringType] = GetType().GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(m => m.GetCustomAttribute<PatchPrefix>() != null);
        }
        public void Disable() => Hooks.Remove(GetTargetMethod().DeclaringType);
    }
}
