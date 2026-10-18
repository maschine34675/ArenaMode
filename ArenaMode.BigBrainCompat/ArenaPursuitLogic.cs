using ArenaMode.Bots;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ArenaMode.BigBrainCompat;

/// <summary>
/// Drives EFT's mover toward the exact NavMesh-validated lease destination.
/// The action never retains its target BotOwner and stops only movement it
/// issued while its exact epoch/generation remained current.
/// </summary>
public sealed class ArenaPursuitLogic : CustomLogic
{
    private const float UnresolvedArrivalGraceSeconds = 1.5f;
    private const float RouteRefreshSeconds = 1f;
    private const float ExactDestinationToleranceMeters = 0.35f;

    private static readonly HashSet<string> LoggedFailures = new(StringComparer.Ordinal);

    private readonly ArenaRunToEnemy _nativeRunToEnemy;
    private bool _ownsMovement;
    private int _ownedLeaseEpoch = -1;
    private long _ownedLeaseGeneration;
    private float _nextRouteRefreshAt;
    private int _unresolvedArrivalEpoch = -1;
    private long _unresolvedArrivalGeneration;
    private float _unresolvedArrivalSince;
    private Vector3 _unresolvedArrivalDestination;

    public ArenaPursuitLogic(BotOwner botOwner)
        : base(botOwner)
    {
        _nativeRunToEnemy = new ArenaRunToEnemy(botOwner);
    }

    public override void Update(CustomLayer.ActionData data)
    {
        if (!Library.RuntimeEnabled
            || data is not ArenaPursuitActionData pursuitData
            || !ArenaPursuitRuntime.TryGetUsableLease(
                BotOwner,
                pursuitData.Epoch,
                pursuitData.Generation,
                out TeamArenaPursuitLease lease))
        {
            StopOwnedMovement();
            return;
        }

        try
        {
            // SAIN's decision loop may replace GoalEnemy while Arena owns the
            // higher-priority movement layer. Rebind the exact lease target on
            // every update before either system can inspect or move toward it.
            if (!ArenaPursuitRuntime.TryBindLeaseTarget(BotOwner, lease, out _, out string goalProblem))
            {
                RejectLease(
                    lease,
                    "target-bind:" + goalProblem,
                    "the exact current-round EFT enemy could not be rebound toward '" + lease.TargetProfileId + "'");
                return;
            }

            ArenaSainHandoffState sainHandoff = ArenaSainMovementHandoff.EnsureSuspended(
                BotOwner,
                out string handoffProblem);
            if (sainHandoff == ArenaSainHandoffState.WaitingForDeactivation)
            {
                StopOwnedMovement();
                return;
            }

            if (sainHandoff == ArenaSainHandoffState.Unavailable)
            {
                RejectLease(
                    lease,
                    "sain-handoff",
                    handoffProblem ?? "unknown incompatibility");
                return;
            }

            BotOwner.PatrollingData?.Pause();
            BotOwner.MovementResume();
            bool ownsExactLease = _ownsMovement
                && _ownedLeaseEpoch == lease.Epoch
                && _ownedLeaseGeneration == lease.Generation;
            if (!ownsExactLease
                && (BotOwner.Mover?.CurrentState == EBotMoverState.NearDoor
                    || BotOwner.DoorOpener?.Interacting == true))
            {
                // A door link temporarily owns the current mover state and its
                // target is the interaction point, not the final lease point.
                // A fresh Arena generation cannot prove who created that path,
                // so leave it unacknowledged and let the native door sequence
                // finish. The exact route is acquired only after Default owns
                // the mover again; the bounded lease timeout otherwise yields.
                return;
            }

            if (!ownsExactLease)
            {
                // Layer transitions can leave a complete route owned by the
                // outgoing PFT/SAIN action. Clear it, then require this exact
                // generation to acquire its own leased destination before it
                // may acknowledge movement.
                BotOwner.Mover?.Stop();
                BotOwner.MovementResume();
                if (!TryAcquireExactLeaseRoute(lease))
                {
                    RejectLease(lease, "route-acquire", "EFT rejected the exact leased destination");
                    return;
                }

                _ownsMovement = true;
                _ownedLeaseEpoch = lease.Epoch;
                _ownedLeaseGeneration = lease.Generation;
                _nextRouteRefreshAt = Time.time + RouteRefreshSeconds;
            }

            // EFT's node owns doors and mines, steering, sprint, stance and
            // weapon selection. Arena refreshes only its exact sampled lease
            // endpoint below so GoalEnemy.CurrPosition cannot overwrite it.
            _nativeRunToEnemy.UpdateNodeByBrain(null);

            // DoorOpen switches EFT to the NearDoor mover and intentionally
            // changes TargetPoint to the interaction point. Do not overwrite
            // or reject that path with the final lease destination while the
            // native door sequence owns it.
            if (_nativeRunToEnemy.HandlingDoorThisUpdate)
            {
                ReportStartedOrStop(lease);
                return;
            }

            // If the exact route has completed, do not rebuild it every frame.
            // Give sensing one short chance at the leased live position. Do not
            // rebuild a completed close route every frame: give sensing one
            // short chance to establish combat, then reject the unresolved
            // target so Arena can switch to another live opponent.
            bool waitingAtUnresolvedArrival = TrackUnresolvedArrival(
                lease,
                out bool unresolvedArrivalExpired);
            if (unresolvedArrivalExpired)
            {
                RejectLease(
                    lease,
                    "unresolved-arrival",
                    "the target did not become a shootable contact at the leased destination");
                return;
            }

            // A no-path contact grace is not movement execution. Keep the
            // generation unacknowledged until it either becomes a real route,
            // becomes combat, or is rejected above.
            if (waitingAtUnresolvedArrival)
                return;

            if ((BotOwner.Mover?.HasPathAndNoComplete != true
                    || Time.time >= _nextRouteRefreshAt)
                && !TryAcquireExactLeaseRoute(lease))
            {
                RejectLease(lease, "route-refresh", "EFT could not refresh the exact leased destination");
                return;
            }

            if (Time.time >= _nextRouteRefreshAt)
                _nextRouteRefreshAt = Time.time + RouteRefreshSeconds;

            BotOwner.WeaponManager?.TryReloadWeaponOrUnderbarrelLauncher();
            ReportStartedOrStop(lease);
        }
        catch (Exception ex)
        {
            RejectLease(lease, "exception", ex.GetBaseException().ToString());
        }
    }

