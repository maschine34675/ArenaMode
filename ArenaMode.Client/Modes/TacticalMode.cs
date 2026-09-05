using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Kits;
using ArenaMode.Spectator;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Counter-Strike, roughly: buy a loadout, fight one squad, and if you fall you
/// sit the round out.
///
/// This mode inverts the arena's central promise on purpose. Everywhere else
/// death costs nothing and you are back before the body lands; here it costs you
/// the round, which is what gives buying a decision and a corner a meaning. Two
/// consequences run through everything below — the player is held dead until the
/// next round (HoldRespawn), and the map has to stay empty otherwise, because a
/// round that ends when the squad is dead cannot end while bots keep arriving.
///
/// The emptiness is enforced twice, on purpose. The server silences the engine's
/// pumps on the per-raid clone when the client announces this mode; the client
/// silences ours with BurstSpawnService.ManualControl. Either alone would be a
/// single point of failure for a mode whose rounds would then never end.
/// </summary>
public sealed class TacticalMode : IArenaMode
{
    public enum Phase
    {
        /// <summary>Team Arena is waiting for both complete five-player sides.</summary>
        Staging,

        /// <summary>Shopping. The squad has not spawned yet.</summary>
        Buy,

        /// <summary>The squad is out there.</summary>
        Live,

        /// <summary>Round decided; the standings are up and the next buy is counting in.</summary>
        Aftermath,

        /// <summary>Protected 5v5 is retiring and rebuilding both fixed rosters.</summary>
        Rebinding,

        /// <summary>The raid ended; no asynchronous callback may re-enter this mode.</summary>
        Ended,
    }

    private enum TeamRebindStep
    {
        None,
        Begin,
        AwaitRetirement,
        AwaitLocalRespawn,
        RebuildLeader,
        PrepareProfiles,
        AwaitProfiles,
        StartFollowers,
        AwaitFollowers,
        AdoptFollowers,
    }

