using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Kits;
using ArenaMode.Respawn;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Endless waves of EFT's built-in infected (Halloween 2024 leftovers)
/// against the local player, or an authenticated PitFireTeam squad. Solo keeps
/// one life; protected teams survive together and restore between waves.
///
/// This class is the Unity/EFT-facing shell only: it freezes the Zombie
/// settings once, drives the pure ZombieWavePolicy every Tick with the real
/// clock and the real alive/pending counts, and translates a policy "spawn
/// one now" into a ZombieRoleMix draw and a ZombieSpawnService.TryRequest
/// call, using ZombieSpawnService's own TryMapRole/TryMapDifficulty for the
/// string-&gt;native-enum step. ZombieSpawnService is wired for real (Stage C:
/// BurstSpawnService.SpawnInfected, plus ZombieNativeEventBridge's native
/// Halloween-event hooks) — see its own doc comment for exactly how a
/// spawned zombie becomes aware of the player even when the native bridge
/// could not arm this raid.
/// </summary>
public sealed class ZombieMode : IArenaMode
{
    private const float NudgeIntervalSeconds = 5f;

    // Item 3 ("refill between waves"): bounded retry ceiling for
    // ZombieRefillPolicy — only real attempts (AtRoundBreak returning
    // false) consume this budget. A live kit handover may still be settling
    // when a wave clears; eligibility retries do not consume the budget.
    private const int RefillMaxAttempts = 20;

    // ZombieWavePolicy.Evaluate + the alive/pending recount it needs do not
    // have to run every single rendered frame to keep the wave's own pacing
    // (SpawnIntervalSeconds floors at 0.01s and is compared against Time.time
    // state the policy itself owns): polling this often instead of every
    // frame cuts a per-tick BotOwners scan and HUD string rebuild down by an
    // order of magnitude or more at a high frame rate, matching the cadence
    // every comparable scanning service in this codebase already uses
    // (BurstSpawnService.Tick, ArenaHostility.Tick, CorpseCleanupService.Tick
    // — and ScorePanel itself only reads HudStatusLine this often).
    private const float WaveEvaluateIntervalSeconds = 0.1f;

    private ArenaMatch _match;
    private LocalGame _game;
    private GameWorld _world;
    private Player _local;
    private string _localId;
    private bool _ended;
    private bool _soloDefeated;
    private bool _soloDeathCounted;
    private bool _soloDeathKillPublished;
    private bool _soloResultPanelReady;
    private bool _resultExitRequested;
    private float _soloDefeatedAt;
    private float _nextStopAttempt;
    private string _resultExitProblem;
    private string _soloNoContestReason;
    private readonly ArenaReplayWaitPolicy _resultReplay = new();

    // Frozen at OnMatchStart; never re-read from Settings afterward (repo
    // convention — see TeamDeathmatchMode/RoundsMode for the same pattern).
    // A live F12 edit mid-raid must not retarget a wave already in flight.
    private ZombieWavePolicy.Config _config;
    private int _shooterPercent;

    private ZombieWavePolicy.State _waveState;
    private System.Random _rng; // Unity also has a Random type in scope here — must stay fully qualified.
    private int _lastAnnouncedWave;
    private int _zombiesKilled;
    private float _nextNudgeAt;
    private float _nextWaveEvaluateAt;
    private bool _openingCountdownStarted;

    // Item 3 ("refill between waves", random-kit mode). _currentKit is kept
    // current by OnKitApplied below — raised after the raid-start swap,
    // this refill itself — so it is always "whatever the player is wearing right
    // now", never a fresh draw taken here. See ZombieLoadoutRefill's own doc
    // comment for exactly how that is handed to LiveKitRespawn.AtRoundBreak
    // without touching KitCatalog.KitProvider for anything but the width of
    // that one call. Stays null and unread when _ownGear is true below: own
    // gear never raises OnKitApplied (it is not a kit — see
    // LiveKitRespawn.SwapRoutine's own doc comment on that exclusion).
    private ZombieRefillPolicy.State _refillState;
    private KitDefinition _currentKit;

