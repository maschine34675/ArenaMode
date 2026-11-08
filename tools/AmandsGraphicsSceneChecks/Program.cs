using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Mono.Cecil;
using UnityEngine;

internal static class Program
{
    // Represents the reviewed storage site for Harmony's instructions in this process.
    public static LevelSettings levelSettings = null;
    private static readonly Type Adapter = typeof(ArenaAmandsGraphicsScene);
    private static readonly Func<string, GameObject, GameObject> FindLevel = AccessTools.DeclaredMethod(Adapter, "FindLevelObject")
        .CreateDelegate<Func<string, GameObject, GameObject>>();
    private static readonly Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> Rewrite = AccessTools.DeclaredMethod(Adapter, "Rewrite")
        .CreateDelegate<Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>>();
    private static int _checks;

    private static void Main(string[] args)
    {
        if (args.Length == 1) CheckPublished(args[0]);
        CheckRewrite();
        CheckLookup();
        Console.WriteLine($"Amand's Graphics scene checks passed ({_checks} assertions).");
    }

    private static void CheckPublished(string dll)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(dll);
        var type = assembly.MainModule.GetType("AmandsGraphics.AmandsGraphicsClass");
        var method = type.Methods.Single(x => x.Name == "ActivateAmandsGraphics");
        var il = method.Body.Instructions;
        var matches = il.Where(x => x.OpCode == Mono.Cecil.Cil.OpCodes.Call
            && x.Operand is MethodReference f && f.FullName == "UnityEngine.GameObject UnityEngine.GameObject::Find(System.String)"
            && x.Next?.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt && x.Next.Operand is GenericInstanceMethod g
            && g.DeclaringType.FullName == "UnityEngine.GameObject" && g.Name == "GetComponent" && g.Parameters.Count == 0
            && g.GenericArguments.Single().FullName == "LevelSettings" && x.Next.Next?.OpCode == Mono.Cecil.Cil.OpCodes.Stsfld
            && x.Next.Next.Operand is FieldReference field && field.Name == "levelSettings" && field.FieldType.FullName == "LevelSettings").ToArray();
        Verify(assembly.Name.Name == "AmandsGraphics" && assembly.Name.Version == new Version(1, 8, 0, 0), "reviewed release identity");
        Verify(!method.IsStatic && method.Parameters[0].ParameterType.FullName == "UnityEngine.GameObject", "first native argument is the FPS camera object");
        Verify(matches.Length == 1, "release has exactly one reviewed find/get/store sequence");
    }

    private static CodeInstruction[] NativeSequence() => new[]
    {
        new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(GameObject), nameof(GameObject.Find))),
        new CodeInstruction(OpCodes.Callvirt, typeof(GameObject).GetMethod(nameof(GameObject.GetComponent)).MakeGenericMethod(typeof(LevelSettings))),
        new CodeInstruction(OpCodes.Stsfld, typeof(Program).GetField(nameof(levelSettings))),
    };

    private static void CheckRewrite()
    {
        var original = NativeSequence();
        var label = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        original[0].labels.Add(label);
        var result = Rewrite(original).ToList();
        Verify(result.Count == 4 && result[0].opcode == OpCodes.Ldarg_1 && result[0].labels.Single() == label, "original entry labels and camera stack argument");
        Verify(result[1].opcode == OpCodes.Call && result[1].operand.Equals(FindLevel.Method) && result[2] == original[1] && result[3] == original[2], "original component lookup and assignment preserved");
        try { Rewrite(NativeSequence().Concat(NativeSequence())).ToList(); throw new Exception("ambiguous pattern was accepted"); }
        catch (InvalidOperationException) { _checks++; }
        try { Rewrite(NativeSequence().Skip(1)).ToList(); throw new Exception("missing pattern was accepted"); }
        catch (MissingMethodException) { _checks++; }
    }

    private static Func<object, GameObject, LevelSettings> CompileLookup(bool patched)
    {
        var method = new DynamicMethod("LevelSettingsLookup", typeof(LevelSettings), new[] { typeof(object), typeof(GameObject) }, typeof(Program), true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "!settings");
        foreach (var instruction in patched ? Rewrite(NativeSequence()) : NativeSequence())
        {
            if (instruction.operand is MethodInfo call) il.Emit(instruction.opcode, call);
            else if (instruction.operand is FieldInfo field) il.Emit(instruction.opcode, field);
            else il.Emit(instruction.opcode);
        }
        il.Emit(OpCodes.Ldsfld, typeof(Program).GetField(nameof(levelSettings)));
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object, GameObject, LevelSettings>>();
    }

    private static void CheckLookup()
    {
        var world = Singleton<GameWorld>.Instance = new GameWorld { LocationId = "labyrinth" };
        var game = new LocalGame { GameWorld = world };
        Singleton<AbstractGame>.Instance = game;
        var level = Singleton<LevelSettings>.Instance = new LevelSettings();
        level.gameObject.Component = level;
        var fps = CameraManager.Instance.Camera.gameObject;
        GameObject Lookup() => FindLevel("!settings", fps);
        Verify(Lookup() == null, "ordinary raid preserves upstream missing lookup");
        var native = CompileLookup(false);
        var patched = CompileLookup(true);
        try { patched(null, fps); throw new Exception("ordinary missing-object behavior was overridden"); }
        catch (NullReferenceException) { _checks++; }
        ArenaSession.ArmedForNextRaid = true;
        ArenaSession.ArmedMap = game.Settings.location = "labyrinth";
        ArenaServerClient.PendingArmToken = "current";
        game.Settings.serverId = "start-current";
        try { native(null, fps); throw new Exception("native missing-name failure was not reproduced"); }
        catch (NullReferenceException) { _checks++; }
        Verify(patched(null, fps) == level && levelSettings == level, "patched native instruction sequence assigns the actual level component");
        Verify(Lookup() == level.gameObject, "first armed activation resolves native LevelSettings");
        GameObject.Named = new GameObject();
        Verify(Lookup() == GameObject.Named, "existing native named object wins");
        GameObject.Named = null;
        Verify(FindLevel("some-other-name", fps) == null, "only the default lookup");
        Verify(FindLevel("!settings", new GameObject()) == null, "foreign camera");
        game.Settings.serverId = "start-previous";
        Verify(Lookup() == null, "stale native start token");
        game.Settings.serverId = "end-current";
        Verify(Lookup() == level.gameObject, "bound native end identity during loading");
        world.LocationId = "factory4_day";
        Verify(Lookup() == null, "other map");
        world.LocationId = "labyrinth";
        game.GameWorld = new GameWorld();
        Verify(Lookup() == null, "different physical world");
        game.GameWorld = world;
        ArenaSession.ArmedForNextRaid = false;
        ArenaSession.IsActive = true;
        Verify(Lookup() == null, "stale active world");
        ArenaSession.Matches = true;
        Verify(Lookup() == level.gameObject, "exact confirmed replacement activation");
        level.gameObject.scene.isLoaded = false;
        Verify(Lookup() == null, "unloading scene");
        level.gameObject.scene.isLoaded = true;
        level.isActiveAndEnabled = false;
        Verify(Lookup() == null, "disabled settings");
        Singleton<LevelSettings>.Instance = null;
        Verify(Lookup() == null, "missing real LevelSettings retains native behavior");
        Singleton<LevelSettings>.Instance = level;
        level.isActiveAndEnabled = true;
        Redeploy.Core.Compat.FikaCompatCheck.IsFikaLoaded = true;
        Verify(Lookup() == null, "Fika excluded");
    }

    private static void Verify(bool value, string description)
    {
        if (!value) throw new Exception(description);
        _checks++;
    }
}
