using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using UnityEngine;

internal static partial class Program
{
    private static void VerifyPreparationTransitions()
    {
        ArenaMatch.SpaceEvents = false;
        Settings.AnnouncerUseUiVolume.Value = false;
        try
        {
            // Both audio routes, both ordering possibilities between the mode
            // event and director tick, and Gun Game's transition without Start.
            foreach (bool legacy in new[] { false, true })
            foreach (int startOrder in new[] { 0, 1, 2 })
            {
                Settings.AnnouncerUseUiVolume.Value = legacy;
                var match = new ArenaMatch();
                var policy = new ArenaAnnouncementPolicy();
                var snapshot = new ArenaAnnouncementSnapshot { Active = true, Epoch = 1, Preparing = true };
                AnnouncerService.Attach(match);
                policy.Update(snapshot, match.Cue);
                var sounds = Singleton<GUISounds>.Instance;
                var source = legacy ? sounds.SingleSource : sounds.gameObject.LastAudioSource;
                var clip = legacy ? sounds.PlayRequests[^1].Clip : source.clip;
                Assert(source != null && clip.Name == "prepare_to_fight" && clip.length > 1.46f,
                    "Preparation uses the complete 64742-frame production-length fixture.");
                int stops = source.StopCalls;
                Time.realtimeSinceStartup += 0.5f;
                snapshot.Preparing = false;
                snapshot.Live = true;
                if (startOrder == 1) match.Cue(ArenaAnnouncerCue.RoundStart);
                policy.Update(snapshot, match.Cue);
                if (startOrder == 2) match.Cue(ArenaAnnouncerCue.RoundStart);
                AnnouncerService.Tick();
                Assert(source != null && source.StopCalls == stops,
                    "Fast preparation-to-live must not cut the playing PREPARE TO FIGHT recording.");
                Time.realtimeSinceStartup += 0.9f;
                AnnouncerService.Tick();
                Assert(source != null && source.StopCalls == stops,
                    "Preparation still owns its real 1.468-second spoken duration.");
                Time.realtimeSinceStartup += 0.2f;
                AnnouncerService.Tick();
                Assert(source.StopCalls == stops + (startOrder == 0 ? 0 : 1),
                    "Only the delayed start voice releases after preparation, independent of event ordering.");
                if (startOrder != 0)
                    Assert((legacy ? sounds.PlayRequests[^1].Clip : source.clip).Name == "round_start",
                        "The queued start keeps its explicit round-start recording.");
                Time.realtimeSinceStartup += 3;
                AnnouncerService.Tick();
                Assert(source.StopCalls == stops + (startOrder == 0 ? 0 : 1), "Start voice cannot replay.");
            }

            Settings.AnnouncerUseUiVolume.Value = false;
            foreach (bool loseReadiness in new[] { false, true })
            {
                var match = new ArenaMatch();
                var policy = new ArenaAnnouncementPolicy();
                var snapshot = new ArenaAnnouncementSnapshot { Active = true, Preparing = true };
                AnnouncerService.Attach(match);
                policy.Update(snapshot, match.Cue);
                var source = Singleton<GUISounds>.Instance.gameObject.LastAudioSource;
                int stops = source.StopCalls;
                Time.realtimeSinceStartup += 0.1f;
                snapshot.Preparing = false;
                snapshot.Countdown = 3;
                policy.Update(snapshot, match.Cue);
                Time.realtimeSinceStartup += 1;
                snapshot.Countdown = 2;
                policy.Update(snapshot, match.Cue);
                AnnouncerService.Tick();
                Assert(source != null && source.StopCalls == stops,
                    "Preparation-to-countdown preserves speech and drops busy digits.");
                if (loseReadiness)
                {
                    snapshot.Preparing = true;
                    snapshot.Countdown = null;
                    policy.Update(snapshot, match.Cue);
                    Assert(source == null, "Lost countdown readiness still cancels preparation immediately.");
                }
                else
                {
                    Time.realtimeSinceStartup += 1;
                    snapshot.Countdown = 1;
                    policy.Update(snapshot, match.Cue);
                    Assert(source.clip.Name == "one" && source.StopCalls == stops + 1,
                        "After preparation only the current countdown digit plays, never stale THREE/TWO.");
                    snapshot.Countdown = null;
                    snapshot.Live = true;
                    policy.Update(snapshot, match.Cue);
                    Assert(source == null, "Live progress still cancels obsolete countdown playback.");
                }
            }

            foreach (string boundary in new[] { "readiness", "epoch", "inactive", "terminal", "pause",
                "disabled", "volume", "detach", "replace", "result", "no-contest" })
            {
                var match = new ArenaMatch();
                var policy = new ArenaAnnouncementPolicy();
                var snapshot = new ArenaAnnouncementSnapshot { Active = true, Preparing = true };
                AnnouncerService.Attach(match);
                policy.Update(snapshot, match.Cue);
                Time.realtimeSinceStartup += 0.2f;
                match.Cue(ArenaAnnouncerCue.RoundStart); // queued behind preparation
                var sounds = Singleton<GUISounds>.Instance;
                var original = sounds.gameObject.LastAudioSource;
                snapshot.Preparing = false;
                if (boundary == "epoch") snapshot.Epoch++;
                if (boundary == "inactive") snapshot.Active = false;
                if (boundary == "terminal") snapshot.Terminal = true;
                if (boundary is "readiness" or "epoch" or "inactive" or "terminal") policy.Update(snapshot, match.Cue);
                if (boundary == "pause") AudioListener.pause = true;
                if (boundary == "disabled") Settings.AnnouncerEnabled.Value = false;
                if (boundary == "volume") Settings.AnnouncerVolume.Value = 0;
                if (boundary == "detach") AnnouncerService.Detach();
                if (boundary == "replace") AnnouncerService.Attach(new ArenaMatch());
                if (boundary == "result") match.Cue(ArenaAnnouncerCue.RoundWon);
                if (boundary == "no-contest") match.Cue(ArenaAnnouncerCue.NoContest);
                AnnouncerService.Tick();
                Assert(original == null, "Invalidation stops owned preparation: " + boundary);
                Settings.AnnouncerEnabled.Value = true;
                Settings.AnnouncerVolume.Value = 0.37f;
                AudioListener.pause = false;
                Time.realtimeSinceStartup += 5;
                AnnouncerService.Tick();
                var remaining = sounds.gameObject.LastAudioSource;
                Assert(remaining == null || remaining.clip.Name is "round_won" or "no_contest",
                    "Canceled preparation cannot release stale start later: " + boundary);
            }
        }
        finally
        {
            AnnouncerService.Detach();
            Settings.AnnouncerUseUiVolume.Value = true;
            ArenaMatch.SpaceEvents = true;
        }
    }
}
