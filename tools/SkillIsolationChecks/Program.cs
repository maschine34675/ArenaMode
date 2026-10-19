using ArenaMode;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;

static class Program
{
    static int checks;
    static void Check(bool value,string reason) { checks++; if(!value) throw new Exception("ASSERT: "+reason); }
    static GameWorld Fresh()
    {
        ArenaSkills.Restore();
        Settings.MaxSkills.Value=ArenaSession.IsActive=true;
        var world=Singleton<GameWorld>.Instance=ArenaSession.World=new GameWorld();
        Singleton<AbstractGame>.Instance=ArenaSession.Game=new AbstractGame();
        Time.realtimeSinceStartup+=10; Time.time=Time.realtimeSinceStartup;
        return world;
    }
    static void Tick() { Time.realtimeSinceStartup+=4; Time.time=Time.realtimeSinceStartup; ArenaSkills.Tick(); }
    static void Main()
    {
        foreach(string fault in new[]{"ordinary","world","game","bot","remote","disabled"})
        {
            var world=Fresh();
            switch(fault) {
                case "ordinary": ArenaSession.IsActive=false; break;
                case "world": Singleton<GameWorld>.Instance=new(); break;
                case "game": Singleton<AbstractGame>.Instance=new(); break;
                case "bot": world.MainPlayer.IsAI=true; break;
                case "remote": world.MainPlayer.IsYourPlayer=false; break;
                case "disabled": Settings.MaxSkills.Value=false; break;
            }
            ArenaSkills.Apply(world); Tick();
            Check(world.MainPlayer.Skills.DisplayList[0].Buff==0 && world.MainPlayer.Skills.Mastering["entry"].Current==17,"no boost: "+fault);
        }
        var active=Fresh(); var skills=active.MainPlayer.Skills;
        ArenaSkills.Apply(active);
        Check(skills.DisplayList.All(s=>s.Buff==50) && skills.Mastering["entry"].Current==300,"active initial boost");
        active.MainPlayer=null; Tick();
        Check(skills.DisplayList[0].Buff==50,"temporary respawn body gap retains owned boost");
        active.MainPlayer=new(){Skills=skills}; skills.Mastering["new"]=new(){Current=9}; Tick();
        Check(skills.Mastering["new"].Current==300,"same profile respawn gets new weapon mastery");
        ArenaSession.IsActive=false; Tick();
        Check(skills.DisplayList.All(s=>s.Buff==0) && skills.Mastering["entry"].Current==17 && skills.Mastering["new"].Current==9,"ordinary transition restores original values");

        foreach(string fault in new[]{"world","game","owner","disabled"})
        {
            var world=Fresh(); var old=world.MainPlayer.Skills; ArenaSkills.Apply(world);
            var foreign=new SkillManager();
            switch(fault) {
                case "world": Singleton<GameWorld>.Instance=new(){MainPlayer=new(){Skills=foreign}}; break;
                case "game": Singleton<AbstractGame>.Instance=new(); break;
                case "owner": world.MainPlayer.Skills=foreign; break;
                case "disabled": Settings.MaxSkills.Value=false; break;
            }
            Tick();
            Check(old.Mastering["entry"].Current==17 && foreign.Mastering["entry"].Current==17,"lost scope restores owner without boosting foreign profile: "+fault);
        }

        var broken=Fresh(); var original=broken.MainPlayer.Skills;
        original.ImmunityAvoidPoisonChance.Value=.13f;
        var bad=original.DisplayList[0]; var good=original.DisplayList[1];
        bad.OnWrite=_=>original.ImmunityAvoidPoisonChance.Value=.99f;
        ArenaSkills.Apply(broken);
        bool fail=true;
        bad.OnWrite=value=> { original.ImmunityAvoidPoisonChance.Value=.99f; if(fail && value==0) throw new InvalidOperationException("listener fault"); };
        ArenaSkills.Restore();
        Check(original.Mastering["entry"].Current==17,"mastery restored despite throwing skill listener");
        Check(good.Buff==0 && original.RecoilControl.Buff==0,"other skill restores survive listener failure");
        Check(original.ImmunityAvoidPoisonChance.Value==.13f,"sticky restored after throwing recompute");
        original.Mastering["entry"].Current=23; // regular XP earned after successful restore
        good.Buff=7;
        original.Mastering["pending"]=new(){Current=11};
        int warnings=Plugin.ModLogger.Warnings.Count, writes=bad.Writes;
        ArenaSkills.Tick();
        Check(bad.Writes==writes,"failed restore retry is throttled");
        ArenaSkills.Apply(broken);
        Check(original.Mastering["pending"].Current==11 && good.Buff==7,"failed restore cannot reapply or replace original baselines");
        ArenaSession.IsActive=false; Tick();
        Check(original.Mastering["pending"].Current==11,"pending retry never boosts ordinary mastery");
        Check(original.Mastering["entry"].Current==23 && good.Buff==7,"successful restores are not replayed over ordinary gains");
        Check(Plugin.ModLogger.Warnings.Count==warnings,"persistent restore error is not logged every tick");
        fail=false; Tick();
        Check(original.ImmunityAvoidPoisonChance.Value==.13f && bad.Buff==0,"retry eventually restores retained originals");
        original.ImmunityAvoidPoisonChance.Value=.25f; Tick();
        Check(original.ImmunityAvoidPoisonChance.Value==.25f,"completed retry relinquishes sticky ownership");

        var partial=Fresh(); var partialSkills=partial.MainPlayer.Skills;
        bool applyFailure=true;
        partialSkills.DisplayList[0].OnWrite=_=> { if(applyFailure) throw new InvalidOperationException("apply listener fault"); };
        ArenaSkills.Apply(partial);
        partialSkills.Mastering["late"]=new(){Current=41}; Tick();
        Check(partialSkills.Mastering["late"].Current==41,"failed apply never activates late mastery boosting");
        applyFailure=false; ArenaSkills.Restore();
        ArenaSkills.Apply(partial); Check(partialSkills.Mastering["late"].Current==300,"clean retry allows a later new application");
        ArenaSkills.Restore(); Check(partialSkills.Mastering["late"].Current==41,"clean retry kept original mastery baseline");
        Console.WriteLine($"SKILL_ISOLATION_CHECKS_PASSED ({checks} assertions)");
    }
}
