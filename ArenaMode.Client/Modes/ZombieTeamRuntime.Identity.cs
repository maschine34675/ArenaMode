using ArenaMode.Compat;
using ArenaMode.Kits;
using ArenaMode.Session;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Modes;

// Exact-body and lifecycle barriers reused from Arena's Tactical coordinator.
// This is our adapter logic, not code copied from PitFireTeam.
internal sealed partial class ZombieTeamRuntime
{
    private bool TryReadBoundLocalPlayer(out bool alive, out string problem)
    {
        alive = false;
        problem = string.Empty;
        try
        {
            if (ReferenceEquals(_boundLocalPlayer, null) || _boundLocalPlayer == null)
            {
                problem = "the exact local PMC body became unavailable";
                return false;
            }

            bool demonstrablyDead = _playerDown
                || _boundLocalPlayer.HealthController?.IsAlive == false;
            var world = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance
                : null;
            var current = world?.MainPlayer;
            if (ReferenceEquals(world, null) || world == null)
            {
                problem = "GameWorld became unavailable while verifying the exact local PMC body";
                return false;
            }

            // The persistent dictionary is overwritten, never cleared, when a
            // replacement registers with the same profile. Checking it closes
            // the gap where that replacement dies and disappears between ticks.
            var everExisted = world.GetEverExistedPlayerByID(_boundLocalProfileId);
            if (ReferenceEquals(everExisted, null)
                || everExisted == null
                || !ReferenceEquals(everExisted, _boundLocalPlayer))
            {
                problem = "GameWorld registered a replacement for the exact local PMC body";
                return false;
            }

            IPlayer[] registeredWithProfile = world.RegisteredPlayers
                .Where(candidate => string.Equals(
                    candidate?.ProfileId?.Trim(),
                    _boundLocalProfileId,
                    StringComparison.Ordinal))
                .ToArray();
            bool registeredIdentityValid = demonstrablyDead
                ? registeredWithProfile.Length == 0
                  || (registeredWithProfile.Length == 1
                      && ReferenceEquals(registeredWithProfile[0], _boundLocalPlayer))
                : registeredWithProfile.Length == 1
                  && ReferenceEquals(registeredWithProfile[0], _boundLocalPlayer);
            if (!registeredIdentityValid)
            {
                problem = $"GameWorld has {registeredWithProfile.Length} active bodies under the local profile instead of the exact bound PMC";
                return false;
            }

            if (!ReferenceEquals(current, _boundLocalPlayer))
            {
                // EFT may clear MainPlayer after the real body is already proven
                // dead. It may never substitute another body into the five.
                if (current != null || !demonstrablyDead)
                {
                    problem = "GameWorld replaced or lost the exact local PMC body";
                    return false;
                }
            }

            if (!string.Equals(
                    _boundLocalPlayer.ProfileId?.Trim(),
                    _boundLocalProfileId,
                    StringComparison.Ordinal))
            {
                problem = "the bound local PMC profile identity changed";
                return false;
            }

            if (demonstrablyDead)
                return true;

            if (_boundLocalPlayer.HealthController?.IsAlive != true)
            {
                problem = "the bound local PMC health state became unreadable";
                return false;
            }

            alive = true;
            return true;
        }
        catch (Exception ex)
        {
            problem = "the exact local PMC body could not be verified: "
                      + ex.GetBaseException().Message;
            return false;
        }
    }

