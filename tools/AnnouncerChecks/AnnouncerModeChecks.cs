using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT.UI;
using UnityEngine;

internal static partial class Program
{
    /// <summary>
    /// Item 2 of the 2026-10-06 follow-up ("announcer mutator"): proves
    /// AnnouncerService's real read sites (the attach-time log line and
    /// SubmitCallout) honor a PER-MODE gate, not only the pre-existing master
    /// <c>Settings.AnnouncerEnabled</c> — AND, since the ANN-1 fix, that the
    /// per-mode half comes from the FROZEN <see cref="ArenaSession.
    /// AnnouncerEnabledSelection"/>, never a live per-mode read keyed on the
    /// attached match's own <c>Mode.Id</c>. The real per-mode default table
    /// and dispatch (<c>Config.Settings.AnnouncerEntry</c>,
    /// <c>Modes.ArenaAnnouncerGate</c>, <c>ArenaSession.MarkArmed</c>'s own
    /// capture) live in production and are covered by ArenaMode.Tests and
    /// tools/TdmMatchMenuChecks instead: this harness only links
    /// <c>AnnouncerService.cs</c> itself, so it sets
    /// <c>ArenaSession.AnnouncerEnabledSelection</c> directly before each
    /// Attach, simulating the one capture <c>MarkArmed</c> would have made —
    /// enough to prove the WIRING at every read site without duplicating the
    /// real table here.
    /// </summary>
    private static void VerifyPerModeGate()
    {
        var sounds = Singleton<GUISounds>.Instance;
        var healthy = sounds.PlayRequests.Last(play => play.Clip != null
            && play.Clip.loadState == AudioDataLoadState.Loaded).Clip;
        var label = healthy.Name.Replace('_', ' ').ToUpperInvariant();
        var messages = ArenaMode.Plugin.ModLogger.Messages;

        // Simulates arming Zombie: production's own default for this mode is
        // OFF (ArenaAnnouncerGate.DefaultEnabled), captured once into
        // AnnouncerEnabledSelection by MarkArmed before the raid — and hence
        // before Attach, which only ever runs after the raid has started.
        ArenaSession.AnnouncerEnabledSelection = false;
        var zombieMatch = new ArenaMatch { Mode = new AnnouncerModeStub { Id = "zombie" } };
        int start = messages.Count;
        AnnouncerService.Attach(zombieMatch);
        Assert(messages.Skip(start).Any(message => message.StartsWith("Arena announcer attached:")
            && message.Contains("mode=zombie") && message.Contains("enabled=False")),
            "Attach reports the frozen per-mode gate (Zombie's own default), not just the master switch.");

        int before = sounds.PlayRequests.Count;
        zombieMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before,
            "Zombie's frozen-off per-mode gate silences a callout even with the master switch on.");

        // Simulates arming a non-Zombie mode instead: production defaults it on.
        ArenaSession.AnnouncerEnabledSelection = true;
        var teamMatch = new ArenaMatch { Mode = new AnnouncerModeStub { Id = "teamdeathmatch" } };
        AnnouncerService.Attach(teamMatch);
        teamMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before + 1,
            "A non-Zombie mode still plays with the exact same master switch left on.");
        Assert(AnnouncerService.IsPmcVoiceBlocked, "An audible announcer cue takes priority over PMC chatter.");
        float previousVolume = Settings.AnnouncerVolume.Value;
        Settings.AnnouncerVolume.Value = 0f;
        Assert(!AnnouncerService.IsPmcVoiceBlocked, "Muted announcer cannot starve PMC chatter.");
        Settings.AnnouncerVolume.Value = previousVolume;

        Settings.AnnouncerEnabled.Value = false;
        Assert(!AnnouncerService.IsPmcVoiceBlocked, "Disabled announcer cannot starve PMC chatter.");
        teamMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before + 1,
            "The master switch still gates a mode whose own frozen per-mode value is on.");
        Settings.AnnouncerEnabled.Value = true;

        // ANN-1 itself (caught in review the same day this follow-up landed):
        // the per-mode half must be the value frozen at arm, NEVER derived
        // live from the attached match's own Mode.Id. Attaching a "zombie"
        // match whose frozen selection was (hypothetically) armed true must
        // play despite Zombie's usual default, and a "teamdeathmatch" match
        // armed false must stay silent despite its usual default — the exact
        // opposite of what a live Mode.Id-keyed lookup would produce. The
        // buggy pre-fix AnnouncerService (calling Settings.IsAnnouncerEnabledFor
        // (match.Mode?.Id) fresh on every read) fails both assertions below:
        // it mutes the "armed true" Zombie match and plays the "armed false"
        // Team Deathmatch match, exactly backwards.
        ArenaSession.AnnouncerEnabledSelection = true;
        var armedOnZombieMatch = new ArenaMatch { Mode = new AnnouncerModeStub { Id = "zombie" } };
        AnnouncerService.Attach(armedOnZombieMatch);
        before = sounds.PlayRequests.Count;
        armedOnZombieMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before + 1,
            "ANN-1: a Zombie match armed with the per-mode switch on must play, "
            + "even though Zombie's own live default is off.");

        ArenaSession.AnnouncerEnabledSelection = false;
        var armedOffTeamMatch = new ArenaMatch { Mode = new AnnouncerModeStub { Id = "teamdeathmatch" } };
        AnnouncerService.Attach(armedOffTeamMatch);
        before = sounds.PlayRequests.Count;
        armedOffTeamMatch.Callout(label);
        Assert(sounds.PlayRequests.Count == before,
            "ANN-1: a Team Deathmatch match armed with the per-mode switch off must stay silent, "
            + "even though that mode's own live default is on.");

        ArenaSession.AnnouncerEnabledSelection = true;
        AnnouncerService.Detach();
        Assert(!AnnouncerService.IsPmcVoiceBlocked, "Detached announcer cannot hold speech in another raid.");
        AnnouncerService.Attach(new ArenaMatch());
    }
}
