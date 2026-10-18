using ArenaMode;
using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Mono.Cecil;
using Mono.Cecil.Cil;
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
            CheckPartialEnableCleanup();
            CheckNestedPartialCleanupRecovery();
            CheckPartialDisableRecovery();
            CheckLocalArenaScaling();
            CheckTimingLimits();
            CheckRaidAndActorGates();
            CheckRespawnIdentityGate();
            CheckInstalledAssemblyContract();
            CheckSourceContract();
            ArenaMagazinePatches.Shutdown();
            Console.WriteLine($"ARENA_MAGAZINE_CHECKS_PASSED ({_assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"ARENA_MAGAZINE_CHECKS_FAILED after {_assertions} assertions: {ex}");
            return 1;
        }
    }

    private static void CheckPartialEnableCleanup()
    {
        Reset();
        string unloadId = Plugin.PluginGuid + ".Magazine.UnloadProcess";
        ModulePatch.FailNextEnableAfterPatchForHarmonyId = unloadId;
        bool failed = false;
        try
        {
            ArenaMagazinePatches.Enable();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Injected failure"))
        {
            failed = true;
        }

        Assert(failed, "partial ModulePatch enable failure reaches the caller");
        Assert(ModulePatch.EnabledTargets.Count == 0
               && ModulePatch.EnabledHarmonyIds.Count == 0,
            "partial enable cleanup removes prefixes installed before IsActive");
        ArenaMagazinePatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 2
               && ModulePatch.EnabledHarmonyIds.Count == 2,
            "a clean retry after partial enable installs exactly one prefix pair");
    }

    private static void CheckPatchRegistration()
    {
        Reset();
        ArenaMagazinePatches.Enable();
        ArenaMagazinePatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 2,
            "magazine hooks register exactly once");
        Assert(ModulePatch.EnabledHarmonyIds.SequenceEqual(new[]
               {
                   Plugin.PluginGuid + ".Magazine.LoadProcess",
                   Plugin.PluginGuid + ".Magazine.UnloadProcess",
               }),
            "magazine hooks use plugin-owned Harmony IDs");

        ConstructorInfo load = (ConstructorInfo)ModulePatch.EnabledTargets[0];
        ConstructorInfo unload = (ConstructorInfo)ModulePatch.EnabledTargets[1];
        Assert(load.DeclaringType == typeof(Player.PlayerInventoryController.LoadMagazineProcess)
               && load.GetParameters().Select(parameter => parameter.ParameterType)
                   .SequenceEqual(new[]
                   {
                       typeof(InventoryController), typeof(Magazine), typeof(Ammo),
                       typeof(int), typeof(bool), typeof(float),
                   }),
            "load hook resolves the exact named SPT 4.1 process constructor");
        Assert(unload.DeclaringType == typeof(Player.PlayerInventoryController.UnloadMagazineProcess)
               && unload.GetParameters().Select(parameter => parameter.ParameterType)
                   .SequenceEqual(new[]
                   {
                       typeof(InventoryController), typeof(Magazine),
                       typeof(float), typeof(bool),
                    }),
            "unload hook resolves the exact named SPT 4.1 process constructor");
        AssertPrefixContract("ArenaMagazineLoadProcessPatch");
        AssertPrefixContract("ArenaMagazineUnloadProcessPatch");

        ArenaMagazinePatches.Shutdown();
        Assert(ModulePatch.EnabledTargets.Count == 0
               && ModulePatch.DisabledTargets.Count == 2
               && ModulePatch.EnabledHarmonyIds.Count == 0
               && ModulePatch.DisabledHarmonyIds.Count == 2,
            "shutdown removes both plugin-owned prefixes");
        ArenaMagazinePatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 2,
            "enable after shutdown installs one fresh prefix pair");
    }

    private static void CheckNestedPartialCleanupRecovery()
    {
        Reset();
        string unloadId = Plugin.PluginGuid + ".Magazine.UnloadProcess";
        ModulePatch.FailNextEnableAfterPatchForHarmonyId = unloadId;
        ModulePatch.FailNextDisableBeforeUnpatchForHarmonyId = unloadId;

        bool failed = false;
        try
        {
            ArenaMagazinePatches.Enable();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Injected failure"))
        {
            failed = true;
        }

        Assert(failed && ModulePatch.EnabledHarmonyIds.SequenceEqual(new[] { unloadId }),
            "nested partial cleanup test retains exactly the injected leaked owner");
        ArenaMagazinePatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 2
               && ModulePatch.EnabledHarmonyIds.Count == 2,
            "retry pre-cleans an attempted patch even when IsActive stayed false");
        Assert(ModulePatch.EnabledHarmonyIds.Distinct().Count() == 2,
            "nested partial cleanup recovery never duplicates a Harmony owner");
    }

    private static void CheckPartialDisableRecovery()
    {
        Reset();
        ArenaMagazinePatches.Enable();
        string unloadId = Plugin.PluginGuid + ".Magazine.UnloadProcess";
        ModulePatch.FailNextDisableAfterUnpatchForHarmonyId = unloadId;

        ArenaMagazinePatches.Shutdown();
        Assert(ModulePatch.EnabledTargets.Count == 0
               && ModulePatch.EnabledHarmonyIds.Count == 0,
            "shutdown removes owner patches even if ModulePatch retains stale IsActive");
        Assert(Plugin.ModLogger.Warnings.Count(message =>
                   message.Contains("patch cleanup failed safely")) == 1,
            "a partial disable failure is reported once and remains recoverable");

        ArenaMagazinePatches.Enable();
        Assert(ModulePatch.EnabledTargets.Count == 2
               && ModulePatch.EnabledHarmonyIds.Distinct().Count() == 2,
            "re-enable repairs stale IsActive and installs one complete prefix pair");
    }

    private static void CheckLocalArenaScaling()
    {
        TestContext context = Context(arena: true, multiplier: 2f);
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", context.Controller, 1.5266f), 0.7633f,
            "load delay preserves vanilla inputs and applies the Arena factor");
        AssertNear(Invoke("ArenaMagazineUnloadProcessPatch", context.Controller, 0.3f), 0.15f,
            "unload delay uses the same Arena factor");
        Assert(Plugin.ModLogger.Infos.Count(message =>
                   message.Contains("Arena magazine handling speed active")) == 1,
            "the first successful process emits one bounded runtime marker");
    }

    private static void CheckTimingLimits()
    {
        AssertNear(ArenaMagazineTiming.NormalizeMultiplier(10f), 4f,
            "out-of-range factors cap at 4x");
        AssertNear(ArenaMagazineTiming.NormalizeMultiplier(float.NaN), 1f,
            "invalid factors disable acceleration");
        Assert(ArenaMagazineTiming.TryScale(0.03f, 4f, out float clamped)
               && Math.Abs(clamped - 0.025f) < 0.0001f,
            "fast valid constructor delays stop at the 25ms floor");
        Assert(!ArenaMagazineTiming.TryScale(0.025f, 4f, out float atFloor)
               && Math.Abs(atFloor - 0.025f) < 0.0001f,
            "a delay already at the floor remains untouched");
        Assert(!ArenaMagazineTiming.TryScale(0.01f, 4f, out float belowFloor)
               && Math.Abs(belowFloor - 0.01f) < 0.0001f,
            "another mod's sub-floor delay is never slowed back up");
        Assert(!ArenaMagazineTiming.TryScale(float.PositiveInfinity, 2f, out float infinite)
               && float.IsPositiveInfinity(infinite),
            "invalid native timing passes through unchanged");
        Assert(!ArenaMagazineTiming.TryScale(-1f, 2f, out float negative)
               && Math.Abs(negative + 1f) < 0.0001f,
            "negative native timing passes through unchanged");

        TestContext vanilla = Context(arena: true, multiplier: 1f);
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", vanilla.Controller, 0.8f), 0.8f,
            "an active Arena with a 1x setting retains vanilla timing");

        TestContext capped = Context(arena: true, multiplier: 10f);
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", capped.Controller, 0.03f), 0.025f,
            "the live patch applies both multiplier cap and constructor-delay floor");
        TestContext disabled = Context(arena: true, multiplier: float.NaN);
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", disabled.Controller, 0.8f), 0.8f,
            "an invalid configured factor leaves live timing untouched");
    }

    private static void CheckRaidAndActorGates()
    {
        TestContext ordinary = Context(arena: false, multiplier: 2f);
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", ordinary.Controller, 0.8f), 0.8f,
            "ordinary raids retain vanilla loading timing");

        TestContext stopped = Context(arena: true, multiplier: 2f);
        ArenaMagazinePatches.Shutdown();
        AssertNear(Invoke("ArenaMagazineUnloadProcessPatch", stopped.Controller, 0.3f), 0.3f,
            "plugin shutdown leaves the dormant Harmony prefixes inert");

        TestContext noWorld = Context(arena: true, multiplier: 2f);
        Singleton<GameWorld>.Instance = null;
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", noWorld.Controller, 0.8f), 0.8f,
            "missing raid world retains vanilla timing");

        TestContext bot = Context(arena: true, multiplier: 2f);
        bot.Player.IsAI = true;
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", bot.Controller, 0.8f), 0.8f,
            "an AI main-player stand-in is never accelerated");

        TestContext dead = Context(arena: true, multiplier: 2f);
        dead.Player.HealthController.IsAlive = false;
        AssertNear(Invoke("ArenaMagazineUnloadProcessPatch", dead.Controller, 0.3f), 0.3f,
            "a dead local player is never accelerated");

        TestContext generic = Context(arena: true, multiplier: 2f);
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", new InventoryController(), 0.8f), 0.8f,
            "a non-player inventory controller is untouched");

        TestContext foreign = Context(arena: true, multiplier: 2f);
        var foreignPlayer = new Player();
        var foreignController = new Player.PlayerInventoryController { Player = foreignPlayer };
        foreignPlayer.InventoryController = foreignController;
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", foreignController, 0.8f), 0.8f,
            "a foreign player inventory controller is untouched");

        TestContext mismatchedOwner = Context(arena: true, multiplier: 2f);
        mismatchedOwner.Controller.Player = new Player();
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", mismatchedOwner.Controller, 0.8f), 0.8f,
            "a controller with a mismatched Player field is untouched");

        TestContext mismatchedInventory = Context(arena: true, multiplier: 2f);
        mismatchedInventory.Player.InventoryController = new InventoryController();
        AssertNear(Invoke("ArenaMagazineUnloadProcessPatch", mismatchedInventory.Controller, 0.3f), 0.3f,
            "a stale controller absent from the player is untouched");
    }

    private static void CheckRespawnIdentityGate()
    {
        TestContext old = Context(arena: true, multiplier: 2f);
        var replacement = new Player();
        var replacementController = new Player.PlayerInventoryController { Player = replacement };
        replacement.InventoryController = replacementController;
        Singleton<GameWorld>.Instance.MainPlayer = replacement;

        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", old.Controller, 0.8f), 0.8f,
            "a pre-respawn controller cannot inherit Arena acceleration");
        AssertNear(Invoke("ArenaMagazineLoadProcessPatch", replacementController, 0.8f), 0.4f,
            "the replacement local controller receives Arena acceleration");
    }

    private static void CheckInstalledAssemblyContract()
    {
        string root = FindRepositoryRoot();
        string spt = Directory.GetParent(Directory.GetParent(root).FullName).FullName;
        string assemblyPath = Path.Combine(
            spt, "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll");
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        TypeDefinition player = assembly.MainModule.GetType("EFT.Player")
                                ?? throw new InvalidOperationException("Installed EFT.Player missing");
        TypeDefinition controller = player.NestedTypes.Single(type =>
            type.Name == "PlayerInventoryController");
        TypeDefinition load = controller.NestedTypes.Single(type =>
            type.Name == "LoadMagazineProcess");
        TypeDefinition unload = controller.NestedTypes.Single(type =>
            type.Name == "UnloadMagazineProcess");

        MethodDefinition loadConstructor = AssertConstructor(load, new[]
        {
            "EFT.InventoryLogic.InventoryController", "EFT.InventoryLogic.Magazine",
            "EFT.InventoryLogic.Ammo", "System.Int32", "System.Boolean", "System.Single",
        }, "installed load process");
        MethodDefinition unloadConstructor = AssertConstructor(unload, new[]
        {
            "EFT.InventoryLogic.InventoryController", "EFT.InventoryLogic.Magazine",
            "System.Single", "System.Boolean",
        }, "installed unload process");
        Assert(loadConstructor.Parameters[0].Name == "inventoryController"
               && loadConstructor.Parameters[^1].Name == "loadOneAmmoSpeed",
            "installed load parameter names retain Harmony's prefix bindings");
        Assert(unloadConstructor.Parameters[0].Name == "inventoryController"
               && unloadConstructor.Parameters[2].Name == "loadOneAmmoSpeed",
            "installed unload parameter names retain Harmony's prefix bindings");

        foreach ((TypeDefinition process, MethodDefinition constructor, ParameterDefinition timingParameter) in new[]
                 {
                     (load, loadConstructor, loadConstructor.Parameters[^1]),
                     (unload, unloadConstructor, unloadConstructor.Parameters[2]),
                 })
        {
            Assert(process.Fields.Any(field => field.IsPublic
                                               && field.Name == "_loadOneAmmoSpeed"
                                               && field.FieldType.FullName == "System.Single"),
                process.Name + " exposes the event duration field used by the UI");
            Assert(process.Fields.Any(field => field.IsPublic
                                               && field.Name == "_cooldown"
                                               && field.FieldType.FullName == "System.Single"),
                process.Name + " exposes the per-round cooldown field");
            Assert(ConstructorStoresParameterInField(
                       constructor, timingParameter, "_loadOneAmmoSpeed")
                   && ConstructorStoresParameterInField(
                       constructor, timingParameter, "_cooldown"),
                process.Name + " constructor writes both timing fields from its inputs");
        }


        Assert(UsesFieldAndConstructsEvent(
                   load, "_loadOneAmmoSpeed", "EFT.InventoryLogic.LoadMagazineEventArgs"),
            "load process publishes the patched duration through its inventory event");
        Assert(UsesFieldAndConstructsEvent(
                   unload, "_loadOneAmmoSpeed", "EFT.InventoryLogic.UnloadMagazineEventArgs"),
            "unload process publishes the patched duration through its inventory event");
        Assert(UsesCooldownDelay(load),
            "load process consumes the patched cooldown through Task.Delay");
        Assert(UsesCooldownDelay(unload),
            "unload process consumes the patched cooldown through Task.Delay");

        Assert(controller.Fields.Any(field => field.IsPublic
                                              && field.Name == "Player"
                                              && field.FieldType.FullName == "EFT.Player"),
            "installed controller exposes the exact owning-player identity gate");
        Assert(player.Properties.Any(property => property.Name == "InventoryController"
                                                && property.PropertyType.FullName
                                                == "EFT.InventoryLogic.InventoryController"),
            "installed player exposes the exact active inventory-controller identity gate");
    }

    private static bool ConstructorStoresParameterInField(
        MethodDefinition constructor,
        ParameterDefinition parameter,
        string fieldName)
    {
        foreach (Instruction instruction in constructor.Body.Instructions)
        {
            if (instruction.OpCode.Code != Code.Stfld
                || instruction.Operand is not FieldReference field
                || field.Name != fieldName)
            {
                continue;
            }

            Instruction source = instruction.Previous;
            while (source != null && source.OpCode.Code == Code.Nop)
                source = source.Previous;
            if (LoadsParameter(source, constructor, parameter))
                return true;
        }

        return false;
    }

    private static bool LoadsParameter(
        Instruction instruction,
        MethodDefinition method,
        ParameterDefinition parameter)
    {
        if (instruction == null)
            return false;

        if ((instruction.OpCode.Code == Code.Ldarg
             || instruction.OpCode.Code == Code.Ldarg_S)
            && instruction.Operand is ParameterDefinition loaded)
        {
            return loaded.Index == parameter.Index;
        }

        int argumentIndex = method.HasThis ? parameter.Index + 1 : parameter.Index;
        return instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => argumentIndex == 0,
            Code.Ldarg_1 => argumentIndex == 1,
            Code.Ldarg_2 => argumentIndex == 2,
            Code.Ldarg_3 => argumentIndex == 3,
            _ => false,
        };
    }

    private static bool UsesFieldAndConstructsEvent(
        TypeDefinition process,
        string fieldName,
        string eventType)
        => process.Methods.Any(method => method.HasBody
            && ReadsField(method, fieldName)
            && method.Body.Instructions.Any(instruction =>
                instruction.OpCode.Code == Code.Newobj
                && instruction.Operand is MethodReference called
                && called.Name == ".ctor"
                && called.DeclaringType.FullName == eventType));

    private static bool UsesCooldownDelay(TypeDefinition process)
        => process.Methods.Any(method => method.HasBody
            && ReadsField(method, "_cooldown")
            && Calls(method, "UnityEngine.Mathf", "CeilToInt")
            && Calls(method, "System.Threading.Tasks.Task", "Delay"));

    private static bool ReadsField(MethodDefinition method, string fieldName)
        => method.Body.Instructions.Any(instruction =>
            (instruction.OpCode.Code == Code.Ldfld
             || instruction.OpCode.Code == Code.Ldflda)
            && instruction.Operand is FieldReference field
            && field.Name == fieldName);

    private static bool Calls(
        MethodDefinition method,
        string declaringType,
        string methodName)
        => method.Body.Instructions.Any(instruction =>
            (instruction.OpCode.Code == Code.Call
             || instruction.OpCode.Code == Code.Callvirt)
            && instruction.Operand is MethodReference called
            && called.Name == methodName
            && called.DeclaringType.FullName == declaringType);

    private static MethodDefinition AssertConstructor(
        TypeDefinition type,
        IReadOnlyList<string> expectedParameters,
        string message)
    {
        MethodDefinition constructor = type.Methods.SingleOrDefault(method =>
            method.IsConstructor
            && method.IsPublic
            && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(expectedParameters));
        Assert(constructor != null, message + " retains the exact public SPT 4.1 signature");
        return constructor;
    }

    private static void AssertPrefixContract(string patchName)
    {
        Type patch = typeof(ArenaMagazinePatches).GetNestedType(
            patchName, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(patchName + " missing");
        MethodInfo prefix = patch.GetMethod(
            "Prefix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(patchName + ".Prefix missing");
        Assert(prefix.GetCustomAttribute<PatchPrefixAttribute>() != null,
            patchName + " retains the ModulePatch prefix attribute");
        Assert(prefix.GetParameters().Select(parameter => parameter.Name)
                .SequenceEqual(new[] { "inventoryController", "loadOneAmmoSpeed" }),
            patchName + " prefix names retain Harmony's original-argument bindings");
    }

    private static void CheckSourceContract()
    {
        string root = FindRepositoryRoot();
        string patch = File.ReadAllText(Path.Combine(root,
            "ArenaMode.Client", "Combat", "ArenaMagazinePatches.cs"));
        string settings = File.ReadAllText(Path.Combine(root,
            "ArenaMode.Client", "Config", "Settings.cs"));
        string plugin = File.ReadAllText(Path.Combine(root,
            "ArenaMode.Client", "Plugin.cs"));

        RequireAll(patch,
            "if (!_enabled)",
            "if (!ArenaSession.IsActive",
            "player.HealthController?.IsAlive != true",
            "inventoryController is not Player.PlayerInventoryController localController",
            "ReferenceEquals(localController.Player, player)",
            "ReferenceEquals(player.InventoryController, inventoryController)",
            "typeof(Player.PlayerInventoryController.LoadMagazineProcess)",
            "typeof(Player.PlayerInventoryController.UnloadMagazineProcess)",
            "ref float loadOneAmmoSpeed",
            "loadOneAmmoSpeed = originalDelay");
        Assert(!patch.Contains("Singleton<GlobalConfiguration>", StringComparison.Ordinal)
               && !patch.Contains("LoadUnloadModifier =", StringComparison.Ordinal)
               && !patch.Contains("Profile.Skills.", StringComparison.Ordinal),
            "implementation never mutates global timing, magazine templates or skills");
        RequireAll(settings,
            "public static ConfigEntry<float> MagazineHandlingSpeed",
            "\"MagazineHandlingSpeed\"",
            "new AcceptableValueRange<float>(1f, 4f)");
        RequireAll(plugin,
            "Combat.ArenaMagazinePatches.Enable()",
            "Combat.ArenaMagazinePatches.Shutdown()");
    }

    private static TestContext Context(bool arena, float multiplier)
    {
        Reset();
        ArenaMagazinePatches.Enable();
        ArenaSession.IsActive = arena;
        Settings.MagazineHandlingSpeed.Value = multiplier;
        var player = new Player();
        var controller = new Player.PlayerInventoryController { Player = player };
        player.InventoryController = controller;
        Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = player };
        return new TestContext(player, controller);
    }

    private static float Invoke(
        string patchName,
        InventoryController controller,
        float delay)
    {
        Type patch = typeof(ArenaMagazinePatches).GetNestedType(
            patchName, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(patchName + " missing");
        MethodInfo prefix = patch.GetMethod(
            "Prefix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(patchName + ".Prefix missing");
        object[] arguments = { controller, delay };
        prefix.Invoke(null, arguments);
        return (float)arguments[1];
    }

    private static void Reset()
    {
        ArenaMagazinePatches.Shutdown();
        ModulePatch.EnabledTargets.Clear();
        ModulePatch.DisabledTargets.Clear();
        ModulePatch.EnabledHarmonyIds.Clear();
        ModulePatch.DisabledHarmonyIds.Clear();
        ModulePatch.FailNextEnableAfterPatchForHarmonyId = null;
        ModulePatch.FailNextDisableBeforeUnpatchForHarmonyId = null;
        ModulePatch.FailNextDisableAfterUnpatchForHarmonyId = null;
        Plugin.ModLogger.Clear();
        ArenaSession.IsActive = false;
        Settings.MagazineHandlingSpeed.Value = 2f;
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
        {
            Assert(source.Contains(fragment, StringComparison.Ordinal),
                "production source retains required contract: " + fragment);
        }
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
        Player Player,
        Player.PlayerInventoryController Controller);
}
