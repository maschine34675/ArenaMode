using System;

namespace ArenaMode.Modes;

internal enum TeamArenaRoundContinuation
{
    RebindNextRound,
    EndMatchWon,
    EndMatchLost,
    EndNoContest,
}

internal readonly struct TeamArenaRoundDecision
{
    public TeamArenaRoundDecision(
        int roundsWon,
        int roundsLost,
        TeamArenaRoundContinuation continuation)
    {
        RoundsWon = roundsWon;
        RoundsLost = roundsLost;
        Continuation = continuation;
    }

    public int RoundsWon { get; }
    public int RoundsLost { get; }
    public TeamArenaRoundContinuation Continuation { get; }
    public bool MatchComplete => Continuation != TeamArenaRoundContinuation.RebindNextRound;
}

/// <summary>Pure score/continuation policy for the protected best-of-N match.</summary>
internal static class TeamArenaRoundPolicy
{
    public static TeamArenaRoundDecision Evaluate(
        bool? won,
        int roundsWon,
        int roundsLost,
        int roundsToWin)
    {
        if (roundsWon < 0 || roundsLost < 0)
            throw new ArgumentOutOfRangeException(nameof(roundsWon), "round scores cannot be negative");
        if (roundsToWin < 1 || roundsToWin > 9)
            throw new ArgumentOutOfRangeException(nameof(roundsToWin), "rounds to win must be between one and nine");

        if (!won.HasValue)
        {
            return new TeamArenaRoundDecision(
                roundsWon,
                roundsLost,
                TeamArenaRoundContinuation.EndNoContest);
        }

        if (won.Value)
            roundsWon++;
        else
            roundsLost++;

        TeamArenaRoundContinuation continuation = roundsWon >= roundsToWin
            ? TeamArenaRoundContinuation.EndMatchWon
            : roundsLost >= roundsToWin
                ? TeamArenaRoundContinuation.EndMatchLost
                : TeamArenaRoundContinuation.RebindNextRound;

        return new TeamArenaRoundDecision(roundsWon, roundsLost, continuation);
    }
}
