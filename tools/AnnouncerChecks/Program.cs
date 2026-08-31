using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using UnityEngine.Networking;

internal static class Program
{
    private static int _assertions;

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
                File.WriteAllText(path, "controlled fake audio request");
            SynchronizationContext.SetSynchronizationContext(context);
            Verify(pluginDir, paths, context);
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
            AnnouncerService.Detach();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            // Delete only our individually named fixture files and empty folders.
            foreach (var path in paths)
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(soundDir)) Directory.Delete(soundDir);
            var arenaDir = Path.Combine(pluginDir, "ArenaMode");
            if (Directory.Exists(arenaDir)) Directory.Delete(arenaDir);
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
            "Playback must preserve the requested label, single UI routing and configured volume.");

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
        Assert(requests.Count == 3, "A completed load must stay cached across later matches.");
    }
}
