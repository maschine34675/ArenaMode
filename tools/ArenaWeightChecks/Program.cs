using ArenaMode.Config;
using ArenaMode.Movement;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckCalculations();
            CheckPatchRegistration();
            CheckPatchGatesAndFields();
            CheckRuntimeLifecycle();
            CheckRuntimeRetries();
            CheckSlideControllerLifecycle();
            CheckDetachedBodyDoesNotTouchOrdinaryRaid();
            CheckSourceContract();
            Console.WriteLine($"ARENA_WEIGHT_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ARENA_WEIGHT_CHECKS_FAILED after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private static void CheckCalculations()
    {
        Vector2 doubled = ArenaWeightTuning.ScaleLimits(new Vector2(20f, 40f), 2f);
        AssertVector(doubled, 40f, 80f, "2x doubles both ends of a weight range");

        Vector2 identity = ArenaWeightTuning.ScaleLimits(new Vector2(30f, 60f), 1f);
        AssertVector(identity, 30f, 60f, "1x preserves the vanilla range");

        Vector3 inertia = ArenaWeightTuning.ScaleInertiaLimits(new Vector3(15f, 30f, 0.4f), 2f);
        AssertVector(inertia, 30f, 60f, 0.4f,
            "inertia weight bounds scale while the curve-shape component stays intact");

        AssertNear(ArenaWeightTuning.BoundMultiplier(-2f), 1f,
            "negative multiplier fails safe to vanilla");
        AssertNear(ArenaWeightTuning.BoundMultiplier(9f), 4f,
            "oversized multiplier is bounded");
        AssertNear(ArenaWeightTuning.BoundMultiplier(float.NaN), 1f,
            "NaN multiplier fails safe to vanilla");
        AssertNear(ArenaWeightTuning.BoundMultiplier(float.PositiveInfinity), 1f,
            "infinite multiplier fails safe to vanilla");
    }

    private static void CheckPatchRegistration()
    {
        Reset();
        ArenaWeightPatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 1,
            "exactly one carry-weight hook is registered");
        MethodBase target = ModulePatch.EnabledTargets[0];
        Assert(target.DeclaringType == typeof(PhysicalBase)
               && target.Name == nameof(PhysicalBase.UpdateWeightLimits)
               && target.GetParameters().Length == 0,
            "hook resolves PhysicalBase.UpdateWeightLimits exactly");
    }

    private static void CheckPatchGatesAndFields()
    {
        Physical local = Context(arena: true, movement: true, multiplier: 2f);
        SetVanillaLimits(local);
        InvokePostfix(local);
        AssertVector(local.WalkOverweightLimits, 60f, 120f,
            "local Arena walk-overweight range scales");
        AssertVector(local.BaseOverweightLimits, 70f, 120f,
            "local Arena base-overweight range scales");
        AssertVector(local.SprintOverweightLimits, 40f, 80f,
            "local Arena sprint-overweight range scales");
        AssertVector(local.WalkSpeedOverweightLimits, 90f, 160f,
            "local Arena walk-speed range scales");
        AssertVector(local.BaseInertiaLimits, 30f, 60f, 0.4f,
            "local Arena weight-based inertia range scales");

        Physical ordinary = Context(arena: false, movement: true, multiplier: 2f);
        SetVanillaLimits(ordinary);
        InvokePostfix(ordinary);
        AssertVanilla(ordinary, "ordinary raid is unchanged");

        Physical disabled = Context(arena: true, movement: false, multiplier: 2f);
        SetVanillaLimits(disabled);
        InvokePostfix(disabled);
        AssertVanilla(disabled, "movement master switch preserves vanilla limits");

        Physical factorOne = Context(arena: true, movement: true, multiplier: 1f);
        SetVanillaLimits(factorOne);
        InvokePostfix(factorOne);
        AssertVanilla(factorOne, "1x setting preserves vanilla limits");

        Physical foreign = Context(arena: true, movement: true, multiplier: 2f);
        SetVanillaLimits(foreign);
        var other = new Physical();
        SetVanillaLimits(other);
        InvokePostfix(other);
        AssertVanilla(other, "teammate and opponent Physical instances are unchanged");

        Physical ai = Context(arena: true, movement: true, multiplier: 2f, ai: true);
        SetVanillaLimits(ai);
        InvokePostfix(ai);
        AssertVanilla(ai, "AI main-player stand-in is unchanged");

        Physical externallyDisabled = Context(arena: true, movement: true, multiplier: 2f);
        externallyDisabled.EncumberDisabled = true;
        SetVanillaLimits(externallyDisabled);
        InvokePostfix(externallyDisabled);
        AssertVanilla(externallyDisabled,
            "another mod's disabled-encumbrance sentinel is left unchanged");

        Singleton<GameWorld>.Instance = null;
        Physical missingWorld = new();
        SetVanillaLimits(missingWorld);
        InvokePostfix(missingWorld);
        AssertVanilla(missingWorld, "missing GameWorld fails open");
    }

    private static void CheckRuntimeLifecycle()
    {
        Reset();
        ArenaSession.IsActive = true;
        Settings.MovementEnabled.Value = true;
        Settings.CarryWeightLimitMultiplier.Value = 2f;
        Settings.NoInertia.Value = true;
        Physical first = NewWorldPhysical();

        MovementRuntime.Tick();
        Assert(first.WeightLimitsUpdateCount == 1,
            "first active Arena tick rebuilds weight limits");
        MovementRuntime.Tick();
        Assert(first.WeightLimitsUpdateCount == 1,
            "unchanged settings do not rebuild limits every frame");

        Settings.CarryWeightLimitMultiplier.Value = 1.5f;
        MovementRuntime.Tick();
        Assert(first.WeightLimitsUpdateCount == 2,
            "live multiplier edit rebuilds limits once");

        Settings.CarryWeightLimitMultiplier.Value = float.NaN;
        MovementRuntime.Tick();
        Assert(first.WeightLimitsUpdateCount == 3,
            "invalid live multiplier fails safe by rebuilding vanilla limits");

        var replacement = new Physical();
        Singleton<GameWorld>.Instance.MainPlayer.Physical = replacement;
        MovementRuntime.Tick();
        Assert(replacement.WeightLimitsUpdateCount == 1,
            "respawned Physical receives the Arena limits");

        Settings.MovementEnabled.Value = false;
        MovementRuntime.Tick();
        Assert(replacement.WeightLimitsUpdateCount == 2,
            "disabling Arena movement immediately rebuilds vanilla limits");
        MovementRuntime.Tick();
        Assert(replacement.WeightLimitsUpdateCount == 2,
            "disabled movement does not repeat the restore each frame");

        Settings.MovementEnabled.Value = true;
        MovementRuntime.Tick();
        Assert(replacement.WeightLimitsUpdateCount == 3,
            "re-enabling Arena movement reapplies limits once");
        ArenaSession.IsActive = false;
        MovementRuntime.Tick();
        Assert(replacement.WeightLimitsUpdateCount == 4,
            "Arena end immediately rebuilds vanilla limits");

        Physical ordinary = NewWorldPhysical();
        MovementRuntime.Tick();
        Assert(ordinary.WeightLimitsUpdateCount == 0,
            "ordinary raid never applies Arena weight limits");
    }

    /// <summary>
    /// One slide controller per LIVE body, never one per death. Arena keeps
    /// corpses, so the component on a replaced body is not destroyed; it
    /// disables itself, and a long raid used to accumulate one running Update
    /// per death (measured: 26 deaths, 26 Updates per frame). The runtime must
    /// therefore attach to a fresh body, leave a healthy one alone, and adopt a
    /// disabled component rather than skip it because it is not null.
    /// </summary>
    private static void CheckSlideControllerLifecycle()
    {
        Reset();
        ArenaSession.IsActive = true;
        Settings.MovementEnabled.Value = true;
        Settings.SlideEnabled.Value = true;

        NewWorldPhysical();
        Player body = Singleton<GameWorld>.Instance.MainPlayer;
        MovementRuntime.Tick();
        var slide = body.GetComponent<ArenaSlideController>();
        Assert(slide != null && slide.enabled && slide.Inits == 1,
            "a fresh live body receives one initialised slide controller");

        MovementRuntime.Tick();
        MovementRuntime.Tick();
        Assert(ReferenceEquals(body.GetComponent<ArenaSlideController>(), slide) && slide.Inits == 1,
            "a healthy controller is neither replaced nor re-initialised every frame");

        // What the production Update does once its body is dead. The runtime
        // must not treat the disabled leftover as "already handled".
        slide.enabled = false;
        MovementRuntime.Tick();
        Assert(ReferenceEquals(body.GetComponent<ArenaSlideController>(), slide)
               && slide.enabled && slide.Inits == 2,
            "a body that is live again adopts and re-initialises its own controller");

        // A respawn is a new body; the corpse keeps its own component.
        NewWorldPhysical();
        Player respawned = Singleton<GameWorld>.Instance.MainPlayer;
        MovementRuntime.Tick();
        var second = respawned.GetComponent<ArenaSlideController>();
        Assert(second != null && !ReferenceEquals(second, slide) && second.Inits == 1,
            "a respawned body gets its own controller");
        Assert(ReferenceEquals(body.GetComponent<ArenaSlideController>(), slide) && slide.Inits == 2,
            "the previous body's controller is left untouched, not re-initialised");

        Settings.SlideEnabled.Value = false;
        NewWorldPhysical();
        MovementRuntime.Tick();
        Assert(Singleton<GameWorld>.Instance.MainPlayer.GetComponent<ArenaSlideController>() == null,
            "sliding switched off attaches no controller at all");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string patch = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Movement", "ArenaWeightPatches.cs"));
        string tuning = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Movement", "ArenaWeightTuning.cs"));
        string runtime = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Movement", "MovementRuntime.cs"));
        string settings = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Config", "Settings.cs"));
        string plugin = File.ReadAllText(Path.Combine(root, "ArenaMode.Client", "Plugin.cs"));

        RequireAll(patch,
            "typeof(PhysicalBase)",
            "nameof(PhysicalBase.UpdateWeightLimits)",
            "[PatchPostfix]",
            "if (!ArenaSession.IsActive",
            "ReferenceEquals(mainPlayer.Physical, physical)",
            "physical.EncumberDisabled",
            "__instance.WalkOverweightLimits",
            "__instance.BaseOverweightLimits",
            "__instance.SprintOverweightLimits",
            "__instance.WalkSpeedOverweightLimits",
            "__instance.BaseInertiaLimits");
        Assert(!patch.Contains("Singleton<GlobalConfiguration>", StringComparison.Ordinal)
               && !patch.Contains("GlobalConfiguration.Instance", StringComparison.Ordinal),
            "patch never mutates shared GlobalConfiguration");
        RequireAll(tuning,
            "limits.x * bounded",
            "limits.y * bounded",
            "new Vector3(limits.x * bounded, limits.y * bounded, limits.z)");
        RequireAll(runtime,
            "ReferenceEquals(player.Physical, _lastWeightPhysical)",
            "Settings.CarryWeightLimitMultiplier.Value",
            "ArenaWeightTuning.BoundMultiplier(",
            "currentPhysical.OnWeightLimitsUpdated()",
            "player.Physical.OnWeightLimitsUpdated()");
        int restoreCall = runtime.IndexOf(
            "currentPhysical.OnWeightLimitsUpdated()", StringComparison.Ordinal);
        int restoreCommit = runtime.IndexOf(
            "_weightValuesApplied = false", restoreCall, StringComparison.Ordinal);
        Assert(restoreCall >= 0 && restoreCommit > restoreCall,
            "restore state commits only after EFT rebuilt vanilla limits");
        int applyCall = runtime.IndexOf(
            "player.Physical.OnWeightLimitsUpdated()", restoreCall + 1, StringComparison.Ordinal);
        int applyCommit = runtime.IndexOf(
            "_weightValuesApplied = true", applyCall, StringComparison.Ordinal);
        Assert(applyCall >= 0 && applyCommit > applyCall,
            "apply state commits only after EFT rebuilt Arena limits");
        RequireAll(settings,
            "public static ConfigEntry<float> CarryWeightLimitMultiplier",
            "\"CarryWeightLimitMultiplier\"",
            "new AcceptableValueRange<float>(1f, 4f)");
        RequireAll(plugin, "Movement.ArenaWeightPatches.Enable()");
    }

    private static void CheckRuntimeRetries()
    {
        Reset();
        ArenaSession.IsActive = true;
        Settings.MovementEnabled.Value = true;
        Settings.CarryWeightLimitMultiplier.Value = 2f;
        Physical physical = NewWorldPhysical();
        physical.ThrowWeightLimitsUpdatesRemaining = 1;

        MovementRuntime.Tick();
        Assert(physical.WeightLimitsUpdateAttemptCount == 1
               && physical.WeightLimitsUpdateCount == 0,
            "failed Arena limit apply is observed without being committed");
        MovementRuntime.Tick();
        Assert(physical.WeightLimitsUpdateAttemptCount == 2
               && physical.WeightLimitsUpdateCount == 1,
            "failed Arena limit apply is retried on the next tick");

        physical.ThrowWeightLimitsUpdatesRemaining = 1;
        Settings.MovementEnabled.Value = false;
        MovementRuntime.Tick();
        Assert(physical.WeightLimitsUpdateAttemptCount == 3
               && physical.WeightLimitsUpdateCount == 1,
            "failed vanilla restore remains pending");
        MovementRuntime.Tick();
        Assert(physical.WeightLimitsUpdateAttemptCount == 4
               && physical.WeightLimitsUpdateCount == 2,
            "failed vanilla restore is retried on the next tick");
    }

    private static void CheckDetachedBodyDoesNotTouchOrdinaryRaid()
    {
        Reset();
        ArenaSession.IsActive = true;
        Settings.MovementEnabled.Value = true;
        Settings.CarryWeightLimitMultiplier.Value = 2f;
        Physical arenaPhysical = NewWorldPhysical();
        MovementRuntime.Tick();
        Assert(arenaPhysical.WeightLimitsUpdateCount == 1,
            "detached-body scenario begins with one Arena apply");

        // Simulate GameEnd after the Arena body has already disappeared. Only
        // that exact body was modified; a new ordinary body is not a restore
        // target and must not receive an extra EFT weight event.
        ArenaSession.IsActive = false;
        Singleton<GameWorld>.Instance = null;
        MovementRuntime.Tick();

        Physical ordinaryPhysical = NewWorldPhysical();
        MovementRuntime.Tick();
        Assert(ordinaryPhysical.WeightLimitsUpdateAttemptCount == 0,
            "next ordinary-raid Physical receives no extra weight update");

        Reset();
        ArenaSession.IsActive = true;
        Settings.MovementEnabled.Value = true;
        Settings.CarryWeightLimitMultiplier.Value = 2f;
        arenaPhysical = NewWorldPhysical();
        MovementRuntime.Tick();
        var replacement = new Physical();
        Singleton<GameWorld>.Instance.MainPlayer.Physical = replacement;
        Settings.MovementEnabled.Value = false;
        MovementRuntime.Tick();
        Assert(replacement.WeightLimitsUpdateAttemptCount == 0,
            "unmodified replacement body is not used as a restore target");
    }

    private static Physical Context(
        bool arena,
        bool movement,
        float multiplier,
        bool ai = false)
    {
        Reset();
        ArenaSession.IsActive = arena;
        Settings.MovementEnabled.Value = movement;
        Settings.CarryWeightLimitMultiplier.Value = multiplier;
        Physical physical = NewWorldPhysical();
        Singleton<GameWorld>.Instance.MainPlayer.IsAI = ai;
        return physical;
    }

    private static Physical NewWorldPhysical()
    {
        var physical = new Physical();
        Singleton<GameWorld>.Instance = new GameWorld
        {
            MainPlayer = new Player { Physical = physical },
        };
        return physical;
    }

    private static void SetVanillaLimits(PhysicalBase physical)
    {
        physical.WalkOverweightLimits = new Vector2(30f, 60f);
        physical.BaseOverweightLimits = new Vector2(35f, 60f);
        physical.SprintOverweightLimits = new Vector2(20f, 40f);
        physical.WalkSpeedOverweightLimits = new Vector2(45f, 80f);
        physical.BaseInertiaLimits = new Vector3(15f, 30f, 0.4f);
    }

    private static void AssertVanilla(PhysicalBase physical, string message)
    {
        AssertVector(physical.WalkOverweightLimits, 30f, 60f, message + " (walk)");
        AssertVector(physical.BaseOverweightLimits, 35f, 60f, message + " (base)");
        AssertVector(physical.SprintOverweightLimits, 20f, 40f, message + " (sprint)");
        AssertVector(physical.WalkSpeedOverweightLimits, 45f, 80f, message + " (speed)");
        AssertVector(physical.BaseInertiaLimits, 15f, 30f, 0.4f, message + " (inertia)");
    }

    private static void InvokePostfix(PhysicalBase physical)
    {
        Type nested = typeof(ArenaWeightPatches).GetNestedType(
            "WeightLimitsPatch", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WeightLimitsPatch missing");
        MethodInfo postfix = nested.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("WeightLimitsPatch.Postfix missing");
        postfix.Invoke(null, new object[] { physical });
    }

    private static void Reset()
    {
        ModulePatch.EnabledTargets.Clear();
        ArenaSession.IsActive = false;
        ArenaSession.ArmedForNextRaid = false;
        Settings.MovementEnabled.Value = false;
        Settings.CarryWeightLimitMultiplier.Value = 1f;
        Settings.NoInertia.Value = false;
        Settings.SlideEnabled.Value = false;
        Settings.InfiniteStamina.Value = false;
        Singleton<GameWorld>.Instance = null;

        Type runtime = typeof(MovementRuntime);
        SetStatic(runtime, "_lastNoInertia", false);
        SetStatic(runtime, "_lastWeightLimitMultiplier", 0f);
        SetStatic(runtime, "_lastWeightPhysical", null);
        SetStatic(runtime, "_weightValuesApplied", false);
    }

    private static void SetStatic(Type type, string name, object value)
    {
        FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(type.FullName, name);
        field.SetValue(null, value);
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
        throw new DirectoryNotFoundException("ArenaMode.sln not found above harness output");
    }

    private static void RequireAll(string source, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                "production source retains required contract: " + fragment);
    }

    private static void AssertVector(Vector2 actual, float x, float y, string message)
    {
        AssertNear(actual.x, x, message + " x");
        AssertNear(actual.y, y, message + " y");
    }

    private static void AssertVector(Vector3 actual, float x, float y, float z, string message)
    {
        AssertNear(actual.x, x, message + " x");
        AssertNear(actual.y, y, message + " y");
        AssertNear(actual.z, z, message + " z");
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
}