    // Item 4 ("own protected gear"), frozen at OnMatchStart like _config and
    // _shooterPercent above: whether this raid restores the player's real
    // entry loadout (ArenaSession.ZombieOwnGearActive) instead of drawing
    // Arena kits. Read once here rather than live every tick for the same
    // reason _config is frozen — ArenaSession.ZombieOwnGearSelection itself
    // cannot change mid-raid anyway (frozen at arm), but caching it keeps
    // this class's own "frozen settings" convention consistent and avoids a
    // property-chain read on every refill decision.
    private bool _ownGear;
    private ZombieTeamRuntime _team;
    private bool _teamIntermissionRestored;

    // Cached from the latest Tick for HudStatusLine, which must stay cheap —
    // ScorePanel reads it up to four times a second.
    private ZombieWavePolicy.Phase _phase;
    private int _waveNumber;
    private string _waveStatusText = "ZOMBIES";

    public string Id => ArenaModeCatalog.ZombieId;
    public string DisplayName => "Zombies";

    /// <summary>Solo ends on the first death; a protected squad finishes after a wipe or unsafe transition.</summary>
    public bool IsComplete => _soloDefeated || _team?.IsComplete == true;

    public float DeathReplaySeconds => IsTeamArena && !Spectator.ArenaSpectatorService.OptionalKillcamReplayMayRun
        ? 0f : ArenaReplaySelection.Seconds;

    /// <summary>Solo never replaces a dead body; protected squads restore after a cleared wave.</summary>
    public bool HoldRespawn => _team == null || _team.HoldRespawn;

    internal bool HasResult => _soloDefeated;
    // Presentation only: terminal ownership and the death hold are immediate.
    // ArenaPanel reads this cached Tick decision without probing camera APIs.
    internal bool ResultPanelReady => HasResult && _soloResultPanelReady;
    internal bool ResultOwnsCurrentRaid => HasResult && IsCurrentRaid()
        && ReferenceEquals(ArenaMatchService.Current, _match);
    internal string ResultHeading => _soloNoContestReason == null ? "SURVIVAL ENDED" : "NO CONTEST";
    internal string ResultDetail => _soloNoContestReason ?? $"Wave {_waveNumber} | {_zombiesKilled} zombie kills";
    internal string ResultExitStatus => _resultExitProblem ?? (ArenaSession.ReplaySeconds > 0f
        ? "Returning to menu after the death replay…" : "Returning to menu…");
    internal bool TryRecordLocalDeath()
    {
        if (_team != null) return true;
        if (_soloDeathCounted || !IsCurrentRaid() || HasResult) return false;
        _soloDeathCounted = true;
        return true;
    }
    internal bool TryPublishLocalDeathKill()
    {
        if (_team != null || _soloDeathKillPublished || !IsCurrentRaid()) return false;
        _soloDeathKillPublished = true;
        return true;
    }

    internal void RequestResultExit() { if (ResultOwnsCurrentRaid) _resultExitRequested = true; }
    internal void ReportResultExitProblem(string problem)
    {
        if (!ResultOwnsCurrentRaid || string.Equals(problem, _resultExitProblem, StringComparison.Ordinal)) return;
        _resultExitProblem = problem;
        Plugin.ModLogger.LogWarning("Zombie survival result: " + problem);
    }

    /// <summary>Zombie owns its entire population (see ArenaPopulationPolicy); no ambient top-up.</summary>
    public bool ControlsOwnSpawns => true;

