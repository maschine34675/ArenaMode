using ArenaMode;
using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using UnityEngine;
using UnityEngine.Networking;

internal static partial class Program
{
    private static void VerifyShotAwards(string pluginDir, QueuedContext context)
    {
        ArenaMatch.SpaceEvents = false;
        Time.realtimeSinceStartup = 9000;
        var sounds = Singleton<GUISounds>.Instance;
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        int logStart = Plugin.ModLogger.Messages.Count;
        foreach (string label in new[] { "TRICKSHOT", "NO SCOPE", "TRICKSHOT", "NO SCOPE" })
        {
            int before = sounds.PlayRequests.Count;
            match.Callout(label);
            Assert(sounds.PlayRequests.Count == before + 1 && sounds.PlayRequests[^1].Clip.Name == "godlike",
                "Missing shot recordings use Godlike exactly once while preserving logical awards: " + label);
            Time.realtimeSinceStartup += 3;
        }
        foreach (string key in new[] { "trickshot", "no_scope" })
            Assert(Plugin.ModLogger.Messages.Skip(logStart).Count(line => line.Contains("provisional recording: '" + key + "'")) == 1,
                "Provisional recording diagnostics are bounded per logical key and match: " + key);

        foreach (var scenario in new[] {
            (First: "NO SCOPE", Second: "KILLING SPREE", Winner: "no_scope", Clip: "godlike"),
            (First: "KILLING SPREE", Second: "NO SCOPE", Winner: "no_scope", Clip: "godlike"),
            (First: "TRICKSHOT", Second: "NO SCOPE", Winner: "trickshot", Clip: "godlike"),
            (First: "NO SCOPE", Second: "TRICKSHOT", Winner: "trickshot", Clip: "godlike"),
            (First: "TRICKSHOT", Second: "TRIPLE KILL", Winner: "triple_kill", Clip: "triple_kill"),
            (First: "TRIPLE KILL", Second: "TRICKSHOT", Winner: "triple_kill", Clip: "triple_kill") })
        {
            AnnouncerService.Attach(match);
            match.Cue(ArenaAnnouncerCue.RoundStart);
            int before = sounds.PlayRequests.Count;
            logStart = Plugin.ModLogger.Messages.Count;
            match.Callout(scenario.First); match.Callout(scenario.Second);
            Assert(sounds.PlayRequests.Count == before, "Shot award fallback waits in the existing bounded callout slot");
            Time.realtimeSinceStartup += .2f; AnnouncerService.Tick();
            Assert(sounds.PlayRequests.Count == before + 1 && sounds.PlayRequests[^1].Clip.Name == scenario.Clip,
                "Shot queue priority chooses the expected recording: " + scenario.First + "/" + scenario.Second);
            if (scenario.Clip == "godlike")
                Assert(Plugin.ModLogger.Messages.Skip(logStart).Any(line => line.Contains("provisional recording: '" + scenario.Winner + "'")),
                    "Godlike fallback retains the selected logical priority: " + scenario.Winner);
        }
        // Future user-supplied root files follow the existing decoder/cache path.
        AddClip(pluginDir, "trickshot"); AddClip(pluginDir, "no_scope");
        AnnouncerService.EnsureLoaded();
        PumpShotClips(context, invalidNoScope: true);
        foreach (var expected in new[] { (Label: "TRICKSHOT", Clip: "trickshot"), (Label: "NO SCOPE", Clip: "godlike") })
        {
            AnnouncerService.Attach(match); int before = sounds.PlayRequests.Count;
            match.Callout(expected.Label);
            Assert(sounds.PlayRequests.Count == before + 1 && sounds.PlayRequests[^1].Clip.Name == expected.Clip,
                "Healthy custom root audio wins; invalid optional decode still falls back: " + expected.Label);
        }
        AnnouncerService.EnsureLoaded(); PumpShotClips(context, invalidNoScope: false);
        AnnouncerService.Attach(match); match.Callout("NO SCOPE");
        Assert(sounds.PlayRequests[^1].Clip.Name == "no_scope", "A repaired optional root recording replaces Godlike on a later normal load");

        AnnouncerService.Attach(match);
        var oldTrick = sounds.PlayRequests.Last(play => play.Clip.Name == "trickshot").Clip;
        oldTrick.SimulateAudioReset(); AudioClip.NextCreatedGetDataResult = false;
        int plays = sounds.PlayRequests.Count; match.Callout("TRICKSHOT");
        Assert(sounds.PlayRequests.Count == plays + 1 && sounds.PlayRequests[^1].Clip.Name == "godlike",
            "Failed custom runtime reconstruction uses one provisional recording");
        AnnouncerService.Attach(match); match.Callout("TRICKSHOT");
        Assert(sounds.PlayRequests[^1].Clip.Name == "trickshot", "Recovered custom PCM regains precedence on the next eligible award");

        foreach (int mute in new[] { 0, 1 })
        {
            AnnouncerService.Attach(match); plays = sounds.PlayRequests.Count;
            if (mute == 0) Settings.AnnouncerEnabled.Value = false;
            else Settings.AnnouncerVolume.Value = 0;
            match.Callout("TRICKSHOT"); match.Callout("NO SCOPE");
            Assert(sounds.PlayRequests.Count == plays, "Logical shot awards preserve enable and volume guards: " + mute);
            Settings.AnnouncerEnabled.Value = true; Settings.AnnouncerVolume.Value = .37f;
        }
        AnnouncerService.Attach(match); logStart = Plugin.ModLogger.Messages.Count;
        sounds.ThrowOnPlay = true;
        try { match.Callout("TRICKSHOT"); }
        finally { sounds.ThrowOnPlay = false; }
        Assert(!Plugin.ModLogger.Messages.Skip(logStart).Any(line => line.Contains("'godlike'")),
            "Native output rejection cannot trigger a second provisional playback attempt");
        AnnouncerService.Attach(match); plays = sounds.PlayRequests.Count;
        match.Callout("UNKNOWN SHOT AWARD");
        Assert(sounds.PlayRequests.Count == plays, "An unknown missing cue cannot inherit the shot-only fallback");
        AnnouncerService.Detach(); ArenaMatch.SpaceEvents = true;
    }

    private static void PumpShotClips(QueuedContext context, bool invalidNoScope)
    {
        for (int i = 0; i < 5 && context.OutstandingAsyncVoids > 0; i++)
        {
            var request = UnityWebRequestMultimedia.Requests.Last(r => !r.Operation.isDone);
            string key = Path.GetFileNameWithoutExtension(new Uri(request.Url).LocalPath);
            request.Content = AudioClip.CreateDecodedFixture(key, 8, 2, 44100);
            if (key == "no_scope" && invalidNoScope) request.Content.GetDataResult = false;
            int count = UnityWebRequestMultimedia.Requests.Count;
            request.Complete(UnityWebRequest.Result.Success);
            context.PumpUntil(() => context.OutstandingAsyncVoids == 0 || UnityWebRequestMultimedia.Requests.Count != count);
        }
        Assert(context.OutstandingAsyncVoids == 0, "Optional shot recording transport completes without a retry loop");
    }
}
