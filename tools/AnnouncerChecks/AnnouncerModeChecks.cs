using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using Comfort.Common;
using EFT.UI;
using UnityEngine;

internal static partial class Program
{
    /// <summary>
    /// Item 2 of the 2026-10-06 follow-up ("announcer mutator"): proves
    /// AnnouncerService's real read sites (the attach-time log line and
    /// SubmitCallout) honor a PER-MODE gate, not only the pre-existing master
    /// <c>Settings.AnnouncerEnabled</c>. The real per-mode default table and
    /// dispatch (<c>Config.Settings.AnnouncerEntry</c>,
    /// <c>Modes.ArenaAnnouncerGate</c>) live in production and are covered by
    /// ArenaMode.Tests and tools/TdmMatchMenuChecks instead: this harness only
    /// links <c>AnnouncerService.cs</c> itself, so StandIns.cs's fake
    /// <c>Settings.IsAnnouncerEnabledFor</c> hardcodes "zombie" as the one
    /// muted id — enough to prove the WIRING at every read site without
    /// duplicating the real table here.
    /// </summary>
    private static void VerifyPerModeGate()
    {
        var sounds = Singleton<GUISounds>.Instance;
        var healthy = sounds.PlayRequests.Last(play => play.Clip != null
            && play.Clip.loadState == AudioDataLoadState.Loaded).Clip;
        var label = healthy.Name.Replace('_', ' ').ToUpperInvariant();
        var messages = ArenaMode.Plugin.ModLogger.Messages;

        var zombieMatch = new ArenaMatch { Mode = new AnnouncerModeStub { Id = "zombie" } };
        int start = messages.Count;
        AnnouncerService.Attach(zombieMatch);
        Assert(messages.Skip(start).Any(message => message.StartsWith("Arena announcer attached:")
            && message.Contains("mode=zombie") && message.Contains("enabled=False")),
            "Attach reports the per-mode gate (Zombie's own default), not just the master switch.");

        int before = sounds.PlayRequests.Count;
        zombieMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before,
            "Zombie's default-off per-mode gate silences a callout even with the master switch on.");

        var teamMatch = new ArenaMatch { Mode = new AnnouncerModeStub { Id = "teamdeathmatch" } };
        AnnouncerService.Attach(teamMatch);
        teamMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before + 1,
            "A non-Zombie mode still plays with the exact same master switch left on.");

        Settings.AnnouncerEnabled.Value = false;
        teamMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before + 1,
            "The master switch still gates a mode whose own per-mode default is on.");
        Settings.AnnouncerEnabled.Value = true;

        AnnouncerService.Attach(new ArenaMatch());
    }
}
