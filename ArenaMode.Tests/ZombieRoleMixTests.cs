using ArenaMode.Modes;

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
            Assert.Equal(ZombieRoleMix.Normal, ZombieRoleMix.Next(rng, 0).Difficulty);
    }

    [Fact]
    public void HundredPercentShooterAlwaysDrawsHard()
    {
        var rng = new Random(98765);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(ZombieRoleMix.Hard, ZombieRoleMix.Next(rng, 100).Difficulty);
    }

    [Fact]
    public void TheSameSeedProducesTheSameSequenceOfDraws()
    {
        var a = new Random(42);
        var b = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var drawA = ZombieRoleMix.Next(a, 50);
            var drawB = ZombieRoleMix.Next(b, 50);
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
            var drawA = ZombieRoleMix.Next(a, 50);
            var drawB = ZombieRoleMix.Next(b, 50);
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
            var draw = ZombieRoleMix.Next(rng, 25);
            Assert.Contains(draw.Role, allowed);
            Assert.True(draw.Difficulty == ZombieRoleMix.Normal || draw.Difficulty == ZombieRoleMix.Hard);
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
            var role = ZombieRoleMix.Next(rng, 0).Role;
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
        Assert.Throws<ArgumentNullException>(() => ZombieRoleMix.Next(null!, 50));
    }
}
