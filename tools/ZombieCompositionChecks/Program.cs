using ArenaMode.Bots;
using ArenaMode.Modes;
using ArenaMode.Shared;
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

    // The special waves: every fifth wave at Maximum, Runners on odd multiples of five and Shooters on even ones.
    // The calendar first (it runs before every older check: none of their faults touches it), written out and
    // not asked of the policy.
    private static void CheckSpecialWaveCalendar()
    {
        const ArenaDifficultyStep Max = ArenaDifficultyStep.Maximum, Red = ArenaDifficultyStep.Reduced;
        var table = new (int Wave, ZombieWaveTheme Theme)[]
        {
            (1, ZombieWaveTheme.None), (4, ZombieWaveTheme.None), (5, ZombieWaveTheme.Runners), (6, ZombieWaveTheme.None),
            (9, ZombieWaveTheme.None), (10, ZombieWaveTheme.Shooters), (11, ZombieWaveTheme.None), (15, ZombieWaveTheme.Runners),
            (20, ZombieWaveTheme.Shooters), (25, ZombieWaveTheme.Runners), (100, ZombieWaveTheme.Shooters),
            (0, ZombieWaveTheme.None), (-5, ZombieWaveTheme.None), (int.MinValue, ZombieWaveTheme.None), (int.MaxValue, ZombieWaveTheme.None)
        };
        foreach (var (wave, theme) in table)
        {
            Check(ArenaDifficultyPolicy.ZombieScheduledTheme(Max, wave) == theme,
                "special waves fall on every fifth wave from wave five, Runners first");
            Check(ArenaDifficultyPolicy.ZombieScheduledTheme(Red, wave) == ZombieWaveTheme.None,
                "Reduced has no special waves");
        }
        Check(ArenaDifficultyPolicy.ZombieSpecialWaveInterval == 5, "special waves are every fifth wave");
        for (int wave = 1; wave <= 200; wave++)
        {
            bool special = wave % 5 == 0;
            Check((ArenaDifficultyPolicy.ZombieScheduledTheme(Max, wave) != ZombieWaveTheme.None) == special,
                "special waves fall on every fifth wave from wave five, Runners first");
            if (special)
                Check(ArenaDifficultyPolicy.ZombieScheduledTheme(Max, wave) == (wave / 5 % 2 == 1 ? ZombieWaveTheme.Runners : ZombieWaveTheme.Shooters),
                    "special waves alternate Runners then Shooters");
        }
    }

    // The decider and the two share functions the draw reads, checked against each other for every shooter setting
    // and wave. It runs after the older share checks, whose faults would otherwise end here first.
    private static void CheckSpecialWaveShares()
    {
        const ArenaDifficultyStep Max = ArenaDifficultyStep.Maximum, Red = ArenaDifficultyStep.Reduced;
        for (int shooter = -3; shooter <= 103; shooter++)
        for (int wave = 1; wave <= 60; wave++)
        {
            int setting = Math.Clamp(shooter, 0, 100);
            var scheduled = ArenaDifficultyPolicy.ZombieScheduledTheme(Max, wave);
            var applied = ArenaDifficultyPolicy.ZombieWaveThemeFor(Max, shooter, wave);
            int shooterShare = ArenaDifficultyPolicy.ZombieShooterPercent(Max, shooter, wave);
            int slowShare = ZombieCompositionPolicy.SlowPercent(wave, Max);

            Check(applied == ZombieWaveTheme.None || applied == scheduled, "an applied theme is always the scheduled one");
            Check(!(setting == 0 && applied == ZombieWaveTheme.Shooters), "a shooter share of 0 never gets a shooters wave");
            Check(!(setting == 100 && applied != ZombieWaveTheme.None), "a shooter share of 100 announces no special wave");
            Check(applied == (setting == 100 || scheduled == ZombieWaveTheme.Shooters && setting == 0
                ? ZombieWaveTheme.None : scheduled), "the theme is dropped only at 100 (both themes) and for shooters at 0");
            // The decider and the draw agree: a Runners wave has no shooter and no slow share, a Shooters wave is all shooters,
            // and the exact settings stay exact on every wave.
            if (applied == ZombieWaveTheme.Runners)
                Check(shooterShare == 0 && slowShare == 0, "a runners wave draws neither shooters nor slow infected");
            if (applied == ZombieWaveTheme.Shooters)
                Check(shooterShare == 100, "a shooters wave draws only shooters");
            if (setting == 0 || setting == 100)
                Check(shooterShare == setting, "exact 0 and 100 percent shooters stay exact at every wave");
            if (scheduled == ZombieWaveTheme.None)
                Check(slowShare == ArenaDifficultyPolicy.ZombieSlowPercent(Max, ZombieCompositionPolicy.SlowPercent(wave), wave),
                    "an ordinary wave keeps the faded slow share");
            Check(ArenaDifficultyPolicy.ZombieShooterPercent(Red, shooter, wave) == setting
                && ZombieCompositionPolicy.SlowPercent(wave, Red) == ZombieCompositionPolicy.SlowPercent(wave)
                && ArenaDifficultyPolicy.ZombieWaveThemeFor(Red, shooter, wave) == ZombieWaveTheme.None,
                "Reduced keeps the flat mix and has no theme");
        }
    }

    private static void Main()
    {
        CheckSpecialWaveCalendar();
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
            Check(ZombieRoleMix.Next(new Rolls(roll, 0), 100, 1, ArenaDifficultyStep.Reduced).Role == role, "four generic role weights remain 50 25 15 10");
        }
        foreach (int wave in Enumerable.Range(1,9))
        foreach (int percent in new[] { -4,0,1,25,50,99,100,104 })
        {
            int hard=0, easy=0, normal=0;
            for (int shooter=0; shooter<100; shooter++)
            for (int melee=0; melee<100; melee++)
            {
                var rng = new Rolls(49, shooter, melee);
                var draw = ZombieRoleMix.Next(rng, percent, wave, ArenaDifficultyStep.Reduced);
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
        // Maximum: the slow share of every wave fades per the policy and the shooter share grows;
        // the role / shooter / melee roll order stays the same, so the buckets are enumerable.
        // Wave 5 is a Runners wave (no slow infected) and wave 10 a Shooters wave (see the schedule below).
        int[] faded = {75,50,25,35,0,15,20,0,0,5,0};
        // Written out, not asked of the policy: configured share -> share in waves 1..11
        // (a tenth of the setting more per wave, rounded half up, twice the setting at wave 11);
        // wave 5 is 0 (Runners) and wave 10 is 100 (Shooters) except at the exact settings 0 and 100.
        var grownShares = new (int Percent, int[] Share)[]
        {
            (0,   new[]{0,0,0,0,0,0,0,0,0,0,0}),
            (20,  new[]{20,22,24,26,0,30,32,34,36,100,40}),
            (37,  new[]{37,41,44,48,0,56,59,63,67,100,74}),
            (100, new[]{100,100,100,100,100,100,100,100,100,100,100})
        };
        for (int wave=1; wave<=faded.Length; wave++)
        {
            Check(ZombieCompositionPolicy.SlowPercent(wave, ArenaDifficultyStep.Maximum)==faded[wave-1], "Maximum fades the slow share from wave four");
            Check(ZombieCompositionPolicy.SlowPercent(wave, ArenaDifficultyStep.Reduced)==ZombieCompositionPolicy.SlowPercent(wave), "Reduced keeps the rhythm");
            foreach (var (percent, shares) in grownShares)
            {
                int share = shares[wave-1];
                Check(ArenaDifficultyPolicy.ZombieShooterPercent(ArenaDifficultyStep.Maximum, percent, wave)==share, "Maximum grows the shooter share by a tenth of the setting per wave");
                Check(ArenaDifficultyPolicy.ZombieShooterPercent(ArenaDifficultyStep.Reduced, percent, wave)==percent, "Reduced keeps the shooter share flat");
                int hard=0, easy=0;
                for (int shooter=0; shooter<100; shooter++)
                for (int melee=0; melee<100; melee++)
                {
                    var draw = ZombieRoleMix.Next(new Rolls(49, shooter, melee), percent, wave, ArenaDifficultyStep.Maximum);
                    if (draw.Difficulty==ZombieRoleMix.Hard) hard++;
                    else if (draw.Difficulty==ZombieRoleMix.Easy) easy++;
                }
                Check(hard==share*100, "Maximum draws the grown shooter share of the wave");
                Check(easy==(100-share)*faded[wave-1], "Maximum draws the faded slow share of the wave");
                if (percent==0 || percent==100) Check(share==percent, "exact 0 and 100 percent shooters stay exact at Maximum");
            }
        }
        Check(ArenaDifficultyPolicy.ZombieShooterPercent(ArenaDifficultyStep.Maximum, 20, 11)==40
            && ArenaDifficultyPolicy.ZombieShooterPercent(ArenaDifficultyStep.Maximum, 20, 99)==40
            && ArenaDifficultyPolicy.ZombieShooterPercent(ArenaDifficultyStep.Maximum, 70, 99)==100,
            "Maximum shooter growth stops at twice the setting and at 100 percent");
        Check(ArenaDifficultyPolicy.ZombieMaxAlive(ArenaDifficultyStep.Reduced, 32)==24 && ArenaDifficultyPolicy.ZombieMaxAlive(ArenaDifficultyStep.Reduced, 8)==8
            && ArenaDifficultyPolicy.ZombieMaxAlive(ArenaDifficultyStep.Maximum, 32)==32,
            "Reduced caps the alive limit at 24 and Maximum never lowers it");
        var a=new Random(20261115); var b=new Random(20261115);
        for(int i=0;i<2000;i++)
        {
            var x=ZombieRoleMix.Next(a,37,i/50+1,ArenaDifficultyStep.Maximum); var y=ZombieRoleMix.Next(b,37,i/50+1,ArenaDifficultyStep.Maximum);
            Check(x.Role==y.Role && x.Difficulty==y.Difficulty, "same seed and wave sequence remain deterministic");
        }
        var early=new Random(1511); var late=new Random(1511);
        for(int i=0;i<2000;i++)
        {
            var x=ZombieRoleMix.Next(early,37,1,ArenaDifficultyStep.Reduced); var y=ZombieRoleMix.Next(late,37,5,ArenaDifficultyStep.Reduced);
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
        CheckSpecialWaveShares();
        Console.WriteLine($"ZOMBIE_COMPOSITION_CHECKS_PASSED {_checks} checks");
    }
}
