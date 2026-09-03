using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    private static readonly FieldInfo SingleUiSourceField = typeof(GUISounds).GetField(
        "audioSource_4",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static ArenaMatch _match;
    private static bool _loading;
    private static bool _directoryScanned;
    private static float _nextRecoveryAt;
    private static string _pluginDirectory;
    private static AudioSource _output;
    private static AudioMixerGroup _outputRoute;
    private static int _calloutsReceived;
    private static int _cuesReceived;
    private static int _requested;
    private static int _skipped;
    private static int _confirmed;
    private static int _fallbackRequested;
    private static string _lastOutputState;
    private static AudioSource _pendingOutputCheck;
    private static AudioClip _pendingClipCheck;
    private static string _pendingKeyCheck;
    private static string _pendingRouteCheck;
    private static float _pendingVolumeCheck;
    private static bool _pendingCheckUsesUi;
    private static float _outputCheckAt;
    private static int _outputGeneration;
    private static int _pendingOutputGeneration;
    private static AudioSource _leasedUiSource;
    private static int _leasedUiPriority;
    private static bool _uiPriorityLeased;
    private static float _uiPriorityRestoreAt;
    private static bool _uiProbeFailureReported;
    private const float RecoveryIntervalSeconds = 10f;
    private const float LoadTimeoutSeconds = 10f;
    private const float OutputCheckDelaySeconds = 0.05f;

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
        _confirmed = _fallbackRequested = 0;
        _lastOutputState = null;
        _uiProbeFailureReported = false;
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
                + $"playRequests={_requested}, confirmed={_confirmed}, "
                + $"fallbackRequests={_fallbackRequested}, skipped={_skipped}.");
        }

        // Detach is the exact Arena lifecycle boundary, including a partial
        // OnMatchStart failure. Never let its private source leak into a normal raid.
        _outputGeneration++;
        ClearOutputCheck();
        if (_output != null)
        {
            _output.Stop();
            _output.clip = null;
        }
        RestoreUiPriority();
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
            // A new cue replaces any not-yet-verified older cue even if both
            // native start paths below happen to throw before scheduling their
            // own check.
            _outputGeneration++;
            ClearOutputCheck();
            string route;
            string fallbackReason = null;
            if (!TryStartDedicated(key, clip, volume, out route, out string dedicatedProblem))
            {
                fallbackReason = dedicatedProblem;
                if (!TryStartUiFallback(
                        key,
                        clip,
                        volume,
                        dedicatedProblem ?? "dedicated output unavailable",
                        out route,
                        out string uiProblem))
                {
                    string detail = JoinProblems(dedicatedProblem, uiProblem);
                    Skip(key, IsPlaybackFailure(detail) ? "play_failed" : "output_unavailable", detail);
                    return;
                }
            }

            _requested++;
            var outputState = $"volume={Number(volume)}, {DescribeOutput(route)}"
                + (fallbackReason == null ? string.Empty : $", fallbackReason='{fallbackReason}'");
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

    private static bool TryStartDedicated(
        string key,
        AudioClip clip,
        float volume,
        out string route,
        out string problem)
    {
        route = null;
        if (!TryEnsureOutput(out problem))
            return false;

        try
        {
            // This source belongs only to Arena. It obeys EFT's Master mixer but
            // avoids the user's strongly attenuated Interface channel and every
            // BetterSource/SurroundAudio hook. A fully materialized PCM clip is
            // assigned below, so the disposed web request owns no playback state.
            _output.Stop();
            _output.clip = clip;
            _output.volume = volume;
            _output.Play();
            route = "Arena.PCM.NonspatialBypass";
            ScheduleOutputCheck(_output, clip, key, route, volume, usesUi: false);
            return true;
        }
        catch (Exception ex)
        {
            problem = "Arena output: " + ex.Message;
            if (_output != null)
            {
                _output.Stop();
                _output.clip = null;
            }
            return false;
        }
    }

    private static bool TryStartUiFallback(
        string key,
        AudioClip clip,
        float volume,
        string reason,
        out string route,
        out string problem)
    {
        route = null;
        problem = null;
        if (!Singleton<GUISounds>.Instantiated || Singleton<GUISounds>.Instance == null)
        {
            problem = "GUISounds unavailable";
            return false;
        }

        GUISounds ui = Singleton<GUISounds>.Instance;
        AudioSource uiSource = TryGetSingleUiSource(ui, out string probeProblem);
        try
        {
            if (uiSource != null)
                LeaseUiPriority(uiSource, clip);

            ui.PlaySound(clip, single: true, commonUiSound: false, volume);
            _fallbackRequested++;
            route = "GUISounds.SingleUI<-" + reason;
            if (uiSource != null)
            {
                ScheduleOutputCheck(uiSource, clip, key, route, volume, usesUi: true);
            }
            else
            {
                // PlaySound is still a valid native fallback when an SPT update
                // renames its private source. Only the additional proof becomes
                // unavailable; reflection failure never breaks raid startup.
                _outputGeneration++;
                ClearOutputCheck();
                if (!_uiProbeFailureReported)
                {
                    _uiProbeFailureReported = true;
                    Plugin.ModLogger.LogWarning(
                        $"Arena announcer UI output is unobservable: {probeProblem ?? "single source unavailable"}.");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            RestoreUiPriority();
            problem = JoinProblems(probeProblem, "GUISounds: " + ex.Message);
            return false;
        }
    }

    private static string JoinProblems(string first, string second)
    {
        if (string.IsNullOrEmpty(first))
            return second;
        return string.IsNullOrEmpty(second) ? first : first + "; " + second;
    }

    private static bool IsPlaybackFailure(string problem)
        => problem != null
            && (problem.Contains("Arena output:") || problem.Contains("GUISounds:"));

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
            AudioMixer mixer = BetterAudio.Instantiated && BetterAudio.Instance != null
                ? BetterAudio.Instance.Master
                : ui?.MasterMixer;
            var uiDb = mixer != null && mixer.GetFloat("UIVolume", out var u) ? Number(u) : "unknown";
            var masterDb = mixer != null && mixer.GetFloat("MasterVolume", out var m) ? Number(m) : "unknown";
            return $"route={route}, uiReady={ui != null && ui.isActiveAndEnabled}, "
                + $"uiDb={uiDb}, masterDb={masterDb}, listenerVolume={Number(AudioListener.volume)}, "
                + $"listenerPaused={AudioListener.pause}";
        }
        catch
        {
            return "outputState=unavailable";
        }
    }

    private static bool TryEnsureOutput(out string problem)
    {
        problem = null;
        AudioMixerGroup route = BetterAudio.Instantiated && BetterAudio.Instance != null
            ? BetterAudio.Instance.NonSpatialBypassMixer
            : null;

        if (route == null)
        {
            problem = "EFT's non-spatial bypass mixer was unavailable";
            return false;
        }

        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            problem = "the persistent Arena plugin host was unavailable";
            return false;
        }

        if (_output == null)
        {
            AudioSource output = plugin.gameObject.AddComponent<AudioSource>();
            output.playOnAwake = false;
            output.loop = false;
            output.spatialBlend = 0f;
            output.spatialize = false;
            output.dopplerLevel = 0f;
            output.bypassReverbZones = true;
            // Zero is Unity's highest priority. The announcer must remain one of
            // the real voices during a full 5v5 firefight, not be silently virtualized.
            output.priority = 0;
            _output = output;
            // Force the common assignment block even when only the Unity source
            // was destroyed while its former route survived.
            _outputRoute = null;
            Plugin.ModLogger.LogInfo("Arena announcer created a dedicated PCM audio channel.");
        }

        if (!ReferenceEquals(_outputRoute, route))
        {
            _outputRoute = route;
            _output.outputAudioMixerGroup = route;
            Plugin.ModLogger.LogInfo($"Arena announcer output route: '{route.name}'.");
        }
        return true;
    }

    internal static void Tick()
    {
        if (_uiPriorityLeased && Time.realtimeSinceStartup >= _uiPriorityRestoreAt)
            RestoreUiPriority();

        AudioSource output = _pendingOutputCheck;
        if (ReferenceEquals(output, null) || Time.realtimeSinceStartup < _outputCheckAt)
            return;

        AudioClip clip = _pendingClipCheck;
        string key = _pendingKeyCheck;
        string route = _pendingRouteCheck;
        float volume = _pendingVolumeCheck;
        bool usesUi = _pendingCheckUsesUi;
        int generation = _pendingOutputGeneration;
        ClearOutputCheck();
        if (generation != _outputGeneration)
            return;

        bool alive = output != null;
        bool active = alive && output.gameObject != null && output.gameObject.activeInHierarchy;
        bool real = active && output.enabled && !output.mute && output.isPlaying && !output.isVirtual;
        string state = alive
            ? $"enabled={output.enabled}, mute={output.mute}, active={active}, "
              + $"playing={output.isPlaying}, virtual={output.isVirtual}, priority={output.priority}"
            : "source=destroyed";

        if (real && !AudioListener.pause && AudioListener.volume > 0f)
        {
            _confirmed++;
            if (_confirmed <= 3 || usesUi)
                Plugin.ModLogger.LogInfo(
                    $"Arena announcer output confirmed: '{key}', route={route}, {state}.");
            return;
        }

        if (AudioListener.pause || AudioListener.volume <= 0f)
        {
            Skip(key, "listener_suppressed",
                $"route={route}, listenerVolume={Number(AudioListener.volume)}, "
                + $"listenerPaused={AudioListener.pause}, {state}");
            return;
        }

        if (usesUi)
        {
            if (alive)
                output.Stop();
            RestoreUiPriority();
            Skip(key, "output_silent", $"route={route}, {state}");
            return;
        }

        // The private source returned from Play without becoming a real voice.
        // Stop it before requesting the shared UI fallback, otherwise a late
        // de-virtualization could play the same cue twice.
        if (alive && ReferenceEquals(output, _output))
        {
            output.Stop();
            if (ReferenceEquals(output.clip, clip))
                output.clip = null;
        }

        if (TryStartUiFallback(
                key,
                clip,
                volume,
                "silent Arena PCM channel",
                out string fallbackRoute,
                out string fallbackProblem))
        {
            Plugin.ModLogger.LogWarning(
                $"Arena announcer output recovery: '{key}', route={route} was silent ({state}); "
                + $"requested {fallbackRoute}.");
            return;
        }

        Skip(key, "output_silent",
            $"route={route}, {state}; fallback={fallbackProblem ?? "unavailable"}");
    }

    private static void ScheduleOutputCheck(
        AudioSource output,
        AudioClip clip,
        string key,
        string route,
        float volume,
        bool usesUi)
    {
        _outputGeneration++;
        _pendingOutputGeneration = _outputGeneration;
        _pendingOutputCheck = output;
        _pendingClipCheck = clip;
        _pendingKeyCheck = key;
        _pendingRouteCheck = route;
        _pendingVolumeCheck = volume;
        _pendingCheckUsesUi = usesUi;
        _outputCheckAt = Time.realtimeSinceStartup + OutputCheckDelaySeconds;
    }

    private static AudioSource TryGetSingleUiSource(GUISounds ui, out string problem)
    {
        problem = null;
        if (SingleUiSourceField == null)
        {
            problem = "GUISounds.audioSource_4 was not found";
            return null;
        }

        try
        {
            var source = SingleUiSourceField.GetValue(ui) as AudioSource;
            if (source == null)
                problem = "GUISounds.audioSource_4 was not initialized";
            return source;
        }
        catch (Exception ex)
        {
            problem = "GUISounds.audioSource_4 read failed: " + ex.Message;
            return null;
        }
    }

    private static void LeaseUiPriority(AudioSource source, AudioClip clip)
    {
        if (!_uiPriorityLeased || !ReferenceEquals(_leasedUiSource, source))
        {
            RestoreUiPriority();
            _leasedUiSource = source;
            _leasedUiPriority = source.priority;
            _uiPriorityLeased = true;
        }

        source.priority = 0;
        _uiPriorityRestoreAt = Time.realtimeSinceStartup + Math.Max(clip.length, 0.1f) + 0.1f;
    }

    private static void RestoreUiPriority()
    {
        AudioSource source = _leasedUiSource;
        int priority = _leasedUiPriority;
        bool leased = _uiPriorityLeased;
        _leasedUiSource = null;
        _leasedUiPriority = 0;
        _uiPriorityLeased = false;
        _uiPriorityRestoreAt = 0f;

        // Do not overwrite a later change made by EFT or another mod.
        if (leased && source != null && source.priority == 0)
            source.priority = priority;
    }

    private static void ClearOutputCheck()
    {
        _pendingOutputCheck = null;
        _pendingClipCheck = null;
        _pendingKeyCheck = null;
        _pendingRouteCheck = null;
        _pendingVolumeCheck = 0f;
        _pendingCheckUsesUi = false;
        _pendingOutputGeneration = 0;
        _outputCheckAt = 0f;
    }

    internal static void Shutdown()
    {
        Detach();
        if (_output != null)
            UnityEngine.Object.Destroy(_output);
        _output = null;
        _outputRoute = null;
        RestoreUiPriority();
        ClearOutputCheck();
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
                // path. The output probes will still expose whether it can become
                // a real voice; a PCM-copy limitation must not block Arena startup.
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

            Plugin.ModLogger.LogInfo(
                $"Arena announcer PCM ready: '{key}', frames={frames}, channels={channels}, "
                + $"frequency={frequency}, peak={Number(peak)}.");
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
