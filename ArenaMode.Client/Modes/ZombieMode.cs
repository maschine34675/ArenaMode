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
/// call. ZombieSpawnService is Stage C's seam (see its own doc comment) —
/// in THIS stage every request it is asked to make is honestly refused, so a
/// zombie raid runs with the wave clock ticking and the HUD counting down a
/// wave that never actually produces a bot. That is expected until Stage C
/// lands; it is not a bug in this file.
/// </summary>
public sealed class ZombieMode : IArenaMode
{
    private const float NudgeIntervalSeconds = 5f;

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

        _config = new ZombieWavePolicy.Config
        {
            MaxAlive = Settings.ZombieMaxAlive.Value,
            WaveStartSize = Settings.ZombieWaveStartSize.Value,
            WaveGrowth = Settings.ZombieWaveGrowth.Value,
            IntermissionSeconds = Settings.ZombieIntermissionSeconds.Value,
            SpawnIntervalSeconds = Settings.ZombieSpawnIntervalSeconds.Value,
        };
        _shooterPercent = Settings.ZombieShooterPercent.Value;

        _rng = new System.Random();
        _waveState = ZombieWavePolicy.Initial();
        _lastAnnouncedWave = 0;
        _zombiesKilled = 0;
        _nextNudgeAt = 0f;
        _phase = _waveState.Phase;
        _waveNumber = _waveState.WaveNumber;
        _waveStatusText = $"WAVE {_waveNumber} | {_config.WaveStartSize} LEFT";

        ZombieSpawnService.Reset();
        ZombieSpawnService.RegisterBackupTargets(ZombieProfilePlan.BackupTargets(_shooterPercent));

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
        int alive = ZombieSpawnService.CountAliveInfected();
        int pending = ZombieSpawnService.Pending;
        var result = ZombieWavePolicy.Evaluate(ref _waveState, _config, now, alive, pending);

        _phase = result.Phase;
        _waveNumber = result.WaveNumber;
        _waveStatusText = result.StatusText;

        if (_phase == ZombieWavePolicy.Phase.Wave && _waveNumber != _lastAnnouncedWave)
        {
            _lastAnnouncedWave = _waveNumber;
            _match?.ReportTextCallout($"WAVE {_waveNumber}");
        }

        for (int i = 0; i < result.SpawnNow; i++)
        {
            var draw = ZombieRoleMix.Next(_rng, _shooterPercent);
            if (TryMapRole(draw.Role, out var role) && TryMapDifficulty(draw.Difficulty, out var difficulty))
                ZombieSpawnService.TryRequest(role, difficulty);
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
    }

    private bool IsCurrentRaid()
        => !_ended && _match != null && _game != null && _game.Status == GameStatus.Started && _world != null
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
           && ArenaSession.IsExactActiveRaid(_game, _world);

    private static bool TryMapRole(string role, out WildSpawnType type)
    {
        switch (role)
        {
            case ZombieRoleMix.InfectedAssault: type = WildSpawnType.infectedAssault; return true;
            case ZombieRoleMix.InfectedPmc: type = WildSpawnType.infectedPmc; return true;
            case ZombieRoleMix.InfectedCivil: type = WildSpawnType.infectedCivil; return true;
            case ZombieRoleMix.InfectedLaborant: type = WildSpawnType.infectedLaborant; return true;
            default:
                type = default;
                Plugin.ModLogger.LogWarning($"Zombie: unknown role mix draw '{role}' — spawn request skipped.");
                return false;
        }
    }

    private static bool TryMapDifficulty(string difficulty, out BotDifficulty value)
    {
        switch (difficulty)
        {
            case ZombieRoleMix.Normal: value = BotDifficulty.normal; return true;
            case ZombieRoleMix.Hard: value = BotDifficulty.hard; return true;
            default:
                value = default;
                Plugin.ModLogger.LogWarning($"Zombie: unknown difficulty draw '{difficulty}' — spawn request skipped.");
                return false;
        }
    }
}
