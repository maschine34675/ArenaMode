using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArenaMode.Shared;

namespace ArenaMode.Modes;

/// <summary>Sends one Tactical round report; the production one is <c>ArenaTacticalRoundClient.SendAsync</c>.</summary>
internal delegate Task<TacticalRoundReply> TacticalRoundSender(string armToken, int round, int seq);

/// <summary>
/// What the gate needs to turn a round into a gear level: the frozen difficulty step and the map's
/// start and end level, plus whether the enemy squad is PMC (a scav squad is reported but never
/// purged or announced). The provider answers null when the map does not scale the bots' tier or
/// the numbers cannot be read; the gate then holds nothing and offers no level.
/// </summary>
internal readonly struct TacticalGearContext
{
    public TacticalGearContext(ArenaDifficultyStep step, int startLevel, int endLevel, bool squadIsPmc)
    {
        Step = step;
        StartLevel = startLevel;
        EndLevel = endLevel;
        SquadIsPmc = squadIsPmc;
    }

    public ArenaDifficultyStep Step { get; }
    public int StartLevel { get; }
    public int EndLevel { get; }
    public bool SquadIsPmc { get; }
}

/// <summary>Why <see cref="TacticalOpeningBandGate.Evaluate"/> lets a held squad request go, or that it does not.</summary>
internal enum TacticalBandRelease
{
    /// <summary>Keep holding.</summary>
    Hold,

    /// <summary>No report is waiting for an answer (none made, all answered, or its send budget is spent).</summary>
    NothingOutstanding,

    /// <summary>The server does not write a stage band for this raid (or reporting was given up): nothing to wait for.</summary>
    NotApplying,

    /// <summary>The report of exactly this round is acknowledged.</summary>
    Settled,

    /// <summary>This round's gear level equals the one of the last acknowledged round: the ack could not change the band.</summary>
    SameLevel,

    /// <summary>The cap has run out; the request goes at whatever stage the server holds.</summary>
    CapExpired,
}

/// <summary>
/// Solo Tactical opening rounds, client half (docs/reviews/TACTICAL-OPENING-ROUNDS-20261119.md): reports
/// the round to the server and holds the round's squad request until the server acknowledged the
/// report, so a profile generated for round N is generated with the band of round N. Pure: the clock,
/// the sender, the arm token, the gear numbers and the log are seams, so the harness runs it against
/// a fake clock and a scripted sender. Nothing here touches a Unity object; the production sender
/// runs its HTTP call off the main thread and this class only polls the task it returns.
///
/// What it decides, and why:
///  - The server stores the LAST reported round. A report reaches it at EndRound, at least six
///    seconds (the aftermath) before the next StartRound, far longer than a loopback request.
///  - The squad request of a round waits for the ack ONLY when the round's gear level differs from
///    the level of the last acknowledged round (no ack counts as round 1, the server's default).
///    Round 1 and every round from 4 on never wait: the stage cannot change there, so a dead route
///    must not cost a hold in every round of the raid (<c>LevelDiffersFromAcked</c> bounds
///    the comparison at round <see cref="ArenaDifficultyPolicy.TacticalOpeningRounds"/> + 1, because
///    an acknowledged round that stops advancing would otherwise differ from every later round).
///  - The hold does not look at the squad's role. The server writes the band role-blind, so a scav
///    squad's profile is generated with the same stage as a PMC squad's and waits for the same
///    acknowledgement; only the pool purge and the buy-screen line (<see cref="CurrentGearLevel"/>)
///    are PMC-only, because only PMC gear is purged or announced.
///  - The hold is capped at <see cref="HoldCapSeconds"/> (measured from <see cref="MarkHeld"/>): the
///    squad assembles within 45 s, a stuck request must not eat that window. On the cap the request
///    goes out anyway and the client keeps expecting its last ACKNOWLEDGED round, never a guess.
///  - Both failure shapes are real. A lost REPORT leaves the server and the client at the previous
///    stage (softer, consistent). A lost ACK (the server took the report, the answer never came)
///    leaves the server one stage ahead of the client's belief until the resend gets a Duplicate
///    reply, or the next round's report. What always holds: the band is never above the step's full
///    gear level.
///  - Any refusal (except the transient one below) and any <c>applies:false</c> set
///    <see cref="Applies"/> to false for the rest of the raid: every hold is released and nothing is
///    sent again. A transport failure of the FIRST report that survives all sends does the same (an
///    old server without the route); a failure after an earlier success keeps <see cref="Applies"/>
///    and stays at the last acknowledged round.
///  - The refusal "raid is ending" (<see cref="Protocol.TacticalRoundRaidEndingRefusal"/>) is
///    TRANSIENT: an aborted native end returns the same raid to InRaid with its round kept. It is not
///    an ack, changes nothing else and writes no warning; the report is resent within its budget.
///  - <see cref="Report"/> and <see cref="Tick"/> and every query catch all exceptions and log once:
///    a failure of this feature must never throw into the match start or the frame loop.
/// </summary>
internal sealed class TacticalOpeningBandGate
{
    /// <summary>How long a squad request may wait for the ack. Equals the 3 s plan-failure retry, far below the 45 s assembly window.</summary>
    public const float HoldCapSeconds = 3f;