    private bool TryFreezeTeamFollowers(
        IReadOnlyList<BotOwner> followers,
        out string problem)
    {
        problem = string.Empty;
        try
        {
            if (followers == null || followers.Count != TeamCompanionCount)
            {
                problem = $"Arena could not freeze exactly {TeamCompanionCount} PitFireTeam follower bodies";
                return false;
            }

            var expectedProfiles = PitFireTeamCompat.CurrentCompanionProfilesByAccountId;
            if (expectedProfiles.Count != TeamCompanionCount)
            {
                problem = "PitFireTeam's current frozen-account profile map is incomplete";
                return false;
            }

            var registered = Comfort.Common.Singleton<IBotGame>.Instantiated
                ? Comfort.Common.Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners?.ToArray()
                : null;
            var world = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance
                : null;
            if (registered == null || ReferenceEquals(world, null) || world == null)
            {
                problem = "EFT's follower body registries are unavailable";
                return false;
            }

            BotOwner[] ordered = followers
                .OrderBy(bot => bot?.Profile?.AccountId, StringComparer.Ordinal)
                .ToArray();
            var seenBodies = new HashSet<BotOwner>(ReferenceEqualityComparer<BotOwner>.Instance);
            var seenPlayers = new HashSet<Player>(ReferenceEqualityComparer<Player>.Instance);
            var seenAccounts = new HashSet<string>(StringComparer.Ordinal);
            var seenProfiles = new HashSet<string>(StringComparer.Ordinal);
            string[] profiles = new string[TeamCompanionCount];

            for (int index = 0; index < ordered.Length; index++)
            {
                BotOwner follower = ordered[index];
                string accountId = follower?.Profile?.AccountId?.Trim();
                string profileId = follower?.ProfileId?.Trim();
                Player player = follower?.GetPlayer;
                if (ReferenceEquals(follower, null)
                    || follower == null
                    || follower.BotState != EBotState.Active
                    || follower.IsDead
                    || follower.HealthController?.IsAlive != true
                    || string.IsNullOrWhiteSpace(accountId)
                    || string.IsNullOrWhiteSpace(profileId)
                    || !expectedProfiles.TryGetValue(accountId, out string expectedProfile)
                    || !string.Equals(profileId, expectedProfile, StringComparison.Ordinal)
                    || ReferenceEquals(player, null)
                    || player == null
                    || !string.Equals(player.ProfileId?.Trim(), profileId, StringComparison.Ordinal)
                    || !seenBodies.Add(follower)
                    || !seenPlayers.Add(player)
                    || !seenAccounts.Add(accountId)
                    || !seenProfiles.Add(profileId)
                    || !registered.Any(candidate => ReferenceEquals(candidate, follower))
                    || !world.RegisteredPlayers.Any(candidate => ReferenceEquals(candidate, player))
                    || !ReferenceEquals(world.GetEverExistedPlayerByID(profileId), player))
                {
                    problem = $"PitFireTeam follower {index + 1}/{TeamCompanionCount} failed the exact active body freeze";
                    return false;
                }

                profiles[index] = profileId;
            }

            if (_boundTeamFollowers.Length != 0)
            {
                if (_boundTeamFollowers.Length != TeamCompanionCount
                    || _boundTeamFollowerProfileIds.Length != TeamCompanionCount)
                {
                    problem = "the previous bound PitFireTeam follower roster was incomplete";
                    return false;
                }

                for (int index = 0; index < TeamCompanionCount; index++)
                {
                    if (!ReferenceEquals(_boundTeamFollowers[index], ordered[index])
                        || !string.Equals(
                            _boundTeamFollowerProfileIds[index],
                            profiles[index],
                            StringComparison.Ordinal))
                    {
                        problem = "a PitFireTeam follower body changed inside the same round epoch";
                        return false;
                    }
                }
            }

            _boundTeamFollowers = ordered;
            _boundTeamFollowerProfileIds = profiles;
            return true;
        }
        catch (Exception ex)
        {
            problem = "the frozen PitFireTeam follower bodies could not be frozen: "
                      + ex.GetBaseException().Message;
            return false;
        }
    }

    private bool TryValidateDeadTeamLeaderBody(
        LocalGame game,
        LocalPlayer deadPlayer,
        out string problem)
    {
        problem = string.Empty;
        if (ReferenceEquals(game, null)
            || !ReferenceEquals(game, _teamGame)
            || game.Status != GameStatus.Started
            || !Comfort.Common.Singleton<AbstractGame>.Instantiated
            || !ReferenceEquals(Comfort.Common.Singleton<AbstractGame>.Instance, game)
            || ReferenceEquals(deadPlayer, null)
            || deadPlayer == null
            || !deadPlayer.IsYourPlayer
            || !ReferenceEquals(deadPlayer, _boundLocalPlayer)
            || !ReferenceEquals(game.LocalPlayer, deadPlayer)
            || !ReferenceEquals(game.Profile, deadPlayer.Profile)
            || deadPlayer.HealthController?.IsAlive != false
            || !string.Equals(
                deadPlayer.ProfileId?.Trim(),
                _boundLocalProfileId,
                StringComparison.Ordinal))
        {
            problem = "the active LocalGame, bound corpse, local profile or dead health state no longer matches";
            return false;
        }

        var world = Comfort.Common.Singleton<GameWorld>.Instantiated
            ? Comfort.Common.Singleton<GameWorld>.Instance
            : null;
        if (ReferenceEquals(world, null) || world == null)
        {
            problem = "GameWorld is unavailable";
            return false;
        }

        if (!ReferenceEquals(world.MainPlayer, deadPlayer))
        {
            problem = "GameWorld.MainPlayer does not reference the exact dead PMC body";
            return false;
        }

        // Player.OnDeadCoroutine unregisters the corpse on the next frame but
        // deliberately leaves MainPlayer and the ever-existed entry intact.
        // By this between-round barrier there must therefore be no active body
        // under the local profile. A replacement would register and overwrite
        // the persistent entry before Redeploy receives authorization.
        string profileId = deadPlayer.ProfileId.Trim();
        int registeredWithProfile = world.RegisteredPlayers.Count(candidate =>
            string.Equals(
                candidate?.ProfileId?.Trim(),
                profileId,
                StringComparison.Ordinal));
        if (registeredWithProfile != 0)
        {
            problem = $"GameWorld has {registeredWithProfile} active bodies under the dead local profile";
            return false;
        }

        if (!ReferenceEquals(world.GetEverExistedPlayerByID(profileId), deadPlayer))
        {
            problem = "GameWorld's persistent profile registry points at another PMC body";
            return false;
        }

        return true;
    }

