using System;

namespace ArenaMode.Modes;

/// <summary>
/// Pure endless-wave state machine for Zombie mode: a Wave is filling up or
/// still has survivors, Intermission is the pause before the next one.
///
/// No EFT/Unity types, so ArenaMode.Tests can link this file directly (see
/// ArenaMode.Tests.csproj's ClientRules Compile Include links). Every input
/// — the clock (<c>now</c>), how many infected are alive, how many spawn
/// requests are still in flight (<c>pending</c>) — is supplied by the caller
/// on every call; this class owns no clock, no game-world reference and no
/// Random. ZombieMode owns Time.time and asks ZombieSpawnService for the
/// alive/pending counts; ZombieRoleMix decides WHICH role/difficulty to
/// request when this class says to spawn one.
///
/// Wave sizing and the spawn-pace/cap rules are the orchestrator's decision:
/// wave n has WaveStartSize + WaveGrowth*(n-1) infected; one spawn request
/// goes out every SpawnIntervalSeconds while alive+pending stays under
/// min(MaxAlive, zombies of this wave not yet spawned + currently alive) —
/// the second term keeps the tail of a wave from ever asking for more than
/// the wave actually still has left, once MaxAlive is no longer the binding
/// constraint. A wave ends only once everything in it has been spawned AND
/// nothing from it is alive any more; only then does Intermission begin —
/// except for the StuckWaveTimeoutSeconds watchdog (Config/State/Result),
/// which force-clears a wave whose tail has stayed "everything spawned, at
/// least one still alive" past that ceiling, since nothing else in this mode
/// recovers from a single unreachable infected.
/// </summary>
internal static class ZombieWavePolicy
{
    internal enum Phase { Wave, Intermission }

    /// <summary>Frozen at match start (see ZombieMode.OnMatchStart); never re-read mid-raid.</summary>
    internal struct Config
    {
        internal int MaxAlive;
        internal int WaveStartSize;
        internal int WaveGrowth;
        internal float IntermissionSeconds;
        internal float SpawnIntervalSeconds;

        /// <summary>
        /// Safety net for a wave's last infected getting wedged in geometry or
        /// stranded on an unreachable NavMesh island: once nothing is left to
        /// spawn for the current wave and at least one infected has stayed
        /// alive this long since that happened, Evaluate signals
        /// Result.ForceRetireStuckInfected and clears the wave anyway, rather
        /// than leaving Phase stuck at Wave — and the whole endless-wave mode
        /// with it — for the rest of the raid. 0 or negative disables the
        /// watchdog entirely (the wave then waits for every infected to die
        /// naturally, with no recovery if one cannot reach the player).
        /// </summary>
        internal float StuckWaveTimeoutSeconds;
    }

    /// <summary>Mutable per-match state. Default-constructed is NOT a valid start — use <see cref="Initial"/>.</summary>
    internal struct State
    {
        internal int WaveNumber;
        internal Phase Phase;

        /// <summary>Spawn requests accepted so far for the CURRENT wave (not yet confirmed alive or dead).</summary>
        internal int SpawnedThisWave;

        /// <summary>Earliest time the next spawn request may go out, meaningful only during Wave.</summary>
        internal float NextSpawnAt;

        /// <summary>When the current Intermission ends, meaningful only during Intermission.</summary>
        internal float PhaseEndsAt;

        /// <summary>
        /// NaN unless the current wave has nothing left to spawn while at
        /// least one infected is still alive, in which case this is the
        /// `now` the tail state was FIRST observed — the stuck-wave
        /// watchdog's clock starts here, not at wave start. Reset to NaN the
        /// moment that tail state is no longer true (more spawning resumed,
        /// the wave cleared normally, or the watchdog force-cleared it).
        /// </summary>
        internal float StuckAliveSince;
    }

    internal struct Result
    {
        /// <summary>0 or 1: whether ZombieMode should issue one spawn request this call.</summary>
        internal int SpawnNow;
        internal int WaveNumber;

        /// <summary>Zombies of the current wave not yet spawned, plus zombies of it still alive.</summary>
        internal int Remaining;
        internal Phase Phase;

        /// <summary>Seconds left in the current intermission; 0 during a wave.</summary>
        internal float IntermissionRemaining;

        /// <summary>"WAVE n | m LEFT" during a wave, "WAVE n+1 IN Xs" during intermission. No elapsed-time
        /// suffix: ZombieMode.HudStatusLine appends that from ArenaMatch.Elapsed, which this pure class
        /// has no access to.</summary>
        internal string StatusText;

        /// <summary>
        /// True exactly once, on the call where the stuck-wave watchdog fired:
        /// the wave was force-cleared with at least one infected still
        /// reported alive. The caller (ZombieMode) must administratively
        /// retire whatever is still alive right now (ZombieSpawnService),
        /// since this pure class has no bot identities to act on itself.
        /// </summary>
        internal bool ForceRetireStuckInfected;
    }

    /// <summary>Wave 1, nothing spawned yet, eligible to spawn immediately.</summary>
    internal static State Initial() => new State
    {
        WaveNumber = 1,
        Phase = Phase.Wave,
        SpawnedThisWave = 0,
        NextSpawnAt = float.NegativeInfinity,
        PhaseEndsAt = 0f,
        StuckAliveSince = float.NaN,
    };

    /// <summary>Wave n's size: WaveStartSize + WaveGrowth * (n - 1), n &gt;= 1.</summary>
    internal static int WaveSize(Config config, int waveNumber)
        => Math.Max(1, config.WaveStartSize) + Math.Max(0, config.WaveGrowth) * Math.Max(0, waveNumber - 1);

