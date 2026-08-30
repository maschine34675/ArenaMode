using ArenaMode.Combat;
using ArenaMode.Config;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Rounds: play until a kill target or a time limit is reached, show the
/// scoreboard during a short intermission, then start the next round inside the
/// SAME raid. Nothing about the world is reset — bots keep fighting, the map
/// stays as it is; a round is a scoring bracket, not a map restart. That keeps
/// the mode honest with everything we learned about mid-raid teardown.
/// </summary>
public sealed class RoundsMode : IArenaMode
{
    /// <summary>Round bookkeeping, deliberately separate from the match totals.</summary>
    public sealed class RoundState
    {
        public int Number = 1;
        public int Kills;
        public int Deaths;
        public int Score;
        public float StartedAt;
    }

    /// <summary>How long a break waits on a respawn that never reports back.</summary>
    private const float StuckRespawnGraceSeconds = 30f;

    private ArenaMatch _match;
    private bool _breakRespawnPending;
    private float _intermissionEndsAt;
    private int _killsAtRoundStart;
    private int _deathsAtRoundStart;
    private int _scoreAtRoundStart;

    public string Id => "rounds";

    public string DisplayName => "Rounds";

    /// <summary>The match never "completes" — rounds keep coming until the raid ends.</summary>
    public bool IsComplete => false;

    public RoundState Round { get; } = new RoundState();

    /// <summary>True while the scoreboard shows and the next round is counting in.</summary>
    public bool InIntermission { get; private set; }

    /// <summary>
    /// Whether the player is invulnerable RIGHT NOW. Asked live rather than
    /// remembered, because protection can run out mid-break — the leftover of a
    /// respawn, say — and the panel freezes input on this answer. A frozen,
    /// vulnerable player with every bot hunting them is worse than no
    /// scoreboard at all, so this has to stop being true the moment it stops
    /// being true.
    /// </summary>
    public bool IntermissionProtected => InIntermission && Combat.ArenaProtection.IsProtectedNow;

    public float IntermissionRemaining =>
        InIntermission ? Mathf.Max(0f, _intermissionEndsAt - Time.time) : 0f;

    /// <summary>Seconds left in the round, or -1 when the mode runs on kills only.</summary>
    public float TimeRemaining
    {
        get
        {
            var limit = Settings.RoundTimeLimitSeconds.Value;
            if (limit <= 0)
                return -1f;

            return Mathf.Max(0f, Round.StartedAt + limit - Time.time);
        }
    }

    public int KillTarget => Settings.RoundKillTarget.Value;

    /// <summary>Raised when a round ends (arg: the finished round's state).</summary>
    public event Action<RoundState> OnRoundEnded;

    public event Action<int> OnRoundStarted;

    /// <summary>Instant respawn leaves no room for one.</summary>
    public float DeathReplaySeconds => 0f;

