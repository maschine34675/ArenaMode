// Only transport, Unity audio, settings and the match event are stand-ins. The
// linked production service owns loading, subscriptions, filtering and routing.
// This harness does not decode Ogg files or exercise a Unity audio device.
namespace ArenaMode
{
    internal static class Plugin
    {
        public static TestLogger ModLogger { get; } = new();
        public static TestPlugin Instance { get; } = new();
    }

    internal sealed class TestPlugin : UnityEngine.Component
    {
        public TestPlugin() => gameObject = new UnityEngine.GameObject();
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
    [Flags]
    internal enum HideFlags
    {
        None = 0,
        DontUnloadUnusedAsset = 32,
    }

    // Unity wrappers survive after their native object is destroyed: reference
    // identity remains, but == null and boolean conversion report Unity-null.
    internal class Object
    {
        private bool _destroyed;
        public HideFlags hideFlags { get; set; }
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
        private int _samples = 8;
        private int _channels = 2;
        private int _frequency = 44100;
        private float[] _data =
            { 0.25f, -0.25f, 0.1f, -0.1f, 0.05f, -0.05f, 0.2f, -0.2f,
              0.25f, -0.25f, 0.1f, -0.1f, 0.05f, -0.05f, 0.2f, -0.2f };
        public static List<AudioClip> CreatedClips { get; } = new();
        public static bool NextCreatedGetDataResult { get; set; } = true;
        public string Name { get; set; }
        public string name => Name;
        public AudioDataLoadState loadState { get; set; } = AudioDataLoadState.Loaded;
        public bool LoadAudioDataResult { get; set; } = true;
        public bool CompleteLoadSynchronously { get; set; } = true;
        public bool GetDataResult { get; set; } = true;
        public bool SetDataResult { get; set; } = true;
        public bool CreatedFromPcm { get; private set; }
        public int GetDataCalls { get; private set; }
        public int SetDataCalls { get; private set; }
        public float[] Data => _data;
        public int LoadAudioDataCalls { get; private set; }
        public bool LoadAudioData()
        {
            LoadAudioDataCalls++;
            if (LoadAudioDataResult && CompleteLoadSynchronously) loadState = AudioDataLoadState.Loaded;
            return LoadAudioDataResult;
        }
        public bool GetData(float[] data, int offsetSamples)
        {
            GetDataCalls++;
            if (!GetDataResult) return false;
            Array.Copy(_data, offsetSamples * _channels, data, 0, Math.Min(data.Length, _data.Length));
            return true;
        }
        public bool SetData(float[] data, int offsetSamples)
        {
            SetDataCalls++;
            if (!SetDataResult) return false;
            _data = data.ToArray();
            return true;
        }
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream)
        {
            var clip = new AudioClip
            {
                Name = name,
                _samples = lengthSamples,
                _channels = channels,
                _frequency = frequency,
                _data = new float[checked(lengthSamples * channels)],
                CreatedFromPcm = true,
                GetDataResult = NextCreatedGetDataResult
            };
            NextCreatedGetDataResult = true;
            CreatedClips.Add(clip);
            return clip;
        }
        public static AudioClip CreateDecodedFixture(string name, int lengthSamples, int channels, int frequency)
        {
            var data = new float[checked(lengthSamples * channels)];
            data[0] = 0.25f;
            return new AudioClip
            {
                Name = name,
                _samples = lengthSamples,
                _channels = channels,
                _frequency = frequency,
                _data = data,
            };
        }
        public void SimulateAudioReset()
        {
            // Matches the observed EFT/Unity failure shape: the managed wrapper
            // survives and still says Loaded, but its native frame/channel data
            // and duration are gone after AudioSettings.Reset.
            _samples = 0;
            _channels = 0;
            _data = Array.Empty<float>();
            loadState = AudioDataLoadState.Loaded;
        }
        public float length => _frequency <= 0 ? 0f : _samples / (float)_frequency;
        public int channels => _channels;
        public int frequency => _frequency;
        public int samples => _samples;
    }
    internal enum AudioDataLoadState { Unloaded, Loading, Loaded, Failed }
    internal enum AudioType { OGGVORBIS }
    internal enum AudioSpeakerMode { Raw, Mono, Stereo, Quad, Surround, Mode5point1, Mode7point1, Prologic }
    internal struct AudioConfiguration
    {
        public AudioSpeakerMode speakerMode;
        public int sampleRate;
        public int numRealVoices;
        public int numVirtualVoices;
    }
    internal static class AudioSettings
    {
        public static int SimulatedResetCount { get; private set; }
        public static int outputSampleRate { get; set; } = 48000;
        public static int BufferLength { get; set; } = 1024;
        public static int BufferCount { get; set; } = 4;
        public static AudioConfiguration Configuration { get; set; } = new()
        {
            speakerMode = AudioSpeakerMode.Mode5point1,
            sampleRate = 48000,
            numRealVoices = 32,
            numVirtualVoices = 512
        };
        public static AudioConfiguration GetConfiguration() => Configuration;
        public static void GetDSPBufferSize(out int bufferLength, out int numBuffers)
        {
            bufferLength = BufferLength;
            numBuffers = BufferCount;
        }
        public static void SimulateReset()
        {
            SimulatedResetCount++;
            foreach (var clip in AudioClip.CreatedClips)
                if (clip != null) clip.SimulateAudioReset();
        }
    }
    internal static class Time { public static float realtimeSinceStartup = 100f; }
    internal static class AudioListener { public static float volume = 0.8f; public static bool pause; }

