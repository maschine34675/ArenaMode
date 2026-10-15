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
/// constraint. This is already a CONTINUOUS refill: nothing here waits for
/// the whole wave to clear before asking for more — as zombies die, alive
/// drops, the min() term above rises back toward MaxAlive, and the next
/// SpawnIntervalSeconds tick asks for a replacement, so a 30+ wave at
/// MaxAlive 20 fills to 20 and then refills one at a time with no pause,
/// exactly as fast as the player can create room (see
/// ZombieWavePolicyTests.ThirtyZombieWaveFillsToMaxAliveThenRefillsOnePerIntervalAsAliveDrops).
/// A wave ends only once every request has settled AND nothing from it is
/// alive any more. A dispatched request still loading its body belongs to
/// this wave, even when its earlier peers have all died. Only then does
/// Intermission begin — except for the
/// StuckWaveTimeoutSeconds watchdog (Config/State/Result), which force-clears
/// whatever is alive once the wave is SATURATED (see "Saturated waves arm
/// the watchdog too" below) with NO infected death for that long, since
/// nothing else in this mode recovers from a single unreachable infected.
///
/// <para><b>Watchdog progress, not duration (2026-10-06 GPU finding).</b> The
/// first GPU raid's watchdog cleared a wave the player was still actively
/// fighting: the original design measured the ceiling as wall-clock time
/// since the tail state began, so a player steadily killing one infected
/// every 20s for 6 minutes would still get force-cleared at 100s even though
/// the wave was never actually stuck. The fix: the timer restarts on every
/// call where <c>Evaluate</c> observes an infected death since the previous
/// call, not merely once at tail entry — "stuck" now means "fully spawned
/// AND no infected has died for StuckWaveTimeoutSeconds", matching the
/// setting's own name.</para>
///
/// <para><b>How a death is detected, and why not the obvious way.</b> Two
/// designs were weighed (see the orchestrator's research note):
/// (a) feed the signal from the kill-capture/scoring pipeline this mode
/// already receives (<see cref="ZombieMode.OnKill"/>, wired to every
/// <c>ArenaKill</c> via <c>ArenaMatch.ReportKill</c>), or (b) derive it here,
/// purely from the <c>alive</c>/<c>arrivedTotal</c> counts <c>Evaluate</c>
/// already takes every call. (b) was chosen. <c>ArenaKill</c> is only raised
/// when EFT can name a credited aggressor
/// (<c>CombatPatches</c>' death postfix requires one); an infected that dies
/// with none identified — environmental damage, a fall — is routed instead to
/// <c>ArenaUncreditedDeathPatch</c>, which explicitly skips any victim that
/// is not the local player
/// (<c>!__instance.IsYourPlayer &amp;&amp; damageType != EDamageType.Barbed</c>
/// returns early there) — so that class of infected death produces NO event
/// anywhere, not even presentation, and would silently fail to reset this
/// exact watchdog. Staying inside this pure layer also means a future change
/// to scoring/kill-feed filtering (assist eligibility, FFA blocking,
/// <c>ArenaAdministrativeDeathGate</c>'s own exclusions) can never silently
/// change watchdog behaviour — the two concerns are independent on purpose.
/// Design (b) reads only what <see cref="ZombieSpawnService.CountAliveInfected"/>
/// and <see cref="ZombieSpawnService.ConfirmedArrivals"/> already track, both
/// keyed on the same "bot's own HealthController.IsAlive" fact, with no
/// dependency on who or what killed it.</para>
///
/// <para><b>Why a raw alive-count drop is not enough by itself.</b> Call
/// <c>Evaluate</c> every tick with a live snapshot, not a delta — exactly as
/// <c>alive</c>/<c>pending</c> already work. A request dispatched BEFORE the
/// tail state began can still resolve into a confirmed arrival DURING the
/// tail (BurstSpawnService.SpawnPair is async), so <c>alive</c> can rise for
/// a reason that has nothing to do with anything dying, and a death and a
/// late arrival can land in the very same call and net to zero visible
/// change in <c>alive</c> at all. <c>Evaluate</c> therefore also takes
/// <paramref name="arrivedTotal"/>, a monotonic lifetime count of confirmed
/// arrivals the caller maintains (<see cref="ZombieSpawnService.ConfirmedArrivals"/>),
/// and nets out exactly the arrivals that happened since the PREVIOUS call
/// before asking whether <c>alive</c> fell short of what that alone would
/// explain. See
/// ZombieWavePolicyTests.AMaskedDeathCoincidingWithALateArrivalStillResetsTheStuckTimer
/// for the case this specifically guards: one infected dies the exact same
/// call a different, already-in-flight one arrives, <c>alive</c> stays
/// numerically unchanged, and the watchdog must still see progress.</para>
///
/// <para><b>Saturated waves arm the watchdog too (2026-10-06 GPU finding,
/// same day).</b> The progress fix above still only ARMED once the wave was
/// fully spawned (<c>remainingToSpawn == 0</c>). A GPU raid the same day
/// showed that is not enough: wave 1 of 30 (<c>WaveStartSize</c> raised to 30
/// by the very next commit), <c>MaxAlive</c> 20, a player who could not
/// land a kill — 20 infected alive, 10 still to spawn, no infected death for
/// over four minutes, and the watchdog never fired at all, because
/// <c>remainingToSpawn</c> never reached 0. In v0 the same kind of stall
/// resolved after 100s, because <c>WaveStartSize</c> (8) sat below
/// <c>MaxAlive</c>, so every wave reached the fully-spawned tail while still
/// comfortably under the cap — the bigger-wave defaults below are what
/// exposed this gap. The real failure condition was never "fully spawned";
/// it is SATURATED: no spawn is currently possible, either because
/// everything has already been requested OR because
/// <c>alive + pending</c> already fills this call's own target
/// (<c>min(MaxAlive, remainingToSpawn + alive)</c>, the same formula the
/// ordinary refill spawn-decision uses). Both are the identical failure mode
/// from the watchdog's point of view: only an infected death can free the
/// room for the next spawn. <c>Evaluate</c> now arms/re-arms on saturation,
/// not merely on the fully-spawned tail, using the exact same
/// progress-not-duration clock described above. When it fires mid-wave
/// (<c>remainingToSpawn &gt; 0</c>), it clears only the alive infected and
/// stays in <c>Phase.Wave</c> — the wave is not over, only unstuck — so the
/// remainder resumes spawning in the very same call, exactly as an ordinary
/// death would have let it. A wave that is still filling up
/// (<c>alive + pending</c> below target) is never saturated and must never
/// arm this; see
/// ZombieWavePolicyTests.AWaveStillFillingUpDoesNotArmTheWatchdogEvenForALongTime.</para>
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
        /// stranded on an unreachable NavMesh island: once the wave is
        /// SATURATED — nothing left to spawn, OR alive+pending already fills
        /// the policy's own target so no further spawn can go out right now —
        /// and no infected has died for this long, Evaluate signals
        /// Result.ForceRetireStuckInfected and clears whatever is alive
        /// anyway, rather than leaving progress stuck forever (at the tail,
        /// the whole endless-wave mode; mid-wave, just this wave's
        /// remainder). The clock measures PROGRESS, not merely duration: it
        /// restarts on every infected death, so a wave the player is still
        /// actively (if slowly) clearing is never force-cleared out from
        /// under them — only saturation with nothing dying for the whole
        /// ceiling is "stuck" (progress-not-duration fixed 2026-10-06 after
        /// the first GPU raid cleared a wave mid-fight; saturation arming
        /// added the same day after a second GPU raid showed a wave capped
        /// at MaxAlive with spawns still left never armed this at all — see
        /// this class's own doc comment, "Watchdog progress, not duration"
        /// and "Saturated waves arm the watchdog too").
        /// 0 or negative disables the watchdog entirely (the wave then waits
        /// for every infected to die naturally, with no recovery if one
        /// cannot reach the player).
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
        /// NaN unless the current wave is SATURATED — nothing left to spawn
        /// (the old "tail"), or alive+pending already fills the target
        /// formula so no further spawn can go out right now — while at least
        /// one infected is still alive, in which case this is the `now`
        /// saturation was most recently (re)armed — the stuck-wave
        /// watchdog's clock counts from here, not from wave start or from
        /// whenever saturation first began. Set to `now` the FIRST time
        /// saturation is observed, and again every time Evaluate detects an
        /// infected death since the previous call (AnyDeathSinceLastCall,
        /// computed from LastAlive/LastArrivedTotal below) — "stuck" means
        /// no death has reset this for StuckWaveTimeoutSeconds, not merely
        /// that saturation has lasted that long. Reset to NaN the moment
        /// saturation is no longer true (room opened up below the target,
        /// the wave cleared normally, or the watchdog force-cleared it).
        /// </summary>
        internal float StuckAliveSince;

        /// <summary>
        /// `alive` as observed on the PREVIOUS Evaluate call (0 before the
        /// first). Paired with <see cref="LastArrivedTotal"/> to compute
        /// whether an infected died since that call: see this class's own
        /// doc comment, "Why a raw alive-count drop is not enough by
        /// itself". Updated on every call, including the self-recursive
        /// ones below, so a fresh wave or a forced retirement never leaves
        /// it stale.
        /// </summary>
        internal int LastAlive;

        /// <summary>
        /// The caller's <c>arrivedTotal</c> (see Evaluate's own parameter doc)
        /// as observed on the PREVIOUS call. The two LastAlive/LastArrivedTotal
        /// fields together are a pure, self-contained "did anything die"
        /// detector: no EFT/Unity types, no dependency on the kill-feed or
        /// scoring pipeline.
        /// </summary>
        internal int LastArrivedTotal;
    }

    internal struct Result
    {
        /// <summary>0 or 1: whether ZombieMode should issue one spawn request this call.</summary>
        internal int SpawnNow;
        internal int WaveNumber;

        /// <summary>Zombies of the current wave not yet requested, still pending, or alive.</summary>
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
        LastAlive = 0,
        LastArrivedTotal = 0,
    };

    /// <summary>Opening break uses the ordinary countdown, without pretending a wave was cleared.</summary>
    internal static State InitialCountdown()
    {
        var state = Initial();
        state.WaveNumber = 0;
        state.Phase = Phase.Intermission;
        // The mode starts this clock only once its local body/team is ready.
        state.PhaseEndsAt = float.PositiveInfinity;
        return state;
    }

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

    /// <param name="arrivedTotal">
    /// Monotonic lifetime count of infected confirmed alive on arrival this
    /// raid (<see cref="ZombieSpawnService.ConfirmedArrivals"/>). Defaults to
    /// 0 for callers (mostly tests) that never exercise the stuck-wave
    /// watchdog's death-progress tracking — every OTHER behaviour of this
    /// method is unaffected by it. A real caller (ZombieMode.Tick) must pass
    /// its live reading every call, and every self-recursive call below
    /// forwards the SAME value it received rather than letting it default,
    /// since defaulting mid-recursion would wrongly zero out
    /// State.LastArrivedTotal's baseline for every future call this raid.
    /// </param>
    internal static Result Evaluate(ref State state, Config config, float now, int alive, int pending, int arrivedTotal = 0)
    {
        if (float.IsNaN(now) || float.IsInfinity(now))
            throw new ArgumentOutOfRangeException(nameof(now));

        alive = Math.Max(0, alive);
        pending = Math.Max(0, pending);
        arrivedTotal = Math.Max(0, arrivedTotal);
        int maxAlive = Math.Max(1, config.MaxAlive);
        float intermissionSeconds = Math.Max(0f, config.IntermissionSeconds);
        float spawnInterval = Math.Max(0.01f, config.SpawnIntervalSeconds);

        // Watchdog progress signal (consulted only in the tail branch below,
        // but computed unconditionally so State.LastAlive/LastArrivedTotal
        // stay current for whichever call next needs them — including the
        // self-recursive ones, which pass the SAME alive/arrivedTotal they
        // received and so recompute this as a harmless, idempotent no-op).
        // arrivedSinceLastCall nets out confirmed arrivals that happened
        // since the PREVIOUS call before asking whether alive fell short of
        // what that alone would explain — see this class's own doc comment,
        // "Why a raw alive-count drop is not enough by itself".
        int arrivedSinceLastCall = Math.Max(0, arrivedTotal - state.LastArrivedTotal);
        int expectedAliveFromProgress = state.LastAlive + arrivedSinceLastCall;
        bool anyDeathSinceLastCall = alive < expectedAliveFromProgress;
        state.LastAlive = alive;
        state.LastArrivedTotal = arrivedTotal;

        int spawnNow = 0;
        int waveSize = WaveSize(config, state.WaveNumber);

        if (state.Phase == Phase.Wave)
        {
            int remainingToSpawn = Math.Max(0, waveSize - state.SpawnedThisWave);
            // The policy's own target formula (also used for the spawn
            // decision below): the most that may be alive+pending right now,
            // capped by MaxAlive AND by what this wave actually has left
            // (remainingToSpawn + alive) — see this class's own doc comment,
            // "This is already a CONTINUOUS refill".
            int target = Math.Min(maxAlive, remainingToSpawn + alive);

            if (remainingToSpawn == 0 && alive <= 0 && pending == 0)
            {
                // Dispatch is not arrival. All requests must settle before a
                // refill/body swap or protected team teardown can begin.
                state.Phase = Phase.Intermission;
                state.PhaseEndsAt = now + intermissionSeconds;
                state.StuckAliveSince = float.NaN;
                // Symmetric with the Intermission branch below: a zero-length
                // intermission must advance straight through to the next wave
                // (and spawn its first zombie) in this same call, not wait a
                // whole extra Tick for the transition to be noticed. Forward
                // arrivedTotal UNCHANGED — this is the same logical call, not
                // a new observation, and defaulting it would wrongly re-zero
                // State.LastArrivedTotal's baseline (see Evaluate's own
                // parameter doc).
                return Evaluate(ref state, config, now, alive, pending, arrivedTotal);
            }

            // Saturated: no spawn is possible RIGHT NOW, for either of two
            // reasons — everything of this wave has already been requested
            // (remainingToSpawn == 0, the old "tail" case), or alive+pending
            // already fills this call's own target (pinned at the MaxAlive
            // cap with more of the wave still to come, or pinned at what
            // little is left late in the wave). Both are the SAME failure
            // mode from the watchdog's point of view: nothing but an infected
            // death can free room for the next spawn, so without a ceiling, a
            // single alive-but-unreachable infected (wedged in geometry,
            // stranded off a connected NavMesh island — AGENTS.md documents
            // exactly this class of problem) would freeze progress — at the
            // tail, the whole endless-wave mode; mid-wave, just this wave's
            // remainder — for the rest of the raid.
            //
            // A wave that is still filling up (alive+pending below target,
            // remainingToSpawn > 0) is NOT saturated and must never arm this:
            // see ZombieWavePolicyTests.AWaveStillFillingUpDoesNotArmTheWatchdogEvenForALongTime.
            // The 2026-10-06 GPU raid is exactly the mid-wave case this adds:
            // wave 1 of 30, MaxAlive 20, 20 alive and 10 still to spawn, zero
            // infected deaths for over four minutes — the OLD condition
            // (remainingToSpawn == 0 only) never armed at all, because the
            // wave was never "fully spawned" yet. In v0 the same situation
            // resolved after 100s because WaveStartSize (8) was below
            // MaxAlive, so every wave reached remainingToSpawn == 0 while
            // still comfortably under the cap.
            bool saturated = remainingToSpawn == 0 || alive + pending >= target;

            if (saturated && alive > 0)
            {
                // Arm (first time saturation is observed) OR re-arm (an
                // infected died since the previous call — progress, not mere
                // duration; see this class's own doc comment, "Watchdog
                // progress, not duration"). Either way the ceiling counts
                // from NOW, not from whenever saturation originally began.
                if (float.IsNaN(state.StuckAliveSince) || anyDeathSinceLastCall)
                    state.StuckAliveSince = now;

                float stuckTimeout = config.StuckWaveTimeoutSeconds;
                if (stuckTimeout > 0f && now - state.StuckAliveSince >= stuckTimeout)
                {
                    // This class owns no bot identities to retire itself —
                    // it only signals ForceRetireStuckInfected so the caller
                    // (ZombieMode) can administratively kill whatever is
                    // still alive right now (ZombieSpawnService), then this
                    // call proceeds with alive forced to 0 from here on (the
                    // caller is about to make that true for real).
                    // arrivedTotal is forwarded UNCHANGED (not the synthetic
                    // alive=0): the recursive call's own progress tracking
                    // must still measure against the REAL arrival count, not
                    // a value that never happened, even though its Result is
                    // computed with alive forced to 0.
                    state.StuckAliveSince = float.NaN;
                    // Keep the same wave and re-evaluate its real pending
                    // requests. The normal clearance rule above alone decides
                    // whether retirement ended the wave: a delayed final body
                    // still belongs to it. Mid-wave retirement instead frees
                    // capacity to request the remainder in this same call.
                    var forced = Evaluate(ref state, config, now, 0, pending, arrivedTotal);
                    forced.ForceRetireStuckInfected = true;
                    return forced;
                }
            }
            else
            {
                state.StuckAliveSince = float.NaN;
            }

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
                // whole extra Tick for the transition to be noticed. Forward
                // arrivedTotal unchanged (same reasoning as the other two
                // self-recursive calls above).
                return Evaluate(ref state, config, now, alive, pending, arrivedTotal);
            }
        }

        int remaining = Math.Max(0, waveSize - state.SpawnedThisWave) + alive + pending;
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
