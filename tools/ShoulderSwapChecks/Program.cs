using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Mono.Cecil;
using hazelify.StanceSync.Patches;

internal static class Program
{
    private const string Guid = "hazelify.StanceSync";
    private const string Owner = "com.maschine.ArenaMode.ShoulderSwap";
    private static int _checks;
    private static readonly MethodInfo Lean = AccessTools.DeclaredMethod(typeof(OnLeanPatchPostfix), "Postfix");
    private static readonly MethodInfo Camera = AccessTools.DeclaredMethod(typeof(PlayerCameraControllerPatchPrefix), "Prefix");
    private static readonly Func<Player, float, bool> BeforeLean = (Func<Player, float, bool>)Delegate.CreateDelegate(typeof(Func<Player, float, bool>),
        AccessTools.DeclaredMethod(typeof(ArenaShoulderSwapCompat), "BeforeLean"));
    private static readonly Func<PlayerCameraController, bool> BeforeCamera = (Func<PlayerCameraController, bool>)Delegate.CreateDelegate(typeof(Func<PlayerCameraController, bool>),
        AccessTools.DeclaredMethod(typeof(ArenaShoulderSwapCompat), "BeforeCamera"));

    private static void Main(string[] args)
    {
        if (args.Length == 1) CheckPublished(args[0]);
        CheckBinding();
        CheckCallbacks();
        ArenaShoulderSwapCompat.Shutdown();
        Check(!Owned(Lean) && !Owned(Camera), "shutdown removes both owned prefixes");
        Console.WriteLine($"SHOULDER_SWAP_CHECKS_PASSED ({_checks} assertions)");
    }

