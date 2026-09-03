using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace ArenaMode.Audio;

/// <summary>
/// Announcer stingers: .ogg files from &lt;plugin dir&gt;/ArenaMode/sounds keyed by
/// filename. Kill labels are normalized to keys ("DOUBLE KILL" becomes
/// "double_kill"); typed round cues map to fixed keys. Clips load asynchronously
/// and are revalidated at each match. Missing optional sounds never block a raid;
/// bounded diagnostics explain skipped announcements.
/// </summary>
internal static class AnnouncerService
{
    /// <summary>
    /// Unity rebuilds its native audio state around raid startup. Runtime-created
    /// AudioClip wrappers can survive that reset with loadState=Loaded while
    /// samples/channels/length have fallen to zero. Managed PCM is therefore the
    /// durable asset; RuntimeClip is only a disposable device-generation cache.
    /// </summary>
    private sealed class CachedClip
    {
        public string Key;
        public float[] Samples;
        public int Frames;
        public int Channels;
        public int Frequency;
        public float Peak;
        public AudioClip RuntimeClip;
        public int RuntimeGeneration;
    }

    private static readonly Dictionary<string, CachedClip> Clips =
        new Dictionary<string, CachedClip>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AvailableFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReportedSkips = new(StringComparer.OrdinalIgnoreCase);

    private static ArenaMatch _match;
    private static bool _loading;
    private static bool _directoryScanned;
    private static float _nextRecoveryAt;
    private static string _pluginDirectory;
    private static int _loadEpoch;
    private static int _calloutsReceived;
    private static int _cuesReceived;
    private static int _requested;
    private static int _skipped;
    private static string _lastOutputState;
    private const float RecoveryIntervalSeconds = 10f;
    private const float LoadTimeoutSeconds = 10f;

