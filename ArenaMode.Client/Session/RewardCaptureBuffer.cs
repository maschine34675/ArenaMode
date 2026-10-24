using ArenaMode.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaMode.Session;

/// <summary>Bounded plain-data journal. Never retains a Player or client profile.</summary>
internal sealed class RewardCaptureBuffer
{
    internal const int MaxRounds = 100, MaxLives = 20000;
    private long _sequence;
    private int _lives;
    private readonly Dictionary<long, RewardActorCapture> _actors = new Dictionary<long, RewardActorCapture>();
    private readonly Dictionary<long, RewardDeathCapture> _deaths = new Dictionary<long, RewardDeathCapture>();
    internal RewardCaptureRequest Packet { get; }
    internal RewardRoundCapture Current => Packet.Rounds.LastOrDefault();

    internal RewardCaptureBuffer(string run, string arm, string native)
        => Packet = new RewardCaptureRequest { RunId = run, ArmToken = arm, NativeEndIdentity = native };

    internal void BeginRound(int number)
    {
        if (Packet.Final || Current != null && Current.Number == number) return;
        if (number != Packet.Rounds.Count + 1 || Packet.Rounds.Count >= MaxRounds)
        { Invalidate(); return; }
        if (Current != null) Current.Closed = true;
        Packet.Rounds.Add(new RewardRoundCapture { Number = number });
        _actors.Clear(); _deaths.Clear();
    }

    internal void Advance(long milliseconds)
    {
        if (Packet.Final || Current == null || Current.Closed) return;
        if (milliseconds < Current.ObservedDurationMilliseconds || milliseconds > 86400000)
        { Invalidate(); return; }
        Current.ObservedDurationMilliseconds = milliseconds;
    }

    internal void Register(long body, string participant, string side, long milliseconds)
    {
        if (Packet.Final || Current == null || Current.Closed) return;
        if (_actors.TryGetValue(body, out var actor))
        {
            if (actor.ParticipantId != participant || actor.Side != side) Invalidate();
            return;
        }
        if (body <= 0 || string.IsNullOrWhiteSpace(participant) || ++_lives > MaxLives
            || milliseconds < 0 || milliseconds > Current.ObservedDurationMilliseconds)
        { Invalidate(); return; }
        actor = new RewardActorCapture { Body = body, ParticipantId = participant,
            Side = side, SpawnedAtMilliseconds = milliseconds };
        Current.Actors.Add(actor);
        _actors.Add(body, actor);
    }

    internal void Death(long victim, long? killer, string kind, long milliseconds)
    {
        if (Packet.Final || Current == null || Current.Closed) return;
        if (_deaths.TryGetValue(victim, out var existing))
        {
            // Give-up uses native damage first, then its exact confirmed-body
            // callback. One private final packet permits this explicit upgrade.
            if (kind == "LocalForfeit" && existing.Kind == "Environment" && killer == null)
                existing.Kind = kind;
            else if (existing.Kind != kind || existing.KillerBody != killer) Invalidate();
            return;
        }
        if (!_actors.ContainsKey(victim)
            || killer.HasValue && !_actors.ContainsKey(killer.Value)
            || milliseconds < 0 || milliseconds > Current.ObservedDurationMilliseconds
            || Current.Deaths.Count > 0 && milliseconds < Current.Deaths[Current.Deaths.Count - 1].AtMilliseconds)
        { Invalidate(); return; }
        var death = new RewardDeathCapture { Sequence = ++_sequence,
            AtMilliseconds = milliseconds, VictimBody = victim, KillerBody = killer, Kind = kind };
        Current.Deaths.Add(death);
        _deaths.Add(victim, death);
    }

    internal bool KnowsActor(long body) => _actors.ContainsKey(body);
    internal bool KnowsDeath(long body) => _deaths.ContainsKey(body);

    internal void CloseRound()
    { if (!Packet.Final && Current != null) Current.Closed = true; }

    internal void Invalidate()
    { if (!Packet.Final) Packet.State = "NoContest"; }

    internal RewardCaptureRequest Finish()
    {
        if (Packet.Final) return Packet;
        Packet.Final = true;
        Packet.Revision = 1;
        return Packet;
    }
}
