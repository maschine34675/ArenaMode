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
        public void Callout(string label) => OnCallout?.Invoke(label);
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
    internal sealed class AudioClip { public string Name { get; init; } }
    internal enum AudioType { OGGVORBIS }
}

namespace EFT.UI
{
    internal sealed class GUISounds
    {
        public readonly record struct Playback(UnityEngine.AudioClip Clip, bool Single, bool CommonUiSound, float Volume);
        public List<Playback> Played { get; } = new();
        public void PlaySound(UnityEngine.AudioClip clip, bool single, bool commonUiSound, float volume)
            => Played.Add(new Playback(clip, single, commonUiSound, volume));
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
        public bool Disposed { get; private set; }
        public int ContentReadCount { get; set; }
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
            return new AudioClip { Name = Path.GetFileNameWithoutExtension(new Uri(request.Url).LocalPath) };
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
