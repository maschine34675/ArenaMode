using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Compat;
using ArenaMode.Config;
using ArenaMode.Kits;
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
    }

    /// <summary>How long the aftermath lingers before the next buy phase.</summary>
    private const float AftermathSeconds = 6f;

    /// <summary>
    /// How long a squad request is given to produce its bots before the round
    /// starts counting them. Profile generation is a server round trip, so a
    /// squad that has not arrived yet must not read as "squad wiped".
    /// </summary>
    private const float SquadGraceSeconds = 12f;

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

    private const int TeamCompanionCount = 4;
    private const int TeamEnemyCount = 5;

    private ArenaMatch _match;

    private readonly List<BotOwner> _squad = new List<BotOwner>();
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
    private string _lastTeamReadinessDiagnostic;

    public string Id => "tactical";

    public string DisplayName => "Tactical";

    /// <summary>Solo rounds keep coming; a compatible team raid stops after one result.</summary>
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

    public float DeathReplaySeconds => Settings.TacticalReplaySeconds.Value;

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
                        return _teamEnemiesRequested
                            ? $"5v5   assembling opponents {_squad.Count}/{TeamEnemyCount}"
                            : $"5v5   assembling teammates {PitFireTeamCompat.GetLiveFollowers().Count}/{TeamCompanionCount}";

                    case Phase.Live:
                        return $"5v5   Allies {AlliesAlive}/5   Enemies {SquadAlive}/5"
                            + (_playerDown ? "   YOU ARE DOWN" : string.Empty);

                    case Phase.Aftermath:
                        return $"5v5   {(_teamWon == true ? "VICTORY" : _teamWon == false ? "DEFEAT" : "NO CONTEST")}"
                            + $"   raid ending in {PhaseRemaining:0}s";
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
        _boundTeamEnemies = Array.Empty<BotOwner>();
        _boundTeamEnemyProfileIds = Array.Empty<string>();
        _boundLocalPlayer = null;
        _boundLocalProfileId = null;
        _playerDown = false;
        _roundStartPending = false;
        _teamEnemiesRequested = false;
        _teamStopRequested = false;
        _teamWon = null;
        _lastTeamReadinessDiagnostic = null;

        if (_teamArena)
        {
            // PitFireTeam 0.10.0 binds every follower to the original PMC
            // object. Replacing that body or equipment would leave its wrappers
            // pointing at a dead leader. Team Arena therefore has no buy phase
            // and never installs a kit provider.
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

            _phase = Phase.Staging;
            _phaseEndsAt = Time.time + TeamAssemblySeconds;

            ArenaToasts.Show("5v5 — assembling four teammates");
            Plugin.ModLogger.LogInfo(
                "Tactical team Arena: waiting for four PitFireTeam companions; the PMC keeps its raid loadout.");
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
                        EndTeamRaid();
                    else
                        EnterBuy();
                }
                break;
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

        // The squad is only allowed to read as "wiped" once it has had time to
        // arrive. Without this the round would end the instant it started, every
        // time, because profile generation has not come back yet.
        if (Time.time < _squadDeadlineAt && _squad.Count < _squadRequested)
            return;

        if (_squad.Count == 0)
        {
            // Nothing arrived at all within the grace period. Calling that a win
            // would hand out a free round, so it is a no-contest: back to the
            // buy phase, money untouched, round number unchanged.
            if (Time.time >= _squadDeadlineAt)
            {
                Plugin.ModLogger.LogWarning("Tactical: no squad arrived — replaying the round.");

                // Bumped so the in-flight request, if it ever lands, is retired
                // rather than joining a round that has already moved on.
                _squadEpoch++;
                EnterBuy();
            }

            return;
        }

        if (SquadAlive == 0)
            EndRound(won: true);
    }

    public void OnKill(ArenaKill kill)
    {
        if (_teamArena || _phase != Phase.Live || !kill.KilledByLocal || kill.LocalIsVictim)
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

        if (_phase != Phase.Live && _phase != Phase.Staging)
            return;

        // Set BEFORE the round is scored: HoldRespawn reads it, and the respawn
        // routine is already running by the time this returns.
        _playerDown = true;

        if (_teamArena)
        {
            // The remaining four actors decide the result. The respawn driver is
            // held until the raid itself ends; there is deliberately no leader
            // body replacement for PitFireTeam to rebind to.
            if (_phase == Phase.Staging)
                EndTeamRound(null, "the local player died before both complete teams were ready");

            return;
        }

        EndRound(won: false);
    }

    public void OnLocalRespawn()
    {
        if (_teamArena)
            return;

        // The hold is what ends a wait, not this — by the time the player is
        // standing again the next round has already started them off.
        _playerDown = false;
    }

    public void OnMatchEnd()
    {
        KitCatalog.KitProvider = null;
        _squad.Clear();
        _boundTeamEnemies = Array.Empty<BotOwner>();
        _boundTeamEnemyProfileIds = Array.Empty<string>();
        _boundLocalPlayer = null;
        _boundLocalProfileId = null;
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
            Plugin.ModLogger.LogError("Tactical team Arena refused an attempted second round.");
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
                _phase = Phase.Live;
                _phaseEndsAt = Time.time + Mathf.Max(30f, Settings.TacticalRoundSeconds.Value);
                ArenaToasts.Show("5v5 — fight!");
                Plugin.ModLogger.LogInfo("Tactical team Arena live: five allies versus five opponents.");
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
        _phaseEndsAt = Time.time + Mathf.Max(20f, SquadGraceSeconds);

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
            EndTeamRound(null, "the live PitFireTeam hostility matrix could not be restored");
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
        return _teamArena && (_phase == Phase.Staging || _phase == Phase.Aftermath);
    }

    internal bool FailClosedStagingGuard(string reason)
    {
        if (!_teamArena || (_phase != Phase.Staging && _phase != Phase.Aftermath))
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

        return true;
    }

    private void EndTeamRound(bool? won, string reason)
    {
        if (!_teamArena || (_phase != Phase.Staging && _phase != Phase.Live))
            return;

        _teamWon = won;
        _phase = Phase.Aftermath;
        _phaseEndsAt = Time.time + AftermathSeconds;
        _squadEpoch++;
        RetireSquad();

        if (won == true)
        {
            RoundsWon = 1;
            ArenaToasts.Show("5v5 victory — raid ending");
        }
        else if (won == false)
        {
            RoundsLost = 1;
            ArenaToasts.Show("5v5 defeat — raid ending");
        }
        else
        {
            ArenaToasts.Show("5v5 no contest — raid ending");
        }

        Plugin.ModLogger.LogWarning(
            $"Tactical team Arena result: {(won == true ? "win" : won == false ? "loss" : "no contest")} — {reason}. No second round will start.");
    }

    private void EndTeamRaid()
    {
        if (!_teamArena || _teamStopRequested)
            return;

        LocalGame game = null;
        try
        {
            if (!(Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame localGame)
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

            ExitStatus exitStatus = _teamWon == true
                ? ExitStatus.Survived
                : mainPlayer?.HealthController?.IsAlive == true
                    ? ExitStatus.MissingInAction
                    : ExitStatus.Killed;

            // Extend both client and server persistence guards across the delayed
            // PitFireTeam post-raid tasks before the native end sequence begins.
            ArenaMode.Respawn.ArenaRespawnDriver.StandDown = true;
            PitFireTeamCompat.BeginFinalizing();
            _teamStopRequested = true;
            game.Stop(profileId, exitStatus, "Arena", 0f);
            Plugin.ModLogger.LogInfo(
                $"Tactical team Arena ended after its single elimination round with native status {exitStatus}.");
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
        _squadDeadlineAt = Time.time + SquadGraceSeconds;

        // The epoch is what makes an abandoned request harmless. The no-contest
        // path can start a new round while the last request is still in flight —
        // profile generation is a server round trip — and without this those bots
        // would arrive onto a map the new round believes is empty.
        var epoch = ++_squadEpoch;

        var size = Mathf.Max(1, Settings.TacticalSquadSize.Value);
        _squadRequested = BurstSpawnService.SpawnSquad(
            size,
            asPmc: Settings.TacticalSquadIsPmc.Value,
            difficulty: ArenaPressure.DifficultyFor(Settings.TacticalSquadIsPmc.Value),
            observer: bot =>
            {
                if (bot == null)
                    return;

                if (epoch != _squadEpoch)
                {
                    // A superseded request. Killing rather than adopting: adopting
                    // would silently inflate a round the player was told holds a
                    // specific number of enemies.
                    RetireBot(bot, "stale solo Tactical squad member");

                    return;
                }

                if (!_squad.Contains(bot))
                    _squad.Add(bot);
            });

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
        if (!wasDown
            && Comfort.Common.Singleton<EFT.AbstractGame>.Instance is LocalGame game
            && game.Status == GameStatus.Started)
        {
            Respawn.LiveKitRespawn.AtRoundBreak(
                game,
                Settings.SpawnProtectionSeconds.Value,
                () => _phase == Phase.Live,
                null);
        }

        Plugin.ModLogger.LogInfo(
            $"Tactical round {RoundNumber}: {_squadRequested} enemies, kit '{_bought?.Id}' for {price:N0}, "
            + $"{Money:N0} left.");

        ArenaToasts.Show($"Round {RoundNumber} — {_squadRequested} enemies");
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
                health.Kill(EFT.EDamageType.Undefined);
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

        RetireSquad();

        _phase = Phase.Aftermath;
        _phaseEndsAt = Time.time + AftermathSeconds;

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
