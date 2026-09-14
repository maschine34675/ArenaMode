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
        internal Weapon Weapon = new() { Id = "weapon-one", BoltAction = true };
        internal Player.FirearmController Gun;
        internal ClientGameWorld World;
        internal LocalGame Game;
        internal Fixture(bool matureFlight = true)
        {
            ArenaShotAwardHooks.Reset();
            ArenaHitHistory.Reset();
            ArenaSession.IsActive = true;
            Gun = new() { _player = Local, Item = Weapon };
            Local.HandsController = Gun;
            World = new() { MainPlayer = Local };
            Game = new() { LocalPlayer = Local, Status = GameStatus.Started };
            Singleton<GameWorld>.Instance = World;
            Singleton<AbstractGame>.Instance = Game;
            ArenaSession.Game = Game;
            ArenaSession.World = World;
            ArenaMatchService.Current = new();
            UnityEngine.Time.time = 0f;
            UnityEngine.Time.deltaTime = 0.1f;
            if (matureFlight) FlyUntil(0.5f);
        }
        internal void Observe(float at, float deltaTime = 0.1f)
        {
            UnityEngine.Time.time = at;
            UnityEngine.Time.deltaTime = deltaTime;
            PatchMethod("FlightPatch", "Postfix").Invoke(null, new object[] { Local.MovementContext, deltaTime });
        }
        internal void FlyUntil(float end)
        {
            float start = UnityEngine.Time.time;
            Observe(start);
            for (int step = 1; start + step * 0.05f < end; step++) Observe(start + step * 0.05f);
            Observe(end);
        }
        internal Shot Raw(int fire = 11, Shot parent = null) => new()
        {
            Player = new() { iPlayer = Local }, Weapon = Weapon,
            Ammo = new() { Id = "cartridge-" + fire, TemplateId = "ammo-template" },
            FireIndex = fire, Parent = parent
        };
        internal Shot Fire(int fire = 11)
        {
            var shot = Raw(fire);
            using (ArenaShotAwardHooks.BeginFire(Gun, Weapon)) ArenaShotAwardHooks.Created(shot);
            return shot;
        }
        internal ArenaShotAwardFacts Impact(Shot shot)
        {
            using var scope = ArenaShotAwardHooks.BeginImpact(World, shot);
            return ArenaShotAwardHooks.CurrentFacts(Damage(shot), Id(shot));
        }
    }

    private static void Check(bool result, string label)
    {
        _checks++;
        if (!result) throw new Exception("FAILED: " + label);
    }
    private static DamageInfo Damage(Shot shot) => new()
    {
        DamageType = EDamageType.Bullet, Player = shot.Player, Weapon = shot.Weapon,
        FireIndex = shot.FireIndex, SourceId = shot.Ammo.TemplateId
    };
    private static ShotId Id(Shot shot) => new(shot.Ammo.Id, shot.FragmentIndex);
    private static ArenaShotAwardFacts Death(object victim, Player shooter, ArenaShotAwardFacts shot,
        ArenaShotAwardFacts damage, bool lethalBullet = true)
    {
        using var shotScope = ArenaHitHistory.BeginShot(victim, shooter, true, false, shot);
        using var damageScope = ArenaHitHistory.BeginDamage(victim, shooter, true, false, damage);
        return ArenaHitHistory.ShotAwards(victim, shooter, lethalBullet);
    }
    private static MethodInfo PatchMethod(string patch, string name)
        => typeof(ArenaShotAwardHooks).GetNestedType(patch, BindingFlags.NonPublic)
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
    private static IDisposable FirePrefix(Fixture f)
    {
        object[] args = { f.Gun, f.Weapon, null };
        PatchMethod("FirePatch", "Prefix").Invoke(null, args);
        return (IDisposable)args[2];
    }
    private static IDisposable ImpactPrefix(Fixture f, Shot shot)
    {
        object[] args = { f.World, shot, null };
        PatchMethod("ImpactPatch", "Prefix").Invoke(null, args);
        return (IDisposable)args[2];
    }
    private static Exception Finalize(string patch, Exception exception, IDisposable scope)
        => (Exception)PatchMethod(patch, "Finalizer").Invoke(null, new object[] { exception, scope });

    private static void NativeBoundary()
    {
        Check(ModulePatch.Targets.Count == 4, "all four native hook targets registered");
        Check(ModulePatch.Targets[0].DeclaringType == typeof(Player.FirearmController)
            && ModulePatch.Targets[0].Name == "InitiateShot", "fire hook targets actual initiation");
        Check(ModulePatch.Targets[1].DeclaringType == typeof(Shot)
            && ModulePatch.Targets[1].Name == "Create", "creation hook targets pooled creation");
        Check(ModulePatch.Targets[2].DeclaringType == typeof(ClientGameWorld)
            && ModulePatch.Targets[2].Name == "ShotDelegate", "impact hook targets local world override");
        Check(ModulePatch.Targets[3].DeclaringType == typeof(MovementContext)
            && ModulePatch.Targets[3].Name == "CheckFlying", "flight observation follows native ground refresh");
        Check(PatchMethod("FlightPatch", "Postfix").IsDefined(typeof(PatchPostfixAttribute)), "flight observer is postfix only");
        foreach (var patch in new[] { "FirePatch", "ImpactPatch" })
        {
            Check(PatchMethod(patch, "Prefix").IsDefined(typeof(PatchPrefixAttribute)), patch + " has prefix attribute");
            Check(PatchMethod(patch, "Finalizer").IsDefined(typeof(PatchFinalizerAttribute)), patch + " has finalizer attribute");
        }
        Check(PatchMethod("CreatePatch", "Postfix").IsDefined(typeof(PatchPostfixAttribute)), "creation hook has postfix attribute");
    }
    private static void Conditions()
    {
        foreach (bool movementGround in new[] { false, true })
        foreach (bool capsuleGround in new[] { false, true })
        foreach (bool aiming in new[] { false, true })
        foreach (bool bolt in new[] { false, true })
        {
            var f = new Fixture();
            f.Local.MovementContext.IsGrounded = movementGround;
            f.Local.CharacterController.isGrounded = capsuleGround;
            f.Gun.IsAiming = aiming;
            f.Weapon.BoltAction = bolt;
            var facts = f.Impact(f.Fire());
            Check(facts.IsKnown == !aiming, "aimed fire has no award snapshot");
            Check(facts.AirborneHipFire == (!aiming && !movementGround && !capsuleGround), "airborne requires both native ground checks and no ADS");
            Check(facts.BoltActionHipFire == (!aiming && bolt), "no scope requires actual bolt action and no ADS");
        }
        foreach (Action<Fixture> missing in new Action<Fixture>[]
        {
            f => f.Local.MovementContext = null, f => f.Local.CharacterController = null
        })
        {
            var f = new Fixture(); missing(f);
            var facts = f.Impact(f.Fire());
            Check(!facts.AirborneHipFire && facts.BoltActionHipFire, "unknown airborne state does not discard proven bolt no scope");
        }
        var landed = new Fixture();
        var shot = landed.Fire();
        landed.Local.MovementContext.IsGrounded = landed.Local.CharacterController.isGrounded = true;
        landed.Gun.IsAiming = true;
        landed.Weapon.BoltAction = false;
        landed.Local.HandsController = new Player.FirearmController { _player = landed.Local,
            Item = new Weapon { Id = "different-current-weapon" }, IsAiming = true };
        var frozen = landed.Impact(shot);
        Check(frozen.AirborneHipFire && frozen.BoltActionHipFire, "landing ADS and weapon switch preserve actual firing facts");
        var later = landed.Raw(12);
        ArenaShotAwardHooks.Created(later);
        Check(!landed.Impact(later).IsKnown, "creation outside actual fire cannot borrow a previous shot");
    }
    private static void Airtime()
    {
        foreach (float elapsed in new[] { 0f, 0.01f, 0.25f, 0.499f, 0.5f, 0.75f })
        {
            var f = new Fixture(false); f.FlyUntil(elapsed);
            var facts = f.Impact(f.Fire());
            Check(facts.AirborneHipFire == (elapsed >= 0.5f), "airtime threshold is inclusive and rejects short drops");
            Check(facts.BoltActionHipFire, "short flight does not suppress independently proven no scope");
        }
        var absent = new Fixture(false); UnityEngine.Time.time = 5f;
        Check(!absent.Impact(absent.Fire()).AirborneHipFire, "no observed flight cannot qualify from process age");
        var early = new Fixture(false); early.FlyUntil(0.25f); var earlyShot = early.Fire(); early.FlyUntil(0.75f);
        Check(!early.Impact(earlyShot).AirborneHipFire, "later airtime before impact cannot promote an early shot");
        Check(early.Impact(early.Fire(12)).AirborneHipFire, "continuous flight survives another shot and airborne impulse");
        foreach (bool movementGround in new[] { true, false })
        {
            var f = new Fixture();
            f.Local.MovementContext.IsGrounded = movementGround;
            f.Local.CharacterController.isGrounded = !movementGround;
            f.Observe(0.55f);
            f.Local.MovementContext.IsGrounded = f.Local.CharacterController.isGrounded = false;
            f.FlyUntil(0.75f);
            Check(!f.Impact(f.Fire()).AirborneHipFire, "either grounding signal restarts continuous flight");
        }
        foreach (Action<Fixture> invalidate in new Action<Fixture>[]
        {
            f => { f.Local = new Player { HandsController = f.Gun }; f.Gun._player = f.Local; f.World.MainPlayer = f.Game.LocalPlayer = f.Local; },
            f => f.Local.MovementContext = new MovementContext(),
            f => { f.World = new ClientGameWorld { MainPlayer = f.Local }; Singleton<GameWorld>.Instance = f.World; ArenaSession.World = f.World; },
            f => { f.Game = new LocalGame { LocalPlayer = f.Local, Status = GameStatus.Started }; Singleton<AbstractGame>.Instance = f.Game; ArenaSession.Game = f.Game; },
            f => ArenaShotAwardHistory.Reset()
        })
        {
            var f = new Fixture(); invalidate(f);
            Check(!f.Impact(f.Fire()).AirborneHipFire, "changed body context world game or round cannot inherit flight");
            f.FlyUntil(1f);
            Check(f.Impact(f.Fire(12)).AirborneHipFire, "fresh identity can earn a new full flight");
        }
        foreach (Action<Fixture> block in new Action<Fixture>[]
        {
            f => f.Local.MovementContext.Vaulting = true,
            f => f.Local.MovementContext.IsInMountedState = true,
            f => f.Local.MovementContext.IsStationaryWeaponInHands = true,
            f => f.Local.HealthController.IsAlive = false
        })
        {
            var f = new Fixture(); block(f); f.Observe(0.55f);
            Check(!f.Impact(f.Fire()).AirborneHipFire, "vault mounted stationary or dead body cannot earn flight");
        }
        var bot = new Fixture();
        ArenaShotAwardHooks.ObserveFlight(new MovementContext { IsGrounded = true }, 0.1f);
        Check(bot.Impact(bot.Fire()).AirborneHipFire, "unrelated bot ground update cannot erase local flight");
        var paused = new Fixture(false); paused.FlyUntil(0.25f);
        for (int n = 0; n < 20; n++) paused.Observe(0.25f, 0f);
        Check(!paused.Impact(paused.Fire()).AirborneHipFire, "paused scaled clock does not grant airtime");
        paused.FlyUntil(0.5f);
        Check(paused.Impact(paused.Fire(12)).AirborneHipFire, "unpaused continuous flight resumes timing");
        var stale = new Fixture(); UnityEngine.Time.time = 2f;
        Check(!stale.Impact(stale.Fire()).AirborneHipFire, "stale movement observation cannot qualify a shot");
        stale.Observe(2f);
        Check(!stale.Impact(stale.Fire(12)).AirborneHipFire, "resumed observation starts a fresh flight");
        var gap = new Fixture(); gap.Observe(2f);
        Check(!gap.Impact(gap.Fire()).AirborneHipFire, "unobserved movement gap resets flight even before firing");
        var slow = new Fixture(false); slow.Observe(0f, 0.25f); slow.Observe(0.25f, 0.25f); slow.Observe(0.5f, 0.25f);
        Check(slow.Impact(slow.Fire()).AirborneHipFire, "real slow movement steps still count simulation time");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            var f = new Fixture(); f.Observe(invalid);
            Check(!f.Impact(f.Fire()).AirborneHipFire, "invalid or backwards clock cannot grant flight");
            f = new Fixture(); f.Observe(0.6f, invalid);
            Check(!f.Impact(f.Fire()).AirborneHipFire, "invalid movement timestep clears flight");
        }
        var reset = new Fixture(); ArenaShotAwardHooks.Reset();
        Check(!reset.Impact(reset.Fire()).AirborneHipFire, "raid reset removes observed flight");
    }

    private static void Admission()
    {
        var cases = new (string Label, Action<Fixture> Change)[]
        {
            ("normal raid", f => ArenaSession.IsActive = false),
            ("foreign active raid", f => ArenaSession.World = new ClientGameWorld()),
            ("stopped game", f => f.Game.Status = GameStatus.Stopped),
            ("missing world", f => Singleton<GameWorld>.Instance = null),
            ("missing game", f => Singleton<AbstractGame>.Instance = null),
            ("nonlocal game", f => Singleton<AbstractGame>.Instance = new AbstractGame()),
            ("missing match", f => ArenaMatchService.Current = null),
            ("missing mode", f => ArenaMatchService.Current.Mode = null),
            ("complete mode", f => ArenaMatchService.Current.Mode.IsComplete = true),
            ("AI shooter", f => f.Local.IsAI = true),
            ("remote shooter", f => f.Local.IsYourPlayer = false),
            ("mismatched local owner", f => f.Game.LocalPlayer = new Player()),
            ("dead firing body", f => f.Local.HealthController.IsAlive = false),
            ("missing health", f => f.Local.HealthController = null),
            ("stale controller body", f => f.Gun._player = new Player()),
            ("inactive hands", f => f.Local.HandsController = new Player.ItemHandsController()),
            ("different firing item", f => f.Gun.Item = new Weapon { Id = "other" }),
            ("underbarrel weapon", f => f.Weapon.IsUnderbarrelWeapon = true),
            ("grenade launcher", f => f.Weapon.IsGrenadeLauncher = true)
        };
        foreach (var test in cases)
        {
            var f = new Fixture(); test.Change(f);
            Check(!f.Impact(f.Fire()).IsKnown, test.Label + " cannot create award facts");
        }
        var prior = new Fixture(); var oldShot = prior.Fire();
        prior.World.MainPlayer = prior.Game.LocalPlayer = new Player { ProfileId = prior.Local.ProfileId };
        Check(!prior.Impact(oldShot).IsKnown, "old respawn body cannot award its replacement with the same profile");
        var current = new Fixture(); var valid = current.Fire();
        using (ArenaShotAwardHooks.BeginImpact(new ClientGameWorld(), valid))
            Check(!ArenaShotAwardHooks.CurrentFacts(Damage(valid), Id(valid)).IsKnown, "foreign impact world is rejected");
        valid.IsFlyingOutOfTime = true;
        Check(!current.Impact(valid).IsKnown, "expired bullet has no impact facts");
        using (ArenaShotAwardHooks.BeginFire(current.Gun, null))
        {
            var unknown = current.Raw(99); ArenaShotAwardHooks.Created(unknown);
            Check(!current.Impact(unknown).IsKnown, "unknown firing weapon is rejected");
        }
    }
    private static void Correlation()
    {
        var f = new Fixture(); var shot = f.Fire(); var info = Damage(shot);
        using var impact = ArenaShotAwardHooks.BeginImpact(f.World, shot);
        var facts = ArenaShotAwardHooks.CurrentFacts(info, Id(shot));
        Check(facts.IsKnown, "exact native bullet contact is admitted");
        Check(!ArenaShotAwardHooks.CurrentFacts(info, new ShotId("other-cartridge", 0)).IsKnown, "wrong cartridge shot id rejected");
        Check(!ArenaShotAwardHooks.CurrentFacts(info, new ShotId(shot.Ammo.Id, 88)).IsKnown, "wrong fragment shot id rejected");
        var wrong = info; wrong.Weapon = new Weapon { Id = "wrong-weapon" };
        Check(!ArenaShotAwardHooks.CurrentFacts(wrong).IsKnown, "wrong damage weapon rejected");
        wrong = info; wrong.FireIndex++;
        Check(!ArenaShotAwardHooks.CurrentFacts(wrong).IsKnown, "wrong fire index rejected");
        wrong = info; wrong.SourceId = "other-ammo-template";
        Check(!ArenaShotAwardHooks.CurrentFacts(wrong).IsKnown, "wrong ammunition source rejected");
        wrong = info; wrong.Player = new Bridge { iPlayer = new Player() };
        Check(!ArenaShotAwardHooks.CurrentFacts(wrong).IsKnown, "wrong damage body rejected");
        foreach (var kind in new[] { EDamageType.Blunt, EDamageType.Explosion, EDamageType.HeavyBleeding, EDamageType.Fall })
        {
            wrong = info; wrong.DamageType = kind;
            Check(!ArenaShotAwardHooks.CurrentFacts(wrong).IsKnown, kind + " does not inherit projectile facts");
        }
        var victim = new object();
        Check(Death(victim, f.Local, facts, facts).IsKnown, "actual shot and damage conjunction awards direct bullet death");
        Check(!Death(victim, f.Local, facts, facts, false).IsKnown, "later nonbullet death cannot inherit shot facts");
        Check(!Death(victim, new Player(), facts, facts).IsKnown, "conjunction requires original physical shooter");
        var other = f.Fire(12); var different = f.Impact(other);
        Check(!Death(victim, f.Local, facts, different).IsKnown, "different projectile damage cannot satisfy lethal conjunction");
        using (ArenaHitHistory.BeginDamage(victim, f.Local, true, false, facts))
            Check(!ArenaHitHistory.ShotAwards(victim, f.Local, true).IsKnown, "direct damage without native ApplyShot cannot earn award");
        using (ArenaHitHistory.BeginShot(victim, f.Local, true, false, facts))
        using (ArenaHitHistory.BeginDamage(victim, f.Local, true, false, facts))
        {
            using (ArenaHitHistory.BeginShot(new object(), f.Local, true, false, different))
                Check(!ArenaHitHistory.ShotAwards(victim, f.Local, true).IsKnown, "nested foreign victim cannot lend or borrow outer award");
            Check(ArenaHitHistory.ShotAwards(victim, f.Local, true).IsKnown, "outer contact restored after nested contact");
        }
        Check(!ArenaHitHistory.ShotAwards(victim, f.Local, true).IsKnown, "disposed lethal contacts cannot be reused");
    }
    private static void Lifetime()
    {
        var f = new Fixture(); var root = f.Fire();
        var child = f.Raw(root.FireIndex, root); child.FragmentIndex = 1;
        ArenaShotAwardHooks.Created(child);
        var inherited = f.Impact(child);
        Check(inherited.AirborneHipFire && inherited.BoltActionHipFire, "fragment inherits actual firing facts outside fire scope");
        ArenaShotAwardHooks.Created(root);
        Check(!f.Impact(root).IsKnown, "unknown pooled create erases former firing facts");
        Check(f.Impact(child).IsKnown, "created child retains copied facts after parent pool reuse");
        var unverifiedChild = f.Raw(root.FireIndex, root);
        using (ArenaShotAwardHooks.BeginFire(f.Gun, f.Weapon)) ArenaShotAwardHooks.Created(unverifiedChild);
        Check(!f.Impact(unverifiedChild).IsKnown, "unknown parent cannot borrow current ambient fire scope");
        root = f.Fire(22);
        using (ArenaShotAwardHooks.BeginImpact(f.World, root))
        {
            var before = ArenaShotAwardHooks.CurrentFacts(Damage(root), Id(root));
            using (ArenaShotAwardHooks.BeginFire(f.Gun, f.Weapon)) ArenaShotAwardHooks.Created(root);
            Check(!ArenaShotAwardHooks.CurrentFacts(Damage(root), Id(root)).IsKnown, "in-stack pooled reuse invalidates old impact token");
            Check(f.Impact(root).ProjectileToken != before.ProjectileToken, "pooled reuse receives fresh projectile identity");
        }
        root = f.Fire(23);
        using (ArenaShotAwardHooks.BeginImpact(f.World, root))
        {
            using (ArenaShotAwardHooks.BeginImpact(f.World, f.Raw(90)))
                Check(!ArenaShotAwardHooks.CurrentFacts(Damage(root), Id(root)).IsKnown, "unknown nested impact masks surrounding known impact");
            Check(ArenaShotAwardHooks.CurrentFacts(Damage(root), Id(root)).IsKnown, "outer impact restored after nested impact");
        }
        using (ArenaShotAwardHooks.BeginFire(f.Gun, f.Weapon))
        {
            using (ArenaShotAwardHooks.BeginFire(new Player.FirearmController { _player = new Player() }, f.Weapon))
            {
                var denied = f.Raw(91); ArenaShotAwardHooks.Created(denied);
                Check(!f.Impact(denied).IsKnown, "unknown nested fire masks surrounding known fire");
            }
            var permitted = f.Raw(92); ArenaShotAwardHooks.Created(permitted);
            Check(f.Impact(permitted).IsKnown, "outer fire restored after nested fire");
        }
        var fireOuter = ArenaShotAwardHooks.BeginFire(f.Gun, f.Weapon);
        var fireInner = ArenaShotAwardHooks.BeginFire(f.Gun, f.Weapon);
        var impactOuter = ArenaShotAwardHooks.BeginImpact(f.World, root);
        var impactInner = ArenaShotAwardHooks.BeginImpact(f.World, child);
        var oldFacts = f.Impact(root);
        using var oldContact = ArenaHitHistory.BeginShot(new object(), f.Local, true, false, oldFacts);
        ArenaShotAwardHooks.Reset(); ArenaHitHistory.Reset();
        fireInner.Dispose(); fireOuter.Dispose(); impactInner.Dispose(); impactOuter.Dispose();
        var orphan = f.Raw(93); ArenaShotAwardHooks.Created(orphan);
        Check(!f.Impact(orphan).IsKnown, "reset and stale finalizers cannot resurrect fire scope");
        Check(!ArenaShotAwardHooks.CurrentFacts(Damage(root), Id(root)).IsKnown, "reset and stale finalizers cannot resurrect impact scope");
        Check(!Death(new object(), f.Local, oldFacts, oldFacts).IsKnown, "reset invalidates copied facts and old hit scopes");
        var fresh = f.Fire(94); var newFacts = f.Impact(fresh);
        Check(Death(new object(), f.Local, newFacts, newFacts).IsKnown, "new session admits fresh native firing and lethal scopes after stale disposals");
        using (ArenaShotAwardHooks.BeginImpact(f.World, fresh))
        {
            ArenaShotAwardHistory.Reset();
            Check(!ArenaShotAwardHooks.CurrentFacts(Damage(fresh), Id(fresh)).IsKnown, "award round reset invalidates in-flight shot without raid reset");
        }
        using (ArenaShotAwardHooks.BeginFire(f.Gun, f.Weapon))
        {
            ArenaShotAwardHistory.Reset();
            var afterRoundReset = f.Raw(95); ArenaShotAwardHooks.Created(afterRoundReset);
            Check(!f.Impact(afterRoundReset).IsKnown, "award round reset invalidates enclosing fire scope before creation");
        }
    }
    private static void Finalizers()
    {
        var f = new Fixture(); var shot = f.Raw();
        var fire = FirePrefix(f);
        PatchMethod("CreatePatch", "Postfix").Invoke(null, new object[] { shot });
        var exception = new InvalidOperationException("native fire failure");
        Check(ReferenceEquals(exception, Finalize("FirePatch", exception, fire)), "fire finalizer preserves native exception");
        var afterFailure = f.Raw(40); ArenaShotAwardHooks.Created(afterFailure);
        Check(!f.Impact(afterFailure).IsKnown, "fire exception finalizer clears capture");
        Check(f.Impact(shot).IsKnown, "created projectile survives later fire exception");
        var impact = ImpactPrefix(f, shot);
        Check(ArenaShotAwardHooks.CurrentFacts(Damage(shot), Id(shot)).IsKnown, "impact prefix exposes frozen projectile");
        exception = new InvalidOperationException("native damage failure");
        Check(ReferenceEquals(exception, Finalize("ImpactPatch", exception, impact)), "impact finalizer preserves native exception");
        Check(!ArenaShotAwardHooks.CurrentFacts(Damage(shot), Id(shot)).IsKnown, "impact exception finalizer clears contact");
        Check(Finalize("ImpactPatch", null, null) == null, "empty finalizer leaves native result unchanged");
        using (ArenaShotAwardHooks.BeginFire(null, f.Weapon))
        {
            var invalid = f.Raw(41); ArenaShotAwardHooks.Created(invalid);
            Check(!f.Impact(invalid).IsKnown, "native owner read exception fails closed without borrowing capture");
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            ArenaShotAwardHooks.Enable();
            var suites = new Dictionary<string, Action>
            {
                ["boundary"] = NativeBoundary, ["conditions"] = Conditions, ["airtime"] = Airtime, ["admission"] = Admission,
                ["correlation"] = Correlation, ["lifetime"] = Lifetime, ["finalizers"] = Finalizers
            };
            foreach (var suite in suites)
                if (args.Length == 0 || args[0] == "all" || args[0] == suite.Key) suite.Value();
            Check(_checks > 1, "requested suite exists and ran");
            Console.WriteLine($"SHOT_AWARD_NATIVE_ADAPTER_PASSED: {_checks} assertions; boundary doubles, not Unity/Harmony runtime proof");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
