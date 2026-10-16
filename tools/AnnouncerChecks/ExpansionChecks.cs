using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using UnityEngine;
using UnityEngine.Networking;

internal static partial class Program
{
    private static readonly (ArenaAnnouncerCue Cue, string Key)[] OptionalCues =
    {
        (ArenaAnnouncerCue.PrepareToFight, "prepare_to_fight"), (ArenaAnnouncerCue.Three, "three"),
        (ArenaAnnouncerCue.Two, "two"), (ArenaAnnouncerCue.One, "one"),
        (ArenaAnnouncerCue.TakenTheLead, "taken_the_lead"), (ArenaAnnouncerCue.LostTheLead, "lost_the_lead"),
        (ArenaAnnouncerCue.TiedForTheLead, "tied_for_the_lead"),
        (ArenaAnnouncerCue.OneMinuteRemaining, "one_minute_remaining"),
        (ArenaAnnouncerCue.ThirtySecondsRemaining, "thirty_seconds_remaining"),
        (ArenaAnnouncerCue.TenSecondsRemaining, "ten_seconds_remaining"),
        (ArenaAnnouncerCue.FiveKillsRemaining, "five_kills_remaining"),
        (ArenaAnnouncerCue.OneKillRemaining, "one_kill_remaining"),
        (ArenaAnnouncerCue.MatchPoint, "match_point"), (ArenaAnnouncerCue.FinalRound, "final_round"),
        (ArenaAnnouncerCue.FinalWeapon, "final_weapon"), (ArenaAnnouncerCue.LadderComplete, "ladder_complete"),
        (ArenaAnnouncerCue.Draw, "draw"), (ArenaAnnouncerCue.Shutdown, "shutdown"),
        (ArenaAnnouncerCue.Clutch, "clutch"),
    };

    private static void VerifyExpansion(string pluginDir, QueuedContext context)
    {
        ArenaMatch.SpaceEvents = false;
        Time.realtimeSinceStartup = 5000;
        Time.frameCount = 100;
        var sounds = Singleton<GUISounds>.Instance;
        var match = new ArenaMatch();
        AnnouncerService.Attach(match);
        Assert(ReferenceEquals(ArenaAnnouncementDirector.Attached, match),
            "The audio attachment gives its exact match to the progress director.");
        int before = sounds.PlayRequests.Count;
        match.Cue(ArenaAnnouncerCue.Clutch);
        match.Cue(ArenaAnnouncerCue.RoundWon);
        Assert(sounds.PlayRequests.Count == before + 1 && sounds.PlayRequests[^1].Clip.Name == "round_won",
            "A missing optional Clutch preserves the established win recording.");

        string soundsDir = Path.Combine(pluginDir, "ArenaMode", "sounds");
        foreach (var cue in OptionalCues) AddExpansionFile(Path.Combine(soundsDir, "reserve", cue.Key + ".ogg"));
        string[] excluded = { "round_won", "round_lost", "victory", "defeat", "your_flag_has_been_taken",
            "enemy_flag_taken", "flag_dropped", "flag_returned", "flag_captured", "unknown_bonus" };
        foreach (string key in excluded) AddExpansionFile(Path.Combine(soundsDir, "reserve", key + ".ogg"));
        AddExpansionFile(Path.Combine(soundsDir, "reserve", "nested", "three.ogg"));
        AddExpansionFile(Path.Combine(soundsDir, "three.ogg"));
        int loadStart = UnityWebRequestMultimedia.Requests.Count;
        AnnouncerService.EnsureLoaded();
        for (int i = 0; i < 30 && context.OutstandingAsyncVoids > 0; i++)
        {
            var request = UnityWebRequestMultimedia.Requests.Last(r => !r.Operation.isDone);
            string key = Path.GetFileNameWithoutExtension(new Uri(request.Url).LocalPath);
            int frames = key switch { "prepare_to_fight" => 64742, "three" => 41783, "two" => 36445, "one" => 37767, _ => 8 };
            request.Content = AudioClip.CreateDecodedFixture(key, frames, 2, 44100);
            int count = UnityWebRequestMultimedia.Requests.Count;
            request.Complete(UnityWebRequest.Result.Success);
            context.PumpUntil(() => context.OutstandingAsyncVoids == 0 || UnityWebRequestMultimedia.Requests.Count != count);
        }
        Assert(context.OutstandingAsyncVoids == 0, "Optional assets complete their bounded controlled load.");
        var loaded = UnityWebRequestMultimedia.Requests.Skip(loadStart)
            .Select(request => new Uri(request.Url).LocalPath).ToArray();
        Assert(loaded.Length == 19 && loaded.Select(Path.GetFileNameWithoutExtension).ToHashSet()
                .SetEquals(OptionalCues.Select(cue => cue.Key)),
            "Runtime loading admits exactly the 19 optional keys, with no recursive alternate or CTF collision.");
        Assert(loaded.Count(path => Path.GetFileNameWithoutExtension(path) == "three") == 1
                && loaded.Contains(Path.Combine(soundsDir, "three.ogg")),
            "An existing root cue wins its basename once, without an optional reserve overwrite.");

        // Copy-only upgrades can leave all nineteen legacy files in reserve.
        // The current root layout must still select exactly one path per cue.
        foreach (var cue in OptionalCues) AddExpansionFile(Path.Combine(soundsDir, cue.Key + ".ogg"));
        string[] promoted = AnnouncerCueCatalog.LoadPaths(soundsDir);
        Assert(OptionalCues.All(cue => promoted.Count(path =>
                Path.GetFileNameWithoutExtension(path) == cue.Key) == 1
                && promoted.Contains(Path.Combine(soundsDir, cue.Key + ".ogg"))),
            "Every promoted root cue wins once even with all legacy reserve copies present.");

        foreach (var cue in OptionalCues.Where(c => c.Cue != ArenaAnnouncerCue.Clutch))
        {
            AnnouncerService.Attach(match);
            before = sounds.PlayRequests.Count;
            match.Cue(cue.Cue);
            Assert(sounds.PlayRequests.Count == before + 1 && sounds.PlayRequests[^1].Clip.Name == cue.Key,
                "Optional typed cue uses its explicit recording: " + cue.Key);
        }
        foreach (var cue in new[] { ArenaAnnouncerCue.RoundWon, ArenaAnnouncerCue.RoundLost })
        {
            AnnouncerService.Attach(match);
            match.Cue(cue);
            Assert(sounds.PlayRequests[^1].Clip.Name == (cue == ArenaAnnouncerCue.RoundWon ? "round_won" : "round_lost")
                    && !loaded.Any(path => excluded.Contains(Path.GetFileNameWithoutExtension(path))),
                "Existing result recordings cannot be replaced by same-name reserve alternates.");
        }
        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        match.Cue((ArenaAnnouncerCue)9999);
        match.Cue(ArenaAnnouncerCue.CancelStatus);
        match.Cue(ArenaAnnouncerCue.Clutch);
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before,
            "Unknown, cancellation and unpaired Clutch cues cannot fall through as result playback.");

