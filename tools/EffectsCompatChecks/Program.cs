using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFTEffectManager;
using HarmonyLib;
using JsonType;
using Redeploy.Core.Api;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Token = "67a438725fa04c4a9e52205baec1b521";
    private static int _checks, _other;
    private static void Need(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("ASSERT: " + label); }
    public static object Call(MethodInfo method, object instance, params object[] args)
    {
        var info = Harmony.GetPatchInfo(method);
        bool run = true;
        foreach (var patch in info?.Prefixes ?? new())
        {
            var result = patch.PatchMethod.Invoke(null, args);
            if (result is false) run = false;
        }
        object resultValue = run ? method.Invoke(instance, args) : null;
        foreach (var patch in info?.Postfixes ?? new()) patch.PatchMethod.Invoke(null, args);
        return resultValue;
    }
    private static void Start(GameWorld world)
        => Call(typeof(EffectsManager).GetMethod("NotifyRaidStarted", Static), null, world);
    private static void End(GameWorld world)
        => Call(typeof(EffectsManager).GetMethod("NotifyRaidEnded", Static), null, new object[] { world });
    private static Player Body(LocalGame game)
    {
        var player = new Player { GameWorld = game.GameWorld, Profile = game.Profile };
        player.HealthController.Skills[ESkillId.Strength] = game.Profile.Skills.DisplayList[0];
        return player;
    }
    private static LocalGame NewGame(bool arena = true)
    {
        ArenaEffectsCompat.Shutdown();
        End(null);
        ArenaMode.Combat.ArenaSkills.Restore();
        var world = new GameWorld { LocationId = "factory4_day" };
        var game = new LocalGame { GameWorld = world, Profile = new Profile { Id = "same-profile" },
            _raidSettings = new() { location = world.LocationId, serverId = Protocol.BuildNativeEndIdentity("native", Token), playerSide = ESideType.Pmc } };
        world.MainPlayer = game.LocalPlayer = Body(game);
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world; Singleton<IBotGame>.Instance = game;
        ArenaSession.IsActive = arena; ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = world;
        ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false;
        ArenaSession.ArmedMap = world.LocationId; ArenaServerClient.PendingArmToken = Token;
        RespawnApi.IsReplacing = false;
        EffectsManager.Configure();
        ArenaEffectsCompat.Initialize();
        Need(ArenaEffectsCompat.GetRejectionReason() == null, "reviewed contract admits");
        return game;
    }
    private static void Replace(LocalGame game, bool dead = false, bool publish = true)
    {
        var old = game.LocalPlayer;
        old.HealthController.IsAlive = !dead;
        var next = Body(game);
        // The native bodies share Profile.Skills; cleanup must not apply twice.
        next.HealthController.Skills = old.HealthController.Skills;
        next.HealthController.Effects.Add(new Effect());
        game.LocalPlayer = game.GameWorld.MainPlayer = next;
        RespawnApi.IsReplacing = true;
        if (publish) RespawnApi.Fire(old, next);
        RespawnApi.IsReplacing = false;
    }
    private static void Main(string[] args)
    {
        PublishedMetadata.Verify(args[0], args[1], args[2], Need);
        EffectsManager.Registrations.Add(typeof(Effect), new());
        EffectsManager.LocalEffectStarted += (_, _) => _other++;
        var game = NewGame();
        Start(game.GameWorld);
        var first = game.LocalPlayer;
        Need(first.HealthController.Subscribers == 6 && first.DamageSubscribers == 1, "initial six health and one damage callbacks");
        first.HealthController.EffectStartedEvent += _ => _other++;
        var effect = new Effect(); first.HealthController.Effects.Add(effect);
        EffectsManager.Bonus(first.HealthController, 5, effect);
        EffectsManager.RefreshPenalties(first);
        var unrelated = new Player(); var unrelatedEffect = new Effect();
        EffectsManager.Bonus(unrelated.HealthController, 9, unrelatedEffect);
        int starts = EffectsManager.OriginalStarts;
        Replace(game);
        Need(ReferenceEquals(EffectsManager.RaidPlayer, game.LocalPlayer), "living replacement bound");
        Need(first.HealthController.Subscribers == 1 && first.DamageSubscribers == 0, "only foreign old callbacks removed");
        Need(game.LocalPlayer.HealthController.Subscribers == 6 && game.LocalPlayer.DamageSubscribers == 1, "new callbacks bound once");
        Need(first.HealthController.Skills[ESkillId.Strength].Buff == 0 && !EffectsManager.HasBonus(first.HealthController)
            && !EffectsManager.HasEffect(effect), "old temporary skill bonus removed exactly");
        Need(unrelated.HealthController.Skills[ESkillId.Strength].Buff == 9 && EffectsManager.HasBonus(unrelated.HealthController)
            && EffectsManager.HasEffect(unrelatedEffect), "unrelated controller bonuses preserved");
        Need(!first.MovementContext.Limited && game.LocalPlayer.MovementContext.Limited, "old speed limit removed and new effect evaluated");
        Need(EffectsManager.StrengthCalls == 1 && EffectsManager.SkillCalls == 1, "new existing effects initialized");
        Need(EffectsManager.OriginalStarts == starts, "world start never replayed");
        int seen = EffectsManager.Observations, other = _other;
        first.HealthController.Fire(); first.Damage();
        Need(EffectsManager.Observations == seen && _other == other + 1, "old body cannot deliver foreign callbacks");
        game.LocalPlayer.HealthController.Fire(); game.LocalPlayer.Damage();
        Need(EffectsManager.Observations == seen + 7, "replacement delivers all callbacks");
        ArenaEffectsCompat.Tick(); ArenaEffectsCompat.Tick();
        RespawnApi.Fire(first, game.LocalPlayer);
        Need(game.LocalPlayer.HealthController.Subscribers == 6 && EffectsManager.SkillCalls == 1
            && game.LocalPlayer.HealthController.Skills[ESkillId.Strength].Buff == 0, "repeat event and tick are idempotent");
        Need(ArenaEffectsCompat.GetRejectionReason() == null, "retired duplicate event does not fault");
        Need(EffectsManager.Registrations.Count == 1 && EffectsManager.MovementSpeedPenalties.Providers.Count == 2,
            "global registrations and providers preserved");
        var living = game.LocalPlayer;
        Replace(game, dead: true);
        Need(living.HealthController.Subscribers == 0 && ReferenceEquals(EffectsManager.RaidPlayer, game.LocalPlayer), "death replacement bound");
        var oldWorld = game.GameWorld;
        End(new GameWorld { LocationId = oldWorld.LocationId });
        Need(ReferenceEquals(EffectsManager.RaidPlayer, game.LocalPlayer), "foreign world dispose cannot release current body");
        var last = game.LocalPlayer; int ends = EffectsManager.OriginalEnds;
        RespawnApi.End();
        Need(EffectsManager.RaidPlayer == null && last.HealthController.Subscribers == 0 && last.DamageSubscribers == 0,
            "raid end removes exact callbacks");
        Need(EffectsManager.OriginalEnds == ends && EffectsManager.HasBonus(unrelated.HealthController),
            "Arena end does not clear foreign controller state");
        End(oldWorld); RespawnApi.End();
        Need(EffectsManager.HasBonus(unrelated.HealthController), "duplicate old-world end leaves foreign ledger intact");

        game = NewGame(false); Start(game.GameWorld); var ordinary = game.LocalPlayer;
        Replace(game);
        ArenaEffectsCompat.Tick();
        Need(ReferenceEquals(EffectsManager.RaidPlayer, ordinary) && ordinary.HealthController.Subscribers == 6,
            "ordinary body replacement untouched");
        End(game.GameWorld);
        Need(EffectsManager.RaidPlayer == null && ordinary.HealthController.Subscribers == 0, "ordinary end executes original");

        // Confirmed loading arm is accepted; a pending token alone is not.
        game = NewGame(false); ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = true;
        game.Status = GameStatus.Starting; Start(game.GameWorld); var armed = game.LocalPlayer; Replace(game);
        Need(armed.HealthController.Subscribers == 0 && ReferenceEquals(EffectsManager.RaidPlayer, game.LocalPlayer),
            "confirmed loading arm replacement captured");
        game = NewGame(false); ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = true;
        game._raidSettings.serverId = "ordinary"; Start(game.GameWorld); var unconfirmed = game.LocalPlayer; Replace(game);
        Need(ReferenceEquals(EffectsManager.RaidPlayer, unconfirmed), "unconfirmed arm does not capture");

        game = NewGame(); Start(game.GameWorld); var initial = game.LocalPlayer;
        // Simulate initial foreign start before the adapter sees its observer.
        var cleared = typeof(ArenaEffectsCompat).GetField("Scope", Static).GetValue(null) is ArenaEffectsScope scope1
            ? Clear(scope1) : throw new Exception("missing scope");
        Replace(game);
        Need(initial.HealthController.Subscribers == 0 && ReferenceEquals(EffectsManager.RaidPlayer, game.LocalPlayer),
            "initial kit replacement before observed start captured");

        game = NewGame(); Start(game.GameWorld); var fallback = game.LocalPlayer;
        Replace(game, publish: false); RespawnApi.IsReplacing = true; ArenaEffectsCompat.Tick();
        Need(ReferenceEquals(EffectsManager.RaidPlayer, fallback), "pending creation not rebound");
        RespawnApi.IsReplacing = false; ArenaEffectsCompat.Tick();
        Need(ReferenceEquals(EffectsManager.RaidPlayer, game.LocalPlayer) && fallback.HealthController.Subscribers == 0,
            "settled authoritative fallback rebinds");

        foreach (var kind in new[] { "world", "local", "profile", "botgame", "scav", "stopping" })
        {
            game = NewGame(); Start(game.GameWorld); var previous = game.LocalPlayer;
            var next = Body(game); game.GameWorld.MainPlayer = game.LocalPlayer = next;
            if (kind == "world") next.GameWorld = new GameWorld();
            if (kind == "local") game.LocalPlayer = previous;
            if (kind == "profile") next.Profile = new Profile { Id = game.Profile.Id };
            if (kind == "botgame") Singleton<IBotGame>.Instance = new LocalGame();
            if (kind == "scav") game._raidSettings.playerSide = ESideType.Savage;
            if (kind == "stopping") game.Status = GameStatus.Stopping;
            RespawnApi.Fire(previous, next); ArenaEffectsCompat.Tick();
            Need(ReferenceEquals(EffectsManager.RaidPlayer, previous), "mismatched " + kind + " not rebound");
        }
        game = NewGame(); Start(game.GameWorld);
        var collision = new MovementContext { Limited = true };
        EffectsManager.MovementSpeedPenalties.WrongContext(collision);
        Replace(game);
        Need(ArenaEffectsCompat.GetRejectionReason()?.Contains("another body") == true && collision.Limited,
            "unknown speed owner rejected without clearing it");
        ArenaEffectsCompat.Shutdown(); End(null);
        EffectsManager.Configure(false); ArenaEffectsCompat.Initialize();
        Need(ArenaEffectsCompat.GetRejectionReason()?.Contains("initialization") == true, "uninitialized library fails Arena admission");
        EffectsManager.Configure();
        Need(ArenaEffectsCompat.GetRejectionReason() == null, "late library initialization retried");
        Harmony.Bound[typeof(GameWorld).GetMethod("OnGameStarted")].Postfixes.Clear();
        Need(ArenaEffectsCompat.GetRejectionReason()?.Contains("native lifecycle") == true, "missing foreign lifecycle receipt rejected");
        ArenaEffectsCompat.Shutdown();
        SkillIntegration();
        SkillCleanupFailures();
        Console.WriteLine($"EFFECTS_COMPAT_CHECKS_PASSED ({_checks})");
    }
    private static bool Clear(ArenaEffectsScope scope) { scope.Clear(); return true; }

    private static void SkillIntegration()
    {
        foreach (bool effectEndsFirst in new[] { false, true })
        {
            var game = NewGame(); Start(game.GameWorld); var player = game.LocalPlayer;
            var skill = player.Skills.DisplayList[0]; skill.Buff = 2;
            var effect = new Effect(); EffectsManager.Bonus(player.HealthController, 5, effect);
            var foreign = new Player(); EffectsManager.Bonus(foreign.HealthController, 11, new Effect());
            ArenaMode.Combat.ArenaSkills.Apply(game.GameWorld);
            Need(skill.Buff == 55 && ArenaEffectsCompat.SkillBonusFor(skill) == 5,
                "existing Effects bonus coexists with actual Arena elite boost");
            Need(ArenaEffectsCompat.SkillBonusFor(foreign.HealthController.Skills[ESkillId.Strength]) == 0,
                "foreign skill lookup returns zero");
            ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
            ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
            Need(ArenaEffectsCompat.SkillBonusFor(skill) == 5, "skill cleanup lookup survives cleared Arena flags");
            if (effectEndsFirst) RespawnApi.End();
            ArenaMode.Combat.ArenaSkills.Restore();
            Need(skill.Buff == (effectEndsFirst ? 2 : 7), "actual Arena restore retains only live Effects contribution");
            if (!effectEndsFirst) RespawnApi.End();
            Need(skill.Buff == 2 && foreign.HealthController.Skills[ESkillId.Strength].Buff == 11,
                "both native teardown orders restore original skill without touching foreign controller");
        }
        {
            var game = NewGame(); Start(game.GameWorld); var player = game.LocalPlayer;
            var skill = player.Skills.DisplayList[0]; skill.Buff = 2;
            var effect = new Effect(); EffectsManager.Bonus(player.HealthController, 5, effect);
            ArenaMode.Combat.ArenaSkills.Apply(game.GameWorld);
            EffectsManager.Expire(player.HealthController, effect);
            Need(skill.Buff == 50, "Effects expiration leaves Arena elite boost intact");
            ArenaMode.Combat.ArenaSkills.Restore();
            Need(skill.Buff == 2, "expired pre-Apply bonus not restored from snapshot");
        }
        {
            var game = NewGame(); Start(game.GameWorld); var player = game.LocalPlayer;
            var skill = player.Skills.DisplayList[0]; skill.Buff = 2;
            ArenaMode.Combat.ArenaSkills.Apply(game.GameWorld);
            EffectsManager.Bonus(player.HealthController, 5, new Effect());
            Need(skill.Buff == 55, "post-Apply foreign bonus additive");
            ArenaMode.Combat.ArenaSkills.Restore();
            Need(skill.Buff == 7, "post-Apply bonus survives restore");
            RespawnApi.End(); Need(skill.Buff == 2, "post-Apply bonus eventually removed");
        }
        {
            var game = NewGame(); Start(game.GameWorld); var player = game.LocalPlayer;
            var skill = player.Skills.DisplayList[0]; skill.Buff = 2;
            EffectsManager.Bonus(player.HealthController, 5, new Effect());
            ArenaMode.Combat.ArenaSkills.Apply(game.GameWorld);
            Replace(game); ArenaEffectsCompat.Tick();
            Need(ReferenceEquals(skill, game.LocalPlayer.Skills.DisplayList[0]) && skill.Buff == 50,
                "shared native profile keeps elite boost after old-body bonus cleanup");
            ArenaMode.Combat.ArenaSkills.Restore(); RespawnApi.End();
            Need(skill.Buff == 2, "shared profile restores pre-effect baseline after replacement");
        }
        {
            var game = NewGame(false); Start(game.GameWorld);
            var skill = game.LocalPlayer.Skills.DisplayList[0]; EffectsManager.Bonus(game.LocalPlayer.HealthController, 5, new Effect());
            Need(ArenaEffectsCompat.SkillBonusFor(skill) == 0, "ordinary skill ledger never claimed");
            ArenaMode.Combat.ArenaSkills.Apply(game.GameWorld); ArenaMode.Combat.ArenaSkills.Restore();
            Need(skill.Buff == 5, "ordinary temporary bonus unchanged by Arena skill system");
        }
        ArenaEffectsCompat.Shutdown(); End(null);
    }
    private static void SkillCleanupFailures()
    {
        foreach (bool afterWrite in new[] { false, true })
        {
            var game = NewGame(); Start(game.GameWorld); var old = game.LocalPlayer;
            var strength = old.Skills.DisplayList[0];
            var second = new Skill();
            old.HealthController.Skills[ESkillId.Endurance] = second;
            EffectsManager.Bonus(old.HealthController, 5, new Effect());
            EffectsManager.MoreBonus(old.HealthController, ESkillId.Endurance, 9);
            Action<int> fail = _ => throw new InvalidOperationException("injected skill listener failure");
            if (afterWrite) second.OnWrite = fail; else second.BeforeWrite = fail;
            Replace(game);
            Need(ArenaEffectsCompat.GetRejectionReason()?.Contains("listener failure") == true,
                "native skill write failure is surfaced");
            Need(ReferenceEquals(EffectsManager.RaidPlayer, old) && old.HealthController.Subscribers == 6,
                "failed skill cleanup retains old callbacks for retry");
            Need(strength.Buff == 0 && second.Buff == (afterWrite ? 0 : 9),
                "each completed skill subtraction commits before later failure");
            Need(ArenaEffectsCompat.SkillBonusFor(strength) == 0
                && ArenaEffectsCompat.SkillBonusFor(second) == (afterWrite ? 0 : 9),
                "failed write ledger follows actual native field state");
            second.OnWrite = second.BeforeWrite = null;
            RespawnApi.End();
            Need(strength.Buff == 0 && second.Buff == 0 && !EffectsManager.HasBonus(old.HealthController),
                "cleanup retry never subtracts an already completed contribution twice");
            Need(EffectsManager.RaidPlayer == null && old.HealthController.Subscribers == 0,
                "cleanup retry finally releases exact old binding");
        }
        ArenaEffectsCompat.Shutdown(); End(null);
    }
}
