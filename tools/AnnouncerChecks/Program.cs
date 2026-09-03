using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using UnityEngine;
using UnityEngine.Networking;

internal static class Program
{
    private static int _assertions;
    private static readonly List<string> CreatedFiles = new();

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        _assertions++;
    }

    private static string Label(UnityWebRequest request)
        => Path.GetFileNameWithoutExtension(new Uri(request.Url).LocalPath).Replace('_', ' ').ToUpperInvariant();

    private static int Main()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new QueuedContext();
        // Spaces, a fragment delimiter and percent are deliberate file-URI cases.
        var pluginDir = Path.Combine(Path.GetTempPath(), "Arena announcer #50% " + Guid.NewGuid().ToString("N"));
        var soundDir = Path.Combine(pluginDir, "ArenaMode", "sounds");
        var paths = new[] { "double_kill.ogg", "triple_kill.ogg", "godlike.ogg" }
            .Select(file => Path.Combine(soundDir, file)).ToArray();
        try
        {
            Directory.CreateDirectory(soundDir);
            // Placeholder bytes: the controlled transport, not an Ogg decoder,
            // supplies clips. Real asset decode validation is a separate check.
            foreach (var path in paths)
            {
                File.WriteAllText(path, "controlled fake audio request");
                CreatedFiles.Add(path);
            }
            SynchronizationContext.SetSynchronizationContext(context);
            Verify(pluginDir, paths, context);
            VerifyAudioResetRecovery(pluginDir, context);
            VerifyDecoderFailures(pluginDir, context);
            VerifyMissingFolder(pluginDir, context);
            VerifyDiagnostics();
            VerifyOutputDiagnostics();
            VerifyShutdownRace(pluginDir, context);
            VerifyRoundCues(pluginDir, context);
            VerifyNativeUiOutputLifecycle();
            Console.WriteLine($"ARENA_ANNOUNCER_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("ARENA_ANNOUNCER_CHECKS_FAILED: " + exception.Message);
            return 1;
        }
        finally
        {
            AnnouncerService.Shutdown();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            // Delete only our individually named fixture files and empty folders.
            foreach (var path in CreatedFiles)
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(pluginDir))
                foreach (var directory in Directory.GetDirectories(pluginDir, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length))
                {
                    if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(pluginDir) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Fixture cleanup escaped its owned directory.");
                    Directory.Delete(directory, recursive: false);
                }
            if (Directory.Exists(pluginDir)) Directory.Delete(pluginDir);
        }
    }

    private static void Verify(string pluginDir, string[] paths, QueuedContext context)
    {
        var sounds = new GUISounds();
        Singleton<GUISounds>.Instance = sounds;
        var requests = UnityWebRequestMultimedia.Requests;
        var firstMatch = new ArenaMatch();
        AnnouncerService.SetPluginDirectory(pluginDir);
        AnnouncerService.EnsureLoaded();
        AnnouncerService.Attach(firstMatch);
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == 1 && !requests[0].Operation.isDone,
            "Repeated EnsureLoaded must share the in-progress first load.");

        var first = requests[0];
        var uri = new Uri(first.Url);
        Assert(uri.IsFile && uri.Fragment == "" && uri.Query == ""
            && paths.Contains(uri.LocalPath) && first.Url.Contains("%23") && first.Url.Contains("%25")
            && first.Url.Contains("%20"), "File URLs must preserve spaces, # and percent characters.");
        var firstLabel = Label(first);
        firstMatch.Callout(firstLabel);
        Assert(sounds.PlayRequests.Count == 0, "A callout before its clip is loaded must safely remain silent.");

        first.Complete(UnityWebRequest.Result.Success);
        context.PumpUntil(() => requests.Count == 2);
        firstMatch.Callout(firstLabel);
        Assert(sounds.PlayRequests.Count == 1,
            "The first match must play a subsequently loaded clip without reattaching.");
        var firstPlay = sounds.PlayRequests[0];
        Assert(firstPlay.Clip.Name == firstLabel.ToLowerInvariant().Replace(' ', '_'),
            "Native UI primary must preserve the requested audio key.");
        Assert(firstPlay.Clip.CreatedFromPcm,
            "Native UI primary must receive an Arena-owned materialized PCM clip.");
        Assert(firstPlay.Single,
            "Native UI primary must request GUISounds single:true replacement semantics.");
        Assert(!firstPlay.CommonUiSound,
            "Native UI primary must request commonUiSound:false and preserve explicit volume.");
        Assert(firstPlay.Volume == Settings.AnnouncerVolume.Value,
            "Native UI primary must pass the configured announcer volume unchanged.");
        Assert(first.Content == null && !ReferenceEquals(first.Content, null)
            && first.Content.GetDataCalls == 1
            && sounds.PlayRequests[0].Clip.SetDataCalls == 1
            && sounds.PlayRequests[0].Clip.GetDataCalls == 1
            && sounds.PlayRequests[0].Clip.Data.Any(sample => Math.Abs(sample) > 0.00001f),
            "Decoded audio is copied into Arena PCM, read back with signal, then released.");
        Assert(ArenaMode.Plugin.Instance.gameObject.LastAudioSource == null,
            "Native UI playback must not create a private AudioSource on the Arena plugin host.");
        Assert(sounds.SingleSource.priority == 128,
            "Native UI playback must leave EFT's shared source priority unchanged.");

        Settings.AnnouncerEnabled.Value = false;
        firstMatch.Callout(firstLabel);
        Assert(sounds.PlayRequests.Count == 1, "Disabling the announcer must suppress loaded clips.");
        Settings.AnnouncerEnabled.Value = true;
        Singleton<GUISounds>.Instance = null;
        firstMatch.Callout(firstLabel);
        Assert(sounds.PlayRequests.Count == 1, "An unavailable GUISounds singleton must fail soft.");
        Singleton<GUISounds>.Instance = sounds;
        firstMatch.Callout("MONSTER KILL");
        firstMatch.Callout(null);
        firstMatch.Callout("");
        Assert(sounds.PlayRequests.Count == 1, "Missing and empty labels must not play a substitute clip.");

        AnnouncerService.Attach(firstMatch);
        AnnouncerService.Attach(firstMatch);
        firstMatch.Callout(firstLabel);
        Assert(sounds.PlayRequests.Count == 2, "Repeated Attach must not duplicate the callout subscription.");
        AnnouncerService.Detach();
        firstMatch.Callout(firstLabel);
        Assert(sounds.PlayRequests.Count == 2, "Detached matches must stop producing announcer playback.");

        // Replace the match while the remaining clips are still loading. Late
        // completion must populate the cache without reviving the old match.
        var nextMatch = new ArenaMatch();
        AnnouncerService.Attach(nextMatch);
        var failed = requests[1];
        failed.Complete(UnityWebRequest.Result.DataProcessingError);
        context.PumpUntil(() => requests.Count == 3);
        Assert(failed.Disposed && failed.ContentReadCount == 0,
            "An audio decoding error must be disposed without attempting to retrieve its clip.");
        var last = requests[2];
        last.Complete(UnityWebRequest.Result.Success);
        context.PumpUntil(() => context.OutstandingAsyncVoids == 0);
        Assert(requests.Count == 3 && requests.All(request => request.Disposed)
            && requests.All(request => request.Type == UnityEngine.AudioType.OGGVORBIS),
            "Loading must continue after a corrupt clip and dispose all Vorbis requests.");
        nextMatch.Callout(Label(failed));
        Assert(sounds.PlayRequests.Count == 2, "An unsuccessfully decoded clip must remain unavailable.");
        nextMatch.Callout(Label(last));
        Assert(sounds.PlayRequests.Count == 3, "A replacement match must play clips completed after attachment.");
        firstMatch.Callout(firstLabel);
        firstMatch.Callout(Label(last));
        Assert(sounds.PlayRequests.Count == 3, "Late load completion must not resubscribe the ended match.");

        AnnouncerService.Attach(null);
        nextMatch.Callout(firstLabel);
        Assert(sounds.PlayRequests.Count == 3, "Attaching null must detach the active match.");
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == 4 && Label(requests[3]) == Label(failed),
            "A failed first load must retry only its failed clip; healthy cached clips must survive.");
        var beforeCompletion = sounds.PlayRequests.Count;
        FinishLoads(context);
        Assert(sounds.PlayRequests.Count == beforeCompletion, "Repair completion must not replay a skipped old callout.");
        var recoveredMatch = new ArenaMatch();
        AnnouncerService.Attach(recoveredMatch);
        recoveredMatch.Callout(Label(failed));
        Assert(sounds.PlayRequests.Count == beforeCompletion + 1, "A recovered first-load failure must play on the next callout.");
        AnnouncerService.Detach();
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == 4, "Healthy cached clips remain reused after a successful repair.");
        Assert(requests.All(request => request.timeout == 10), "Each native request has a ten-second timeout.");
    }

    private static void VerifyAudioResetRecovery(string pluginDir, QueuedContext context)
    {
        var sounds = Singleton<GUISounds>.Instance;
        var requests = UnityWebRequestMultimedia.Requests;
        var label = Label(requests[0]);
        var originalClip = sounds.PlayRequests.First(play => play.Clip.Name ==
            label.ToLowerInvariant().Replace(' ', '_')).Clip;
        var requestCount = requests.Count;
        var createdCount = AudioClip.CreatedClips.Count;

        // EFT calls AudioSettings.Reset between map selection (where the pack is
        // decoded) and raid start. Unity can leave a Loaded wrapper behind whose
        // native sample metadata has been cleared to zero.
        AudioSettings.SimulateReset();
        Assert(originalClip != null && originalClip.loadState == AudioDataLoadState.Loaded
            && originalClip.samples == 0 && originalClip.channels == 0 && originalClip.length == 0f,
            "The controlled audio reset reproduces the live Loaded-but-zero runtime clip.");
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == requestCount,
            "A native audio reset must not redownload PCM already owned in managed memory.");

        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        var plays = sounds.PlayRequests.Count;
        match.Callout(label);
        var restored = sounds.PlayRequests[^1].Clip;
        Assert(sounds.PlayRequests.Count == plays + 1
            && AudioClip.CreatedClips.Count == createdCount + 1
            && restored != null && !ReferenceEquals(restored, originalClip)
            && restored.samples > 0 && restored.channels == 2 && restored.length > 0f
            && restored.SetDataCalls == 1 && restored.GetDataCalls == 1
            && restored.hideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset),
            "The next cue synchronously reconstructs a signal-bearing runtime clip from managed PCM.");
        Assert(ArenaMode.Plugin.ModLogger.Messages.Any(message =>
                message.Contains("runtime clip rebuilt: '")
                && message.Contains("previous=state=Loaded,frames=0,channels=0")),
            "Reset recovery records the zero-metadata wrapper that forced reconstruction.");

        // A reset before the first cue must also be harmless: the durable cache
        // exists even though no Unity runtime clip has been built for this key.
        AddClip(pluginDir, "pre_cue_reset");
        AnnouncerService.EnsureLoaded();
        FinishLoads(context);
        requestCount = requests.Count;
        AudioSettings.SimulateReset();
        plays = sounds.PlayRequests.Count;
        match.Callout("PRE CUE RESET");
        Assert(requests.Count == requestCount && sounds.PlayRequests.Count == plays + 1
            && sounds.PlayRequests[^1].Clip.samples > 0,
            "Managed PCM loaded before an audio reset remains playable on its first later cue.");
        AnnouncerService.Detach();
    }

    private static void VerifyDecoderFailures(string pluginDir, QueuedContext context)
    {
        var sounds = Singleton<GUISounds>.Instance;
        var requests = UnityWebRequestMultimedia.Requests;
        AddClip(pluginDir, "decode_throw");
        AddClip(pluginDir, "following_clip");
        var count = requests.Count;
        AnnouncerService.EnsureLoaded();
        var failed = requests[^1];
        failed.ThrowOnGetContent = true;
        failed.Complete(UnityWebRequest.Result.Success);
        context.PumpUntil(() => requests.Count == count + 2 || context.OutstandingAsyncVoids == 0);
        Assert(requests.Count == count + 2 && failed.Disposed && failed.ContentReadCount == 1,
            "GetContent throwing for one file must not abort loading subsequent files.");
        var following = requests[^1];
        FinishLoads(context);
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        var plays = sounds.PlayRequests.Count;
        match.Callout(Label(following));
        Assert(sounds.PlayRequests.Count == plays + 1, "A later file remains playable after a GetContent exception.");
        AnnouncerService.Detach();
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count + 3 && Label(requests[^1]) == Label(failed),
            "Next-match loading retries only the file whose decoder threw.");
        FinishLoads(context);

        AddClip(pluginDir, "needs_audio_data");
        AnnouncerService.EnsureLoaded();
        var unloaded = requests[^1];
        var unloadedDecoded = new AudioClip { Name = "needs_audio_data", loadState = AudioDataLoadState.Unloaded };
        unloaded.Content = unloadedDecoded;
        FinishLoads(context);
        Assert(unloadedDecoded.LoadAudioDataCalls == 1
            && unloadedDecoded.loadState == AudioDataLoadState.Loaded
            && unloadedDecoded == null,
            "Unloaded decoder audio is loaded, copied to PCM and released before caching.");

        AddClip(pluginDir, "pcm_capture_failure");
        AnnouncerService.EnsureLoaded();
        var captureFailure = requests[^1];
        var uncopyable = new AudioClip
        {
            Name = "pcm_capture_failure",
            GetDataResult = false
        };
        captureFailure.Content = uncopyable;
        FinishLoads(context);
        Assert(uncopyable == null && uncopyable.GetDataCalls == 1
            && ArenaMode.Plugin.ModLogger.Messages.Any(message =>
                message.Contains("PCM capture failed for 'pcm_capture_failure'")),
            "A decoder that cannot expose PCM is released and never becomes a fragile native-only cache entry.");
        var fallbackMatch = new ArenaMatch();
        AnnouncerService.Attach(fallbackMatch);
        plays = sounds.PlayRequests.Count;
        fallbackMatch.Callout("PCM CAPTURE FAILURE");
        Assert(sounds.PlayRequests.Count == plays,
            "A failed managed capture cannot leak the decoder-owned clip into playback.");
        AnnouncerService.Detach();
        File.Delete(Path.Combine(pluginDir, "ArenaMode", "sounds", "pcm_capture_failure.ogg"));

        AddClip(pluginDir, "runtime_readback_failure");
        AnnouncerService.EnsureLoaded();
        var readbackFailure = requests[^1];
        var decodedForReadback = new AudioClip { Name = "runtime_readback_failure" };
        readbackFailure.Content = decodedForReadback;
        FinishLoads(context);
        AudioClip.NextCreatedGetDataResult = false;
        var readbackMatch = new ArenaMatch();
        AnnouncerService.Attach(readbackMatch);
        plays = sounds.PlayRequests.Count;
        readbackMatch.Callout("RUNTIME READBACK FAILURE");
        var unreadablePcm = AudioClip.CreatedClips[^1];
        Assert(decodedForReadback == null && decodedForReadback.GetDataCalls == 1
            && unreadablePcm == null && unreadablePcm.SetDataCalls == 1 && unreadablePcm.GetDataCalls == 1
            && ArenaMode.Plugin.ModLogger.Messages.Any(message =>
                message.Contains("reason=clip_rebuild_failed")
                && message.Contains("rebuilt AudioClip.GetData returned false")),
            "A failed runtime readback destroys the unusable wrapper and skips playback without losing managed PCM.");
        Assert(sounds.PlayRequests.Count == plays,
            "A runtime reconstruction failure never reaches GUISounds.");
        readbackMatch.Callout("RUNTIME READBACK FAILURE");
        Assert(sounds.PlayRequests.Count == plays + 1
            && sounds.PlayRequests[^1].Clip != null
            && !ReferenceEquals(sounds.PlayRequests[^1].Clip, unreadablePcm),
            "The next cue retries runtime reconstruction from the retained managed PCM.");
        AnnouncerService.Detach();

        AddClip(pluginDir, "slow_decoder");
        AnnouncerService.EnsureLoaded();
        var slow = requests[^1];
        var loadingClip = new AudioClip { Name = "slow_decoder", loadState = AudioDataLoadState.Loading };
        slow.Content = loadingClip;
        slow.Complete(UnityWebRequest.Result.Success);
        context.PumpUntil(() => slow.ContentReadCount == 1);
        Time.realtimeSinceStartup += 11f;
        context.PumpUntil(() => context.OutstandingAsyncVoids == 0);
        Assert(slow.Disposed && loadingClip == null,
            "A clip stuck Loading times out and its owned native object is discarded.");
        AnnouncerService.EnsureLoaded();
        Assert(!ReferenceEquals(requests[^1], slow) && Label(requests[^1]) == "SLOW DECODER",
            "A readiness timeout leaves the affected file retryable at the next match.");
        FinishLoads(context);
    }

    private static void VerifyMissingFolder(string pluginDir, QueuedContext context)
    {
        var alternative = Path.Combine(pluginDir, "later installation");
        var sounds = Singleton<GUISounds>.Instance;
        var requests = UnityWebRequestMultimedia.Requests;
        var count = requests.Count;
        AnnouncerService.SetPluginDirectory(alternative);
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count, "A missing optional audio folder creates no request.");
        var oldMatch = new ArenaMatch();
        AnnouncerService.Attach(oldMatch);
        oldMatch.Callout("FOLDER REPAIR");
        AnnouncerService.Detach();
        AddClip(alternative, "folder_repair");
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count + 1 && Label(requests[^1]) == "FOLDER REPAIR",
            "A previously missing folder is scanned again at the next match.");
        var nextMatch = new ArenaMatch();
        AnnouncerService.Attach(nextMatch);
        var plays = sounds.PlayRequests.Count;
        nextMatch.Callout("FOLDER REPAIR");
        FinishLoads(context);
        oldMatch.Callout("FOLDER REPAIR");
        Assert(sounds.PlayRequests.Count == plays, "Installing a folder never replays skipped or detached callouts.");
        nextMatch.Callout("FOLDER REPAIR");
        Assert(sounds.PlayRequests.Count == plays + 1, "The next live callout uses newly installed audio.");
        AnnouncerService.Detach();
        AnnouncerService.SetPluginDirectory(pluginDir);
    }

    private static void VerifyDiagnostics()
    {
        var sounds = Singleton<GUISounds>.Instance;
        var requests = UnityWebRequestMultimedia.Requests;
        var healthyClip = sounds.PlayRequests.Last(play => play.Clip != null
            && play.Clip.CreatedFromPcm && play.Clip.samples > 0).Clip;
        var key = healthyClip.Name;
        var label = key.Replace('_', ' ').ToUpperInvariant();
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        var logStart = ArenaMode.Plugin.ModLogger.Messages.Count;
        var plays = sounds.PlayRequests.Count;
        Settings.AnnouncerEnabled.Value = false;
        Repeat(() => match.Callout(label));
        Assert(ReasonCount(logStart, key, "disabled") == 1, "Disabled diagnostics are limited to one key/reason per match.");
        Settings.AnnouncerEnabled.Value = true;
        var volume = Settings.AnnouncerVolume.Value;
        Settings.AnnouncerVolume.Value = 0;
        Repeat(() => match.Callout(label));
        Assert(ReasonCount(logStart, key, "volume_zero") == 1, "Zero-volume diagnostics are limited per match.");
        Settings.AnnouncerVolume.Value = volume;
        Singleton<GUISounds>.Instance = null;
        Repeat(() => match.Callout(label));
        Assert(ReasonCount(logStart, key, "ui_unavailable") == 1,
            "A missing native UI host is diagnosed without flooding.");
        Singleton<GUISounds>.Instance = sounds;
        sounds.ThrowOnPlay = true;
        Repeat(() => match.Callout(label));
        Assert(ReasonCount(logStart, key, "play_failed") == 1,
            "Repeated native PlaySound failures log once per match and fail soft.");
        sounds.ThrowOnPlay = false;
        var count = requests.Count;
        Repeat(() => match.Callout("MONSTER KILL"));
        Assert(ReasonCount(logStart, "monster_kill", "missing_clip") == 1 && requests.Count == count,
            "Known missing pack labels are diagnosed without launching repeated disk/network scans.");
        Assert(sounds.PlayRequests.Count == plays, "Diagnostic skip paths never request substitute audio.");

        AnnouncerService.Detach();
        match = new ArenaMatch();
        AnnouncerService.Attach(match);
        logStart = ArenaMode.Plugin.ModLogger.Messages.Count;
        Settings.AnnouncerEnabled.Value = false;
        match.Callout(label);
        Assert(ReasonCount(logStart, key, "disabled") == 1, "Skip diagnostics reset for a new match.");
        Settings.AnnouncerEnabled.Value = true;

        int created = AudioClip.CreatedClips.Count;
        AudioSettings.SimulateReset();
        count = requests.Count;
        plays = sounds.PlayRequests.Count;
        match.Callout(label);
        var rebuilt = sounds.PlayRequests[^1].Clip;
        Assert(requests.Count == count && sounds.PlayRequests.Count == plays + 1
            && AudioClip.CreatedClips.Count == created + 1
            && rebuilt != null && rebuilt.samples > 0 && !ReferenceEquals(rebuilt, healthyClip),
            "A zeroed runtime wrapper is rebuilt synchronously without a transport repair.");

        UnityEngine.Object.Destroy(rebuilt);
        Assert(rebuilt == null && !ReferenceEquals(rebuilt, null),
            "Destroyed clip fixture preserves managed identity while behaving like Unity-null.");
        created = AudioClip.CreatedClips.Count;
        match.Callout(label);
        Assert(requests.Count == count && AudioClip.CreatedClips.Count == created + 1
            && sounds.PlayRequests[^1].Clip != null
            && !ReferenceEquals(sounds.PlayRequests[^1].Clip, rebuilt),
            "A Unity-destroyed runtime wrapper is rebuilt from the same managed PCM cache.");
        AnnouncerService.Detach();
    }

    private static int ReasonCount(int start, string key, string reason)
        => ArenaMode.Plugin.ModLogger.Messages.Skip(start)
            .Count(message => message.Contains(key, StringComparison.OrdinalIgnoreCase)
                && message.Contains("reason=" + reason, StringComparison.Ordinal));

    private static void VerifyOutputDiagnostics()
    {
        var sounds = Singleton<GUISounds>.Instance;
        var healthy = sounds.PlayRequests.Last(play => play.Clip != null
            && play.Clip.loadState == AudioDataLoadState.Loaded).Clip;
        var label = healthy.Name.Replace('_', ' ').ToUpperInvariant();
        var messages = ArenaMode.Plugin.ModLogger.Messages;
        var start = messages.Count;
        var match = new ArenaMatch();
        AudioListener.volume = 0.8f;
        AudioListener.pause = false;
        AnnouncerService.Attach(match);
        Assert(messages.Skip(start).Any(message => message.StartsWith("Arena announcer attached:")
            && message.Contains("enabled=True") && message.Contains("volume=") && message.Contains("ready=")),
            "Attach reports enabled setting, volume and cache readiness.");
        var calls = 0;
        void Call() { match.Callout(label); calls++; }
        int OutputLogs() => messages.Skip(start).Count(message => message.StartsWith("Arena announcer play request:"));
        Call(); Call(); Call(); Call();
        Assert(OutputLogs() == 3, "Stable output logs exactly the first three successful playback requests.");
        Assert(messages.Skip(start).Where(message => message.StartsWith("Arena announcer play request:"))
            .All(message => message.Contains("route=GUISounds.SingleUI.Primary")
                && message.Contains("clipGeneration=")
                && message.Contains("clipBeforePlay=state=Loaded,frames=")
                && message.Contains("clipAfterPlay=state=Loaded,frames=")
                && message.Contains("clipFrames=") && message.Contains("clipChannels=2")
                && message.Contains("clipFrequency=44100") && message.Contains("clipLengthMs=")
                && message.Contains("clipState=Loaded")
                && message.Contains("uiReady=True") && message.Contains("uiDb=-4")
                && message.Contains("masterDb=-2") && message.Contains("listenerVolume=0.8")
                && message.Contains("listenerPaused=False") && message.Contains("speakerMode=Mode5point1")
                && message.Contains("sampleRate=48000") && message.Contains("voices=32/512")
                && message.Contains("dspBuffer=1024x4")),
            "Native UI request diagnostics include clip, mixer, listener, voice and buffer state.");

        AudioListener.pause = true;
        Call(); Call();
        Assert(OutputLogs() == 4 && AudioListener.pause,
            "A changed listener pause is reported once without unpausing the user's audio.");
        AudioListener.volume = 0;
        Call(); Call();
        Assert(OutputLogs() == 5 && AudioListener.volume == 0,
            "A listener mute is observable without the announcer overriding it.");
        sounds.MasterMixer.Parameters["UIVolume"] = -80;
        Call(); Call();
        Assert(OutputLogs() == 6 && sounds.MasterMixer.Parameters["UIVolume"] == -80,
            "A changed UI mixer level is reported once and left untouched.");
        var originalMixer = sounds.MasterMixer;
        sounds.MasterMixer = null;
        Call();
        Assert(OutputLogs() == 7 && messages[^1].Contains("uiDb=unknown") && messages[^1].Contains("masterDb=unknown"),
            "An unavailable mixer is diagnosed without suppressing an otherwise valid native playback request.");
        sounds.MasterMixer = originalMixer;

        Settings.AnnouncerEnabled.Value = false;
        Call(); Call();
        Settings.AnnouncerEnabled.Value = true;
        AnnouncerService.Detach();
        Assert(messages[^1] == $"Arena announcer detached: callouts={calls}, cues=0, "
            + $"playRequests={calls - 2}, skipped=2.",
            "Detach counts every received/requested/skipped callout even when skip logs are deduplicated.");
        var plays = sounds.PlayRequests.Count;
        match.Callout(label);
        Assert(sounds.PlayRequests.Count == plays, "Detached callouts cannot produce playback or update diagnostics.");
        AudioListener.volume = 0.8f;
        AudioListener.pause = false;
        sounds.MasterMixer.Parameters["UIVolume"] = -4;
    }

    private static void VerifyShutdownRace(string pluginDir, QueuedContext context)
    {
        var raceDir = Path.Combine(pluginDir, "shutdown race");
        AddClip(raceDir, "shutdown_race");
        var requests = UnityWebRequestMultimedia.Requests;
        var sounds = Singleton<GUISounds>.Instance;

        AnnouncerService.SetPluginDirectory(raceDir);
        int requestCount = requests.Count;
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == requestCount + 1 && !requests[^1].Operation.isDone,
            "Shutdown-race fixture starts one controlled in-flight decoder request.");
        var stale = requests[^1];

        AnnouncerService.Shutdown();
        stale.Complete(UnityWebRequest.Result.Success);
        context.PumpUntil(() => context.OutstandingAsyncVoids == 0);
        Assert(stale.Disposed && stale.Content == null && !ReferenceEquals(stale.Content, null),
            "A request completed after Shutdown disposes its decoder-owned Unity clip.");

        requestCount = requests.Count;
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == requestCount + 1 && !ReferenceEquals(requests[^1], stale)
            && Label(requests[^1]) == "SHUTDOWN RACE",
            "A stale completion neither republishes managed PCM nor leaves the loader stuck after Shutdown.");
        FinishLoads(context);

        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        int plays = sounds.PlayRequests.Count;
        match.Callout("SHUTDOWN RACE");
        Assert(sounds.PlayRequests.Count == plays + 1
            && sounds.PlayRequests[^1].Clip.Name == "shutdown_race"
            && sounds.PlayRequests[^1].Clip.samples > 0,
            "Only the fresh post-Shutdown load can supply later playback.");

        AnnouncerService.Shutdown();
        AnnouncerService.SetPluginDirectory(pluginDir);
        AnnouncerService.EnsureLoaded();
        FinishLoads(context);
    }

    private static void VerifyRoundCues(string pluginDir, QueuedContext context)
    {
        AddClip(pluginDir, "round_start");
        AddClip(pluginDir, "round_won");
        AddClip(pluginDir, "round_lost");
        var requestCount = UnityWebRequestMultimedia.Requests.Count;
        AnnouncerService.EnsureLoaded();
        Assert(UnityWebRequestMultimedia.Requests.Count == requestCount + 1
            && Label(UnityWebRequestMultimedia.Requests[requestCount]) == "ROUND START",
            "Round start loads ahead of ordinary callouts and result cues.");
        FinishLoads(context);

        var sounds = Singleton<GUISounds>.Instance;
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        AnnouncerService.Attach(match);
        var plays = sounds.PlayRequests.Count;

        match.Cue(ArenaAnnouncerCue.RoundStart);
        match.Cue(ArenaAnnouncerCue.RoundWon);
        match.Cue(ArenaAnnouncerCue.RoundLost);

        Assert(sounds.PlayRequests.Count == plays + 3,
            "Each typed round cue plays exactly once after repeated attachment.");
        Assert(sounds.PlayRequests.Skip(plays).Select(play => play.Clip.Name).SequenceEqual(
                new[] { "round_start", "round_won", "round_lost" }),
            "Typed round cues map to their fixed audio keys in lifecycle order.");

        AnnouncerService.Detach();
        match.Cue(ArenaAnnouncerCue.RoundStart);
        Assert(sounds.PlayRequests.Count == plays + 3,
            "A detached match cannot play a lifecycle cue.");
    }

    private static void VerifyNativeUiOutputLifecycle()
    {
        var sounds = Singleton<GUISounds>.Instance;
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        int requests = sounds.PlayRequests.Count;
        int sharedStops = sounds.SingleSource.StopCalls;
        int sharedPriority = sounds.SingleSource.priority;

        // The native GUISounds route must remain usable without BetterAudio.
        // This catches accidentally restoring the former private bypass channel
        // as either a prerequisite or a first-choice path.
        BetterAudio.Instantiated = false;
        match.Cue(ArenaAnnouncerCue.RoundStart);
        Assert(sounds.PlayRequests.Count == requests + 1,
            "A round cue synchronously requests native GUISounds playback before the event returns.");
        var first = sounds.PlayRequests[^1];
        Assert(first.Clip.Name == "round_start" && first.Clip.CreatedFromPcm
            && first.Single && !first.CommonUiSound
            && first.Volume == Settings.AnnouncerVolume.Value,
            "Native UI primary receives owned PCM with single replacement semantics and configured volume.");
        Assert(ArenaMode.Plugin.Instance.gameObject.LastAudioSource == null,
            "Native UI primary never creates an Arena-owned AudioSource.");
        Assert(sounds.SingleSource.priority == sharedPriority,
            "Playback never leases or otherwise mutates EFT's shared UI-source priority.");

        match.Cue(ArenaAnnouncerCue.RoundWon);
        Assert(sounds.PlayRequests.Count == requests + 2
            && sounds.PlayRequests[^1].Clip.Name == "round_won"
            && sounds.PlayRequests.Skip(requests).All(play => play.Single && !play.CommonUiSound),
            "Successive Arena cues are sent exactly once through GUISounds' native single-source contract.");

        int stopsAfterRequests = sounds.SingleSource.StopCalls;
        Assert(stopsAfterRequests == sharedStops + 2,
            "Only GUISounds itself performs its normal replace-before-PlayOneShot stops.");
        AnnouncerService.Detach();
        Assert(sounds.SingleSource.StopCalls == stopsAfterRequests
            && sounds.SingleSource.priority == sharedPriority,
            "Detach only removes Arena subscriptions; it neither stops nor reprioritizes shared UI audio.");
        AnnouncerService.Shutdown();
        Assert(sounds.SingleSource.StopCalls == stopsAfterRequests
            && sounds.SingleSource.priority == sharedPriority
            && ArenaMode.Plugin.Instance.gameObject.LastAudioSource == null,
            "Shutdown leaves the shared native source and its current one-shot untouched.");
        BetterAudio.Instantiated = true;
    }

    private static void Repeat(Action action)
    {
        for (var i = 0; i < 20; i++) action();
    }

    private static void AddClip(string pluginDirectory, string key)
    {
        var path = Path.Combine(pluginDirectory, "ArenaMode", "sounds", key + ".ogg");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "controlled fake audio request");
        CreatedFiles.Add(path);
    }

    private static void FinishLoads(QueuedContext context)
    {
        for (var attempt = 0; attempt < 100 && context.OutstandingAsyncVoids > 0; attempt++)
        {
            var current = UnityWebRequestMultimedia.Requests.LastOrDefault(request => !request.Operation.isDone);
            if (current == null)
                throw new InvalidOperationException("Expected a pending transport request, not an unbounded audio-readiness wait.");
            var requestCount = UnityWebRequestMultimedia.Requests.Count;
            current.Complete(UnityWebRequest.Result.Success);
            context.PumpUntil(() => context.OutstandingAsyncVoids == 0
                || UnityWebRequestMultimedia.Requests.Count != requestCount);
        }
        Assert(context.OutstandingAsyncVoids == 0, "All controlled audio loads finish within the bounded request pump.");
    }
}
