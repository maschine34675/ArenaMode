using ArenaMode.Bots;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.BigBrainCompat;

public sealed class ArenaDominationLogic : CustomLogic
{
    private readonly ObjectiveMove _move;
    private DominationObjectiveLease _lease;
    private ArenaDominationAction _action;
    private BotMover _mover;
    private Player _body;
    private BotMoverState _state;
    private AbstractBotPath _path;
    private Vector3 _destination, _progressPosition;
    private float _refreshAt, _progressAt, _remaining, _startedAt, _handoffAt;
    private bool _ownsMovement, _inDoor, _holding, _warned;

    public ArenaDominationLogic(BotOwner bot) : base(bot) { _move = new ObjectiveMove(bot); }

    public override void Update(CustomLayer.ActionData data)
    {
        try
        {
            var action = data as ArenaDominationAction;
            var lease = action?.Lease;
            if (!ArenaDominationRuntime.CanAct(BotOwner, lease, _ownsMovement)) { StopOwned(); return; }
            if (!ReferenceEquals(_lease, lease)) { StopOwned(); _lease = lease; _action = action; _handoffAt = Time.time; }
            var handoff = ArenaSainMovementHandoff.EnsureSuspended(BotOwner, out var reason);
            if (handoff == ArenaSainHandoffState.Unavailable) { Fail("SAIN handoff: " + reason); return; }
            if (handoff != ArenaSainHandoffState.Ready)
            { if (Time.time - _handoffAt > 3f) Fail("SAIN handoff timed out"); return; }
            if (!lease.Current) { StopOwned(); return; }
            if (!_ownsMovement)
            {
                BotOwner.Mover.Stop(); BotOwner.MovementResume();
                _mover = BotOwner.Mover; _body = BotOwner.GetPlayer; _ownsMovement = true;
                _action.OwnsMovement = true;
                _startedAt = _progressAt = Time.time; _progressPosition = BotOwner.Position; _remaining = float.MaxValue;
                _destination = lease.Destination;
                _holding = lease.Inside(BotOwner.Position);
                if (!_holding && !AcquireRoute())
                {
                    // One alternative on the opposite side, not unlimited scene searches or center stacking.
                    _destination = lease.Center * 2f - lease.Destination;
                    if (!AcquireRoute()) { Fail("no complete objective route"); return; }
                }
                _refreshAt = Time.time + 1f;
                if (Diagnostics.ArenaLog.DeveloperEnabled)
                    Diagnostics.ArenaLog.Developer($"Arena Domination {(_holding ? "holding" : "moving")}: bot={BotOwner.ProfileId}; point={lease.PointId}; destination={_destination}.");
            }
            if (_holding)
            {
                if (lease.Inside(BotOwner.Position))
                { BotOwner.Sprint(false); BotOwner.LookData.SetLookPointByHearing(); return; }
                _holding = false; _startedAt = _progressAt = Time.time; _remaining = float.MaxValue;
                if (!AcquireRoute()) { Fail("return to point refused"); return; }
            }
            _move.UpdateNodeByBrain(null);
            bool inDoor = _move.HandlingDoor || BotOwner.Mover.CurrentState == EBotMoverState.NearDoor;
            if (inDoor)
            {
                _inDoor = true; _progressAt = Time.time; _remaining = float.MaxValue;
                if (BotOwner.Mover.CurrentState == EBotMoverState.NearDoor) ClearRoute();
                if (Time.time - _startedAt > 60f) Fail("objective trip timed out in door");
                return;
            }
            if (_inDoor)
            {
                _inDoor = false; _progressPosition = BotOwner.Position; _progressAt = Time.time;
                if (BotOwner.Mover.CurrentState != EBotMoverState.Default || !AcquireRoute())
                { Fail("door return route refused"); return; }
            }
            if (lease.Inside(BotOwner.Position) && (BotOwner.Position - _destination).sqrMagnitude <= 1f)
            {
                ClearRoute(); BotOwner.Sprint(false); _holding = true;
                if (Diagnostics.ArenaLog.DeveloperEnabled)
                    Diagnostics.ArenaLog.Developer($"Arena Domination arrived: bot={BotOwner.ProfileId}; point={lease.PointId}; position={BotOwner.Position}.");
                return;
            }
            float distance = BotOwner.Mover.DistDestination;
            if (_remaining - distance >= .4f && (BotOwner.Position - _progressPosition).sqrMagnitude >= .04f)
            { _remaining = distance; _progressAt = Time.time; _progressPosition = BotOwner.Position; }
            if (Time.time - _progressAt > 4f || Time.time - _startedAt > 60f)
            { Fail("objective travel made no progress"); return; }
            if (Time.time >= _refreshAt)
            {
                _refreshAt = Time.time + 1f;
                if (!AcquireRoute()) Fail("objective route refresh refused");
            }
        }
        catch (Exception ex)
        {
            Fail("native objective exception");
            if (!_warned) { _warned = true; Library.ModLogger?.LogWarning("Arena Domination movement failed: " + ex.GetBaseException().Message); }
        }
    }

