using System.Reflection;

namespace UnityEngine
{
    public readonly record struct Scene(string path, int handle = 1, bool isLoaded = true)
    {
        public bool IsValid() => handle > 0;
    }
    public class GameObject
    {
        public string name;
        public Scene scene;
        public Transform transform = new();
    }
    public class Transform
    {
        public Transform Parent;
        public bool IsChildOf(Transform other) => ReferenceEquals(this, other) || Parent?.IsChildOf(other) == true;
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
    }
    public class AudioClip { public string name; }
    public class AudioSource : MonoBehaviour
    {
        public AudioClip clip;
        public bool Scheduled = true;
        public bool ThrowOnStop;
        public int Stops;
        public void Stop()
        {
            if (ThrowOnStop) throw new InvalidOperationException("destroyed source");
            Stops++;
            Scheduled = false;
        }
    }
}
namespace Audio.AudioCulling
{
    public class SyncLoopSoundPlayer : UnityEngine.MonoBehaviour
    {
        public UnityEngine.AudioSource[] _sources;
        public UnityEngine.AudioClip _clip;
        public bool _playOnAwake;
        public bool _useSoundDelayCalculation = true;
        public void InitializeSource(UnityEngine.AudioSource source) { }
    }
}
namespace EFT
{
    public class GameWorld { public string LocationId = "labyrinth"; }
    public class AbstractGame { }
}
namespace EFT.GameTriggers { public interface ITriggerEntity { } }
namespace CommonAssets.Scripts.Game
{
    public class SyncableLoopSoundPlayer : UnityEngine.MonoBehaviour, EFT.GameTriggers.ITriggerEntity
    {
        public string _playTriggerId, _stopTriggerId;
        public global::Audio.AudioCulling.SyncLoopSoundPlayer _loopSoundPlayer;
    }
}
public static class LocationScene
{
    public static int Reads;
    public static List<EFT.GameTriggers.ITriggerEntity> Entities = new();
    public static IEnumerable<T> GetAll<T>()
    {
        Reads++;
        return Entities.OfType<T>();
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
namespace ArenaMode.Session
{
    public static class ArenaSession
    {
        public static bool IsActive = true;
        public static EFT.GameWorld World;
        public static EFT.AbstractGame Game;
        public static bool IsExactActiveRaid(EFT.AbstractGame game, EFT.GameWorld world)
            => IsActive && ReferenceEquals(game, Game) && ReferenceEquals(world, World);
    }
}
namespace ArenaMode.Config
{
    public class Entry<T> { public T Value; }
    public static class Settings
    {
        public static Entry<bool> MuteLabyrinthAlarm = new() { Value = true };
    }
}
namespace ArenaMode
{
    public class Logger
    {
        public List<string> Messages = new();
        public void LogInfo(string message) => Messages.Add(message); public void LogDebug(string message) => Messages.Add(message);
        public void LogWarning(string message) => Messages.Add(message);
    }
    public static class Plugin { public static Logger ModLogger = new(); }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters)
            => type.GetMethod(name, parameters);
    }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefix : Attribute { }
    public abstract class ModulePatch
    {
        public static MethodBase Target;
        public static MethodInfo InstalledPrefix;
        public static bool Active;
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            Target = GetTargetMethod() ?? throw new Exception("missing hook");
            InstalledPrefix = GetType().GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(m => m.GetCustomAttribute<PatchPrefix>() != null);
            Active = true;
        }
        public void Disable() => Active = false;
    }
}
