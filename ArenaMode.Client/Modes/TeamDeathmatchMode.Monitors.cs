using System;
using ArenaMode.Compat;
using EFT;

namespace ArenaMode.Modes;

public sealed partial class TeamDeathmatchMode
{
    private ArenaMapPortScoreMonitors.Session _monitors;
    private int _monitorScoreVersion = -1;
    private int _monitorFriendlyScore, _monitorOpponentScore;

    // ArenaMatchService stops ticking gameplay as soon as native Stop owns
    // the raid. Presentation cleanup still runs before that early return.
    internal void OnNativePresentationStop()
    {
        ReleaseMonitors();
        ReleaseDomination();
    }

    private void ClaimMonitors()
    {
        try
        {
            ReleaseMonitors();
            if (!CanPresentMonitors()) return;
            // MapPort deduplicates equal owner strings and truncates them at 40
            // characters. Each match gets a distinct 39-character owner.
            _monitors = ArenaMapPortScoreMonitors.TryClaim("AM TDM " + Guid.NewGuid().ToString("N"));
            _monitorScoreVersion = -1;
            PushMonitors();
        }
        catch (Exception ex)
        {
            ReleaseMonitors();
            Plugin.ModLogger.LogWarning((IsDomination ? "Domination" : "Team Deathmatch")
                + " score monitors unavailable: " + ex.GetBaseException().Message);
        }
    }

    private bool CanPresentMonitors()
        => !_ended && !_nativeStopObserved && _failure == null && MatchRulesReady && _population != null
           && IsCurrentRaid() && ReferenceEquals(ArenaMatchService.Current, _match)
           && ReferenceEquals(_match?.Mode, this);

    private void PushMonitors()
    {
        var monitors = _monitors;
        if (monitors == null) return;
        try
        {
            // IsCurrentMatch excludes terminal results; the exact raid is
            // still ours throughout their 15-second display hold.
            if (!CanPresentMonitors()) { ReleaseMonitors(); return; }
            if (HasResult)
            {
                _monitorFriendlyScore = FinalFriendlyScore;
                _monitorOpponentScore = FinalOpponentScore;
            }
            else if (IsDomination)
            {
                // Objective income does not change the kill scoreboard version.
                // These policy counters are constant-time reads; the monitor
                // session still suppresses unchanged score frames.
                _monitorFriendlyScore = FriendlyTeamScore;
                _monitorOpponentScore = OpponentTeamScore;
            }
            else if (_monitorScoreVersion != _match.Scoreboard.Version)
            {
                _monitorFriendlyScore = FriendlyTeamScore;
                _monitorOpponentScore = OpponentTeamScore;
                _monitorScoreVersion = _match.Scoreboard.Version;
            }
            bool localUsec = _population.LocalUsec;
            int friendlyAlive = CountMonitorBots(localUsec);
            if (_local != null && !_local.IsAI && _local.ProfileId == _localId
                && ReferenceEquals(_world.MainPlayer, _local) && _local.HealthController?.IsAlive == true)
                friendlyAlive++;
            var frame = TeamDeathmatchMonitors.FrameFor(localUsec, TeamSize, MatchPhase.Value,
                MatchTimeLimit, IsDomination ? _dominationRules.StartedAt : _rules.StartedAt,
                _gateCountdownEndsAt, PlayingElapsed,
                _monitorFriendlyScore, _monitorOpponentScore, friendlyAlive, CountMonitorBots(!localUsec));
            if (!monitors.Push(frame)) ReleaseMonitors();
        }
        catch (Exception ex)
        {
            ReleaseMonitors();
            Plugin.ModLogger.LogWarning((IsDomination ? "Domination" : "Team Deathmatch")
                + " score monitors stopped: " + ex.GetBaseException().Message);
        }
    }

    private int CountMonitorBots(bool usec)
    {
        int count = 0;
        foreach (var bot in _bots)
        {
            if (!Alive(bot) || bot.BotState != EBotState.Active
                || !_usecById.TryGetValue(bot.ProfileId, out bool side) || side != usec) continue;
            var player = bot.GetPlayer;
            if (player.IsAI && player.ProfileId == bot.ProfileId
                && ReferenceEquals(player.AIData?.BotOwner, bot)
                && ReferenceEquals(_world.GetAlivePlayerByProfileID(bot.ProfileId), player)) count++;
        }
        return count;
    }

    private void ReleaseMonitors()
    {
        var monitors = _monitors;
        _monitors = null;
        monitors?.Release();
    }
}
