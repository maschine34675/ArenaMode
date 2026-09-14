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
    private static float _calloutBusyUntil;
    private static string _pendingResultKey;
    private static string _pendingCalloutKey;
    private static float _pendingCalloutExpiresAt;
    private static string _pendingResultFallbackKey;
    private static bool _clutchArmed;
    private static int _clutchFrame;
    private static string _activeKey;
    private const float StatusMaxWaitSeconds = 2f;
    private const float CalloutMaxWaitSeconds = 4f;
    private const float RecoveryIntervalSeconds = 10f;
    private const float LoadTimeoutSeconds = 10f;
    private const float ResultCueGapSeconds = 0.1f;

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

            var files = AnnouncerCueCatalog.LoadPaths(soundsDir);
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
        ArenaAnnouncementDirector.Attach(match);
        Plugin.ModLogger.LogInfo($"Arena announcer attached: enabled={Settings.AnnouncerEnabled.Value}, "
            + $"volume={Number(Settings.AnnouncerVolume.Value)}, ready={ReadyCount()}, loading={_loading}.");
    }

    public static void Detach()
    {
        ArenaAnnouncementDirector.Detach();
        ArenaAnnouncerOutput.Reset();
        _clutchArmed = false;
        _activeKey = null;
        CancelPendingResult("match detached");
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
        _clutchArmed = false;
        if (string.IsNullOrEmpty(label))
            return;

        _calloutsReceived++;
        var key = label.ToLowerInvariant().Replace(' ', '_');
        SubmitCallout(key);
    }

    private static void SubmitCallout(string key)
    {
        bool status = AnnouncerCueCatalog.IsStatus(key);
        if (status && AudioListener.pause)
        {
            Skip(key, "listener_paused");
            return;
        }
        if (!Settings.AnnouncerEnabled.Value || Settings.AnnouncerVolume.Value <= 0f)
        {
            Receive(key, out _);
            return;
        }
        if (!Clips.TryGetValue(key, out var queuedClip) || !IsReady(queuedClip))
        {
            Receive(key, out _); // diagnose/recover now, never replay a late load
            return;
        }
        if (_pendingResultKey != null)
        {
            Skip(key, "round_result_pending");
            return;
        }
        if (Time.realtimeSinceStartup < _calloutBusyUntil)
        {
            // One pending phrase, never a backlog of every kill. Higher-value
            // events replace lower ones; equal priority keeps the latest event.
            if (_pendingCalloutKey == null || CalloutPriority(key) >= CalloutPriority(_pendingCalloutKey))
            {
                if (_pendingCalloutKey != null) Skip(_pendingCalloutKey, "superseded_callout");
                _pendingCalloutKey = key;
                _pendingCalloutExpiresAt = Time.realtimeSinceStartup
                    + (status ? StatusMaxWaitSeconds : CalloutMaxWaitSeconds);
            }
            else Skip(key, "lower_priority_callout");
            return;
        }
        ClearPendingCallout("newer callout admitted");
        StartCallout(key);
    }

    private static void StartCallout(string key, string fallbackKey = null)
    {
        if (!Receive(key, out float clipLengthSeconds))
        {
            if (fallbackKey == null || !Receive(fallbackKey, out clipLengthSeconds)) return;
            key = fallbackKey;
        }
        _activeKey = key;
        // Digit clips already include their recorded pause. An additional 100 ms
        // would make THREE (947 ms) occupy the next real countdown second.
        _calloutBusyUntil = Time.realtimeSinceStartup + clipLengthSeconds
            + (AnnouncerCueCatalog.IsCountdown(key) ? 0f : ResultCueGapSeconds);
    }

    private static int CalloutPriority(string key) => key switch
    {
        "last_man_standing" => 100,
        "first_blood" => 95,
        "payback" => 90,
        "shutdown" => 85,
        "monster_kill" or "multi_kill" or "triple_kill" or "double_kill" => 80,
        "godlike" or "unstoppable" or "dominating" or "rampage" or "killing_spree" => 60,
        "final_weapon" or "ladder_complete" => 45,
        "final_round" or "match_point" => 40,
        "one_minute_remaining" or "thirty_seconds_remaining" or "ten_seconds_remaining"
            or "five_kills_remaining" or "one_kill_remaining" => 30,
        "taken_the_lead" or "lost_the_lead" or "tied_for_the_lead" => 20,
        "prepare_to_fight" => 10,
        _ => 50,
    };

    private static void OnAnnouncerCue(ArenaAnnouncerCue cue)
    {
        bool clutchWin = _clutchArmed && _clutchFrame == Time.frameCount
            && (cue == ArenaAnnouncerCue.RoundWon || cue == ArenaAnnouncerCue.MatchWon);
        _clutchArmed = false;
        if (cue == ArenaAnnouncerCue.CancelStatus)
        {
            CancelStatus("mode status changed");
            return;
        }
        var kind = AnnouncerCueCatalog.Classify(cue, out string key);
        if (kind == AnnouncerCueKind.None) return;
        _cuesReceived++;
        if (kind == AnnouncerCueKind.Clutch)
        {
            // This presentation hint does not consume or replace the lifecycle
            // event. Only the directly following, same-frame win may use it.
            _clutchArmed = true;
            _clutchFrame = Time.frameCount;
            return;
        }
        if (kind == AnnouncerCueKind.Countdown)
        {
            // No delayed digits: only a number emitted for the current snapshot
            // may request audio. A busy channel or slow asset load drops it.
            ClearPendingStatus("current countdown number");
            if (AudioListener.pause || _pendingResultKey != null
                || Time.realtimeSinceStartup < _calloutBusyUntil)
                Skip(key, "countdown_not_currently_playable");
            else StartCallout(key);
            return;
        }
        if (kind == AnnouncerCueKind.Award || kind == AnnouncerCueKind.Status)
        {
            SubmitCallout(key);
            return;
        }
        if (kind == AnnouncerCueKind.Start)
        {
            // A new live round owns the channel. This also prevents an unusually
            // long custom result queue from leaking into the next round.
            CancelPendingResult("new round started");
            StartCallout(key);
            return;
        }
        // Only explicit result members reach here, never a new/unknown enum.
        if (kind != AnnouncerCueKind.Result) return;
        string fallbackKey = clutchWin ? key : null;
        if (clutchWin) key = "clutch";
        CancelStatus("round ended");
        ClearPendingCallout("round ended");
        if (Time.realtimeSinceStartup < _calloutBusyUntil)
        {
            if (_pendingResultKey == null)
            {
                _pendingResultKey = key;
                _pendingResultFallbackKey = fallbackKey;
                Plugin.ModLogger.LogInfo($"Arena announcer result queued: '{key}', "
                    + $"waitMs={Number((_calloutBusyUntil - Time.realtimeSinceStartup) * 1000f)}.");
            }
            else if (!string.Equals(_pendingResultKey, key, StringComparison.OrdinalIgnoreCase))
            {
                Skip(key, "result_already_queued", $"pending={_pendingResultKey}");
            }
            return;
        }
        StartCallout(key, fallbackKey);
    }

    private static void ClearPendingStatus(string reason)
    {
        if (AnnouncerCueCatalog.IsStatus(_pendingCalloutKey)) ClearPendingCallout(reason);
    }

    private static void CancelStatus(string reason)
    {
        ClearPendingStatus(reason);
        if (!AnnouncerCueCatalog.IsStatus(_activeKey) && !AnnouncerCueCatalog.IsCountdown(_activeKey)) return;
        // Only our owned output can be stopped safely. Legacy GUISounds shares
        // its source with buttons and other UI; do not stop somebody else's sound.
        if (!Settings.AnnouncerUseUiVolume.Value) ArenaAnnouncerOutput.Reset();
        _activeKey = null;
        _calloutBusyUntil = 0f;
    }

    /// <summary>
    /// Drains bounded pending presentation after the actual spoken duration.
    /// Results take precedence over a pending kill or last-survivor cue.
    /// Outside an attached Arena match this is an allocation-free no-op.
    /// </summary>
    internal static void Tick()
    {
        _clutchArmed = false;
        // Fresh mode state must invalidate pending status before it can drain.
        ArenaAnnouncementDirector.Tick();
        ArenaAnnouncerOutput.Tick(_match != null && Settings.AnnouncerEnabled.Value
            && Settings.AnnouncerVolume.Value > 0f && !Settings.AnnouncerUseUiVolume.Value, Settings.AnnouncerVolume.Value);
        if (_match != null && (!Settings.AnnouncerEnabled.Value || Settings.AnnouncerVolume.Value <= 0f))
        {
            CancelPendingResult("announcer muted");
            return;
        }
        if (_match != null && AudioListener.pause)
        {
            CancelStatus("listener paused");
            return;
        }
        if (_match == null || Time.realtimeSinceStartup < _calloutBusyUntil)
            return;

        if (_pendingResultKey != null)
        {
            string key = _pendingResultKey;
            string fallbackKey = _pendingResultFallbackKey;
            ClearPendingResult();
            Plugin.ModLogger.LogInfo($"Arena announcer queued result released: '{key}'.");
            StartCallout(key, fallbackKey);
        }
        else if (_pendingCalloutKey != null)
        {
            string key = _pendingCalloutKey;
            _pendingCalloutKey = null;
            if (Time.realtimeSinceStartup > _pendingCalloutExpiresAt)
                Skip(key, "stale_callout");
            else StartCallout(key);
        }
    }

    private static void CancelPendingResult(string reason)
    {
        ClearPendingCallout(reason);
        _clutchArmed = false;
        string key = _pendingResultKey;
        ClearPendingResult();
        if (key != null)
        {
            _skipped++;
            Plugin.ModLogger.LogWarning(
                $"Arena announcer skipped: '{key}', reason=queued_result_canceled, {reason}.");
        }
    }

    private static void ClearPendingResult()
    {
        _pendingResultKey = null;
        _pendingResultFallbackKey = null;
        _activeKey = null;
        _calloutBusyUntil = 0f;
    }

    private static void ClearPendingCallout(string reason)
    {
        if (_pendingCalloutKey != null) Skip(_pendingCalloutKey, "queued_callout_canceled", reason);
        _pendingCalloutKey = null;
        _pendingCalloutExpiresAt = 0f;
    }

    private static bool Receive(string key, out float clipLengthSeconds)
    {
        clipLengthSeconds = 0f;
        if (!Settings.AnnouncerEnabled.Value)
        {
            Skip(key, "disabled");
            return false;
        }
        if (Settings.AnnouncerVolume.Value <= 0f)
        {
            Skip(key, "volume_zero");
            return false;
        }
        return Play(key, out clipLengthSeconds);
    }

    private static bool Play(string key, out float clipLengthSeconds)
    {
        clipLengthSeconds = 0f;
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
                return false;
            }

            if (!TryGetRuntimeClip(cached, out AudioClip clip, out string clipProblem))
            {
                Skip(key, "clip_rebuild_failed", clipProblem);
                return false;
            }

            float volume = Settings.AnnouncerVolume.Value;
            string clipBeforePlay = RuntimeClipState(clip);
            if (!TryStartNativeUi(clip, volume, out string problem))
            {
                Skip(key, problem?.StartsWith("GUISounds:", StringComparison.Ordinal) == true
                    ? "play_failed"
                    : "ui_unavailable", problem);
                return false;
            }

            _requested++;
            clipLengthSeconds = clip.length;
            string route = Settings.AnnouncerUseUiVolume.Value
                ? "GUISounds.SingleUI.Primary" : "Arena.Master";
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
            return true;
        }
        catch (Exception ex)
        {
            Skip(key, "play_failed", ex.Message);
            return false;
        }
    }

    private static bool TryStartNativeUi(AudioClip clip, float volume, out string problem)
    {
        if (!Settings.AnnouncerUseUiVolume.Value)
            return ArenaAnnouncerOutput.TryPlay(clip, volume, out problem);
        ArenaAnnouncerOutput.Reset();
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
            || string.Equals(key, "round_lost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "match_won", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "match_lost", StringComparison.OrdinalIgnoreCase))
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