    private enum TeamRetirementDecision
    {
        Pending,
        Ready,
        Invalid,
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T left, T right) => ReferenceEquals(left, right);
        public int GetHashCode(T value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    /// <summary>How long the aftermath lingers before the next buy phase.</summary>
    private const float AftermathSeconds = 6f;

    /// <summary>
    /// PitFireTeam creates its followers asynchronously after the local raid is
    /// already running. The Arena opponents are not requested until all four
    /// authenticated followers exist, so an incomplete squad can never silently
    /// turn into a smaller match.
    /// </summary>
    private const float TeamAssemblySeconds = 45f;

    /// <summary>
    /// Profile generation consumes part of the request deadline. Once all five
    /// callbacks have arrived, preserve a small independent window for EFT's
    /// normal PreActive-to-Active update instead of timing out that same frame.
    /// </summary>
    private const float TeamOpponentActivationSeconds = 5f;
    private const float TeamTransitionSeconds = 45f;
    private static readonly TimeSpan TeamLifecycleTimeout = TimeSpan.FromSeconds(30);

    private const int TeamCompanionCount = 4;
    private const int TeamEnemyCount = 5;

    private ArenaMatch _match;

    private readonly List<BotOwner> _squad = new List<BotOwner>();
    private BotOwner[] _boundTeamFollowers = Array.Empty<BotOwner>();
    private string[] _boundTeamFollowerProfileIds = Array.Empty<string>();
    private BotOwner[] _boundTeamEnemies = Array.Empty<BotOwner>();
    private string[] _boundTeamEnemyProfileIds = Array.Empty<string>();
    private Player _boundLocalPlayer;
    private string _boundLocalProfileId;

    private Phase _phase = Phase.Buy;
    private float _phaseEndsAt;
    private float _squadDeadlineAt;
    private int _squadRequested;
    private int _squadEpoch;

    private KitDefinition _bought;

    /// <summary>
    /// The kit the player is actually wearing, as opposed to one they just paid
    /// for. Keeping it is what lets a survivor be re-issued exactly what they
    /// have rather than the catalogue's default.
    /// </summary>
    private KitDefinition _worn;
    private bool _playerDown;
    private bool _roundStartPending;
    private bool _teamArena;
    private bool _teamEnemiesRequested;
    private bool _teamStopRequested;
    private bool? _teamWon;
    private bool? _teamMatchWon;
    private TeamArenaRoundContinuation _teamContinuation = TeamArenaRoundContinuation.EndNoContest;
    private int _teamRoundsToWin;
    private int _teamRoundEpoch;
    private TeamRebindStep _teamRebindStep;
    private float _teamRebindDeadlineAt;
    private int _expectedPitLifecycleEpoch;
    private LocalGame _teamGame;
    private TeamArenaLoadoutSnapshot _teamLoadoutSnapshot;
    private TeamArenaPreparedLoadoutRestore _preparedTeamRespawn;
    private LocalPlayer _authorizedDeadTeamPlayer;
    private bool _teamRespawnAuthorizationAvailable;
    private bool _teamRespawnAuthorizationConsumed;
    private string _preferredSpectatorProfileId;
    private string _lastTeamReadinessDiagnostic;

    public string Id => "tactical";

    public string DisplayName => "Tactical";

    /// <summary>Solo rounds keep coming; a protected team match ends at its configured score.</summary>
    public bool IsComplete => _teamArena && _teamStopRequested;

    /// <summary>
    /// The round is the squad. An ambient top-up would keep feeding the map bots
    /// the round is not counting, and it would never end.
    /// </summary>
    public bool ControlsOwnSpawns => true;

    public int RoundNumber { get; private set; } = 1;
    public int RoundsWon { get; private set; }
    public int RoundsLost { get; private set; }

    /// <summary>Roubles. The handbook value of a kit is what it costs, so this is a currency the player already reads.</summary>
    public int Money { get; private set; }

    public Phase CurrentPhase => _phase;

    public bool InBuyPhase => !_teamArena && _phase == Phase.Buy;

    public bool IsTeamArena => _teamArena;

    // Awards use current physical bodies, never historical tracer/profile aliases.
    internal bool IsOpposingAwardKill(Player victim, IPlayer attacker)
    {
        if (_phase != Phase.Live || victim == null || attacker == null) return false;
        if (!_teamArena)
        {
            var local = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer : null;
            return (ReferenceEquals(attacker, local) && _squad.Any(bot => bot != null && ReferenceEquals(bot.GetPlayer, victim)))
                || (ReferenceEquals(victim, local) && _squad.Any(bot => bot != null && ReferenceEquals(bot.GetPlayer, attacker)));
        }
        int victimSide = AwardTeam(victim);
        int attackerSide = AwardTeam(attacker);
        return victimSide != 0 && attackerSide != 0 && victimSide != attackerSide;
    }

    private int AwardTeam(IPlayer player)
    {
        if (player == null) return 0;
        if (ReferenceEquals(player, _boundLocalPlayer)
            && string.Equals(player.ProfileId, _boundLocalProfileId, StringComparison.Ordinal)) return 1;
        for (int i = 0; i < _boundTeamFollowers.Length && i < _boundTeamFollowerProfileIds.Length; i++)
            if (_boundTeamFollowers[i] != null && ReferenceEquals(player, _boundTeamFollowers[i].GetPlayer)
                && string.Equals(player.ProfileId, _boundTeamFollowerProfileIds[i], StringComparison.Ordinal)) return 1;
        for (int i = 0; i < _boundTeamEnemies.Length && i < _boundTeamEnemyProfileIds.Length; i++)
            if (_boundTeamEnemies[i] != null && ReferenceEquals(player, _boundTeamEnemies[i].GetPlayer)
                && string.Equals(player.ProfileId, _boundTeamEnemyProfileIds[i], StringComparison.Ordinal)) return 2;
        return 0;
    }

    internal bool IsTeamStaging => _teamArena && _phase == Phase.Staging;

    public float PhaseRemaining => Mathf.Max(0f, _phaseEndsAt - Time.time);

    /// <summary>What the player will be handed when the round starts.</summary>
    public KitDefinition Bought => _bought;

    /// <summary>
    /// Whether buying nothing means walking into the round with what you have.
    ///
    /// True for a player who survived the last round: the gear is still on them
    /// and nothing will replace it. False for one who died — they have no gear,
    /// so the round start hands them the best they can afford.
    /// </summary>
    public bool KeepsCurrentGear => !_playerDown;

    /// <summary>Squad members still standing. Counted live — bots die to each other too.</summary>
    public int SquadAlive
    {
        get
        {
            var alive = 0;
            var actors = _teamArena && _boundTeamEnemies.Length == TeamEnemyCount
                ? (IReadOnlyList<BotOwner>)_boundTeamEnemies
                : _squad;
            foreach (var bot in actors)
            {
                if (bot != null && bot.HealthController != null && bot.HealthController.IsAlive)
                    alive++;
            }

            return alive;
        }
    }

    /// <summary>The local PMC plus every surviving authenticated PitFireTeam follower.</summary>
    public int AlliesAlive
    {
        get
        {
            if (!_teamArena)
                return 0;

            var alive = 0;
            try
            {
                if (TryReadBoundLocalPlayer(out var localAlive, out _) && localAlive)
                    alive++;
            }
            catch (Exception)
            {
                // A world transitioning to the result screen simply contributes
                // no living local player to the last HUD frame.
            }

            foreach (var follower in PitFireTeamCompat.GetLiveFollowers())
            {
                if (follower?.HealthController?.IsAlive == true)
                    alive++;
            }

            return alive;
        }
    }

    public float DeathReplaySeconds
        => _teamArena && !ArenaSpectatorService.OptionalKillcamReplayMayRun
            ? 0f
            : Settings.TacticalReplaySeconds.Value;

    /// <summary>
    /// Held from the moment the player goes down until the next round starts.
    /// The respawn routine waits on this before it picks a kit, which is what
    /// lets the buy menu decide what the player comes back holding.
    /// </summary>
    public bool HoldRespawn => _playerDown;

    public string HudStatusLine
    {
        get
        {
            if (_teamArena)
            {
                switch (_phase)
                {
                    case Phase.Staging:
                        return $"5v5   Round {RoundNumber}   {RoundsWon}-{RoundsLost}   "
                            + (_teamEnemiesRequested
                                ? $"assembling opponents {_squad.Count}/{TeamEnemyCount}"
                                : $"assembling teammates {PitFireTeamCompat.GetLiveFollowers().Count}/{TeamCompanionCount}");

                    case Phase.Live:
                        return $"5v5   Round {RoundNumber}   {RoundsWon}-{RoundsLost}   "
                            + $"Allies {AlliesAlive}/5   Enemies {SquadAlive}/5"
                            + (_playerDown ? "   YOU ARE DOWN" : string.Empty);

                    case Phase.Aftermath:
                        return $"5v5   Round {RoundNumber}   {RoundsWon}-{RoundsLost}   "
                            + $"{(_teamWon == true ? "VICTORY" : _teamWon == false ? "DEFEAT" : "NO CONTEST")}   "
                            + (_teamContinuation == TeamArenaRoundContinuation.RebindNextRound
                                ? $"next round in {PhaseRemaining:0}s"
                                : $"raid ending in {PhaseRemaining:0}s");

                    case Phase.Rebinding:
                        return $"5v5   Round {RoundNumber}   {RoundsWon}-{RoundsLost}   "
                            + $"rebuilding teams ({DescribeTeamRebindStep()})";
                }
            }

            var line = $"Round {RoundNumber}   {RoundsWon}-{RoundsLost}   {Money:N0} ₽";

            switch (_phase)
            {
                case Phase.Staging:
                    break;

                case Phase.Buy:
                    line += $"   BUY  {PhaseRemaining:0}s";

                    // Says what you will actually walk out with, which is not the
                    // same question as what you bought: buying nothing means
                    // keeping your gear if you lived, and being handed the best
                    // you can afford if you did not.
                    if (_bought != null)
                        line += $"   [{_bought.Name ?? _bought.Id}]";
                    else if (_playerDown)
                        line += "   [nothing bought yet]";
                    else
                        line += "   [keeping your gear]";
                    break;

                case Phase.Live:
                    line += _playerDown ? "   DOWN" : $"   {SquadAlive} left";
                    break;

                case Phase.Aftermath:
                    line += $"   next round in {PhaseRemaining:0}s";
                    break;
            }

            return line;
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        _match = match;
        _teamArena = PitFireTeamCompat.TeamArenaActive;

        Money = Settings.TacticalStartMoney.Value;
        RoundNumber = 1;
        RoundsWon = 0;
        RoundsLost = 0;
        _squad.Clear();
        _boundTeamFollowers = Array.Empty<BotOwner>();
        _boundTeamFollowerProfileIds = Array.Empty<string>();
        _boundTeamEnemies = Array.Empty<BotOwner>();
        _boundTeamEnemyProfileIds = Array.Empty<string>();
        _boundLocalPlayer = null;
        _boundLocalProfileId = null;
        _playerDown = false;
        _roundStartPending = false;
        _teamEnemiesRequested = false;
        _teamStopRequested = false;
        _teamWon = null;
        _teamMatchWon = null;
        _teamContinuation = TeamArenaRoundContinuation.EndNoContest;
        _teamRoundsToWin = 1;
        _teamRoundEpoch = 1;
        _teamRebindStep = TeamRebindStep.None;
        _teamRebindDeadlineAt = 0f;
        _expectedPitLifecycleEpoch = 0;
        _teamGame = null;
        _teamLoadoutSnapshot = null;
        _preparedTeamRespawn = null;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = false;
        _teamRespawnAuthorizationConsumed = false;
        _preferredSpectatorProfileId = null;
        _lastTeamReadinessDiagnostic = null;
        TeamArenaPursuit.Reset();
        ArenaSpectatorService.Stop("Tactical match start");

        if (_teamArena)
        {
            // PitFireTeam 0.10.0 binds every live-round follower to one exact PMC
            // object. Team Arena therefore has no ordinary buy/kit replacement
            // path; a dead leader is replaced only after the guarded PFT teardown,
            // using the process-local entry snapshot below.
            Money = 0;
            _bought = null;
            _worn = null;
            KitCatalog.KitProvider = null;

            var world = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance
                : null;
            _boundLocalPlayer = world?.MainPlayer;
            _boundLocalProfileId = _boundLocalPlayer?.ProfileId?.Trim();
            if (ReferenceEquals(_boundLocalPlayer, null)
                || _boundLocalPlayer == null
                || string.IsNullOrWhiteSpace(_boundLocalProfileId))
            {
                throw new InvalidOperationException(
                    "protected 5v5 could not bind the exact local PMC body and profile");
            }

            if (!PitFireTeamCompat.TryBindLocalPlayerBody(_boundLocalPlayer, out var leaderProblem))
            {
                throw new InvalidOperationException(
                    "protected 5v5 could not bind PitFireTeam to the exact local PMC body: "
                    + leaderProblem);
            }

            if (!(Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame teamGame)
                || teamGame.Status != GameStatus.Started
                || !ReferenceEquals(teamGame.LocalPlayer, _boundLocalPlayer))
            {
                throw new InvalidOperationException(
                    "protected 5v5 could not bind the exact active LocalGame and PMC body");
            }

            if (!TeamArenaLoadoutSnapshot.TryCapture(
                    teamGame.Profile,
                    out _teamLoadoutSnapshot,
                    out var loadoutProblem))
            {
                throw new InvalidOperationException(
                    "protected 5v5 could not capture the process-local entry loadout: "
                    + loadoutProblem);
            }

            _teamGame = teamGame;
            _teamRoundsToWin = Mathf.Clamp(Settings.TacticalTeamRoundsToWin.Value, 1, 9);

            _phase = Phase.Staging;
            _phaseEndsAt = Time.time + TeamAssemblySeconds;

            ArenaToasts.Show("5v5 — assembling four teammates");
            Plugin.ModLogger.LogInfo(
                $"Tactical team Arena: first to {_teamRoundsToWin}; waiting for four PitFireTeam companions; "
                + "the PMC entry loadout is protected in process memory.");
            return;
        }

        _bought = Affordable();

        // Every respawn and every round-start swap asks this, so the player is
        // always handed what they bought rather than the config's selected kit.
        // GetBaseKit, not GetSelectedKit: the latter asks the provider, so this
        // lambda would have called itself for as long as _bought was null — an
        // unbounded recursion, and a StackOverflowException is not catchable.
        // _worn before the base kit: a survivor who bought nothing must be
        // re-issued the kit they are standing in, not the catalogue's first
        // entry. GetBaseKit remains the floor for the opening round, when
        // nothing has been worn yet.
        KitCatalog.KitProvider = () => _bought ?? _worn ?? KitCatalog.GetBaseKit();

        EnterBuy();
        Plugin.ModLogger.LogInfo($"Tactical: match started with {Money:N0} roubles.");
    }

    public void Tick(float deltaTime)
    {
        if (_match == null)
            return;

        switch (_phase)
        {
            case Phase.Staging:
                TickTeamStaging();
                break;

            case Phase.Buy:
                if (Time.time >= _phaseEndsAt)
                    StartRound();
                break;

            case Phase.Live:
                TickLive();
                break;

            case Phase.Aftermath:
                if (Time.time >= _phaseEndsAt)
                {
                    if (_teamArena)
                    {
                        if (_teamContinuation == TeamArenaRoundContinuation.RebindNextRound)
                            EnterTeamRebinding();
                        else
                            EndTeamRaid();
                    }
                    else
                        EnterBuy();
                }
                break;

            case Phase.Rebinding:
                TickTeamRebinding();
                break;
        }
    }

    /// <summary>
    /// Publishes only plain identities from the roster this mode already owns.
    /// PFT followers carry their authenticated account ID across replacement
    /// bodies; generated opponents deliberately remain one-profile contestants.
    /// </summary>
    internal bool TrySynchronizeScoreboardRoster(out string problem)
    {
        problem = null;
        if (_match == null)
            return true;

        // Preserve the just-finished line-up while bodies are absent. The first
        // complete snapshot of the next staging/live phase replaces it.
        if (_phase == Phase.Aftermath
            || _phase == Phase.Rebinding
            || _phase == Phase.Ended
            || (!_teamArena && _phase == Phase.Buy))
        {
            return true;
        }

        try
        {
            Player localPlayer = _teamArena
                ? _boundLocalPlayer
                : (Comfort.Common.Singleton<GameWorld>.Instantiated
                    ? Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer
                    : null);
            ArenaScoreboardActor local = ArenaMatchService.SnapshotActor(localPlayer);
            if (local == null)
            {
                problem = "Tactical local player identity is unavailable";
                return false;
            }

            var friendlies = new List<ArenaScoreboardActor>(TeamCompanionCount);
            if (_teamArena)
            {
                foreach (BotOwner follower in _boundTeamFollowers)
                {
                    string accountId = follower?.Profile?.AccountId?.Trim();
                    ArenaScoreboardActor actor = ArenaMatchService.SnapshotActor(
                        follower?.GetPlayer,
                        accountId);
                    if (actor == null || string.IsNullOrWhiteSpace(accountId))
                    {
                        problem = "a bound PitFireTeam scoreboard identity is unavailable";
                        return false;
                    }

                    friendlies.Add(actor);
                }
            }

            IReadOnlyList<BotOwner> opponentBodies = _teamArena
                                                      && _boundTeamEnemies.Length == TeamEnemyCount
                ? _boundTeamEnemies
                : _squad;
            var opponents = new List<ArenaScoreboardActor>(opponentBodies.Count);
            foreach (BotOwner opponent in opponentBodies)
            {
                ArenaScoreboardActor actor = ArenaMatchService.SnapshotActor(opponent?.GetPlayer);
                if (actor == null)
                {
                    problem = "a Tactical opponent scoreboard identity is unavailable";
                    return false;
                }

                opponents.Add(actor);
            }

            int epoch = _teamArena ? _teamRoundEpoch : RoundNumber;
            return _match.Scoreboard.TrySynchronizeRoster(
                epoch,
                local,
                friendlies,
                opponents,
                out problem);
        }
        catch (Exception ex)
        {
            problem = "Tactical scoreboard snapshot failed: " + ex.GetBaseException().Message;
            return false;
        }
    }

    private void TickLive()
    {
        if (_teamArena)
        {
            TickTeamLive();
            return;
        }

        if (_playerDown)
            return;

        var spawnDecision = SoloTacticalSpawnPolicy.Evaluate(
            _squadRequested,
            _squad.Count,
            Time.time,
            _squadDeadlineAt);
        if (spawnDecision == SoloTacticalSpawnDecision.Waiting)
            return;

        if (spawnDecision == SoloTacticalSpawnDecision.Retry)
        {
            RequestSoloSquad(retry: true);
            return;
        }

        // The safety net, and it has to be checked or it is decoration: a squad
        // member stuck somewhere the player cannot reach would otherwise hold
        // the round — and therefore the match — open forever. Scored as a loss,
        // because the round was not won.
        if (Time.time >= _phaseEndsAt)
        {
            Plugin.ModLogger.LogWarning(
                $"Tactical: round {RoundNumber} ran out of time with {SquadAlive} enemies left.");
            EndRound(won: false);
            return;
        }

        if (SquadAlive == 0)
            EndRound(won: true);
    }

    public void OnKill(ArenaKill kill)
    {
        if (_teamArena)
        {
            if (_phase == Phase.Live && kill?.LocalIsVictim == true)
                _preferredSpectatorProfileId = kill.KillerProfileId?.Trim();
            return;
        }

        if (_phase != Phase.Live || !kill.KilledByLocal || kill.LocalIsVictim)
            return;

        Money += Settings.TacticalKillReward.Value;
    }

    public void OnLocalDeath()
    {
        if (_playerDown)
            return;

        // The 5v5 result is already fixed during Aftermath, but the native raid
        // does not stop until its short result presentation ends. A death in
        // that window must keep the respawn coroutine parked so it cannot replace
        // PFT's leader body or issue a second native Killed stop.
        if (_teamArena && _phase == Phase.Aftermath)
        {
            _playerDown = true;
            return;
        }

        if (_teamArena && _phase == Phase.Rebinding)
        {
            _playerDown = true;
            FailTeamTransition("the local PMC died during the protected round rebuild");
            return;
        }

        if (_phase != Phase.Live && _phase != Phase.Staging)
            return;

        // Set BEFORE the round is scored: HoldRespawn reads it, and the respawn
        // routine is already running by the time this returns.
        _playerDown = true;

        if (_teamArena)
        {
            if (_phase == Phase.Staging)
                EndTeamRound(null, "the local player died before both complete teams were ready");
            else
                TryStartTeamSpectator();

            return;
        }

        EndRound(won: false);
    }

    public void OnLocalRespawn(Player player)
    {
        if (_teamArena)
        {
            try
            {
                if (_phase != Phase.Rebinding
                    || _teamRebindStep != TeamRebindStep.AwaitLocalRespawn
                    || !_teamRespawnAuthorizationConsumed)
                {
                    FailTeamTransition("an unrequested local replacement body appeared during protected 5v5");
                    return;
                }

                if (!TryValidateCurrentTeamLeaderBody(player, out string bodyProblem))
                {
                    FailTeamTransition(
                        "the respawn pipeline did not publish the exact expected local PMC body: "
                        + bodyProblem);
                    return;
                }

                if (!PitFireTeamCompat.TryBindLocalPlayerBody(player, out var bindProblem))
                {
                    FailTeamTransition("PitFireTeam rejected the replacement local PMC body: " + bindProblem);
                    return;
                }

                _boundLocalPlayer = player;
                _preparedTeamRespawn = null;
                _authorizedDeadTeamPlayer = null;
                _teamRespawnAuthorizationAvailable = false;
                _teamRespawnAuthorizationConsumed = false;
                _playerDown = false;
                _teamRebindStep = TeamRebindStep.RebuildLeader;
                _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
                Plugin.ModLogger.LogInfo(
                    $"Tactical team Arena round {RoundNumber}: exact replacement PMC body is ready for PitFireTeam rebind.");
            }
            catch (Exception ex)
            {
                FailTeamTransition(
                    "the replacement local PMC body could not be verified: "
                    + ex.GetBaseException().Message);
            }
            return;
        }

        // The hold is what ends a wait, not this — by the time the player is
        // standing again the next round has already started them off.
        _playerDown = false;
    }

    /// <summary>
    /// One-shot hand-off to ArenaRespawnDriver. The detached item graph was
    /// already built while the corpse owner still existed; the driver may only
    /// commit that exact prepared transaction from Redeploy's equipment hook.
    /// </summary>
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
            || _phase != Phase.Rebinding
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
            reason = "the protected Tactical respawn lease no longer matches its game, corpse, epoch or PitFireTeam lifecycle";
            return false;
        }

        if (!TryValidateDeadTeamLeaderBody(game, deadPlayer, out string bodyProblem))
        {
            reason = "the protected Tactical corpse identity drifted before respawn authorization: "
                     + bodyProblem;
            return false;
        }

        prepared = _preparedTeamRespawn;
        _teamRespawnAuthorizationAvailable = false;
        _teamRespawnAuthorizationConsumed = true;
        return true;
    }

