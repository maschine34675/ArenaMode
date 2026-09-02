using ArenaMode.Bots;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.BigBrainCompat;

/// <summary>
/// Drives EFT's native move-to-enemy helper from the live lease destination.
/// The action never retains its target BotOwner and stops only movement it
/// issued while its exact epoch/generation remained current.
/// </summary>
public sealed class ArenaPursuitLogic : CustomLogic
{
    private const float UnresolvedArrivalGraceSeconds = 1.5f;

    private readonly ArenaRunToEnemy _nativeRunToEnemy;
    private bool _ownsMovement;
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
        if (!Plugin.RuntimeEnabled
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
            BotOwner.MovementResume();
            _ownsMovement = true;

            if (BotOwner.MoveToEnemyData == null)
            {
                TeamArenaPursuitLeaseBridge.ReportRejected(
                    Plugin.PluginGuid,
                    lease.SourceProfileId,
                    lease.Epoch,
                    lease.Generation);
                StopOwnedMovement();
                return;
            }

            // EFT's node owns its bounded route refresh plus doors and mines,
            // steering, sprint, stance and weapon selection. Calling the path
            // helper unconditionally here would add a raycast and potentially
            // several NavMesh/cover queries on every brain update.
            _nativeRunToEnemy.UpdateNodeByBrain(null);

            // If the native bounded refresh could not produce a route, make
            // one explicit attempt against the leased live position. Do not
            // rebuild a completed close route every frame: give sensing one
            // short chance to establish combat, then reject the unresolved
            // target so Arena can switch to its guarded fallback.
            bool waitingAtUnresolvedArrival = TrackUnresolvedArrival(
                lease,
                out bool unresolvedArrivalExpired);
            if (unresolvedArrivalExpired)
            {
                TeamArenaPursuitLeaseBridge.ReportRejected(
                    Plugin.PluginGuid,
                    lease.SourceProfileId,
                    lease.Epoch,
                    lease.Generation);
                StopOwnedMovement();
                return;
            }

            // A no-path contact grace is not movement execution. Keep the
            // generation unacknowledged until it either becomes a real route,
            // becomes combat, or is rejected above.
            if (waitingAtUnresolvedArrival)
                return;

            if (BotOwner.Mover?.HasPathAndNoComplete != true
                && !BotOwner.MoveToEnemyData.TryMoveToEnemy(lease.Destination))
            {
                TeamArenaPursuitLeaseBridge.ReportRejected(
                    Plugin.PluginGuid,
                    lease.SourceProfileId,
                    lease.Epoch,
                    lease.Generation);
                StopOwnedMovement();
                return;
            }

            BotOwner.WeaponManager?.TryReloadWeaponOrUnderbarrelLauncher();
            if (!TeamArenaPursuitLeaseBridge.ReportStarted(
                    Plugin.PluginGuid,
                    lease.SourceProfileId,
                    lease.Epoch,
                    lease.Generation))
            {
                StopOwnedMovement();
            }
        }
        catch (Exception)
        {
            TeamArenaPursuitLeaseBridge.ReportRejected(
                Plugin.PluginGuid,
                lease.SourceProfileId,
                lease.Epoch,
                lease.Generation);
            StopOwnedMovement();
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
        ResetUnresolvedArrival();
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

        public override void UpdateNodeByBrain(CoreActionResultParams data)
        {
            ReachedDestinationThisUpdate = false;
            bool canRun = DoorOpen() == DoorInteractionStatus.CanRun;
            _owner.SetTargetMoveSpeed(1f);
            NotMovingCheck();
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