    private bool TryValidateCurrentTeamLeaderBody(Player player, out string problem)
    {
        problem = string.Empty;
        if (ReferenceEquals(player, null)
            || player == null
            || !player.IsYourPlayer
            || player.HealthController?.IsAlive != true
            || string.IsNullOrWhiteSpace(_boundLocalProfileId)
            || !string.Equals(
                player.ProfileId?.Trim(),
                _boundLocalProfileId,
                StringComparison.Ordinal)
            || ReferenceEquals(_teamGame, null)
            || _teamGame.Status != GameStatus.Started
            || !Comfort.Common.Singleton<AbstractGame>.Instantiated
            || !ReferenceEquals(Comfort.Common.Singleton<AbstractGame>.Instance, _teamGame)
            || !ReferenceEquals(_teamGame.LocalPlayer, player)
            || !ReferenceEquals(_teamGame.Profile, player.Profile))
        {
            problem = "the active LocalGame, local profile or living health state no longer matches";
            return false;
        }

        return TryValidateLivingTeamLeaderWorldIdentity(player, out problem);
    }

    private static bool TryValidateLivingTeamLeaderWorldIdentity(
        Player expectedPlayer,
        out string problem)
    {
        problem = string.Empty;
        var world = Comfort.Common.Singleton<GameWorld>.Instantiated
            ? Comfort.Common.Singleton<GameWorld>.Instance
            : null;
        if (ReferenceEquals(world, null) || world == null)
        {
            problem = "GameWorld is unavailable";
            return false;
        }

        if (!ReferenceEquals(world.MainPlayer, expectedPlayer))
        {
            problem = "GameWorld.MainPlayer does not reference the exact expected PMC body";
            return false;
        }

        string profileId = expectedPlayer.ProfileId?.Trim();
        IPlayer[] registeredWithProfile = world.RegisteredPlayers
            .Where(candidate => string.Equals(
                candidate?.ProfileId?.Trim(),
                profileId,
                StringComparison.Ordinal))
            .ToArray();
        if (registeredWithProfile.Length != 1
            || !ReferenceEquals(registeredWithProfile[0], expectedPlayer))
        {
            problem = $"GameWorld has {registeredWithProfile.Length} registered bodies for the local profile instead of the exact expected body";
            return false;
        }

        if (!ReferenceEquals(world.GetEverExistedPlayerByID(profileId), expectedPlayer))
        {
            problem = "GameWorld's persistent profile registry points at another PMC body";
            return false;
        }

        return true;
    }

