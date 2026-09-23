using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.BigBrainCompat;

public sealed class ArenaPickupLogic : CustomLogic
{
    private readonly PickupMove _move;
    private ArenaPickupObjective _lease;
    private bool _ownsMovement;
    private BotMover _ownedMover;
    private Player _ownedBody;
    private BotMoverState _ownedState;
    private AbstractBotPath _ownedPath;
    private bool _wasHandlingDoor;
    private float _refreshAt;
    public ArenaPickupLogic(BotOwner bot) : base(bot) { _move = new PickupMove(bot); }

    public override void Update(CustomLayer.ActionData data)
    {
        try
        {
            var lease = (data as ArenaPickupAction)?.Lease;
            if (lease == null || !ReferenceEquals(lease.Bot, BotOwner) || !lease.Current()) { StopOwned(); return; }
            if (!ReferenceEquals(_lease, lease)) { StopOwned(); _lease = lease; }
            var handoff = ArenaSainMovementHandoff.EnsureSuspended(BotOwner, out var reason);
            if (handoff == ArenaSainHandoffState.Unavailable) { Fail("SAIN handoff: " + reason); return; }
            if (handoff != ArenaSainHandoffState.Ready) return;
            if (!lease.Current()) { StopOwned(); return; }
            if (!_ownsMovement)
            {
                // We now own BigBrain's action slot. No patrol-state mutation:
                // the previous layer was already stopped by BigBrain.
                BotOwner.Mover.Stop(); BotOwner.MovementResume();
                if (!AcquireRoute()) { Fail("native route refused"); return; }
                _ownsMovement = true; _ownedMover = BotOwner.Mover; _ownedBody = BotOwner.GetPlayer;
                lease.Started = true; _refreshAt = Time.time + 1f;
            }
            _move.UpdateNodeByBrain(null);
            bool inDoor = _move.HandlingDoor || BotOwner.Mover.CurrentState == EBotMoverState.NearDoor;
            lease.Progress(inDoor);
            if (inDoor)
            {
                _wasHandlingDoor = true;
                // Door callbacks can return to Default outside our Update.
                // Detach the suspended pickup route now, while its identity is
                // still proven, so an expired lease cannot revive it later.
                if (BotOwner.Mover.CurrentState == EBotMoverState.NearDoor) ClearDefaultRoute();
                return;
            }
            // Native Default re-entry can rebuild the same route. Reacquire it
            // immediately under our still-current action, never retain the old
            // path object's identity after a door transition.
            if (_wasHandlingDoor)
            {
                _wasHandlingDoor = false;
                if (BotOwner.Mover.CurrentState != EBotMoverState.Default || !AcquireRoute()) { Fail("door route return refused"); return; }
                _refreshAt = Time.time + 1f;
            }
            if (Time.time >= _refreshAt && (BotOwner.Position - lease.Target.Position).sqrMagnitude > .2f)
            {
                _refreshAt = Time.time + 1f;
                if (!AcquireRoute()) Fail("native route refresh refused");
            }
        }
        catch (Exception ex) { Fail("native exception: " + ex.GetBaseException().Message); }
    }

    private bool AcquireRoute()
    {
        if (!_lease.Current() || !ArenaPickupObjective.RouteAllowed(BotOwner.Position, _lease.Target.Position)) return false;
        var result = BotOwner.GoToPoint(_lease.Target.Position, slowAtTheEnd: true, reachDist: .45f, getUpWithCheck: false, mustHaveWay: false);
        if (result == NavMeshPathStatus.PathComplete && BotOwner.Mover.TargetPoint is Vector3 point
            && (point - _lease.Target.Position).sqrMagnitude <= .1225f
            && ArenaPickupObjective.CornersAllowed(BotOwner.Position, BotOwner.Mover.GetCornersFromIndex(0)))
        {
            _ownedState = BotOwner.Mover._moverStateMachine.DefaultMoverState;
            _ownedPath = _ownedState.PathController.CurPath;
            return true;
        }
        // Native GoToPoint can write a partial route even on failure.
        BotOwner.Mover.Stop(); return false;
    }

    private void Fail(string reason) { _lease?.Release(reason); StopOwned(); }
    private bool ClearDefaultRoute()
    {
        if (_ownedState == null || (!ReferenceEquals(_ownedState.PathController.CurPath, _ownedPath)
            && _ownedState.PathController.CurPath != null)) return false;
        _ownedState.PathController.Stop();
        _ownedPath = null;
        if (_ownedState.LastTargetPoint is Vector3 point && _lease != null
            && (point - _lease.Target.Position).sqrMagnitude <= .1225f)
            _ownedState.LastTargetPoint = null;
        return true;
    }

    private void StopOwned()
    {
        if (_ownsMovement && ReferenceEquals(BotOwner.GetPlayer, _ownedBody) && ReferenceEquals(BotOwner.Mover, _ownedMover))
        {
            try
            {
                // NearDoor owns a different temporary path. Revoke only our
                // captured default route, including the native return target;
                // never stop a newer action's path or the door interaction.
                if (ClearDefaultRoute())
                {
                    if (BotOwner.Mover.CurrentState == EBotMoverState.Default) { BotOwner.Mover.Stop(); BotOwner.Sprint(false); }
                }
            }
            catch { /* Native disposal. */ }
        }
        _ownsMovement = false; _ownedMover = null; _ownedBody = null; _ownedState = null; _ownedPath = null; _lease = null; _wasHandlingDoor = false;
    }
    public override void Stop() { _lease?.Release("action stopped"); StopOwned(); base.Stop(); }

    private sealed class PickupMove : RunToEnemy
    {
        internal bool HandlingDoor;
        internal PickupMove(BotOwner bot) : base(bot) { }
        public override void UpdateNodeByBrain(CoreActionResultParams data)
        {
            var door = DoorOpen(); HandlingDoor = door == DoorInteractionStatus.OpeningDoor;
            _owner.SetTargetMoveSpeed(1f); _owner.SetPose(1f); PeriodChangeWeapon();
            bool moving = door == DoorInteractionStatus.CanRun && _owner.Mover.HasPathAndNoComplete;
            if (moving) _owner.Steering.LookToMovingDirection();
            _owner.Sprint(moving && _owner.Mover.DistDestination > 5f);
            if (!HandlingDoor && _owner.Mover.CurrentState == EBotMoverState.Default
                && _owner.Mover.HasPathAndNoComplete && _owner.Mover.IsComeTo(.45f, onCover: false)) _owner.StopMove();
        }
    }
}
