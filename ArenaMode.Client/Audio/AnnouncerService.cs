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
    private static readonly Dictionary<string, AudioClip> Clips =
        new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AvailableFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReportedSkips = new(StringComparer.OrdinalIgnoreCase);

    private static ArenaMatch _match;
    private static bool _loading;
    private static bool _directoryScanned;
    private static float _nextRecoveryAt;
    private static string _pluginDirectory;
    private static int _calloutsReceived;
    private static int _cuesReceived;
    private static int _requested;
    private static int _skipped;
    private static string _lastOutputState;
    private const float RecoveryIntervalSeconds = 10f;
    private const float LoadTimeoutSeconds = 10f;

    public static void SetPluginDirectory(string pluginDirectory)
        => _pluginDirectory = pluginDirectory;

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
        LoadAsync(_pluginDirectory);
    }

    private static async void LoadAsync(string pluginDirectory)
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
            AvailableFiles.Clear();
            foreach (var file in files)
                AvailableFiles.Add(Path.GetFileNameWithoutExtension(file));
            _directoryScanned = true;
            var loaded = 0;
            var reused = 0;
            var failed = 0;
            foreach (var file in files)
            {
                var key = Path.GetFileNameWithoutExtension(file);
                try
                {
                    Clips.TryGetValue(key, out var previous);
                    if (IsReady(previous) || (previous != null
                        && previous.loadState == AudioDataLoadState.Loading
                        && await WaitForClipReady(previous)))
                    {
                        reused++;
                        continue;
                    }

                    if (Clips.ContainsKey(key))
                        Plugin.ModLogger.LogWarning($"Arena announcer: reloading '{key}', state={ClipState(previous)}.");
                    var clip = await LoadClip(file);
                    if (clip == null)
                    {
                        failed++;
                        continue;
                    }

                    // The previous clip may finish its own load while the file
                    // request is awaiting. It can already be playing again;
                    // never destroy that recovered clip to install a duplicate.
                    if (IsReady(previous))
                    {
                        if (clip != previous)
                            UnityEngine.Object.Destroy(clip);
                        reused++;
                        continue;
                    }

                    Clips[key] = clip;
                    loaded++;
                    // These are exclusively our downloaded clips, never assets
                    // owned by GUISounds or the game. Publish a usable replacement
                    // first, then dispose the old, unusable native object.
                    if (previous != null && previous != clip)
                        UnityEngine.Object.Destroy(previous);
                }
                catch (Exception ex)
                {
                    failed++;
                    Plugin.ModLogger.LogWarning($"Arena announcer clip failed: '{key}': {ex.Message}");
                }
            }

            Plugin.ModLogger.LogInfo($"Arena announcer load check: loaded={loaded}, reused={reused}, "
                + $"failed={failed}, ready={ReadyCount()}, files={files.Length}.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena announcer load failed: {ex.Message}");
        }
        finally
        {
            _loading = false;
            _nextRecoveryAt = Time.realtimeSinceStartup + RecoveryIntervalSeconds;
        }
    }

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
            var found = Clips.TryGetValue(key, out var clip);
            if (!IsReady(clip))
            {
                var pending = _loading || (clip != null && clip.loadState == AudioDataLoadState.Loading);
                Skip(key, pending ? "loading" : found ? "invalid_clip" : "missing_clip",
                    $"state={ClipState(clip)}");
                // Never retry absent pack phrases on every kill. Known failed or
                // invalid clips can recover, as can a folder installed late.
                if ((found || AvailableFiles.Contains(key) || !_directoryScanned)
                    && !_loading && Time.realtimeSinceStartup >= _nextRecoveryAt)
                    EnsureLoaded();
                // A late load is for future callouts; do not replay this kill.
                return;
            }

            float volume = Settings.AnnouncerVolume.Value;
            if (!TryStartNativeUi(clip, volume, out string problem))
            {
                Skip(key, problem?.StartsWith("GUISounds:", StringComparison.Ordinal) == true
                    ? "play_failed"
                    : "ui_unavailable", problem);
                return;
            }

            _requested++;
            const string route = "GUISounds.SingleUI.Primary";
            var outputState = $"volume={Number(volume)}, clipFrames={clip.samples}, "
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
            // commonUiSound:false keeps the configured volume and Interface mixer.
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

    private static bool IsReady(AudioClip clip)
        => clip != null && clip.loadState == AudioDataLoadState.Loaded;

    private static string ClipState(AudioClip clip)
        => clip == null ? "missing_or_destroyed" : clip.loadState.ToString();

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
            var interfaceDb = mixer != null && mixer.GetFloat("InterfaceVolume", out var i) ? Number(i) : "unknown";
            var uiDb = mixer != null && mixer.GetFloat("UIVolume", out var u) ? Number(u) : "unknown";
            var masterDb = mixer != null && mixer.GetFloat("MasterVolume", out var m) ? Number(m) : "unknown";
            AudioConfiguration configuration = AudioSettings.GetConfiguration();
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            return $"route={route}, uiReady={ui != null && ui.isActiveAndEnabled}, "
                + $"interfaceDb={interfaceDb}, uiDb={uiDb}, masterDb={masterDb}, "
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
        return IsReady(clip);
    }

    private static async Task<AudioClip> LoadClip(string path)
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
            var retainDecoded = false;
            try
            {
                string key = Path.GetFileNameWithoutExtension(path);
                if (!await WaitForClipReady(decoded))
                {
                    Plugin.ModLogger.LogWarning($"Arena announcer clip not ready: '{key}', "
                        + $"state={ClipState(decoded)}.");
                    return null;
                }

                AudioClip pcm = TryMaterializePcm(decoded, key, out string pcmProblem);
                if (pcm != null)
                    return pcm;

                // Keep the decoder-owned Unity clip as a fail-soft compatibility
                // path. GUISounds can consume either form; a PCM-copy limitation
                // must not block Arena startup.
                retainDecoded = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena announcer PCM copy unavailable for '{key}': {pcmProblem}; retaining decoded clip.");
                return decoded;
            }
            finally
            {
                if (!retainDecoded && decoded != null)
                    UnityEngine.Object.Destroy(decoded);
            }
        }
    }

    private static AudioClip TryMaterializePcm(AudioClip decoded, string key, out string problem)
    {
        problem = null;
        AudioClip pcm = null;
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

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float sample = samples[i];
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                {
                    problem = $"decoded PCM contained a non-finite sample at index {i}";
                    return null;
                }
                peak = Math.Max(peak, Math.Abs(sample));
            }

            if (peak <= 0.00001f)
            {
                problem = "decoded PCM contained no signal";
                return null;
            }

            pcm = AudioClip.Create(key, frames, channels, frequency, stream: false);
            if (pcm == null)
            {
                problem = "AudioClip.Create returned null";
                return null;
            }
            if (!pcm.SetData(samples, 0))
            {
                problem = "AudioClip.SetData returned false";
                UnityEngine.Object.Destroy(pcm);
                return null;
            }

            float expectedLength = frames / (float)frequency;
            if (pcm.loadState != AudioDataLoadState.Loaded
                || pcm.samples != frames || pcm.channels != channels || pcm.frequency != frequency
                || pcm.length <= 0f || Math.Abs(pcm.length - expectedLength) > Math.Max(0.01f, expectedLength * 0.01f))
            {
                problem = $"materialized metadata drifted: state={pcm.loadState}, frames={pcm.samples}/{frames}, "
                    + $"channels={pcm.channels}/{channels}, frequency={pcm.frequency}/{frequency}, "
                    + $"length={Number(pcm.length)}/{Number(expectedLength)}";
                UnityEngine.Object.Destroy(pcm);
                return null;
            }

            // SetData only proves that Unity accepted the write request. Read the
            // created clip back once so a wrapper with valid-looking metadata but
            // no native sample storage falls back to the decoder-owned clip.
            var readback = new float[sampleCount];
            if (!pcm.GetData(readback, 0))
            {
                problem = "materialized AudioClip.GetData returned false";
                UnityEngine.Object.Destroy(pcm);
                return null;
            }
            float readbackPeak = 0f;
            for (int i = 0; i < readback.Length; i++)
            {
                float sample = readback[i];
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                {
                    problem = $"materialized PCM contained a non-finite sample at index {i}";
                    UnityEngine.Object.Destroy(pcm);
                    return null;
                }
                readbackPeak = Math.Max(readbackPeak, Math.Abs(sample));
            }
            if (readbackPeak <= 0.00001f)
            {
                problem = "materialized PCM readback contained no signal";
                UnityEngine.Object.Destroy(pcm);
                return null;
            }

            Plugin.ModLogger.LogInfo(
                $"Arena announcer PCM ready: '{key}', frames={frames}, channels={channels}, "
                + $"frequency={frequency}, lengthMs={Number(pcm.length * 1000f)}, "
                + $"peak={Number(peak)}, readbackPeak={Number(readbackPeak)}.");
            return pcm;
        }
        catch (Exception ex)
        {
            if (pcm != null)
                UnityEngine.Object.Destroy(pcm);
            problem = ex.Message;
            return null;
        }
    }
}
