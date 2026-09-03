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
    private static GUISounds _outputHost;
    private static AudioSource _output;
    private static AudioMixerGroup _outputRoute;
    private static int _calloutsReceived;
    private static int _cuesReceived;
    private static int _requested;
    private static int _skipped;
    private static string _lastOutputState;
    private static AudioSource _pendingOutputCheck;
    private static AudioClip _pendingClipCheck;
    private static string _pendingKeyCheck;
    private static float _outputCheckAt;
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

        // Detach is the exact Arena lifecycle boundary, including a partial
        // OnMatchStart failure. Never let its private source leak into a normal raid.
        ClearOutputCheck();
        if (_output != null)
            _output.Stop();
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

            if (!Singleton<GUISounds>.Instantiated)
            {
                Skip(key, "ui_unavailable");
                return;
            }

            GUISounds ui = Singleton<GUISounds>.Instance;
            if (!TryEnsureOutput(ui, out string outputProblem))
            {
                Skip(key, "output_unavailable", outputProblem);
                return;
            }

            // GUISounds.PlaySound(single:true) uses one global UI source for every
            // caller. Any menu sound can Stop() that source between this request and
            // the next audio frame. Keep a private source on the persistent plugin
            // host instead. EFT's non-spatial bypass route still obeys Master but
            // avoids applying the Interface attenuation a second time on top of the
            // dedicated Arena volume. Only another Arena cue may replace this cue.
            _output.Stop();
            _output.clip = clip;
            _output.volume = Settings.AnnouncerVolume.Value;
            _output.Play();
            _requested++;
            _pendingOutputCheck = _output;
            _pendingClipCheck = clip;
            _pendingKeyCheck = key;
            _outputCheckAt = Time.realtimeSinceStartup + OutputCheckDelaySeconds;
            var outputState = $"volume={Number(Settings.AnnouncerVolume.Value)}, {DescribeOutput()}";
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

    private static string DescribeOutput()
    {
        // Diagnosis must never change mixer settings, bypass mute or turn a
        // successful playback request into an exception. A request is not proof
        // of audibility; the native source/device can still be muted or stopped.
        try
        {
            var ui = Singleton<GUISounds>.Instance;
            var mixer = ui.MasterMixer;
            var uiDb = mixer != null && mixer.GetFloat("UIVolume", out var u) ? Number(u) : "unknown";
            var masterDb = mixer != null && mixer.GetFloat("MasterVolume", out var m) ? Number(m) : "unknown";
            string channel = _output == null
                ? "channel=missing"
                : $"channelEnabled={_output.enabled}, channelMute={_output.mute}, "
                  + $"channelPlaying={_output.isPlaying}, channelVirtual={_output.isVirtual}, "
                  + $"priority={_output.priority}, route='{_output.outputAudioMixerGroup?.name ?? "none"}'";
            return $"uiActive={ui.isActiveAndEnabled}, uiDb={uiDb}, masterDb={masterDb}, "
                + $"listenerVolume={Number(AudioListener.volume)}, listenerPaused={AudioListener.pause}, {channel}";
        }
        catch
        {
            return "outputState=unavailable";
        }
    }

    private static bool TryEnsureOutput(GUISounds ui, out string problem)
    {
        problem = null;
        if (ui == null)
        {
            problem = "the native UI audio host was destroyed";
            return false;
        }

        AudioMixerGroup route = null;
        if (BetterAudio.Instantiated)
            route = BetterAudio.Instance?.NonSpatialBypassMixer;
        if (route == null)
        {
            AudioMixer mixer = ui.MasterMixer;
            AudioMixerGroup[] groups = mixer?.FindMatchingGroups("UI");
            if (groups != null && groups.Length > 0)
                route = groups[0];
        }

        if (route == null)
        {
            problem = "neither EFT's non-spatial bypass nor its UI mixer group was available";
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
            _outputHost = ui;
            _output = output;
            // Force the common assignment block even when only the Unity source
            // was destroyed while its host and former route survived.
            _outputRoute = null;
            Plugin.ModLogger.LogInfo("Arena announcer created a dedicated non-spatial audio channel.");
        }

        if (!ReferenceEquals(_outputHost, ui) || !ReferenceEquals(_outputRoute, route))
        {
            _outputHost = ui;
            _outputRoute = route;
            _output.outputAudioMixerGroup = route;
            Plugin.ModLogger.LogInfo($"Arena announcer output route: '{route.name}'.");
        }
        return true;
    }

    internal static void Tick()
    {
        AudioSource output = _pendingOutputCheck;
        if (ReferenceEquals(output, null) || Time.realtimeSinceStartup < _outputCheckAt)
            return;

        AudioClip clip = _pendingClipCheck;
        string key = _pendingKeyCheck;
        bool isCurrentOutput = ReferenceEquals(output, _output);
        ClearOutputCheck();
        if (!isCurrentOutput)
        {
            return;
        }

        if (output == null)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena announcer output check: '{key}' source was destroyed before the next audio frame.");
            return;
        }

        if (!ReferenceEquals(output.clip, clip))
            return;

        if (!output.enabled || output.mute || !output.isPlaying || output.isVirtual)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena announcer output check: '{key}' was not on a real audible source after one audio frame; "
                + $"enabled={output.enabled}, mute={output.mute}, playing={output.isPlaying}, "
                + $"virtual={output.isVirtual}, priority={output.priority}, "
                + $"route='{output.outputAudioMixerGroup?.name ?? "none"}'.");
        }
    }

    private static void ClearOutputCheck()
    {
        _pendingOutputCheck = null;
        _pendingClipCheck = null;
        _pendingKeyCheck = null;
        _outputCheckAt = 0f;
    }

    internal static void Shutdown()
    {
        Detach();
        if (_output != null)
            UnityEngine.Object.Destroy(_output);
        _output = null;
        _outputHost = null;
        _outputRoute = null;
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

            var clip = DownloadHandlerAudioClip.GetContent(request);
            var retained = false;
            try
            {
                if (!await WaitForClipReady(clip))
                {
                    Plugin.ModLogger.LogWarning($"Arena announcer clip not ready: '{Path.GetFileNameWithoutExtension(path)}', "
                        + $"state={ClipState(clip)}.");
                    return null;
                }
                retained = true;
                return clip;
            }
            finally
            {
                if (!retained && clip != null)
                    UnityEngine.Object.Destroy(clip);
            }
        }
    }
}
