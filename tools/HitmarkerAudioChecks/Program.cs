using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT.UI;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string why)
    { if (!value) throw new InvalidOperationException(why); _checks++; }
    private static void Step(float seconds = 0.05f)
    { Time.realtimeSinceStartup += seconds; HitmarkerAudioService.Tick(); }

    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string pluginDirectory = Path.Combine(root, "ArenaMode.Client", "Assets");
        string audioDirectory = Path.Combine(pluginDirectory, "ArenaMode", "hitmarkers");
        string scratch = Path.Combine(Path.GetTempPath(), "ArenaHitmarker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (string name in new[] { "hit", "headshot", "kill" })
            {
                var pcm = HitmarkerPcmClip.Load(Path.Combine(audioDirectory, name + ".wav"));
                var first = pcm.GetRuntime();
                Check(first.channels == 1 && first.frequency == 44100 && first.length > 0f, "Bundled PCM metadata");
                Check(first.Data.Any(v => Math.Abs(v) > 0.1f), "Bundled PCM signal");
                Check(ReferenceEquals(pcm.GetRuntime(), first), "Healthy clip reused");
                first.SimulateAudioReset();
                var rebuilt = pcm.GetRuntime();
                Check(first == null && !ReferenceEquals(first, rebuilt) && rebuilt.samples > 0, "Device reset reconstructs PCM");
                Check(rebuilt.Data.Any(v => Math.Abs(v) > 0.1f), "Reconstructed clip contains signal");
                pcm.ResetRuntime();
                Check(rebuilt == null, "Explicit PCM cleanup");
            }
            byte[] valid = File.ReadAllBytes(Path.Combine(audioDirectory, "kill.wav"));
            void Reject(string name, Action<byte[]> mutate)
            {
                byte[] bytes = valid.ToArray(); mutate(bytes);
                string file = Path.Combine(scratch, name + ".wav"); File.WriteAllBytes(file, bytes);
                bool rejected = false;
                try { HitmarkerPcmClip.Load(file); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Malformed PCM rejected: " + name);
            }
            Reject("riff", b => b[0] = 0);
            Reject("truncated", b => b[4] = 0);
            Reject("stereo", b => b[22] = 2);
            Reject("float", b => b[20] = 3);
            Reject("rate", b => b[24] = 0);
            Reject("bits", b => b[34] = 8);
            Reject("oversize-chunk", b => b[43] = 127);
            Reject("silence", b => Array.Clear(b, 44, b.Length - 44));

            var host = new GUISounds(); Singleton<GUISounds>.Instance = host;
            HitmarkerAudioService.SetPluginDirectory(pluginDirectory);
            var match = new ArenaMatch(); ArenaMatchService.Current = match;
            HitmarkerAudioService.Attach(match);
            Check(ArenaEvents.Subscribers == 0, "Ordinary raid stays unsubscribed");
            ArenaSession.IsActive = true;
            HitmarkerAudioService.Attach(match);
            Check(ArenaEvents.Subscribers == 1, "Attach independently of visual HUD");
            HitmarkerAudioService.Attach(match);
            Check(ArenaEvents.Subscribers == 1, "Repeated attach does not duplicate subscription");

            ArenaEvents.Damage(eligible: false); Step();
            Check(host.gameObject.LastAudioSource == null, "Ineligible native contact silent");
            Settings.HitmarkerSounds.Value = false; ArenaEvents.Damage(); Step();
            Check(host.gameObject.LastAudioSource == null, "Disabled event silent");
            Settings.HitmarkerSounds.Value = true;
            Settings.HitmarkerVolume.Value = float.NaN; ArenaEvents.Damage(); Step();
            Check(host.gameObject.LastAudioSource == null, "Invalid gain silent");
            Settings.HitmarkerVolume.Value = 0.5f;

            // Hold an independent announcement while submitting a pellet burst.
            var phrase = AudioClip.Create("announcer", 1000, 1, 44100, false);
            Check(ArenaAnnouncerOutput.TryPlay(phrase, 0.8f, out _), "Announcer starts");
            var announcer = host.gameObject.LastAudioSource;
            int stops = announcer.StopCalls;
            ArenaEvents.Damage(); ArenaEvents.Damage(head: true); ArenaEvents.Damage(kill: true);
            Step(0.02f);
            Check(ReferenceEquals(host.gameObject.LastAudioSource, announcer), "Collect window waits for clustered impacts");
            Step(0.03f);
            var impact = host.gameObject.LastAudioSource;
            Check(!ReferenceEquals(impact, announcer) && impact.clip.name.EndsWith("kill"), "Kill wins coalesced burst on separate source");
            Check(impact.PlayCalls == 1 && announcer.StopCalls == stops, "One impact cannot cut off phrase");
            Check(impact.outputAudioMixerGroup.name == "Master", "Exact Master output");
            Check(!impact.ignoreListenerVolume && !impact.ignoreListenerPause && impact.spatialBlend == 0, "2D output retains listener mute and pause");
            Check(host.SingleSource.StopCalls == 0 && host.PlayRequests.Count == 0, "Native button channel untouched");

            ArenaEvents.Damage(head: true); Step();
            Check(impact.clip.name.EndsWith("headshot") && impact.PlayCalls == 2, "Head-only burst gets head cue");
            ArenaEvents.Damage(); Step();
            Check(impact.clip.name.EndsWith("hit") && impact.PlayCalls == 3, "Ordinary hit cue");
            Settings.HitmarkerVolume.Value = 0.2f; Step();
            Check(Math.Abs(impact.volume - 0.2f) < 0.001f && announcer.volume == 0.8f, "Live gain changes affect only impact channel");
            impact.clip.SimulateAudioReset(); ArenaEvents.Damage(); Step();
            Check(impact.clip.samples > 0 && impact.clip.Data.Any(v => Math.Abs(v) > 0.1f), "Service recovers invalid native clip");

            int plays = impact.PlayCalls;
            ArenaEvents.Damage(kill: true); Step(0.25f); Step();
            Check(impact.PlayCalls == plays, "Frame stall drops stale cue without replay");
            ArenaEvents.Damage(); AudioListener.pause = true; Step(); AudioListener.pause = false; Step();
            Check(impact == null && impact.PlayCalls == plays, "Pause cancels pending cue and active tail without replay");
            Check(announcer != null, "Impact pause cleanup leaves announcer alone");
            ArenaEvents.Damage(); Step(); impact = host.gameObject.LastAudioSource;
            ArenaEvents.Damage(); Settings.HitmarkerVolume.Value = 0; Step();
            Check(impact == null && announcer != null, "Mute destroys only owned impact source");
            Settings.HitmarkerVolume.Value = 0.5f; Step();
            Check(host.gameObject.LastAudioSource == null, "Unmute does not replay canceled cue");

            ArenaEvents.Damage(kill: true); Step(); impact = host.gameObject.LastAudioSource;
            var clip = impact.clip;
            ArenaMatchService.Current = new ArenaMatch(); Step();
            Check(ArenaEvents.Subscribers == 0 && impact == null && clip == null, "Match replacement cancels and cleans all owned audio");
            HitmarkerAudioService.Attach(ArenaMatchService.Current);
            ArenaEvents.Damage(); ArenaSession.IsActive = false; Step();
            Check(ArenaEvents.Subscribers == 0, "Ordinary session drift detaches");
            ArenaSession.IsActive = true;
            HitmarkerAudioService.Attach(ArenaMatchService.Current);
            ArenaEvents.Damage(); HitmarkerAudioService.Detach(); Step();
            Check(ArenaEvents.Subscribers == 0 && announcer != null, "Detach drops queued impacts but preserves announcement");

            // Missing assets fail as presentation only and recover next match.
            HitmarkerAudioService.SetPluginDirectory(scratch);
            HitmarkerAudioService.Attach(ArenaMatchService.Current);
            for (int i = 0; i < 50; i++) { ArenaEvents.Damage(); Step(); }
            Check(ArenaEvents.Subscribers == 1, "Missing files do not block match attachment");
            Check(ArenaMode.Plugin.ModLogger.Messages.Count(m => m.Contains("missing_hit,")) == 1, "Repeated missing cue diagnostics bounded");
            HitmarkerAudioService.SetPluginDirectory(pluginDirectory);
            HitmarkerAudioService.Attach(ArenaMatchService.Current); ArenaEvents.Damage(); Step();
            Check(host.gameObject.LastAudioSource.clip.samples > 0, "Repaired assets recover next match");
            Console.WriteLine($"HITMARKER_PRODUCTION_CHECKS_PASSED ({_checks} assertions)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            HitmarkerAudioService.Detach(); ArenaAnnouncerOutput.Reset();
            foreach (string file in Directory.GetFiles(scratch)) File.Delete(file);
            Directory.Delete(scratch);
        }
    }
}