    public string HudStatusLine
    {
        get
        {
            if (InIntermission)
                return $"Round {Round.Number} over — next in {Mathf.CeilToInt(IntermissionRemaining)}s";

            var parts = $"Round {Round.Number}";
            var target = KillTarget;
            if (target > 0)
                parts += $"   {Round.Kills}/{target} kills";

            var remaining = TimeRemaining;
            if (remaining >= 0f)
                parts += $"   {Mathf.FloorToInt(remaining / 60f):0}:{Mathf.FloorToInt(remaining % 60f):00}";

            return parts;
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        _match = match;

        if (KillTarget <= 0 && Settings.RoundTimeLimitSeconds.Value <= 0)
        {
            Plugin.ModLogger.LogWarning(
                "Rounds mode has neither a kill target nor a time limit — the round will never end. "
                + "Set RoundKillTarget or RoundTimeLimitSeconds in the config.");
        }

        StartRound(1);
    }

    public void Tick(float deltaTime)
    {
        if (_match == null)
            return;

        if (InIntermission)
        {
            // Never start a round on top of a running respawn — the round break
            // respawn, or a death during the pause. Both end with the player in
            // a body that is only finished once the pipeline says so.
            if (Respawn.ArenaRespawnDriver.IsRespawnInProgress)
                return;

            // The break respawn spends its first seconds waiting — for a draw
            // animation to finish, for the kit's bundles to load — and during
            // those seconds it has not claimed the body yet, so the flag above
            // is still false. Starting the round in that window is how the swap
            // ends up landing in the middle of the next round. The grace period
            // is the backstop for a coroutine that dies without reporting.
            if (_breakRespawnPending)
            {
                if (Time.time < _intermissionEndsAt + StuckRespawnGraceSeconds)
                    return;

                Plugin.ModLogger.LogWarning(
                    "Arena round break respawn never reported back — starting the next round without it.");
                _breakRespawnPending = false;
            }

            if (Time.time >= _intermissionEndsAt)
                StartRound(Round.Number + 1);

            return;
        }

        if (IsRoundOver())
            EndRound();
    }

    public void OnKill(ArenaKill kill)
    {
        if (_match == null || InIntermission || !kill.KilledByLocal || kill.LocalIsVictim)
            return;

        Round.Kills = _match.Kills - _killsAtRoundStart;
        Round.Score = _match.Score - _scoreAtRoundStart;
    }

    public void OnLocalDeath()
    {
        // Symmetric with OnKill: what happens during the pause belongs to the
        // next round, whose snapshot is taken when it starts.
        if (_match == null || InIntermission)
            return;

        Round.Deaths = _match.Deaths - _deathsAtRoundStart;
    }

    public void OnLocalRespawn()
    {
    }

    public void OnMatchEnd()
    {
        EndRound(matchEnding: true);
    }

    /// <summary>
    /// The break only really starts once the player is standing again: the
    /// pipeline runs behind a black screen, and counting that time against the
    /// scoreboard would leave nothing to look at. A failed respawn falls back to
    /// the old behaviour so the pause is never spent lying in the open.
    /// </summary>
    private void OnRoundBreakRespawn(bool success)
    {
        // First, and outside every other condition: a break that is somehow
        // already over must not leave the pause holding on this forever.
        _breakRespawnPending = false;

        if (!InIntermission)
            return;

        var duration = Mathf.Max(3f, Settings.RoundIntermissionSeconds.Value);
        _intermissionEndsAt = Time.time + duration;

        if (success)
            return;

        HealForNewRound();
        Combat.ArenaProtection.Begin(duration);
    }

    private bool IsRoundOver()
    {
        var target = KillTarget;
        if (target > 0 && Round.Kills >= target)
            return true;

        var remaining = TimeRemaining;
        return remaining >= 0f && remaining <= 0f;
    }

    private void EndRound(bool matchEnding = false)
    {
        if (InIntermission)
            return;

        InIntermission = true;
        var duration = Mathf.Max(3f, Settings.RoundIntermissionSeconds.Value);
        _intermissionEndsAt = Time.time + duration;

        // The break is where the next round's body comes from: a fresh kit at a
        // spawn point away from the fight that just ended. Standing invulnerable
        // in the middle of the bots that were shooting at you is technically
        // safe and reads as broken — being somewhere else is what makes a pause
        // feel like one. The pipeline brings its own protection, so nothing is
        // applied here that a body swap would throw away.
        // Not while the raid is tearing down: the protection coroutine has no
        // teardown guard and would be left holding a zeroed damage coefficient.
        var respawning = !matchEnding
            && Settings.RoundRespawn.Value
            && Comfort.Common.Singleton<EFT.AbstractGame>.Instance is EFT.LocalGame game
            && game.Status == EFT.GameStatus.Started
            && Respawn.LiveKitRespawn.AtRoundBreak(game, duration, () => InIntermission, OnRoundBreakRespawn);

        // Holds the pause open until the respawn reports back — see Tick.
        _breakRespawnPending = respawning;

        if (!respawning && !matchEnding)
        {
            // Either the respawn is switched off or it could not run (no kit, a
            // death already being handled). Fall back to the old behaviour:
            // patch the player up where they stand and make them untouchable.
            HealForNewRound();
            if (!Combat.ArenaProtection.Begin(duration))
                Plugin.ModLogger.LogWarning("Arena intermission without protection — input stays free.");
        }

        Plugin.ModLogger.LogInfo(
            $"Arena round {Round.Number} ended: {Round.Kills} kills / {Round.Deaths} deaths, score {Round.Score}.");
        ArenaToasts.Show($"Round {Round.Number}: {Round.Kills} kills, {Round.Score:n0} points");

        try
        {
            OnRoundEnded?.Invoke(Round);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena round-end subscriber failed: {ex}");
        }
    }

    /// <summary>
    /// The fallback for a break without a respawn: patch the player up where
    /// they stand. Only their condition is touched — the world, the bots and the
    /// inventory stay exactly as they are. A player who died during the break is
    /// skipped; the respawn driver owns them and has already healed them.
    /// </summary>
    private static void HealForNewRound()
    {
        try
        {
            var player = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated
                ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.MainPlayer
                : null;

            var health = player?.ActiveHealthController;
            if (health == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            if (Respawn.ArenaRespawnDriver.IsRespawnInProgress)
                return;

            health.RestoreFullHealth();
            health.RemoveNegativeEffects(EBodyPart.Common);

            // Intoxication is permanent and survives the line above.
            health.RemoveIntoxication();

            // Both clamp at their maximum.
            health.ChangeEnergy(1000f);
            health.ChangeHydration(1000f);

            // The full-heal path writes the body parts directly and raises
            // neither the restore nor the health-changed event, so the crippled
            // movement state (blacked-leg speed limit, sprint lock, arm sway)
            // would survive a "full heal" — the same trap the respawn engine
            // already documents. Drive the recompute by hand.
            player.UpdateSpeedLimitByHealth();
            player.UpdateArmsCondition();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena round heal failed: {ex.Message}");
        }
    }

    private void StartRound(int number)
    {
        InIntermission = false;
        _breakRespawnPending = false;
        Round.Number = number;
        Round.Kills = 0;
        Round.Deaths = 0;
        Round.Score = 0;
        Round.StartedAt = Time.time;

        _killsAtRoundStart = _match?.Kills ?? 0;
        _deathsAtRoundStart = _match?.Deaths ?? 0;
        _scoreAtRoundStart = _match?.Score ?? 0;

        Plugin.ModLogger.LogInfo($"Arena round {number} started.");
        if (number > 1)
            ArenaToasts.Show($"Round {number} — fight!");

        try
        {
            OnRoundStarted?.Invoke(number);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena round-start subscriber failed: {ex}");
        }
    }
}
