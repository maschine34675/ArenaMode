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