    /// <summary>An unanswered report whose send finished (failed) is sent again after this long, with the same seq.</summary>
    public const float ResendSeconds = 2f;

    /// <summary>A send that has not completed after this long counts as lost: it no longer blocks a new send (a late answer is still welcome).</summary>
    public const float InFlightLostSeconds = 8f;

    /// <summary>Sends per report; after that the gate waits for the next round's report.</summary>
    public const int MaxSends = 4;

    private const int MaxTrackedSends = 16;

    // One counter for the whole game process, not per gate or per mode instance: a second match in
    // the same raid (a failed first start that is retried) or a later raid must never report a seq
    // the server already holds (it would answer Duplicate and the round would never advance). The
    // server restarts its own counter at the start of every raid, so a higher number is always fine.
    private static int s_lastSeq;

    private readonly Func<float> _clock;
    private readonly TacticalRoundSender _send;
    private readonly Func<string> _armToken;
    private readonly Func<TacticalGearContext?> _gearContext;
    private readonly Action<string> _debug;
    private readonly Action<string> _warn;

    private sealed class SendRecord
    {
        public Task<TacticalRoundReply> Task;
        public int Seq;
        public float SentAt;
        public bool Lost;
    }

    private readonly List<SendRecord> _sends = new List<SendRecord>();

    private bool _applies = true;
    private int _ackedRound;
    private int _ackedSeq;
    private int _newestRound;
    private int _newestSeq;
    private int _sendsOfNewest;
    private float _lastSendAt;
    private bool _transportFailure;
    private string _lastFailure;
    private bool _exhausted;
    private int _heldRound;
    private float _heldAt;
    private bool _warnedOnce;
    private bool _faultLogged;

    public TacticalOpeningBandGate(
        Func<float> clock,
        TacticalRoundSender send,
        Func<string> armToken,
        Func<TacticalGearContext?> gearContext,
        Action<string> debug = null,
        Action<string> warn = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _armToken = armToken ?? throw new ArgumentNullException(nameof(armToken));
        _gearContext = gearContext ?? throw new ArgumentNullException(nameof(gearContext));
        _debug = debug;
        _warn = warn;
    }

    /// <summary>True until a refusal, an <c>applies:false</c>, a blank arm token or a failed first report ended the feature for this raid.</summary>
    public bool Applies => _applies;

    /// <summary>The round the server last acknowledged (its STORED round), 0 when none was.</summary>
    public int AckedRound => _ackedRound;

    /// <summary>The seq of the last acknowledged report, 0 when none was.</summary>
    public int AckedSeq => _ackedSeq;

    /// <summary>The seq of the newest report made, 0 when none was.</summary>
    public int NewestSeq => _newestSeq;

