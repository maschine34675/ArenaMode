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
            VerifyRecovery(pluginDir, context);
            VerifyDecoderFailures(pluginDir, context);
            VerifyMissingFolder(pluginDir, context);
            VerifyDiagnosticsAndRepairThrottle(context);
            VerifyLateReadinessRace(pluginDir, context);
            VerifyOutputDiagnostics();
            VerifyRoundCues(pluginDir, context);
            VerifyDedicatedOutputLifecycle();
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
        Assert(sounds.Played.Count == 0, "A callout before its clip is loaded must safely remain silent.");

        first.Complete(UnityWebRequest.Result.Success);
        context.PumpUntil(() => requests.Count == 2);
        firstMatch.Callout(firstLabel);
        Assert(sounds.Played.Count == 1,
            "The first match must play a subsequently loaded clip without reattaching.");
        Assert(sounds.Played[0].Clip.Name == firstLabel.ToLowerInvariant().Replace(' ', '_')
            && sounds.Played[0].Single && !sounds.Played[0].CommonUiSound
            && sounds.Played[0].Volume == Settings.AnnouncerVolume.Value,
            "Playback must preserve the requested label, replacement semantics and configured volume.");
        var output = ArenaMode.Plugin.Instance.gameObject.LastAudioSource;
        Assert(output != null && !output.playOnAwake && !output.loop
            && output.spatialBlend == 0f && !output.spatialize && output.dopplerLevel == 0f
            && output.bypassReverbZones && output.priority == 0,
            "Announcer playback owns one highest-priority non-spatial source on the persistent plugin host.");
        Assert(ReferenceEquals(output.outputAudioMixerGroup, BetterAudio.Instance.NonSpatialBypassMixer),
            "Announcer playback uses EFT's master-controlled non-spatial bypass route.");

        Settings.AnnouncerEnabled.Value = false;
        firstMatch.Callout(firstLabel);
        Assert(sounds.Played.Count == 1, "Disabling the announcer must suppress loaded clips.");
        Settings.AnnouncerEnabled.Value = true;
        Singleton<GUISounds>.Instance = null;
        firstMatch.Callout(firstLabel);
        Assert(sounds.Played.Count == 1, "An unavailable native audio singleton must safely remain silent.");
        Singleton<GUISounds>.Instance = sounds;
        firstMatch.Callout("MONSTER KILL");
        firstMatch.Callout(null);
        firstMatch.Callout("");
        Assert(sounds.Played.Count == 1, "Missing and empty labels must not play a substitute clip.");

        AnnouncerService.Attach(firstMatch);
        AnnouncerService.Attach(firstMatch);
        firstMatch.Callout(firstLabel);
        Assert(sounds.Played.Count == 2, "Repeated Attach must not duplicate the callout subscription.");
        AnnouncerService.Detach();
        firstMatch.Callout(firstLabel);
        Assert(sounds.Played.Count == 2, "Detached matches must stop producing announcer playback.");

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
        Assert(sounds.Played.Count == 2, "An unsuccessfully decoded clip must remain unavailable.");
        nextMatch.Callout(Label(last));
        Assert(sounds.Played.Count == 3, "A replacement match must play clips completed after attachment.");
        firstMatch.Callout(firstLabel);
        firstMatch.Callout(Label(last));
        Assert(sounds.Played.Count == 3, "Late load completion must not resubscribe the ended match.");

        AnnouncerService.Attach(null);
        nextMatch.Callout(firstLabel);
        Assert(sounds.Played.Count == 3, "Attaching null must detach the active match.");
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == 4 && Label(requests[3]) == Label(failed),
            "A failed first load must retry only its failed clip; healthy cached clips must survive.");
        var beforeCompletion = sounds.Played.Count;
        FinishLoads(context);
        Assert(sounds.Played.Count == beforeCompletion, "Repair completion must not replay a skipped old callout.");
        var recoveredMatch = new ArenaMatch();
        AnnouncerService.Attach(recoveredMatch);
        recoveredMatch.Callout(Label(failed));
        Assert(sounds.Played.Count == beforeCompletion + 1, "A recovered first-load failure must play on the next callout.");
        AnnouncerService.Detach();
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == 4, "Healthy cached clips remain reused after a successful repair.");
        Assert(requests.All(request => request.timeout == 10), "Each native request has a ten-second timeout.");
    }

    private static void VerifyRecovery(string pluginDir, QueuedContext context)
    {
        var sounds = Singleton<GUISounds>.Instance;
        var requests = UnityWebRequestMultimedia.Requests;
        var label = Label(requests[0]);
        var originalClip = requests[0].Content;
        var count = requests.Count;
        UnityEngine.Object.Destroy(originalClip);
        Assert(originalClip == null && !ReferenceEquals(originalClip, null),
            "Destroyed clip fixture preserves managed identity while behaving like Unity-null.");
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count + 1 && Label(requests[^1]) == label,
            "Next-match validation reloads a destroyed Unity clip, not healthy siblings.");
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count + 1, "Repeated validation deduplicates an in-flight repair.");
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        var plays = sounds.Played.Count;
        match.Callout(label);
        AnnouncerService.Detach();
        FinishLoads(context);
        match.Callout(label);
        Assert(sounds.Played.Count == plays, "A detached match stays silent throughout late repair completion.");
        var nextMatch = new ArenaMatch();
        AnnouncerService.Attach(nextMatch);
        nextMatch.Callout(label);
        var restored = sounds.Played[^1].Clip;
        Assert(sounds.Played.Count == plays + 1 && restored != null && !ReferenceEquals(restored, originalClip),
            "Replacement match plays the repaired native clip, never a destroyed wrapper.");

        AnnouncerService.Detach();
        restored.loadState = AudioDataLoadState.Loading;
        count = requests.Count;
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count && context.OutstandingAsyncVoids > 0,
            "A still-loading cached clip is awaited rather than immediately replaced.");
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count, "Concurrent readiness checks do not duplicate a cached-clip load.");
        restored.loadState = AudioDataLoadState.Loaded;
        context.PumpUntil(() => context.OutstandingAsyncVoids == 0);
        AnnouncerService.Attach(nextMatch);
        nextMatch.Callout(label);
        Assert(ReferenceEquals(sounds.Played[^1].Clip, restored), "A recovered cached Loading clip retains identity.");

        restored.loadState = AudioDataLoadState.Unloaded;
        AnnouncerService.Detach();
        AnnouncerService.EnsureLoaded();
        FinishLoads(context);
        AnnouncerService.Attach(nextMatch);
        nextMatch.Callout(label);
        Assert(sounds.Played[^1].Clip.loadState == AudioDataLoadState.Loaded,
            "An Unloaded cached clip is made ready before playback in the next match.");
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
        var plays = sounds.Played.Count;
        match.Callout(Label(following));
        Assert(sounds.Played.Count == plays + 1, "A later file remains playable after a GetContent exception.");
        AnnouncerService.Detach();
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count + 3 && Label(requests[^1]) == Label(failed),
            "Next-match loading retries only the file whose decoder threw.");
        FinishLoads(context);

        AddClip(pluginDir, "needs_audio_data");
        AnnouncerService.EnsureLoaded();
        var unloaded = requests[^1];
        unloaded.Content = new AudioClip { Name = "needs_audio_data", loadState = AudioDataLoadState.Unloaded };
        FinishLoads(context);
        Assert(unloaded.Content.LoadAudioDataCalls == 1 && unloaded.Content.loadState == AudioDataLoadState.Loaded,
            "Success with Unloaded audio requires LoadAudioData before entering the playable cache.");

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
        var plays = sounds.Played.Count;
        nextMatch.Callout("FOLDER REPAIR");
        FinishLoads(context);
        oldMatch.Callout("FOLDER REPAIR");
        Assert(sounds.Played.Count == plays, "Installing a folder never replays skipped or detached callouts.");
        nextMatch.Callout("FOLDER REPAIR");
        Assert(sounds.Played.Count == plays + 1, "The next live callout uses newly installed audio.");
        AnnouncerService.Detach();
        AnnouncerService.SetPluginDirectory(pluginDir);
    }

    private static void VerifyDiagnosticsAndRepairThrottle(QueuedContext context)
    {
        var sounds = Singleton<GUISounds>.Instance;
        var requests = UnityWebRequestMultimedia.Requests;
        var healthy = requests.First(request => request.Content != null && request.Content.loadState == AudioDataLoadState.Loaded);
        var label = Label(healthy);
        var key = Path.GetFileNameWithoutExtension(new Uri(healthy.Url).LocalPath);
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        var logStart = ArenaMode.Plugin.ModLogger.Messages.Count;
        var plays = sounds.Played.Count;
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
        Assert(ReasonCount(logStart, key, "ui_unavailable") == 1, "Missing native UI audio is diagnosed without flooding.");
        Singleton<GUISounds>.Instance = sounds;
        sounds.ThrowOnPlay = true;
        Repeat(() => match.Callout(label));
        Assert(ReasonCount(logStart, key, "play_failed") == 1, "Repeated native playback failures log once per match.");
        sounds.ThrowOnPlay = false;
        var count = requests.Count;
        Repeat(() => match.Callout("MONSTER KILL"));
        Assert(ReasonCount(logStart, "monster_kill", "missing_clip") == 1 && requests.Count == count,
            "Known missing pack labels are diagnosed without launching repeated disk/network scans.");
        Assert(sounds.Played.Count == plays, "Diagnostic skip paths never request substitute audio.");

        AnnouncerService.Detach();
        match = new ArenaMatch();
        AnnouncerService.Attach(match);
        logStart = ArenaMode.Plugin.ModLogger.Messages.Count;
        Settings.AnnouncerEnabled.Value = false;
        match.Callout(label);
        Assert(ReasonCount(logStart, key, "disabled") == 1, "Skip diagnostics reset for a new match.");
        Settings.AnnouncerEnabled.Value = true;

        healthy.Content.loadState = AudioDataLoadState.Failed;
        Time.realtimeSinceStartup += 100f;
        count = requests.Count;
        plays = sounds.Played.Count;
        Repeat(() => match.Callout(label));
        Assert(requests.Count == count + 1, "Repeated invalid-clip callouts start only one in-flight repair.");
        Assert(ReasonCount(logStart, key, "invalid_clip") == 1
            && ReasonCount(logStart, key, "loading") == 1,
            "Invalid and loading diagnostics are each bounded per key/reason.");
        requests[^1].Complete(UnityWebRequest.Result.ConnectionError);
        context.PumpUntil(() => context.OutstandingAsyncVoids == 0);
        Repeat(() => match.Callout(label));
        Assert(requests.Count == count + 1, "A failed repair does not restart repeatedly at the same clock time.");
        Time.realtimeSinceStartup += 9f;
        match.Callout(label);
        Assert(requests.Count == count + 1, "Repair attempts remain throttled for at least ten seconds.");
        Time.realtimeSinceStartup += 1.1f;
        match.Callout(label);
        Assert(requests.Count == count + 2, "A known failed clip becomes retryable after the repair interval.");
        FinishLoads(context);
        Assert(sounds.Played.Count == plays, "Repair completion alone does not enqueue late playback.");
        match.Callout(label);
        Assert(sounds.Played.Count == plays + 1, "A repaired clip plays on the next new callout.");
        AnnouncerService.Detach();
    }

    private static int ReasonCount(int start, string key, string reason)
        => ArenaMode.Plugin.ModLogger.Messages.Skip(start)
            .Count(message => message.Contains(key, StringComparison.OrdinalIgnoreCase)
                && message.Contains("reason=" + reason, StringComparison.Ordinal));

    private static void VerifyLateReadinessRace(string pluginDir, QueuedContext context)
    {
        var sounds = Singleton<GUISounds>.Instance;
        var requests = UnityWebRequestMultimedia.Requests;
        var nativePath = Path.Combine(pluginDir, "ArenaMode", "sounds") + Path.DirectorySeparatorChar;
        var cached = requests.Last(request => request.Content != null
            && request.Content.loadState == AudioDataLoadState.Loaded
            && new Uri(request.Url).LocalPath.StartsWith(nativePath, StringComparison.OrdinalIgnoreCase));
        var oldClip = cached.Content;
        var label = Label(cached);
        oldClip.loadState = AudioDataLoadState.Loading;
        var count = requests.Count;
        AnnouncerService.EnsureLoaded();
        Assert(requests.Count == count && context.OutstandingAsyncVoids > 0,
            "Late-readiness race first waits for the existing Loading clip.");
        Time.realtimeSinceStartup += 11f;
        context.PumpUntil(() => requests.Count > count);
        var replacement = requests[^1];
        Assert(Label(replacement) == label, "Readiness timeout starts a replacement request for the affected clip only.");
        oldClip.loadState = AudioDataLoadState.Loaded;
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        match.Callout(label);
        Assert(ReferenceEquals(sounds.Played[^1].Clip, oldClip),
            "The original clip can recover and play while its replacement request is in flight.");
        FinishLoads(context);
        Assert(oldClip != null && replacement.Content == null && !ReferenceEquals(replacement.Content, null),
            "Late-ready cached clip survives; its unnecessary downloaded replacement is destroyed.");
        match.Callout(label);
        Assert(ReferenceEquals(sounds.Played[^1].Clip, oldClip),
            "Subsequent callouts retain the original late-ready native clip.");
        AnnouncerService.Detach();
    }

    private static void VerifyOutputDiagnostics()
    {
        var sounds = Singleton<GUISounds>.Instance;
        var healthy = UnityWebRequestMultimedia.Requests.Last(request => request.Content != null
            && request.Content.loadState == AudioDataLoadState.Loaded);
        var label = Label(healthy);
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
            .All(message => message.Contains("uiActive=True") && message.Contains("uiDb=-4")
                && message.Contains("masterDb=-2") && message.Contains("listenerVolume=0.8")
                && message.Contains("listenerPaused=False")),
            "Playback diagnostics include native mixer levels and listener state.");

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
            "A changed Interface mixer level is reported once and left untouched.");
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
        Assert(messages[^1] == $"Arena announcer detached: callouts={calls}, cues=0, playRequests={calls - 2}, skipped=2.",
            "Detach counts every received/requested/skipped callout even when skip logs are deduplicated.");
        var plays = sounds.Played.Count;
        match.Callout(label);
        Assert(sounds.Played.Count == plays, "Detached callouts cannot produce playback or update diagnostics.");
        AudioListener.volume = 0.8f;
        AudioListener.pause = false;
        sounds.MasterMixer.Parameters["UIVolume"] = -4;
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
        var plays = sounds.Played.Count;

        match.Cue(ArenaAnnouncerCue.RoundStart);
        match.Cue(ArenaAnnouncerCue.RoundWon);
        match.Cue(ArenaAnnouncerCue.RoundLost);

        Assert(sounds.Played.Count == plays + 3,
            "Each typed round cue plays exactly once after repeated attachment.");
        Assert(sounds.Played.Skip(plays).Select(play => play.Clip.Name).SequenceEqual(
                new[] { "round_start", "round_won", "round_lost" }),
            "Typed round cues map to their fixed audio keys in lifecycle order.");

        AnnouncerService.Detach();
        match.Cue(ArenaAnnouncerCue.RoundStart);
        Assert(sounds.Played.Count == plays + 3,
            "A detached match cannot play a lifecycle cue.");
    }

    private static void VerifyDedicatedOutputLifecycle()
    {
        var sounds = Singleton<GUISounds>.Instance;
        var output = ArenaMode.Plugin.Instance.gameObject.LastAudioSource;
        Assert(output != null, "The successful Arena session retains exactly one dedicated output source.");
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        int stops = output.StopCalls;
        match.Cue(ArenaAnnouncerCue.RoundStart);
        match.Cue(ArenaAnnouncerCue.RoundWon);
        Assert(ReferenceEquals(output, ArenaMode.Plugin.Instance.gameObject.LastAudioSource)
            && output.StopCalls == stops + 2,
            "Overlapping Arena cues replace each other on the same private source.");

        BetterAudio.Instantiated = false;
        match.Cue(ArenaAnnouncerCue.RoundLost);
        Assert(output.outputAudioMixerGroup?.name == "UI",
            "If BetterAudio is unavailable, the existing private source falls back to EFT's UI mixer.");
        BetterAudio.Instantiated = true;
        match.Cue(ArenaAnnouncerCue.RoundStart);
        Assert(ReferenceEquals(output.outputAudioMixerGroup, BetterAudio.Instance.NonSpatialBypassMixer),
            "The private source rebinds when EFT's non-spatial bypass route returns.");

        int logStart = ArenaMode.Plugin.ModLogger.Messages.Count;
        output.isVirtual = true;
        Time.realtimeSinceStartup += 0.1f;
        AnnouncerService.Tick();
        Assert(ArenaMode.Plugin.ModLogger.Messages.Skip(logStart).Any(message =>
                message.StartsWith("Arena announcer output check:") && message.Contains("virtual=True")),
            "A next-frame probe diagnoses a source Unity silently virtualized after Play returned.");
        output.isVirtual = false;

        match.Cue(ArenaAnnouncerCue.RoundStart);
        logStart = ArenaMode.Plugin.ModLogger.Messages.Count;
        UnityEngine.Object.Destroy(output);
        Time.realtimeSinceStartup += 0.1f;
        AnnouncerService.Tick();
        AnnouncerService.Tick();
        Assert(ArenaMode.Plugin.ModLogger.Messages.Skip(logStart).Count(message =>
                message.Contains("source was destroyed before the next audio frame")) == 1,
            "A source destroyed after Play is diagnosed once and clears its delayed check.");
        match.Cue(ArenaAnnouncerCue.RoundWon);
        var replacement = ArenaMode.Plugin.Instance.gameObject.LastAudioSource;
        Assert(replacement != null && !ReferenceEquals(replacement, output)
            && ReferenceEquals(replacement.outputAudioMixerGroup, BetterAudio.Instance.NonSpatialBypassMixer),
            "A destroyed private source is recreated and explicitly rebound to the current native route.");

        stops = replacement.StopCalls;
        AnnouncerService.Detach();
        Assert(replacement.StopCalls == stops + 1 && !replacement.isPlaying,
            "Detach stops Arena audio at the match lifecycle boundary.");
        stops = replacement.StopCalls;
        AnnouncerService.Shutdown();
        Assert(replacement == null && replacement.StopCalls == stops + 1,
            "Plugin shutdown reuses the idempotent detach stop and destroys only the Arena-owned source.");
        Assert(sounds.Played.Count > 0, "Dedicated output lifecycle never clears prior playback evidence.");
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
