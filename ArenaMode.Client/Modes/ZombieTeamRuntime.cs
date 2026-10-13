using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Compat;
using ArenaMode.Kits;
using ArenaMode.Respawn;
using ArenaMode.Session;
using ArenaMode.Spectator;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Protected squad survival. ZombieMode alone owns infected/waves; this
/// coordinator admits the exact frozen authenticated followers before allowing a
/// wave, keeps the dead leader held, and rebuilds PFT only between cleared waves.
/// Surviving PMC bodies never take the solo Zombie refill/body-swap path.
/// </summary>
internal sealed partial class ZombieTeamRuntime
{
    private enum TeamPhase { Staging, Live, Rebinding, Ended }
    private enum TeamRebindStep
    {
        None, Begin, AwaitRetirement, AwaitLocalRespawn, RebuildLeader,
        PrepareProfiles, AwaitProfiles, StartFollowers, AwaitFollowers, AdoptFollowers,
    }
    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceEqualityComparer<T> Instance = new();
        public bool Equals(T left, T right) => ReferenceEquals(left, right);
        public int GetHashCode(T value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    private readonly int TeamCompanionCount;
    private const float TeamAssemblySeconds = 45f;
    private const float TeamTransitionSeconds = 45f;
    private static readonly TimeSpan TeamLifecycleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TeamFollowerAssemblyTimeout = TimeSpan.FromSeconds(20);
    private readonly ArenaMatch _match;
    private readonly LocalGame _teamGame;
    private readonly GameWorld _world;
    private Player _boundLocalPlayer;
    private readonly string _boundLocalProfileId;
    private BotOwner[] _boundTeamFollowers = Array.Empty<BotOwner>();
    private string[] _boundTeamFollowerProfileIds = Array.Empty<string>();
    private TeamArenaLoadoutSnapshot _teamLoadoutSnapshot;
    private TeamArenaPreparedLoadoutRestore _preparedTeamRespawn;
    private LocalPlayer _authorizedDeadTeamPlayer;
    private bool _teamRespawnAuthorizationAvailable;
    private bool _teamRespawnAuthorizationConsumed;
    private bool _playerDown;
    private bool _teamStopRequested;
    private bool _disposed;
    private int _teamRoundEpoch = 1;
    private int _expectedPitLifecycleEpoch;
    private TeamPhase _phase;
    private TeamRebindStep _teamRebindStep;
    private float _teamRebindDeadlineAt;
    private float _stopAt;
    private bool _noContest;
    private float _nextObservationAt;
    private bool _teamArena => IsCurrentRaid();

    internal bool IsComplete => _phase == TeamPhase.Ended;
    internal bool IsLive => IsCurrentRaid() && _phase == TeamPhase.Live;
    internal bool HoldRespawn => _phase == TeamPhase.Ended || _playerDown;
    internal bool HasFriendlyRoster => IsCurrentRaid() && _phase == TeamPhase.Live
        && _boundTeamFollowers.Length == TeamCompanionCount;
    internal Player Leader => _boundLocalPlayer;
    internal int Epoch => _teamRoundEpoch;
    internal string Status => _phase switch
    {
        TeamPhase.Staging => $"ASSEMBLING TEAM — {TeamCompanionCount} COMPANIONS",
        TeamPhase.Rebinding => "NEXT WAVE — RESTORING TEAM",
        TeamPhase.Ended => _noContest ? "TEAM SURVIVAL — NO CONTEST" : "TEAM ELIMINATED",
        _ => null,
    };

    internal IReadOnlyList<Player> LiveTeamMembers
    {
        get
        {
            var result = new List<Player>(TeamCompanionCount + 1);
            if (!HasFriendlyRoster) return result;
            if (TryReadBoundLocalPlayer(out bool alive, out _) && alive)
                result.Add(_boundLocalPlayer);
            foreach (BotOwner follower in _boundTeamFollowers)
                if (IsCurrentFriendlyMarkerBody(follower?.GetPlayer)) result.Add(follower.GetPlayer);
            return result;
        }
    }

    internal ZombieTeamRuntime(ArenaMatch match, LocalGame game, GameWorld world, Player local)
    {
        _match = match ?? throw new ArgumentNullException(nameof(match));
        _teamGame = game;
        _world = world;
        _boundLocalPlayer = local;
        _boundLocalProfileId = local?.ProfileId?.Trim();
        TeamCompanionCount = PitFireTeamCompat.FrozenCompanionCount;
        if (TeamCompanionCount < 1 || TeamCompanionCount > 4)
            throw new InvalidOperationException("Zombie team has no valid frozen companion count.");
        if (!IsCurrentRaid() || !PitFireTeamCompat.TeamArenaActive
            || !PitFireTeamCompat.PersistenceSuppressionActive
            || !TryValidateCurrentTeamLeaderBody(local, out _))
            throw new InvalidOperationException("Zombie team requires the exact protected local PMC body.");
        if (!PitFireTeamCompat.TryBindLocalPlayerBody(local, out string bindProblem))
            throw new InvalidOperationException("Zombie team leader bind failed: " + bindProblem);
        if (!TeamArenaLoadoutSnapshot.TryCapture(game.Profile, out _teamLoadoutSnapshot, out string snapshotProblem))
            throw new InvalidOperationException("Zombie team entry loadout capture failed: " + snapshotProblem);
        KitCatalog.KitProvider = null;
        _phase = TeamPhase.Staging;
        _teamRebindDeadlineAt = Time.time + TeamAssemblySeconds;
        Plugin.ModLogger.LogInfo($"Zombie team: waiting for {TeamCompanionCount} exact PitFireTeam companions; own gear and team persistence protected.");
    }

    // Called before ZombieWavePolicy. A false result must neither dispatch
    // infected nor advance its intermission clock. Wipe wins over wave-clear.
    internal bool Tick()
    {
        if (!IsCurrentRaid()) return false;
        try
        {
            if (_phase == TeamPhase.Ended)
            {
                if (Time.time >= _stopAt) EndTeamRaid();
                return false;
            }
            if (!PitFireTeamCompat.TeamArenaActive || !PitFireTeamCompat.PersistenceSuppressionActive
                || PitFireTeamCompat.FrozenCompanionCount != TeamCompanionCount)
            {
                FailTeamTransition("the protected PitFireTeam lease was lost");
                return false;
            }
            // Cheap physical wipe check remains frame-accurate; registry and
            // reflected PFT scans are bounded to four per second while live.
            if (_phase == TeamPhase.Live && _boundLocalPlayer?.HealthController?.IsAlive == false
                && _boundTeamFollowers.Length == TeamCompanionCount
                && _boundTeamFollowers.All(bot => bot != null && bot.GetPlayer != null
                    && bot.HealthController?.IsAlive == false))
            { EndSurvival(false, "all team members were eliminated"); return false; }
            if (Time.time < _nextObservationAt) return _phase == TeamPhase.Live;
            _nextObservationAt = Time.time + (_phase == TeamPhase.Live ? 0.25f : 0.1f);
            if (_phase == TeamPhase.Rebinding)
            {
                TickTeamRebinding();
                return false;
            }
            if (!TryReadBoundLocalPlayer(out bool localAlive, out string localProblem))
            {
                FailTeamTransition(localProblem);
                return false;
            }
            if (!PitFireTeamCompat.TryGetLiveFollowers(out var followers, out string rosterProblem))
            {
                FailTeamTransition(rosterProblem);
                return false;
            }
            if (_phase == TeamPhase.Staging)
            {
                if (!localAlive) { FailTeamTransition("the PMC died before the complete team was ready"); return false; }
                if (followers.Count != TeamCompanionCount)
                {
                    if (Time.time >= _teamRebindDeadlineAt)
                        FailTeamTransition($"team readiness timed out ({followers.Count}/{TeamCompanionCount} companions)");
                    return false;
                }
                if (!TryFreezeTeamFollowers(followers, out string freezeProblem))
                { FailTeamTransition(freezeProblem); return false; }
                if (!Effects.ArenaTracerTeams.TryBindCooperativeWave(_teamRoundEpoch, TeamCompanionCount,
                        _boundLocalProfileId, _boundTeamFollowerProfileIds, out string tracerProblem))
                { FailTeamTransition("team identities could not bind presentation: " + tracerProblem); return false; }
                BindFriendlyFireRoster();
                _phase = TeamPhase.Live;
                Plugin.ModLogger.LogInfo($"Zombie team wave {_teamRoundEpoch}: exact {TeamCompanionCount + 1}-player team ready.");
            }
            if (_boundTeamFollowers.Length != TeamCompanionCount
                || _boundTeamFollowers.Any(bot => bot == null || bot.GetPlayer == null || bot.HealthController == null)
                || followers.Any(bot => !_boundTeamFollowers.Any(bound => ReferenceEquals(bound, bot))))
            { FailTeamTransition("the authenticated frozen follower body roster drifted"); return false; }
            if (!localAlive && followers.Count == 0)
            {
                EndSurvival(false, "all team members were eliminated");
                return false;
            }
            var enemies = CurrentInfected();
            foreach (var enemy in enemies)
                Effects.ArenaTracerTeams.RegisterCooperativeInfected(enemy.ProfileId);
            // A wave may legitimately have no active infected yet (or any
            // left). The existing Tactical adapter rejects empty matrices.
            if (enemies.Count > 0 && !PitFireTeamCompat.EnsureHostility(enemies, _boundLocalPlayer))
            { FailTeamTransition("the live infected/team hostility matrix could not be verified"); return false; }
            _match.ReportTeamSurvivors(localAlive, followers.Count + (localAlive ? 1 : 0), enemies.Count);
            return true;
        }
        catch (Exception ex)
        {
            FailTeamTransition("team observation failed: " + ex.GetBaseException().Message);
            return false;
        }
    }

    internal void WaveCleared()
    {
        if (!IsLive) return;
        ArenaSpectatorService.Stop("Zombie wave cleared; team restore starting");
        _teamRoundEpoch++;
        _phase = TeamPhase.Rebinding;
        _teamRebindStep = TeamRebindStep.Begin;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        _preparedTeamRespawn = null;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = _teamRespawnAuthorizationConsumed = false;
        _nextObservationAt = 0f;
        Plugin.ModLogger.LogInfo($"Zombie team: wave {_teamRoundEpoch - 1} survived; restoring the {TeamCompanionCount} companions before wave {_teamRoundEpoch}.");
    }

    private void TickTeamRebinding()
    {
        if (Time.time >= _teamRebindDeadlineAt && _teamRebindStep != TeamRebindStep.AwaitRetirement)
        { FailTeamTransition("the bounded team restore window expired"); return; }
        switch (_teamRebindStep)
        {
            case TeamRebindStep.Begin: BeginTeamRebind(); break;
            case TeamRebindStep.AwaitRetirement: TickTeamRetirement(); break;
            case TeamRebindStep.AwaitLocalRespawn: break;
            case TeamRebindStep.RebuildLeader: RebuildTeamLeader(); break;
            case TeamRebindStep.PrepareProfiles: BeginTeamFollowerProfiles(); break;
            case TeamRebindStep.AwaitProfiles: TickTeamFollowerProfiles(); break;
            case TeamRebindStep.StartFollowers: StartTeamFollowers(); break;
            case TeamRebindStep.AwaitFollowers: TickTeamFollowers(); break;
            case TeamRebindStep.AdoptFollowers: AdoptTeamFollowers(); break;
            default: FailTeamTransition("the team restore step became invalid"); break;
        }
    }

    private void TickTeamRetirement()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        { FailTeamTransition(epochProblem); return; }
        var result = PitFireTeamRoundLifecycle.PollRetirement(_world);
        if (result.Decision == PitFireTeamRoundPollDecision.Failed)
        { FailTeamTransition("follower retirement failed: " + result.Reason); return; }
        if (result.Decision != PitFireTeamRoundPollDecision.Ready)
        {
            if (Time.time >= _teamRebindDeadlineAt) FailTeamTransition("old follower bodies failed to retire: " + result.Reason);
            return;
        }
        bool localDead = _playerDown || _boundLocalPlayer?.HealthController?.IsAlive == false;
        // The death hold owns the replay timeout. Do not spend the replacement
        // budget while that camera still owns the corpse.
        if (localDead && ArenaSession.ReplaySeconds > 0f && !KillcamReplayCompat.CameraReleased) return;
        if (!localDead)
        {
            if (!TryValidateCurrentTeamLeaderBody(_boundLocalPlayer, out string livingProblem))
            { FailTeamTransition(livingProblem); return; }
            _teamRebindStep = TeamRebindStep.RebuildLeader;
            _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
            return;
        }
        if (!_playerDown || _boundLocalPlayer is not LocalPlayer deadPlayer
            || !ArenaRespawnDriver.IsRespawnInProgress || _teamLoadoutSnapshot == null)
        { FailTeamTransition("the dead PMC has no held respawn pipeline or entry snapshot"); return; }
        if (!TryValidateDeadTeamLeaderBody(_teamGame, deadPlayer, out string deadProblem))
        { FailTeamTransition(deadProblem); return; }
        if (!_teamLoadoutSnapshot.TryArm(_teamRoundEpoch, out string armProblem))
        { FailTeamTransition("entry loadout arm failed: " + armProblem); return; }
        if (!_teamLoadoutSnapshot.TryPrepare(_teamGame.Profile, _teamRoundEpoch,
                out _preparedTeamRespawn, out string prepareProblem))
        { FailTeamTransition("entry loadout prepare failed: " + prepareProblem); return; }
        _authorizedDeadTeamPlayer = deadPlayer;
        _teamRespawnAuthorizationAvailable = true;
        _teamRespawnAuthorizationConsumed = false;
        _teamRebindStep = TeamRebindStep.AwaitLocalRespawn;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        // Last: only the fully prepared transaction may release the death hold.
        _playerDown = false;
    }

