using ArenaMode.Bots;
using ArenaMode.Modes;
using Comfort.Common;
using EFT;

internal static class Program
{
    private static int _checks;
    private static void Check(bool pass, string reason)
    { _checks++; if (!pass) throw new InvalidOperationException("FAIL: " + reason); }

    // Enumerate all probability buckets independently, rather than trusting a
    // statistical tolerance or reproducing the production random implementation.
    private sealed class Rolls(params int[] values) : Random
    {
        internal int Calls;
        public override int Next(int maxValue)
        {
            Check(Calls < values.Length && values[Calls] >= 0 && values[Calls] < maxValue,
                "draw consumes only the expected bounded role shooter and melee rolls");
            return values[Calls++];
        }
    }

    private static void Main()
    {
        int[] expected = {75,50,25,40,20,30,40,20,30};
        Check(ZombieCompositionPolicy.SlowPercent(int.MinValue) == 75,
            "pre-wave sentinel stays in opening composition");
        for (int i=0; i<expected.Length; i++)
            Check(ZombieCompositionPolicy.SlowPercent(i+1)==expected[i], "slow curve advances through opening and repeat cycle");
        foreach (int wave in new[] { 100, 1000, int.MaxValue })
            Check(ZombieCompositionPolicy.SlowPercent(wave) is 20 or 30 or 40, "late wave mix stays bounded without overflow");

        var roles = new[] { ZombieRoleMix.InfectedAssault, ZombieRoleMix.InfectedPmc,
            ZombieRoleMix.InfectedCivil, ZombieRoleMix.InfectedLaborant };
        for (int roll=0; roll<100; roll++)
        {
            string role = roll < 50 ? roles[0] : roll < 75 ? roles[1] : roll < 90 ? roles[2] : roles[3];
            Check(ZombieRoleMix.Next(new Rolls(roll, 0), 100, 1).Role == role, "four generic role weights remain 50 25 15 10");
        }
        foreach (int wave in Enumerable.Range(1,9))
        foreach (int percent in new[] { -4,0,1,25,50,99,100,104 })
        {
            int hard=0, easy=0, normal=0;
            for (int shooter=0; shooter<100; shooter++)
            for (int melee=0; melee<100; melee++)
            {
                var rng = new Rolls(49, shooter, melee);
                var draw = ZombieRoleMix.Next(rng, percent, wave);
                bool shooting = shooter < Math.Clamp(percent,0,100);
                Check((draw.Difficulty==ZombieRoleMix.Hard)==shooting, "shooter chance remains independent and exact at zero and hundred");
                Check(rng.Calls == (shooting ? 2 : 3), "speed draw is conditional and preserves role shooter draw order");
                if(shooting) hard++;
                else if(draw.Difficulty==ZombieRoleMix.Easy) easy++;
                else if(draw.Difficulty==ZombieRoleMix.Normal) normal++;
                else Check(false, "draw never produces unsupported difficulty");
            }
            Check(hard == Math.Clamp(percent,0,100)*100, "shooter bucket count does not change with wave");
            Check(easy == (100-Math.Clamp(percent,0,100))*expected[wave-1]
                && easy+normal+hard==10000, "melee bucket split follows each wave independently of shooter chance");
        }
        var a=new Random(20261115); var b=new Random(20261115);
        for(int i=0;i<2000;i++)
        {
            var x=ZombieRoleMix.Next(a,37,i/50+1); var y=ZombieRoleMix.Next(b,37,i/50+1);
            Check(x.Role==y.Role && x.Difficulty==y.Difficulty, "same seed and wave sequence remain deterministic");
        }
        var early=new Random(1511); var late=new Random(1511);
        for(int i=0;i<2000;i++)
        {
            var x=ZombieRoleMix.Next(early,37,1); var y=ZombieRoleMix.Next(late,37,5);
            Check(x.Role==y.Role && (x.Difficulty==ZombieRoleMix.Hard)==(y.Difficulty==ZombieRoleMix.Hard),
                "wave speed changes do not perturb role or shooter random stream");
        }

        foreach(int size in new[] {0,1,2,7,13,50,97,100,200})
        foreach(int percent in new[] {0,1,25,50,99,100})
        {
            var opening=ZombieProfilePlan.OpeningProfiles(size,percent);
            Check(opening.Sum(e=>e.Count)==size, "opening profile quota remains exact");
            Check(opening.All(e=>e.Count>0 && e.Count<=100) && opening.Select(e=>(e.Role,e.Difficulty)).Distinct().Count()==opening.Count,
                "opening entries respect native request cap and unique pair ownership");
            foreach(string role in roles)
            {
                var rows=opening.Where(e=>e.Role==role).ToArray();
                int melee=rows.Where(e=>e.Difficulty!=ZombieRoleMix.Hard).Sum(e=>e.Count);
                int slow=rows.Where(e=>e.Difficulty==ZombieRoleMix.Easy).Sum(e=>e.Count);
                Check(Math.Abs(slow-melee*.75)<=.5, "opening preload reflects actual first wave slow share");
            }
            var backup=ZombieProfilePlan.BackupTargets(percent,ZombieProfilePlan.BackupCountForWaveStartSize(size));
            var allowed=percent==0 ? new[]{ZombieRoleMix.Easy,ZombieRoleMix.Normal}
                : percent==100 ? new[]{ZombieRoleMix.Hard} : new[]{ZombieRoleMix.Easy,ZombieRoleMix.Normal,ZombieRoleMix.Hard};
            Check(backup.Count==roles.Length*allowed.Length && backup.All(e=>allowed.Contains(e.Difficulty)),
                "backup contains only every possible future pair including shooter extremes");
            foreach(string role in roles)
            foreach(string difficulty in allowed)
                Check(backup.Count(e=>e.Role==role && e.Difficulty==difficulty)==1,
                    "backup contains only every possible future pair including shooter extremes");
            Check(opening.All(e=>backup.Any(b=>b.Role==e.Role && b.Difficulty==e.Difficulty)),
                "opening pairs have continuing cache coverage");
            var game=new BotGame(); Singleton<IBotGame>.Instance=game;
            ZombieSpawnService.RegisterBackupTargets(backup);
            var native=game.BotsController.BotSpawner.Backups;
            Check(native.Count==backup.Count && native.Sum(e=>e.Count)==backup.Sum(e=>e.Count),
                "production backup registration preserves all native pairs and counts");
        }
        foreach(int requested in new[]{-1,0,3,4,25,100,101,int.MaxValue})
            Check(ZombieProfilePlan.BackupTargets(25,requested).All(e=>e.Count==Math.Clamp(requested,4,100)),
                "backup preserves native floor and per pair ceiling");
        Check(ZombieProfilePlan.BackupCountForWaveStartSize(50)==25 && ZombieProfilePlan.BackupCountForWaveStartSize(200)==100,
            "existing generous per pair cache headroom is unchanged");
        Check(ZombieSpawnService.TryMapDifficulty("easy",out var slowDifficulty) && slowDifficulty==BotDifficulty.easy,
            "easy maps to native slow difficulty");
        Check(ZombieSpawnService.TryMapDifficulty("normal",out var fastDifficulty) && fastDifficulty==BotDifficulty.normal
            && ZombieSpawnService.TryMapDifficulty("hard",out var shootingDifficulty) && shootingDifficulty==BotDifficulty.hard,
            "fast and shooting native mappings stay distinct");
        Check(!ZombieSpawnService.TryMapDifficulty("impossible",out _) && !ZombieSpawnService.TryMapDifficulty(null,out _)
            && !ZombieSpawnService.TryMapRole("infectedTagilla",out _), "unsupported brain and boss remain excluded");
        Console.WriteLine($"ZOMBIE_COMPOSITION_CHECKS_PASSED {_checks} checks");
    }
}
