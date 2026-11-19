using ArenaMode.Modes;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

public sealed class ZombieRoleMixTests
{
    [Fact]
    public void WeightsSumToTheTotalWeightTheDrawReliesOn()
    {
        int sum = 0;
        foreach (var (_, weight) in ZombieRoleMix.Weights)
            sum += weight;
        Assert.Equal(ZombieRoleMix.TotalWeight, sum);
    }

    [Fact]
    public void ZeroShooterPercentNeverDrawsHard()
    {
        var rng = new Random(12345);
        for (int i = 0; i < 1000; i++)
            Assert.NotEqual(ZombieRoleMix.Hard, ZombieRoleMix.Next(rng, 0, i / 100 + 1, ArenaDifficultyStep.Maximum).Difficulty);
    }

    [Fact]
    public void HundredPercentShooterAlwaysDrawsHard()
    {
        var rng = new Random(98765);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(ZombieRoleMix.Hard, ZombieRoleMix.Next(rng, 100, i / 100 + 1, ArenaDifficultyStep.Maximum).Difficulty);
    }

    [Fact]
    public void TheSameSeedProducesTheSameSequenceOfDraws()
    {
        var a = new Random(42);
        var b = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var drawA = ZombieRoleMix.Next(a, 50, i / 20 + 1, ArenaDifficultyStep.Maximum);
            var drawB = ZombieRoleMix.Next(b, 50, i / 20 + 1, ArenaDifficultyStep.Maximum);
            Assert.Equal(drawA.Role, drawB.Role);
            Assert.Equal(drawA.Difficulty, drawB.Difficulty);
        }
    }

    [Fact]
    public void DifferentSeedsEventuallyDisagree()
    {
        // Not a hard guarantee for any two seeds, but true for these two —
        // guards against an implementation that ignores the rng entirely.
        var a = new Random(1);
        var b = new Random(2);
        bool sawDifference = false;
        for (int i = 0; i < 200 && !sawDifference; i++)
        {
            var drawA = ZombieRoleMix.Next(a, 50, 1, ArenaDifficultyStep.Maximum);
            var drawB = ZombieRoleMix.Next(b, 50, 1, ArenaDifficultyStep.Maximum);
            if (drawA.Role != drawB.Role || drawA.Difficulty != drawB.Difficulty)
                sawDifference = true;
        }

        Assert.True(sawDifference);
    }

    [Fact]
    public void OnlyTheFourGenericInfectedRolesAreEverDrawn()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            ZombieRoleMix.InfectedAssault, ZombieRoleMix.InfectedPmc,
            ZombieRoleMix.InfectedCivil, ZombieRoleMix.InfectedLaborant,
        };
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var draw = ZombieRoleMix.Next(rng, 25, 1, ArenaDifficultyStep.Maximum);
            Assert.Contains(draw.Role, allowed);
            Assert.Contains(draw.Difficulty, new[] { ZombieRoleMix.Easy, ZombieRoleMix.Normal, ZombieRoleMix.Hard });
        }
    }

    [Fact]
    public void TheRoleDistributionMatchesTheConfiguredWeightsWithinTolerance()
    {
        var rng = new Random(2026);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        const int draws = 20000;
        for (int i = 0; i < draws; i++)
        {
            var role = ZombieRoleMix.Next(rng, 0, 1, ArenaDifficultyStep.Maximum).Role;
            counts[role] = counts.GetValueOrDefault(role) + 1;
        }

        foreach (var (role, weight) in ZombieRoleMix.Weights)
        {
            double expected = draws * weight / (double)ZombieRoleMix.TotalWeight;
            double actual = counts.GetValueOrDefault(role);
            // Generous tolerance: this guards against a badly broken weight
            // table (e.g. uniform draws), not statistical precision.
            Assert.InRange(actual, expected * 0.85, expected * 1.15);
        }
    }

    [Fact]
    public void ANullRngIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ZombieRoleMix.Next(null!, 50, 1, ArenaDifficultyStep.Maximum));
    }

    [Theory]
    [InlineData(ArenaDifficultyStep.Maximum)]
    [InlineData(ArenaDifficultyStep.Reduced)]
    public void ExactZeroAndHundredShooterSharesStayExactAtEveryWave(ArenaDifficultyStep step)
    {
        var rng = new Random(5);
        for (int wave = 1; wave <= 40; wave++)
            for (int i = 0; i < 100; i++)
            {
                Assert.NotEqual(ZombieRoleMix.Hard, ZombieRoleMix.Next(rng, 0, wave, step).Difficulty);
                Assert.Equal(ZombieRoleMix.Hard, ZombieRoleMix.Next(rng, 100, wave, step).Difficulty);
            }
    }

    [Fact]
    public void MaximumDrawsMoreShootersInLateWavesThanReducedAndNoMoreSlowOnesThanTheRhythm()
    {
        int Count(ArenaDifficultyStep step, int wave, string difficulty)
        {
            var rng = new Random(77);
            int n = 0;
            for (int i = 0; i < 20000; i++)
                if (ZombieRoleMix.Next(rng, 20, wave, step).Difficulty == difficulty) n++;
            return n;
        }

        // Wave 11 at Maximum: shooter share doubled (40 %), slow share faded to 0.
        Assert.InRange(Count(ArenaDifficultyStep.Maximum, 11, ZombieRoleMix.Hard), 7200, 8800);
        Assert.Equal(0, Count(ArenaDifficultyStep.Maximum, 11, ZombieRoleMix.Easy));
        // Reduced stays on the flat 20 % and keeps the rhythm (wave 11 is 20 % of the melee remainder).
        Assert.InRange(Count(ArenaDifficultyStep.Reduced, 11, ZombieRoleMix.Hard), 3200, 4800);
        Assert.InRange(Count(ArenaDifficultyStep.Reduced, 11, ZombieRoleMix.Easy), 2400, 3900);
    }
}