    public static void SetPluginDirectory(string pluginDirectory)
    {
        if (string.Equals(_pluginDirectory, pluginDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        // A late continuation from the previous directory must never publish
        // into the new cache or keep the loader permanently busy.
        _loadEpoch++;
        _loading = false;
        _pluginDirectory = pluginDirectory;
    }

    /// <summary>
    /// Called at each Arena match start. Healthy clips stay cached; a previous
    /// failure or an invalid Unity object must not disable loading forever.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loading || string.IsNullOrEmpty(_pluginDirectory))
            return;

        _loading = true;
        _nextRecoveryAt = Time.realtimeSinceStartup + RecoveryIntervalSeconds;
        int loadEpoch = ++_loadEpoch;
        LoadAsync(_pluginDirectory, loadEpoch);
    }

    private static async void LoadAsync(string pluginDirectory, int loadEpoch)
    {
        try
        {
            var soundsDir = Path.Combine(pluginDirectory, "ArenaMode", "sounds");
            if (!Directory.Exists(soundsDir))
            {
                _directoryScanned = false;
                Plugin.ModLogger.LogWarning($"Arena announcer: sounds folder missing: {soundsDir}");
                return;
            }

            var files = Directory.GetFiles(soundsDir, "*.ogg");
            // The initial load begins when Arena is selected, long before the
            // raid, but keep lifecycle cues first as a second guard for an
            // unusually quick start or a repaired folder. Kill calls must never
            // delay START.
            Array.Sort(files, CompareLoadOrder);
            if (!IsCurrentLoad(loadEpoch, pluginDirectory))
                return;
            AvailableFiles.Clear();
            foreach (var file in files)
                AvailableFiles.Add(Path.GetFileNameWithoutExtension(file));
            _directoryScanned = true;
            var loaded = 0;
            var reused = 0;
            var failed = 0;
            foreach (var file in files)
            {
                if (!IsCurrentLoad(loadEpoch, pluginDirectory))
                    return;
                var key = Path.GetFileNameWithoutExtension(file);
                try
                {
                    Clips.TryGetValue(key, out var previous);
                    if (IsReady(previous))
                    {
                        reused++;
                        continue;
                    }

                    if (Clips.ContainsKey(key))
                        Plugin.ModLogger.LogWarning($"Arena announcer: reloading '{key}', state={ClipState(previous)}.");
                    var cached = await LoadClip(file);
                    if (!IsCurrentLoad(loadEpoch, pluginDirectory))
                    {
                        DestroyRuntimeClip(cached);
                        return;
                    }
                    if (cached == null)
                    {
                        failed++;
                        continue;
                    }

                    // The previous clip may finish its own load while the file
                    // request is awaiting. It can already be playing again;
                    // never replace that recovered entry with a duplicate.
                    if (IsReady(previous))
                    {
                        DestroyRuntimeClip(cached);
                        reused++;
                        continue;
                    }

                    Clips[key] = cached;
                    loaded++;
                    DestroyRuntimeClip(previous);
                }
                catch (Exception ex)
                {
                    failed++;
                    Plugin.ModLogger.LogWarning($"Arena announcer clip failed: '{key}': {ex.Message}");
                }
            }

            if (IsCurrentLoad(loadEpoch, pluginDirectory))
                Plugin.ModLogger.LogInfo($"Arena announcer load check: loaded={loaded}, reused={reused}, "
                    + $"failed={failed}, ready={ReadyCount()}, files={files.Length}.");
        }
        catch (Exception ex)
        {
            if (IsCurrentLoad(loadEpoch, pluginDirectory))
                Plugin.ModLogger.LogWarning($"Arena announcer load failed: {ex.Message}");
        }
        finally
        {
            if (IsCurrentLoad(loadEpoch, pluginDirectory))
            {
                _loading = false;
                _nextRecoveryAt = Time.realtimeSinceStartup + RecoveryIntervalSeconds;
            }
        }
    }

    private static bool IsCurrentLoad(int loadEpoch, string pluginDirectory)
        => loadEpoch == _loadEpoch
           && string.Equals(_pluginDirectory, pluginDirectory, StringComparison.OrdinalIgnoreCase);

    public static void Attach(ArenaMatch match)
    {
        Detach();
        if (match == null)
            return;

        // Subscribe even while the first async load is pending. Play safely skips
        // unavailable clips; waiting for a populated cache here loses the whole
        // first match because nobody calls Attach again when loading completes.
        _match = match;
        _calloutsReceived = _cuesReceived = _requested = _skipped = 0;
        _lastOutputState = null;
        ReportedSkips.Clear();
        match.OnCallout += OnCallout;
        match.OnAnnouncerCue += OnAnnouncerCue;
        Plugin.ModLogger.LogInfo($"Arena announcer attached: enabled={Settings.AnnouncerEnabled.Value}, "
            + $"volume={Number(Settings.AnnouncerVolume.Value)}, ready={ReadyCount()}, loading={_loading}.");
    }

    public static void Detach()
    {
        if (_match != null)
        {
            _match.OnCallout -= OnCallout;
            _match.OnAnnouncerCue -= OnAnnouncerCue;
            _match = null;
            Plugin.ModLogger.LogInfo($"Arena announcer detached: callouts={_calloutsReceived}, cues={_cuesReceived}, "
                + $"playRequests={_requested}, skipped={_skipped}.");
        }
    }

    private static void OnCallout(string label)
    {
        if (string.IsNullOrEmpty(label))
            return;

        _calloutsReceived++;
        var key = label.ToLowerInvariant().Replace(' ', '_');
        Receive(key);
    }

    private static void OnAnnouncerCue(ArenaAnnouncerCue cue)
    {
        var key = cue switch
        {
            ArenaAnnouncerCue.RoundStart => "round_start",
            ArenaAnnouncerCue.RoundWon => "round_won",
            ArenaAnnouncerCue.RoundLost => "round_lost",
            _ => null,
        };

        if (key == null)
            return;

        _cuesReceived++;
        Receive(key);
    }

    private static void Receive(string key)
    {
        if (!Settings.AnnouncerEnabled.Value)
        {
            Skip(key, "disabled");
            return;
        }
        if (Settings.AnnouncerVolume.Value <= 0f)
        {
            Skip(key, "volume_zero");
            return;
        }
        Play(key);
    }

    private static void Play(string key)
    {
        try
        {
            var found = Clips.TryGetValue(key, out var cached);
            if (!IsReady(cached))
            {
                var pending = _loading;
                Skip(key, pending ? "loading" : found ? "invalid_clip" : "missing_clip",
                    $"state={ClipState(cached)}");
                // Never retry absent pack phrases on every kill. Known failed or
                // invalid clips can recover, as can a folder installed late.
                if ((found || AvailableFiles.Contains(key) || !_directoryScanned)
                    && !_loading && Time.realtimeSinceStartup >= _nextRecoveryAt)
                    EnsureLoaded();
                // A late load is for future callouts; do not replay this kill.
                return;
            }

            if (!TryGetRuntimeClip(cached, out AudioClip clip, out string clipProblem))
            {
                Skip(key, "clip_rebuild_failed", clipProblem);
                return;
            }

            float volume = Settings.AnnouncerVolume.Value;
            string clipBeforePlay = RuntimeClipState(clip);
            if (!TryStartNativeUi(clip, volume, out string problem))
            {
                Skip(key, problem?.StartsWith("GUISounds:", StringComparison.Ordinal) == true
                    ? "play_failed"
                    : "ui_unavailable", problem);
                return;
            }

            _requested++;
            const string route = "GUISounds.SingleUI.Primary";
            var outputState = $"volume={Number(volume)}, clipGeneration={cached.RuntimeGeneration}, "
                + $"clipBeforePlay={clipBeforePlay}, clipAfterPlay={RuntimeClipState(clip)}, "
                + $"clipFrames={clip.samples}, "
                + $"clipChannels={clip.channels}, clipFrequency={clip.frequency}, "
                + $"clipLengthMs={Number(clip.length * 1000f)}, clipState={clip.loadState}, "
                + DescribeOutput(route);
            if (_requested <= 3 || outputState != _lastOutputState)
                Plugin.ModLogger.LogInfo($"Arena announcer play request: '{key}', "
                    + outputState + ".");
            _lastOutputState = outputState;
        }
        catch (Exception ex)
        {
            Skip(key, "play_failed", ex.Message);
        }
    }

    private static bool TryStartNativeUi(AudioClip clip, float volume, out string problem)
    {
        problem = null;
        if (!Singleton<GUISounds>.Instantiated || Singleton<GUISounds>.Instance == null)
        {
            problem = "GUISounds unavailable";
            return false;
        }
        try
        {
            // Use EFT's own persistent GUI audio host and UI mixer. This exact
            // native route was the last path heard in live playtests. single:true
            // gives announcer stingers their intended replace-not-stack behavior;
            // commonUiSound:false keeps the configured volume and native UI mixer.
            Singleton<GUISounds>.Instance.PlaySound(
                clip,
                single: true,
                commonUiSound: false,
                volume);
            return true;
        }
        catch (Exception ex)
        {
            problem = "GUISounds: " + ex.Message;
            return false;
        }
    }

    private static bool TryGetRuntimeClip(CachedClip cached, out AudioClip clip, out string problem)
    {
        clip = cached?.RuntimeClip;
        problem = null;
        if (IsRuntimeClipValid(cached, clip))
            return true;

        string previousState = RuntimeClipState(clip);
        bool rebuilding = !ReferenceEquals(clip, null);
        if (clip != null)
            UnityEngine.Object.Destroy(clip);
        cached.RuntimeClip = null;
        clip = null;

        try
        {
            clip = AudioClip.Create(
                cached.Key,
                cached.Frames,
                cached.Channels,
                cached.Frequency,
                stream: false);
            if (clip == null)
            {
                problem = "AudioClip.Create returned null";
                return false;
            }
            // Make the intended native lifetime explicit as an extra guard around
            // scene cleanup. Arena still validates every cue and explicitly
            // destroys each disposable runtime generation.
            clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (!clip.SetData(cached.Samples, 0))
            {
                problem = "AudioClip.SetData returned false";
                UnityEngine.Object.Destroy(clip);
                clip = null;
                return false;
            }
            if (!IsRuntimeClipValid(cached, clip))
            {
                problem = "rebuilt metadata invalid: " + RuntimeClipState(clip);
                UnityEngine.Object.Destroy(clip);
                clip = null;
                return false;
            }

            // Read one complete copy back after each device-generation rebuild.
            // This is intentionally not repeated for every cue; subsequent calls
            // use the strict metadata check above and the managed PCM remains the
            // recovery source if Unity later clears the native buffer again.
            var readback = new float[cached.Samples.Length];
            if (!clip.GetData(readback, 0))
            {
                problem = "rebuilt AudioClip.GetData returned false";
                UnityEngine.Object.Destroy(clip);
                clip = null;
                return false;
            }
            float readbackPeak = Peak(readback, out int invalidIndex);
            if (invalidIndex >= 0 || readbackPeak <= 0.00001f)
            {
                problem = invalidIndex >= 0
                    ? $"rebuilt PCM contained a non-finite sample at index {invalidIndex}"
                    : "rebuilt PCM readback contained no signal";
                UnityEngine.Object.Destroy(clip);
                clip = null;
                return false;
            }

            cached.RuntimeClip = clip;
            cached.RuntimeGeneration++;
            Plugin.ModLogger.LogInfo(
                $"Arena announcer runtime clip {(rebuilding ? "rebuilt" : "created")}: '{cached.Key}', "
                + $"generation={cached.RuntimeGeneration}, previous={previousState}, frames={clip.samples}, "
                + $"channels={clip.channels}, frequency={clip.frequency}, "
                + $"lengthMs={Number(clip.length * 1000f)}, readbackPeak={Number(readbackPeak)}.");
            return true;
        }
        catch (Exception ex)
        {
            if (clip != null)
                UnityEngine.Object.Destroy(clip);
            cached.RuntimeClip = null;
            clip = null;
            problem = ex.Message;
            return false;
        }
    }

    private static bool IsRuntimeClipValid(CachedClip cached, AudioClip clip)
    {
        if (!IsReady(cached) || clip == null || clip.loadState != AudioDataLoadState.Loaded)
            return false;

        float expectedLength = cached.Frames / (float)cached.Frequency;
        return clip.samples == cached.Frames
            && clip.channels == cached.Channels
            && clip.frequency == cached.Frequency
            && clip.length > 0f
            && Math.Abs(clip.length - expectedLength) <= Math.Max(0.01f, expectedLength * 0.01f);
    }

    private static string RuntimeClipState(AudioClip clip)
        => clip == null
            ? "missing_or_destroyed"
            : $"state={clip.loadState},frames={clip.samples},channels={clip.channels},"
              + $"frequency={clip.frequency},lengthMs={Number(clip.length * 1000f)}";

    private static void DestroyRuntimeClip(CachedClip cached)
    {
        if (cached?.RuntimeClip != null)
            UnityEngine.Object.Destroy(cached.RuntimeClip);
        if (cached != null)
            cached.RuntimeClip = null;
    }

    private static int CompareLoadOrder(string left, string right)
    {
        int priority = LoadPriority(left).CompareTo(LoadPriority(right));
        return priority != 0
            ? priority
            : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static int LoadPriority(string path)
    {
        string key = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(key, "round_start", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(key, "round_won", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "round_lost", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    private static void Skip(string key, string reason, string detail = null)
    {
        _skipped++;
        if (ReportedSkips.Add(key + ":" + reason))
            Plugin.ModLogger.LogWarning($"Arena announcer skipped: '{key}', reason={reason}"
                + (string.IsNullOrEmpty(detail) ? "." : $", {detail}."));
    }

    private static bool IsReady(CachedClip clip)
        => clip != null
            && clip.Samples != null
            && clip.Frames > 0
            && clip.Channels > 0
            && clip.Frequency > 0
            && clip.Samples.Length == (long)clip.Frames * clip.Channels
            && clip.Peak > 0.00001f;

    private static string ClipState(CachedClip clip)
        => clip == null
            ? "missing"
            : $"managedFrames={clip.Frames}, managedChannels={clip.Channels}, "
              + $"managedFrequency={clip.Frequency}, managedSamples={clip.Samples?.Length ?? 0}, "
              + $"runtime={RuntimeClipState(clip.RuntimeClip)}";

    private static int ReadyCount()
    {
        var count = 0;
        foreach (var clip in Clips.Values)
            if (IsReady(clip)) count++;
        return count;
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string DescribeOutput(string route)
    {
        // Diagnosis must never change mixer settings, bypass mute or turn a
        // successful playback request into an exception. A request is not proof
        // of audibility; the native source/device can still be muted or stopped.
        try
        {
            GUISounds ui = Singleton<GUISounds>.Instantiated
                ? Singleton<GUISounds>.Instance
                : null;
            AudioMixer mixer = ui?.MasterMixer;
            var uiDb = mixer != null && mixer.GetFloat("UIVolume", out var u) ? Number(u) : "unknown";
            var masterDb = mixer != null && mixer.GetFloat("MasterVolume", out var m) ? Number(m) : "unknown";
            AudioConfiguration configuration = AudioSettings.GetConfiguration();
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            return $"route={route}, uiReady={ui != null && ui.isActiveAndEnabled}, "
                + $"uiDb={uiDb}, masterDb={masterDb}, "
                + $"listenerVolume={Number(AudioListener.volume)}, "
                + $"listenerPaused={AudioListener.pause}, speakerMode={configuration.speakerMode}, "
                + $"sampleRate={configuration.sampleRate}, voices={configuration.numRealVoices}/"
                + $"{configuration.numVirtualVoices}, dspBuffer={bufferLength}x{bufferCount}";
        }
        catch
        {
            return "outputState=unavailable";
        }
    }

    internal static void Shutdown()
    {
        Detach();
        _loadEpoch++;
        _loading = false;
        foreach (var cached in Clips.Values)
            DestroyRuntimeClip(cached);
        Clips.Clear();
        AvailableFiles.Clear();
        ReportedSkips.Clear();
    }

    private static async Task<bool> WaitForClipReady(AudioClip clip)
    {
        if (clip == null)
            return false;
        if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData())
            return false;
        var deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
        while (clip != null && clip.loadState == AudioDataLoadState.Loading
            && Time.realtimeSinceStartup < deadline)
            await Task.Yield();
        return clip != null && clip.loadState == AudioDataLoadState.Loaded;
    }

    private static async Task<CachedClip> LoadClip(string path)
    {
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
        {
            request.timeout = (int)LoadTimeoutSeconds;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Plugin.ModLogger.LogWarning($"Arena announcer clip failed: {path}: {request.error}");
                return null;
            }

            var decoded = DownloadHandlerAudioClip.GetContent(request);
            try
            {
                string key = Path.GetFileNameWithoutExtension(path);
                if (!await WaitForClipReady(decoded))
                {
                    Plugin.ModLogger.LogWarning($"Arena announcer clip not ready: '{key}', "
                        + $"state={RuntimeClipState(decoded)}.");
                    return null;
                }

                CachedClip cached = TryCapturePcm(decoded, key, out string pcmProblem);
                if (cached == null)
                    Plugin.ModLogger.LogWarning(
                        $"Arena announcer PCM capture failed for '{key}': {pcmProblem}.");
                return cached;
            }
            finally
            {
                if (decoded != null)
                    UnityEngine.Object.Destroy(decoded);
            }
        }
    }

    private static CachedClip TryCapturePcm(AudioClip decoded, string key, out string problem)
    {
        problem = null;
        try
        {
            int frames = decoded.samples;
            int channels = decoded.channels;
            int frequency = decoded.frequency;
            if (frames <= 0 || channels <= 0 || frequency <= 0)
            {
                problem = $"invalid metadata frames={frames}, channels={channels}, frequency={frequency}";
                return null;
            }

            int sampleCount = checked(frames * channels);
            var samples = new float[sampleCount];
            if (!decoded.GetData(samples, 0))
            {
                problem = "AudioClip.GetData returned false";
                return null;
            }

            float peak = Peak(samples, out int invalidIndex);
            if (invalidIndex >= 0)
            {
                problem = $"decoded PCM contained a non-finite sample at index {invalidIndex}";
                return null;
            }

            if (peak <= 0.00001f)
            {
                problem = "decoded PCM contained no signal";
                return null;
            }

            float expectedLength = frames / (float)frequency;
            Plugin.ModLogger.LogInfo(
                $"Arena announcer PCM cached: '{key}', frames={frames}, channels={channels}, "
                + $"frequency={frequency}, lengthMs={Number(expectedLength * 1000f)}, "
                + $"peak={Number(peak)}, managedBytes={(long)sampleCount * sizeof(float)}.");
            return new CachedClip
            {
                Key = key,
                Samples = samples,
                Frames = frames,
                Channels = channels,
                Frequency = frequency,
                Peak = peak,
            };
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    private static float Peak(float[] samples, out int invalidIndex)
    {
        invalidIndex = -1;
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = samples[i];
            if (float.IsNaN(sample) || float.IsInfinity(sample))
            {
                invalidIndex = i;
                return 0f;
            }
            peak = Math.Max(peak, Math.Abs(sample));
        }
        return peak;
    }
}
