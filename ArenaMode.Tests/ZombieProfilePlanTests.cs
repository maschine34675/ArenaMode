using ArenaMode.Modes;

namespace ArenaMode.Tests;

public sealed class ZombieProfilePlanTests
{
    [Fact]
    public void OpeningProfilesSumBackToTheExactWaveStartSize()
    {
        foreach (int size in new[] { 0, 1, 2, 7, 8, 13, 97, 100 })
        {
            var entries = ZombieProfilePlan.OpeningProfiles(size, shooterPercent: 0);
            Assert.Equal(size, entries.Sum(e => e.Count));
        }
    }

    [Fact]
    public void ZeroWaveStartSizeProducesNoEntries()
    {
        Assert.Empty(ZombieProfilePlan.OpeningProfiles(0, shooterPercent: 50));
    }

    [Fact]
    public void ZeroShooterPercentProducesNoHardEntries()
    {
        var entries = ZombieProfilePlan.OpeningProfiles(50, shooterPercent: 0);
        Assert.DoesNotContain(entries, e => e.Difficulty == ZombieRoleMix.Hard);
        Assert.Equal(50, entries.Sum(e => e.Count));
    }

    [Fact]
    public void HundredPercentShooterProducesOnlyHardEntries()
    {
        var entries = ZombieProfilePlan.OpeningProfiles(50, shooterPercent: 100);
        Assert.DoesNotContain(entries, e => e.Difficulty == ZombieRoleMix.Normal);
        Assert.Equal(50, entries.Sum(e => e.Count));
    }

    [Fact]
    public void OpeningProfilesApproximatesTheRoleWeights()
    {
        var entries = ZombieProfilePlan.OpeningProfiles(100, shooterPercent: 0);
        Assert.Equal(50, entries.Single(e => e.Role == ZombieRoleMix.InfectedAssault).Count);
        Assert.Equal(25, entries.Single(e => e.Role == ZombieRoleMix.InfectedPmc).Count);
        Assert.Equal(15, entries.Single(e => e.Role == ZombieRoleMix.InfectedCivil).Count);
        Assert.Equal(10, entries.Single(e => e.Role == ZombieRoleMix.InfectedLaborant).Count);
    }

    [Fact]
    public void OpeningProfilesIsDeterministic()
    {
        var a = ZombieProfilePlan.OpeningProfiles(37, 42);
        var b = ZombieProfilePlan.OpeningProfiles(37, 42);
        Assert.Equal(
            a.Select(e => (e.Role, e.Difficulty, e.Count)),
            b.Select(e => (e.Role, e.Difficulty, e.Count)));
    }

    [Fact]
    public void BackupTargetsAreAlwaysAtLeastTheMinimumBackupCount()
    {
        // The native backup loader only tops up once its deficit exceeds 3
        // (decompile-verified, see this class's doc comment) — anything at or
        // below that would silently never trigger a top-up.
        foreach (int requested in new[] { 0, 1, 3, 4, 5, 10 })
        {
            var entries = ZombieProfilePlan.BackupTargets(shooterPercent: 50, countPerPair: requested);
            Assert.All(entries, e => Assert.True(e.Count >= ZombieProfilePlan.MinimumBackupCount));
        }
    }

    [Fact]
    public void BackupTargetsCoverEveryMeleeRoleAlwaysAndTheHardVariantOnlyWhenAShooterShareExists()
    {
        var withoutShooter = ZombieProfilePlan.BackupTargets(shooterPercent: 0);
        Assert.Equal(4, withoutShooter.Count);
        Assert.All(withoutShooter, e => Assert.Equal(ZombieRoleMix.Normal, e.Difficulty));

        var withShooter = ZombieProfilePlan.BackupTargets(shooterPercent: 1);
        Assert.Equal(8, withShooter.Count);
        foreach (var (role, _) in ZombieRoleMix.Weights)
        {
            Assert.Contains(withShooter, e => e.Role == role && e.Difficulty == ZombieRoleMix.Normal);
            Assert.Contains(withShooter, e => e.Role == role && e.Difficulty == ZombieRoleMix.Hard);
        }
    }
}
