using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Modes;

/// <summary>
/// Match-local capacity, independent of EFT's sometimes leaked spawn counters.
/// Expiry invalidates admission, but is NOT evidence that native work ended.
/// Keep uncertain dispatched requests as bounded debt; never start a second
/// profile load for a faction while its first load has not returned.
/// </summary>
internal sealed class TeamDeathmatchPopulation
{
    internal const float RequestTimeout = 45f;
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
            || faction.Any(item => !item.Dispatched || IsCurrent(item, now))) return false;
        ticket = new Ticket(++_nextId, usec, now);
        _pending.Add(ticket);
        return true;
    }

    internal bool IsCurrent(Ticket ticket, float now)
        => !_stopped && ticket != null && _pending.Contains(ticket)
           && now >= ticket.CreatedAt && now - ticket.CreatedAt < RequestTimeout;

    internal void DispatchFinished(Ticket ticket, bool handedToNative)
    {
        // An entirely synchronous observer can already have settled the ticket.
        if (ticket == null || !_pending.Contains(ticket)) return;
        if (handedToNative) ticket.Dispatched = true;
        else _pending.Remove(ticket);
    }

    internal void Settle(Ticket ticket) => _pending.Remove(ticket);

    internal bool IsStalled(float now)
        => !_stopped && _pending.Any(item => !IsCurrent(item, now));

    // At most three tickets per faction. Use process-local request numbers,
    // never profile IDs; inspection must not settle or reopen uncertain work.
    internal string DescribePending(bool usec, float now)
    {
        Ticket[] tickets = _pending.Where(item => item.Usec == usec).ToArray();
        if (tickets.Length == 0) return "pending=0";
        return "pending=" + tickets.Length + " [" + string.Join(", ", tickets.Select(item =>
            FormattableString.Invariant($"#{item.Id}:{(item.Dispatched ? "native" : "loading")}:{Math.Max(0f, now - item.CreatedAt):F0}s{(IsCurrent(item, now) ? "" : ":expired")}"))) + "]";
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
        internal bool Dispatched { get; set; }
    }
}