    /// <summary>Team assembly/restoration/result phases do not admit combat rewards.</summary>
    internal bool AssistScoringOpen => IsCurrentRaid() && !IsComplete && (_team == null || _team.IsLive);
    internal bool IsSpawnAdmissionOpen => IsCurrentRaid() && !IsComplete;
    internal bool IsTeamArena => _team != null;
    // Polling active follower identities after a confirmed terminal result can
    // misclassify normal dead-body disposal as an unauthorized replacement.
    internal bool TeamResultOwnsCurrentRaid => _team?.IsComplete == true && IsCurrentRaid()
        && ReferenceEquals(ArenaMatchService.Current, _match);
    internal bool HasFriendlyRoster => _team?.HasFriendlyRoster == true;
    internal System.Collections.Generic.IReadOnlyList<Player> LiveTeamMembers
        => _team?.LiveTeamMembers ?? Array.Empty<Player>();
    internal bool IsCurrentFriendlyMarkerBody(Player player) => _team?.IsCurrentFriendlyMarkerBody(player) == true;
    internal bool IsOpposingAwardKill(Player victim, IPlayer attacker) => _team?.IsOpposingAwardKill(victim, attacker) == true;
    internal bool ShouldBlockFriendlyDamage(Player victim, IPlayer attacker)
        => ReferenceEquals(ArenaMatchService.Current?.Mode, this)
            && _team?.ShouldBlockFriendlyDamage(victim, attacker) == true;
    internal bool ShouldBlockStagingDamage(Player victim) => _team?.ShouldBlockStagingDamage(victim) == true;
    internal bool FailClosedStagingGuard(string reason) => _team?.FailClosedStagingGuard(reason) == true;
    internal bool TrySynchronizeTeamScoreboard(out string problem)
    {
        problem = null;
        return _team == null || _team.TrySynchronizeScoreboardRoster(out problem);
    }
    internal bool TryTakeTeamRespawnAuthorization(LocalGame game, LocalPlayer deadPlayer,
        out TeamArenaPreparedLoadoutRestore prepared, out int epoch, out string reason)
    {
        prepared = null;
        epoch = 0;
        reason = "the active Zombie match does not own a protected team";
        return _team != null && _team.TryTakeTeamRespawnAuthorization(game, deadPlayer, out prepared, out epoch, out reason);
    }

    /// <summary>Zombies the LOCAL player has killed this raid. Not shown in HudStatusLine today;
    /// kept for a future HUD and for diagnostics (decision 4: "OnKill counts infected victims").</summary>
    internal int ZombiesKilled => _zombiesKilled;