    private static void CheckPublished(string dll)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(dll);
        var plugin = assembly.MainModule.GetType("hazelify.StanceSync.Plugin");
        var metadata = plugin.CustomAttributes.Single(x => x.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)metadata.ConstructorArguments[0].Value == Guid && (string)metadata.ConstructorArguments[2].Value == "1.1.1", "actual published GUID/version");
        foreach (var target in new[]
        {
            (Type: "hazelify.StanceSync.Patches.OnLeanPatchPostfix", Method: "Postfix", Parameters: new[] { "EFT.Player", "System.Single" }),
            (Type: "hazelify.StanceSync.Patches.PlayerCameraControllerPatchPrefix", Method: "Prefix", Parameters: new[] { "EFT.CameraControl.PlayerCameraController" })
        })
        {
            var method = assembly.MainModule.GetType(target.Type).Methods.Single(x => x.Name == target.Method);
            Check(method.IsStatic && method.ReturnType.FullName == "System.Void" && method.HasBody && !method.HasGenericParameters
                && method.Parameters.Select(x => x.ParameterType.FullName).SequenceEqual(target.Parameters), "published callback signature: " + target.Method);
            Check(method.Body.Instructions.Any(x => x.Operand is MethodReference m && m.Name == "get_CurrentScope"), "published unguarded scope read: " + target.Method);
        }
    }

    private static void CheckBinding()
    {
        Check(ArenaShoulderSwapCompat.GetRejectionReason() == null && !Owned(Lean), "absent plugin leaves patch state untouched");
        var info = new PluginInfo { Metadata = new BepInPlugin(Guid, Guid, "1.1.1") };
        Chainloader.PluginInfos.Add(Guid, info);
        Check(ArenaShoulderSwapCompat.GetRejectionReason() != null && !Owned(Lean), "not loaded yet is refused without patching");
        info.Instance = new BaseUnityPlugin();
        var rejection = ArenaShoulderSwapCompat.GetRejectionReason();
        Check(rejection != null && !Owned(Lean) && !Owned(Camera), "wrong plugin type is refused");
        Check(ArenaShoulderSwapCompat.GetRejectionReason() == rejection, "same rejected instance remains cached");
        info.Instance = new hazelify.StanceSync.Plugin();
        info.Metadata = new BepInPlugin(Guid, Guid, "1.2.0");
        Check(ArenaShoulderSwapCompat.GetRejectionReason() != null, "metadata spoof does not receive guard");
        info.Metadata = new BepInPlugin(Guid, Guid, "1.1.1");
        Check(ArenaShoulderSwapCompat.GetRejectionReason() != null, "refused instance is not retried every display pass");
        info.Instance = new hazelify.StanceSync.Plugin();
        Check(ArenaShoulderSwapCompat.GetRejectionReason() == null && Owned(Lean) && Owned(Camera), "loaded reviewed contract receives both prefixes");
        Check(ArenaShoulderSwapCompat.GetRejectionReason() == null && Harmony.GetPatchInfo(Lean).Prefixes.Count(x => x.owner == Owner) == 1,
            "successful admission does not duplicate guards");
    }

    private static void CheckCallbacks()
    {
        var player = new Player();
        var world = Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = player };
        var game = new LocalGame { GameWorld = world, LocalPlayer = player };
        Singleton<AbstractGame>.Instance = game;
        var camera = Singleton<PlayerCameraController>.Instance = new PlayerCameraController { Player = player };
        ArenaSession.Game = game; ArenaSession.World = world; ArenaSession.IsActive = true;
        player.ToggleLean(-1f); camera.LateUpdate();
        Check(player.NativeLeans == 1 && camera.NativeUpdates == 1 && ForeignCalls.Lean == 1 && ForeignCalls.Camera == 1,
            "valid firearm native calls and foreign callbacks both execute");
        player.HandsController = new object();
        player.ProceduralWeaponAnimation.ScopeAimTransforms.Clear();
        player.ToggleLean(-1f); camera.LateUpdate();
        Check(player.NativeLeans == 2 && camera.NativeUpdates == 2 && ForeignCalls.Lean == 1 && ForeignCalls.Camera == 1,
            "knife keeps native lean/camera while skipping unsafe foreign callbacks");
        player.HandsController = new Player.FirearmController();
        player.ToggleLean(-1f); camera.LateUpdate();
        Check(ForeignCalls.Lean == 1 && ForeignCalls.Camera == 1, "firearm draw with empty scopes is held safely");
        player.ProceduralWeaponAnimation.ScopeAimTransforms.Add(new object());
        player.ProceduralWeaponAnimation.AimIndex = -1;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "negative transient aim index excluded");
        player.ProceduralWeaponAnimation.AimIndex = 15;
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "native modulo indexing retained");
        player.ProceduralWeaponAnimation.ScopeAimTransforms[0] = null;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "null sight excluded");
        player.ProceduralWeaponAnimation.ScopeAimTransforms = null;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "missing sight list excluded");
        player.ProceduralWeaponAnimation = null;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "missing PWA excluded");
        player.ProceduralWeaponAnimation = new();
        player.HandsController = null;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "empty hands excluded");
        player.HandsController = new Player.FirearmController();
        player.HealthController.IsAlive = false;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "dead body callbacks held");
        player.HealthController = null;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "missing health controller held");
        player.HealthController = new(); player.MovementContext = null;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "missing movement context held");
        player.MovementContext = new(); player.CameraPosition = null;
        Check(!BeforeLean(player, -1f) && !BeforeCamera(camera), "missing camera position held");
        player.CameraPosition = new(); player.HandsController = new object();

        // Invalid hands make any accidental broader ownership visible: each must pass through.
        ArenaSession.IsActive = false;
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "ordinary PMC/Scav path passes through");
        ExpectFailure(() => player.ToggleLean(-1f), "ordinary foreign knife behavior is unchanged");
        ArenaSession.IsActive = true;
        ArenaSession.Game = new LocalGame();
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "wrong active game passes through");
        ArenaSession.Game = game; ArenaSession.World = new GameWorld();
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "wrong active world passes through");
        ArenaSession.World = world; game.GameWorld = new GameWorld();
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "game/world mismatch passes through");
        game.GameWorld = world; game.LocalPlayer = new Player();
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "successor not yet committed passes through");
        game.LocalPlayer = player;
        Check(BeforeLean(new Player(), -1f), "unrelated physical body is untouched");
        Check(BeforeCamera(new PlayerCameraController { Player = player }), "detached camera is untouched");
        camera.Player = new Player();
        Check(BeforeCamera(camera), "camera bound to another body is untouched");
        camera.Player = player; player.IsAI = true;
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "AI excluded");
        player.IsAI = false; player.IsYourPlayer = false;
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "non-local player excluded");
        player.IsYourPlayer = true; Redeploy.Core.Compat.FikaCompatCheck.IsFikaLoaded = true;
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "Fika excluded");
        Redeploy.Core.Compat.FikaCompatCheck.IsFikaLoaded = false;

        var successor = new Player();
        world.MainPlayer = game.LocalPlayer = successor;
        var replacementCamera = Singleton<PlayerCameraController>.Instance = new PlayerCameraController { Player = successor };
        Check(BeforeLean(successor, -1f) && BeforeCamera(replacementCamera), "replacement body works without a cached old player");
        successor.HandsController = new object();
        Check(!BeforeLean(successor, -1f) && !BeforeCamera(replacementCamera), "replacement knife also protected");
        Check(BeforeLean(player, -1f) && BeforeCamera(camera), "retired body's callbacks are not mistaken for current body");
    }

    private static bool Owned(MethodInfo method) => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true;
    private static void ExpectFailure(Action action, string description)
    {
        try { action(); throw new Exception(description); }
        catch (InvalidOperationException) { _checks++; }
    }
    private static void Check(bool success, string description)
    { if (!success) throw new Exception(description); _checks++; }
}
