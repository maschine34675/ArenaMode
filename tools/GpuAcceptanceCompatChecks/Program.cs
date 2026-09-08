using System.Reflection;
using ArenaMode;
using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using GPUInstancer;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Need(bool ok, string message)
    {
        _checks++;
        if (!ok) throw new Exception("ASSERT: " + message);
    }
    private static (GPUInstancerManager manager, Camera current) Reset()
    {
        ArenaGpuAcceptanceCompat.Shutdown();
        var player = new Player();
        var world = new GameWorld { MainPlayer = player };
        var game = new LocalGame { Status = GameStatus.Started, GameWorld = world };
        Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = game;
        Singleton<PlayerCameraController>.Instance = new PlayerCameraController { Player = player };
        ArenaSession.Active = true; ArenaSession.BoundGame = game; ArenaSession.BoundWorld = world;
        var current = new Camera();
        CameraManager.Instance = new CameraManager { Camera = current };
        GPUInstancerManager.TextureWidth = 1114; GPUInstancerManager.TextureHeight = 548;
        GPUInstancerManager.Cleared = false; GPUInstancerManager.SharedDraws.Clear();
        Plugin.ModLogger.Lines.Clear();
        return (new GPUInstancerManager { Camera = new Camera { Destroyed = true, Width = 1114, Height = 548 } }, current);
    }
    private static void Unchanged(string label, Action<GPUInstancerManager, Camera> setup)
    {
        var (manager, current) = Reset(); setup(manager, current);
        Camera before = manager.Camera;
        ArenaGpuAcceptanceCompat.BeforeGpuUpdate(manager);
        Need(manager.Rebinds == 0 && ReferenceEquals(manager.Camera, before), label);
    }
    private static void RenderChecks()
    {
        var (manager, current) = Reset();
        manager.Update();
        Need(!manager.RenderDimensionsMatch, "native late reacquire reproduces first-frame mismatch");
        manager.Update();
        Need(manager.RenderDimensionsMatch, "native next update repairs the dimensions");

        (manager, current) = Reset();
        ArenaGpuAcceptanceCompat.BeforeGpuUpdate(manager);
        Need(manager.Rebinds == 1 && ReferenceEquals(manager.Camera, current), "replacement camera binds before native update");
        Need(GPUInstancerManager.TextureWidth == 1114, "compat does not allocate or set render dimensions");
        manager.Update();
        Need(manager.RenderDimensionsMatch, "first replacement render has matching dimensions");
        current.Width = 1114; current.Height = 548; // SSAA commits upscale rect after first render.
        ArenaGpuAcceptanceCompat.BeforeGpuUpdate(manager); manager.Update();
        Need(manager.Rebinds == 1 && manager.RenderDimensionsMatch, "native SSAA change remains authoritative");
        Need(Plugin.ModLogger.Lines.Count == 1, "stable camera avoids repeat diagnosis");

        (manager, current) = Reset();
        var trees = new GPUInstancerManager { Name = "trees", Camera = manager.Camera };
        ArenaGpuAcceptanceCompat.BeforeGpuUpdate(manager); manager.Update();
        ArenaGpuAcceptanceCompat.BeforeGpuUpdate(trees); trees.Update();
        Need(GPUInstancerManager.SharedDraws.SequenceEqual(new[] { "grass", "trees" }), "multiple managers retain all shared draws");
        Need(Plugin.ModLogger.Lines.Count == 1, "replacement diagnosis once per camera across managers");

        Unchanged("ordinary raid remains untouched", (m,c) => ArenaSession.Active = false);
        Unchanged("stale game remains untouched", (m,c) => ArenaSession.BoundGame = new LocalGame());
        Unchanged("stale world remains untouched", (m,c) => ArenaSession.BoundWorld = new GameWorld());
        Unchanged("loading game remains untouched", (m,c) => ((LocalGame)Singleton<AbstractGame>.Instance).Status = GameStatus.Running);
        Unchanged("stopping game remains untouched", (m,c) => ((LocalGame)Singleton<AbstractGame>.Instance).Status = GameStatus.Stopping);
        Unchanged("wrong game world remains untouched", (m,c) => ((LocalGame)Singleton<AbstractGame>.Instance).GameWorld = new GameWorld());
        Unchanged("optic manager remains untouched", (m,c) => m.IsOptic = true);
        Unchanged("unassigned manager remains native", (m,c) => m.Camera = null);
        Unchanged("live foreign camera remains untouched", (m,c) => m.Camera = new Camera());
        Unchanged("live current camera remains untouched", (m,c) => m.Camera = c);
        Unchanged("unbound camera controller remains untouched", (m,c) => Singleton<PlayerCameraController>.Instance = null);
        Unchanged("wrong local body remains untouched", (m,c) => Singleton<PlayerCameraController>.Instance.Player = new Player());
        Unchanged("missing player remains untouched", (m,c) => Singleton<GameWorld>.Instance.MainPlayer = null);
        Unchanged("inactive current camera remains untouched", (m,c) => c.isActiveAndEnabled = false);
        Unchanged("missing camera manager remains untouched", (m,c) => CameraManager.Instance = null);
        Unchanged("missing current camera remains untouched", (m,c) => CameraManager.Instance.Camera = null);
        Unchanged("destroyed current camera remains untouched", (m,c) => c.Destroyed = true);
        (manager, current) = Reset(); manager.ThrowOnRebind = true;
        ArenaGpuAcceptanceCompat.BeforeGpuUpdate(manager); ArenaGpuAcceptanceCompat.BeforeGpuUpdate(manager);
        Need(Plugin.ModLogger.Lines.Count == 1, "repair exception is contained and reported once");
    }
    private sealed class WarmupHost : MonoBehaviour
    {
        internal int Started, Built;
        internal bool Pending;
        internal void Queue()
        {
            if (!ArenaGpuAcceptanceCompat.BeforePftWarmup(this)) return;
            if (Pending) return;
            if (!isActiveAndEnabled) throw new InvalidOperationException("inactive host");
            Pending = true; Started++;
        }
        internal void OpenSettings() { Built++; }
    }
    private static void MenuChecks()
    {
        Reset(); var host = new WarmupHost { isActiveAndEnabled = false };
        Need(!ArenaGpuAcceptanceCompat.BeforePftWarmup(host), "inactive Arena host rejects original coroutine scheduling");
        host.Queue();
        Need(host.Started == 0 && !host.Pending, "inactive Arena menu warmup is skipped without occupying pending state");
        host.Queue();
        Need(Plugin.ModLogger.Lines.Count == 1, "inactive-menu diagnostic remains bounded");
        host.isActiveAndEnabled = true; host.Queue();
        Need(host.Started == 1 && host.Pending, "active Show retry starts original warmup");
        host.OpenSettings(); Need(host.Built == 1, "explicit settings build is not suppressed");
        Reset(); host = new WarmupHost { isActiveAndEnabled = false }; ArenaSession.Active = false;
        Need(ArenaGpuAcceptanceCompat.BeforePftWarmup(host), "ordinary PFT menu remains untouched");
        Reset(); host = new WarmupHost { isActiveAndEnabled = false }; ArenaSession.BoundWorld = new GameWorld();
        Need(ArenaGpuAcceptanceCompat.BeforePftWarmup(host), "foreign raid menu remains untouched");
        Reset(); ((LocalGame)Singleton<AbstractGame>.Instance).Status = GameStatus.Stopping;
        Need(ArenaGpuAcceptanceCompat.BeforePftWarmup(host), "teardown menu remains untouched");
        Reset(); Need(ArenaGpuAcceptanceCompat.BeforePftWarmup(null), "null optional host remains native");
        Need(ArenaGpuAcceptanceCompat.BeforePftWarmup(new object()), "foreign optional host type remains native");
    }
    private sealed class MethodShapes
    {
        public void Right() { }
        private void PrivateRight() { }
        public int WrongReturn() => 0;
        public static void Static() { }
        public void Parameter(int value) { }
        public void Generic<T>() { }
    }
    private static void BindingChecks()
    {
        Reset(); ArenaGpuAcceptanceCompat.Enable(); ArenaGpuAcceptanceCompat.Enable();
        Need(HarmonyLib.Harmony.Installed.Count == 1, "optional PFT absence and repeated Enable install one native hook");
        var hook = HarmonyLib.Harmony.Installed.Single();
        Need(hook.target.DeclaringType == typeof(GPUInstancerManager) && hook.target.Name == "Update"
            && hook.prefix.Method.Name == nameof(ArenaGpuAcceptanceCompat.BeforeGpuUpdate), "exact native Update hook binds production prefix");
        MethodInfo resolver = typeof(ArenaGpuAcceptanceCompat).GetMethod("ExactMethod", BindingFlags.Static | BindingFlags.NonPublic);
        Need(resolver.Invoke(null, new object[] { typeof(MethodShapes), "Right", true }) is MethodInfo, "exact public method accepted");
        Need(resolver.Invoke(null, new object[] { typeof(MethodShapes), "PrivateRight", false }) is MethodInfo, "exact private method accepted");
        foreach (string name in new[] { "Missing", "WrongReturn", "Static", "Parameter", "Generic" })
        {
            bool rejected = false;
            try { resolver.Invoke(null, new object[] { typeof(MethodShapes), name, true }); }
            catch (TargetInvocationException ex) when (ex.InnerException is MissingMethodException) { rejected = true; }
            Need(rejected, "drifted method rejected: " + name);
        }
        ArenaGpuAcceptanceCompat.Shutdown();
        Need(HarmonyLib.Harmony.Installed.Count == 0, "shutdown removes only owned hooks");
    }
    private static int Main()
    {
        try { RenderChecks(); MenuChecks(); BindingChecks(); Console.WriteLine($"GPU_ACCEPTANCE_COMPAT_CONTRACTS_PASSED: {_checks}"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
