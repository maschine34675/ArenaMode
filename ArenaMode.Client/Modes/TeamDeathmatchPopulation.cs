using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Modes;

/// <summary>
/// Match-local capacity, independent of EFT's sometimes leaked spawn counters.
/// Preparation owns a single flight per faction; its watchdog is terminal,
/// never permission to replace unresolved work. Native admission gets its own
/// clock only at submission. Expiry is not evidence that native work ended.
/// </summary>
internal sealed class TeamDeathmatchPopulation
{
    internal const float RequestTimeout = 45f;
    internal const float PreparationSlowAfter = 45f;
    // Operational limit, not a claim that native loading supports cancellation.
    internal const float PreparationTimeout = 300f;
    internal const int MaxOutstandingPerFaction = 3;
    private readonly List<Ticket> _pending = new();
    private int _nextId;
    private bool _stopped;

    internal TeamDeathmatchPopulation(int teamSize, bool localUsec)
    {
        TeamSize = Math.Max(2, Math.Min(12, teamSize));
        LocalUsec = localUsec;
    }

    internal int TeamSize { get; }
    internal bool LocalUsec { get; }
    internal int OutstandingCount => _pending.Count;
    internal int BotTarget(bool usec) => TeamSize - (usec == LocalUsec ? 1 : 0);

    internal bool TryReserve(bool usec, int occupied, float now, out Ticket ticket)
    {
        ticket = null;
        if (_stopped || occupied < 0 || occupied >= BotTarget(usec)) return false;
        Ticket[] faction = _pending.Where(item => item.Usec == usec).ToArray();
        if (faction.Length >= MaxOutstandingPerFaction
            || faction.Any(item => item.Preparing || IsCurrent(item, now))) return false;
        if (float.IsNaN(now) || float.IsInfinity(now)) return false;
        ticket = new Ticket(++_nextId, usec, now);
        _pending.Add(ticket);
        return true;
    }

    internal bool IsCurrent(Ticket ticket, float now)
        => !_stopped && ticket != null && _pending.Contains(ticket)
           && now >= ticket.CreatedAt
           && (ticket.Preparing
               ? now - ticket.CreatedAt < PreparationTimeout
               : now >= ticket.SubmittedAt && now - ticket.SubmittedAt < RequestTimeout);

    internal bool CanAdmit(Ticket ticket, float now)
        => IsCurrent(ticket, now) && !ticket.Preparing;

    // Called exactly once, immediately before native submission, after the
    // caller rechecks the exact raid, actual occupancy and current placement.
    internal bool TryBeginDispatch(Ticket ticket, int occupied, float now)
    {
        if (!IsCurrent(ticket, now) || !ticket.Preparing
            || occupied < 0 || occupied >= BotTarget(ticket.Usec)) return false;
        ticket.SubmittedAt = now;
        ticket.Preparing = false;
        return true;
    }

    internal bool PreparationExpired(Ticket ticket, float now)
        => !_stopped && ticket != null && _pending.Contains(ticket) && ticket.Preparing
           && now >= ticket.CreatedAt && now - ticket.CreatedAt >= PreparationTimeout;

    internal Ticket ExpiredPreparation(float now)
        => _pending.FirstOrDefault(ticket => PreparationExpired(ticket, now));

    internal void DispatchFinished(Ticket ticket, bool handedToNative)
    {
        // An entirely synchronous observer can already have settled the ticket.
        if (ticket == null || !_pending.Contains(ticket)) return;
        // Submission owns the phase transition, not this terminal notification:
        // callbacks may already have settled it and must never resurrect it.
        if (!handedToNative) _pending.Remove(ticket);
    }

    internal void Settle(Ticket ticket) => _pending.Remove(ticket);

    internal bool IsStalled(float now)
        => !_stopped && _pending.Any(item => item.Preparing
            ? now - item.CreatedAt >= PreparationSlowAfter : !IsCurrent(item, now));

    // At most three tickets per faction. Use process-local request numbers,
    // never profile IDs; inspection must not settle or reopen uncertain work.
    internal string DescribePending(bool usec, float now)
    {
        Ticket[] tickets = _pending.Where(item => item.Usec == usec).ToArray();
        if (tickets.Length == 0) return "pending=0";
        return "pending=" + tickets.Length + " [" + string.Join(", ", tickets.Select(item => DescribeTicket(item, now))) + "]";
    }

    private string DescribeTicket(Ticket item, float now)
    {
        string phase = item.Preparing ? "preparing" : "native";
        float elapsed = Math.Max(0f, now - (item.Preparing ? item.CreatedAt : item.SubmittedAt));
        string state = !IsCurrent(item, now) ? ":expired"
            : item.Preparing && now - item.CreatedAt >= PreparationSlowAfter ? ":slow" : "";
        string preparation = item.Preparing ? ""
            : FormattableString.Invariant($":prepared={item.SubmittedAt - item.CreatedAt:F1}s");
        return FormattableString.Invariant($"#{item.Id}:{phase}:{elapsed:F0}s{state}{preparation}");
    }

    internal void Stop()
    {
        _stopped = true;
        _pending.Clear();
    }

    internal sealed class Ticket
    {
        internal Ticket(int id, bool usec, float createdAt)
        { Id = id; Usec = usec; CreatedAt = createdAt; }
        internal int Id { get; }
        internal bool Usec { get; }
        internal float CreatedAt { get; }
        internal bool Preparing { get; set; } = true;
        internal float SubmittedAt { get; set; }
    }
}
