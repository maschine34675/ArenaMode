using ArenaMode.Bots;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Endless waves of EFT's built-in infected (Halloween 2024 leftovers)
/// against the local player. Solo, instant respawn, Arena kits — see
/// AGENTS.md and the orchestrator's zombie-mode design for the full picture.
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

    // Cached from the latest Tick for HudStatusLine, which must stay cheap —
    // ScorePanel reads it up to four times a second.
    private ZombieWavePolicy.Phase _phase;
    private int _waveNumber;
    private string _waveStatusText = "ZOMBIES";

    public string Id => ArenaModeCatalog.ZombieId;
    public string DisplayName => "Zombies";

    /// <summary>Endless: the wave count never completes the mode.</summary>
    public bool IsComplete => false;

    public float DeathReplaySeconds => ArenaReplaySelection.Seconds;

    /// <summary>Instant respawn, exactly like Deathmatch — the waves keep coming either way.</summary>
    public bool HoldRespawn => false;

    /// <summary>Zombie owns its entire population (see ArenaPopulationPolicy); no ambient top-up.</summary>
    public bool ControlsOwnSpawns => true;

    /// <summary>Gates ArenaAssistCapture (see its ScoringOpen switch) — open for the whole live raid.</summary>
    internal bool AssistScoringOpen => IsCurrentRaid();

    /// <summary>Zombies the LOCAL player has killed this raid. Not shown in HudStatusLine today;
    /// kept for a future HUD and for diagnostics (decision 4: "OnKill counts infected victims").</summary>
    internal int ZombiesKilled => _zombiesKilled;

    public string HudStatusLine
    {
        get
        {
            if (!IsCurrentRaid() || _match == null)
                return "ZOMBIES";

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

        _rng = new System.Random();
        _waveState = ZombieWavePolicy.Initial();
        _lastAnnouncedWave = 0;
        _zombiesKilled = 0;
        _nextNudgeAt = 0f;
        _nextWaveEvaluateAt = 0f;
        _phase = _waveState.Phase;
        _waveNumber = _waveState.WaveNumber;
        _waveStatusText = $"WAVE {_waveNumber} | {_config.WaveStartSize} LEFT";

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

        float now = Time.time;

        if (now >= _nextWaveEvaluateAt)
        {
            _nextWaveEvaluateAt = now + WaveEvaluateIntervalSeconds;

            int alive = ZombieSpawnService.CountAliveInfected();
            int pending = ZombieSpawnService.Pending;
            int arrivedTotal = ZombieSpawnService.ConfirmedArrivals;
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
                Plugin.ModLogger.LogWarning(
                    $"Zombie: wave {result.WaveNumber} did not clear within "
                    + $"{_config.StuckWaveTimeoutSeconds:0.#}s of its last spawn — administratively "
                    + "retiring whatever infected is still alive.");
                ZombieSpawnService.RetireAllAliveInfected();
            }

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
        if (kill == null || !IsCurrentRaid())
            return;

        if (kill.KilledByLocal && !kill.LocalIsVictim
            && string.Equals(kill.VictimRoleLabel, "ZOMBIE", StringComparison.Ordinal))
            _zombiesKilled++;
    }

    /// <summary>No scored round/result to protect — instant respawn, same as Deathmatch.</summary>
    public void OnLocalDeath() { }

    public void OnLocalRespawn(Player player)
    {
        if (!IsCurrentRaid() || player == null)
            return;

        _local = player;
        _localId = player.ProfileId;
    }

    public void OnMatchEnd()
    {
        _ended = true;
        ZombieSpawnService.Reset();
        ZombieMeleePatch.Reset();

        // No richer existing match-end summary to fold this into (unlike
        // GunGameMode's own tier/wins line) — one line here is what
        // ZombieMeleePatch's own fix design asks for: confirm from the log
        // alone, without reading raw swing/hit counters by hand, whether a
        // GPU raid's zombies actually swung and landed hits at all.
        var (swings, hits) = ZombieMeleeDiagnostics.ConsumeTotals();
        Plugin.ModLogger.LogInfo(
            $"Zombie match ended: {swings} native melee swing(s) attempted, {hits} landed hit(s) this raid.");
    }

    private bool IsCurrentRaid()
        => !_ended && _match != null && _game != null && _game.Status == GameStatus.Started && _world != null
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
           && ArenaSession.IsExactActiveRaid(_game, _world);
}
