using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        need(assembly.Name.Name == "rejzbihai.Pause" && assembly.Name.Version == new Version(1, 5, 0, 0), "published identity");
        var types = All(assembly.MainModule.Types).ToArray();
        var plugin = types.Single(t => t.FullName == "Pause.Plugin");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.rejzbihai.pause"
            && (string)attr.ConstructorArguments[2].Value == "1.5.0", "published plugin metadata");
        var controller = types.Single(t => t.FullName == "Pause.PauseController");
        need(controller.BaseType.FullName == "UnityEngine.MonoBehaviour" && !controller.IsAbstract, "published component identity");
        var update = controller.Methods.Single(m => m.Name == "Update");
        var pause = controller.Methods.Single(m => m.Name == "Pause");
        var unpause = controller.Methods.Single(m => m.Name == "Unpause");
        var awake = controller.Methods.Single(m => m.Name == "Awake");
        var destroy = controller.Methods.Single(m => m.Name == "OnDestroy");
        var property = controller.Properties.Single(p => p.Name == "IsPaused");
        need(new[] { update, pause, unpause, awake, destroy }.All(m => !m.IsStatic && m.IsPrivate && m.Parameters.Count == 0
            && m.ReturnType.FullName == "System.Void"), "published lifecycle and pause methods");
        need(property.PropertyType.FullName == "System.Boolean" && property.GetMethod.IsStatic
            && property.SetMethod.IsStatic && property.SetMethod.Parameters.Single().ParameterType.FullName == "System.Boolean",
            "published pause state accessors");
        var cachedWorld = controller.Fields.Single(f => f.Name == "GameWorld");
        var cachedGame = controller.Fields.Single(f => f.Name == "_abstractGame");
        var toggle = plugin.Fields.Single(f => f.Name == "TogglePause");
        need(cachedWorld.IsStatic && cachedWorld.FieldType.FullName == "EFT.GameWorld"
            && !cachedGame.IsStatic && cachedGame.FieldType.FullName == "EFT.AbstractGame"
            && toggle.IsStatic && toggle.FieldType.FullName == "BepInEx.Configuration.ConfigEntry`1<BepInEx.Configuration.KeyboardShortcut>",
            "published physical ownership and keybind fields");
        var key = controller.Methods.Single(m => m.Name == "IsKeyPressed");
        need(key.IsStatic && key.ReturnType.FullName == "System.Boolean" && key.Parameters.Single().ParameterType.FullName == "BepInEx.Configuration.KeyboardShortcut",
            "published key read signature");
        need(key.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "GetKeyDown"),
            "published key helper reads edge without consuming input");
        var methods = types.SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
        var callers = methods.Where(m => m.Body.Instructions.Any(i => Same(i.Operand, pause))).ToArray();
        need(callers.Length == 1 && callers[0] == update, "published activation only from guarded Update");
        var writers = methods.Where(m => m.Body.Instructions.Any(i => Same(i.Operand, property.SetMethod))).ToArray();
        need(writers.Length == 3 && writers.Contains(update) && writers.Contains(awake) && writers.Contains(destroy),
            "published exactly three state writers");
        foreach (var clear in new[] { awake, destroy })
        {
            var instructions = clear.Body.Instructions.ToArray();
            int at = Array.FindIndex(instructions, i => Same(i.Operand, property.SetMethod)) - 1;
            while (at >= 0 && instructions[at].OpCode.Code == Code.Nop) at--;
            need(at >= 0 && instructions[at].OpCode.Code == Code.Ldc_I4_0, "published init/disposal only clears flag");
        }
        var updateInstructions = update.Body.Instructions.ToArray();
        need(updateInstructions.Count(i => Same(i.Operand, key)) == 1
            && updateInstructions.Count(i => Same(i.Operand, property.SetMethod)) == 1
            && updateInstructions.Count(i => Same(i.Operand, unpause)) == 1
            && updateInstructions.Count(i => i.Operand is FieldReference f && f.FullName == toggle.FullName) == 1
            && Array.FindIndex(updateInstructions, i => Same(i.Operand, key))
                < Array.FindIndex(updateInstructions, i => Same(i.Operand, property.SetMethod)),
            "published key check precedes global pause flag");
        need(pause.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName.Contains("UnityEngine.Time::set_timeScale"))
            && pause.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "PauseAllEffectsOnPlayer"),
            "published pause mutates time and captured player effects");
        need(unpause.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "UnpauseAllEffectsOnPlayer"),
            "published release retains body recovery");
        var enable = controller.Methods.Single(m => m.Name == "Enable");
        need(enable.Body.Instructions.Any(i => i.Operand is FieldReference f && f.FullName == cachedWorld.FullName)
            && awake.Body.Instructions.Any(i => i.Operand is FieldReference f && f.FullName == cachedGame.FullName),
            "published world/game captured by normal creation");
        Console.WriteLine("PUBLISHED_PAUSE_SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    private static bool Same(object operand, MethodReference method) => operand is MethodReference reference && reference.FullName == method.FullName;
    internal static void VerifyNative(string path, Action<bool, string> need)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var types = All(assembly.MainModule.Types).ToArray();
        var game = types.Single(t => t.FullName == "EFT.LocalGame");
        var baseGame = types.Single(t => t.FullName == "EFT.BaseLocalGame`1");
        need(game.BaseType.GetElementType().FullName == baseGame.FullName
            && baseGame.Fields.Any(f => f.Name == "_raidSettings" && !f.IsStatic && f.FieldType.FullName == "EFT.LocalRaidSettings"),
            "native exact raid settings field");
        var world = types.Single(t => t.FullName == "EFT.GameWorld");
        need(world.BaseType.FullName.Contains("MonoBehaviour") || world.BaseType.FullName == "EFT.AbstractGameWorld",
            "native world has Unity component ownership");
        need(world.Methods.Any(m => m.Name == "OnGameStarted" && !m.IsStatic && m.Parameters.Count == 0),
            "native world-start callback exists");
        Console.WriteLine("NATIVE_ASSEMBLY_SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in All(type.NestedTypes)) yield return nested;
        }
    }
}
