using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Kits;
using ArenaMode.Respawn;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

internal sealed partial class ZombieTeamRuntime
{
    /// <summary>
    /// Only after the old followers/boss have retired may a living leader take
    /// the established live own-gear replacement path. Never mutate a live
    /// inventory in place, and never lend the dead-body authorization to it.
    /// </summary>
    private void BeginLivingLeaderRefill()
    {
        if (_boundLocalPlayer is not LocalPlayer oldPlayer || _teamLoadoutSnapshot == null
            || ArenaRespawnDriver.IsRespawnInProgress || ArenaRespawnDriver.StandDown)
        { FailTeamTransition("the living PMC has no exclusive refill pipeline or entry snapshot"); return; }
        if (!_teamLoadoutSnapshot.TryArm(_teamRoundEpoch, out string armProblem))
        { FailTeamTransition("living entry loadout arm failed: " + armProblem); return; }
        if (!_teamLoadoutSnapshot.TryPrepare(_teamGame.Profile, _teamRoundEpoch,
                out var prepared, out string prepareProblem))
        { FailTeamTransition("living entry loadout prepare failed: " + prepareProblem); return; }

        int epoch = _teamRoundEpoch;
        bool committed = false;
        _teamRebindStep = TeamRebindStep.AwaitLocalRefill;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        bool LeaseIsCurrent()
            => IsCurrentRaid() && _phase == TeamPhase.Rebinding
               && _teamRebindStep == TeamRebindStep.AwaitLocalRefill
               && _teamRoundEpoch == epoch && ReferenceEquals(_boundLocalPlayer, oldPlayer)
               && !_playerDown && PitFireTeamCompat.TeamArenaActive
               && PitFireTeamCompat.FrozenCompanionCount == TeamCompanionCount
               && PitFireTeamCompat.RoundTransitionActive && PitFireTeamCompat.PersistenceSuppressionActive
               && TryReadExpectedPitLifecycle(out var lifecycle, out _)
               && lifecycle.Stage == PitFireTeamRoundLifecycleStage.ReadyForLeader;

        bool started = LiveKitRespawn.AtRoundBreakOwnGear(
            _teamGame,
            (profile, previous) =>
            {
                // The engine has unregistered the old owner at this seam, so
                // the pre-swap world identity check belongs in stillWanted.
                if (!LeaseIsCurrent() || !ReferenceEquals(previous, oldPlayer)
                    || !ReferenceEquals(profile, oldPlayer.Profile) || committed)
                    throw new InvalidOperationException("Zombie living refill lease changed before equipment commit.");
                AutoQuickSlots.SettleBeforeReissue(profile);
                if (!prepared.TryCommit(profile, out TeamArenaLoadoutRestoreResult result))
                    throw new InvalidOperationException("Zombie living entry loadout commit failed: "
                        + (result?.Failure ?? "no restore diagnostic"));
                committed = true;
                AutoQuickSlots.NoteItemsReissued(prepared.NextRuntimeByPreviousRuntimeId);
            },
            Settings.SpawnProtectionSeconds.Value,
            () => LeaseIsCurrent() && TryValidateCurrentTeamLeaderBody(oldPlayer, out _),
            success =>
            {
                // Stale completion after quit/failed transition must not bind
                // another raid's player or resurrect the finished coordinator.
                if (!IsCurrentRaid() || _phase == TeamPhase.Ended) return;
                if (!LeaseIsCurrent())
                { FailTeamTransition("the living refill completed outside its exact team lease"); return; }
                if (!success || !committed)
                { FailTeamTransition("the living PMC entry loadout replacement failed"); return; }
                Player replacement = _teamGame.LocalPlayer;
                if (ReferenceEquals(replacement, oldPlayer))
                { FailTeamTransition("the living refill did not install a replacement PMC"); return; }
                if (!TryValidateCurrentTeamLeaderBody(replacement, out string bodyProblem))
                { FailTeamTransition(bodyProblem); return; }
                if (!PitFireTeamCompat.TryBindLocalPlayerBody(replacement, out string bindProblem))
                { FailTeamTransition("refilled leader bind failed: " + bindProblem); return; }
                _boundLocalPlayer = replacement;
                _teamRebindStep = TeamRebindStep.RebuildLeader;
                _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
                Plugin.ModLogger.LogDebug($"Zombie team wave {epoch}: living PMC entry loadout restored before follower rebuild.");
            });
        if (!started)
            FailTeamTransition("the living PMC entry loadout replacement could not start");
    }
}