    private bool AcquireRoute()
    {
        if (_lease?.Current != true || !ArenaDominationRuntime.RouteAllowed(BotOwner.Position, _destination)) return false;
        var result = BotOwner.GoToPoint(_destination, slowAtTheEnd: true, reachDist: .45f,
            getUpWithCheck: false, mustHaveWay: false, onlyShortTrie: true);
        if (result == NavMeshPathStatus.PathComplete && BotOwner.Mover.TargetPoint is Vector3 target
            && (target - _destination).sqrMagnitude <= .1225f
            && ArenaDominationRuntime.CornersAllowed(BotOwner.Position, BotOwner.Mover.GetCornersFromIndex(0)))
        {
            _state = BotOwner.Mover._moverStateMachine.DefaultMoverState; _path = _state.PathController.CurPath;
            return _path != null;
        }
        BotOwner.Mover.Stop(); return false;
    }

    private void Fail(string reason)
    {
        var lease = _lease;
        try { DominationObjectiveBridge.Reject(lease); }
        finally { StopOwned(); }
        if (Diagnostics.ArenaLog.DeveloperEnabled)
            Diagnostics.ArenaLog.Developer($"Arena Domination trip rejected: {lease?.PointId}; bot {BotOwner?.ProfileId}; {reason}.");
    }

    private bool ClearRoute()
    {
        if (_state == null || (!ReferenceEquals(_state.PathController.CurPath, _path) && _state.PathController.CurPath != null)) return false;
        _state.PathController.Stop(); _path = null;
        if (_state.LastTargetPoint is Vector3 point && (point - _destination).sqrMagnitude <= .1225f) _state.LastTargetPoint = null;
        return true;
    }

    private void StopOwned()
    {
        if (_ownsMovement && ReferenceEquals(BotOwner.GetPlayer, _body) && ReferenceEquals(BotOwner.Mover, _mover))
        {
            try
            {
                if (ClearRoute() && BotOwner.Mover.CurrentState == EBotMoverState.Default)
                { BotOwner.Mover.Stop(); BotOwner.Sprint(false); }
            }
            catch { /* Native disposal; never acquire another body's route to clean up. */ }
        }
        if (_action != null) _action.OwnsMovement = false;
        _ownsMovement = _holding = _inDoor = false; _lease = null; _action = null; _body = null; _mover = null; _state = null; _path = null;
    }

    public override void Stop() { StopOwned(); base.Stop(); }

    private sealed class ObjectiveMove : RunToEnemy
    {
        internal bool HandlingDoor;
        internal ObjectiveMove(BotOwner bot) : base(bot) { }
        public override void UpdateNodeByBrain(CoreActionResultParams data)
        {
            var door = DoorOpen(); HandlingDoor = door == DoorInteractionStatus.OpeningDoor;
            _owner.SetTargetMoveSpeed(1f); _owner.SetPose(1f); PeriodChangeWeapon();
            bool moving = door == DoorInteractionStatus.CanRun && _owner.Mover.HasPathAndNoComplete;
            if (moving) _owner.Steering.LookToMovingDirection();
            _owner.Sprint(moving && _owner.CanSprintPlayer && _owner.Mover.DistDestination > 5f);
        }
    }
}
