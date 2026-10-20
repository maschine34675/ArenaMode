using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using UnityEngine;

public static class Program
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string Token = "67a438725fa04c4a9e52205baec1b521";
    private static int _checks;
    public static bool KeyPressed;
    public static int Pauses, Unpauses, FovTracks;
    public static float TimeScale = 1;
    private sealed record Fixture(Type Plugin, Type Controller, MethodInfo Update, BepInPlugin Metadata);
    private static void Need(bool ok, string name)
    { _checks++; if (!ok) throw new InvalidOperationException("ASSERT: " + name); }
    public static bool ReadKey(KeyboardShortcut key) => KeyPressed;
    public static void PauseNow() { Pauses++; TimeScale = 0; }
    public static void UnpauseNow() { Unpauses++; TimeScale = 1; }
    public static void TrackFov() => FovTracks++;

    private static Fixture Make(string version = "1.5.0", string mismatch = null, string assemblyName = "rejzbihai.Pause",
        bool badAttribute = false, bool extraActivation = false, bool awakePaused = false, bool missingKey = false)
    {
        var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName) { Version = new(mismatch ?? version) },
            AssemblyBuilderAccess.RunAndCollect);
        var module = asm.DefineDynamicModule("pause-fixture");
        var plugin = module.DefineType("Pause.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        var attr = new BepInPlugin(ArenaPauseContract.PluginGuid, "fixture", version);
        if (!badAttribute) plugin.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { attr.GUID, attr.Name, version }));
        var toggle = plugin.DefineField("TogglePause", typeof(ConfigEntry<KeyboardShortcut>), FieldAttributes.Public | FieldAttributes.Static);
        var controller = module.DefineType("Pause.PauseController", TypeAttributes.Public, typeof(MonoBehaviour));
        controller.DefineDefaultConstructor(MethodAttributes.Public);
        controller.DefineField("GameWorld", typeof(GameWorld), FieldAttributes.Private | FieldAttributes.Static);
        controller.DefineField("_abstractGame", typeof(AbstractGame), FieldAttributes.Private);
        controller.DefineField("MainPlayer", typeof(Player), FieldAttributes.Private | FieldAttributes.Static);
        var paused = controller.DefineField("_paused", typeof(bool), FieldAttributes.Private | FieldAttributes.Static);
        var getter = controller.DefineMethod("get_IsPaused", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            typeof(bool), Type.EmptyTypes);
        var il = getter.GetILGenerator(); il.Emit(OpCodes.Ldsfld, paused); il.Emit(OpCodes.Ret);
        var setter = controller.DefineMethod("set_IsPaused", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName,
            typeof(void), new[] { typeof(bool) });
        il = setter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stsfld, paused); il.Emit(OpCodes.Ret);
        var property = controller.DefineProperty("IsPaused", PropertyAttributes.None, typeof(bool), null);
        property.SetGetMethod(getter); property.SetSetMethod(setter);
        foreach (var methodName in new[] { "Awake", "OnDestroy" })
        {
            var method = controller.DefineMethod(methodName, MethodAttributes.Private, typeof(void), Type.EmptyTypes);
            il = method.GetILGenerator(); il.Emit(methodName == "Awake" && awakePaused ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, setter); il.Emit(OpCodes.Ret);
        }
        var pause = controller.DefineMethod("Pause", MethodAttributes.Private, typeof(void), Type.EmptyTypes);
        il = pause.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(PauseNow))); il.Emit(OpCodes.Ret);
        var unpause = controller.DefineMethod("Unpause", MethodAttributes.Private, typeof(void), Type.EmptyTypes);
        il = unpause.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(UnpauseNow))); il.Emit(OpCodes.Ret);
        var key = controller.DefineMethod("IsKeyPressed", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), new[] { typeof(KeyboardShortcut) });
        il = key.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(ReadKey))); il.Emit(OpCodes.Ret);
        var update = controller.DefineMethod("Update", MethodAttributes.Private, typeof(void), Type.EmptyTypes);
        il = update.GetILGenerator();
        var end = il.DefineLabel(); var release = il.DefineLabel();
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(TrackFov)));
        il.Emit(OpCodes.Ldsfld, toggle); il.Emit(OpCodes.Callvirt, typeof(ConfigEntry<KeyboardShortcut>).GetProperty("Value").GetGetMethod());
        if (missingKey) { il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_1); }
        else il.Emit(OpCodes.Call, key);
        il.Emit(OpCodes.Brfalse, end);
        il.Emit(OpCodes.Call, getter); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Call, setter);
        il.Emit(OpCodes.Call, getter); il.Emit(OpCodes.Brfalse, release);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, pause); il.Emit(OpCodes.Br, end);
        il.MarkLabel(release); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, unpause);
        il.MarkLabel(end); il.Emit(OpCodes.Ret);
        if (extraActivation)
        {
            var extra = controller.DefineMethod("OtherPause", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            il = extra.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, pause); il.Emit(OpCodes.Ret);
        }
        var pluginType = plugin.CreateType(); var controllerType = controller.CreateType();
        pluginType.GetField("TogglePause").SetValue(null, new ConfigEntry<KeyboardShortcut> { Value = new() { Key = 1 } });
        return new(pluginType, controllerType, controllerType.GetMethod("Update", Flags), attr);
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false; ArenaPauseCompat.Shutdown();
        typeof(ArenaPauseCompat).GetField("_reportedNewer", Flags).SetValue(null, false);
        Chainloader.PluginInfos.Clear(); Harmony.Bound.Clear(); Harmony.FailPatch = false;
        AccessTools.MissingSettings = LocalGameAccessor.FailRead = false;
        ArenaSession.ArmedForNextRaid = ArenaSession.IsActive = false;
        ArenaSession.ArmedMap = null; ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null; Singleton<IBotGame>.Instance = null;
        ArenaMode.Plugin.ModLogger.Info.Clear(); ArenaMode.Plugin.ModLogger.Warnings.Clear(); ArenaMode.Modes.ArenaToasts.Messages.Clear();
        KeyPressed = true; Pauses = Unpauses = FovTracks = 0; TimeScale = 1;
    }
    private static void Install(Fixture f)
    {
        Chainloader.PluginInfos[ArenaPauseContract.PluginGuid] = new()
        { Instance = (BaseUnityPlugin)Activator.CreateInstance(f.Plugin), Metadata = f.Metadata };
    }
    private static LocalGame World(bool armed = true)
    {
        var world = new GameWorld { LocationId = "bigmap" }; var profile = new Profile();
        world.MainPlayer = new Player { GameWorld = world, Profile = profile };
        var game = new LocalGame { GameWorld = world, Profile = profile, LocalPlayer = world.MainPlayer,
            _raidSettings = new() { location = "bigmap", playerSide = ESideType.Pmc, serverId = "ordinary" } };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world; Singleton<IBotGame>.Instance = game;
        ArenaSession.ArmedForNextRaid = armed; ArenaServerClient.HasPendingArm = armed;
        if (armed)
        {
            ArenaSession.ArmedMap = "bigmap"; ArenaServerClient.PendingArmToken = Token;
            game._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native", Token);
        }
        return game;
    }
    private static object Controller(Fixture f, LocalGame game)
    {
        var controller = (Component)Activator.CreateInstance(f.Controller);
        controller.gameObject = game.GameWorld.gameObject;
        f.Controller.GetField("GameWorld", Flags).SetValue(null, game.GameWorld);
        f.Controller.GetField("_abstractGame", Flags).SetValue(controller, game);
        f.Controller.GetField("MainPlayer", Flags).SetValue(null, game.LocalPlayer);
        return controller;
    }
    private static bool Call(Fixture f, object controller)
    {
        bool run = true;
        foreach (var patch in Harmony.GetPatchInfo(f.Update)?.Prefixes.ToArray() ?? Array.Empty<Patch>())
            if (patch.PatchMethod.Invoke(null, new[] { controller }) is false) run = false;
        if (run) f.Update.Invoke(controller, null);
        return run;
    }
    private static bool Paused(Fixture f) => (bool)f.Controller.GetProperty("IsPaused", Flags).GetValue(null);

    private static void Contracts()
    {
        foreach (string version in new[] { "1.5.0", "1.5.1", "2.0.0" })
        {
            Reset(); var f = Make(version); Install(f);
            Need(ArenaPauseCompat.GetRejectionReason() == null, "reviewed and forward contracts accepted");
            Need(ArenaPauseCompat.GetRejectionReason() == null && Harmony.GetPatchInfo(f.Update).Prefixes.Count == 1, "idempotent adapter");
            Need(ArenaMode.Plugin.ModLogger.Warnings.Count == (version == "1.5.0" ? 0 : 1), "forward warning once");
        }
        foreach (var f in new[] { Make("1.4.9"), Make(mismatch: "1.5.1"), Make(assemblyName: "wrong"),
            Make(badAttribute: true), Make(extraActivation: true), Make(awakePaused: true), Make(missingKey: true) })
        { Reset(); Install(f); Need(ArenaPauseCompat.GetRejectionReason() != null, "unsafe contract rejected"); }
        Reset(); Need(ArenaPauseCompat.GetRejectionReason() == null, "optional absence accepted");
        var fixture = Make(); Install(fixture); AccessTools.MissingSettings = true;
        Need(ArenaPauseCompat.GetRejectionReason() != null, "missing native settings rejected");
        Reset(); Install(fixture); Harmony.FailPatch = true;
        Need(ArenaPauseCompat.GetRejectionReason() != null && Harmony.GetPatchInfo(fixture.Update).Prefixes.Count == 0, "partial hook cleaned");
        Reset(); Install(fixture); Need(ArenaPauseCompat.GetRejectionReason() == null, "valid adapter installed");
        Harmony.Bound.Clear();
        Need(ArenaPauseCompat.GetRejectionReason() != null, "removed hook rejects admission");
        Reset(); Chainloader.PluginInfos[ArenaPauseContract.PluginGuid] = new();
        Need(ArenaPauseCompat.GetRejectionReason() != null, "uninitialized plugin rejected");
    }

    private static void Behaviors()
    {
        Reset(); var f = Make(); Install(f); Need(ArenaPauseCompat.GetRejectionReason() == null, "behavior fixture installed");
        var game = World(); var controller = Controller(f, game);
        Need(!Call(f, controller) && !Paused(f) && TimeScale == 1 && Pauses == 0, "Arena request suppressed before flag and time mutation");
        Need(ArenaMode.Modes.ArenaToasts.Messages.Count == 1 && ArenaMode.Plugin.ModLogger.Info.Count == 1, "visible reason emitted once");
        Need(!Call(f, controller) && ArenaMode.Modes.ArenaToasts.Messages.Count == 1, "repeat request remains blocked without spam");
        KeyPressed = false;
        Need(Call(f, controller) && FovTracks == 1 && !Paused(f), "non-activation update retained");
        KeyPressed = true;
        f.Controller.GetProperty("IsPaused", Flags).SetValue(null, true); TimeScale = 0;
        Need(Call(f, controller) && !Paused(f) && TimeScale == 1 && Unpauses == 1, "Arena unpause always retained");
        game.LocalPlayer = game.GameWorld.MainPlayer = new Player { GameWorld = game.GameWorld, Profile = game.Profile };
        Need(!Call(f, controller) && !Paused(f), "replacement body with stale foreign cache remains protected");
        ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false;
        ArenaSession.IsActive = false;
        Need(!Call(f, controller), "same owned world protected through terminal cleanup");
        var ordinary = World(false); var ordinaryController = Controller(f, ordinary);
        Need(Call(f, ordinaryController) && Paused(f) && TimeScale == 0, "ordinary later raid can pause");
        Need(Call(f, ordinaryController) && !Paused(f) && TimeScale == 1, "ordinary later raid can unpause");
        Need(ArenaMode.Modes.ArenaToasts.Messages.Count == 1, "ordinary raid no extra notice");
        ArenaPauseCompat.Shutdown();
        Need(Harmony.GetPatchInfo(f.Update).Prefixes.Count == 0 && Call(f, ordinaryController) && Paused(f), "shutdown preserves foreign behavior");

        void Allowed(string label, Action<LocalGame, Fixture, object> change, bool armed = true)
        {
            Reset(); var test = Make(); Install(test); ArenaPauseCompat.Initialize();
            var g = World(armed); var c = Controller(test, g); change(g, test, c);
            Need(Call(test, c) && Paused(test) && TimeScale == 0, label);
        }
        Allowed("ordinary positive control", (_, _, _) => { }, false);
        Allowed("scav not captured", (g, _, _) => g._raidSettings.playerSide = ESideType.Savage);
        Allowed("map mismatch untouched", (g, _, _) => g._raidSettings.location = "factory4_day");
        Allowed("unconfirmed start untouched", (g, _, _) => g._raidSettings.serverId = Protocol.BuildNativeStartIdentity(Token));
        Allowed("missing pending arm untouched", (_, _, _) => ArenaServerClient.HasPendingArm = false);
        Allowed("wrong native world untouched", (g, _, _) => g.GameWorld = new GameWorld { LocationId = "bigmap" });
        Allowed("wrong controller object untouched", (_, _, c) => ((Component)c).gameObject = new GameObject());
        Allowed("wrong cached world untouched", (_, t, _) => t.Controller.GetField("GameWorld", Flags).SetValue(null, new GameWorld()));
        Allowed("wrong cached game untouched", (_, t, c) => t.Controller.GetField("_abstractGame", Flags).SetValue(c, new LocalGame()));
        Allowed("wrong bot game untouched", (_, _, _) => Singleton<IBotGame>.Instance = new LocalGame());
        Allowed("hideout untouched", (_, _, _) => Singleton<AbstractGame>.Instance = new HideoutGame());
        Allowed("early running without profile untouched", (g, _, _) => { g.Status = GameStatus.Running; g.Profile = null; });
        Allowed("loading profile mismatch untouched", (g, _, _) => { g.Status = GameStatus.Running; g.LocalPlayer.Profile = new Profile(); });
        Allowed("unknown identity fault does not change foreign raid", (_, _, _) => LocalGameAccessor.FailRead = true);
        Need(ArenaPauseCompat.GetRejectionReason() != null, "identity fault closes future admission");

        Reset(); f = Make(); Install(f); ArenaPauseCompat.Initialize(); game = World(); controller = Controller(f, game);
        game.Status = GameStatus.Running; game.LocalPlayer = game.GameWorld.MainPlayer = null; Singleton<IBotGame>.Instance = null;
        Need(!Call(f, controller) && !Paused(f), "server-confirmed loading protected before bot game");
        Reset(); f = Make(); Install(f); ArenaPauseCompat.Initialize(); game = World(false); controller = Controller(f, game);
        ArenaSession.IsActive = true; ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld;
        Need(!Call(f, controller), "exact active pair protected without pending arm");
        Reset(); f = Make(); Install(f); ArenaPauseCompat.Initialize(); game = World(false); controller = Controller(f, game);
        ArenaSession.IsActive = true; ArenaSession.ActiveGame = new LocalGame(); ArenaSession.ActiveWorld = game.GameWorld;
        Need(Call(f, controller), "global active alone not ownership");
    }

    public static int Main(string[] args)
    {
        try
        {
            Contracts(); Behaviors();
            if (args.Length == 2) { PublishedMetadata.Verify(args[0], Need); PublishedMetadata.VerifyNative(args[1], Need); }
            else throw new ArgumentException("Published client and native Assembly-CSharp paths required");
            Console.WriteLine($"PAUSE_COMPAT_CHECKS_PASSED ({_checks})"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Reset(); }
    }
}
