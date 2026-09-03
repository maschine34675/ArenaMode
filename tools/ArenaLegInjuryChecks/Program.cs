using ArenaMode.Config;
using ArenaMode.Movement;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using System.Reflection;

internal static class Program
{
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckPenaltyCalculation();
            CheckPatchRegistration();
            CheckLocalArenaApplication();
            CheckIsolationGates();
            CheckSourceContract();
            Console.WriteLine($"ARENA_LEG_INJURY_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"ARENA_LEG_INJURY_CHECKS_FAILED after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private static void CheckPenaltyCalculation()
    {
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(0.3f, 50), 0.65f,
            "50% reduction halves the vanilla one-leg penalty");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(0.2f, 50), 0.6f,
            "50% reduction halves the vanilla two-leg penalty");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(0.3f, 0), 0.3f,
            "0% keeps the vanilla limit");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(0.2f, 100), 1f,
            "100% removes only the walking-speed penalty");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(1f, 50), 1f,
            "a neutral limit remains neutral");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(0.3f, -50), 0.3f,
            "negative configuration is bounded to zero");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(0.3f, 150), 1f,
            "configuration above 100 is bounded");
        Assert(float.IsNaN(ArenaLegInjuryTuning.ReducePenalty(float.NaN, 50)),
            "NaN native input is preserved for fail-open behavior");
        Assert(float.IsPositiveInfinity(
                ArenaLegInjuryTuning.ReducePenalty(float.PositiveInfinity, 50)),
            "infinite native input is preserved for fail-open behavior");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(-0.1f, 50), -0.1f,
            "negative native input is not rewritten");
        AssertNear(ArenaLegInjuryTuning.ReducePenalty(1.1f, 50), 1.1f,
            "native input above the neutral limit is not rewritten");
    }

    private static void CheckPatchRegistration()
    {
        Reset();
        ArenaLegInjuryPatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 1,
            "exactly one leg-injury hook is registered");
        MethodBase target = ModulePatch.EnabledTargets[0];
        Assert(target.DeclaringType == typeof(MovementContext)
               && target.Name == nameof(MovementContext.AddStateSpeedLimit)
               && target.GetParameters().Select(parameter => parameter.ParameterType)
                   .SequenceEqual(new[] { typeof(float), typeof(Player.ESpeedLimit) }),
            "hook resolves the exact non-duration SPT 4.1 speed-limit overload");
    }

    private static void CheckLocalArenaApplication()
    {
        TestContext context = Context(arena: true, reductionPercent: 50);
        AssertNear(Invoke(context.Movement, 0.3f, Player.ESpeedLimit.HealthCondition), 0.65f,
            "local Arena one-leg limit is softened");
        AssertNear(Invoke(context.Movement, 0.2f, Player.ESpeedLimit.HealthCondition), 0.6f,
            "local Arena two-leg limit is softened");

        Settings.LegInjurySlowdownReductionPercent.Value = 100;
        AssertNear(Invoke(context.Movement, 0.2f, Player.ESpeedLimit.HealthCondition), 1f,
            "live 100% setting removes the walking limit");
    }

    private static void CheckIsolationGates()
    {
        TestContext ordinary = Context(arena: false, reductionPercent: 50);
        AssertNear(Invoke(ordinary.Movement, 0.3f, Player.ESpeedLimit.HealthCondition), 0.3f,
            "ordinary raid is unchanged");

        TestContext disabled = Context(arena: true, reductionPercent: 0);
        AssertNear(Invoke(disabled.Movement, 0.3f, Player.ESpeedLimit.HealthCondition), 0.3f,
            "0% setting is vanilla");

        TestContext aiming = Context(arena: true, reductionPercent: 50);
        AssertNear(Invoke(aiming.Movement, 0.3f, Player.ESpeedLimit.Aiming), 0.3f,
            "aiming slowdown is unchanged");

        TestContext foreign = Context(arena: true, reductionPercent: 50);
        var foreignMovement = new MovementContext();
        AssertNear(Invoke(foreignMovement, 0.3f, Player.ESpeedLimit.HealthCondition), 0.3f,
            "teammate and opponent movement contexts are unchanged");

        TestContext ai = Context(arena: true, reductionPercent: 50);
        ai.Player.IsAI = true;
        AssertNear(Invoke(ai.Movement, 0.3f, Player.ESpeedLimit.HealthCondition), 0.3f,
            "an AI main-player stand-in is unchanged");

        TestContext missingWorld = Context(arena: true, reductionPercent: 50);
        Singleton<GameWorld>.Instance = null;
        AssertNear(Invoke(missingWorld.Movement, 0.3f, Player.ESpeedLimit.HealthCondition), 0.3f,
            "missing GameWorld fails open");

        AssertNear(Invoke(null, 0.3f, Player.ESpeedLimit.HealthCondition), 0.3f,
            "missing movement context fails open");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string patch = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Movement", "ArenaLegInjuryPatches.cs"));
        string tuning = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Movement", "ArenaLegInjuryTuning.cs"));
        string settings = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Config", "Settings.cs"));
        string plugin = File.ReadAllText(Path.Combine(root, "ArenaMode.Client", "Plugin.cs"));

        RequireAll(patch,
            "typeof(MovementContext)",
            "nameof(MovementContext.AddStateSpeedLimit)",
            "Player.ESpeedLimit.HealthCondition",
            "if (!ArenaSession.IsActive",
            "ReferenceEquals(mainPlayer.MovementContext, context)",
            "ArenaLegInjuryTuning.ReducePenalty(");
        Assert(!patch.Contains("DoFracture", StringComparison.Ordinal)
               && !patch.Contains("EnableSprint", StringComparison.Ordinal)
               && !patch.Contains("StartInflictSelfDamageCoroutine", StringComparison.Ordinal),
            "patch cannot suppress injuries, unlock sprint or suppress self-damage");
        RequireAll(tuning,
            "vanillaSpeedLimit + (1f - vanillaSpeedLimit) * reduction",
            "MinimumReductionPercent = 0",
            "MaximumReductionPercent = 100");
        RequireAll(settings,
            "public static ConfigEntry<int> LegInjurySlowdownReductionPercent",
            "\"LegInjurySlowdownReductionPercent\"",
            "new AcceptableValueRange<int>(0, 100)");
        Assert(!settings.Contains("NoFractures", StringComparison.Ordinal),
            "obsolete NoFractures setting is no longer bound");
        RequireAll(plugin, "Movement.ArenaLegInjuryPatches.Enable()");
        Assert(!plugin.Contains("ArenaFracturePatches", StringComparison.Ordinal),
            "obsolete fracture blocker is no longer registered");
        Assert(!File.Exists(Path.Combine(
                root, "ArenaMode.Client", "Combat", "ArenaFracturePatches.cs")),
            "obsolete fracture-blocking source is removed");
    }

    private static TestContext Context(bool arena, int reductionPercent)
    {
        Reset();
        ArenaLegInjuryPatches.Enable();
        ArenaSession.IsActive = arena;
        Settings.LegInjurySlowdownReductionPercent.Value = reductionPercent;
        var movement = new MovementContext();
        var player = new Player { MovementContext = movement };
        Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = player };
        return new TestContext(player, movement);
    }

    private static float Invoke(
        MovementContext context,
        float speedLimit,
        Player.ESpeedLimit cause)
    {
        Type patch = typeof(ArenaLegInjuryPatches).GetNestedType(
            "HealthConditionSpeedPatch", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("HealthConditionSpeedPatch missing");
        MethodInfo prefix = patch.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("HealthConditionSpeedPatch.Prefix missing");
        object[] arguments = { context, speedLimit, cause };
        prefix.Invoke(null, arguments);
        return (float)arguments[1];
    }

    private static void Reset()
    {
        ModulePatch.EnabledTargets.Clear();
        ArenaSession.IsActive = false;
        Settings.LegInjurySlowdownReductionPercent.Value = 50;
        Singleton<GameWorld>.Instance = null;
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

    private sealed record TestContext(Player Player, MovementContext Movement);
}