    private void AdoptTeamFollowers()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        { FailTeamTransition(epochProblem); return; }
        if (!TryValidateCurrentTeamLeaderBody(_boundLocalPlayer, out string leaderProblem))
        { FailTeamTransition(leaderProblem); return; }
        if (!PitFireTeamCompat.TryAdoptRoundRoster(_boundLocalPlayer, out var followers, out string adoptProblem))
        { FailTeamTransition("follower adoption failed: " + adoptProblem); return; }
        _boundTeamFollowers = Array.Empty<BotOwner>();
        _boundTeamFollowerProfileIds = Array.Empty<string>();
        if (!TryFreezeTeamFollowers(followers, out string freezeProblem))
        { FailTeamTransition(freezeProblem); return; }
        _teamRebindStep = TeamRebindStep.None;
        _phase = TeamPhase.Staging;
        _teamRebindDeadlineAt = Time.time + TeamAssemblySeconds;
    }

    internal void OnLocalDeath()
    {
        if (_playerDown || _phase == TeamPhase.Ended) return;
        _playerDown = true;
        if (_phase != TeamPhase.Live)
        { FailTeamTransition("the PMC died while the team was not combat-ready"); return; }
        BotOwner[] bodies = _boundTeamFollowers.ToArray();
        string[] profiles = _boundTeamFollowerProfileIds.ToArray();
        Player deadBody = _boundLocalPlayer;
        int epoch = _teamRoundEpoch;
        var request = new ArenaSpectatorRequest(epoch, TeamCompanionCount, () => _teamRoundEpoch,
            () => IsLive && _playerDown && epoch == _teamRoundEpoch && ReferenceEquals(deadBody, _boundLocalPlayer),
            deadBody, profiles,
            () => bodies.Select((bot, index) => new ArenaSpectatorTarget(
                bot?.GetPlayer, profiles[index], bot?.Profile?.Nickname, ArenaSpectatorSide.Ally)).ToArray(), null, cooperative: true);
        if (!ArenaSpectatorService.RequestStart(request, out string problem))
            Plugin.ModLogger.LogWarning("Zombie team spectator remained disabled: " + problem);
    }

    internal bool OnLocalRespawn(Player player)
    {
        if (_phase != TeamPhase.Rebinding || _teamRebindStep != TeamRebindStep.AwaitLocalRespawn
            || !_teamRespawnAuthorizationConsumed)
        { FailTeamTransition("an unauthorized replacement PMC appeared"); return false; }
        if (!TryValidateCurrentTeamLeaderBody(player, out string bodyProblem))
        { FailTeamTransition(bodyProblem); return false; }
        if (!PitFireTeamCompat.TryBindLocalPlayerBody(player, out string bindProblem))
        { FailTeamTransition("replacement leader bind failed: " + bindProblem); return false; }
        _boundLocalPlayer = player;
        _preparedTeamRespawn = null;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = _teamRespawnAuthorizationConsumed = false;
        _playerDown = false;
        _teamRebindStep = TeamRebindStep.RebuildLeader;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        return true;
    }

    internal bool ShouldBlockStagingDamage(Player victim)
        => IsCurrentRaid() && _phase != TeamPhase.Live;

    internal bool FailClosedStagingGuard(string reason)
    {
        if (!ShouldBlockStagingDamage(null)) return false;
        FailTeamTransition(reason);
        return true;
    }

    internal bool IsCurrentFriendlyMarkerBody(Player player)
    {
        if (!HasFriendlyRoster || player == null || player.HealthController?.IsAlive != true
            || !ReferenceEquals(_world.GetEverExistedPlayerByID(_boundLocalProfileId), _boundLocalPlayer)
            || !ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)
            || !ReferenceEquals(_world.GetEverExistedPlayerByID(player.ProfileId), player)) return false;
        for (int i = 0; i < _boundTeamFollowers.Length; i++)
        {
            var owner = _boundTeamFollowers[i];
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active
                || owner.HealthController?.IsAlive != true || !ReferenceEquals(owner.GetPlayer, player)
                || !ReferenceEquals(player.AIData?.BotOwner, owner)) continue;
            return player.ProfileId == _boundTeamFollowerProfileIds[i] && owner.ProfileId == _boundTeamFollowerProfileIds[i];
        }
        return false;
    }

    internal bool IsOpposingAwardKill(Player victim, IPlayer attacker)
    {
        if (!IsLive || victim == null || attacker == null) return false;
        bool attackerFriendly = ReferenceEquals(attacker, _boundLocalPlayer)
            || _boundTeamFollowers.Any(bot => ReferenceEquals(bot?.GetPlayer, attacker));
        bool victimFriendly = ReferenceEquals(victim, _boundLocalPlayer)
            || _boundTeamFollowers.Any(bot => ReferenceEquals(bot?.GetPlayer, victim));
        bool attackerInfected = attacker.Profile?.Info?.Settings?.Role.IsInfected() == true;
        bool victimInfected = victim.Profile?.Info?.Settings?.Role.IsInfected() == true;
        return (attackerFriendly && victimInfected) || (victimFriendly && attackerInfected);
    }

    internal bool TrySynchronizeScoreboardRoster(out string problem)
    {
        problem = null;
        if (!HasFriendlyRoster) return true; // Keep the completed lineup throughout rebind.
        var local = ArenaMatchService.SnapshotActor(_boundLocalPlayer);
        var followers = _boundTeamFollowers.Select(bot =>
            ArenaMatchService.SnapshotActor(bot?.GetPlayer, bot?.Profile?.AccountId?.Trim())).ToArray();
        if (local == null || followers.Any(actor => actor == null))
        { problem = "Zombie team scoreboard body identity unavailable"; return false; }
        // Infected history is still compactable: never pin an endless enemy roster.
        return _match.Scoreboard.TrySynchronizeRoster(_teamRoundEpoch, local, followers,
            Array.Empty<ArenaScoreboardActor>(), out problem);
    }

    private static IReadOnlyList<BotOwner> CurrentInfected()
        => Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners?
            .Where(bot => bot != null && bot.BotState == EBotState.Active && !bot.IsDead
                && bot.HealthController?.IsAlive == true && bot.Profile?.Info?.Settings?.Role.IsInfected() == true
                && bot.GetPlayer != null && bot.GetPlayer.ProfileId == bot.ProfileId
                && ReferenceEquals(Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(bot.ProfileId), bot.GetPlayer)
                && ReferenceEquals(Singleton<GameWorld>.Instance?.GetEverExistedPlayerByID(bot.ProfileId), bot.GetPlayer))
            .ToArray() ?? Array.Empty<BotOwner>();

    private bool IsCurrentRaid()
        => !_disposed && _teamGame != null && _world != null && _teamGame.Status == GameStatus.Started
            && ReferenceEquals(Singleton<AbstractGame>.Instance, _teamGame)
            && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
            && ReferenceEquals(ArenaMatchService.Current, _match)
            && ArenaSession.IsExactActiveRaid(_teamGame, _world);

    private void FailTeamTransition(string reason) => EndSurvival(true, reason);

    internal void EndNoContest(string reason)
    {
        if (IsCurrentRaid()) FailTeamTransition(reason);
    }

    private void EndSurvival(bool noContest, string reason)
    {
        if (_disposed || _phase == TeamPhase.Ended) return;
        _noContest = noContest;
        _phase = TeamPhase.Ended;
        _playerDown = true;
        _stopAt = Time.time + 3f;
        _preparedTeamRespawn = null;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = _teamRespawnAuthorizationConsumed = false;
        PitFireTeamRoundLifecycle.CancelFollowerSpawnLease("Zombie team ended: " + reason);
        ArenaSpectatorService.Stop("Zombie team ended");
        _match.ReportTextCallout(noContest ? "NO CONTEST" : "TEAM ELIMINATED");
        Plugin.ModLogger.LogInfo($"Zombie team ended after wave {_teamRoundEpoch}: {(noContest ? "no contest" : "defeat")} — {reason}.");
    }

    private void EndTeamRaid()
    {
        if (_teamStopRequested || !IsCurrentRaid()) return;
        try
        {
            ArenaRespawnDriver.StandDown = true;
            PitFireTeamCompat.BeginFinalizing();
            _teamStopRequested = true;
            var status = _boundLocalPlayer?.HealthController?.IsAlive == true ? ExitStatus.MissingInAction : ExitStatus.Killed;
            _teamGame.Stop(_boundLocalProfileId, status, "Arena", 0f);
        }
        catch (Exception ex)
        {
            if (_teamGame.Status == GameStatus.Started)
            {
                _teamStopRequested = false;
                ArenaRespawnDriver.StandDown = false;
                PitFireTeamCompat.CancelFinalizingAfterFailedStop();
                _stopAt = Time.time + 1f;
            }
            Plugin.ModLogger.LogError("Zombie protected stop failed: " + ex.GetBaseException().Message);
        }
    }

    internal void Dispose()
    {
        _disposed = true;
        _phase = TeamPhase.Ended;
        _playerDown = true;
        _preparedTeamRespawn = null;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = _teamRespawnAuthorizationConsumed = false;
        PitFireTeamRoundLifecycle.CancelFollowerSpawnLease("Zombie match ended");
        ArenaSpectatorService.Stop("Zombie match ended");
        _boundTeamFollowers = Array.Empty<BotOwner>();
        _boundTeamFollowerProfileIds = Array.Empty<string>();
    }
}