    private bool TryReadExpectedPitLifecycle(
        out PitFireTeamRoundLifecycleSnapshot status,
        out string problem)
    {
        status = PitFireTeamRoundLifecycle.Status;
        if (_expectedPitLifecycleEpoch <= 0
            || status.Epoch != _expectedPitLifecycleEpoch
            || status.Stage == PitFireTeamRoundLifecycleStage.Failed
            || status.Capability?.CanStartAdditionalRound != true)
        {
            problem = $"PitFireTeam lifecycle drifted during round {_teamRoundEpoch}: "
                      + $"expected epoch {_expectedPitLifecycleEpoch}, got {status.Epoch}/{status.Stage} ({status.Diagnostic})";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool HaveExactBodySet(
        IReadOnlyList<BotOwner> left,
        IReadOnlyList<BotOwner> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;

        return left.All(item => right.Any(candidate => ReferenceEquals(candidate, item)))
               && right.All(item => left.Any(candidate => ReferenceEquals(candidate, item)));
    }

    internal bool TryTakeTeamRespawnAuthorization(
        LocalGame game,
        LocalPlayer deadPlayer,
        out TeamArenaPreparedLoadoutRestore prepared,
        out int teamEpoch,
        out string reason)
    {
        prepared = null;
        teamEpoch = _teamRoundEpoch;
        reason = string.Empty;

        PitFireTeamRoundLifecycleSnapshot lifecycle = PitFireTeamRoundLifecycle.Status;
        if (!_teamArena
            || _phase != TeamPhase.Rebinding
            || _teamRebindStep != TeamRebindStep.AwaitLocalRespawn
            || !_teamRespawnAuthorizationAvailable
            || _teamRespawnAuthorizationConsumed
            || !ReferenceEquals(game, _teamGame)
            || !ReferenceEquals(deadPlayer, _authorizedDeadTeamPlayer)
            || !ReferenceEquals(game?.LocalPlayer, deadPlayer)
            || deadPlayer?.HealthController?.IsAlive != false
            || _preparedTeamRespawn == null
            || _preparedTeamRespawn.RoundEpoch != _teamRoundEpoch
            || !PitFireTeamCompat.RoundTransitionActive
            || !PitFireTeamCompat.PersistenceSuppressionActive
            || lifecycle.Stage != PitFireTeamRoundLifecycleStage.ReadyForLeader
            || lifecycle.Epoch != _expectedPitLifecycleEpoch)
        {
            reason = "the protected Zombie team respawn lease no longer matches its game, corpse, epoch or PitFireTeam lifecycle";
            return false;
        }

        if (!TryValidateDeadTeamLeaderBody(game, deadPlayer, out string bodyProblem))
        {
            reason = "the protected Zombie team corpse identity drifted before respawn authorization: "
                     + bodyProblem;
            return false;
        }

        prepared = _preparedTeamRespawn;
        _teamRespawnAuthorizationAvailable = false;
        _teamRespawnAuthorizationConsumed = true;
        return true;
    }

    private void BeginTeamRebind()
    {
        // Check the survivors once more before opening the administrative
        // teardown (which deliberately retires even living followers).
        if (_boundLocalPlayer?.HealthController?.IsAlive == false
            && _boundTeamFollowers.Length == TeamCompanionCount
            && _boundTeamFollowers.All(bot => bot != null && bot.GetPlayer != null
                && bot.HealthController?.IsAlive == false))
        {
            EndSurvival(false, "the last team member died before wave restoration began");
            return;
        }
        if (!PitFireTeamCompat.BeginRoundTransition(
                out Player oldLeader,
                out IReadOnlyList<BotOwner> oldFollowers,
                out string transitionProblem))
        {
            FailTeamTransition("PitFireTeam could not open the round transition: " + transitionProblem);
            return;
        }

        if (!ReferenceEquals(oldLeader, _boundLocalPlayer)
            || !HaveExactBodySet(oldFollowers, _boundTeamFollowers))
        {
            FailTeamTransition("PitFireTeam's teardown snapshot differed from the completed round bodies");
            return;
        }

        if (!PitFireTeamRoundLifecycle.BeginTeardown(
                oldLeader,
                oldFollowers,
                PitFireTeamCompat.CanonicalCompanionProfilesByAccountId,
                PitFireTeamCompat.PersistenceSuppressionActive,
                TeamLifecycleTimeout,
                out string teardownProblem))
        {
            FailTeamTransition(teardownProblem);
            return;
        }

        PitFireTeamRoundLifecycleSnapshot status = PitFireTeamRoundLifecycle.Status;
        if (status.Stage != PitFireTeamRoundLifecycleStage.AwaitingRetirement
            || status.Epoch <= 0)
        {
            FailTeamTransition("PitFireTeam teardown did not enter its retirement barrier");
            return;
        }

        _expectedPitLifecycleEpoch = status.Epoch;
        _teamRebindStep = TeamRebindStep.AwaitRetirement;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        Plugin.ModLogger.LogInfo(
            $"Zombie team wave {_teamRoundEpoch}: retiring the exact previous follower bodies.");
    }

    private void RebuildTeamLeader()
    {
        if (!TryReadExpectedPitLifecycle(
                out PitFireTeamRoundLifecycleSnapshot status,
                out string epochProblem))
        {
            FailTeamTransition(epochProblem);
            return;
        }

        if (status.Stage != PitFireTeamRoundLifecycleStage.ReadyForLeader)
        {
            FailTeamTransition(
                $"PitFireTeam is not ready for the next local leader (stage {status.Stage})");
            return;
        }

        if (!TryValidateCurrentTeamLeaderBody(
                _boundLocalPlayer,
                out string currentLeaderProblem))
        {
            FailTeamTransition(
                "the current local PMC drifted before PitFireTeam leader rebind: "
                + currentLeaderProblem);
            return;
        }

        BotsController botsController = Comfort.Common.Singleton<IBotGame>.Instantiated
            ? Comfort.Common.Singleton<IBotGame>.Instance?.BotsController
            : null;
        if (!PitFireTeamRoundLifecycle.TryRebuildBoss(
                _boundLocalPlayer,
                botsController,
                PitFireTeamCompat.PersistenceSuppressionActive,
                out string problem))
        {
            FailTeamTransition(problem);
            return;
        }

        _teamRebindStep = TeamRebindStep.PrepareProfiles;
        Plugin.ModLogger.LogInfo(
            $"Zombie team wave {_teamRoundEpoch}: PitFireTeam leader ready — "
            + PitFireTeamRoundLifecycle.Diagnostic);
    }

    private void BeginTeamFollowerProfiles()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        {
            FailTeamTransition(epochProblem);
            return;
        }

        if (!PitFireTeamRoundLifecycle.BeginFollowerProfilePreparation(
                PitFireTeamCompat.PersistenceSuppressionActive,
                TeamLifecycleTimeout,
                out string problem))
        {
            FailTeamTransition(problem);
            return;
        }

        _teamRebindStep = TeamRebindStep.AwaitProfiles;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        Plugin.ModLogger.LogInfo(
            $"Zombie team wave {_teamRoundEpoch}: PitFireTeam follower profile preparation is active.");
    }

    private void TickTeamFollowerProfiles()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        {
            FailTeamTransition(epochProblem);
            return;
        }

        PitFireTeamRoundPolicyDecision decision =
            PitFireTeamRoundLifecycle.PollFollowerProfilePreparation(
                PitFireTeamCompat.PersistenceSuppressionActive);
        if (decision.Decision == PitFireTeamRoundPollDecision.Failed)
            FailTeamTransition("PitFireTeam follower profile preparation failed: " + decision.Reason);
        else if (decision.Decision == PitFireTeamRoundPollDecision.Ready)
        {
            _teamRebindStep = TeamRebindStep.StartFollowers;
            Plugin.ModLogger.LogInfo(
                $"Zombie team wave {_teamRoundEpoch}: {TeamCompanionCount} fresh PitFireTeam follower profiles are ready.");
        }
    }