    public void OnMatchEnd()
    {
        // Invalidate the mode-local observer before releasing any body arrays.
        // A profile request started during Staging can complete after GameEnd;
        // its callback must see both a different epoch and a terminal phase.
        _squadEpoch++;
        _phase = Phase.Ended;
        PitFireTeamRoundLifecycle.CancelFollowerSpawnLease("Tactical match ended");
        KitCatalog.KitProvider = null;
        ArenaSpectatorService.Stop("Tactical match ended");
        TeamArenaPursuit.Reset();
        _squad.Clear();
        _boundTeamFollowers = Array.Empty<BotOwner>();
        _boundTeamFollowerProfileIds = Array.Empty<string>();
        _boundTeamEnemies = Array.Empty<BotOwner>();
        _boundTeamEnemyProfileIds = Array.Empty<string>();
        _boundLocalPlayer = null;
        _boundLocalProfileId = null;
        _teamGame = null;
        _teamLoadoutSnapshot = null;
        _preparedTeamRespawn = null;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = false;
        _teamRespawnAuthorizationConsumed = false;
        _teamRebindStep = TeamRebindStep.None;
        _expectedPitLifecycleEpoch = 0;
        _preferredSpectatorProfileId = null;
        _lastTeamReadinessDiagnostic = null;

        Plugin.ModLogger.LogInfo(
            $"Tactical ended: {RoundsWon}-{RoundsLost} over {RoundNumber} round(s), {Money:N0} roubles left.");
    }

    /// <summary>Ends the buy phase now. The round still starts through Tick, so there is one path in.</summary>
    public void ReadyUp()
    {
        if (!_teamArena && _phase == Phase.Buy)
            _phaseEndsAt = Time.time;
    }

    /// <summary>Buy a kit. Refuses what the player cannot afford, and charges nothing until the round starts.</summary>
    public bool TryBuy(KitDefinition kit)
    {
        if (_teamArena || kit == null || _phase != Phase.Buy)
            return false;

        if (PriceOf(kit) > Money)
            return false;

        _bought = kit;
        return true;
    }

    public int PriceOf(KitDefinition kit) => kit?.Price ?? 0;

    public bool CanAfford(KitDefinition kit) => PriceOf(kit) <= Money;

    private void EnterBuy()
    {
        if (_teamArena)
        {
            Plugin.ModLogger.LogError("Tactical team Arena refused an invalid buy-phase transition.");
            EndTeamRaid();
            return;
        }

        _phase = Phase.Buy;
        _phaseEndsAt = Time.time + Mathf.Max(3f, Settings.TacticalBuySeconds.Value);
        _roundStartPending = true;

        // Nothing is pre-selected. _bought means "I chose to buy this", not "the
        // kit I will be wearing", and the difference is the whole point: a player
        // who SURVIVED keeps what they are carrying unless they deliberately
        // replace it.
        //
        // Auto-picking the best affordable kit here was a real downgrade
        // machine. Win a round with something good on your body, spend down to
        // where it is no longer affordable, and the next round handed you the
        // cheap thing you could still pay for — punishing the win.
        _bought = null;

        ArenaToasts.Show(_playerDown
            ? $"Round {RoundNumber} — buy phase"
            : $"Round {RoundNumber} — buy phase (you keep your gear)");
    }

