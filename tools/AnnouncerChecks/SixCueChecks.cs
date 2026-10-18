using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using UnityEngine;

internal static partial class Program
{
    private static void VerifySixCues(string pluginDir, QueuedContext context)
    {
        ArenaMatch.SpaceEvents = false;
        Time.realtimeSinceStartup = 12000;
        foreach (string active in new[] { "no_contest", "embarrassing" }) AddClip(pluginDir, active);
        // Reserve titles are never new award keys or an accidental root override.
        // Registered like every other fixture file, or the owned-directory
        // cleanup in Main cannot remove the folder it created.
        string reserve = Path.Combine(pluginDir, "ArenaMode", "announcements", "reserve");
        Directory.CreateDirectory(reserve);
        foreach (string unused in new[] { "trickshot", "air_superiority" })
        {
            string path = Path.Combine(reserve, unused + ".ogg");
            File.WriteAllBytes(path, new byte[] { 1 });
            CreatedFiles.Add(path);
        }
        Assert(!AnnouncerCueCatalog.LoadPaths(Path.GetDirectoryName(reserve)).Any(path =>
            Path.GetFileNameWithoutExtension(path) is "trickshot" or "air_superiority"),
            "Unused new recordings stay reserve-only");
        AnnouncerService.EnsureLoaded(); PumpShotClips(context, invalidNoScope: false);
        var sounds = Singleton<GUISounds>.Instance;
        var match = new ArenaMatch(); AnnouncerService.Attach(match);
        match.Cue(ArenaAnnouncerCue.Embarrassing);
        Assert(sounds.PlayRequests.Last().Clip.Name == "embarrassing", "Typed own grenade cue uses its own recording");
        Assert(AnnouncerCueCatalog.Classify(ArenaAnnouncerCue.Embarrassing, out var key) == AnnouncerCueKind.Award
            && key == "embarrassing", "Embarrassing is an award, never a lifecycle result");
        Assert(AnnouncerCueCatalog.Classify(ArenaAnnouncerCue.NoContest, out key) == AnnouncerCueKind.Result
            && key == "no_contest", "NoContest retains its distinct result key");
        // A technical end cancels an already queued win and every later award/status.
        AnnouncerService.Attach(match); match.Callout("GODLIKE");
        match.Cue(ArenaAnnouncerCue.MatchWon);
        int before = sounds.PlayRequests.Count;
        match.Cue(ArenaAnnouncerCue.NoContest);
        Assert(sounds.PlayRequests.Count == before + 1 && sounds.PlayRequests.Last().Clip.Name == "no_contest",
            "NoContest replaces a queued victory immediately");
        match.Cue(ArenaAnnouncerCue.NoContest); match.Callout("AIRBORNE");
        match.Cue(ArenaAnnouncerCue.PrepareToFight); match.Cue(ArenaAnnouncerCue.Clutch);
        match.Cue(ArenaAnnouncerCue.MatchWon); match.Cue(ArenaAnnouncerCue.RoundStart);
        Time.realtimeSinceStartup += 10; AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 1, "NoContest is once-only and cannot leak stale speech or victory");
        AnnouncerService.Attach(new ArenaMatch());
        match.Cue(ArenaAnnouncerCue.NoContest);
        Assert(sounds.PlayRequests.Count == before + 1, "Detached old match cannot abort new audio");
        foreach (string condition in new[] { "disabled", "zero", "paused" })
        {
            match = new ArenaMatch(); AnnouncerService.Attach(match);
            match.Cue(ArenaAnnouncerCue.RoundStart); match.Callout("AIRBORNE");
            before = sounds.PlayRequests.Count;
            if (condition == "disabled") Settings.AnnouncerEnabled.Value = false;
            if (condition == "zero") Settings.AnnouncerVolume.Value = 0;
            if (condition == "paused") AudioListener.pause = true;
            match.Cue(ArenaAnnouncerCue.NoContest);
            Settings.AnnouncerEnabled.Value = true; Settings.AnnouncerVolume.Value = .37f; AudioListener.pause = false;
            Time.realtimeSinceStartup += 10; AnnouncerService.Tick(); match.Cue(ArenaAnnouncerCue.NoContest);
            Assert(sounds.PlayRequests.Count == before, "Muted/paused terminal cue cancels stale work without replay: " + condition);
        }
        match = new ArenaMatch(); AnnouncerService.Attach(match); before = sounds.PlayRequests.Count;
        match.Cue(ArenaAnnouncerCue.RoundStart);
        Assert(sounds.PlayRequests.Count == before + 1, "A fresh match clears the terminal audio latch");
        AnnouncerService.Detach(); ArenaMatch.SpaceEvents = true;
    }
}