    private void StartTeamFollowers()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        {
            FailTeamTransition(epochProblem);
            return;
        }

        if (!PitFireTeamRoundLifecycle.TryStartFollowerSpawn(
                PitFireTeamCompat.PersistenceSuppressionActive,
                TeamFollowerAssemblyTimeout,
                out string problem))
        {
            FailTeamTransition("PitFireTeam follower spawn failed: " + problem);
            return;
        }

        _teamRebindStep = TeamRebindStep.AwaitFollowers;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        Plugin.ModLogger.LogInfo(
            $"Zombie team wave {_teamRoundEpoch}: started the fresh PitFireTeam follower spawn.");
    }

    private void TickTeamFollowers()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        {
            FailTeamTransition(epochProblem);
            return;
        }

        var world = Comfort.Common.Singleton<GameWorld>.Instantiated
            ? Comfort.Common.Singleton<GameWorld>.Instance
            : null;
        PitFireTeamRoundPolicyDecision decision = PitFireTeamRoundLifecycle.PollFollowers(
            world,
            PitFireTeamCompat.PersistenceSuppressionActive);
        if (decision.Decision == PitFireTeamRoundPollDecision.Failed)
            FailTeamTransition("PitFireTeam follower activation failed: " + decision.Reason);
        else if (decision.Decision == PitFireTeamRoundPollDecision.Ready)
        {
            _teamRebindStep = TeamRebindStep.AdoptFollowers;
            Plugin.ModLogger.LogInfo(
                $"Zombie team wave {_teamRoundEpoch}: {TeamCompanionCount} fresh follower bodies passed the stable registry barrier.");
        }
    }
}
