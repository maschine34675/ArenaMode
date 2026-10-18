using ArenaMode.Pickups;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.BigBrainCompat;

public sealed class ArenaPickupLayer : CustomLayer
{
    private readonly List<ArenaPickupTarget> _candidates = new();
    private ArenaPickupObjective _lease;
    private float _nextAttempt;
    private bool _warned;

    public ArenaPickupLayer(BotOwner bot, int priority) : base(bot, priority) { }
    public override string GetName() => "Arena Combat Pickup";

    public override bool IsActive()
    {
        try { return TryActivate(); }
        catch (System.Exception ex)
        {
            _lease?.Release("selection failed"); _lease = null; _nextAttempt = Time.time + 8f;
            if (!_warned) { _warned = true; Library.ModLogger?.LogWarning("Arena pickup selection skipped: " + ex.GetBaseException().Message); }
            return false;
        }
    }

    private bool TryActivate()
    {
        if (_lease?.Current() == true) return true;
        if (_lease != null) { _lease.Release("expired, collected or interrupted"); _lease = null; _nextAttempt = Time.time + 8f; }
        if (Time.time < _nextAttempt) return false;
        _nextAttempt = Time.time + 2f;
        return ArenaPickupObjective.TryAcquire(BotOwner, _candidates, out _lease);
    }
    public override Action GetNextAction() => new(typeof(ArenaPickupLogic), "Arena pickup", new ArenaPickupAction(_lease));
    public override bool IsCurrentActionEnding()
    {
        try { return _lease?.Current() != true; }
        catch { _lease?.Release("native owner disposed"); return true; }
    }
    public override void Stop()
    {
        _lease?.Release("layer yielded"); _lease = null; _nextAttempt = Time.time + 8f;
        base.Stop();
    }
}

internal sealed class ArenaPickupAction : CustomLayer.ActionData
{
    internal readonly ArenaPickupObjective Lease;
    internal ArenaPickupAction(ArenaPickupObjective lease) => Lease = lease;
}
