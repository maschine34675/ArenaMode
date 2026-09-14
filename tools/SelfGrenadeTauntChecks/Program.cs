using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using System.Reflection;

internal static class Program
{
    private static int _checks;
    private sealed class Fixture
    {
        internal Player Local = new();
        internal ThrowWeap Grenade = new() { Id = "hand-grenade-one", TemplateId = "f1-template", FragmentType = "f1-fragment-template" };
        internal Player.BaseGrenadeHandsController Hands;
        internal GameWorld World;
        internal LocalGame Game;
        internal ArenaMatch Match = new();
        internal List<string> Text = new();
        internal int CombatAudio, AnnouncerAudio, ScoreEvents;
        internal Fixture()
        {
            ArenaSelfGrenadeTaunt.Reset(); ArenaShotAwardHistory.Reset();
            ArenaHitHistory.Reset(); ArenaAdministrativeDeathGate.Reset();
            ArenaSession.IsActive = true;
            BindBody(Local);
            World = new() { MainPlayer = Local }; Game = new() { LocalPlayer = Local, Status = GameStatus.Started };
            Singleton<GameWorld>.Instance = World; Singleton<AbstractGame>.Instance = Game;
            ArenaSession.World = World; ArenaSession.Game = Game;
            ArenaMatchService.Current = Match;
            Match.OnTextCallout += label => Text.Add(label);
            Match.OnCallout += _ => CombatAudio++;
            Match.OnAnnouncerCue += _ => AnnouncerAudio++;
            Match.OnScoreChanged += () => ScoreEvents++;
        }
        internal void BindBody(Player body)
        {
            Local = body; Hands = new() { _player = body, Item = Grenade }; body.HandsController = Hands;
            if (World != null) World.MainPlayer = body;
            if (Game != null) Game.LocalPlayer = body;
        }
        internal DamageInfo Info(EDamageType type = EDamageType.GrenadeFragment) => new()
        {
            DamageType = type, Player = new() { iPlayer = Local }, Weapon = Grenade,
            SourceId = type == EDamageType.Bullet ? Grenade.FragmentType : Grenade.TemplateId,
            FireIndex = type == EDamageType.Bullet ? 1 : -1
        };
        internal void Throw() => Patch("ThrowPatch", "Prefix").Invoke(null, new object[] { Hands });
        internal void Die(DamageInfo info, EDamageType? lethal = null, Player victim = null, IPlayer aggressor = null)
        {
            victim ??= Local; aggressor ??= Local;
            using var scope = ArenaSelfGrenadeTaunt.BeginDamage(victim, info);
            Local.HealthController.IsAlive = false;
            ArenaSelfGrenadeTaunt.ObserveDeath(victim, aggressor, info, lethal ?? info.DamageType);
        }
        internal void Silent(string label) => Check(Text.Count == 0 && CombatAudio == 0 && AnnouncerAudio == 0, label);
        internal void Neutral()
        {
            Check(Match.Score == 27 && Match.Kills == 12 && Match.Deaths == 3 && Match.KillCalls == 0
                && Match.LocalDeathCalls == 0 && ScoreEvents == 0, "text dispatch changes no score death or kill bookkeeping");
            Check(CombatAudio == 0 && AnnouncerAudio == 0, "text dispatch emits no combat or announcer audio event");
        }
    }
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception("FAILED: " + label);
    }
    private static MethodInfo Patch(string nested, string method)
        => typeof(ArenaSelfGrenadeTaunt).GetNestedType(nested, BindingFlags.NonPublic)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);

    private static void Positive()
    {
        Check(ModulePatch.Targets.Count == 2, "exactly throw and damage observation hooks enabled");
        Check(ModulePatch.Targets[0].DeclaringType == typeof(Player.BaseGrenadeHandsController)
            && ModulePatch.Targets[0].Name == "CreateAndRegisterGrenade", "exact actual hand throw path hooked");
        Check(ModulePatch.Targets[1].DeclaringType == typeof(Player)
            && ModulePatch.Targets[1].Name == "ApplyDamageInfo", "actual local damage path hooked");
        Check(Patch("ThrowPatch", "Prefix").IsDefined(typeof(PatchPrefixAttribute)), "throw observation is a prefix before any prewarmed explosion");
        Check(Patch("DamagePatch", "Finalizer").IsDefined(typeof(PatchFinalizerAttribute)), "damage scope uses exception-safe finalizer");
        foreach (var type in new[] { EDamageType.GrenadeFragment, EDamageType.Bullet })
        {
            var f = new Fixture(); f.Throw(); var info = f.Info(type);
            f.Local.HandsController = new Player.ItemHandsController { _player = f.Local };
            f.Die(info);
            Check(f.Text.SequenceEqual(new[] { "NICE THROW, GENIUS!" }), "proven own grenade emits exactly the HUD text");
            f.Neutral();
            ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, type);
            Check(f.Text.Count == 1, "later stored aggressor does not duplicate HUD text");
        }
        var consumer = new Fixture(); consumer.Throw();
        consumer.Match.OnTextCallout += _ => throw new Exception("HUD listener failure");
        int warnings = ArenaMode.Plugin.ModLogger.Lines.Count;
        consumer.Die(consumer.Info());
        Check(consumer.Text.Count == 1 && ArenaMode.Plugin.ModLogger.Lines.Count > warnings, "actual RaiseSafe contains text listener exception");
        consumer.Neutral();
    }
    private static void Sources()
    {
        foreach (var type in new[] { EDamageType.GrenadeFragment, EDamageType.Bullet })
        {
            var f = new Fixture(); f.Throw(); var wrong = f.Info(type); wrong.SourceId = "wrong-template";
            f.Die(wrong); f.Silent("wrong blast or fragment source rejected");
            f = new Fixture(); f.Throw(); wrong = f.Info(type);
            wrong.SourceId = type == EDamageType.Bullet ? f.Grenade.TemplateId : f.Grenade.FragmentType;
            f.Die(wrong); f.Silent("blast and fragment source templates are not interchangeable");
            f = new Fixture(); f.Throw(); wrong = f.Info(type); wrong.DelayedDamage = true;
            f.Die(wrong); f.Silent("planted delayed blast flag rejected");
            f = new Fixture(); f.Throw(); wrong = f.Info(type);
            wrong.Weapon = new ThrowWeap { Id = f.Grenade.Id, TemplateId = f.Grenade.TemplateId, FragmentType = f.Grenade.FragmentType };
            f.Die(wrong); f.Silent("same-id grenade clone lacks actual thrown object provenance");
            f = new Fixture(); f.Throw(); wrong = f.Info(type); wrong.Player = new() { iPlayer = new Player() };
            f.Die(wrong); f.Silent("enemy grenade shooter rejected");
            f = new Fixture(); f.Die(f.Info(type));
            f.Silent("unregistered or tripwire grenade lacking hand throw path rejected");
        }
        foreach (var type in new[] { EDamageType.Explosion, EDamageType.Blunt, EDamageType.Landmine,
            EDamageType.HeavyBleeding, EDamageType.Fall, EDamageType.Undefined, EDamageType.Melee })
        {
            var f = new Fixture(); f.Throw(); f.Die(f.Info(type)); f.Silent(type + " cannot borrow grenade origin");
        }
        var launcher = new Fixture(); launcher.Throw(); var info = launcher.Info(EDamageType.Bullet);
        info.Weapon = new Weapon { Id = "grenade-launcher", TemplateId = "gl-template" };
        launcher.Die(info); launcher.Silent("GL or explosive projectile weapon is not a hand grenade");
        var empty = new Fixture(); empty.Grenade.FragmentType = null; empty.Throw(); empty.Die(empty.Info(EDamageType.Bullet));
        empty.Silent("missing fragment identity fails closed");
    }
    private static void Identity()
    {
        var cases = new (string, Action<Fixture>)[]
        {
            ("normal raid", f => ArenaSession.IsActive = false),
            ("foreign raid", f => ArenaSession.World = new GameWorld()),
            ("missing game", f => Singleton<AbstractGame>.Instance = null),
            ("stopped game", f => f.Game.Status = GameStatus.Stopped),
            ("AI body", f => f.Local.IsAI = true),
            ("not local body", f => f.Local.IsYourPlayer = false),
            ("missing match", f => ArenaMatchService.Current = null),
            ("completed mode", f => f.Match.Mode.IsComplete = true),
            ("old physical hand owner", f => f.Hands._player = new Player { ProfileId = f.Local.ProfileId }),
            ("inactive controller", f => f.Local.HandsController = new Player.ItemHandsController()),
            ("already dead thrower", f => f.Local.HealthController.IsAlive = false),
            ("missing item", f => f.Hands.Item = null)
        };
        foreach (var (label, change) in cases)
        {
            var f = new Fixture(); change(f); f.Throw(); f.Die(f.Info()); f.Silent(label + " cannot register/report local self grenade");
        }
        var prior = new Fixture(); prior.Throw(); prior.BindBody(new Player { ProfileId = prior.Local.ProfileId });
        prior.Die(prior.Info()); prior.Silent("old throwing body cannot taunt same-profile respawn successor");
        var wrongVictim = new Fixture(); wrongVictim.Throw(); wrongVictim.Die(wrongVictim.Info(), victim: new Player());
        wrongVictim.Silent("foreign victim rejected");
        var wrongAggressor = new Fixture(); wrongAggressor.Throw(); wrongAggressor.Die(wrongAggressor.Info(), aggressor: new Player());
        wrongAggressor.Silent("foreign aggressor rejected");
        var oldRound = new Fixture(); oldRound.Throw(); ArenaShotAwardHistory.Reset(); oldRound.Die(oldRound.Info());
        oldRound.Silent("previous award round thrown origin rejected");
        var admin = new Fixture(); admin.Throw();
        using (ArenaAdministrativeDeathGate.Enter(admin.Local.ProfileId)) admin.Die(admin.Info());
        admin.Silent("administrative retirement cannot taunt");
    }
    private static void Contexts()
    {
        var f = new Fixture(); f.Throw(); var info = f.Info();
        ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
        f.Silent("stored aggressor without immediate damage scope rejected");
        using (ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info))
        {
            ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
            f.Silent("living player cannot receive death taunt");
        }
        foreach (var mismatch in new[] { "fire", "source", "weapon", "type", "lethal" })
        {
            f = new Fixture(); f.Throw(); info = f.Info(); var wrong = info; var lethal = info.DamageType;
            using (ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info))
            {
                if (mismatch == "fire") wrong.FireIndex++;
                if (mismatch == "source") wrong.SourceId = f.Grenade.FragmentType;
                if (mismatch == "weapon") wrong.Weapon = new ThrowWeap { TemplateId = f.Grenade.TemplateId };
                if (mismatch == "type") wrong.DamageType = EDamageType.Bullet;
                if (mismatch == "lethal") lethal = EDamageType.HeavyBleeding;
                f.Local.HealthController.IsAlive = false;
                ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, wrong, lethal);
                f.Silent(mismatch + " mismatch with accepted immediate context rejected");
            }
        }
        f = new Fixture(); f.Throw(); info = f.Info();
        using (ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info))
        {
            using (ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info))
            {
                f.Local.HealthController.IsAlive = false;
                ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
                f.Silent("even matching nested damage context cannot borrow outer provenance");
            }
            ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
            ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
            Check(f.Text.Count == 1, "outer context restored and same-body duplicate reports suppressed");
        }
        f = new Fixture(); f.Throw(); info = f.Info();
        using (ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info))
        using (ArenaSelfGrenadeTaunt.BeginDamage(new Player(), info))
        {
            f.Local.HealthController.IsAlive = false;
            ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
            f.Silent("unrelated nested victim masks outer damage context");
        }
        f = new Fixture(); f.Throw(); info = f.Info();
        object[] args = { f.Local, info, null };
        Patch("DamagePatch", "Prefix").Invoke(null, args);
        var exception = new InvalidOperationException("native damage failure");
        var returned = Patch("DamagePatch", "Finalizer").Invoke(null, new object[] { exception, args[2] });
        Check(ReferenceEquals(exception, returned), "native damage finalizer preserves exception");
        f.Local.HealthController.IsAlive = false;
        ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
        f.Silent("native exception finalizer clears immediate context");
        f = new Fixture(); f.Throw(); info = f.Info();
        var outer = ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info);
        var inner = ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info);
        ArenaSelfGrenadeTaunt.Reset(); inner.Dispose(); outer.Dispose();
        f.Local.HealthController.IsAlive = false;
        ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
        f.Silent("raid reset and stale disposals cannot resurrect context");
        f.Local.HealthController.IsAlive = true; f.Die(info);
        f.Silent("raid reset removes item origins");
        f.Local.HealthController.IsAlive = true; f.Throw(); f.Die(info);
        Check(f.Text.Count == 1, "fresh throw after reset reports normally");
        f.Grenade = new ThrowWeap { Id = "new-life-grenade", TemplateId = "f1-template", FragmentType = "f1-fragment-template" };
        f.BindBody(new Player { ProfileId = f.Local.ProfileId }); f.Throw(); f.Die(f.Info());
        Check(f.Text.Count == 2, "same-profile replacement can earn its own taunt without inheriting old-body dedupe");
        f.Neutral();
        f = new Fixture(); f.Throw(); info = f.Info();
        using (ArenaSelfGrenadeTaunt.BeginDamage(f.Local, info))
        {
            ArenaShotAwardHistory.Reset(); f.Local.HealthController.IsAlive = false;
            ArenaSelfGrenadeTaunt.ObserveDeath(f.Local, f.Local, info, info.DamageType);
            f.Silent("round reset invalidates active damage scope");
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            ArenaSelfGrenadeTaunt.Enable();
            foreach (var (name, suite) in new (string, Action)[] { ("positive", Positive), ("sources", Sources), ("identity", Identity), ("contexts", Contexts) })
                if (args.Length == 0 || args[0] == "all" || args[0] == name) suite();
            Check(_checks > 0, "requested suite ran");
            Console.WriteLine($"SELF_GRENADE_PRODUCTION_PASSED: {_checks} assertions; full production taunt and actual extracted text dispatch; not native runtime proof");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