    public override void Stop()
    {
        StopOwnedMovement();
        base.Stop();
    }

    private void StopOwnedMovement()
    {
        if (_ownsMovement)
        {
            try
            {
                BotOwner?.Mover?.Stop();
            }
            catch (Exception)
            {
                // The bot may already be disposed. The lease identity is held as
                // values only, so there is no remaining movement owner to clean.
            }
        }

        _ownsMovement = false;
        _ownedLeaseEpoch = -1;
        _ownedLeaseGeneration = 0;
        _nextRouteRefreshAt = 0f;
        ResetUnresolvedArrival();
    }

    private void RejectLease(
        TeamArenaPursuitLease lease,
        string category,
        string detail)
    {
        string key = lease.Epoch + ":" + lease.SourceProfileId + ":" + category;
        if (LoggedFailures.Add(key))
        {
            string message = $"Arena pursuit {category} rejected for '{lease.SourceProfileId}' "
                + $"at {lease.Epoch}/{lease.Generation}: {detail}";
            // A stale route or a target that moved out of contact is normal
            // replanning. Contract failures and exceptions remain warnings.
            if (category == "route-acquire" || category == "route-refresh" || category == "unresolved-arrival")
                Library.ModLogger?.LogDebug(message);
            else
                Library.ModLogger?.LogWarning(message);
        }

        TeamArenaPursuitLeaseBridge.ReportRejected(
            Library.ConsumerId,
            lease.SourceProfileId,
            lease.Epoch,
            lease.Generation);
        StopOwnedMovement();
    }

    private bool IsAtLeaseDestination(TeamArenaPursuitLease lease)
        => (lease.Destination - BotOwner.Position).sqrMagnitude
           <= ArenaRunToEnemy.ArenaReachDistanceMeters
              * ArenaRunToEnemy.ArenaReachDistanceMeters;

    private bool TryAcquireExactLeaseRoute(TeamArenaPursuitLease lease)
    {
        NavMeshPathStatus status = BotOwner.GoToPoint(
            lease.Destination,
            slowAtTheEnd: false,
            reachDist: ArenaRunToEnemy.ArenaReachDistanceMeters,
            getUpWithCheck: false,
            mustHaveWay: false);
        bool exactTarget = status == NavMeshPathStatus.PathComplete
            && BotOwner.Mover?.TargetPoint is Vector3 targetPoint
            && (targetPoint - lease.Destination).sqrMagnitude
               <= ExactDestinationToleranceMeters * ExactDestinationToleranceMeters;
        if (exactTarget)
            return true;

        // GoToPoint mutates mover state even when it reports a partial or
        // rejected route. Clear that path before the lease is rejected so no
        // unowned fallback endpoint can survive this generation.
        BotOwner.Mover?.Stop();
        return IsAtLeaseDestination(lease);
    }