    /// <summary>
    /// Waits for four authenticated followers before requesting opponents, then
    /// waits for all five opponents and the complete hostility matrix. Damage to
    /// either assembling side is suppressed by CombatPatches during this phase.
    /// </summary>
    private void TickTeamStaging()
    {
        if (!_teamArena)
            return;

        if (!PitFireTeamCompat.TeamArenaActive)
        {
            EndTeamRound(null, "PitFireTeam's authenticated Arena roster was lost");
            return;
        }

        if (!TryReadBoundLocalPlayer(out var localAlive, out var localProblem))
        {
            EndTeamRound(null, localProblem);
            return;
        }

        if (!localAlive)
        {
            EndTeamRound(null, "the bound local PMC died before both complete teams were ready");
            return;
        }

        if (!PitFireTeamCompat.TryGetLiveFollowers(out var followers, out var rosterProblem))
        {
            EndTeamRound(null, rosterProblem);
            return;
        }

        if (!_teamEnemiesRequested)
        {
            if (followers.Count == TeamCompanionCount)
            {
                RequestTeamEnemies();
                return;
            }

            if (Time.time >= _phaseEndsAt)
            {
                EndTeamRound(
                    null,
                    $"only {followers.Count}/{TeamCompanionCount} authenticated companions arrived");
            }

            return;
        }

        var completeRosters = followers.Count == TeamCompanionCount
            && _squad.Count == TeamEnemyCount;

        if (completeRosters)
        {
            if (!TryFreezeTeamFollowers(followers, out var followerProblem))
            {
                EndTeamRound(null, followerProblem);
                return;
            }

            var freezeDecision = TryFreezeTeamEnemies(out var enemyProblem);
            if (freezeDecision == TeamOpponentActivationDecision.Invalid)
            {
                EndTeamRound(null, enemyProblem);
                return;
            }

            if (freezeDecision == TeamOpponentActivationDecision.Pending)
            {
                NoteTeamReadinessPending(enemyProblem);
            }
            else if (PitFireTeamCompat.EnsureHostility(_boundTeamEnemies, _boundLocalPlayer))
            {
                _lastTeamReadinessDiagnostic = null;
                if (!TrySynchronizeScoreboardRoster(out string scoreboardProblem))
                {
                    // Presentation must never turn a valid protected roster
                    // into a no-contest. Keep the previous valid board instead.
                    Plugin.ModLogger.LogWarning(
                        $"Tactical team Arena round {RoundNumber}: scoreboard roster stayed unchanged: {scoreboardProblem}.");
                }

                if (!Effects.ArenaTracerTeams.TryBindTeamRound(
                        _teamRoundEpoch,
                        _boundLocalProfileId,
                        _boundTeamFollowerProfileIds,
                        _boundTeamEnemyProfileIds,
                        out string tracerProblem))
                {
                    // Tracers are cosmetic. A stale or ambiguous identity must
                    // stay native, never turn a valid protected round into a no-contest.
                    Plugin.ModLogger.LogWarning(
                        $"Tactical team Arena round {RoundNumber}: team tracers stayed native: {tracerProblem}.");
                }

                _phase = Phase.Live;
                _phaseEndsAt = Time.time + Mathf.Max(30f, Settings.TacticalRoundSeconds.Value);
                _preferredSpectatorProfileId = null;
                TeamArenaPursuit.BeginRound(_teamRoundEpoch);
                _match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
                ArenaToasts.Show("5v5 — fight!");
                Plugin.ModLogger.LogInfo(
                    $"Tactical team Arena round {RoundNumber} live: five allies versus five opponents.");
                return;
            }
            else
            {
                NoteTeamReadinessPending("the complete bidirectional hostility matrix is not ready yet");
            }
        }

        if (Time.time >= _phaseEndsAt)
        {
            var diagnostic = string.IsNullOrWhiteSpace(_lastTeamReadinessDiagnostic)
                ? string.Empty
                : $"; last check: {_lastTeamReadinessDiagnostic}";
            EndTeamRound(
                null,
                $"team readiness timed out (allies {followers.Count}/4, opponents {SquadAlive}/5, registered {_squad.Count}/5){diagnostic}");
        }
    }

    private void RequestTeamEnemies()
    {
        if (_teamEnemiesRequested)
            return;

        _teamEnemiesRequested = true;
        _squad.Clear();
        _lastTeamReadinessDiagnostic = null;
        // Profile generation itself is part of team assembly. Under profile or
        // bundle-loading pressure, the exact-five request can legitimately spend
        // more than twenty seconds before its first callback reaches the client.
        _phaseEndsAt = Time.time + TeamAssemblySeconds;

        var epoch = ++_squadEpoch;
        _squadRequested = BurstSpawnService.SpawnSquad(
            TeamEnemyCount,
            asPmc: Settings.TacticalSquadIsPmc.Value,
            difficulty: ArenaPressure.DifficultyFor(Settings.TacticalSquadIsPmc.Value),
            observer: bot =>
            {
                if (bot == null)
                    return;

                if (_squad.Contains(bot))
                    return;

                if (epoch != _squadEpoch || _phase != Phase.Staging || _squad.Count >= TeamEnemyCount)
                {
                    RetireBot(bot, "late or excess team-Arena opponent");
                    return;
                }

                _squad.Add(bot);
                if (_squad.Count == TeamEnemyCount)
                {
                    _phaseEndsAt = Mathf.Max(
                        _phaseEndsAt,
                        Time.time + TeamOpponentActivationSeconds);
                }
            },
            requireExactCount: true);

        Plugin.ModLogger.LogInfo(
            $"Tactical team Arena: requested exactly {TeamEnemyCount} opponents; spawn service planned {_squadRequested}.");

        if (_squadRequested != TeamEnemyCount)
        {
            EndTeamRound(
                null,
                $"the Arena spawn service could plan only {_squadRequested}/{TeamEnemyCount} opponents");
            return;
        }

        ArenaToasts.Show("5v5 — assembling five opponents");
    }

    private void TickTeamLive()
    {
        if (!PitFireTeamCompat.TryGetLiveFollowers(out var followers, out var rosterProblem))
        {
            EndTeamRound(null, rosterProblem);
            return;
        }

        if (_boundTeamFollowers.Length != TeamCompanionCount
            || _boundTeamFollowerProfileIds.Length != TeamCompanionCount
            || followers.Any(follower => !_boundTeamFollowers.Any(bound => ReferenceEquals(bound, follower))))
        {
            EndTeamRound(null, "the frozen four-follower round roster was lost");
            return;
        }

        if (!TryReadBoundLocalPlayer(out var localAlive, out var localProblem))
        {
            EndTeamRound(null, localProblem);
            return;
        }

        if (!TryValidateBoundTeamEnemies(out int enemiesAlive, out var enemyProblem))
        {
            EndTeamRound(null, enemyProblem);
            return;
        }

        // A simultaneous wipe is a loss: Arena never awards a win without a
        // surviving member of the authenticated five-player team.
        int alliesAlive = followers.Count + (localAlive ? 1 : 0);
        if (alliesAlive <= 0)
        {
            EndTeamRound(false, "the complete player team was eliminated");
            return;
        }

        if (enemiesAlive <= 0)
        {
            EndTeamRound(true, "all five Arena opponents were eliminated");
            return;
        }

        if (Time.time >= _phaseEndsAt)
        {
            EndTeamRound(false, $"round timer expired with {alliesAlive} allies and {enemiesAlive} opponents alive");
            return;
        }

        // Groups can be rebuilt by AI code after staging. Reassert and verify
        // the complete surviving matrix every tick. Once both teams still have
        // actors but mutual hostility cannot be proven, no elimination result
        // is trustworthy, so fail closed as a no-contest.
        if (!PitFireTeamCompat.EnsureHostility(_boundTeamEnemies, _boundLocalPlayer))
        {
            EndTeamRound(null, "the live PitFireTeam hostility matrix could not be restored");
            return;
        }

        _match.ReportTeamSurvivors(localAlive, alliesAlive, enemiesAlive);
        if (_playerDown)
            TeamArenaPursuit.Tick(_teamRoundEpoch, true, followers, _boundTeamEnemies);
    }

    private void TryStartTeamSpectator()
    {
        if (!Settings.TacticalTeamSpectator.Value
            || _phase != Phase.Live
            || _boundTeamFollowers.Length != TeamCompanionCount
            || _boundTeamFollowerProfileIds.Length != TeamCompanionCount
            || _boundTeamEnemies.Length != TeamEnemyCount
            || _boundTeamEnemyProfileIds.Length != TeamEnemyCount)
        {
            return;
        }

        BotOwner[] followers = _boundTeamFollowers.ToArray();
        string[] followerProfiles = _boundTeamFollowerProfileIds.ToArray();
        BotOwner[] enemies = _boundTeamEnemies.ToArray();
        string[] enemyProfiles = _boundTeamEnemyProfileIds.ToArray();
        string[] allowedProfiles = followerProfiles.Concat(enemyProfiles).ToArray();
        int epoch = _teamRoundEpoch;
        Player deadPlayer = _boundLocalPlayer;

        var request = new ArenaSpectatorRequest(
            epoch,
            () => _teamRoundEpoch,
            () => _teamArena
                  && _phase == Phase.Live
                  && _playerDown
                  && _teamRoundEpoch == epoch
                  && ReferenceEquals(_boundLocalPlayer, deadPlayer),
            deadPlayer,
            allowedProfiles,
            () => BuildTeamSpectatorTargets(
                followers,
                followerProfiles,
                enemies,
                enemyProfiles),
            _preferredSpectatorProfileId);

        if (!ArenaSpectatorService.RequestStart(request, out string problem))
        {
            Plugin.ModLogger.LogWarning(
                "Tactical team Arena spectator stayed disabled for this death: " + problem);
        }
    }

