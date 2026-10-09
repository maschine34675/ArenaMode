using ArenaMode.Modes;

namespace ArenaMode.Tests;

public sealed class ZombieWavePolicyTests
{
    private static ZombieWavePolicy.Config Config(int maxAlive = 20, int start = 8, int growth = 4,
        float intermission = 10f, float interval = 1f, float stuckWaveTimeout = 0f)
        => new ZombieWavePolicy.Config
        {
            MaxAlive = maxAlive,
            WaveStartSize = start,
            WaveGrowth = growth,
            IntermissionSeconds = intermission,
            SpawnIntervalSeconds = interval,
            StuckWaveTimeoutSeconds = stuckWaveTimeout,
        };

    [Fact]
    public void WaveSizeGrowsLinearlyFromTheStartSize()
    {
        var config = Config(start: 8, growth: 4);
        Assert.Equal(8, ZombieWavePolicy.WaveSize(config, 1));
        Assert.Equal(12, ZombieWavePolicy.WaveSize(config, 2));
        Assert.Equal(16, ZombieWavePolicy.WaveSize(config, 3));
        Assert.Equal(8 + 4 * 9, ZombieWavePolicy.WaveSize(config, 10));

        // Zero growth keeps every wave the same size.
        Assert.Equal(8, ZombieWavePolicy.WaveSize(Config(start: 8, growth: 0), 50));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(10f)]
    public void FinalPendingRequestKeepsItsWaveUntilItArrivesAndDies(float intermission)
    {
        var config = Config(start: 1, growth: 0, intermission: intermission);
        var state = ZombieWavePolicy.Initial();
        Assert.Equal(1, ZombieWavePolicy.Evaluate(ref state, config, 0, 0, 0).SpawnNow);

        foreach (float now in new[] { .1f, 5f, 25f, 100f })
        {
            var waiting = ZombieWavePolicy.Evaluate(ref state, config, now, 0, 1);
            Assert.Equal(ZombieWavePolicy.Phase.Wave, waiting.Phase);
            Assert.Equal(1, waiting.WaveNumber);
            Assert.Equal(1, waiting.Remaining);
            Assert.Equal(0, waiting.SpawnNow);
            Assert.False(waiting.ForceRetireStuckInfected);
        }
        var arrived = ZombieWavePolicy.Evaluate(ref state, config, 101, 1, 0, 1);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, arrived.Phase);
        var cleared = ZombieWavePolicy.Evaluate(ref state, config, 102, 0, 0, 1);
        Assert.Equal(intermission == 0 ? ZombieWavePolicy.Phase.Wave : ZombieWavePolicy.Phase.Intermission, cleared.Phase);
        Assert.Equal(intermission == 0 ? 2 : 1, cleared.WaveNumber);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(10f)]
    public void WatchdogRetiresAliveBodiesWithoutForgivingPendingTail(float intermission)
    {
        var config = Config(start: 2, growth: 0, interval: .1f,
            intermission: intermission, stuckWaveTimeout: 1);
        var state = ZombieWavePolicy.Initial();
        ZombieWavePolicy.Evaluate(ref state, config, 0, 0, 0);
        ZombieWavePolicy.Evaluate(ref state, config, .1f, 1, 0, 1);
        ZombieWavePolicy.Evaluate(ref state, config, .2f, 1, 1, 1);
        var forced = ZombieWavePolicy.Evaluate(ref state, config, 2, 1, 1, 1);
        Assert.True(forced.ForceRetireStuckInfected);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, forced.Phase);
        Assert.Equal(1, forced.WaveNumber);
        Assert.Equal(1, forced.Remaining);
        Assert.Equal(0, forced.SpawnNow);
        var waiting = ZombieWavePolicy.Evaluate(ref state, config, 30, 0, 1, 1);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, waiting.Phase);
        Assert.False(waiting.ForceRetireStuckInfected);
        var arrived = ZombieWavePolicy.Evaluate(ref state, config, 31, 1, 0, 2);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, arrived.Phase);
        Assert.False(arrived.ForceRetireStuckInfected);
        var cleared = ZombieWavePolicy.Evaluate(ref state, config, 31.1f, 0, 0, 2);
        Assert.Equal(intermission == 0 ? 2 : 1, cleared.WaveNumber);
        Assert.Equal(intermission == 0 ? ZombieWavePolicy.Phase.Wave : ZombieWavePolicy.Phase.Intermission, cleared.Phase);
    }

    [Fact]
    public void DefaultWaveSizingMatchesTheRequestedBiggerWaveDefaults()
    {
        // Settings.cs defaults as of 2026-10-06 (item 2, "bigger waves"):
        // WaveStartSize 30, WaveGrowth 10. This is the sizing formula's own
        // arithmetic at exactly those numbers, independent of the Settings/
        // BepInEx binding (which this pure-policy test project cannot load —
        // see ArenaMode.Tests.csproj's own header comment).
        var config = Config(start: 30, growth: 10);
        Assert.Equal(30, ZombieWavePolicy.WaveSize(config, 1));
        Assert.Equal(40, ZombieWavePolicy.WaveSize(config, 2));
        Assert.Equal(50, ZombieWavePolicy.WaveSize(config, 3));
        Assert.Equal(30 + 10 * 9, ZombieWavePolicy.WaveSize(config, 10));
    }

    [Fact]
    public void ThirtyZombieWaveFillsToMaxAliveThenRefillsOnePerIntervalAsAliveDrops()
    {
        // Item 2 ("bigger waves"): within a wave, many more zombies spawn —
        // not necessarily all at once, but without any pause — bounded by
        // MaxAlive. New defaults: WaveStartSize 30, WaveGrowth 10,
        // SpawnIntervalSeconds 0.75; MaxAlive unchanged at 20.
        var config = Config(maxAlive: 20, start: 30, growth: 10, intermission: 10f, interval: 0.75f);
        var state = ZombieWavePolicy.Initial();
        int alive = 0, spawnedTotal = 0;
        float now = 0f;

        // Ramp-up: every spawn is confirmed alive promptly (the realistic
        // case — BurstSpawnService's own round trip is well inside 0.75s —
        // so alive itself climbs to the cap, not merely pending).
        for (int i = 0; i < 400 && alive < 20; i++)
        {
            var result = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0);
            if (result.SpawnNow > 0)
            {
                spawnedTotal += result.SpawnNow;
                alive += result.SpawnNow;
            }
            now += 0.1f;
        }

        Assert.Equal(20, alive);
        Assert.Equal(20, spawnedTotal); // exactly MaxAlive so far; 10 of the 30 still to come

        // At the cap, with the wave far from exhausted: no further spawn,
        // and critically no pause of the raid (Phase stays Wave, no
        // force-retire) — MaxAlive is a performance ceiling, not a stop.
        var atCap = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0);
        Assert.Equal(0, atCap.SpawnNow);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, atCap.Phase);
        Assert.False(atCap.ForceRetireStuckInfected);

        // Each of the remaining 10 kills must be refilled within one
        // SpawnIntervalSeconds, continuously, with no pause, until the
        // wave's full 30 have gone out.
        for (int death = 0; death < 10; death++)
        {
            alive -= 1;
            bool refilled = false;
            for (int tick = 0; tick < 20 && !refilled; tick++)
            {
                now += 0.1f;
                var result = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0);
                if (result.SpawnNow > 0)
                {
                    spawnedTotal += result.SpawnNow;
                    alive += result.SpawnNow;
                    refilled = true;
                }
            }
            Assert.True(refilled, $"Expected a refill within {config.SpawnIntervalSeconds}s of kill #{death}.");
        }

        Assert.Equal(30, spawnedTotal);
        Assert.Equal(20, alive);
    }

    [Fact]
    public void SpawningStopsAtTheConfiguredMaxAliveEvenWithManyLeftToSpawn()
    {
        // 100-strong wave, but only 5 may ever be alive/pending at once. The
        // bug this guards: a cap that only looked at MaxAlive without
        // comparing it against what the wave has left could ask for more than
        // five anyway once the wave was nearly spawned out.
        var config = Config(maxAlive: 5, start: 100, growth: 0, intermission: 0f, interval: 0f);
        var state = ZombieWavePolicy.Initial();
        int alive = 0, pending = 0, spawnedTotal = 0;
        float now = 0f;

        for (int i = 0; i < 200 && spawnedTotal < 5; i++)
        {
            var result = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending);
            if (result.SpawnNow > 0)
            {
                spawnedTotal += result.SpawnNow;
                pending += result.SpawnNow;
            }

            now += 0.05f;
        }

        Assert.Equal(5, spawnedTotal);
        Assert.Equal(5, pending);

        // With all five in flight (pending, none yet alive), nothing more is requested.
        for (int i = 0; i < 20; i++)
        {
            var result = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending);
            Assert.Equal(0, result.SpawnNow);
            now += 0.05f;
        }

        // Confirming one as alive (pending -> alive) still keeps the ceiling at 5: no new request appears.
        pending -= 1;
        alive += 1;
        var afterConfirm = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending);
        Assert.Equal(0, afterConfirm.SpawnNow);
    }

    [Fact]
    public void TheWaveEndsOnlyOnceEverythingIsSpawnedAndNothingIsAlive()
    {
        // Small wave so the whole thing can be driven by hand.
        var config = Config(maxAlive: 20, start: 2, growth: 0, intermission: 10f, interval: 0f);
        var state = ZombieWavePolicy.Initial();

        // Spawn both. (now advances slightly between the two calls: the
        // interval floors to 0.01s even when SpawnIntervalSeconds is 0, so
        // asking twice at the exact same instant would only spawn once.)
        var r1 = ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 0, pending: 0);
        Assert.Equal(1, r1.SpawnNow);
        var r2 = ZombieWavePolicy.Evaluate(ref state, config, now: 0.02f, alive: 1, pending: 0);
        Assert.Equal(1, r2.SpawnNow);

        // Everything of the wave is now spawned (2/2), but one is still alive:
        // must NOT end the wave yet.
        var stillAlive = ZombieWavePolicy.Evaluate(ref state, config, now: 1f, alive: 1, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillAlive.Phase);
        Assert.Equal(0, stillAlive.SpawnNow);

        // Nothing alive, but (hypothetically) something still unspawned must
        // also not end the wave — simulated directly on a fresh state where
        // spawning has not kept up.
        var partial = ZombieWavePolicy.Initial();
        var partialResult = ZombieWavePolicy.Evaluate(ref partial, config, now: 0f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, partialResult.Phase);
        Assert.True(partialResult.Remaining > 0);

        // Both conditions true: the wave ends and intermission begins.
        var cleared = ZombieWavePolicy.Evaluate(ref state, config, now: 2f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, cleared.Phase);
        Assert.Equal(1, cleared.WaveNumber); // still reports the wave that just ended
    }

    [Fact]
    public void IntermissionLastsExactlyItsConfiguredDurationThenAdvancesTheWave()
    {
        var config = Config(maxAlive: 20, start: 1, growth: 0, intermission: 10f, interval: 0f);
        var state = ZombieWavePolicy.Initial();
        ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 0, pending: 0); // spawns the only zombie
        var cleared = ZombieWavePolicy.Evaluate(ref state, config, now: 1f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, cleared.Phase);
        Assert.Equal(10f, cleared.IntermissionRemaining, 3);

        var midway = ZombieWavePolicy.Evaluate(ref state, config, now: 6f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, midway.Phase);
        Assert.Equal(5f, midway.IntermissionRemaining, 3);
        Assert.Equal(1, midway.WaveNumber);

        // Exactly at the deadline: the next wave has begun and already spawned once.
        var next = ZombieWavePolicy.Evaluate(ref state, config, now: 11f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, next.Phase);
        Assert.Equal(2, next.WaveNumber);
        Assert.Equal(1, next.SpawnNow);
    }

    [Fact]
    public void ZeroLengthIntermissionAdvancesAndSpawnsInTheSameCall()
    {
        var config = Config(maxAlive: 20, start: 1, growth: 0, intermission: 0f, interval: 0f);
        var state = ZombieWavePolicy.Initial();
        ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 0, pending: 0);
        var result = ZombieWavePolicy.Evaluate(ref state, config, now: 1f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, result.Phase);
        Assert.Equal(2, result.WaveNumber);
        Assert.Equal(1, result.SpawnNow);
    }

    [Fact]
    public void AnAliveButPermanentlyUnreachableInfectedWouldFreezeTheWaveWithNoWatchdog()
    {
        // The bug this guards (wave-softlock-stuck-zombie): with the watchdog
        // disabled (stuckWaveTimeout: 0, the default), a single infected that
        // never dies — wedged in geometry, stranded off a connected NavMesh
        // island — leaves remainingToSpawn at 0 and alive pinned at 1
        // forever. Nothing ends the wave. Driving the clock for a simulated
        // half hour must still show Phase stuck at Wave with no spawn
        // requested, confirming this test would catch a regression that
        // removed the watchdog entirely.
        var config = Config(maxAlive: 20, start: 1, growth: 0, intermission: 10f, interval: 0f, stuckWaveTimeout: 0f);
        var state = ZombieWavePolicy.Initial();
        var first = ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 0, pending: 0);
        Assert.Equal(1, first.SpawnNow);

        Assert.Equal(ZombieWavePolicy.Phase.Wave,
            ZombieWavePolicy.Evaluate(ref state, config, now: 1800f, alive: 1, pending: 0).Phase);
    }

    [Fact]
    public void TheStuckWaveWatchdogForceClearsAndSignalsAdministrativeRetirement()
    {
        // Same unreachable-infected setup as above, but with the watchdog
        // enabled: once the tail state ("nothing left to spawn, still alive")
        // has persisted for StuckWaveTimeoutSeconds, the wave must clear
        // anyway and the caller must be told to retire whatever is alive.
        var config = Config(maxAlive: 20, start: 1, growth: 0, intermission: 10f, interval: 0f, stuckWaveTimeout: 90f);
        var state = ZombieWavePolicy.Initial();
        ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 0, pending: 0); // spawns the only zombie

        // Stuck tail begins now (alive=1, nothing left to spawn). Short of
        // the ceiling: must NOT force-clear yet.
        var stillWaiting = ZombieWavePolicy.Evaluate(ref state, config, now: 10f, alive: 1, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillWaiting.Phase);
        Assert.False(stillWaiting.ForceRetireStuckInfected);

        // A plain "nothing alive" dip before the ceiling must not arm the
        // watchdog permanently either — confirm the timer actually resets.
        var briefDip = ZombieWavePolicy.Evaluate(ref state, config, now: 20f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, briefDip.Phase);
        Assert.False(briefDip.ForceRetireStuckInfected);

        // Re-arm for the real assertion: a fresh wave, stuck again, driven
        // past the 90s ceiling with the clock alone (alive pinned at 1 the
        // whole time, exactly the "unreachable infected" scenario).
        var next = ZombieWavePolicy.Evaluate(ref state, config, now: 30f, alive: 0, pending: 0); // wave 2 starts + spawns
        Assert.Equal(2, next.WaveNumber);
        var stillStuck = ZombieWavePolicy.Evaluate(ref state, config, now: 40f, alive: 1, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillStuck.Phase);

        var forced = ZombieWavePolicy.Evaluate(ref state, config, now: 40f + 90f, alive: 1, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, forced.Phase);
        Assert.True(forced.ForceRetireStuckInfected);
        Assert.Equal(2, forced.WaveNumber); // still reports the wave that was force-cleared
    }

    [Fact]
    public void TheStuckWaveWatchdogDoesNotFireWhileInfectedKeepDying()
    {
        // Item 1 fix, fault control: this guards exactly the 2026-10-06 GPU
        // finding — the ORIGINAL watchdog measured wall-clock time since the
        // tail state began, so a wave the player was steadily (if slowly)
        // clearing still got force-cleared once total tail duration crossed
        // StuckWaveTimeoutSeconds, even though an infected kept dying the
        // whole time. Reverting ZombieWavePolicy.Evaluate's tail branch from
        // `if (float.IsNaN(state.StuckAliveSince) || anyDeathSinceLastCall)`
        // back to `if (float.IsNaN(state.StuckAliveSince))` alone (the
        // pre-fix condition) makes this test fail at the 120s mark below —
        // confirmed by hand before this commit (see the commit message for
        // the red/green pair), matching AGENTS.md's fault-control convention.
        var config = Config(maxAlive: 20, start: 3, growth: 0, intermission: 10f, interval: 0f, stuckWaveTimeout: 100f);
        var state = ZombieWavePolicy.Initial();
        float now = 0f;
        int alive = 0;
        int arrivedTotal = 0;

        // Spawn and promptly confirm all three (realistic — see the
        // continuous-refill test above for why immediate confirmation is the
        // representative case).
        for (int i = 0; i < 3; i++)
        {
            var r = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
            Assert.Equal(1, r.SpawnNow);
            now += 0.01f;
            alive += 1;
            arrivedTotal += 1;
        }

        // Tail state begins here (remainingToSpawn=0, alive=3).
        var tailBegins = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, tailBegins.Phase);
        Assert.False(tailBegins.ForceRetireStuckInfected);

        // Death #1, 40s later (well under the 100s ceiling).
        now += 40f;
        alive -= 1;
        var afterDeath1 = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, afterDeath1.Phase);
        Assert.False(afterDeath1.ForceRetireStuckInfected);

        // Death #2, another 40s later (80s since tail began — would already
        // exceed the 100s ceiling measured from tail START if one more 40s
        // step follows with no death, proving this is actually exercising
        // the restart, not just a longer-than-100s total that never trips).
        now += 40f;
        alive -= 1;
        var afterDeath2 = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, afterDeath2.Phase);
        Assert.False(afterDeath2.ForceRetireStuckInfected);

        // No death this time: 40s after death #2 (120s since tail START,
        // already past the 100s ceiling if it had never been reset) — must
        // still NOT fire, because only 40s have passed since the LAST death.
        now += 40f;
        var stillOk = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillOk.Phase);
        Assert.False(stillOk.ForceRetireStuckInfected);

        // 90s after death #2 (still under the 100s ceiling measured from
        // the last death).
        now += 50f;
        var stillOk2 = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillOk2.Phase);
        Assert.False(stillOk2.ForceRetireStuckInfected);

        // Finally the last infected dies for real: the wave clears
        // naturally, never via the watchdog.
        now += 1f;
        alive = 0;
        var cleared = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, cleared.Phase);
        Assert.False(cleared.ForceRetireStuckInfected);
    }

    [Fact]
    public void TheStuckWaveWatchdogStillFiresWhenDeathsActuallyStop()
    {
        // Companion to the test above: the progress-based reset must not
        // disable the watchdog outright — once deaths genuinely stop, the
        // SAME configured ceiling still fires, confirmed from the moment of
        // the LAST death rather than from tail entry.
        var config = Config(maxAlive: 20, start: 2, growth: 0, intermission: 10f, interval: 0f, stuckWaveTimeout: 50f);
        var state = ZombieWavePolicy.Initial();
        float now = 0f;
        int alive = 0;
        int arrivedTotal = 0;

        for (int i = 0; i < 2; i++)
        {
            ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
            now += 0.01f;
            alive += 1;
            arrivedTotal += 1;
        }

        // Tail begins (alive=2). One death resets the clock once more.
        ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        now += 10f;
        alive -= 1;
        var afterLastDeath = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, afterLastDeath.Phase);
        Assert.False(afterLastDeath.ForceRetireStuckInfected);

        // No further deaths. Short of the ceiling measured from THAT death.
        now += 49f;
        var notYet = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, notYet.Phase);
        Assert.False(notYet.ForceRetireStuckInfected);

        // Past it: the watchdog fires.
        now += 1f;
        var forced = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, forced.Phase);
        Assert.True(forced.ForceRetireStuckInfected);
    }

    [Fact]
    public void TheStuckWaveWatchdogArmsWhenTheWaveIsSaturatedEvenWithSpawnsStillLeft()
    {
        // Fault control (a) for the 2026-10-06 GPU finding: wave 1 of 30,
        // MaxAlive 20, a player who could not land a kill. 20 zombies alive,
        // 10 left to spawn, no infected death for over four minutes — and
        // the watchdog never fired, because the OLD arming condition
        // required remainingToSpawn == 0 (fully spawned), which this wave
        // never reached. Proving the fix: revert the arming condition from
        // `saturated` (remainingToSpawn == 0 || alive + pending >= target)
        // back to the old `remainingToSpawn == 0` alone, and this test goes
        // red — the 90s ceiling below is crossed with the wave still capped
        // at 20/30 and ForceRetireStuckInfected never turns true (confirmed
        // by hand before this commit; restored, full suite green again).
        var config = Config(maxAlive: 20, start: 30, growth: 0, intermission: 10f, interval: 0.1f, stuckWaveTimeout: 90f);
        var state = ZombieWavePolicy.Initial();
        int alive = 0;
        float now = 0f;

        // Ramp up to the MaxAlive cap (20 of the 30 spawned and promptly
        // confirmed alive; 10 left in the wave).
        for (int i = 0; i < 400 && alive < 20; i++)
        {
            var r = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0);
            if (r.SpawnNow > 0)
                alive += r.SpawnNow;
            now += 0.05f;
        }
        Assert.Equal(20, alive);

        // Saturated: pinned at MaxAlive, 10 of the 30 still to spawn, nobody
        // dying. Must arm but not fire yet.
        var saturated = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, saturated.Phase);
        Assert.Equal(0, saturated.SpawnNow); // still capped, no room
        Assert.False(saturated.ForceRetireStuckInfected);

        // Short of the 90s ceiling: must not fire yet.
        now += 60f;
        var stillWaiting = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillWaiting.Phase);
        Assert.False(stillWaiting.ForceRetireStuckInfected);

        // Past the 90s ceiling with zero deaths the whole time: fires,
        // clears the alive infected, and — because 10 of the 30 are still
        // owed — the wave's remainder resumes spawning in this SAME call
        // rather than ending the wave outright.
        now += 31f;
        var forced = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0);
        Assert.True(forced.ForceRetireStuckInfected);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, forced.Phase);
        Assert.Equal(1, forced.SpawnNow);
    }

    [Fact]
    public void TheStuckWaveWatchdogKeepsResettingWhileSaturatedAndInfectedKeepDying()
    {
        // Fault control (b): companion to the saturation-arming test above.
        // Uses the SAME masked-death pattern as
        // AMaskedDeathCoincidingWithALateArrivalStillResetsTheStuckTimer
        // (one infected dies and a different, already-in-flight one is
        // confirmed arrived in the very same call, so the raw `alive` count
        // never visibly dips below the MaxAlive cap) but for the mid-wave/
        // capped saturation case instead of the fully-spawned tail —
        // deliberately so that alive+pending never drops below `target`
        // either, meaning the ONLY possible reset path is the
        // arrivedTotal-based anyDeathSinceLastCall check, not a transient
        // "briefly unsaturated" dip. Reverting the re-arm condition from
        // `NaN || anyDeathSinceLastCall` back to `NaN`-only makes this test
        // fail (red) inside the loop below (the clock stays armed from the
        // FIRST saturated call, since it is never reset to NaN by anything
        // else while the wave stays pinned at the cap, and 120s of masked
        // deaths crosses the 100s ceiling on the third cycle); confirmed by
        // hand before this commit, restored, full suite green again.
        var config = Config(maxAlive: 3, start: 50, growth: 0, intermission: 10f, interval: 0f, stuckWaveTimeout: 100f);
        var state = ZombieWavePolicy.Initial();
        float now = 0f;
        int alive = 0;
        int arrivedTotal = 0;

        // Fill to the MaxAlive cap (3 of the 50 spawned and confirmed; 47
        // left, so remainingToSpawn stays > 0 for the rest of this test —
        // this is the mid-wave saturation case, never the fully-spawned
        // tail).
        for (int i = 0; i < 3; i++)
        {
            var r = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
            Assert.Equal(1, r.SpawnNow);
            now += 0.01f;
            alive += 1;
            arrivedTotal += 1;
        }

        // Saturated: capped at MaxAlive with 47 still to spawn.
        var saturatedBegins = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, saturatedBegins.Phase);
        Assert.False(saturatedBegins.ForceRetireStuckInfected);

        // Three masked death+replacement events, 40s apart (120s in total):
        // `alive` stays pinned at the MaxAlive cap (3) throughout — never
        // dipping — while arrivedTotal climbs by 1 each time.
        for (int cycle = 0; cycle < 3; cycle++)
        {
            now += 40f;
            arrivedTotal += 1; // alive itself never changes across this call
            var afterMaskedDeath = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
            Assert.Equal(ZombieWavePolicy.Phase.Wave, afterMaskedDeath.Phase);
            Assert.False(afterMaskedDeath.ForceRetireStuckInfected);
        }

        // No further death: the SAME 100s ceiling still applies, measured
        // from the LAST masked death above (120s after saturation began —
        // already past the 100s ceiling if it had never reset).
        now += 99f;
        var stillOk = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillOk.Phase);
        Assert.False(stillOk.ForceRetireStuckInfected);

        // Past it: fires. The wave is not done (47-ish left), so it must
        // still be Phase.Wave, not Intermission.
        now += 2f;
        var forced = ZombieWavePolicy.Evaluate(ref state, config, now, alive, pending: 0, arrivedTotal);
        Assert.True(forced.ForceRetireStuckInfected);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, forced.Phase);
    }

    [Fact]
    public void AWaveStillFillingUpDoesNotArmTheWatchdogEvenForALongTime()
    {
        // Fault control (c): alive+pending below the policy's own target —
        // ramping up toward MaxAlive, or a wave whose own size never reaches
        // MaxAlive — must never arm the clock, no matter how much wall-clock
        // time passes. This is the boundary the saturation condition must
        // respect: "saturated" is `remainingToSpawn == 0 || alive + pending
        // >= target`, never merely "a long time has passed".
        var config = Config(maxAlive: 20, start: 30, growth: 0, intermission: 10f, interval: 0f, stuckWaveTimeout: 50f);
        var state = ZombieWavePolicy.Initial();

        ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 0, pending: 0); // spawns #1

        // Sit at 5 alive (far short of MaxAlive 20) for 1000s — twenty times
        // the 50s ceiling — with no deaths and no watchdog firing.
        var stillFilling = ZombieWavePolicy.Evaluate(ref state, config, now: 1000f, alive: 5, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillFilling.Phase);
        Assert.False(stillFilling.ForceRetireStuckInfected);
        // And it is actively trying to spawn more, confirming this is
        // "filling", not accidentally stuck or capped.
        Assert.Equal(1, stillFilling.SpawnNow);
    }

    [Fact]
    public void AMaskedDeathCoincidingWithALateArrivalStillResetsTheStuckTimer()
    {
        // The specific robustness case behind choosing the arrivedTotal-
        // based design over a naive "did alive drop" check (see
        // ZombieWavePolicy's own doc comment, "Why a raw alive-count drop is
        // not enough by itself"): a request dispatched BEFORE the tail began
        // resolves into a confirmed arrival DURING the tail at the exact
        // same moment a different infected dies, so the raw `alive` count
        // does not change at all across the two calls even though a real
        // death happened. A naive alive-delta watchdog would miss this
        // entirely; this one must not.
        var config = Config(maxAlive: 20, start: 2, growth: 0, intermission: 10f, interval: 0f, stuckWaveTimeout: 50f);
        var state = ZombieWavePolicy.Initial();

        // Spawn #1, confirm it immediately.
        ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 0, pending: 0, arrivedTotal: 0);
        // Spawn #2 (now both requested; #2 is left PENDING, not yet alive).
        ZombieWavePolicy.Evaluate(ref state, config, now: 0.01f, alive: 1, pending: 0, arrivedTotal: 1);

        // Tail begins at t=1.0 with #2 still only pending (not yet arrived):
        // alive=1, pending=1, arrivedTotal=1 (only #1 confirmed so far).
        var tailBegins = ZombieWavePolicy.Evaluate(ref state, config, now: 1.0f, alive: 1, pending: 1, arrivedTotal: 1);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, tailBegins.Phase);

        // 19s pass with no change: still well short of the 50s ceiling.
        var stillWaiting = ZombieWavePolicy.Evaluate(ref state, config, now: 20.0f, alive: 1, pending: 1, arrivedTotal: 1);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillWaiting.Phase);
        Assert.False(stillWaiting.ForceRetireStuckInfected);

        // The masked event: #1 dies AND #2 (the late-pending one) arrives
        // alive, in the same call. Net `alive` is unchanged (1 -> 1).
        var masked = ZombieWavePolicy.Evaluate(ref state, config, now: 20.1f, alive: 1, pending: 0, arrivedTotal: 2);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, masked.Phase);
        Assert.False(masked.ForceRetireStuckInfected);

        // 30.9s after the masked event (51s after the ORIGINAL tail start at
        // t=1.0 — past the 50s ceiling if it had never been reset). Must
        // NOT fire: the real reset point was t=20.1, not t=1.0.
        var stillOk = ZombieWavePolicy.Evaluate(ref state, config, now: 51.0f, alive: 1, pending: 0, arrivedTotal: 2);
        Assert.Equal(ZombieWavePolicy.Phase.Wave, stillOk.Phase);
        Assert.False(stillOk.ForceRetireStuckInfected);

        // 50s after the masked event with no further death: fires now.
        var forced = ZombieWavePolicy.Evaluate(ref state, config, now: 70.1f, alive: 1, pending: 0, arrivedTotal: 2);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, forced.Phase);
        Assert.True(forced.ForceRetireStuckInfected);
    }

    [Fact]
    public void APermanentlyFailingDispatchDoesNotCountAsSpawnedOrSilentlyEmptyTheWave()
    {
        // The bug this guards (zombie-wave-optimistic-slot-commit):
        // Evaluate commits a wave slot (SpawnedThisWave++) the instant it
        // DECIDES to spawn, before the caller has even attempted the real
        // dispatch. Simulating a dispatch that always fails (alive and
        // pending both stay 0 forever, mirroring ZombieSpawnService.
        // TryRequest returning false every time) without ever calling
        // ReportFailedDispatch would let the wave reach "everything
        // spawned, nothing alive" and silently clear with zero real
        // zombies ever dispatched.
        var config = Config(maxAlive: 20, start: 5, growth: 0, intermission: 10f, interval: 1f);
        var state = ZombieWavePolicy.Initial();
        float now = 0f;

        for (int i = 0; i < 50; i++)
        {
            var result = ZombieWavePolicy.Evaluate(ref state, config, now, alive: 0, pending: 0);
            if (result.SpawnNow > 0)
                ZombieWavePolicy.ReportFailedDispatch(ref state); // every dispatch fails

            Assert.Equal(ZombieWavePolicy.Phase.Wave, result.Phase); // never silently clears
            now += 1f;
        }
    }

    [Fact]
    public void OnlyConfirmedDispatchesCountTowardTheWaveWhenSomeFail()
    {
        // Mixed success/failure: only confirmed dispatches may ever reduce
        // "remaining". Every other spawn attempt in this run fails and must
        // be retried, not written off.
        var config = Config(maxAlive: 20, start: 3, growth: 0, intermission: 10f, interval: 1f);
        var state = ZombieWavePolicy.Initial();
        float now = 0f;
        int confirmed = 0;
        bool failNext = true; // alternate fail/succeed

        for (int i = 0; i < 50 && confirmed < 3; i++)
        {
            var result = ZombieWavePolicy.Evaluate(ref state, config, now, alive: 0, pending: 0);
            if (result.SpawnNow > 0)
            {
                if (failNext)
                    ZombieWavePolicy.ReportFailedDispatch(ref state);
                else
                    confirmed++;
                failNext = !failNext;
            }

            now += 1f;
        }

        Assert.Equal(3, confirmed);
        // Still open for business — exactly 3 real dispatches were needed to
        // clear a 3-zombie wave, no fewer, despite the interleaved failures.
        var afterWaveDone = ZombieWavePolicy.Evaluate(ref state, config, now, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, afterWaveDone.Phase);
    }

    [Fact]
    public void StatusTextMatchesTheDocumentedHudFormat()
    {
        var config = Config(maxAlive: 20, start: 8, growth: 4, intermission: 10f, interval: 100f);
        var state = ZombieWavePolicy.Initial();
        var wave = ZombieWavePolicy.Evaluate(ref state, config, now: 0f, alive: 4, pending: 0);
        // 8 start size, 1 spawned (the long interval means only the very first
        // one goes out at now=0), 4 alive -> remaining = (8-1) + 4 = 11.
        Assert.Equal("WAVE 1 | 11 LEFT", wave.StatusText);

        // A separate, fast-spawning config so the wave can actually clear and
        // show the intermission text.
        var fastConfig = Config(maxAlive: 20, start: 1, growth: 4, intermission: 7f, interval: 0f);
        var fastState = ZombieWavePolicy.Initial();
        ZombieWavePolicy.Evaluate(ref fastState, fastConfig, now: 0f, alive: 0, pending: 0);
        var intermission = ZombieWavePolicy.Evaluate(ref fastState, fastConfig, now: 1f, alive: 0, pending: 0);
        Assert.Equal(ZombieWavePolicy.Phase.Intermission, intermission.Phase);
        Assert.Equal("WAVE 2 IN 7s", intermission.StatusText);
    }
}