    public string HudStatusLine
    {
        get
        {
            if (!IsCurrentRaid() || _match == null)
                return "ZOMBIES";
            if (_team?.Status != null) return _team.Status;
            if (HasResult) return ResultHeading + " | " + ResultDetail;

            if (_phase == ZombieWavePolicy.Phase.Intermission)
                return _waveStatusText;

            float elapsed = _match.Elapsed;
            return $"{_waveStatusText} | {Mathf.FloorToInt(elapsed / 60f):00}:{Mathf.FloorToInt(elapsed % 60f):00}";
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        if (_match != null)
            throw new InvalidOperationException("A Zombie instance belongs to one raid.");

        _match = match ?? throw new ArgumentNullException(nameof(match));
        _game = Singleton<AbstractGame>.Instance as LocalGame;
        _world = Singleton<GameWorld>.Instance;
        _local = _world?.MainPlayer;
        _localId = _game?.ProfileId;
        if (!IsCurrentRaid() || string.IsNullOrWhiteSpace(_localId) || _local?.ProfileId != _localId)
            throw new InvalidOperationException("Zombie requires the exact active local raid and player identity.");

        // WaveStartSize/ShooterPercent: read the exact same snapshot the
        // native opening-wave preload used (ZombieNativeEventBridge, hook 3),
        // when one exists, rather than reading Settings a second time here —
        // a supported mid-loading F12 edit must not desync the two. Falls
        // back to a live read only when that hook never captured one this
        // raid (it failed to install, or this raid somehow started without
        // IsArmedZombieLoading ever being true for it).
        if (!ZombieNativeEventBridge.TryGetOpeningSnapshot(out int waveStartSize, out int shooterPercent))
        {
            waveStartSize = Settings.ZombieWaveStartSize.Value;
            shooterPercent = Settings.ZombieShooterPercent.Value;
        }

        _config = new ZombieWavePolicy.Config
        {
            MaxAlive = Settings.ZombieMaxAlive.Value,
            WaveStartSize = waveStartSize,
            WaveGrowth = Settings.ZombieWaveGrowth.Value,
            IntermissionSeconds = Settings.ZombieIntermissionSeconds.Value,
            SpawnIntervalSeconds = Settings.ZombieSpawnIntervalSeconds.Value,
            StuckWaveTimeoutSeconds = Settings.ZombieStuckWaveTimeoutSeconds.Value,
        };
        _shooterPercent = shooterPercent;
        _ownGear = ArenaSession.ZombieOwnGearActive;
        if (Compat.PitFireTeamCompat.TeamArenaActive)
        {
            if (!_ownGear) throw new InvalidOperationException("Protected Zombie teams require own gear.");
            _team = new ZombieTeamRuntime(match, _game, _world, _local);
            // The pure solo wave director intentionally crosses a zero-length
            // break in one call. A team must observe every cleared-wave barrier.
            _config.IntermissionSeconds = Math.Max(0.1f, _config.IntermissionSeconds);
        }

        _rng = new System.Random();
        _waveState = ZombieWavePolicy.InitialCountdown();
        _refillState = ZombieRefillPolicy.Initial();
        _currentKit = null;
        _lastAnnouncedWave = 0;
        _zombiesKilled = 0;
        _nextNudgeAt = 0f;
        _nextWaveEvaluateAt = 0f;
        _phase = _waveState.Phase;
        _waveNumber = _waveState.WaveNumber;
        _waveStatusText = $"WAVE 1 IN {(int)Math.Ceiling(Math.Max(0f, _config.IntermissionSeconds))}s";

        // Tracks "the kit the player currently has" for the intermission
        // refill below — see ZombieLoadoutRefill's own doc comment. Must be
        // subscribed before Respawn.LiveKitRespawn.AtRaidStart runs so the
        // very first life's kit is captured too; ArenaSession.OnRaidStarted
        // calls Modes.ArenaMatchService.StartMatch (which calls this method)
        // strictly BEFORE its own AtRaidStart call, so this is in time.
        ArenaEvents.OnKitApplied += OnKitApplied;

        ZombieSpawnService.Reset();
        // countPerPair scales with WaveStartSize (see its own doc comment):
        // the fixed MinimumBackupCount=4 default left the native backup
        // cache for the heaviest-weighted role (infectedAssault, 50%) far
        // below a 30+ wave 1's own consumption, serializing most of it
        // through BotProfileClient's synchronous miss path (research note
        // "spawnThrottles").
        ZombieSpawnService.RegisterBackupTargets(
            ZombieProfilePlan.BackupTargets(_shooterPercent, ZombieProfilePlan.BackupCountForWaveStartSize(_config.WaveStartSize)));
        // So this raid's own log states which path (reverse patch/fallback)
        // actually drives its melee, and so a fallback failure gets its own
        // rate-limited warning rather than staying silenced by a previous
        // raid's — see ZombieMeleePatch.Reset()'s own doc comment.
        ZombieMeleePatch.Reset();

        Plugin.ModLogger.LogInfo(
            $"Zombie match started: wave 1 of {_config.WaveStartSize}, +{_config.WaveGrowth}/wave, "
            + $"max alive {_config.MaxAlive}, spawn every {_config.SpawnIntervalSeconds:0.##}s, "
            + $"intermission {_config.IntermissionSeconds:0.##}s, shooter share {_shooterPercent}%.");
    }

    public void Tick(float deltaTime)
    {
        if (!IsCurrentRaid())
            return;

        // Death callbacks normally arrive synchronously. This backstop also
        // covers a native death without an aggressor/score notification, before
        // evaluating another wave or beginning an intermission body replacement.
        if (_team == null && !HasResult && _world.MainPlayer?.HealthController?.IsAlive == false)
            _match.ReportLocalDeath();
        if (HasResult)
        {
            TickSoloResult();
            return;
        }

        // Optional harder horde: recall distant infected. OFF leaves native movement
        // intact; rendering is never changed. Sight/avoidance use separate hooks.
        ZombieCrowdRuntime.Tick();

        float now = Time.time;

        if (_team != null && !_team.Tick())
        {
            // Readiness and protected rebind consume no intermission time. This
            // also keeps the initial wave from spawning before all four arrive.
            if (_waveState.Phase == ZombieWavePolicy.Phase.Intermission)
                _waveState.PhaseEndsAt = now + _config.IntermissionSeconds;
            return;
        }

        // A raid-start kit swap must finish before the opening pause begins.
        // PFT reaches here only after its complete authenticated team is ready.
        if (!_openingCountdownStarted)
        {
            if (LiveKitRespawn.IsInitialSwapPending || ArenaRespawnDriver.IsRespawnInProgress
                || ArenaRespawnDriver.StandDown || _world.MainPlayer?.HealthController?.IsAlive != true)
                return;
            _openingCountdownStarted = true;
            _waveState.PhaseEndsAt = now + Math.Max(0f, _config.IntermissionSeconds);
        }
        if (_team != null) _local = _team.Leader;

        if (now >= _nextWaveEvaluateAt)
        {
            _nextWaveEvaluateAt = now + WaveEvaluateIntervalSeconds;

            if (ZombieSpawnService.TryGetPendingFailure(out string spawnFailure))
            {
                if (_team != null) _team.EndNoContest(spawnFailure);
                else EndSoloNoContest(spawnFailure);
                return;
            }
            // Asynchronous profile/placement failure is known before native
            // handoff. Restore those slots before the clearance decision;
            // unresolved native requests remain counted, never free capacity.
            int failedDispatches = ZombieSpawnService.ConsumeFailedDispatches();
            for (int i = 0; i < failedDispatches; i++)
                ZombieWavePolicy.ReportFailedDispatch(ref _waveState);

            int alive = ZombieSpawnService.CountAliveInfected();
            int pending = ZombieSpawnService.Pending;
            int arrivedTotal = ZombieSpawnService.ConfirmedArrivals;
            // The shared policy owns pending-arrival barriers for both solo
            // refill and protected team teardown, including watchdog clears.
            var result = ZombieWavePolicy.Evaluate(ref _waveState, _config, now, alive, pending, arrivedTotal);

            _phase = result.Phase;
            _waveNumber = result.WaveNumber;
            _waveStatusText = result.StatusText;

            if (_phase == ZombieWavePolicy.Phase.Wave && _waveNumber != _lastAnnouncedWave)
            {
                _lastAnnouncedWave = _waveNumber;
                _match?.ReportTextCallout($"WAVE {_waveNumber}");
            }

            if (result.ForceRetireStuckInfected)
            {
                // "did not clear" is no longer always literal (see
                // ZombieWavePolicy's own doc comment, "Saturated waves arm
                // the watchdog too"): a saturated MID-wave firing retires
                // the stuck survivor(s) and lets the wave's remainder keep
                // spawning, rather than ending the wave outright. Phase
                // still tells the difference for anyone reading the log
                // alongside the next status line.
                Plugin.ModLogger.LogWarning(
                    $"Zombie: wave {result.WaveNumber} made no progress (no infected death) for "
                    + $"{_config.StuckWaveTimeoutSeconds:0.#}s while saturated — administratively "
                    + "retiring whatever infected is still alive.");
                ZombieSpawnService.RetireAllAliveInfected();
            }

            if (_team != null && _waveNumber > 0 && _phase == ZombieWavePolicy.Phase.Intermission && !_teamIntermissionRestored)
            {
                _teamIntermissionRestored = true;
                _team.WaveCleared();
                return;
            }
            if (_phase == ZombieWavePolicy.Phase.Wave) _teamIntermissionRestored = false;

            for (int i = 0; i < result.SpawnNow; i++)
            {
                var draw = ZombieRoleMix.Next(_rng, _shooterPercent);
                bool dispatched = ZombieSpawnService.TryMapRole(draw.Role, out var role)
                    && ZombieSpawnService.TryMapDifficulty(draw.Difficulty, out var difficulty)
                    && ZombieSpawnService.TryRequest(role, difficulty);

                // Evaluate committed this wave slot optimistically, before
                // this dispatch attempt ran. A permanently failing dispatch
                // (bad draw, or TryRequest's own native-side rejection) must
                // not still count as "spawned" — undo the commit so the
                // wave does not silently reach "everything spawned" with
                // fewer real zombies than configured.
                if (!dispatched)
                    ZombieWavePolicy.ReportFailedDispatch(ref _waveState);
            }

            // Self-heal _local before computing playerEligible below.
            // _local is otherwise only refreshed by the real death-respawn
            // event (OnLocalRespawn, raised from ArenaRespawnDriver's three
            // death call sites) — but the raid-start kit swap AND this very
            // refill's own live-body swap (LiveKitRespawn.AtRaidStart /
            // AtRoundBreak / AtRoundBreakOwnGear, all through the shared
            // SwapRoutine) replace the live body too, and destroy the old
            // one (ArenaRetiredBodyEffects.TryDestroy) WITHOUT ever raising
            // that event. Because EFT.Player : MonoBehaviour, Unity's
            // overridden ==/!= make a cached reference to that destroyed
            // body read as "not null" never again, so an unhealed _local
            // would make playerEligible below false forever — silently, no
            // log line — after the very first kit swap or refill of the
            // raid. TeamDeathmatchMode.Tick already carries this exact
            // fix (poll _world.MainPlayer, heal on change); mirrored here.
            // Guarded on !IsRespawnInProgress because that flag (via
            // ArenaRespawnDriver.BeginPipelineUse/EndPipelineUse, which
            // LiveKitRespawn.SwapRoutine also uses) stays true for the
            // whole swap, so the heal only ever fires once the replacement
            // body is fully wired — never mid-swap, matching TDM's guard.
            Player currentPlayer = _world?.MainPlayer;
            if (_team == null && !ArenaRespawnDriver.IsRespawnInProgress
                && currentPlayer != null && !ReferenceEquals(currentPlayer, _local))
            {
                OnLocalRespawn(currentPlayer);
            }

            // Item 3 ("refill between waves", random-kit mode): decide WHEN
            // purely from the wave phase this call already computed, plus
            // the player's real eligibility right now. ZombieRefillPolicy
            // itself detects the Wave->Intermission edge and the "exactly
            // once" rule; this is just the Unity-facing signal it needs.
            bool playerEligible = _waveNumber > 0 && _local != null
                && _local.HealthController != null
                && _local.HealthController.IsAlive
                && !ArenaRespawnDriver.IsRespawnInProgress
                && !ArenaRespawnDriver.StandDown;

            var refillConfig = new ZombieRefillPolicy.Config { MaxAttempts = RefillMaxAttempts };
            var refill = ZombieRefillPolicy.Evaluate(ref _refillState, refillConfig, _phase, playerEligible && _team == null);
            if (refill.ShouldAttempt)
            {
                int clearedWave = _waveNumber;
                Func<bool> stillInThisIntermission =
                    () => IsCurrentRaid() && !IsComplete && _phase == ZombieWavePolicy.Phase.Intermission;

                // Item 4 ("own protected gear"): the WHEN (ZombieRefillPolicy,
                // just evaluated above) is identical either way — only the HOW
                // differs, between re-applying the player's current random kit
                // (ZombieLoadoutRefill.TryRefill) and restoring the protected
                // entry loadout (TryRefillOwnGear). See ZombieLoadoutRefill's
                // own doc comments for both.
                bool started = _ownGear
                    ? ZombieLoadoutRefill.TryRefillOwnGear(
                        _game,
                        clearedWave,
                        Settings.SpawnProtectionSeconds.Value,
                        stillInThisIntermission)
                    : ZombieLoadoutRefill.TryRefill(
                        _game,
                        _currentKit,
                        clearedWave,
                        Settings.SpawnProtectionSeconds.Value,
                        stillInThisIntermission);

                if (started)
                    ZombieRefillPolicy.ReportStarted(ref _refillState);
                else
                    ZombieRefillPolicy.ReportNotStarted(ref _refillState);
            }
        }

        if (now >= _nextNudgeAt)
        {
            _nextNudgeAt = now + NudgeIntervalSeconds;
            var player = _world?.MainPlayer;
            if (player != null)
                ZombieSpawnService.NudgeIdleZombies(player.Position);
        }
    }

    public void OnKill(ArenaKill kill)
    {
        if (kill == null || !IsCurrentRaid() || IsComplete)
            return;

        if (kill.KilledByLocal && !kill.LocalIsVictim
            && string.Equals(kill.VictimRoleLabel, "ZOMBIE", StringComparison.Ordinal))
            _zombiesKilled++;
    }

    /// <summary>Solo finishes immediately; protected teams observe surviving allies.</summary>
    public void OnLocalDeath()
    {
        if (_team != null) { _team.OnLocalDeath(); return; }
        if (!IsCurrentRaid() || HasResult) return;
        _soloDefeated = true;
        _soloResultPanelReady = ArenaSession.ReplaySeconds <= 0f;
        _soloDefeatedAt = Time.unscaledTime;
        _match.Scoreboard.ResetAssists();
        _match.ReportTextCallout(ResultHeading);
        Plugin.ModLogger.LogInfo("Zombie solo survival ended on local death; respawn held through protected exit.");
    }

    private void TickSoloResult()
    {
        if (!ResultOwnsCurrentRaid || TdmMatchExit.PipelineRunning) return;
        // Let every native death listener, including optional KillcamReplay,
        // publish its pending playback before inspecting camera ownership.
        if (Time.unscaledTime - _soloDefeatedAt < 0.25f) return;
        if (ArenaSession.ReplaySeconds > 0f && !_resultExitRequested)
        {
            var owner = Spectator.KillcamReplayCompat.ReadState(out float remaining, out string problem);
            var decision = _resultReplay.Observe(owner, remaining, Time.time);
            _soloResultPanelReady = decision == ArenaReplayWaitDecision.Ready;
            if (decision == ArenaReplayWaitDecision.Wait) return;
            if (decision == ArenaReplayWaitDecision.Unsafe)
                ReportResultExitProblem("Death replay did not release safely; ending survival: " + (problem ?? "bounded wait expired"));
        }
        if (Time.unscaledTime < _nextStopAttempt) return;
        _nextStopAttempt = Time.unscaledTime + 1f;
        TdmMatchExit.TryStop(_game, _world, _localId, ReportResultExitProblem);
    }

    private void EndSoloNoContest(string reason)
    {
        if (!IsCurrentRaid() || IsComplete) return;
        _soloNoContestReason = reason;
        _soloDefeated = true;
        _soloResultPanelReady = true;
        _resultExitRequested = true; // no death happened, so no replay to await
        _soloDefeatedAt = Time.unscaledTime;
        _match.Scoreboard.ResetAssists();
        _match.ReportTextCallout(ResultHeading);
        Plugin.ModLogger.LogWarning("Zombie survival ended no contest: " + reason);
    }

    public void OnLocalRespawn(Player player)
    {
        if (!IsCurrentRaid() || IsComplete || player == null)
            return;
        if (_team != null && !_team.OnLocalRespawn(player)) return;

        _local = player;
        _localId = player.ProfileId;
    }

    /// <summary>
    /// Keeps _currentKit as "whatever the player is wearing right now":
    /// raised after the raid-start swap and this mode's own
    /// intermission refill. ArenaEvents.OnKitApplied is already scoped to
    /// the raid it fired for (LiveKitRespawn/ArenaRespawnDriver only raise
    /// it once their own "same active raid" check passes), and this handler
    /// is unsubscribed in OnMatchEnd, so a later raid's kit swaps can never
    /// reach a stale ZombieMode instance.
    /// </summary>
    private void OnKitApplied(KitDefinition kit) => _currentKit = kit;

    public void OnMatchEnd()
    {
        _ended = true;
        _team?.Dispose();
        ArenaEvents.OnKitApplied -= OnKitApplied;
        ZombieSpawnService.Reset();
        ZombieMeleePatch.Reset();

        // No richer existing match-end summary to fold this into (unlike
        // GunGameMode's own tier/wins line) — one line here is what
        // ZombieMeleePatch's own fix design asks for: confirm from the log
        // alone, without reading raw swing/hit counters by hand, whether a
        // GPU raid's zombies actually swung and landed hits at all.
        var (swings, hits) = ZombieMeleeDiagnostics.ConsumeTotals();
        if (ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled)
            ArenaMode.Diagnostics.ArenaLog.Developer(
                $"Zombie match ended: {swings} native melee swing(s) attempted, {hits} landed hit(s) observed (partial developer observation if enabled after spawning).");
    }

    private bool IsCurrentRaid()
        => !_ended && _match != null && _game != null && _game.Status == GameStatus.Started && _world != null
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
           && ArenaSession.IsExactActiveRaid(_game, _world);
}
