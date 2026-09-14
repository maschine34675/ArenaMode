using System;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;

namespace ArenaMode.Audio;

/// <summary>Reads current mode progress only. Never advances rules or holds native actors.</summary>
internal static class ArenaAnnouncementDirector
{
    private static ArenaMatch _match;
    private static readonly ArenaAnnouncementPolicy Progress = new();
    private static bool _invalidated;
    private static bool _reportedFailure;

    internal static void Attach(ArenaMatch match)
    {
        _match = match;
        Progress.Reset();
        _invalidated = _reportedFailure = false;
    }

    internal static void Detach()
    {
        _match = null;
        Progress.Reset();
        _invalidated = _reportedFailure = false;
    }

    internal static void Tick()
    {
        var match = _match;
        if (match == null) return;
        try
        {
            var game = Singleton<AbstractGame>.Instance as LocalGame;
            var world = Singleton<GameWorld>.Instance;
            if (!ReferenceEquals(match, ArenaMatchService.Current)
                || game == null || game.Status != GameStatus.Started
                || !ArenaSession.IsExactActiveRaid(game, world))
            {
                CancelStatus(match);
                return;
            }
            var snapshot = ReadSnapshot(match.Mode);
            if (!snapshot.Active) { CancelStatus(match); return; }
            _invalidated = false;
            Progress.Update(snapshot, cue => match.ReportAnnouncerCue(cue, beginAwardRound: false));
        }
        catch (Exception ex)
        {
            CancelStatus(match);
            if (_reportedFailure) return;
            _reportedFailure = true;
            Plugin.ModLogger.LogWarning($"Arena announcement progress unavailable: {ex.Message}");
        }
    }

    internal static ArenaAnnouncementSnapshot ReadSnapshot(IArenaMode mode) => mode switch
    {
        RoundsMode deathmatch => deathmatch.AnnouncementSnapshot(),
        TeamDeathmatchMode teams => teams.AnnouncementSnapshot(),
        TacticalMode tactical => tactical.AnnouncementSnapshot(),
        GunGameMode gunGame => gunGame.AnnouncementSnapshot(),
        _ => default,
    };

    private static void CancelStatus(ArenaMatch match)
    {
        if (_invalidated) return;
        _invalidated = true;
        // Do not reset logical progress on a transient world/owner transition.
        // A real detach/attach resets it; respawning must not repeat milestones.
        match.ReportAnnouncerCue(ArenaAnnouncerCue.CancelStatus, beginAwardRound: false);
    }
}
