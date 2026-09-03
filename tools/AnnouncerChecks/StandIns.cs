// Only transport, Unity audio, settings and the match event are stand-ins. The
// linked production service owns loading, subscriptions, filtering and routing.
// This harness does not decode Ogg files or exercise a Unity audio device.
namespace ArenaMode
{
    internal static class Plugin
    {
        public static TestLogger ModLogger { get; } = new();
    }

    internal sealed class TestLogger
    {
        public List<string> Messages { get; } = new();
        public void LogInfo(object message) => Messages.Add(message.ToString());
        public void LogWarning(object message) => Messages.Add(message.ToString());
    }
}

namespace ArenaMode.Config
{
    internal sealed class Entry<T> { public T Value; }

    internal static class Settings
    {
        public static Entry<bool> AnnouncerEnabled { get; } = new() { Value = true };
        public static Entry<float> AnnouncerVolume { get; } = new() { Value = 0.37f };
    }
}

namespace ArenaMode.Modes
{
    internal sealed class ArenaMatch
    {
        public event Action<string> OnCallout;
        public event Action<ArenaAnnouncerCue> OnAnnouncerCue;
        public void Callout(string label) => OnCallout?.Invoke(label);
        public void Cue(ArenaAnnouncerCue cue) => OnAnnouncerCue?.Invoke(cue);
    }
}

namespace Comfort.Common
{
    internal static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}

namespace UnityEngine
{
    // Unity wrappers survive after their native object is destroyed: reference
    // identity remains, but == null and boolean conversion report Unity-null.
    internal class Object
    {
        private bool _destroyed;
        public static void Destroy(Object value)
        {
            if (!ReferenceEquals(value, null)) value._destroyed = true;
        }
        private static bool IsNull(Object value) => ReferenceEquals(value, null) || value._destroyed;
        public static bool operator ==(Object left, Object right)
            => IsNull(left) ? IsNull(right) : !IsNull(right) && ReferenceEquals(left, right);
        public static bool operator !=(Object left, Object right) => !(left == right);
        public static implicit operator bool(Object value) => !IsNull(value);
        public override bool Equals(object other)
            => other is Object unityObject ? this == unityObject : other is null && this == null;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    internal sealed class AudioClip : Object
    {
        public string Name { get; init; }
        public string name => Name;
        public AudioDataLoadState loadState { get; set; } = AudioDataLoadState.Loaded;
        public bool LoadAudioDataResult { get; set; } = true;
        public bool CompleteLoadSynchronously { get; set; } = true;
        public int LoadAudioDataCalls { get; private set; }
        public bool LoadAudioData()
        {
            LoadAudioDataCalls++;
            if (LoadAudioDataResult && CompleteLoadSynchronously) loadState = AudioDataLoadState.Loaded;
            return LoadAudioDataResult;
        }
        public float length => 1.5f;
        public int channels => 2;
        public int frequency => 44100;
        public int samples => 66150;
    }
    internal enum AudioDataLoadState { Unloaded, Loading, Loaded, Failed }
    internal enum AudioType { OGGVORBIS }
    internal static class Time { public static float realtimeSinceStartup = 100f; }
    internal static class AudioListener { public static float volume = 0.8f; public static bool pause; }
}

namespace UnityEngine.Audio
{
    internal sealed class AudioMixer : UnityEngine.Object
    {
        public Dictionary<string, float> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["InterfaceVolume"] = -4f,
            ["UIVolume"] = -4f,
            ["MasterVolume"] = -2f
        };
        public bool GetFloat(string name, out float value) => Parameters.TryGetValue(name, out value);
    }
}

namespace EFT.UI
{
    internal sealed class GUISounds : UnityEngine.Object
    {
        public readonly record struct Playback(UnityEngine.AudioClip Clip, bool Single, bool CommonUiSound, float Volume);
        public List<Playback> Played { get; } = new();
        public bool isActiveAndEnabled { get; set; } = true;
        public UnityEngine.Audio.AudioMixer MasterMixer { get; set; } = new();
        public bool ThrowOnPlay { get; set; }
        public void PlaySound(UnityEngine.AudioClip clip, bool single, bool commonUiSound, float volume)
        {
            if (ThrowOnPlay) throw new InvalidOperationException("injected native PlaySound failure");
            Played.Add(new Playback(clip, single, commonUiSound, volume));
        }
    }
}

namespace UnityEngine.Networking
{
    internal sealed class UnityWebRequestAsyncOperation
    {
        public bool isDone;
    }

    internal sealed class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }

        public string Url { get; init; }
        public AudioType Type { get; init; }
        public Result result = Result.InProgress;
        public string error;
        public int timeout;
        public bool Disposed { get; private set; }
        public int ContentReadCount { get; set; }
        public bool ThrowOnGetContent { get; set; }
        public bool ReturnNullContent { get; set; }
        public AudioClip Content { get; set; }
        public UnityWebRequestAsyncOperation Operation { get; } = new();
        public UnityWebRequestAsyncOperation SendWebRequest() => Operation;
        public void Complete(Result value)
        {
            result = value;
            error = value == Result.Success ? null : "injected audio decoding failure";
            Operation.isDone = true;
        }
        public void Dispose() => Disposed = true;
    }

    internal static class UnityWebRequestMultimedia
    {
        public static List<UnityWebRequest> Requests { get; } = new();
        public static UnityWebRequest GetAudioClip(string url, AudioType audioType)
        {
            var request = new UnityWebRequest { Url = url, Type = audioType };
            Requests.Add(request);
            return request;
        }
    }

    internal static class DownloadHandlerAudioClip
    {
        public static AudioClip GetContent(UnityWebRequest request)
        {
            request.ContentReadCount++;
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException("A failed audio request must not reach GetContent.");
            if (request.ThrowOnGetContent)
                throw new InvalidOperationException("injected GetContent decoder exception");
            if (request.ReturnNullContent) return null;
            return request.Content ??= new AudioClip
            {
                Name = Path.GetFileNameWithoutExtension(new Uri(request.Url).LocalPath)
            };
        }
    }
}

internal sealed class QueuedContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object State)> _pending = new();
    public int OutstandingAsyncVoids { get; private set; }
    public override void Post(SendOrPostCallback callback, object state) => _pending.Enqueue((callback, state));
    public override void OperationStarted() => OutstandingAsyncVoids++;
    public override void OperationCompleted() => OutstandingAsyncVoids--;

    public void PumpUntil(Func<bool> completed)
    {
        for (var attempts = 0; attempts < 1000 && !completed(); attempts++)
        {
            if (!_pending.TryDequeue(out var work))
                throw new InvalidOperationException("Loading stopped before the expected continuation. "
                    + string.Join(" | ", ArenaMode.Plugin.ModLogger.Messages));
            work.Callback(work.State);
        }
        if (!completed())
            throw new InvalidOperationException("The bounded async continuation pump did not finish.");
    }
}
