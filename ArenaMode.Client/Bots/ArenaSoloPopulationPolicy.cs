using System;
using System.Collections.Generic;

namespace ArenaMode.Bots;

/// <summary>
/// One reserved human slot, a bounded native bot budget, and no timeout on work
/// already handed to EFT. A lost callback costs capacity until raid teardown;
/// time passing cannot prove that native activation stopped.
/// </summary>
internal sealed class ArenaSoloPopulationPolicy
{
    internal const double PreparationSeconds = 180d;
    internal sealed class Ticket
    {
        internal int Id;
        internal bool Boss, Pmc, Dispatched;
        internal double Expires;
        internal object RejectedBody;
    }

    private readonly List<Ticket> _tickets = new List<Ticket>();
    private bool _stopped;
    private int _serial;
    internal int Capacity { get; }
    internal bool BossEnabled { get; }
    internal bool NextBossIsKilla { get; private set; }
    internal int Outstanding => _tickets.Count;
    internal int NativeOutstanding
    {
        get { int count = 0; foreach (var ticket in _tickets) if (ticket.Dispatched) count++; return count; }
    }
    internal int PendingPmc
    {
        get { int count = 0; foreach (var ticket in _tickets) if (ticket.Pmc) count++; return count; }
    }
    internal bool BossPending => _tickets.Exists(t => t.Boss);

    internal ArenaSoloPopulationPolicy(int participants, bool boss)
    {
        if (participants < 2 || participants > 24)
            throw new ArgumentOutOfRangeException(nameof(participants));
        Capacity = participants - 1;
        BossEnabled = boss;
    }

    internal void Sweep(double now)
        => _tickets.RemoveAll(t => !t.Dispatched && now >= t.Expires);

    internal bool IsCurrent(Ticket ticket, double now)
    {
        Sweep(now);
        return !_stopped && ticket != null && _tickets.Contains(ticket);
    }

    private bool Fits(Ticket ticket, int alive, int bosses)
    {
        if (alive < 0 || bosses < 0 || bosses > alive || alive + _tickets.Count > Capacity)
            return false;
        int pendingBosses = _tickets.Exists(t => t.Boss) ? 1 : 0;
        if (ticket.Boss)
            return BossEnabled && bosses + pendingBosses <= 1;
        int normalLimit = Capacity - (BossEnabled ? 1 : 0);
        return alive - bosses + _tickets.Count - pendingBosses <= normalLimit;
    }

    internal bool TryReserve(int alive, int bosses, bool boss, bool pmc, double now, out Ticket ticket)
    {
        Sweep(now);
        ticket = null;
        if (_stopped || boss && (!BossEnabled || BossPending) || alive + Outstanding >= Capacity)
            return false;
        var candidate = new Ticket { Id = ++_serial, Boss = boss, Pmc = pmc, Expires = now + PreparationSeconds };
        _tickets.Add(candidate);
        if (!Fits(candidate, alive, bosses))
        {
            _tickets.Remove(candidate);
            return false;
        }
        ticket = candidate;
        return true;
    }

    internal bool TryBeginDispatch(Ticket ticket, int alive, int bosses, double now)
    {
        if (!IsCurrent(ticket, now) || ticket.Dispatched || !Fits(ticket, alive, bosses))
            return false;
        ticket.Dispatched = true;
        return true;
    }

    // EFT registers the arriving body BEFORE our observer. The caller excludes
    // that exact identity from these counts, while every other body still counts.
    internal bool CanAdmit(Ticket ticket, int otherAlive, int otherBosses, double now)
        => IsCurrent(ticket, now) && ticket.Dispatched && Fits(ticket, otherAlive, otherBosses);

    internal void DispatchFinished(Ticket ticket, bool handedToNative)
    {
        if (!handedToNative) _tickets.Remove(ticket);
    }

    internal void Settle(Ticket ticket) => _tickets.Remove(ticket);
    internal void Rejected(Ticket ticket, object body, bool retired, bool original)
    {
        if (!original || !_tickets.Contains(ticket)) return;
        ticket.RejectedBody = body;
        if (retired) Settle(ticket);
    }
    internal void ObserveDeath(object body)
        => _tickets.RemoveAll(t => ReferenceEquals(t.RejectedBody, body));
    internal void BossDied() => NextBossIsKilla = !NextBossIsKilla;
    internal void Stop() { _stopped = true; _tickets.Clear(); }
}
