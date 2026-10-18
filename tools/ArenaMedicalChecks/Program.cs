using ArenaMode;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using Redeploy.Core.Engine;
using SPT.Reflection.Patching;
using System.Reflection;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckPatchRegistration();
            CheckWaitingEffectAndAnimation();
            CheckWorkingEffectProgress();
            CheckNormalRaidAndActorGates();
            CheckItemAndValueGates();
            CheckAnimatorFailureIsTransactional();
            CheckLifecycleRestoration();
            CheckSourceContract();
            ArenaMedicalPatches.Shutdown();
            Console.WriteLine($"ARENA_MEDICAL_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ARENA_MEDICAL_CHECKS_FAILED after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private static void CheckPatchRegistration()
    {
        Reset();
        ArenaMedicalPatches.Enable();
        ArenaMedicalPatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 2,
            "medical hooks register exactly once");
        MethodBase effect = ModulePatch.EnabledTargets[0];
        MethodBase destroy = ModulePatch.EnabledTargets[1];
        Assert(effect.DeclaringType == typeof(ActiveHealthController)
               && effect.Name == nameof(ActiveHealthController.DoMedEffect)
               && effect.GetParameters().Select(parameter => parameter.ParameterType)
                   .SequenceEqual(new[] { typeof(Item), typeof(EBodyPart), typeof(float?) }),
            "effect hook resolves the exact SPT 4.1 DoMedEffect signature");
        Assert(destroy.DeclaringType == typeof(Player.MedsController)
               && destroy.Name == nameof(Player.MedsController.Destroy),
            "cleanup hook resolves the exact medical-controller destroy method");
    }

    private static void CheckWaitingEffectAndAnimation()
    {
        var context = Context(arena: true, multiplier: 2f, animationSpeed: 1.25f);
        var effect = Effect(context.Item, EEffectState.Added,
            delay: 0.8f, work: 4f, stateDuration: 0.8f, elapsed: 0.2f, whole: 0.2f, buildUp: 1f);

        InvokeEffect(context.Health, context.Item, effect);
        AssertNear(effect.DelayTime, 0.4f, "start delay scales with the Arena factor");
        AssertNear(effect.WorkStateTime, 2f, "medical work time scales with the Arena factor");
        AssertNear(effect._stateTime, 0.4f, "the live waiting-state clock scales with its delay");
        AssertNear(effect.StateCurrentTime, 0.1f, "waiting-state progress percentage is preserved");
        AssertNear(effect.WholeTime, 0.1f, "whole application progress scales with the timeline");
        AssertNear(effect.BuildUpTime, 0.5f, "effect buildup keeps its proportion");
        AssertNear(context.Animator.Animator.speed, 2.5f,
            "hands animation multiplies the exact pre-Arena speed");
        Assert(Plugin.ModLogger.Infos.Count(message => message.Contains("Arena medical speed active")) == 1,
            "first successful use emits one bounded runtime marker");

        var second = Effect(context.Item, EEffectState.Added,
            delay: 1f, work: 6f, stateDuration: 1f, elapsed: 0f, whole: 0f, buildUp: 1f);
        InvokeEffect(context.Health, context.Item, second);
        AssertNear(context.Animator.Animator.speed, 2.5f,
            "multi-limb use derives from the captured baseline instead of multiplying repeatedly");
        Assert(Plugin.ModLogger.Infos.Count(message => message.Contains("Arena medical speed active")) == 1,
            "subsequent limbs do not spam the log");

        InvokeDestroy(context.Controller);
        AssertNear(context.Animator.Animator.speed, 1.25f,
            "controller destruction restores the exact animation speed it inherited");
    }

    private static void CheckWorkingEffectProgress()
    {
        var context = Context(arena: true, multiplier: 2f);
        var effect = Effect(context.Item, EEffectState.Started,
            delay: 0f, work: 6f, stateDuration: 6f, elapsed: 1.5f, whole: 1.5f, buildUp: 1f);
        InvokeEffect(context.Health, context.Item, effect);
        AssertNear(effect.WorkStateTime, 3f, "already-started work duration scales");
        AssertNear(effect._stateTime, 3f, "already-started live state duration scales");
        AssertNear(effect.StateCurrentTime, 0.75f,
            "already-started effect remains one quarter complete");
        AssertNear(effect.WholeTime, 0.75f,
            "already-started whole timeline remains one quarter complete");
        AssertNear(effect.BuildUpTime, 0.5f, "started buildup scales with work time");
    }

    private static void CheckNormalRaidAndActorGates()
    {
        var ordinary = Context(arena: false, multiplier: 2f);
        AssertUntouched(ordinary, "ordinary raid");

        var stopped = Context(arena: true, multiplier: 2f);
        ArenaMedicalPatches.Shutdown();
        AssertUntouched(stopped, "disabled plugin lifecycle");

        var bot = Context(arena: true, multiplier: 2f);
        bot.Player.IsAI = true;
        AssertUntouched(bot, "AI main-player stand-in");

        var dead = Context(arena: true, multiplier: 2f);
        dead.Player.HealthController.IsAlive = false;
        AssertUntouched(dead, "dead local player");

        var foreign = Context(arena: true, multiplier: 2f);
        var otherHealth = new ActiveHealthController();
        var effect = StandardEffect(foreign.Item);
        InvokeEffect(otherHealth, foreign.Item, effect);
        AssertNear(effect.WorkStateTime, 4f, "another actor's health controller is untouched");
        AssertNear(foreign.Animator.Animator.speed, 1f, "another actor cannot accelerate local hands");
    }

    private static void CheckItemAndValueGates()
    {
        var mismatch = Context(arena: true, multiplier: 2f);
        var other = new Meds("other");
        var mismatchEffect = StandardEffect(other);
        InvokeEffect(mismatch.Health, other, mismatchEffect);
        AssertNear(mismatchEffect.WorkStateTime, 4f, "an item not held by the PMC is untouched");

        var foodContext = Context(arena: true, multiplier: 2f);
        var food = new FoodDrink();
        foodContext.Player.HandsController = new Player.MedsController(food, foodContext.Animator);
        var foodEffect = StandardEffect(food);
        InvokeEffect(foodContext.Health, food, foodEffect);
        AssertNear(foodEffect.WorkStateTime, 4f, "food and drink remain vanilla");

        var disabled = Context(arena: true, multiplier: 1f);
        AssertUntouched(disabled, "1x disabled setting");

        var invalid = Context(arena: true, multiplier: float.NaN);
        AssertUntouched(invalid, "invalid setting");

        var capped = Context(arena: true, multiplier: 10f);
        var cappedEffect = StandardEffect(capped.Item);
        InvokeEffect(capped.Health, capped.Item, cappedEffect);
        AssertNear(cappedEffect.WorkStateTime, 1f, "out-of-range setting is safely capped at 4x");
        AssertNear(capped.Animator.Animator.speed, 4f, "animation uses the same capped factor");

        var badTiming = Context(arena: true, multiplier: 2f);
        var badEffect = Effect(badTiming.Item, EEffectState.Added,
            1f, float.PositiveInfinity, 1f, 0f, 0f, 1f);
        InvokeEffect(badTiming.Health, badTiming.Item, badEffect);
        Assert(float.IsPositiveInfinity(badEffect.WorkStateTime)
               && Math.Abs(badTiming.Animator.Animator.speed - 1f) < 0.0001f,
            "invalid native timing leaves both effect and animation untouched");

        var residue = Context(arena: true, multiplier: 2f);
        var residueEffect = Effect(residue.Item, EEffectState.Residued,
            1f, 4f, 0f, 0f, 5f, 1f);
        InvokeEffect(residue.Health, residue.Item, residueEffect);
        AssertNear(residueEffect.WorkStateTime, 4f, "completed medical effects are never rewritten");
    }

    private static void CheckAnimatorFailureIsTransactional()
    {
        var context = Context(arena: true, multiplier: 2f);
        context.Animator.ThrowOnSet = true;
        var effect = StandardEffect(context.Item);
        InvokeEffect(context.Health, context.Item, effect);
        InvokeEffect(context.Health, context.Item, StandardEffect(context.Item));
        AssertNear(effect.DelayTime, 1f, "failed animator leaves delay untouched");
        AssertNear(effect.WorkStateTime, 4f, "failed animator leaves work time untouched");
        Assert(Plugin.ModLogger.Warnings.Count(message => message.Contains("failed safely")) == 1,
            "repeated native animator failures log only once per raid");

        int failedSetCalls = context.Animator.SetCalls;
        context.Animator.ThrowOnSet = false;
        ArenaMedicalPatches.Shutdown();
        Assert(context.Animator.SetCalls == failedSetCalls,
            "failed first animator write never publishes stale cleanup ownership");
    }

    private static void CheckLifecycleRestoration()
    {
        var ended = Context(arena: true, multiplier: 2f, animationSpeed: 0.8f);
        InvokeEffect(ended.Health, ended.Item, StandardEffect(ended.Item));
        RaidLifecycle.RaiseEnded();
        AssertNear(ended.Animator.Animator.speed, 0.8f,
            "raid end restores the captured animation speed");

        var started = Context(arena: true, multiplier: 2f, animationSpeed: 1.1f);
        InvokeEffect(started.Health, started.Item, StandardEffect(started.Item));
        RaidLifecycle.RaiseStarted(Singleton<GameWorld>.Instance);
        AssertNear(started.Animator.Animator.speed, 1.1f,
            "new raid start repairs a missed preceding end callback");

        var shutdown = Context(arena: true, multiplier: 2f, animationSpeed: 0.9f);
        InvokeEffect(shutdown.Health, shutdown.Item, StandardEffect(shutdown.Item));
        ArenaMedicalPatches.Shutdown();
        AssertNear(shutdown.Animator.Animator.speed, 0.9f,
            "plugin shutdown restores the captured animation speed");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string patch = File.ReadAllText(Path.Combine(root,
            "ArenaMode.Client", "Combat", "ArenaMedicalPatches.cs"));
        string settings = File.ReadAllText(Path.Combine(root,
            "ArenaMode.Client", "Config", "Settings.cs"));
        string plugin = File.ReadAllText(Path.Combine(root,
            "ArenaMode.Client", "Plugin.cs"));

        RequireAll(patch,
            "if (!_enabled)",
            "if (!ArenaSession.IsActive",
            "item is not Meds",
            "ReferenceEquals(player.ActiveHealthController, health)",
            "ReferenceEquals(medsController.GetItem(), item)",
            "effect._stateTime = scaled.StateDuration",
            "effect.WorkStateTime = scaled.Work",
            "RestoreAnimator()",
            "RaidLifecycle.OnRaidStarted += OnRaidStarted",
            "RaidLifecycle.OnRaidEnded += OnRaidEnded");
        Assert(!patch.Contains("GlobalConfiguration", StringComparison.Ordinal)
               && !patch.Contains("MedsTemplate", StringComparison.Ordinal)
               && !patch.Contains("SurgerySpeed.Value =", StringComparison.Ordinal),
            "implementation never mutates shared templates, global health settings or skills");
        RequireAll(settings,
            "public static ConfigEntry<float> MedicalUseSpeed",
            "\"MedicalUseSpeed\"",
            "new AcceptableValueRange<float>(1f, 4f)");
        RequireAll(plugin,
            "Combat.ArenaMedicalPatches.Enable()",
            "Combat.ArenaMedicalPatches.Shutdown()");
    }

    private static void Reset()
    {
        ArenaMedicalPatches.Shutdown();
        ModulePatch.EnabledTargets.Clear();
        Plugin.ModLogger.Clear();
        ArenaSession.IsActive = false;
        Settings.MedicalUseSpeed.Value = 2f;
        Singleton<GameWorld>.Instance = null;
    }

    private static TestContext Context(bool arena, float multiplier, float animationSpeed = 1f)
    {
        ArenaMedicalPatches.Enable();
        ArenaSession.IsActive = arena;
        Settings.MedicalUseSpeed.Value = multiplier;
        var item = new Meds();
        var animator = new FirearmsAnimator { Animator = new FakeAnimator { speed = animationSpeed } };
        var controller = new Player.MedsController(item, animator);
        var health = new ActiveHealthController();
        var player = new Player
        {
            ActiveHealthController = health,
            HandsController = controller,
        };
        Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = player };
        return new TestContext(item, animator, controller, health, player);
    }

    private static void AssertUntouched(TestContext context, string reason)
    {
        var effect = StandardEffect(context.Item);
        float animation = context.Animator.Animator.speed;
        InvokeEffect(context.Health, context.Item, effect);
        AssertNear(effect.DelayTime, 1f, reason + " keeps delay");
        AssertNear(effect.WorkStateTime, 4f, reason + " keeps work time");
        AssertNear(effect._stateTime, 1f, reason + " keeps live state clock");
        AssertNear(context.Animator.Animator.speed, animation, reason + " keeps animation speed");
    }

    private static ActiveHealthController.MedEffect StandardEffect(Item item)
        => Effect(item, EEffectState.Added, 1f, 4f, 1f, 0f, 0f, 1f);

    private static ActiveHealthController.MedEffect Effect(
        Item item,
        EEffectState state,
        float delay,
        float work,
        float stateDuration,
        float elapsed,
        float whole,
        float buildUp)
        => new()
        {
            MedItem = item,
            State = state,
            DelayTime = delay,
            WorkStateTime = work,
            _stateTime = stateDuration,
            StateCurrentTime = elapsed,
            WholeTime = whole,
            BuildUpTime = buildUp,
        };

    private static void InvokeEffect(
        ActiveHealthController health,
        Item item,
        IHealthEffect effect)
    {
        Type patch = typeof(ArenaMedicalPatches).GetNestedType(
            "ArenaMedicalEffectPatch", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ArenaMedicalEffectPatch missing");
        MethodInfo postfix = patch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ArenaMedicalEffectPatch.Postfix missing");
        postfix.Invoke(null, new object[] { health, item, effect });
    }

    private static void InvokeDestroy(Player.MedsController controller)
    {
        Type patch = typeof(ArenaMedicalPatches).GetNestedType(
            "ArenaMedicalMedsControllerDestroyPatch", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ArenaMedicalMedsControllerDestroyPatch missing");
        MethodInfo prefix = patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ArenaMedicalMedsControllerDestroyPatch.Prefix missing");
        prefix.Invoke(null, new object[] { controller });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("ArenaMode.sln not found above the harness output");
    }

    private static void RequireAll(string source, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                "production source retains required contract: " + fragment);
    }

    private static void AssertNear(float actual, float expected, string message)
        => Assert(Math.Abs(actual - expected) < 0.0001f,
            $"{message} (expected {expected}, got {actual})");

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record TestContext(
        Meds Item,
        FirearmsAnimator Animator,
        Player.MedsController Controller,
        ActiveHealthController Health,
        Player Player);
}