    private void ReportStartedOrStop(TeamArenaPursuitLease lease)
    {
        if (!TeamArenaPursuitLeaseBridge.ReportStarted(
                Library.ConsumerId,
                lease.SourceProfileId,
                lease.Epoch,
                lease.Generation))
        {
            StopOwnedMovement();
        }
    }

    private bool TrackUnresolvedArrival(
        TeamArenaPursuitLease lease,
        out bool expired)
    {
        expired = false;
        if (BotOwner.Mover?.HasPathAndNoComplete == true)
        {
            ResetUnresolvedArrival();
            return false;
        }

        float reachDistanceSquared = ArenaRunToEnemy.ArenaReachDistanceMeters
            * ArenaRunToEnemy.ArenaReachDistanceMeters;
        bool exactLeaseAlreadyWaiting = _unresolvedArrivalEpoch == lease.Epoch
            && _unresolvedArrivalGeneration == lease.Generation;
        if (exactLeaseAlreadyWaiting)
        {
            // A moving target invalidates the completed endpoint. Let the outer
            // action acquire a fresh route immediately instead of waiting out
            // an arrival which no longer describes this lease destination.
            if ((lease.Destination - _unresolvedArrivalDestination).sqrMagnitude
                > reachDistanceSquared)
            {
                ResetUnresolvedArrival();
                return false;
            }

            expired = Time.time - _unresolvedArrivalSince
                >= UnresolvedArrivalGraceSeconds;
            return true;
        }

        bool reachedLeaseDestination = (lease.Destination - BotOwner.Position).sqrMagnitude
            <= reachDistanceSquared;
        if (!_nativeRunToEnemy.ReachedDestinationThisUpdate
            && !reachedLeaseDestination)
        {
            ResetUnresolvedArrival();
            return false;
        }

        _unresolvedArrivalEpoch = lease.Epoch;
        _unresolvedArrivalGeneration = lease.Generation;
        _unresolvedArrivalSince = Time.time;
        _unresolvedArrivalDestination = lease.Destination;
        return true;
    }

    private void ResetUnresolvedArrival()
    {
        _unresolvedArrivalEpoch = -1;
        _unresolvedArrivalGeneration = 0;
        _unresolvedArrivalSince = 0f;
        _unresolvedArrivalDestination = Vector3.zero;
    }

    /// <summary>
    /// EFT's RunToEnemy behavior with an Arena-local arrival radius. PitFireTeam
    /// raises the bot setting used by the stock node to 1.5 m, which can stop a
    /// follower on the near side of thin geometry and make the outer action
    /// rebuild that same path every frame. All other movement primitives stay
    /// native: bounded replanning, door/mine handling, steering, sprint, pose
    /// and weapon selection.
    /// </summary>
    private sealed class ArenaRunToEnemy : RunToEnemy
    {
        internal const float ArenaReachDistanceMeters = 1f;

        internal ArenaRunToEnemy(BotOwner botOwner)
            : base(botOwner)
        {
        }

        internal bool ReachedDestinationThisUpdate { get; private set; }
        internal bool HandlingDoorThisUpdate { get; private set; }

        public override void UpdateNodeByBrain(CoreActionResultParams data)
        {
            ReachedDestinationThisUpdate = false;
            DoorInteractionStatus doorStatus = DoorOpen();
            HandlingDoorThisUpdate = doorStatus == DoorInteractionStatus.OpeningDoor;
            bool canRun = doorStatus == DoorInteractionStatus.CanRun;
            _owner.SetTargetMoveSpeed(1f);
            PeriodChangeWeapon();
            _owner.SetPose(1f);

            if (canRun && _owner.Mover.HasPathAndNoComplete)
            {
                _owner.Steering.LookToMovingDirection();
                _owner.Sprint(val: true);
            }
            else
            {
                _owner.LookData.SetLookPointByHearing();
                _owner.Sprint(val: false);
            }

            // IsComeTo reads PlayerRemainingDist, which is zero when no path
            // exists. Never turn that sentinel into a false arrival: the outer
            // action must then perform its bool-checked route attempt.
            if (!_owner.Mover.HasPathAndNoComplete
                || !_owner.Mover.IsComeTo(ArenaReachDistanceMeters, onCover: false))
                return;

            ReachedDestinationThisUpdate = true;
            InPoint();
        }
    }
}
