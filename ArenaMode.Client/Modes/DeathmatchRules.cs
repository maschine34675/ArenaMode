using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>Accepted menu values frozen before the raid is armed.</summary>
public sealed class DeathmatchOptions
{
    public DeathmatchOptions(int rounds, int kills, int seconds, bool freeForAll,
        int intermissionSeconds, bool respawn)
    {
        if (rounds < 0) throw new ArgumentOutOfRangeException(nameof(rounds));
        if (kills < 0) throw new ArgumentOutOfRangeException(nameof(kills));
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (intermissionSeconds < 0) throw new ArgumentOutOfRangeException(nameof(intermissionSeconds));
        if (rounds > 0 && kills == 0 && seconds == 0)
            throw new ArgumentException("Finite Deathmatch rounds require a kill or time limit.");
        Rounds = rounds;
        Kills = kills;
        Seconds = seconds;
        FreeForAll = freeForAll;
        IntermissionSeconds = intermissionSeconds;
        Respawn = respawn;
    }

    public int Rounds { get; }
    public int Kills { get; }
    public int Seconds { get; }
    public bool FreeForAll { get; }
    public int IntermissionSeconds { get; }
    public bool Respawn { get; }
    public bool Continuous => Kills == 0 && Seconds == 0;
}

internal enum DeathmatchPhase { Waiting, Live, Intermission, Finished, Stopped }
internal enum DeathmatchOutcome { None, Won, Lost, Draw, Complete, NoContest }

/// <summary>
/// Per-round individual counters independent of the scoreboard's compacted
/// history. Generated profile IDs are different contestants, never durable bot
/// slots. Exhausting the explicit capacity ends as no contest rather than
/// dropping a possible winner or merging opponents into one contestant.
/// </summary>
internal sealed class DeathmatchRules
{
    internal const int ContestantCapacity = 4096;
    internal const float ResultSeconds = 15f;
    private sealed class Counter
    {
        internal string Name;
        internal int Value;
        internal Counter(string name) { Name = name; }
    }

    private readonly string _localId;
    private readonly string _localName;
    private readonly Dictionary<string, Counter> _kills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Counter> _wins = new(StringComparer.Ordinal);
    private double _startedAt;
    private double _finishedAt;

