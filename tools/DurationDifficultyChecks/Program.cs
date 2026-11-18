using System.Reflection;
using ArenaMode.Bots;
using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string label) { checks++; if(!value) throw new Exception("FAIL: "+label); }
    // --- the client reader of the shared policy (ArenaDifficulty) -------------------------------------
    private static readonly string[] FixedModes={"deathmatch","teamdeathmatch","domination","tactical","zombie"};
    private static void Arm(string mode,ArenaDifficultyStep step,float elapsed,int streak)
    {
        ArenaSession.IsActive=true; ArenaSession.ActiveMode=mode; ArenaSession.Difficulty=step;
        ArenaMatchService.Current=new Match { Mode=new Mode(),Elapsed=elapsed,Streak=streak };
        ArenaClientConfig.RampMinutes=15; Settings.DifficultyStep.Value=step;
    }
    private static void Difficulty()
    {
        // The expected values are written out here, not read from the policy: a policy edit must show up.
        var table=new Dictionary<ArenaDifficultyStep,(BotDifficulty Pmc,BotDifficulty Scav,int Bonus,float Cooldown)>
        {
            [ArenaDifficultyStep.Maximum]=(BotDifficulty.impossible,BotDifficulty.hard,4,4f),
            [ArenaDifficultyStep.Reduced]=(BotDifficulty.hard,BotDifficulty.normal,2,6f),
        };
        foreach(var mode in FixedModes) foreach(var (step,row) in table)
            foreach((float elapsed,int streak) in new[]{(0f,0),(0f,8),(1f,0),(900f,0),(900f,8),(7200f,8)})
            {
                Arm(mode,step,elapsed,streak);
                Check(ArenaDifficulty.DifficultyFor(true)==row.Pmc,$"fixed mode ignores elapsed time and streak: PMC level {mode} {step} {elapsed}/{streak}");
                Check(ArenaDifficulty.DifficultyFor(false)==row.Scav,$"fixed mode ignores elapsed time and streak: scav level {mode} {step} {elapsed}/{streak}");
                Check(ArenaDifficulty.PmcFloor(8)==8+row.Bonus,$"fixed mode ignores elapsed time and streak: PMC floor {mode} {step} {elapsed}/{streak}");
                Check(ArenaDifficulty.PmcFloor(0)==0,"PmcFloor zero stays off at every step");
                Check(ArenaDifficulty.BurstCooldown(8f)==row.Cooldown,$"refill cooldown factor follows the step {mode} {step}");
                Check(ArenaDifficulty.Progress==1f,"progress is one outside Gun Game");
                Check(ArenaDifficulty.Describe()==step.ToString(),"panel line names only the frozen step outside Gun Game");
            }
        // No match at all (loading, between rounds) is still the fixed row, never a throw.
        Arm("tactical",ArenaDifficultyStep.Maximum,0,0); ArenaMatchService.Current=null;
        Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.impossible,"fixed mode needs no match");
        // The frozen step wins over the live setting, both ways round, and the live one answers outside a raid.
        Arm("deathmatch",ArenaDifficultyStep.Reduced,0,0); Settings.DifficultyStep.Value=ArenaDifficultyStep.Maximum;
        Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard && ArenaDifficulty.PmcFloor(8)==10,"frozen step wins over a live setting change (reduced frozen)");
        Arm("deathmatch",ArenaDifficultyStep.Maximum,0,0); Settings.DifficultyStep.Value=ArenaDifficultyStep.Reduced;
        Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.impossible && ArenaDifficulty.DifficultyFor(false)==BotDifficulty.hard,"frozen step wins over a live setting change (maximum frozen)");
        ArenaSession.Difficulty=null;
        Check(ArenaDifficulty.Step==ArenaDifficultyStep.Reduced,"without a frozen step the live setting answers");
        // Gun Game: the curve over the map's ramp length, one clock for level, bonus and the panel line.
        foreach(var (step,row) in table)
        {
            foreach((double portion,BotDifficulty pmc,BotDifficulty scav,int bonus) in new[]
            {
                (0d,BotDifficulty.hard,BotDifficulty.normal,0),
                (.2d,BotDifficulty.hard,BotDifficulty.normal,row.Bonus==4?1:0),
                (.49d,BotDifficulty.hard,BotDifficulty.normal,row.Bonus==4?2:1),
                (.5d,BotDifficulty.hard,row.Scav,row.Bonus==4?2:1),
                (.79d,BotDifficulty.hard,row.Scav,row.Bonus==4?3:2),
                (.8d,row.Pmc,row.Scav,row.Bonus==4?3:2),
                (1d,row.Pmc,row.Scav,row.Bonus),
                (3d,row.Pmc,row.Scav,row.Bonus),
            })
            {
                Arm("gungame",step,(float)(portion*900),0);
                Check(ArenaDifficulty.DifficultyFor(true)==pmc,$"Gun Game PMC level follows the curve {step} {portion}");
                Check(ArenaDifficulty.DifficultyFor(false)==scav,$"Gun Game scav level follows the curve {step} {portion}");
                Check(ArenaDifficulty.PmcFloor(8)==8+bonus,$"Gun Game PMC bonus follows the curve {step} {portion}");
                Check(ArenaDifficulty.PmcFloor(0)==0,"Gun Game keeps PmcFloor zero off");
            }
            Arm("gungame",step,0,0);
            Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard && ArenaDifficulty.PmcFloor(8)==8,$"Gun Game opens with today's opening {step}");
            Arm("gungame",step,450,0);
            Check(Math.Abs(ArenaDifficulty.Progress-.5f)<.00001f && ArenaDifficulty.Describe()==$"{step}, build-up 50%","Gun Game panel line shows the build-up");
            // A kill streak or a finished ladder changes nothing: progress stays where the clock is.
            Arm("gungame",step,100,8);
            Check(ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard && ArenaDifficulty.PmcFloor(8)==8 && Math.Abs(ArenaDifficulty.Progress-100f/900f)<.00001f,
                $"kill streak never feeds the Gun Game curve {step}");
        }
        // The ramp length is the map's value; an absent one falls back to fifteen minutes.
        Arm("gungame",ArenaDifficultyStep.Maximum,300,0); ArenaClientConfig.RampMinutes=10;
        Check(ArenaDifficulty.DifficultyFor(false)==BotDifficulty.hard && Math.Abs(ArenaDifficulty.Progress-.5f)<.00001f,"Gun Game uses the map's ramp length");
        ArenaClientConfig.RampMinutes=0; Check(Math.Abs(ArenaDifficulty.Progress-300f/900f)<.00001f,"an absent ramp length falls back to fifteen minutes");
        ArenaClientConfig.RampMinutes=15;
        ArenaMatchService.Current=null; Check(ArenaDifficulty.Progress==0f && ArenaDifficulty.DifficultyFor(true)==BotDifficulty.hard,"Gun Game without a match is the opening");
        ArenaSession.ActiveMode=null; ArenaSession.Difficulty=null; ArenaMatchService.Current=null; Settings.DifficultyStep.Value=ArenaDifficultyStep.Maximum;
    }
    // --- wiring that cannot be linked (Unity/EFT-heavy) is pinned as text; mutants override the files -------
    private static string Text(string key)
    {
        var path=Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key==key).Value!;
        return System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path),@"\s+"," ");
    }
    private static void WiringPins()
    {
        Check(ArenaDifficultyPolicy.SweepInterval(15,false,ArenaDifficultyStep.Maximum)==15 && ArenaDifficultyPolicy.SweepInterval(15,false,ArenaDifficultyStep.Reduced)==22.5
            && ArenaDifficultyPolicy.SweepInterval(15,true,ArenaDifficultyStep.Reduced)==11.25 && ArenaDifficultyPolicy.SweepInterval(3,true,ArenaDifficultyStep.Reduced)==3,
            "sweep interval takes the step factor last");
        Check(ArenaDifficultyPolicy.BotSpeedFactor(99,ArenaDifficultyStep.Reduced)==1.25 && ArenaDifficultyPolicy.BotSpeedFactor(99,ArenaDifficultyStep.Maximum)==1.5,
            "bot speed caps the setting before scaling the surplus");
        var session=Text("SessionText");
        Check(session.Contains("ClearParticipants(); Participants = participants; ParticipantBosses = participantBosses; Difficulty = difficulty;"),
            "MarkArmed freezes the acknowledged difficulty step after clearing the old raid");
        Check(session.Contains("ParticipantBosses = false; Difficulty = null;"),"ClearParticipants resets the frozen difficulty step");
        var config=Text("ClientConfigText");
        Check(config.Contains("\"botTierRampMinutes\"") && Text("ServerConfigText").Contains("JsonPropertyName(\"botTierRampMinutes\")"),
            "client and server read the same botTierRampMinutes key");
        Check(config.Contains("value.HasValue ? Math.Max(1, value.Value) : fallback"),
            "a present non-positive ramp length clamps to one minute like the server");
        var hostility=Text("HostilityText");
        Check(hostility.Contains("interval = (float)ArenaDifficultyPolicy.SweepInterval( interval, Modes.ArenaMatchService.Current?.Mode is Modes.TacticalMode, ArenaDifficulty.Step);"),
            "the HuntPlayer sweep interval comes from the policy with the frozen step");
        Check(Text("MovementText").Contains("requested = (float)ArenaDifficultyPolicy.BotSpeedFactor(requested, ArenaDifficulty.Step);"),
            "bot speed comes from the policy with the frozen step");
    }
    // --- the PMC prewarm: exactly the (role, level) pairs the frozen step can request ----------------------
    private static string Levels(string mode,ArenaDifficultyStep step)=>string.Join(",",ArenaDifficultyPolicy.PmcRequestLevels(mode,step));
    private static void Prewarm()
    {
        // Written out, not read from the policy. Fixed modes warm one level, Gun Game the opening plus the step's level.
        foreach(var mode in new[]{"deathmatch","teamdeathmatch","domination","tactical"})
        {
            Check(Levels(mode,ArenaDifficultyStep.Maximum)=="Impossible",$"prewarm {mode} Maximum warms only impossible");
            Check(Levels(mode,ArenaDifficultyStep.Reduced)=="Hard",$"prewarm {mode} Reduced warms only hard");
        }
        Check(Levels("gungame",ArenaDifficultyStep.Maximum)=="Hard,Impossible","prewarm Gun Game Maximum warms hard then impossible");
        Check(Levels("gungame",ArenaDifficultyStep.Reduced)=="Hard","prewarm Gun Game Reduced warms hard once");
        Check(Levels("zombie",ArenaDifficultyStep.Maximum)=="" && Levels("zombie",ArenaDifficultyStep.Reduced)=="","prewarm Zombie warms no PMC level");
        // Every level a mode really requests is covered, and nothing else is warmed (the first request finds its pool).
        foreach(var mode in new[]{"deathmatch","teamdeathmatch","domination","tactical","gungame"}) foreach(var step in new[]{ArenaDifficultyStep.Maximum,ArenaDifficultyStep.Reduced})
        {
            var warmed=ArenaDifficultyPolicy.PmcRequestLevels(mode,step);
            Check(warmed.Distinct().Count()==warmed.Length,$"prewarm lists no level twice {mode} {step}");
            var requested=new HashSet<ArenaBotLevel>();
            foreach(double progress in new[]{0d,.5d,.79d,.8d,1d}) requested.Add(ArenaDifficultyPolicy.BotLevel(step,true,ArenaDifficultyPolicy.Progress(mode,progress*900,15)));
            Check(requested.SetEquals(warmed),$"prewarm covers exactly the PMC levels requested {mode} {step}");
        }
        // The call site walks that list instead of a literal pair list, and no clock readiness gates it.
        var burst=Text("BurstText");
        Check(burst.Contains("foreach (var level in Shared.ArenaDifficultyPolicy.PmcRequestLevels(ArenaSession.ActiveMode, ArenaDifficulty.Step))"),"prewarm walks the policy's level list for the frozen step");
        Check(burst.Contains("spawner.AddToTargetBackup(difficulty, WildSpawnType.pmcUSEC, 6);") && burst.Contains("spawner.AddToTargetBackup(difficulty, WildSpawnType.pmcBEAR, 6);"),"prewarm covers both PMC sides");
        Check(!burst.Contains("IsReady") && !burst.Contains("TeamDeathmatchTimeLimitSeconds.Value > 0) return;"),"prewarm has no clock readiness gate");
    }
    private static void Main()
    {
        Difficulty();
        WiringPins();
        Prewarm();
        Console.WriteLine($"ARENA_DURATION_BASELINE_PASSED checks={checks}");
    }
}
