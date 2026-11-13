using System;

namespace ArenaMode.Modes;

/// <summary>Why a held solo Tactical squad request was released.</summary>
internal enum SoloTacticalSquadRelease
{
    /// <summary>The body move landed: the player stands where the round starts.</summary>
    BodyLanded,

    /// <summary>
    /// The move reported that it did not land (it stood down, the engine refused it, it failed):
    /// the player is still where they were.
    /// </summary>
    BodyMoveFailed,

    /// <summary>No body move was in flight: it was refused synchronously, or there was nothing to move.</summary>
    NoBodyMove,

    /// <summary>The move never reported within <see cref="SoloTacticalSquadDispatch.BodyMoveCapSeconds"/>.</summary>
    CapExpired,
}

/// <summary>
/// Holds a solo Tactical round's opening squad request until the player's body has landed where
/// the round starts, then releases it exactly once.
///
/// Why: <c>BurstSpawnService.SpawnPair</c> picks the squad's points right after
/// <c>BotCreationData.Create</c> returns, at least 25 m from wherever the player stands at that
/// moment. With a warm profile pool that await can complete synchronously, inside the request.
/// StartRound used to request the squad first and only then start the round-break swap
/// (<c>LiveKitRespawn.AtRoundBreak</c>, which moves a survivor to a Player row at least
/// <c>RespawnMinDistance</c> away; a dead player is moved by the held respawn instead). Whenever
/// the profiles won that race the points were chosen against the spot the player was leaving,
/// and on a compact map the squad could start 10 to 15 m from the new row
/// (docs/reviews/SOLO-TACTICAL-SPAWN-REVIEW.md, "Dispatch order"). Here the request waits for the move.
///
/// The move cannot be pre-resolved instead: <c>ArenaSpawnSelector</c> draws the destination at
/// random inside <c>RespawnEngine.RunRespawn</c>.
///
/// Every way the move can end releases the request with the position the player then has: the
/// move's own report (landed or not), a move that was never started (or threw while starting),
/// and the cap, which bounds a move that never reports at all. The cap equals the squad's own
/// assembly window (<see cref="SoloTacticalSpawnPolicy.AssemblySeconds"/>): the wait for the body
/// may never cost the round more than the wait for the opponents themselves.
///
/// Bound to the round it was opened for: a late report from an earlier round's move, or a round
/// that ended before the move landed, releases nothing. Pure (no Unity), so the offline harnesses
/// drive it with a fake clock and simulated moves (tools/SoloTacticalSpawnChecks); the
/// Unity-facing half (the request, the toast, the log) lives in TacticalMode.
/// </summary>
internal sealed class SoloTacticalSquadDispatch
{
    /// <summary>
    /// The longest a move may hold the request. A legitimate round-break swap lands in a fraction
    /// of a second with warm bundles (75 ms measured on GPU-2, 2026-11-13); only a move that will
    /// never report waits this long, and the request then goes out from where the player stands.
    /// </summary>
    public const float BodyMoveCapSeconds = SoloTacticalSpawnPolicy.AssemblySeconds;

    private readonly Func<float> _clock;
    private readonly Action<int, SoloTacticalSquadRelease, float> _dispatch;
    private bool _pending;
    private int _round;
    private float _openedAt;

    /// <param name="clock">The mode's clock (Unity's <c>Time.time</c>).</param>
    /// <param name="dispatch">Requests the squad: round, why it was released, seconds held.</param>
    public SoloTacticalSquadDispatch(Func<float> clock, Action<int, SoloTacticalSquadRelease, float> dispatch)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    }

    /// <summary>Whether a request is held for <paramref name="round"/>.</summary>
    public bool IsHeldFor(int round) => _pending && _round == round;

    /// <summary>
    /// The round start: hold this round's request FIRST, then start the body move, which reports
    /// through the callback it is handed; release at once when the move was not started.
    ///
    /// The hold is in place before the move starts because a move may report synchronously,
    /// before <paramref name="startBodyMove"/> returns (a refusal inside the swap's first frame).
    /// That report releases the request; the release is idempotent, so the return value cannot
    /// release it a second time. A start that throws releases the request too, and the exception
    /// still reaches the caller.
    /// </summary>
    /// <param name="round">The round being opened.</param>
    /// <param name="startBodyMove">
    /// Starts the move and returns whether it is in flight; the callback it receives reports
    /// whether the body landed. Null means there is nothing to move.
    /// </param>
    public void Open(int round, Func<Action<bool>, bool> startBodyMove)
    {
        _pending = true;
        _round = round;
        _openedAt = _clock();

        bool moving = false;
        try
        {
            moving = startBodyMove != null && startBodyMove(landed => Report(round, landed));
        }
        finally
        {
            if (!moving)
                Release(round, SoloTacticalSquadRelease.NoBodyMove);
        }
    }

    /// <summary>The move's report: landed (true) or not (false). Ignored unless the round is still held.</summary>
    public void Report(int round, bool landed)
        => Release(round, landed ? SoloTacticalSquadRelease.BodyLanded : SoloTacticalSquadRelease.BodyMoveFailed);

    /// <summary>
    /// Per frame while the round has no squad: whether <paramref name="round"/> is still waiting for
    /// its body move. Releases the request (and returns false) once the cap has run out. A hold left
    /// over from another round is dropped rather than released, so it can never stall this one.
    /// </summary>
    public bool Waiting(int round)
    {
        if (!_pending)
            return false;

        if (_round != round)
        {
            _pending = false;
            return false;
        }

        if (_clock() - _openedAt < BodyMoveCapSeconds)
            return true;

        Release(round, SoloTacticalSquadRelease.CapExpired);
        return false;
    }

    /// <summary>The round ended (or the match did) before the request went out: release nothing.</summary>
    public void Cancel() => _pending = false;

    private void Release(int round, SoloTacticalSquadRelease why)
    {
        if (!_pending || _round != round)
            return;

        // Cleared before the dispatch runs: a re-entrant report from inside the request (a warm
        // spawn path that reaches back into the mode) must find nothing left to release.
        _pending = false;
        _dispatch(round, why, _clock() - _openedAt);
    }
}