    internal DeathmatchRules(DeathmatchOptions options, string localId, string localName)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(localId)) throw new ArgumentException("Local contestant ID is required.", nameof(localId));
        _localId = localId;
        _localName = SafeName(localName, "You");
    }

    internal DeathmatchOptions Options { get; }
    internal DeathmatchPhase Phase { get; private set; }
    internal DeathmatchOutcome Outcome { get; private set; }
    internal DeathmatchOutcome RoundOutcome { get; private set; }
    internal int RoundNumber { get; private set; }
    internal int LocalKills { get; private set; }
    internal int LocalDeaths { get; private set; }
    internal int LocalRoundWins { get; private set; }
    internal int LocalRoundLosses { get; private set; }
    internal int DrawnRounds { get; private set; }
    internal string WinnerProfileId { get; private set; }
    internal string WinnerName { get; private set; }
    internal string MatchWinnerName { get; private set; }
    internal string Reason { get; private set; }
    internal string RoundReason { get; private set; }
    internal int RetainedKillCounters => _kills.Count;
    internal int RetainedWinCounters => _wins.Count;

    /// <summary>Same per-round raw-kill counters as winner selection; never scoreboard points/history.</summary>
    internal void ReadAnnouncementScore(out int? killsRemaining, out int? lead)
    {
        int best = LocalKills;
        int otherBest = 0;
        if (Options.FreeForAll)
            foreach (var pair in _kills)
            {
                best = Math.Max(best, pair.Value.Value);
                if (pair.Key != _localId) otherBest = Math.Max(otherBest, pair.Value.Value);
            }
        killsRemaining = Options.Kills > 0 ? Math.Max(0, Options.Kills - best) : null;
        // No leadership contest in all-vs-player or unlimited continuous play.
        lead = Options.FreeForAll && !Options.Continuous && best > 0
            ? LocalKills.CompareTo(otherBest) : null;
    }

    internal bool StartRound(double now)
    {
        if (!Finite(now) || (Phase != DeathmatchPhase.Waiting && Phase != DeathmatchPhase.Intermission)) return false;
        if (RoundNumber == int.MaxValue) { NoContest("Round counter capacity reached"); return false; }
        RoundNumber++;
        LocalKills = LocalDeaths = 0;
        _kills.Clear();
        WinnerProfileId = WinnerName = RoundReason = null;
        RoundOutcome = DeathmatchOutcome.None;
        _startedAt = now;
        Phase = DeathmatchPhase.Live;
        return true;
    }

    internal double TimeRemaining(double now)
        => Options.Seconds <= 0 ? -1d
            : Math.Max(0d, Options.Seconds - ((Phase == DeathmatchPhase.Live ? now : _finishedAt) - _startedAt));

    internal bool AdmitEvent(double now)
    {
        if (!Finite(now) || Phase != DeathmatchPhase.Live) return false;
        if (Options.Seconds > 0 && now - _startedAt >= Options.Seconds)
        {
            FinishRound(now, timedOut: true);
            return false;
        }
        return true;
    }

    internal void Tick(double now) => AdmitEvent(now);

    internal bool RecordKill(double now, string killerId, string killerName, string victimId)
    {
        if (!AdmitEvent(now) || string.IsNullOrWhiteSpace(killerId)
            || string.IsNullOrWhiteSpace(victimId) || string.Equals(killerId, victimId, StringComparison.Ordinal)) return false;
        bool local = string.Equals(killerId, _localId, StringComparison.Ordinal);
        if (!Options.FreeForAll && !local) return false;
        // Continuous play has no winner. Its durable local totals live on the
        // match scoreboard; do not accumulate an unbounded opponent history.
        if (Options.Continuous)
        {
            if (local) LocalKills = Increment(LocalKills);
            return true;
        }
        if (!_kills.TryGetValue(killerId, out Counter count))
        {
            if (_kills.Count >= ContestantCapacity) { NoContest("Round contestant capacity reached"); return false; }
            count = new Counter(local ? _localName : SafeName(killerName, "Opponent"));
            _kills.Add(killerId, count);
        }
        count.Value = Increment(count.Value);
        if (local) LocalKills = count.Value;
        if (Options.Kills > 0 && count.Value >= Options.Kills)
        {
            WinnerProfileId = killerId;
            WinnerName = count.Name;
            FinishRound(now, timedOut: false);
        }
        return true;
    }

    internal void RecordLocalDeath(double now)
    {
        if (AdmitEvent(now)) LocalDeaths = Increment(LocalDeaths);
    }

    internal void Stop()
    {
        // A user's ordinary raid exit is not a scored round or a fabricated win.
        if (Phase != DeathmatchPhase.Finished) Phase = DeathmatchPhase.Stopped;
        _kills.Clear();
        _wins.Clear();
    }

    private void FinishRound(double now, bool timedOut)
    {
        _finishedAt = now;
        RoundReason = timedOut ? "Time limit" : "Kill limit";
        if (timedOut && Options.FreeForAll)
        {
            SelectLeader(_kills, out var leaderId, out var leaderName, out _);
            WinnerProfileId = leaderId;
            WinnerName = leaderName;
        }

        if (!Options.FreeForAll && timedOut)
            RoundOutcome = Options.Kills > 0 ? DeathmatchOutcome.Lost : DeathmatchOutcome.Complete;
        else RoundOutcome = WinnerProfileId == null ? DeathmatchOutcome.Draw
            : WinnerProfileId == _localId ? DeathmatchOutcome.Won : DeathmatchOutcome.Lost;
        if (RoundOutcome == DeathmatchOutcome.Won) LocalRoundWins = Increment(LocalRoundWins);
        if (RoundOutcome == DeathmatchOutcome.Lost) LocalRoundLosses = Increment(LocalRoundLosses);
        if (RoundOutcome == DeathmatchOutcome.Draw) DrawnRounds = Increment(DrawnRounds);

        // Only finite FFA series need a match-long individual wins table.
        if (Options.FreeForAll && Options.Rounds > 0 && WinnerProfileId != null)
        {
            if (!_wins.TryGetValue(WinnerProfileId, out Counter wins))
            {
                if (_wins.Count >= ContestantCapacity) { NoContest("Match contestant capacity reached"); return; }
                wins = new Counter(WinnerName);
                _wins.Add(WinnerProfileId, wins);
            }
            wins.Value = Increment(wins.Value);
        }
        Phase = Options.Rounds > 0 && RoundNumber >= Options.Rounds
            ? DeathmatchPhase.Finished : DeathmatchPhase.Intermission;
        if (Phase != DeathmatchPhase.Finished) return;
        Reason = $"{RoundNumber} round{(RoundNumber == 1 ? "" : "s")} completed";
        if (Options.FreeForAll)
        {
            SelectLeader(_wins, out var matchWinner, out var matchName, out _);
            MatchWinnerName = matchName;
            Outcome = matchWinner == null ? DeathmatchOutcome.Draw
                : matchWinner == _localId ? DeathmatchOutcome.Won : DeathmatchOutcome.Lost;
        }
        else Outcome = Options.Kills == 0 ? DeathmatchOutcome.Complete
            : LocalRoundWins == LocalRoundLosses ? DeathmatchOutcome.Draw
            : LocalRoundWins > LocalRoundLosses ? DeathmatchOutcome.Won : DeathmatchOutcome.Lost;
    }

    private static void SelectLeader(Dictionary<string, Counter> counters,
        out string id, out string name, out int value)
    {
        id = name = null;
        value = 0;
        bool tie = false;
        foreach (var entry in counters)
        {
            if (entry.Value.Value > value)
            {
                id = entry.Key;
                name = entry.Value.Name;
                value = entry.Value.Value;
                tie = false;
            }
            else if (entry.Value.Value == value) tie = true;
        }
        if (tie) id = name = null;
    }

    private void NoContest(string reason)
    {
        Outcome = RoundOutcome = DeathmatchOutcome.NoContest;
        Reason = RoundReason = reason;
        WinnerProfileId = WinnerName = MatchWinnerName = null;
        Phase = DeathmatchPhase.Finished;
    }

    private static int Increment(int value) => value == int.MaxValue ? value : value + 1;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static string SafeName(string text, string fallback)
    {
        text = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        return (text.Length > 64 ? text.Substring(0, 64) : text).Replace('<', '‹').Replace('>', '›');
    }
}
