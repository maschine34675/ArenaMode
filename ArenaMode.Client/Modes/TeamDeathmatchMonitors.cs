using System;
using ArenaMode.Compat;
using ArenaMode.Effects;

namespace ArenaMode.Modes;

/// <summary>Pure faction/clock projection: BEAR is always left, USEC right.</summary>
internal static class TeamDeathmatchMonitors
{
    internal static ArenaMapPortScoreMonitors.Frame FrameFor(bool localUsec, int teamSize,
        TdmMatchPhase phase, int timeLimitSeconds, float startedAt, float countdownEndsAt, float playingElapsed,
        int friendlyKills, int opponentKills, int friendlyAlive, int opponentAlive)
    {
        int mode;
        float seconds;
        switch (phase)
        {
            case TdmMatchPhase.Assembling:
                mode = timeLimitSeconds > 0 ? 1 : 0;
                seconds = timeLimitSeconds > 0 ? timeLimitSeconds : 0;
                break;
            case TdmMatchPhase.Countdown:
                mode = 2;
                seconds = countdownEndsAt;
                break;
            case TdmMatchPhase.Live:
                mode = timeLimitSeconds > 0 ? 2 : 3;
                seconds = timeLimitSeconds > 0 ? startedAt + timeLimitSeconds : startedAt;
                break;
            case TdmMatchPhase.Finished:
                mode = 1;
                seconds = timeLimitSeconds > 0 ? Math.Max(0f, timeLimitSeconds - playingElapsed) : playingElapsed;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase));
        }
        return new ArenaMapPortScoreMonitors.Frame(ArenaTeamPalette.BearRgb, ArenaTeamPalette.UsecRgb, teamSize,
            localUsec ? opponentKills : friendlyKills, localUsec ? friendlyKills : opponentKills,
            localUsec ? opponentAlive : friendlyAlive, localUsec ? friendlyAlive : opponentAlive, mode, seconds);
    }
}
