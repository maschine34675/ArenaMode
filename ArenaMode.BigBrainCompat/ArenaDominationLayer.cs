using ArenaMode.Bots;
using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace ArenaMode.BigBrainCompat;

/// <summary>Capture/defend intent yields immediate combat, but remains active while quietly holding.</summary>
public sealed class ArenaDominationLayer : CustomLayer
{
    private DominationObjectiveLease _lease;
    private ArenaDominationAction _action;
    public ArenaDominationLayer(BotOwner bot, int priority) : base(bot, priority) { }
    public override string GetName() => "Arena Combat Domination";
    public override bool IsActive()
    {
        if (!DominationObjectiveBridge.TryGet(BotOwner, out var lease)) return false;
        bool same = ReferenceEquals(_lease, lease);
        if (!ArenaDominationRuntime.CanAct(BotOwner, lease, same && _action?.OwnsMovement == true)) return false;
        // Keep the running action identity until BigBrain actually ends it.
        // IsActive is evaluated before IsCurrentActionEnding; replacing both
        // fields here could strand the old action on a revoked planner lease.
        if (!same) _lease = lease;
        return true;
    }
    public override Action GetNextAction()
    {
        _action = new ArenaDominationAction(_lease);
        return new(typeof(ArenaDominationLogic), "Domination " + _lease?.PointId, _action);
    }
    public override bool IsCurrentActionEnding() => _action == null || !ReferenceEquals(_action.Lease, _lease)
        || !ArenaDominationRuntime.CanAct(BotOwner, _lease, _action.OwnsMovement);
    public override void Stop()
    {
        // A temporary combat/medical preemption must not destroy the stable planner assignment.
        _action = null; _lease = null; base.Stop();
    }
}

internal sealed class ArenaDominationAction : CustomLayer.ActionData
{
    internal ArenaDominationAction(DominationObjectiveLease lease) { Lease = lease; }
    internal readonly DominationObjectiveLease Lease;
    internal bool OwnsMovement;
}
