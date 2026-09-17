using ArenaMode.Modes;
using System;
using System.Collections.Generic;
using System.IO;

namespace ArenaMode.Audio;

internal enum AnnouncerCueKind { None, Start, Result, Award, Status, Countdown, Clutch }

/// <summary>Explicit runtime mapping, independent of the pack's original intake classification.</summary>
internal static class AnnouncerCueCatalog
{
    // Never recursively scan reserve: its alternate round_won/round_lost share
    // basenames with the established result recordings, and CTF is unsupported.
    internal static readonly string[] OptionalReserveKeys =
    {
        "prepare_to_fight", "three", "two", "one", "taken_the_lead", "lost_the_lead",
        "tied_for_the_lead", "one_minute_remaining", "thirty_seconds_remaining",
        "ten_seconds_remaining", "five_kills_remaining", "one_kill_remaining",
        "match_point", "final_round", "final_weapon", "ladder_complete", "draw", "shutdown", "clutch",
    };

    internal static string[] LoadPaths(string soundsDirectory)
    {
        var files = new List<string>(Directory.GetFiles(soundsDirectory, "*.ogg"));
        var rootKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files) rootKeys.Add(Path.GetFileNameWithoutExtension(file));
        foreach (string key in OptionalReserveKeys)
        {
            string path = Path.Combine(soundsDirectory, "reserve", key + ".ogg");
            if (!rootKeys.Contains(key) && File.Exists(path)) files.Add(path);
        }
        return files.ToArray();
    }

    internal static AnnouncerCueKind Classify(ArenaAnnouncerCue cue, out string key)
    {
        key = cue switch
        {
            ArenaAnnouncerCue.RoundStart => "round_start",
            ArenaAnnouncerCue.RoundWon => "round_won",
            ArenaAnnouncerCue.RoundLost => "round_lost",
            ArenaAnnouncerCue.LastManStanding => "last_man_standing",
            ArenaAnnouncerCue.MatchWon => "match_won",
            ArenaAnnouncerCue.MatchLost => "match_lost",
            ArenaAnnouncerCue.PrepareToFight => "prepare_to_fight",
            ArenaAnnouncerCue.Three => "three",
            ArenaAnnouncerCue.Two => "two",
            ArenaAnnouncerCue.One => "one",
            ArenaAnnouncerCue.TakenTheLead => "taken_the_lead",
            ArenaAnnouncerCue.LostTheLead => "lost_the_lead",
            ArenaAnnouncerCue.TiedForTheLead => "tied_for_the_lead",
            ArenaAnnouncerCue.OneMinuteRemaining => "one_minute_remaining",
            ArenaAnnouncerCue.ThirtySecondsRemaining => "thirty_seconds_remaining",
            ArenaAnnouncerCue.TenSecondsRemaining => "ten_seconds_remaining",
            ArenaAnnouncerCue.FiveKillsRemaining => "five_kills_remaining",
            ArenaAnnouncerCue.OneKillRemaining => "one_kill_remaining",
            ArenaAnnouncerCue.MatchPoint => "match_point",
            ArenaAnnouncerCue.FinalRound => "final_round",
            ArenaAnnouncerCue.FinalWeapon => "final_weapon",
            ArenaAnnouncerCue.LadderComplete => "ladder_complete",
            ArenaAnnouncerCue.Draw => "draw",
            ArenaAnnouncerCue.Shutdown => "shutdown",
            ArenaAnnouncerCue.Clutch => "clutch",
            ArenaAnnouncerCue.NoContest => "no_contest",
            ArenaAnnouncerCue.Embarrassing => "embarrassing",
            _ => null,
        };
        if (key == null) return AnnouncerCueKind.None;
        if (cue == ArenaAnnouncerCue.RoundStart) return AnnouncerCueKind.Start;
        if (cue == ArenaAnnouncerCue.RoundWon || cue == ArenaAnnouncerCue.RoundLost
            || cue == ArenaAnnouncerCue.MatchWon || cue == ArenaAnnouncerCue.MatchLost
            || cue == ArenaAnnouncerCue.Draw || cue == ArenaAnnouncerCue.NoContest) return AnnouncerCueKind.Result;
        if (cue == ArenaAnnouncerCue.Clutch) return AnnouncerCueKind.Clutch;
        if (cue == ArenaAnnouncerCue.LastManStanding || cue == ArenaAnnouncerCue.Shutdown
            || cue == ArenaAnnouncerCue.Embarrassing)
            return AnnouncerCueKind.Award;
        return IsCountdown(key) ? AnnouncerCueKind.Countdown : AnnouncerCueKind.Status;
    }

    internal static bool IsCountdown(string key) => key == "three" || key == "two" || key == "one";

    internal static bool IsStatus(string key) => key switch
    {
        "prepare_to_fight" or "taken_the_lead" or "lost_the_lead" or "tied_for_the_lead"
            or "one_minute_remaining" or "thirty_seconds_remaining" or "ten_seconds_remaining"
            or "five_kills_remaining" or "one_kill_remaining" or "match_point" or "final_round"
            or "final_weapon" or "ladder_complete" => true,
        _ => false,
    };
}