    internal class Component : Object
    {
        public GameObject gameObject { get; internal set; }
    }

    internal sealed class GameObject : Object
    {
        public AudioSource LastAudioSource { get; private set; }
        public bool activeInHierarchy { get; set; } = true;

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            if (component is AudioSource source)
            {
                LastAudioSource = source;
            }
            return component;
        }
    }

    internal sealed class AudioSource : Component
    {
        public bool playOnAwake { get; set; }
        public bool loop { get; set; }
        public float spatialBlend { get; set; }
        public bool spatialize { get; set; }
        public float dopplerLevel { get; set; }
        public bool bypassReverbZones { get; set; }
        public int priority { get; set; }
        public UnityEngine.Audio.AudioMixerGroup outputAudioMixerGroup { get; set; }
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public bool enabled { get; set; } = true;
        public bool mute { get; set; }
        private bool _isPlaying;
        private bool _isVirtual;
        private bool _startPending;
        private float _startAt;
        public bool isPlaying { get { RefreshPendingStart(); return _isPlaying; } }
        public bool isVirtual { get { RefreshPendingStart(); return _isVirtual; } set => _isVirtual = value; }
        public bool ForceSilent { get; set; }
        public float StartDelaySeconds { get; set; }
        public int StopCalls { get; private set; }
        public void Stop()
        {
            StopCalls++;
            _startPending = false;
            _isPlaying = false;
        }
        public void Play()
        {
            if (ForceSilent)
            {
                _startPending = false;
                _isPlaying = false;
                _isVirtual = true;
                return;
            }
            if (StartDelaySeconds > 0f)
            {
                _startPending = true;
                _startAt = Time.realtimeSinceStartup + StartDelaySeconds;
                _isPlaying = false;
                _isVirtual = true;
                return;
            }
            StartNow(clip, volume);
        }
        public void PlayOneShot(AudioClip oneShot, float volumeScale)
        {
            if (ForceSilent)
            {
                _isPlaying = false;
                _isVirtual = true;
                return;
            }
            StartNow(oneShot, volumeScale);
        }
        public void FinishPlayback()
        {
            _startPending = false;
            _isPlaying = false;
            _isVirtual = true;
        }
        private void RefreshPendingStart()
        {
            if (_startPending && Time.realtimeSinceStartup >= _startAt)
            {
                _startPending = false;
                StartNow(clip, volume);
            }
        }
        private void StartNow(AudioClip playingClip, float playingVolume)
        {
            _isPlaying = true;
            _isVirtual = false;
        }
    }
}

namespace UnityEngine.Audio
{
    internal sealed class AudioMixer : UnityEngine.Object
    {
        public Dictionary<string, float> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["UIVolume"] = -4f,
            ["MasterVolume"] = -2f
        };
        public bool GetFloat(string name, out float value) => Parameters.TryGetValue(name, out value);
        public AudioMixerGroup[] FindMatchingGroups(string name)
            => name == "UI" ? new[] { new AudioMixerGroup { name = "UI" } } : Array.Empty<AudioMixerGroup>();
    }

    internal sealed class AudioMixerGroup : UnityEngine.Object { public string name { get; set; } }
}

internal sealed class BetterAudio
{
    public static bool Instantiated { get; set; } = true;
    public static BetterAudio Instance { get; } = new();
    public UnityEngine.Audio.AudioMixerGroup NonSpatialBypassMixer { get; set; } =
        new() { name = "NonspatialBypass" };
    public UnityEngine.Audio.AudioMixer Master { get; set; } = new();
}

namespace EFT.UI
{
    internal sealed class GUISounds : UnityEngine.Component
    {
        private readonly UnityEngine.AudioSource audioSource_4;
        public readonly record struct Playback(
            UnityEngine.AudioClip Clip,
            bool Single,
            bool CommonUiSound,
            float Volume);
        public List<Playback> PlayRequests { get; } = new();
        public bool isActiveAndEnabled { get; set; } = true;
        public UnityEngine.Audio.AudioMixer MasterMixer { get; set; } = new();
        public bool ThrowOnPlay { get; set; }
        internal UnityEngine.AudioSource SingleSource => audioSource_4;
        public GUISounds()
        {
            gameObject = new UnityEngine.GameObject();
            audioSource_4 = new UnityEngine.AudioSource { gameObject = gameObject, priority = 128 };
            audioSource_4.outputAudioMixerGroup = MasterMixer.FindMatchingGroups("UI")[0];
        }
        public void PlaySound(UnityEngine.AudioClip clip, bool single, bool commonUiSound, float volume)
        {
            if (ThrowOnPlay) throw new InvalidOperationException("injected native PlaySound failure");
            // Capture the public contract synchronously, independently from any
            // simulated device/source state. The service only owns this call.
            PlayRequests.Add(new Playback(clip, single, commonUiSound, volume));
            if (single)
            {
                audioSource_4.Stop();
                if (MasterMixer != null)
                    audioSource_4.outputAudioMixerGroup = MasterMixer.FindMatchingGroups("UI")[0];
                audioSource_4.PlayOneShot(clip, volume);
            }
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
