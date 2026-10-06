using ArenaMode.Modes;

namespace ArenaMode.Tests;

public sealed class ZombieRefillPolicyTests
{
    private static ZombieRefillPolicy.Config Config(int maxAttempts = 20)
        => new ZombieRefillPolicy.Config { MaxAttempts = maxAttempts };

    [Fact]
    public void DoesNotFireDuringAWave()
    {
        var state = ZombieRefillPolicy.Initial();
        var config = Config();

        for (int i = 0; i < 50; i++)
        {
            var result = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Wave, playerEligible: true);
            Assert.False(result.ShouldAttempt);
        }
    }

    [Fact]
    public void FiresExactlyOnceOnEnteringIntermissionWhilePlayerIsEligible()
    {
        var state = ZombieRefillPolicy.Initial();
        var config = Config();

        // A few ticks of Wave first — the realistic lead-in; must not arm anything.
        for (int i = 0; i < 5; i++)
            ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Wave, playerEligible: true);

        var first = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.True(first.ShouldAttempt);
        ZombieRefillPolicy.ReportStarted(ref state);

        // The swap is now in flight; must not ask again for the rest of
        // this intermission, however many more ticks land before it ends.
        for (int i = 0; i < 50; i++)
        {
            var later = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
            Assert.False(later.ShouldAttempt);
        }
    }

    [Fact]
    public void RetriesOnLaterTicksWhilePlayerIsIneligibleThenFiresOnceEligible()
    {
        // The exact scenario item 3 calls out: Zombie's instant-respawn
        // pipeline can still be finishing exactly when the wave's last
        // infected dies (measured ~0.2s on the GPU host). Two ineligible
        // ticks, then eligible — must fire on the third, and the two
        // ineligible ticks must not have consumed any of the bounded
        // attempts budget (confirmed below with a tight budget of 1).
        var state = ZombieRefillPolicy.Initial();
        var config = Config(maxAttempts: 1);

        var duringRespawn1 = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: false);
        Assert.False(duringRespawn1.ShouldAttempt);
        var duringRespawn2 = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: false);
        Assert.False(duringRespawn2.ShouldAttempt);

        var nowEligible = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.True(nowEligible.ShouldAttempt);
    }

    [Fact]
    public void BoundedRetriesGiveUpAfterMaxAttemptsWithoutStarting()
    {
        // A persistently refusing underlying path (AtRoundBreak returning
        // false for some unrelated transient reason every single time) must
        // not retry without end within one intermission.
        var state = ZombieRefillPolicy.Initial();
        var config = Config(maxAttempts: 3);

        int attempts = 0;
        for (int i = 0; i < 10; i++)
        {
            var result = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
            if (result.ShouldAttempt)
            {
                attempts++;
                ZombieRefillPolicy.ReportNotStarted(ref state);
            }
        }

        Assert.Equal(3, attempts);

        // Further ticks in the SAME intermission must stay silent — gave up.
        var afterGivingUp = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.False(afterGivingUp.ShouldAttempt);
    }

    [Fact]
    public void ArmsAgainOnTheNextIntermission()
    {
        var state = ZombieRefillPolicy.Initial();
        var config = Config();

        var first = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.True(first.ShouldAttempt);
        ZombieRefillPolicy.ReportStarted(ref state);

        var waveAgain = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Wave, playerEligible: true);
        Assert.False(waveAgain.ShouldAttempt);

        // Second intermission: fires again, independent of the first.
        var second = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.True(second.ShouldAttempt);
    }

    [Fact]
    public void GivingUpOnOneIntermissionDoesNotCarryIntoTheNext()
    {
        // The bounded-retry ceiling is per-intermission, not per-raid: a
        // give-up must not poison every later intermission for the rest of
        // the raid.
        var state = ZombieRefillPolicy.Initial();
        var config = Config(maxAttempts: 1);

        var first = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.True(first.ShouldAttempt);
        ZombieRefillPolicy.ReportNotStarted(ref state); // the one allowed attempt fails to start

        var givenUp = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.False(givenUp.ShouldAttempt);

        ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Wave, playerEligible: true);
        var secondIntermission = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.True(secondIntermission.ShouldAttempt);
    }

    [Fact]
    public void TheRefillPolicyNeverFiresWhileThePlayerIsIneligible()
    {
        // Fault control for item 3's own named requirement — "the refill
        // must happen only while the player is alive and not mid-death/
        // respawn." Confirmed by hand that temporarily dropping
        // `playerEligible` from Evaluate's ShouldAttempt expression makes
        // this test fail (every one of the 20 ineligible ticks below fires
        // instead of none); restoring it passes again (see the commit
        // message for the red/green pair).
        var state = ZombieRefillPolicy.Initial();
        var config = Config();

        for (int i = 0; i < 20; i++)
        {
            var result = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: false);
            Assert.False(result.ShouldAttempt);
        }
    }

    [Fact]
    public void TheRefillPolicyDoesNotRefireAfterAlreadyStartedThisIntermission()
    {
        // Fault control for the "exactly once" contract. Confirmed by hand
        // that making ReportStarted a no-op (never clearing State.Armed)
        // makes this test fail — the second Evaluate below also returns
        // ShouldAttempt=true; restoring it passes again (see the commit
        // message for the red/green pair).
        var state = ZombieRefillPolicy.Initial();
        var config = Config();

        var first = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.True(first.ShouldAttempt);
        ZombieRefillPolicy.ReportStarted(ref state);

        var second = ZombieRefillPolicy.Evaluate(ref state, config, ZombieWavePolicy.Phase.Intermission, playerEligible: true);
        Assert.False(second.ShouldAttempt);
    }
}
