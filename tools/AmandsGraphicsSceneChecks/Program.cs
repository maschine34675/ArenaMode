using System.Reflection;
using System.Reflection.Emit;
using ArenaMode;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
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

    // The Arena maps are the public "...Id" string constants of the catalogue. Reflecting over them (not a literal list)
    // makes a twelfth map break this harness until someone decides whether Amand's lookup must cover it.
    private static string[] CatalogIds() => typeof(ArenaMapCatalog).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith("Id") && f.Name != nameof(ArenaMapCatalog.ArenaMapPortPluginId))
        .Select(f => (string)f.GetRawConstantValue()).ToArray();

    private static void CheckLookup()
    {
        var world = Singleton<GameWorld>.Instance = new GameWorld { LocationId = "labyrinth" };
        var game = new LocalGame { GameWorld = world };
        Singleton<AbstractGame>.Instance = game;
        var level = Singleton<LevelSettings>.Instance = new LevelSettings();
        level.gameObject.Component = level;
        level.gameObject.name = "---Level_levelsettings---";
        var fps = CameraManager.Instance.Camera.gameObject;
        var log = Plugin.ModLogger.Lines;
        GameObject Lookup() => FindLevel("!settings", fps);
        // The loading-window state of a native Arena arm on the given map: every identity the gate compares agrees.
        void Arm(string map)
        {
            ArenaSession.IsActive = false;
            ArenaSession.Matches = false;
            ArenaSession.ArmedForNextRaid = true;
            world.LocationId = ArenaSession.ArmedMap = game.Settings.location = map;
            ArenaServerClient.PendingArmToken = "current";
            game.Settings.serverId = "start-current";
        }
        // The confirmed raid: IsActive with an exact-raid match; the armed fields are left behind on purpose, they must not matter.
        void Activate(string map)
        {
            ArenaSession.ArmedForNextRaid = false;
            ArenaSession.IsActive = true;
            ArenaSession.Matches = true;
            world.LocationId = map;
        }
        void Ordinary()
        {
            ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = ArenaSession.Matches = false;
            world.LocationId = "labyrinth";
        }

        Verify(Lookup() == null, "ordinary raid preserves upstream missing lookup");
        var native = CompileLookup(false);
        var patched = CompileLookup(true);
        try { patched(null, fps); throw new Exception("ordinary missing-object behavior was overridden"); }
        catch (NullReferenceException) { _checks++; }

        // Ordinary raid: the Arena state is fully valid except that neither flag is set. On Labyrinth and on a supported
        // non-Labyrinth map the lookup must stay missing (this is what pins the Arena gate, the loading arm alone says yes).
        foreach (var map in new[] { "labyrinth", ArenaMapCatalog.EquatorId, ArenaMapCatalog.CustomsId })
        {
            Arm(map);
            ArenaSession.ArmedForNextRaid = false;
            Verify(Lookup() == null, "ordinary raid with a valid-looking arm but no Arena flag preserves the missing lookup on " + map);
        }
        Ordinary();

        Arm("labyrinth");
        try { native(null, fps); throw new Exception("native missing-name failure was not reproduced"); }
        catch (NullReferenceException) { _checks++; }
        Verify(patched(null, fps) == level && levelSettings == level, "patched native instruction sequence assigns the actual level component");
        Verify(Lookup() == level.gameObject, "first armed activation resolves native LevelSettings");
        Verify(log.Any(x => x.Contains("Arena branch") && x.Contains("labyrinth") && x.Contains(level.gameObject.name)), "substitution names branch, map and object");
        Verify(FindLevel("---Anything_levelsettings---", fps) == level.gameObject, "no name filter: any name Amand asked for is repaired when the native lookup yields nothing");

        // Every Arena map, armed and confirmed. Armed: every identity names the same id, otherwise even the positive case
        // would fail for the wrong reason.
        var ids = CatalogIds();
        Verify(ids.Length == 12 && ids.All(ArenaMapCatalog.IsSupportedMap), "the catalogue holds exactly the twelve reviewed Arena maps, all supported");
        foreach (var id in ids)
        {
            Arm(id);
            Verify(Lookup() == level.gameObject, "armed first activation resolves on " + id);
            Activate(id);
            Verify(Lookup() == level.gameObject, "confirmed activation resolves on " + id);
        }

        // Unsupported ids. Active path: the exact-raid stand-in ignores the map, so the map gate is the only blocker.
        // Armed path: armed map, native location and world all name the id, so the loading arm accepts it on its own.
        foreach (var id in new[] { "shoreline", "sandbox", "", null })
        {
            Activate(id);
            Verify(Lookup() == null, "unsupported map '" + id + "' stays untouched in the confirmed raid");
            Arm(id);
            Verify(Lookup() == null, "unsupported map '" + id + "' stays untouched while loading");
        }

        // Loading arm identities: each of the three must hold.
        Arm("arena_bay5");
        ArenaSession.ArmedMap = "arena_equator";
        Verify(Lookup() == null, "armed map differing from the world's map");
        Arm("arena_bay5");
        game.Settings.location = "arena_equator";
        Verify(Lookup() == null, "native settings location differing from the world's map");
        Arm("labyrinth");
        game.Settings.serverId = "start-previous";
        Verify(Lookup() == null, "stale native start token");
        game.Settings.serverId = "end-previous";
        Verify(Lookup() == null, "stale native end token");
        game.Settings.serverId = "end-current";
        Verify(Lookup() == level.gameObject, "bound native end identity during loading");
        game.Settings.serverId = "start-current";
        Verify(Lookup() == level.gameObject, "armed state restored");

        // Amand's own lookup wins whenever it yields a LevelSettings, on a known-scene map with Arena fully armed.
        Arm(ArenaMapCatalog.CustomsId);
        var own = new LevelSettings();
        var named = GameObject.Named = new GameObject { name = "---Custom_levelsettings---", Component = own };
        Verify(Lookup() == named, "Amand's own object that carries a LevelSettings wins over the live singleton");
        Verify(patched(null, fps) == own && levelSettings == own, "patched native sequence keeps Amand's own component");
        Verify(log.Any(x => x.Contains("native branch")), "native branch is named while Arena is armed");
        // The line belongs to a raid: armed or active alone (a hideout activation while the next raid is armed) names nothing.
        log.Clear();
        var hideout = game.GameWorld;
        game.GameWorld = new GameWorld { LocationId = world.LocationId };
        Verify(Lookup() == named && !log.Any(x => x.Contains("native branch")), "native branch is not named outside the local game's own world");
        game.GameWorld = hideout;
        world.LocationId = "shoreline";
        Verify(Lookup() == named && !log.Any(x => x.Contains("native branch")), "native branch is not named on a map Arena does not cover");
        world.LocationId = ArenaMapCatalog.CustomsId;
        Verify(Lookup() == named && log.Any(x => x.Contains("native branch")), "native branch is named again once world and map agree");
        // An existing object without a LevelSettings is treated like a missing one.
        named.Component = null;
        Verify(Lookup() == level.gameObject, "named object without a LevelSettings is replaced by the live one");
        named.Component = own;
        // Ordinary raid with Amand's own object: untouched and silent.
        Ordinary();
        log.Clear();
        Verify(Lookup() == named, "ordinary raid keeps Amand's own object");
        Verify(log.Count == 0, "ordinary raid logs nothing");
        GameObject.Named = null;

        Verify(FindLevel("!settings", new GameObject()) == null && Lookup() == null, "foreign camera");
        Arm("labyrinth");
        Verify(FindLevel("!settings", new GameObject()) == null, "foreign camera while armed");
        game.GameWorld = new GameWorld();
        Verify(Lookup() == null, "different physical world");
        game.GameWorld = world;
        Activate("labyrinth");
        ArenaSession.Matches = false;
        Verify(Lookup() == null, "stale active world");
        ArenaSession.Matches = true;
        Verify(Lookup() == level.gameObject, "exact confirmed replacement activation");

        // Camera guards: a missing argument must not match a missing live camera object.
        CameraManager.Instance.Camera.NullObject = true;
        Verify(FindLevel("!settings", null) == null, "absent camera object");
        CameraManager.Instance.Camera.NullObject = false;

        // The live LevelSettings must be usable.
        level.gameObject.scene.isLoaded = false;
        Verify(Lookup() == null, "unloading scene");
        level.gameObject.scene.isLoaded = true;
        level.isActiveAndEnabled = false;
        Verify(Lookup() == null, "disabled settings");
        level.isActiveAndEnabled = true;
        var other = new LevelSettings();
        level.gameObject.Component = other;
        Verify(Lookup() == null, "component mismatch: GetComponent would not return the singleton");
        level.gameObject.Component = level;
        Singleton<LevelSettings>.Instance = null;
        log.Clear();
        Verify(Lookup() == null, "missing real LevelSettings retains native behavior");
        Verify(log.Any(x => x.Contains("no usable live LevelSettings")), "missing LevelSettings names the reason");
        Singleton<LevelSettings>.Instance = level;
        Verify(Lookup() == level.gameObject, "all guards restored");

        // Never the thrower.
        CameraManager.Instance.Camera.ThrowOnRead = true;
        GameObject thrown = null;
        bool escaped = false;
        try { thrown = FindLevel("!settings", fps); } catch (Exception) { escaped = true; }
        CameraManager.Instance.Camera.ThrowOnRead = false;
        Verify(!escaped && thrown == null, "an exception inside the Arena lookup ends as Amand's own result");
        Verify(log.Any(x => x.Contains("InvalidOperationException")), "the swallowed exception is named in the Debug log");
        // The native branch is inside the same net: Graphics' own object comes back even when reading its component throws.
        Arm(ArenaMapCatalog.CustomsId);
        GameObject.Named = named;
        GameObject.ThrowOnGetComponent = true;
        log.Clear();
        GameObject nativeThrown = null;
        bool nativeEscaped = false;
        try { nativeThrown = FindLevel("!settings", fps); } catch (Exception) { nativeEscaped = true; }
        GameObject.ThrowOnGetComponent = false;
        GameObject.Named = null;
        Verify(!nativeEscaped && nativeThrown == named, "an exception inside the native branch ends as Amand's own result");
        Verify(log.Any(x => x.Contains("InvalidOperationException")), "the exception swallowed in the native branch is named in the Debug log");

        Redeploy.Core.Compat.FikaCompatCheck.IsFikaLoaded = true;
        Verify(Lookup() == null, "Fika excluded");
        Redeploy.Core.Compat.FikaCompatCheck.IsFikaLoaded = false;
    }

    private static void Verify(bool value, string description)
    {
        if (!value) throw new Exception(description);
        _checks++;
    }
}
