using ArenaMode;
using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Movement;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using System.Reflection;
using UnityEngine;

internal static class Program
{
    private const string LightName = "ArenaMode_PhaseLighting";
    private static readonly string[] SupportedMaps =
    {
        "laboratory", "factory4_day", "factory4_night", "labyrinth", "bigmap",
        "arena_equator", "arena_bay5", "arena_airpit",
    };
    private static int _assertions;

    private static int Main()
    {
        try
        {
            CheckPolicySafetyAndMapScope();
            CheckPolicyPrecedenceAndFades();
            foreach (string map in SupportedMaps)
            {
                CheckRuntimeOwnershipAndCleanup(map);
                CheckModeTransitions(map);
                CheckExternalLightingCompatibility(map);
                CheckStaleWorldAndCreationFailure(map);
            }
            CheckFastDeployIsolation();
            CheckSourceContract();
            Console.WriteLine($"ARENA_LIGHTING_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"ARENA_LIGHTING_CHECKS_FAILED after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private static void CheckPolicySafetyAndMapScope()
    {
        Assert(ArenaLightingPolicy.SupportsMap("laboratory"), "Labs is supported");
        Assert(ArenaLightingPolicy.SupportsMap(" LABORATORY "), "Labs id is normalized");
        Assert(ArenaLightingPolicy.SupportsMap("factory4_day"), "Factory day is supported");
        Assert(ArenaLightingPolicy.SupportsMap(" FACTORY4_NIGHT "), "Factory night id is normalized");
        Assert(!ArenaLightingPolicy.SupportsMap("factory4"), "ambiguous Factory alias is rejected");
        Assert(ArenaLightingPolicy.SupportsMap("labyrinth"), "Labyrinth is supported");
        Assert(ArenaLightingPolicy.SupportsMap(" LABYRINTH "), "Labyrinth id is normalized");
        Assert(!ArenaLightingPolicy.SupportsMap("labyrinth_extra"), "unknown Labyrinth variant is rejected");
        Assert(!ArenaLightingPolicy.SupportsMap("shoreline"), "unsupported map is rejected");
        Assert(!ArenaLightingPolicy.SupportsMap(null), "missing map is rejected");
        foreach (string map in SupportedMaps)
        {
            Assert(ArenaLightingPolicy.SupportsMap(map), $"supported Arena map {map}");
            Assert(ArenaLightingPolicy.SupportsMap($" {map.ToUpperInvariant()} "),
                $"normalized supported Arena map {map}");
            Assert(!ArenaLightingPolicy.SupportsMap(map + "_extra"),
                $"unknown map variant {map}_extra is rejected");
        }

        ArenaLightingContext context = BaseContext();
        context.Enabled = false;
        AssertOff(ArenaLightingPolicy.Evaluate(context), "disabled setting");
        context.Enabled = true;
        context.SupportedMap = false;
        AssertOff(ArenaLightingPolicy.Evaluate(context), "unsupported map");
        context.SupportedMap = true;
        context.Strength = 0f;
        AssertOff(ArenaLightingPolicy.Evaluate(context), "zero strength");
        context.Strength = float.NaN;
        AssertOff(ArenaLightingPolicy.Evaluate(context), "NaN strength");
        context.Strength = float.PositiveInfinity;
        AssertOff(ArenaLightingPolicy.Evaluate(context), "infinite strength");
        AssertNear(ArenaLightingPolicy.BoundStrength(-1f), 0f, "negative strength clamps off");
        AssertNear(ArenaLightingPolicy.BoundStrength(9f), 2f, "strength has a hard upper bound");
        AssertNear(ArenaLightingPolicy.BoundOutcomeDuration(float.NaN), 3f,
            "invalid outcome duration falls back");
        AssertNear(ArenaLightingPolicy.BoundOutcomeDuration(-1f), 3f,
            "negative outcome duration falls back");
        AssertNear(ArenaLightingPolicy.BoundOutcomeDuration(20f), 8f,
            "outcome duration has a hard upper bound");

        ArenaLightColor normalized = new ArenaLightColor(0.25f, 1.25f, 2f).Normalized();
        AssertNear(normalized.Red, 0.125f, "HDR red normalizes");
        AssertNear(normalized.Green, 0.625f, "HDR green normalizes");
        AssertNear(normalized.Blue, 1f, "HDR blue normalizes");
        ArenaLightColor invalid = new ArenaLightColor(float.NaN, -2f, float.PositiveInfinity).Normalized();
        AssertNear(invalid.Red, 0f, "invalid red fails black");
        AssertNear(invalid.Green, 0f, "negative green fails black");
        AssertNear(invalid.Blue, 0f, "invalid blue fails black");
    }

    private static void CheckPolicyPrecedenceAndFades()
    {
        ArenaLightingContext context = BaseContext();
        context.Outcome = ArenaLightingOutcome.Won;
        context.OutcomeRemaining = 3f;
        context.FightRemaining = 0.7f;
        context.CountdownRemaining = 3f;
        context.LiveRemaining = 10f;
        ArenaLightingFrame frame = ArenaLightingPolicy.Evaluate(context);
        Assert(frame.Effect == ArenaLightingEffect.Victory, "victory outranks every lower phase");
        AssertColor(frame.Color, 0.125f, 0.625f, 1f, "victory reuses normalized friendly cyan");

        context.Outcome = ArenaLightingOutcome.Lost;
        frame = ArenaLightingPolicy.Evaluate(context);
        Assert(frame.Effect == ArenaLightingEffect.Defeat, "defeat has result precedence");
        AssertColor(frame.Color, 1f, 0.1175f, 0.1175f, "defeat reuses normalized opponent red");

        context.Outcome = ArenaLightingOutcome.None;
        frame = ArenaLightingPolicy.Evaluate(context);
        Assert(frame.Effect == ArenaLightingEffect.Fight, "fight flash outranks countdown");

        context.FightRemaining = 0f;
        frame = ArenaLightingPolicy.Evaluate(context);
        Assert(frame.Effect == ArenaLightingEffect.Countdown, "countdown outranks time pressure");
        float integerPulse = frame.Intensity;
        context.CountdownRemaining = 2.5f;
        float halfPulse = ArenaLightingPolicy.Evaluate(context).Intensity;
        Assert(integerPulse > halfPulse * 3f, "countdown visibly peaks at integer boundaries");

        context.CountdownRemaining = 4f;
        context.LiveRemaining = 30f;
        ArenaLightingFrame earlyPressure = ArenaLightingPolicy.Evaluate(context);
        Assert(earlyPressure.Effect == ArenaLightingEffect.TimePressure,
            "pressure begins at the configured window");
        context.LiveRemaining = 5f;
        ArenaLightingFrame urgentPressure = ArenaLightingPolicy.Evaluate(context);
        Assert(urgentPressure.Intensity > earlyPressure.Intensity,
            "pressure increases as time expires");
        context.LiveRemaining = 31f;
        AssertOff(ArenaLightingPolicy.Evaluate(context), "pressure stays outside its window");

        context = BaseContext();
        context.Outcome = ArenaLightingOutcome.Won;
        context.OutcomeRemaining = 3f;
        float fullResult = ArenaLightingPolicy.Evaluate(context).Intensity;
        context.OutcomeRemaining = 0.5f;
        float fadingResult = ArenaLightingPolicy.Evaluate(context).Intensity;
        Assert(fullResult > fadingResult * 5f, "result light eases out instead of snapping");
    }

    private static void CheckRuntimeOwnershipAndCleanup(string map)
    {
        ResetHarness();
        var world = World(map);
        var match = new ArenaMatch(new EndlessMode());
        ArenaSession.IsActive = false;
        ArenaLightingDirector.Attach(match, world);
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == 0, "ordinary raid creates no light");
        Assert(match.AnnouncerSubscriberCount == 0, "ordinary-raid tick releases subscription");

        ResetHarness();
        world = World(map);
        match = new ArenaMatch(new EndlessMode());
        ArenaSession.IsActive = true;
        Time.time = 10f;
        ArenaLightingDirector.Attach(match, world);
        Assert(match.AnnouncerSubscriberCount == 1, "active match has one cue subscription");
        ArenaLightingDirector.Tick();
        GameObject lightObject = SinglePhaseLight();
        Light light = lightObject.GetComponent<Light>();
        Assert(light != null && light.enabled, "initial fight flash creates and enables one light");
        Assert(light.type == LightType.Directional, "owned light is directional");
        Assert(light.shadows == LightShadows.None, "owned light cannot add shadow cost");
        Assert(light.bounceIntensity == 0f, "owned light cannot feed baked bounce");
        Assert(lightObject.transform.parent == world.transform, "owned light belongs to exact GameWorld");
        Assert(lightObject.hideFlags == HideFlags.HideAndDontSave, "owned light is transient");
        int count = PhaseLights().Count;
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == count, "repeated tick does not duplicate the light");

        Time.time += ArenaLightingPolicy.FightFlashSeconds + 0.1f;
        ArenaLightingDirector.Tick();
        Assert(!light.enabled && light.intensity == 0f, "inactive phase disables the owned light");

        Settings.FriendlyTracerColor.Value = ArenaTracerColor.Green;
        match.Emit(ArenaAnnouncerCue.RoundWon);
        ArenaLightingDirector.Tick();
        Assert(light.enabled, "victory cue enables result light");
        AssertNear(light.color.r, 0.3046667f, "victory uses configured friendly red channel", 0.0002f);
        AssertNear(light.color.g, 1f, "victory uses configured friendly green channel");
        AssertNear(light.color.b, 0.338f, "victory uses configured friendly blue channel", 0.0002f);

        Settings.ArenaLightingOutcomeSeconds.Value = float.NaN;
        match.Emit(ArenaAnnouncerCue.RoundLost);
        ArenaLightingDirector.Tick();
        Assert(light.enabled && float.IsFinite(light.intensity),
            "invalid result duration falls back without poisoning the runtime light");
        Assert(light.color.r > 0.95f && light.color.g < 0.15f,
            "invalid result duration still renders the configured opponent color");

        ArenaLightingDirector.Reset();
        Assert(lightObject.Destroyed, "reset destroys the owned GameObject");
        Assert(!light.enabled && light.intensity == 0f, "reset disables light before deferred destroy");
        Assert(match.AnnouncerSubscriberCount == 0, "reset removes cue subscription");
        ArenaLightingDirector.Reset();
        Assert(match.AnnouncerSubscriberCount == 0, "repeat reset is idempotent");
    }

    private static void CheckModeTransitions(string map)
    {
        ResetHarness();
        var tactical = new TacticalMode
        {
            CurrentPhase = TacticalMode.Phase.Buy,
            PhaseRemaining = 3f,
        };
        Time.time = 20f;
        ArenaLightingDirector.Attach(new ArenaMatch(tactical), World(map));
        ArenaLightingDirector.Tick();
        Light light = SinglePhaseLight().GetComponent<Light>();
        AssertColor(light.color, 1f, 0.28f, 0.04f,
            "Tactical waits for its real round start and shows the buy countdown in amber");

        ResetHarness();
        tactical = new TacticalMode
        {
            CurrentPhase = TacticalMode.Phase.Staging,
            PhaseRemaining = 30f,
        };
        ArenaLightingDirector.Attach(new ArenaMatch(tactical), World(map));
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == 0,
            "protected Tactical staging cannot produce a premature fight flash");

        tactical.CurrentPhase = TacticalMode.Phase.Live;
        tactical.PhaseRemaining = 20f;
        var tacticalMatch = new ArenaMatch(tactical);
        ArenaLightingDirector.Attach(tacticalMatch, Singleton<GameWorld>.Instance);
        tacticalMatch.Emit(ArenaAnnouncerCue.RoundStart);
        ArenaLightingDirector.Tick();
        light = SinglePhaseLight().GetComponent<Light>();
        Assert(light.color.b > light.color.r,
            "Tactical's real round-start cue produces the friendly flash");

        Time.time += ArenaLightingPolicy.FightFlashSeconds + 0.1f;
        tactical.CurrentPhase = TacticalMode.Phase.Live;
        tactical.PhaseRemaining = 20f;
        ArenaLightingDirector.Tick();
        Assert(light.color.r > 0.95f && light.color.g < 0.05f,
            "Tactical final seconds are red");

        ResetHarness();
        var rounds = new RoundsMode
        {
            InIntermission = true,
            IntermissionRemaining = 2f,
        };
        rounds.Round.Number = 1;
        ArenaLightingDirector.Attach(new ArenaMatch(rounds), World(map));
        Time.time = 40f;
        SetPrivateStatic("_fightUntil", 0f);
        ArenaLightingDirector.Tick();
        light = SinglePhaseLight().GetComponent<Light>();
        AssertColor(light.color, 1f, 0.28f, 0.04f, "Rounds intermission counts in amber");

        rounds.InIntermission = false;
        rounds.Round.Number = 2;
        rounds.TimeRemaining = 300f;
        ArenaLightingDirector.Tick();
        Assert(light.color.b > light.color.r, "new Rounds bracket flashes friendly color");

        ResetHarness();
        var unsupportedMatch = new ArenaMatch(new EndlessMode());
        ArenaLightingDirector.Attach(unsupportedMatch, World("shoreline"));
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == 0, "unsupported Arena map creates no light");
        Assert(unsupportedMatch.AnnouncerSubscriberCount == 1,
            "unsupported map remains attached for clean lifecycle teardown");
        ArenaLightingDirector.Reset();
        Assert(unsupportedMatch.AnnouncerSubscriberCount == 0,
            "unsupported map still tears down its subscription");
    }

    private static void CheckExternalLightingCompatibility(string map)
    {
        ResetHarness();
        Chainloader.PluginInfos["com.vultify.blackout"] = new object();
        ArenaLightingDirector.Attach(new ArenaMatch(new EndlessMode()), World(map));
        ArenaLightingDirector.Tick();
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == 0, "Blackout ownership suppresses Arena light");
        Assert(Plugin.ModLogger.Infos.Count(message => message.Contains("Blackout")) == 1,
            "external ownership logs once");

        Settings.ArenaLightingRespectExternalMods.Value = false;
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == 1, "explicit override permits additive light");

        ResetHarness();
        Chainloader.PluginInfos["com.senze.goingdark"] = new object();
        ArenaLightingDirector.Attach(new ArenaMatch(new EndlessMode()), World(map));
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == 0, "Going Dark ownership suppresses Arena light");
    }

    private static void CheckStaleWorldAndCreationFailure(string map)
    {
        ResetHarness();
        var first = World(map);
        var match = new ArenaMatch(new EndlessMode());
        ArenaLightingDirector.Attach(match, first);
        ArenaLightingDirector.Tick();
        GameObject lightObject = SinglePhaseLight();
        Singleton<GameWorld>.Instance = new GameWorld { LocationId = map };
        ArenaLightingDirector.Tick();
        Assert(lightObject.Destroyed, "replacement GameWorld tears down stale light");
        Assert(match.AnnouncerSubscriberCount == 0, "replacement GameWorld tears down subscription");

        ResetHarness();
        Chainloader.ThrowOnRead = true;
        ArenaLightingDirector.Attach(new ArenaMatch(new EndlessMode()), World(map));
        ArenaLightingDirector.Tick();
        ArenaLightingDirector.Tick();
        Assert(PhaseLights().Count == 1, "registry probe failure does not break additive light");
        Assert(Plugin.ModLogger.Warnings.Count(message => message.Contains("ownership")) == 1,
            "registry probe failure logs once");

        Settings.ArenaLightingEnabled.Value = false;
        Light light = SinglePhaseLight().GetComponent<Light>();
        ArenaLightingDirector.Tick();
        Assert(!light.enabled, "live disable immediately turns owned light off");
    }

    private static void CheckFastDeployIsolation()
    {
        ResetHarness();
        var config = new GlobalConfiguration { TimeBeforeDeployLocal = 20 };
        Singleton<GlobalConfiguration>.Instance = config;
        ArenaSession.IsActive = true;
        InvokeDeployHook("ShortenCountdownPatch", "Prefix");
        Assert(config.TimeBeforeDeployLocal == 20,
            "ordinary unarmed countdown remains unchanged even if an active latch was stale");

        ArenaSession.IsActive = false;
        ArenaSession.ArmedForNextRaid = true;
        InvokeDeployHook("ShortenCountdownPatch", "Prefix");
        Assert(config.TimeBeforeDeployLocal == 1,
            "armed Arena loading countdown is exactly one second before IsActive");
        InvokeDeployHook("RestoreAfterCountdownPatch", "Postfix");
        Assert(config.TimeBeforeDeployLocal == 20, "native countdown completion restores original duration");
        FastDeployPatches.Restore();
        Assert(config.TimeBeforeDeployLocal == 20, "repeated teardown preserves original duration");

        config.TimeBeforeDeployLocal = 35;
        InvokeDeployHook("ShortenCountdownPatch", "Prefix");
        ArenaSession.ArmedForNextRaid = false;
        InvokeDeployHook("ShortenCountdownPatch", "Prefix");
        Assert(config.TimeBeforeDeployLocal == 35,
            "ordinary raid after aborted Arena load heals original duration before its arm gate");

        ArenaSession.ArmedForNextRaid = true;
        InvokeDeployHook("ShortenCountdownPatch", "Prefix");
        Settings.FastDeploy.Value = false;
        InvokeDeployHook("ShortenCountdownPatch", "Prefix");
        Assert(config.TimeBeforeDeployLocal == 35,
            "disabled fast deploy heals aborted Arena duration before its setting gate");

        Settings.FastDeploy.Value = true;
        foreach (int existing in new[] { 0, 1 })
        {
            config.TimeBeforeDeployLocal = existing;
            InvokeDeployHook("ShortenCountdownPatch", "Prefix");
            Assert(config.TimeBeforeDeployLocal == existing,
                $"short existing duration {existing} is never increased");
            FastDeployPatches.Restore();
            Assert(config.TimeBeforeDeployLocal == existing,
                $"unchanged duration {existing} creates no restoration lease");
        }

        Singleton<GlobalConfiguration>.Instance = null;
        InvokeDeployHook("ShortenCountdownPatch", "Prefix");
        FastDeployPatches.Restore();
        Assert(Singleton<GlobalConfiguration>.Instance == null,
            "missing configuration is ignored without manufacturing global state");
    }

    private static void InvokeDeployHook(string patchName, string hookName)
    {
        Type patch = typeof(FastDeployPatches).GetNestedType(patchName, BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(FastDeployPatches).FullName, patchName);
        MethodInfo hook = patch.GetMethod(hookName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(patch.FullName, hookName);
        hook.Invoke(null, null);
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string director = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Effects", "ArenaLightingDirector.cs"));
        string policy = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Effects", "ArenaLightingPolicy.cs"));
        string settings = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Config", "Settings.cs"));
        string service = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Modes", "ArenaMatchService.cs"));
        string controller = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "ArenaController.cs"));
        string plugin = File.ReadAllText(Path.Combine(
            root, "ArenaMode.Client", "Plugin.cs"));

        RequireAll(director,
            "if (!ArenaSession.IsActive",
            "ReferenceEquals(Singleton<GameWorld>.Instance, _world)",
            "ArenaLightingPolicy.SupportsMap(_world?.LocationId)",
            "match.Mode is TacticalMode",
            "com.vultify.blackout",
            "com.senze.goingdark",
            "new GameObject(LightObjectName)",
            "created.transform.SetParent(_world.transform, false)",
            "LightType.Directional",
            "LightShadows.None",
            "UnityEngine.Object.Destroy(_lightObject)",
            "_match.OnAnnouncerCue -= OnAnnouncerCue");
        Assert(CountOccurrences(director, "new GameObject(") == 1,
            "runtime owns at most one GameObject creation site");
        Assert(!director.Contains("FindObjectsOfType", StringComparison.Ordinal)
               && !director.Contains("Resources.", StringComparison.Ordinal)
               && !director.Contains("sharedMaterial", StringComparison.Ordinal)
               && !director.Contains("RenderSettings", StringComparison.Ordinal)
               && !director.Contains("LevelSettings", StringComparison.Ordinal)
               && !director.Contains("SceneManager", StringComparison.Ordinal),
            "runtime performs no scene scan or mutation of existing map lighting");
        RequireAll(policy,
            "string.Equals(locationId?.Trim(), \"laboratory\"",
            "context.Outcome != ArenaLightingOutcome.None",
            "IsPositive(context.FightRemaining)",
            "context.CountdownRemaining <= CountdownWindowSeconds",
            "context.LiveRemaining <= context.PressureWindow");
        RequireAll(settings,
            "public static ConfigEntry<bool> ArenaLightingEnabled",
            "public static ConfigEntry<float> ArenaLightingStrength",
            "public static ConfigEntry<int> ArenaLightingPressureSeconds",
            "public static ConfigEntry<float> ArenaLightingOutcomeSeconds",
            "public static ConfigEntry<bool> ArenaLightingRespectExternalMods");
        RequireAll(service,
            "Effects.ArenaLightingDirector.Attach(match, world)",
            "Try(Effects.ArenaLightingDirector.Reset, \"lighting detach\")",
            "Effects.ArenaLightingDirector.Reset();");
        Assert(CountOccurrences(service, "Effects.ArenaLightingDirector.Reset") == 3,
            "failure, raid end and new-raid recovery each own lighting teardown");
        RequireAll(controller, "Effects.ArenaLightingDirector.Tick();");
        RequireAll(plugin, "Effects.ArenaLightingDirector.Shutdown();");
    }

    private static ArenaLightingContext BaseContext()
        => new()
        {
            Enabled = true,
            SupportedMap = true,
            Now = 1f,
            Strength = 1f,
            CountdownRemaining = -1f,
            LiveRemaining = -1f,
            PressureWindow = 30f,
            FightRemaining = -1f,
            Outcome = ArenaLightingOutcome.None,
            OutcomeRemaining = -1f,
            OutcomeDuration = 3f,
            FriendlyColor = new ArenaLightColor(0.25f, 1.25f, 2f),
            OpponentColor = new ArenaLightColor(2f, 0.235f, 0.235f),
        };

    private static GameWorld World(string locationId)
    {
        ArenaSession.IsActive = true;
        var world = new GameWorld { LocationId = locationId };
        Singleton<GameWorld>.Instance = world;
        return world;
    }

    private static void ResetHarness()
    {
        FastDeployPatches.Restore();
        Singleton<GlobalConfiguration>.Instance = null;
        ArenaSession.ArmedForNextRaid = false;
        Settings.FastDeploy.Value = true;
        ArenaLightingDirector.Reset();
        GameObject.Created.Clear();
        Singleton<GameWorld>.Instance = null;
        ArenaSession.IsActive = false;
        Time.time = 0f;
        Chainloader.ThrowOnRead = false;
        Chainloader.PluginInfos = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Settings.ArenaLightingEnabled.Value = true;
        Settings.ArenaLightingStrength.Value = 1f;
        Settings.ArenaLightingPressureSeconds.Value = 30;
        Settings.ArenaLightingOutcomeSeconds.Value = 3f;
        Settings.ArenaLightingRespectExternalMods.Value = true;
        Settings.FriendlyTracerColor.Value = ArenaTracerColor.Cyan;
        Settings.OpponentTracerColor.Value = ArenaTracerColor.Red;
        Plugin.ModLogger.Infos.Clear();
        Plugin.ModLogger.Warnings.Clear();
        Plugin.ModLogger.Debugs.Clear();
    }

    private static List<GameObject> PhaseLights()
        => GameObject.Created.Where(item => item.name == LightName).ToList();

    private static GameObject SinglePhaseLight()
    {
        List<GameObject> lights = PhaseLights();
        Assert(lights.Count == 1, $"exactly one phase light exists (got {lights.Count})");
        return lights[0];
    }

    private static void SetPrivateStatic(string name, object value)
    {
        FieldInfo field = typeof(ArenaLightingDirector).GetField(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(typeof(ArenaLightingDirector).FullName, name);
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

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static void RequireAll(string source, params string[] fragments)
    {
        foreach (string fragment in fragments)
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                "production source retains required contract: " + fragment);
    }

    private static void AssertOff(ArenaLightingFrame frame, string message)
        => Assert(frame.Effect == ArenaLightingEffect.Off && !frame.Visible && frame.Intensity == 0f,
            message);

    private static void AssertColor(
        ArenaLightColor actual,
        float red,
        float green,
        float blue,
        string message)
    {
        AssertNear(actual.Red, red, message + " red");
        AssertNear(actual.Green, green, message + " green");
        AssertNear(actual.Blue, blue, message + " blue");
    }

    private static void AssertColor(
        Color actual,
        float red,
        float green,
        float blue,
        string message)
    {
        AssertNear(actual.r, red, message + " red");
        AssertNear(actual.g, green, message + " green");
        AssertNear(actual.b, blue, message + " blue");
    }

    private static void AssertNear(float actual, float expected, string message, float epsilon = 0.0001f)
        => Assert(Math.Abs(actual - expected) <= epsilon,
            $"{message} (expected {expected}, got {actual})");

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
