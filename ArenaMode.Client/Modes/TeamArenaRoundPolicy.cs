using System;

namespace ArenaMode.Modes;

/// <summary>
/// How a protected 5v5 round actually finished. Drawn is a RESULT — the round was
/// played and nobody won it; NoContest is the absence of a result, because Arena
/// could not prove the identities or bodies the round needed.
/// </summary>
internal enum TeamArenaRoundResult
{
    Won,
    Lost,
    Drawn,
    NoContest,
}

internal enum TeamArenaRoundContinuation
{
    RebindNextRound,
    EndMatchWon,
    EndMatchLost,
    EndNoContest,
    EndMatchDrawn,
}

internal readonly struct TeamArenaRoundDecision
{
    public TeamArenaRoundDecision(
        int roundsWon,
        int roundsLost,
        int consecutiveScorelessRounds,
        TeamArenaRoundContinuation continuation)
    {
        RoundsWon = roundsWon;
        RoundsLost = roundsLost;
        ConsecutiveScorelessRounds = consecutiveScorelessRounds;
        Continuation = continuation;
    }

    public int RoundsWon { get; }
    public int RoundsLost { get; }
    public int ConsecutiveScorelessRounds { get; }
    public TeamArenaRoundContinuation Continuation { get; }
    public bool MatchComplete => Continuation != TeamArenaRoundContinuation.RebindNextRound;
}

/// <summary>Pure score/continuation policy for the protected best-of-N match.</summary>
internal static class TeamArenaRoundPolicy
{
    /// <summary>
    /// How many drawn rounds may follow each other before the match is called a
    /// draw.
    ///
    /// A drawn round scores nothing, so on its own it could repeat forever — and
    /// the round timer that produces most draws exists as the anti-stuck safety
    /// net (see Settings.TacticalRoundSeconds), which a freely repeating draw
    /// would quietly disarm. Two keeps the net: a genuine stalemate costs one
    /// extra round, a bot stuck somewhere unreachable costs two and then ends.
    /// A decided round resets the count, so an occasional draw in a long match
    /// never accumulates toward this.
    /// </summary>
    public const int MaxConsecutiveScorelessRounds = 2;

    public static TeamArenaRoundDecision Evaluate(
        TeamArenaRoundResult result,
        int roundsWon,
        int roundsLost,
        int roundsToWin,
        int consecutiveScorelessRounds)
    {
        if (roundsWon < 0 || roundsLost < 0)
            throw new ArgumentOutOfRangeException(nameof(roundsWon), "round scores cannot be negative");
        if (roundsToWin < 1 || roundsToWin > 9)
            throw new ArgumentOutOfRangeException(nameof(roundsToWin), "rounds to win must be between one and nine");
        if (consecutiveScorelessRounds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveScorelessRounds),
                "the scoreless streak cannot be negative");
        }

        if (result == TeamArenaRoundResult.NoContest)
        {
            // No result to score and no streak to continue: the match stops here.
            return new TeamArenaRoundDecision(
                roundsWon,
                roundsLost,
                consecutiveScorelessRounds,
                TeamArenaRoundContinuation.EndNoContest);
        }

        if (result == TeamArenaRoundResult.Drawn)
        {
            int streak = consecutiveScorelessRounds + 1;
            return new TeamArenaRoundDecision(
                roundsWon,
                roundsLost,
                streak,
                streak >= MaxConsecutiveScorelessRounds
                    ? TeamArenaRoundContinuation.EndMatchDrawn
                    : TeamArenaRoundContinuation.RebindNextRound);
        }

        if (result == TeamArenaRoundResult.Won)
            roundsWon++;
        else
            roundsLost++;

        TeamArenaRoundContinuation continuation = roundsWon >= roundsToWin
            ? TeamArenaRoundContinuation.EndMatchWon
            : roundsLost >= roundsToWin
                ? TeamArenaRoundContinuation.EndMatchLost
                : TeamArenaRoundContinuation.RebindNextRound;

        return new TeamArenaRoundDecision(roundsWon, roundsLost, 0, continuation);
    }
}