    /// <summary>
    /// The gear level (band centre) the server writes for the acknowledged round, the number the
    /// pool purge and the buy-screen line read. Null when the feature does not apply, no round was
    /// acknowledged, the squad is scav (nothing to purge or announce), the map offers no numbers, or
    /// the opening does not differ from the full level on this map.
    /// </summary>
    public int? CurrentGearLevel
    {
        get
        {
            try
            {
                if (!_applies || _ackedRound < 1 || _gearContext() is not TacticalGearContext context
                    || !context.SquadIsPmc
                    || !ArenaDifficultyPolicy.TacticalOpeningDiffers(context.Step, context.StartLevel, context.EndLevel))
                    return null;
                return ArenaDifficultyPolicy.TacticalGearLevel(
                    context.Step, context.StartLevel, context.EndLevel, _ackedRound);
            }
            catch (Exception ex)
            {
                LogFault("reading the current gear level", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// The buy-screen line for <paramref name="round"/> (<see cref="TacticalOpeningText"/>), empty unless
    /// the feature applies, the squad is PMC and the report of exactly this round is acknowledged: the
    /// player is never told about a stage the server has not confirmed. Called every frame by the buy
    /// screen, so it allocates nothing and never throws (a failing gear context gives an empty line).
    /// </summary>
    public string OpponentGearNote(int round)
    {
        try
        {
            if (!_applies || !Settled(round) || _gearContext() is not TacticalGearContext context)
                return string.Empty;
            return TacticalOpeningText.Note(context.Step, context.StartLevel, context.EndLevel, context.SquadIsPmc, round);
        }
        catch (Exception ex)
        {
            LogFault("reading the opponent gear note", ex);
            return string.Empty;
        }
    }

    /// <summary>Test seam: the process-wide seq counter (the next report gets this plus one).</summary>
    internal static int LastSeqForTests
    {
        get => Volatile.Read(ref s_lastSeq);
        set => Volatile.Write(ref s_lastSeq, value);
    }

    /// <summary>Starts over for a new match: drops every report and send (a late completion of an old one is ignored) and re-arms the once-only logs. The seq counter is NOT reset.</summary>
    public void Reset()
    {
        _sends.Clear();
        _applies = true;
        _ackedRound = 0;
        _ackedSeq = 0;
        _newestRound = 0;
        _newestSeq = 0;
        _sendsOfNewest = 0;
        _lastSendAt = 0f;
        _transportFailure = false;
        _lastFailure = null;
        _exhausted = false;
        _heldRound = 0;
        _heldAt = 0f;
        _warnedOnce = false;
        _faultLogged = false;
    }

    /// <summary>
    /// Reports <paramref name="round"/> to the server: a new seq, the first send at once. A blank arm
    /// token ends the feature for the raid (<see cref="Applies"/> false) without sending anything.
    /// Never throws.
    /// </summary>
    public void Report(int round)
    {
        try
        {
            if (!_applies)
                return;

            string token = _armToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                EndFeature("there is no arm token to report with");
                return;
            }

            int seq = Interlocked.Increment(ref s_lastSeq);
            if (seq < 1 || seq > Protocol.TacticalMaxSeq)
            {
                // The server refuses a seq outside 1..TacticalMaxSeq, and one that is sent anyway
                // would read as a refusal. A process that reported a million times has no use for
                // this feature any more.
                EndFeature("the report counter is used up");
                return;
            }

            _newestRound = round;
            _newestSeq = seq;
            _sendsOfNewest = 0;
            _transportFailure = false;
            _lastFailure = null;
            _exhausted = false;
            TrySend(token);
        }
        catch (Exception ex)
        {
            // Whatever failed, nothing may be left waiting for an answer that will not come.
            _exhausted = true;
            LogFault("reporting the round", ex);
        }
    }

    /// <summary>Polls the answers, resends within the budget and gives up when it is spent. Called every frame, also while the countdown menu is open. Never throws.</summary>
    public void Tick()
    {
        try
        {
            PollSends();
            if (!Outstanding)
                return;

            if (_sendsOfNewest < MaxSends)
            {
                if (_clock() - _lastSendAt >= ResendSeconds && !HasLiveSend())
                {
                    string token = _armToken();
                    if (string.IsNullOrWhiteSpace(token))
                        EndFeature("there is no arm token to report with");
                    else
                        TrySend(token);
                }
            }
            else if (!HasLiveSend())
            {
                GiveUp();
            }
        }
        catch (Exception ex)
        {
            _exhausted = true;
            LogFault("polling the round report", ex);
        }
    }

    /// <summary>True when the report of exactly <paramref name="round"/> is the acknowledged one. An ack of another round does not settle it.</summary>
    public bool Settled(int round) => _ackedRound == round;

    /// <summary>Starts the hold clock of <paramref name="round"/> (the first call per round stamps it, later calls keep it).</summary>
    public void MarkHeld(int round)
    {
        try
        {
            if (_heldRound == round)
                return;
            _heldRound = round;
            _heldAt = _clock();
        }
        catch (Exception ex)
        {
            LogFault("marking the hold", ex);
        }
    }

    /// <summary>How long the request of <paramref name="round"/> has been held, in the gate's clock (0 when never marked).</summary>
    public float HeldSeconds(int round)
    {
        try
        {
            return _heldRound == round ? Math.Max(0f, _clock() - _heldAt) : 0f;
        }
        catch (Exception ex)
        {
            LogFault("reading the held time", ex);
            return 0f;
        }
    }

    /// <summary>True when the held request of <paramref name="round"/> may go out.</summary>
    public bool ShouldRelease(int round) => Evaluate(round) != TacticalBandRelease.Hold;

    /// <summary>
    /// The hold decision with its reason. True ("release") when no report is outstanding, the feature
    /// does not apply, the round's own report is acknowledged, the round's gear level equals the one
    /// of the last acknowledged round (no ack counts as round 1), or the cap has run out. On an
    /// exception it releases: a broken feature never delays a squad.
    /// </summary>
    public TacticalBandRelease Evaluate(int round)
    {
        try
        {
            if (!_applies)
                return TacticalBandRelease.NotApplying;
            if (!Outstanding)
                return TacticalBandRelease.NothingOutstanding;
            if (Settled(round))
                return TacticalBandRelease.Settled;
            if (!LevelDiffersFromAcked(round))
                return TacticalBandRelease.SameLevel;
            if (_heldRound == round && _clock() - _heldAt >= HoldCapSeconds)
                return TacticalBandRelease.CapExpired;
            return TacticalBandRelease.Hold;
        }
        catch (Exception ex)
        {
            LogFault("deciding the hold", ex);
            return TacticalBandRelease.NothingOutstanding;
        }
    }

    // A report is outstanding while it is the newest one, unanswered, and may still be answered:
    // its sends are not all spent and given up on.
    private bool Outstanding => _applies && _newestSeq != 0 && _ackedSeq < _newestSeq && !_exhausted;

    private bool LevelDiffersFromAcked(int round)
    {
        // From the round after the last opening round on, the stage is the full level whatever was
        // acknowledged: a request there never waits. Without this bound a route that died after an
        // early ack (acked round 1 or 2, report 3 lost) would compare the full level with the old
        // acknowledged one in EVERY later round, because the acknowledged round never advances, and
        // cost the 3 s cap in each of them.
        if (round > ArenaDifficultyPolicy.TacticalOpeningRounds + 1)
            return false;
        if (_gearContext() is not TacticalGearContext context)
            return false;
        int ackedLevel = ArenaDifficultyPolicy.TacticalGearLevel(
            context.Step, context.StartLevel, context.EndLevel, Math.Max(1, _ackedRound));
        int roundLevel = ArenaDifficultyPolicy.TacticalGearLevel(
            context.Step, context.StartLevel, context.EndLevel, round);
        return ackedLevel != roundLevel;
    }

    private bool HasLiveSend()
    {
        foreach (var record in _sends)
            if (record.Seq == _newestSeq && !record.Lost)
                return true;
        return false;
    }

    private void TrySend(string token)
    {
        float now = _clock();
        _sendsOfNewest++;
        _lastSendAt = now;
        Task<TacticalRoundReply> task;
        try
        {
            task = _send(token, _newestRound, _newestSeq)
                ?? throw new InvalidOperationException("the sender returned no task");
        }
        catch (Exception ex)
        {
            // A sender that throws synchronously is a failed send like a faulted task.
            NoteTransportFailure(ex);
            return;
        }

        // A task that the gate stops polling (Reset, EndFeature, the tracked-send cap) is never read
        // again; touching its exception here keeps a late fault from reaching the unobserved-exception
        // event. Off the Unity thread and touching nothing of Unity's.
        task.ContinueWith(
            static finished => { _ = finished.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        _sends.Add(new SendRecord { Task = task, Seq = _newestSeq, SentAt = now });
        while (_sends.Count > MaxTrackedSends)
            _sends.RemoveAt(0);
    }

    private void PollSends()
    {
        if (_sends.Count == 0)
            return;

        float now = _clock();
        // Applying a reply may end the feature and clear the list, so work on a copy.
        foreach (var record in _sends.ToArray())
        {
            if (!_sends.Contains(record))
                continue;

            if (record.Task.IsCompleted)
            {
                _sends.Remove(record);
                if (record.Task.IsFaulted || record.Task.IsCanceled)
                {
                    if (record.Seq == _newestSeq)
                        NoteTransportFailure(record.Task.Exception?.GetBaseException());
                    continue;
                }

                try
                {
                    Apply(record.Task.Result);
                }
                catch (Exception ex)
                {
                    // A reply that cannot be applied (null, a nonsensical round) is a malformed answer.
                    if (record.Seq == _newestSeq)
                        NoteTransportFailure(ex);
                }
            }
            else if (!record.Lost && now - record.SentAt >= InFlightLostSeconds)
            {
                record.Lost = true;
                if (record.Seq == _newestSeq)
                    NoteTransportFailure(new TimeoutException(
                        $"no answer within {InFlightLostSeconds:0} s"));
            }
        }
    }

    private void Apply(TacticalRoundReply reply)
    {
        if (reply == null)
            throw new FormatException("the reply was empty");

        if (!reply.Accepted)
        {
            if (string.Equals(reply.Reason, Protocol.TacticalRoundRaidEndingRefusal, StringComparison.Ordinal))
            {
                // Transient (see the class summary): not an ack, no change, no warning. The report
                // stays outstanding and is resent within its budget.
                _debug?.Invoke(
                    $"Tactical opening rounds: the server is ending the raid; report seq {_newestSeq} is not acknowledged.");
                return;
            }

            EndFeature($"the server refused the round report ({reply.Reason ?? "no reason"})");
            return;
        }

        if (!reply.Applies)
        {
            EndFeature("the server writes no Tactical stage band for this raid");
            return;
        }

        if (reply.Round < 1 || reply.Seq < 1)
            throw new FormatException($"the reply names round {reply.Round}, seq {reply.Seq}");

        // Only an answer that is at least as new as the newest report counts: a late reply to an
        // older report must not set the client back. A Duplicate reply (a resend the server already
        // held) is an ack, and the STORED round and seq are trusted over the request's.
        if (reply.Seq >= _newestSeq && reply.Seq >= _ackedSeq)
        {
            _ackedRound = reply.Round;
            _ackedSeq = reply.Seq;
        }
    }

    private void NoteTransportFailure(Exception ex)
    {
        _transportFailure = true;
        _lastFailure = ex?.Message;
        _debug?.Invoke(
            $"Tactical opening rounds: report seq {_newestSeq} (round {_newestRound}) failed: {_lastFailure ?? "unknown"}.");
    }

    // The send budget of the newest report is spent and nothing is left in flight.
    private void GiveUp()
    {
        _exhausted = true;
        if (!_transportFailure)
            return;

        Warn("the server did not answer the round report "
            + $"({_lastFailure ?? "unknown"}); the round is not acknowledged");
        // The FIRST report of the raid that never got through: an old server without the route, or
        // none. Nothing was ever acknowledged, so stop expecting a band. A failure after an
        // earlier success keeps the feature and stays at the last acknowledged round.
        if (_ackedRound < 1)
            EndFeature("the first round report never reached the server");
    }

    private void EndFeature(string why)
    {
        _applies = false;
        _exhausted = true;
        _sends.Clear();
        _debug?.Invoke($"Tactical opening rounds are off for this raid: {why}.");
    }

    private void Warn(string message)
    {
        if (_warnedOnce)
            return;
        _warnedOnce = true;
        _warn?.Invoke($"Tactical opening rounds: {message}.");
    }

    private void LogFault(string doing, Exception ex)
    {
        if (_faultLogged)
            return;
        _faultLogged = true;
        _warn?.Invoke($"Tactical opening rounds failed while {doing} ({ex.GetType().Name}: {ex.Message}); the squad is not held.");
    }
}