    private static IReadOnlyList<ArenaSpectatorTarget> BuildTeamSpectatorTargets(
        IReadOnlyList<BotOwner> followers,
        IReadOnlyList<string> followerProfiles,
        IReadOnlyList<BotOwner> enemies,
        IReadOnlyList<string> enemyProfiles)
    {
        var targets = new List<ArenaSpectatorTarget>(TeamCompanionCount + TeamEnemyCount);
        for (int index = 0; index < TeamCompanionCount; index++)
        {
            BotOwner bot = followers[index];
            targets.Add(new ArenaSpectatorTarget(
                bot?.GetPlayer,
                followerProfiles[index],
                bot?.Profile?.Nickname,
                ArenaSpectatorSide.Ally));
        }

        for (int index = 0; index < TeamEnemyCount; index++)
        {
            BotOwner bot = enemies[index];
            targets.Add(new ArenaSpectatorTarget(
                bot?.GetPlayer,
                enemyProfiles[index],
                bot?.Profile?.Nickname,
                ArenaSpectatorSide.Opponent));
        }

        return targets;
    }

    /// <summary>
    /// PFT binds every follower wrapper to the PMC object which entered the raid.
    /// A later GameWorld.MainPlayer replacement is never a legitimate fifth ally.
    /// A demonstrably dead original may remain as a corpse; a missing, destroyed
    /// or unreadable living original ends the fixed match as no-contest.
    /// </summary>
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
                problem = "PitFireTeam's current four-account profile map is incomplete";
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
            problem = "the four PitFireTeam follower bodies could not be frozen: "
                      + ex.GetBaseException().Message;
            return false;
        }
    }

    private TeamOpponentActivationDecision TryFreezeTeamEnemies(out string problem)
    {
        problem = string.Empty;
        if (_squad.Count != TeamEnemyCount)
        {
            problem = $"only {_squad.Count}/{TeamEnemyCount} Arena opponent bodies are registered";
            return TeamOpponentActivationDecision.Pending;
        }

        try
        {
            var bodies = _squad.ToArray();
            var players = new Player[TeamEnemyCount];
            var profiles = new string[TeamEnemyCount];
            var seenBodies = new List<BotOwner>(TeamEnemyCount);
            var seenPlayers = new List<Player>(TeamEnemyCount);
            var seenProfiles = new HashSet<string>(StringComparer.Ordinal);
            string pendingProblem = null;

            for (int index = 0; index < bodies.Length; index++)
            {
                var enemy = bodies[index];
                if (ReferenceEquals(enemy, null)
                    || enemy == null
                    || seenBodies.Exists(existing => ReferenceEquals(existing, enemy)))
                {
                    problem = "Arena could not freeze five distinct opponent bodies";
                    return TeamOpponentActivationDecision.Invalid;
                }

                var player = enemy.GetPlayer;
                if (ReferenceEquals(player, null)
                    || player == null
                    || seenPlayers.Exists(existing => ReferenceEquals(existing, player)))
                {
                    problem = $"Arena opponent {index + 1}/{TeamEnemyCount} has no distinct physical player body";
                    return TeamOpponentActivationDecision.Invalid;
                }

                string profileId = enemy.ProfileId?.Trim();
                string playerProfileId = player.ProfileId?.Trim();
                if (string.IsNullOrWhiteSpace(profileId)
                    || !string.Equals(profileId, playerProfileId, StringComparison.Ordinal)
                    || !seenProfiles.Add(profileId))
                {
                    problem = $"Arena opponent {index + 1}/{TeamEnemyCount} has a missing, duplicate, or mismatched profile identity";
                    return TeamOpponentActivationDecision.Invalid;
                }

                var health = enemy.HealthController;
                var activation = TeamOpponentActivationPolicy.Evaluate(
                    playerPresent: true,
                    healthReadable: health != null,
                    alive: !enemy.IsDead && health?.IsAlive == true,
                    isPreActive: enemy.BotState == EBotState.PreActive,
                    isActive: enemy.BotState == EBotState.Active);
                if (activation == TeamOpponentActivationDecision.Invalid)
                {
                    problem = $"Arena opponent {index + 1}/{TeamEnemyCount} cannot activate "
                              + $"(state {enemy.BotState}, dead {enemy.IsDead}, health readable {health != null})";
                    return TeamOpponentActivationDecision.Invalid;
                }

                if (activation == TeamOpponentActivationDecision.Pending && pendingProblem == null)
                {
                    pendingProblem = $"opponent {index + 1}/{TeamEnemyCount} is still {enemy.BotState}";
                }

                seenBodies.Add(enemy);
                seenPlayers.Add(player);
                players[index] = player;
                profiles[index] = profileId;
            }

            if (pendingProblem != null)
            {
                problem = pendingProblem;
                return TeamOpponentActivationDecision.Pending;
            }

            var registered = Comfort.Common.Singleton<IBotGame>.Instantiated
                ? Comfort.Common.Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners?.ToArray()
                : null;
            if (registered == null)
            {
                problem = "EFT's active bot registry is not ready yet";
                return TeamOpponentActivationDecision.Pending;
            }

            for (int index = 0; index < TeamEnemyCount; index++)
            {
                if (!registered.Any(candidate => ReferenceEquals(candidate, bodies[index])))
                {
                    problem = $"opponent {index + 1}/{TeamEnemyCount} is Active but not registered in EFT's active bot list yet";
                    return TeamOpponentActivationDecision.Pending;
                }
            }

            var world = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance
                : null;
            if (ReferenceEquals(world, null) || world == null)
            {
                problem = "EFT's persistent player registry is not ready yet";
                return TeamOpponentActivationDecision.Pending;
            }

            for (int index = 0; index < TeamEnemyCount; index++)
            {
                Player everExisted = world.GetEverExistedPlayerByID(profiles[index]);
                if (ReferenceEquals(everExisted, null) || everExisted == null)
                {
                    problem = $"opponent {index + 1}/{TeamEnemyCount} is not in EFT's persistent player registry yet";
                    return TeamOpponentActivationDecision.Pending;
                }

                if (!ReferenceEquals(everExisted, players[index]))
                {
                    problem = $"Arena detected a replacement player body for opponent {profiles[index]} before live play";
                    return TeamOpponentActivationDecision.Invalid;
                }
            }

            _boundTeamEnemies = bodies;
            _boundTeamEnemyProfileIds = profiles;
            return TeamOpponentActivationDecision.Ready;
        }
        catch (Exception ex)
        {
            problem = "the five Arena opponent identities could not be inspected: "
                      + ex.GetBaseException().Message;
            return TeamOpponentActivationDecision.Invalid;
        }
    }

    private void NoteTeamReadinessPending(string problem)
    {
        if (string.IsNullOrWhiteSpace(problem)
            || string.Equals(problem, _lastTeamReadinessDiagnostic, StringComparison.Ordinal))
        {
            return;
        }

        _lastTeamReadinessDiagnostic = problem;
        Plugin.ModLogger.LogInfo($"Tactical team Arena readiness pending: {problem}.");
    }

    private bool TryValidateBoundTeamEnemies(out int alive, out string problem)
    {
        alive = 0;
        problem = string.Empty;
        try
        {
            if (_boundTeamEnemies.Length != TeamEnemyCount
                || _boundTeamEnemyProfileIds.Length != TeamEnemyCount
                || _squad.Count != TeamEnemyCount)
            {
                problem = "the frozen five-opponent roster was lost";
                return false;
            }

            var registered = Comfort.Common.Singleton<IBotGame>.Instantiated
                ? Comfort.Common.Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners?.ToArray()
                : null;
            if (registered == null)
            {
                problem = "Arena could not inspect the registered opponent bodies";
                return false;
            }

            var world = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance
                : null;
            if (ReferenceEquals(world, null) || world == null)
            {
                problem = "Arena could not inspect the persistent opponent-body registry";
                return false;
            }

            var frozenProfiles = new HashSet<string>(
                _boundTeamEnemyProfileIds,
                StringComparer.Ordinal);
            foreach (var candidate in registered)
            {
                if (ReferenceEquals(candidate, null) || candidate == null)
                    continue;

                string candidateProfile = candidate.ProfileId?.Trim();
                if (!frozenProfiles.Contains(candidateProfile))
                    continue;

                if (!_boundTeamEnemies.Any(bound => ReferenceEquals(bound, candidate)))
                {
                    problem = $"Arena detected a replacement body for opponent {candidateProfile}";
                    return false;
                }
            }

            for (int index = 0; index < TeamEnemyCount; index++)
            {
                var enemy = _boundTeamEnemies[index];
                if (ReferenceEquals(enemy, null) || enemy == null)
                {
                    problem = "a frozen Arena opponent became destroyed or unreadable";
                    return false;
                }

                Player everExisted = world.GetEverExistedPlayerByID(
                    _boundTeamEnemyProfileIds[index]);
                Player originalPlayer = enemy.GetPlayer;
                if (ReferenceEquals(everExisted, null)
                    || everExisted == null
                    || ReferenceEquals(originalPlayer, null)
                    || originalPlayer == null
                    || !ReferenceEquals(everExisted, originalPlayer))
                {
                    problem = $"Arena detected a previously registered replacement body for opponent {_boundTeamEnemyProfileIds[index]}";
                    return false;
                }

                if (!ReferenceEquals(_squad[index], enemy)
                    || !string.Equals(
                        enemy.ProfileId?.Trim(),
                        _boundTeamEnemyProfileIds[index],
                        StringComparison.Ordinal))
                {
                    problem = "a frozen Arena opponent identity changed";
                    return false;
                }

                bool demonstrablyDead = enemy.IsDead
                    || enemy.HealthController?.IsAlive == false;
                if (demonstrablyDead)
                    continue;

                if (enemy.HealthController?.IsAlive != true
                    || enemy.BotState != EBotState.Active
                    || enemy.GetPlayer == null
                    || !registered.Any(candidate => ReferenceEquals(candidate, enemy)))
                {
                    problem = $"living Arena opponent {_boundTeamEnemyProfileIds[index]} became inactive or disappeared";
                    return false;
                }

                alive++;
            }

            return true;
        }
        catch (Exception ex)
        {
            problem = "the frozen Arena opponent roster could not be verified: "
                      + ex.GetBaseException().Message;
            return false;
        }
    }

    internal bool ShouldBlockStagingDamage(Player victim)
    {
        // No combat is legitimate before all ten actors and the full hostility
        // matrix are proven. Once any result is fixed, keep all ten bodies frozen
        // through the short native-stop handoff as well. This is especially
        // important for a readiness no-contest, which never entered Live at all.
        return _teamArena
               && (_phase == Phase.Staging
                   || _phase == Phase.Aftermath
                   || _phase == Phase.Rebinding);
    }

    internal bool FailClosedStagingGuard(string reason)
    {
        if (!_teamArena
            || (_phase != Phase.Staging
                && _phase != Phase.Aftermath
                && _phase != Phase.Rebinding))
            return false;

        // Staging can still be converted to a no-contest. Aftermath already has
        // its one immutable result; a guard failure there must keep damage
        // blocked without scoring or scheduling a second result transition.
        if (_phase == Phase.Staging)
        {
            try
            {
                EndTeamRound(null, reason);
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogError(
                    $"Tactical staging no-contest transition failed, damage remains blocked: {ex}");
            }
        }
        else if (_phase == Phase.Rebinding)
        {
            FailTeamTransition(reason);
        }

        return true;
    }

    private void EndTeamRound(bool? won, string reason)
    {
        if (!_teamArena || (_phase != Phase.Staging && _phase != Phase.Live))
            return;

        ArenaSpectatorService.Stop("the protected 5v5 round ended");
        TeamArenaPursuit.Reset();
        _teamWon = won;
        TeamArenaRoundDecision decision = TeamArenaRoundPolicy.Evaluate(
            won,
            RoundsWon,
            RoundsLost,
            _teamRoundsToWin);
        RoundsWon = decision.RoundsWon;
        RoundsLost = decision.RoundsLost;
        _teamContinuation = decision.Continuation;
        _teamMatchWon = decision.Continuation switch
        {
            TeamArenaRoundContinuation.EndMatchWon => true,
            TeamArenaRoundContinuation.EndMatchLost => false,
            TeamArenaRoundContinuation.EndNoContest => null,
            _ => null,
        };
        _phase = Phase.Aftermath;
        _phaseEndsAt = Time.time + AftermathSeconds;
        _match.Scoreboard.MarkCurrentRosterInactive();
        _squadEpoch++;
        RetireSquad();
        _preferredSpectatorProfileId = null;

        if (won.HasValue)
        {
            _match.ReportAnnouncerCue(
                decision.Continuation == TeamArenaRoundContinuation.EndMatchWon ? ArenaAnnouncerCue.MatchWon
                : decision.Continuation == TeamArenaRoundContinuation.EndMatchLost ? ArenaAnnouncerCue.MatchLost
                : won.Value ? ArenaAnnouncerCue.RoundWon : ArenaAnnouncerCue.RoundLost);
        }

        string outcome = won == true ? "victory" : won == false ? "defeat" : "no contest";
        string continuation = decision.Continuation == TeamArenaRoundContinuation.RebindNextRound
            ? "next round incoming"
            : "raid ending";
        ArenaToasts.Show($"5v5 {outcome} — {RoundsWon}-{RoundsLost}, {continuation}");

        Plugin.ModLogger.LogWarning(
            $"Tactical team Arena round {RoundNumber}: {outcome} — {reason}; "
            + $"score {RoundsWon}-{RoundsLost}, continuation {decision.Continuation}.");
    }

    private void EnterTeamRebinding()
    {
        if (!_teamArena
            || _phase != Phase.Aftermath
            || _teamContinuation != TeamArenaRoundContinuation.RebindNextRound)
        {
            return;
        }

        ArenaSpectatorService.Stop("the next protected 5v5 round is rebuilding");
        TeamArenaPursuit.Reset();
        RoundNumber++;
        _teamRoundEpoch++;
        _phase = Phase.Rebinding;
        _teamRebindStep = TeamRebindStep.Begin;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;
        _teamWon = null;
        _preparedTeamRespawn = null;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = false;
        _teamRespawnAuthorizationConsumed = false;
        ArenaToasts.Show($"Round {RoundNumber} — rebuilding both teams");
    }

    private void TickTeamRebinding()
    {
        if (!_teamArena || _phase != Phase.Rebinding)
            return;

        if (!PitFireTeamCompat.TeamArenaActive
            || !PitFireTeamCompat.PersistenceSuppressionActive
            || !ReferenceEquals(
                Comfort.Common.Singleton<AbstractGame>.Instantiated
                    ? Comfort.Common.Singleton<AbstractGame>.Instance
                    : null,
                _teamGame)
            || _teamGame?.Status != GameStatus.Started)
        {
            FailTeamTransition("the protected raid or its PitFireTeam persistence guard was lost during rebind");
            return;
        }

        switch (_teamRebindStep)
        {
            case TeamRebindStep.Begin:
                BeginTeamRebind();
                break;

            case TeamRebindStep.AwaitRetirement:
                TickTeamRetirement();
                break;

            case TeamRebindStep.AwaitLocalRespawn:
                if (Time.time >= _teamRebindDeadlineAt)
                    FailTeamTransition("the exact local PMC replacement did not complete within the bounded rebind window");
                break;

            case TeamRebindStep.RebuildLeader:
                RebuildTeamLeader();
                break;

            case TeamRebindStep.PrepareProfiles:
                BeginTeamFollowerProfiles();
                break;

            case TeamRebindStep.AwaitProfiles:
                TickTeamFollowerProfiles();
                break;

            case TeamRebindStep.StartFollowers:
                StartTeamFollowers();
                break;

            case TeamRebindStep.AwaitFollowers:
                TickTeamFollowers();
                break;

            case TeamRebindStep.AdoptFollowers:
                AdoptTeamFollowers();
                break;

            default:
                FailTeamTransition("the protected round rebuild lost its monotonic step");
                break;
        }
    }

    private void BeginTeamRebind()
    {
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
            $"Tactical team Arena round {RoundNumber}: retiring the exact previous follower and opponent bodies.");
    }

    private void TickTeamRetirement()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        {
            FailTeamTransition(epochProblem);
            return;
        }

        var world = Comfort.Common.Singleton<GameWorld>.Instantiated
            ? Comfort.Common.Singleton<GameWorld>.Instance
            : null;
        PitFireTeamRoundPolicyDecision followerRetirement =
            PitFireTeamRoundLifecycle.PollRetirement(world);
        if (followerRetirement.Decision == PitFireTeamRoundPollDecision.Failed)
        {
            FailTeamTransition("PitFireTeam follower retirement failed: " + followerRetirement.Reason);
            return;
        }

        TeamRetirementDecision enemyRetirement = ObserveTeamEnemyRetirement(out string enemyProblem);
        if (enemyRetirement == TeamRetirementDecision.Invalid)
        {
            FailTeamTransition(enemyProblem);
            return;
        }

        if (followerRetirement.Decision != PitFireTeamRoundPollDecision.Ready
            || enemyRetirement != TeamRetirementDecision.Ready)
        {
            if (Time.time >= _teamRebindDeadlineAt)
            {
                string pendingReason = !string.IsNullOrWhiteSpace(enemyProblem)
                    ? enemyProblem
                    : followerRetirement.Reason;
                FailTeamTransition(
                    "old team bodies did not leave active registries: "
                    + pendingReason);
            }
            return;
        }

        Plugin.ModLogger.LogInfo(
            $"Tactical team Arena round {RoundNumber}: all nine old bot bodies left active registries.");

        bool localDead = _playerDown || _boundLocalPlayer?.HealthController?.IsAlive == false;
        if (!localDead)
        {
            if (!TryValidateCurrentTeamLeaderBody(
                    _boundLocalPlayer,
                    out string currentLeaderProblem))
            {
                FailTeamTransition(
                    "the surviving local PMC was not the exact current player body after retirement: "
                    + currentLeaderProblem);
                return;
            }

            _teamRebindStep = TeamRebindStep.RebuildLeader;
            return;
        }

        if (!_playerDown
            || _boundLocalPlayer is not LocalPlayer deadPlayer
            || !ArenaMode.Respawn.ArenaRespawnDriver.IsRespawnInProgress
            || _teamLoadoutSnapshot == null)
        {
            FailTeamTransition("the dead local PMC has no exact held respawn pipeline and loadout snapshot");
            return;
        }

        if (!TryValidateDeadTeamLeaderBody(_teamGame, deadPlayer, out string deadLeaderProblem))
        {
            FailTeamTransition(
                "the dead local PMC body drifted before loadout preparation: "
                + deadLeaderProblem);
            return;
        }

        if (!_teamLoadoutSnapshot.TryArm(_teamRoundEpoch, out string armProblem))
        {
            FailTeamTransition(
                "the entry loadout could not be prepared before respawn release: "
                + armProblem);
            return;
        }

        if (!_teamLoadoutSnapshot.TryPrepare(
                _teamGame.Profile,
                _teamRoundEpoch,
                out _preparedTeamRespawn,
                out string prepareProblem))
        {
            FailTeamTransition(
                "the entry loadout could not be prepared before respawn release: "
                + prepareProblem);
            return;
        }

        _authorizedDeadTeamPlayer = deadPlayer;
        _teamRespawnAuthorizationAvailable = true;
        _teamRespawnAuthorizationConsumed = false;
        _teamRebindStep = TeamRebindStep.AwaitLocalRespawn;
        _teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;

        // Last mutation: releasing HoldRespawn lets the already-running driver
        // consume the complete authorization on its next coroutine continuation.
        _playerDown = false;
        Plugin.ModLogger.LogInfo(
            $"Tactical team Arena round {RoundNumber}: prepared entry loadout and released the exact held PMC respawn.");
    }

    private TeamRetirementDecision ObserveTeamEnemyRetirement(out string problem)
    {
        problem = string.Empty;
        if (_boundTeamEnemies.Length != TeamEnemyCount
            || _boundTeamEnemyProfileIds.Length != TeamEnemyCount)
        {
            problem = "the completed round lost its five-opponent retirement lease";
            return TeamRetirementDecision.Invalid;
        }

        var activeOwners = Comfort.Common.Singleton<IBotGame>.Instantiated
            ? Comfort.Common.Singleton<IBotGame>.Instance?.BotsController?.Bots?.BotOwners?.ToArray()
            : null;
        var world = Comfort.Common.Singleton<GameWorld>.Instantiated
            ? Comfort.Common.Singleton<GameWorld>.Instance
            : null;
        if (activeOwners == null || ReferenceEquals(world, null) || world == null)
        {
            problem = "EFT's old-opponent retirement registries are not ready";
            return TeamRetirementDecision.Pending;
        }

        var oldProfiles = new HashSet<string>(_boundTeamEnemyProfileIds, StringComparer.Ordinal);
        for (int index = 0; index < TeamEnemyCount; index++)
        {
            BotOwner enemy = _boundTeamEnemies[index];
            if (ReferenceEquals(enemy, null))
            {
                problem = "an old opponent reference disappeared before retirement could be proven";
                return TeamRetirementDecision.Invalid;
            }

            if (activeOwners.Any(candidate => ReferenceEquals(candidate, enemy)))
            {
                problem = $"old opponent {index + 1}/{TeamEnemyCount} remains in EFT's active bot registry";
                return TeamRetirementDecision.Pending;
            }

            if (enemy != null
                && (enemy.BotState == EBotState.Active
                    || enemy.BotState == EBotState.PreActive
                    || enemy.HealthController?.IsAlive == true))
            {
                problem = $"old opponent {index + 1}/{TeamEnemyCount} still has an active physical body";
                return TeamRetirementDecision.Pending;
            }
        }

        foreach (Player registered in world.RegisteredPlayers)
        {
            if (registered != null && oldProfiles.Contains(registered.ProfileId?.Trim()))
            {
                problem = $"old opponent {registered.ProfileId} remains in GameWorld's active player registry";
                return TeamRetirementDecision.Pending;
            }
        }

        return TeamRetirementDecision.Ready;
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
            $"Tactical team Arena round {RoundNumber}: PitFireTeam leader ready — "
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
        Plugin.ModLogger.LogInfo(
            $"Tactical team Arena round {RoundNumber}: PitFireTeam follower profile preparation is active.");
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
                $"Tactical team Arena round {RoundNumber}: four fresh PitFireTeam follower profiles are ready.");
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
                TeamLifecycleTimeout,
                out string problem))
        {
            FailTeamTransition("PitFireTeam follower spawn failed: " + problem);
            return;
        }

        _teamRebindStep = TeamRebindStep.AwaitFollowers;
        Plugin.ModLogger.LogInfo(
            $"Tactical team Arena round {RoundNumber}: started the fresh PitFireTeam follower spawn.");
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
                $"Tactical team Arena round {RoundNumber}: four fresh follower bodies passed the stable registry barrier.");
        }
    }

    private void AdoptTeamFollowers()
    {
        if (!TryReadExpectedPitLifecycle(out _, out string epochProblem))
        {
            FailTeamTransition(epochProblem);
            return;
        }

        if (!TryValidateCurrentTeamLeaderBody(
                _boundLocalPlayer,
                out string currentLeaderProblem))
        {
            FailTeamTransition(
                "the current local PMC drifted before PitFireTeam follower adoption: "
                + currentLeaderProblem);
            return;
        }

        if (!PitFireTeamCompat.TryAdoptRoundRoster(
                _boundLocalPlayer,
                out IReadOnlyList<BotOwner> followers,
                out string problem))
        {
            FailTeamTransition("PitFireTeam follower adoption failed: " + problem);
            return;
        }

        _boundTeamFollowers = Array.Empty<BotOwner>();
        _boundTeamFollowerProfileIds = Array.Empty<string>();
        if (!TryFreezeTeamFollowers(followers, out string followerProblem))
        {
            FailTeamTransition("the adopted follower roster failed Arena's body freeze: " + followerProblem);
            return;
        }

        _squad.Clear();
        _boundTeamEnemies = Array.Empty<BotOwner>();
        _boundTeamEnemyProfileIds = Array.Empty<string>();
        _teamEnemiesRequested = false;
        _lastTeamReadinessDiagnostic = null;
        _teamRebindStep = TeamRebindStep.None;
        _phase = Phase.Staging;
        _phaseEndsAt = Time.time + TeamAssemblySeconds;
        ArenaToasts.Show($"Round {RoundNumber} — assembling five opponents");
        Plugin.ModLogger.LogInfo(
            $"Tactical team Arena round {RoundNumber}: four fresh PitFireTeam followers adopted; requesting opponents next.");
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

    private void FailTeamTransition(string reason)
    {
        if (!_teamArena || _teamStopRequested)
            return;

        PitFireTeamRoundLifecycle.CancelFollowerSpawnLease(
            "the protected Tactical round rebuild failed: " + reason);
        ArenaSpectatorService.Stop("the protected round rebuild failed");
        TeamArenaPursuit.Reset();
        _teamContinuation = TeamArenaRoundContinuation.EndNoContest;
        _teamMatchWon = null;
        _teamWon = null;
        _teamRebindStep = TeamRebindStep.None;
        _authorizedDeadTeamPlayer = null;
        _teamRespawnAuthorizationAvailable = false;
        _teamRespawnAuthorizationConsumed = false;
        _preparedTeamRespawn = null;
        _phase = Phase.Aftermath;
        _phaseEndsAt = Time.time + AftermathSeconds;
        _squadEpoch++;
        RetireSquad();
        ArenaToasts.Show("5v5 no contest — protected round rebuild failed");
        Plugin.ModLogger.LogError(
            $"Tactical team Arena ended no contest during round {RoundNumber} rebind: {reason}.");
    }

    private string DescribeTeamRebindStep()
        => _teamRebindStep switch
        {
            TeamRebindStep.Begin => "opening transition",
            TeamRebindStep.AwaitRetirement => "retiring old bodies",
            TeamRebindStep.AwaitLocalRespawn => "restoring PMC",
            TeamRebindStep.RebuildLeader => "binding leader",
            TeamRebindStep.PrepareProfiles => "requesting teammates",
            TeamRebindStep.AwaitProfiles => "preparing teammates",
            TeamRebindStep.StartFollowers => "starting teammates",
            TeamRebindStep.AwaitFollowers => "activating teammates",
            TeamRebindStep.AdoptFollowers => "freezing teammates",
            _ => "guarded",
        };

    private void EndTeamRaid()
    {
        if (!_teamArena || _teamStopRequested)
            return;

        LocalGame game = null;
        try
        {
            if (!Comfort.Common.Singleton<AbstractGame>.Instantiated
                || !(Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame localGame)
                || !ReferenceEquals(localGame, _teamGame)
                || localGame.Status != GameStatus.Started
                || !Comfort.Common.Singleton<GameWorld>.Instantiated)
            {
                return;
            }

            game = localGame;

            var mainPlayer = _boundLocalPlayer;
            var profileId = _boundLocalProfileId;
            if (string.IsNullOrEmpty(profileId))
                return;

            ExitStatus exitStatus = _teamMatchWon == true
                ? ExitStatus.Survived
                : mainPlayer?.HealthController?.IsAlive == true
                    ? ExitStatus.MissingInAction
                    : ExitStatus.Killed;

            // Extend both client and server persistence guards across the delayed
            // PitFireTeam post-raid tasks before the native end sequence begins.
            ArenaSpectatorService.Stop("the protected 5v5 match is ending");
            TeamArenaPursuit.Reset();
            ArenaMode.Respawn.ArenaRespawnDriver.StandDown = true;
            PitFireTeamCompat.BeginFinalizing();
            _teamStopRequested = true;
            game.Stop(profileId, exitStatus, "Arena", 0f);
            Plugin.ModLogger.LogInfo(
                $"Tactical team Arena ended {RoundsWon}-{RoundsLost} after {RoundNumber} round(s) "
                + $"with native status {exitStatus}.");
        }
        catch (Exception ex)
        {
            // BaseLocalGame.Stop commits GameStatus.Stopping before optional
            // BotsController cleanup. Once committed it cannot be retried; keep
            // finalization and death interception stood down. A pre-commit
            // failure remains Started and is safe to retry on the next Tick.
            bool committed = game != null && game.Status != GameStatus.Started;
            if (!committed)
            {
                _teamStopRequested = false;
                ArenaMode.Respawn.ArenaRespawnDriver.StandDown = false;
                PitFireTeamCompat.CancelFinalizingAfterFailedStop();
            }

            Plugin.ModLogger.LogError(
                committed
                    ? $"Tactical team Arena stop failed after EFT committed teardown; protection remains active: {ex}"
                    : $"Tactical team Arena could not end the raid yet and will retry: {ex}");
        }
    }

    /// <summary>The dearest kit the player can afford, so being broke still arms them.</summary>
    private KitDefinition Affordable()
    {
        KitDefinition best = null;
        foreach (var kit in KitCatalog.Kits ?? new List<KitDefinition>())
        {
            if (PriceOf(kit) > Money)
                continue;

            if (best == null || PriceOf(kit) > PriceOf(best))
                best = kit;
        }

        // Nothing affordable at all: hand out the cheapest anyway rather than
        // sending the player in with nothing. Being broke is a setback, not a
        // reason to stand in a corridor unarmed.
        if (best == null)
        {
            foreach (var kit in KitCatalog.Kits ?? new List<KitDefinition>())
            {
                if (best == null || PriceOf(kit) < PriceOf(best))
                    best = kit;
            }
        }

        return best;
    }

    private void StartRound()
    {
        if (!_roundStartPending)
            return;

        _roundStartPending = false;

        // THE hold is released here, and this is the only place that can do it.
        // _playerDown was cleared in OnLocalRespawn, which is raised by the tail
        // of the very respawn routine the hold parks — a circular wait that left
        // the first death in a Tactical raid permanently dead behind a black
        // screen. Captured first, because the round-break swap below still needs
        // to know whether this player survived the last round or is being handed
        // back a body.
        var wasDown = _playerDown;
        _playerDown = false;

        // A player who died has nothing to stand up in, so if they bought nothing
        // they get the best they can afford rather than a naked round. A player
        // who survived and bought nothing keeps what they are wearing — that is
        // what winning is for.
        if (wasDown && _bought == null)
            _bought = Affordable();

        // Charged now rather than at the click, so browsing the menu is free and
        // a round that never starts costs nothing.
        var price = PriceOf(_bought);
        if (price <= Money)
            Money -= price;

        _squad.Clear();
        _phase = Phase.Live;
        _phaseEndsAt = Time.time + Mathf.Max(30f, Settings.TacticalRoundSeconds.Value);
        RequestSoloSquad(retry: false);

        // A player who SURVIVED the last round is standing there in last round's
        // gear; the round-break swap puts them in what they just bought. One who
        // died is handled by the held respawn instead, which picks the same kit
        // through the same provider once HoldRespawn lets go.
        // The kit this round runs on, remembered before the swap so the NEXT
        // round can re-issue it.
        _worn = _bought ?? _worn;

        // Survivors are re-issued their kit too, not skipped. "Keeping your
        // gear" has to mean keeping the LOADOUT, not keeping the wounds and the
        // empty magazines it ended the round with — a round that starts with a
        // fractured leg and one round in the chamber is not a round. Re-applying
        // the same kit is exactly a heal and a resupply, through the path that
        // is already tested for it, and it changes nothing about the gear
        // because it is the same kit definition.
        int roundNumber = RoundNumber;
        if (!wasDown
            && Comfort.Common.Singleton<EFT.AbstractGame>.Instance is LocalGame game
            && game.Status == GameStatus.Started)
        {
            Respawn.LiveKitRespawn.AtRoundBreak(
                game,
                Settings.SpawnProtectionSeconds.Value,
                () => _phase == Phase.Live && RoundNumber == roundNumber,
                null);
        }

        Plugin.ModLogger.LogInfo(
            $"Tactical round {RoundNumber}: {_squadRequested} enemies, kit '{_bought?.Id}' for {price:N0}, "
            + $"{Money:N0} left.");

        _match.ReportAnnouncerCue(ArenaAnnouncerCue.RoundStart);
        ArenaToasts.Show($"Round {RoundNumber} — {_squadRequested} enemies");
    }

    private void RequestSoloSquad(bool retry)
    {
        if (_teamArena || _phase != Phase.Live)
            return;

        int epoch = ++_squadEpoch;
        if (retry)
        {
            Plugin.ModLogger.LogWarning(
                $"Tactical round {RoundNumber}: no opponent arrived within "
                + $"{SoloTacticalSpawnPolicy.AssemblySeconds:0}s; retrying the squad request without restarting the round.");
            _squad.Clear();
        }

        int expected = Mathf.Max(1, Settings.TacticalSquadSize.Value);

        // Publish the observer's limit before SpawnSquad. Warm profile and pool
        // tasks may complete synchronously, so its callback is allowed to run
        // before SpawnSquad returns on the first round.
        _squadRequested = expected;
        _squadDeadlineAt = SoloTacticalSpawnPolicy.Deadline(Time.time, requestDispatched: true);

        int planned = BurstSpawnService.SpawnSquad(
            expected,
            asPmc: Settings.TacticalSquadIsPmc.Value,
            difficulty: ArenaPressure.DifficultyFor(Settings.TacticalSquadIsPmc.Value),
            observer: bot =>
            {
                if (bot == null)
                    return;

                if (_squad.Contains(bot))
                    return;

                if (epoch != _squadEpoch
                    || _phase != Phase.Live
                    || _squad.Count >= expected)
                {
                    RetireBot(bot, "stale solo Tactical squad member");
                    return;
                }

                // Profile and bundle assembly must not consume the configured
                // combat duration. The first real opponent starts that clock.
                if (_squad.Count == 0)
                    _phaseEndsAt = Time.time + Mathf.Max(30f, Settings.TacticalRoundSeconds.Value);

                _squad.Add(bot);
            },
            requestStillCurrent: () => epoch == _squadEpoch && _phase == Phase.Live);

        if (planned != expected)
            _squadDeadlineAt = SoloTacticalSpawnPolicy.Deadline(Time.time, requestDispatched: false);

        Plugin.ModLogger.LogInfo(
            $"Tactical round {RoundNumber}: requested {expected} opponents; spawn service planned {planned}.");
    }

    /// <summary>
    /// Clears the map of whatever is left of the round's squad.
    ///
    /// A LOST round leaves its survivors standing, and the old code only forgot
    /// about them — so they became uncounted permanent residents hunting a
    /// player frozen in the buy menu, and the mode's "the map is empty
    /// otherwise" promise held for round one only.
    /// </summary>
    private static void RetireBot(BotOwner bot, string reason)
    {
        try
        {
            var health = bot?.GetPlayer?.ActiveHealthController;
            if (health != null && health.IsAlive)
            {
                using (ArenaAdministrativeDeathGate.Enter(bot.ProfileId))
                    health.Kill(EFT.EDamageType.Undefined);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Tactical: {reason} survived retirement: {ex.Message}");
        }
    }

    private void RetireSquad()
    {
        foreach (var bot in _squad)
            RetireBot(bot, "round squad member");

        _squad.Clear();
    }

    private void EndRound(bool won)
    {
        if (_phase != Phase.Live)
            return;

        // Invalidate detached profile/activation callbacks before retiring the
        // roster. A bot arriving during Aftermath must be killed by its observer,
        // never adopted and then forgotten by the next StartRound clear.
        _squadEpoch++;
        RetireSquad();

        _phase = Phase.Aftermath;
        _phaseEndsAt = Time.time + AftermathSeconds;
        _match.Scoreboard.MarkCurrentRosterInactive();

        _match.ReportAnnouncerCue(
            won ? ArenaAnnouncerCue.RoundWon : ArenaAnnouncerCue.RoundLost);

        if (won)
        {
            RoundsWon++;
            Money += Settings.TacticalWinBonus.Value;
            ArenaToasts.Show($"Round {RoundNumber} won  (+{Settings.TacticalWinBonus.Value:N0} ₽)");
        }
        else
        {
            RoundsLost++;
            Money += Settings.TacticalLossBonus.Value;
            ArenaToasts.Show($"Round {RoundNumber} lost  (+{Settings.TacticalLossBonus.Value:N0} ₽)");
        }

        Plugin.ModLogger.LogInfo(
            $"Tactical round {RoundNumber} {(won ? "won" : "lost")} — {RoundsWon}-{RoundsLost}, {Money:N0} roubles.");

        RoundNumber++;
    }
}