    /// <summary>
    /// Call exactly once per SpawnNow==1 that the caller actually tried and
    /// failed to turn into a real dispatch (ZombieSpawnService.TryRequest
    /// returned false). Evaluate below commits the wave slot
    /// (SpawnedThisWave++) the instant it DECIDES to spawn, before the
    /// caller has even attempted the real native dispatch — optimistic by
    /// design, so a slow-but-successful dispatch is never double-counted.
    /// But a dispatch that permanently fails (no usable spawn point under
    /// map/geometry contention — BurstSpawnService's own documented failure
    /// mode) would otherwise still count as "spawned", letting
    /// remainingToSpawn reach 0 and the wave silently "clear" with fewer
    /// real zombies than configured, in the worst case none at all. This
    /// undoes exactly that one optimistic increment, so the slot is retried
    /// on the wave's normal pacing cadence (NextSpawnAt was already
    /// advanced by Evaluate either way) instead of being lost.
    /// </summary>
    internal static void ReportFailedDispatch(ref State state)
    {
        if (state.SpawnedThisWave > 0)
            state.SpawnedThisWave--;
    }

    internal static Result Evaluate(ref State state, Config config, float now, int alive, int pending)
    {
        if (float.IsNaN(now) || float.IsInfinity(now))
            throw new ArgumentOutOfRangeException(nameof(now));

        alive = Math.Max(0, alive);
        pending = Math.Max(0, pending);
        int maxAlive = Math.Max(1, config.MaxAlive);
        float intermissionSeconds = Math.Max(0f, config.IntermissionSeconds);
        float spawnInterval = Math.Max(0.01f, config.SpawnIntervalSeconds);

        int spawnNow = 0;
        int waveSize = WaveSize(config, state.WaveNumber);

        if (state.Phase == Phase.Wave)
        {
            int remainingToSpawn = Math.Max(0, waveSize - state.SpawnedThisWave);
            if (remainingToSpawn == 0 && alive <= 0)
            {
                // Everything of this wave was spawned and none of it is alive:
                // the wave is cleared, not merely "thinned out".
                state.Phase = Phase.Intermission;
                state.PhaseEndsAt = now + intermissionSeconds;
                state.StuckAliveSince = float.NaN;
                // Symmetric with the Intermission branch below: a zero-length
                // intermission must advance straight through to the next wave
                // (and spawn its first zombie) in this same call, not wait a
                // whole extra Tick for the transition to be noticed.
                return Evaluate(ref state, config, now, alive, pending);
            }

            // Tail of the wave: nothing left to request, but at least one of
            // it has not died. Unlike every other state here, this one has no
            // other exit — remainingToSpawn stays 0 for the rest of the wave,
            // so without a ceiling, a single alive-but-unreachable infected
            // (wedged in geometry, stranded off a connected NavMesh island —
            // AGENTS.md documents exactly this class of problem) would freeze
            // Phase at Wave, and the whole endless-wave mode with it, for the
            // rest of the raid.
            if (remainingToSpawn == 0 && alive > 0)
            {
                if (float.IsNaN(state.StuckAliveSince))
                    state.StuckAliveSince = now;

                float stuckTimeout = config.StuckWaveTimeoutSeconds;
                if (stuckTimeout > 0f && now - state.StuckAliveSince >= stuckTimeout)
                {
                    // This class owns no bot identities to retire itself —
                    // it only signals ForceRetireStuckInfected so the caller
                    // (ZombieMode) can administratively kill whatever is
                    // still alive right now (ZombieSpawnService), then this
                    // call proceeds exactly like the ordinary "nothing left
                    // alive" clear above, with alive forced to 0 from here on
                    // (the caller is about to make that true for real).
                    state.Phase = Phase.Intermission;
                    state.PhaseEndsAt = now + intermissionSeconds;
                    state.StuckAliveSince = float.NaN;
                    var forced = Evaluate(ref state, config, now, 0, pending);
                    forced.ForceRetireStuckInfected = true;
                    return forced;
                }
            }
            else
            {
                state.StuckAliveSince = float.NaN;
            }

            int target = Math.Min(maxAlive, remainingToSpawn + alive);
            if (remainingToSpawn > 0 && alive + pending < target && now >= state.NextSpawnAt)
            {
                spawnNow = 1;
                state.SpawnedThisWave++;
                state.NextSpawnAt = now + spawnInterval;
            }
        }
        else // Intermission
        {
            if (now >= state.PhaseEndsAt)
            {
                state.WaveNumber++;
                state.Phase = Phase.Wave;
                state.SpawnedThisWave = 0;
                state.NextSpawnAt = now;
                state.StuckAliveSince = float.NaN;
                // Re-evaluate as the new wave in the SAME call: a zero-length
                // intermission must spawn immediately rather than waiting a
                // whole extra Tick for the transition to be noticed.
                return Evaluate(ref state, config, now, alive, pending);
            }
        }

        int remaining = Math.Max(0, waveSize - state.SpawnedThisWave) + alive;
        float intermissionRemaining = state.Phase == Phase.Intermission ? Math.Max(0f, state.PhaseEndsAt - now) : 0f;
        string statusText = state.Phase == Phase.Wave
            ? $"WAVE {state.WaveNumber} | {remaining} LEFT"
            : $"WAVE {state.WaveNumber + 1} IN {(int)Math.Ceiling(intermissionRemaining)}s";

        return new Result
        {
            SpawnNow = spawnNow,
            WaveNumber = state.WaveNumber,
            Remaining = remaining,
            Phase = state.Phase,
            IntermissionRemaining = intermissionRemaining,
            StatusText = statusText,
        };
    }
}