        VerifyExpansionQueues(match, sounds);
        VerifyClutchAudio(match, sounds);
        VerifyStatusOutputCancellation(match);
        AnnouncerService.Detach();
        Assert(ArenaAnnouncementDirector.Attached == null,
            "Detach clears the director alongside audio event subscriptions.");
        ArenaMatch.SpaceEvents = true;
    }

    private static void VerifyExpansionQueues(ArenaMatch match, GUISounds sounds)
    {
        AnnouncerService.Attach(match);
        int before = sounds.PlayRequests.Count;
        match.Callout("FIRST BLOOD");
        match.Callout("UNSTOPPABLE");
        match.Callout("DOUBLE KILL");
        Time.realtimeSinceStartup += 2.11f;
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 2 && sounds.PlayRequests[^1].Clip.Name == "double_kill",
            "A queued multi-kill replaces a pending spree and drains before it.");
        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        match.Callout("FIRST BLOOD");
        match.Callout("DOUBLE KILL");
        match.Callout("GODLIKE");
        Time.realtimeSinceStartup += 2.11f;
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 2 && sounds.PlayRequests[^1].Clip.Name == "double_kill",
            "A later spree cannot displace a queued multi-kill.");

        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        match.Callout("FIRST BLOOD");
        match.Cue(ArenaAnnouncerCue.Three);
        match.Cue(ArenaAnnouncerCue.Two);
        Time.realtimeSinceStartup += 2.11f;
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 1,
            "Busy countdown numbers are dropped instead of replaying after their spoken second.");
        match.Cue(ArenaAnnouncerCue.One);
        Assert(sounds.PlayRequests.Count == before + 2 && sounds.PlayRequests[^1].Clip.Name == "one",
            "Only the newly reported current countdown number may play after a busy channel clears.");

        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        foreach (var cue in new[] { ArenaAnnouncerCue.Three, ArenaAnnouncerCue.Two, ArenaAnnouncerCue.One })
        {
            match.Cue(cue);
            Time.realtimeSinceStartup += 1;
        }
        Assert(sounds.PlayRequests.Skip(before).Select(play => play.Clip.Name)
                .SequenceEqual(new[] { "three", "two", "one" }),
            "Pack-length countdown clips fit each actual second without an inherited combat-callout gap.");

        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        match.Callout("FIRST BLOOD");
        match.Cue(ArenaAnnouncerCue.OneMinuteRemaining);
        Time.realtimeSinceStartup += 2.11f;
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 1,
            "A status delayed beyond two seconds expires without consuming the result slot.");
        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        match.Cue(ArenaAnnouncerCue.RoundStart);
        match.Cue(ArenaAnnouncerCue.TakenTheLead);
        match.Cue(ArenaAnnouncerCue.LostTheLead);
        match.Cue(ArenaAnnouncerCue.TiedForTheLead);
        Time.realtimeSinceStartup += 0.2f;
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 2 && sounds.PlayRequests[^1].Clip.Name == "tied_for_the_lead",
            "Equal-priority status coalesces to the newest phrase in a single bounded slot.");

        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        match.Cue(ArenaAnnouncerCue.RoundStart);
        match.Cue(ArenaAnnouncerCue.TakenTheLead);
        match.Callout("DOUBLE KILL");
        match.Cue(ArenaAnnouncerCue.OneKillRemaining);
        Time.realtimeSinceStartup += 0.2f;
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 2 && sounds.PlayRequests[^1].Clip.Name == "double_kill",
            "Combat awards supersede pending status, and later status cannot displace the award.");

        foreach (var cancel in new[] { ArenaAnnouncerCue.CancelStatus, ArenaAnnouncerCue.RoundStart,
            ArenaAnnouncerCue.RoundWon, ArenaAnnouncerCue.MatchLost, ArenaAnnouncerCue.Draw })
        {
            AnnouncerService.Attach(match);
            before = sounds.PlayRequests.Count;
            match.Cue(ArenaAnnouncerCue.RoundStart);
            match.Cue(ArenaAnnouncerCue.TakenTheLead);
            match.Cue(cancel);
            Time.realtimeSinceStartup += 0.2f;
            AnnouncerService.Tick();
            Assert(!sounds.PlayRequests.Skip(before).Any(play => play.Clip.Name == "taken_the_lead"),
                "Pending status is canceled by its lifecycle boundary: " + cancel);
        }

        AnnouncerService.Attach(match);
        before = sounds.PlayRequests.Count;
        match.Cue(ArenaAnnouncerCue.RoundStart);
        match.Cue(ArenaAnnouncerCue.FinalRound);
        ArenaAnnouncementDirector.OnTick = () => match.Cue(ArenaAnnouncerCue.CancelStatus);
        Time.realtimeSinceStartup += 0.2f;
        AnnouncerService.Tick();
        Assert(sounds.PlayRequests.Count == before + 1,
            "Director invalidation runs before the audio queue can release stale status.");
        ArenaAnnouncementDirector.OnTick = null;

        foreach (int boundary in new[] { 0, 1, 2, 3 })
        {
            AnnouncerService.Attach(match);
            before = sounds.PlayRequests.Count;
            match.Cue(ArenaAnnouncerCue.RoundStart);
            match.Cue(ArenaAnnouncerCue.TakenTheLead);
            if (boundary == 0) Settings.AnnouncerEnabled.Value = false;
            if (boundary == 1) Settings.AnnouncerVolume.Value = 0;
            if (boundary == 2) AudioListener.pause = true;
            if (boundary == 3) AnnouncerService.Attach(new ArenaMatch());
            AnnouncerService.Tick();
            Settings.AnnouncerEnabled.Value = true;
            Settings.AnnouncerVolume.Value = 0.37f;
            AudioListener.pause = false;
            Time.realtimeSinceStartup += 0.2f;
            AnnouncerService.Tick();
            Assert(sounds.PlayRequests.Count == before + 1,
                "Status never replays after mute, zero-volume, listener pause or match replacement: " + boundary);
        }
    }

    private static void VerifyClutchAudio(ArenaMatch match, GUISounds sounds)
    {
        AnnouncerService.Attach(match);
        int before = sounds.PlayRequests.Count;
        AudioClip.NextCreatedGetDataResult = false;
        match.Cue(ArenaAnnouncerCue.Clutch);
        match.Cue(ArenaAnnouncerCue.RoundWon);
        Assert(sounds.PlayRequests.Count == before + 1 && sounds.PlayRequests[^1].Clip.Name == "round_won",
            "A failed optional Clutch reconstruction falls back to the actual win audio.");

        AnnouncerService.Attach(match);
        var events = new List<ArenaAnnouncerCue>();
        void Observe(ArenaAnnouncerCue cue) => events.Add(cue);
        match.OnAnnouncerCue += Observe;
        try
        {
            before = sounds.PlayRequests.Count;
            match.Callout("FIRST BLOOD");
            match.Cue(ArenaAnnouncerCue.Clutch);
            match.Cue(ArenaAnnouncerCue.MatchWon);
            Assert(sounds.PlayRequests.Count == before + 1
                    && events.SequenceEqual(new[] { ArenaAnnouncerCue.Clutch, ArenaAnnouncerCue.MatchWon }),
                "Clutch preserves the original MatchWon event for lighting and waits behind spoken combat audio.");
            Time.realtimeSinceStartup += 2.11f;
            AnnouncerService.Tick();
            Assert(sounds.PlayRequests.Count == before + 2 && sounds.PlayRequests[^1].Clip.Name == "clutch",
                "An immediately paired Clutch wins the result audio slot exactly once.");
            Time.realtimeSinceStartup += 3;
            AnnouncerService.Tick();
            Assert(sounds.PlayRequests.Count == before + 2,
                "The ordinary win audio cannot replay after a successful Clutch replacement.");
        }
        finally { match.OnAnnouncerCue -= Observe; }

        foreach (int invalid in new[] { 0, 1, 2, 3, 4 })
        {
            AnnouncerService.Attach(match);
            before = sounds.PlayRequests.Count;
            match.Cue(ArenaAnnouncerCue.Clutch);
            if (invalid == 0) AnnouncerService.Tick();
            if (invalid == 1) Time.frameCount++;
            if (invalid == 2) match.Cue((ArenaAnnouncerCue)9999);
            if (invalid == 3) match.Callout("");
            match.Cue(invalid == 4 ? ArenaAnnouncerCue.RoundLost : ArenaAnnouncerCue.RoundWon);
            Assert(sounds.PlayRequests.Count == before + 1
                    && sounds.PlayRequests[^1].Clip.Name == (invalid == 4 ? "round_lost" : "round_won"),
                "An unpaired or losing outcome cannot inherit a Clutch audio hint: " + invalid);
        }
    }

    private static void VerifyStatusOutputCancellation(ArenaMatch match)
    {
        Settings.AnnouncerUseUiVolume.Value = false;
        try
        {
            foreach (var cue in new[] { ArenaAnnouncerCue.Three, ArenaAnnouncerCue.PrepareToFight })
            {
                AnnouncerService.Attach(match);
                match.Cue(cue);
                var sounds = Singleton<GUISounds>.Instance;
                var owned = sounds.gameObject.LastAudioSource;
                int sharedStops = sounds.SingleSource.StopCalls;
                Assert(owned != null && owned.isPlaying, "Fresh status/countdown reaches the owned output.");
                match.Cue(ArenaAnnouncerCue.CancelStatus);
                Assert(owned == null && sounds.SingleSource.StopCalls == sharedStops,
                    "Readiness loss cancels playing owned countdown/status without touching shared GUI audio.");
            }
            foreach (int boundary in new[] { 0, 1, 2 })
            {
                AnnouncerService.Attach(match);
                match.Cue(ArenaAnnouncerCue.Three);
                var owned = Singleton<GUISounds>.Instance.gameObject.LastAudioSource;
                if (boundary == 0) Settings.AnnouncerVolume.Value = 0;
                if (boundary == 1) Settings.AnnouncerEnabled.Value = false;
                if (boundary == 2) AudioListener.pause = true;
                AnnouncerService.Tick();
                Settings.AnnouncerEnabled.Value = true;
                Settings.AnnouncerVolume.Value = 0.37f;
                AudioListener.pause = false;
                AnnouncerService.Tick();
                Assert(owned == null && Singleton<GUISounds>.Instance.gameObject.LastAudioSource == null,
                    "Canceled owned countdown cannot revive after mute, zero volume or listener pause: " + boundary);
            }
        }
        finally { AnnouncerService.Detach(); Settings.AnnouncerUseUiVolume.Value = true; }
    }

    private static void AddExpansionFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "controlled optional audio request");
        CreatedFiles.Add(path);
    }
}
