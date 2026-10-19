using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Operation = EFT.Player.MedsController.MedsInHandsOperation;

public static class Program
{
    private static int _checks;
    private static bool _foreignCancel;
    private static int _foreignStarts, _foreignCancels;
    private static void Need(bool condition, string label)
    { ++_checks; if (!condition) throw new InvalidOperationException("ASSERT: " + label); }

    // Sentinels make foreign execution observable. They intentionally are not a
    // medical algorithm: an admitted foreign end always vetoes native work.
    public static bool ForeignEnd(Operation op, IHealthEffect effect)
    { op.ForeignRuns++; if (_foreignCancel) op.Callbacks++; return false; }
    public static void ForeignStart(Player body) { _foreignStarts++; if (body.IsYourPlayer) _foreignCancel = false; }
    public static void ForeignCancel(Player body) { _foreignCancels++; if (body.IsYourPlayer) _foreignCancel = true; }

    private static bool RunPrefix(MethodInfo target, object value)
    {
        bool runForeign = true, result = false;
        foreach (var patch in Harmony.GetPatchInfo(target)?.Prefixes.ToArray() ?? Array.Empty<Patch>())
        {
            object[] args = target.ReturnType == typeof(bool) ? new[] { value, (object)result } : new[] { value, target };
            if (!(bool)patch.PatchMethod.Invoke(null, args)) runForeign = false;
            if (target.ReturnType == typeof(bool)) result = (bool)args[1];
        }
        if (runForeign)
        {
            var answer = target.Invoke(null, target.ReturnType == typeof(bool) ? new[] { value, new OtherEffect() } : new[] { value });
            if (target.ReturnType == typeof(bool)) result = (bool)answer;
        }
        return result;
    }

    private static Operation End(Fixture f, Player body, IHealthEffect effect)
    {
        var op = new Operation { _controller = new() { _player = body } };
        if (RunPrefix(f.Prefixes[0], op)) op.OnEffectRemoved(effect);
        return op;
    }

    private static void EndCheck(Fixture f, Player body, bool bypass, string label)
    {
        _foreignCancel = true;
        var op = End(f, body, new MedEffect());
        Need(op.ForeignRuns == (bypass ? 0 : 1), label + " foreign gate");
        Need(op.NativeRuns == (bypass ? 1 : 0), label + " native fallback");
        Need(op.Callbacks == 1, label + " single completion callback");
        op = End(f, body, new OtherEffect());
        Need(op.NativeRuns == (bypass ? 1 : 0) && op.Callbacks == (bypass ? 0 : 1), label + " non-med effect");
        Need(_foreignCancel, label + " foreign global flag untouched");
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false;
        ArenaContinuousHealingCompat.Shutdown();
        Harmony.Bound.Clear(); Chainloader.PluginInfos.Clear();
        Harmony.DropReceipt = false; Harmony.FailPatchAt = Harmony.PatchCalls = 0;
        ArenaSession.IsActive = false; ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        Singleton<AbstractGame>.Instance = null; Singleton<IBotGame>.Instance = null; Singleton<GameWorld>.Instance = null;
        ArenaMode.Plugin.ModLogger.Warnings.Clear(); ArenaMode.Plugin.ModLogger.Debug.Clear();
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = false;
        ArenaMode.Diagnostics.ArenaLog.Messages.Clear();
        _foreignStarts = _foreignCancels = 0; _foreignCancel = false;
    }

    private static LocalGame World(bool active)
    {
        var game = new LocalGame { GameWorld = new() };
        game.GameWorld.MainPlayer = new() { GameWorld = game.GameWorld, IsYourPlayer = true };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = game.GameWorld; Singleton<IBotGame>.Instance = game;
        if (active) { ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld; ArenaSession.IsActive = true; }
        return game;
    }

    private static void ScopeCases()
    {
        Reset(); var f = FixtureFactory.Make(); f.Register(); ArenaContinuousHealingCompat.Initialize();
        Need(ArenaContinuousHealingCompat.GetRejectionReason() == null, "reviewed API admitted");
        var ordinary = World(false);
        EndCheck(f, ordinary.GameWorld.MainPlayer, false, "ordinary local PMC or Scav");
        EndCheck(f, new() { GameWorld = ordinary.GameWorld }, false, "ordinary bot");
        var game = World(true); var local = game.GameWorld.MainPlayer;
        EndCheck(f, local, false, "current Arena local body");
        var bot = new Player { GameWorld = game.GameWorld };
        EndCheck(f, bot, true, "Arena bot");
        EndCheck(f, new() { GameWorld = game.GameWorld }, true, "new Arena replacement bot");
        EndCheck(f, new() { GameWorld = ordinary.GameWorld }, false, "foreign body world");
        EndCheck(f, new(), false, "unowned body");

        // Preserve current-player cancellation and resets in the actual prefix seam.
        _foreignCancel = true; RunPrefix(f.Prefixes[1], local); Need(!_foreignCancel, "live local start reaches foreign reset");
        RunPrefix(f.Prefixes[2], local); Need(_foreignCancel, "live local cancel reaches foreign state");
        int starts = _foreignStarts, cancels = _foreignCancels;
        RunPrefix(f.Prefixes[1], bot); RunPrefix(f.Prefixes[2], bot);
        Need(_foreignStarts == starts && _foreignCancels == cancels, "bot start and cancel guarded");

        // Native MainPlayer remains the dead local until replacement publication.
        local.HealthController.Alive = false;
        EndCheck(f, local, true, "dead old MainPlayer");
        _foreignCancel = false; RunPrefix(f.Prefixes[2], local);
        Need(!_foreignCancel, "retired body cannot cancel replacement healing");
        _foreignCancel = true; RunPrefix(f.Prefixes[1], local);
        Need(_foreignCancel, "retired body cannot reset replacement healing");
        var replacement = new Player { GameWorld = game.GameWorld, IsYourPlayer = true };
        game.GameWorld.MainPlayer = replacement;
        EndCheck(f, replacement, false, "replacement local continues foreign healing");
        local.HealthController.Alive = true; EndCheck(f, local, true, "retired living same-profile body");

        // Capture through start alone; no prior end/cancel must be necessary.
        var startOnly = new Player { GameWorld = game.GameWorld };
        RunPrefix(f.Prefixes[1], startOnly);
        ArenaSession.IsActive = false; ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        EndCheck(f, startOnly, true, "delayed callback after session cleanup");
        var next = World(false);
        EndCheck(f, next.GameWorld.MainPlayer, false, "next ordinary local");
        EndCheck(f, new() { GameWorld = next.GameWorld }, false, "next ordinary bot");
        EndCheck(f, replacement, true, "previous local callback during ordinary reentry");
        EndCheck(f, startOnly, true, "previous bot callback during ordinary reentry");
        startOnly.GameWorld = next.GameWorld;
        EndCheck(f, startOnly, false, "reused body loses old-world ownership");

        // Global active state alone is not ownership of a fresh world.
        ArenaSession.IsActive = true; ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld;
        EndCheck(f, new() { GameWorld = next.GameWorld }, false, "stale active pair");
        var correct = World(true);
        Singleton<GameWorld>.Instance = new();
        ArenaSession.ActiveWorld = Singleton<GameWorld>.Instance;
        EndCheck(f, new() { GameWorld = Singleton<GameWorld>.Instance }, false, "mismatched game world");
        Singleton<GameWorld>.Instance = correct.GameWorld; ArenaSession.ActiveWorld = correct.GameWorld;
        Singleton<IBotGame>.Instance = new LocalGame();
        EndCheck(f, new() { GameWorld = correct.GameWorld }, false, "mismatched bot game");
        Singleton<IBotGame>.Instance = correct;
        EndCheck(f, new() { GameWorld = correct.GameWorld, IsYourPlayer = true }, true, "foreign IsYourPlayer body is not local authority");

        var scope = new ArenaContinuousHealingScope(); var bodyKey = new object(); var worldKey = new object();
        Need(scope.Owns(bodyKey, worldKey, worldKey, true), "weak body lease acquired");
        scope.Clear(); Need(!scope.Owns(bodyKey, worldKey, worldKey, false), "scope cleanup releases ownership");
        ArenaContinuousHealingCompat.Shutdown(); EndCheck(f, bot, false, "shutdown removes guard");
    }

    private static void DiagnosticCases()
    {
        Reset(); var f = FixtureFactory.Make(); f.Register(); ArenaContinuousHealingCompat.Initialize();
        var game = World(true); var local = game.GameWorld.MainPlayer; var bot = new Player { GameWorld = game.GameWorld };
        EndCheck(f, bot, true, "diagnostics disabled keeps protection");
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count == 0, "diagnostics default quiet");
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = true;
        for (int i = 0; i < 12; i++)
        {
            End(f, local, new MedEffect()); End(f, bot, new MedEffect());
            foreach (int seam in new[] { 1, 2 }) { RunPrefix(f.Prefixes[seam], local); RunPrefix(f.Prefixes[seam], bot); }
        }
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count(m => m.Contains("current local body")) == 3, "current-local reports bounded");
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count(m => m.Contains("Arena bot")) == 3, "guard reports bounded");
        local.HealthController.Alive = false;
        End(f, local, new MedEffect()); RunPrefix(f.Prefixes[1], local); RunPrefix(f.Prefixes[2], local);
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count(m => m.Contains("retired local body")) == 3, "retirement reports have independent bound");
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = false;
        EndCheck(f, bot, true, "turning diagnostics off keeps protection");
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count == 9, "diagnostics stop immediately");
    }

    private static void ContractCases()
    {
        Reset(); ArenaContinuousHealingCompat.Initialize();
        Need(ArenaContinuousHealingCompat.GetRejectionReason() == null, "optional absence");
        var f = FixtureFactory.Make(); f.Register();
        Need(ArenaContinuousHealingCompat.GetRejectionReason() == null, "late discovery");
        ArenaContinuousHealingCompat.GetRejectionReason();
        Need(f.Prefixes.All(p => Harmony.GetPatchInfo(p).Prefixes.Count == 1), "idempotent own receipts");
        foreach (var version in new[] { "1.6.4", "2.0.0" })
        {
            f = FixtureFactory.Make(version); f.Register();
            Need(ArenaContinuousHealingCompat.GetRejectionReason() == null, "newer checked API admitted");
            var game = World(true); EndCheck(f, new() { GameWorld = game.GameWorld }, true, "newer API protects bot");
        }
        Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "newer advisory once");
        foreach (var bad in new[] {
            FixtureFactory.Make("1.6.2"), FixtureFactory.Make(assemblyName: "OtherAssembly"),
            FixtureFactory.Make(assemblyVersion: "1.6.4"), FixtureFactory.Make(metadataVersion: "1.6.4"),
            FixtureFactory.Make(guid: "com.lacyway.CH"), FixtureFactory.Make(defect: "identity"),
            FixtureFactory.Make(defect: "attribute"), FixtureFactory.Make(defect: "result"),
            FixtureFactory.Make(defect: "instance"), FixtureFactory.Make(defect: "name") })
        {
            bad.Register(); Need(ArenaContinuousHealingCompat.GetRejectionReason() != null, "changed API rejects Arena");
            Need(bad.Prefixes.All(p => Harmony.GetPatchInfo(p)?.Prefixes.Count is null or 0), "bad API leaves foreign hooks alone");
        }
        f = FixtureFactory.Make(); f.Register();
        var nativeEnd = typeof(Operation).GetMethod("OnEffectRemoved");
        Harmony.GetPatchInfo(nativeEnd).Prefixes.Clear();
        Need(ArenaContinuousHealingCompat.GetRejectionReason() != null, "missing foreign native receipt rejects Arena");
        f.Register(); Need(ArenaContinuousHealingCompat.GetRejectionReason() == null, "valid replacement repairs binding");
        Harmony.GetPatchInfo(f.Prefixes[1]).Prefixes.Clear();
        Need(ArenaContinuousHealingCompat.GetRejectionReason() != null, "lost own receipt rejects Arena");
        f.Register(); Harmony.GetPatchInfo(nativeEnd).Postfixes.Add(new() { owner = "foreign", PatchMethod = f.Prefixes[0] });
        Need(ArenaContinuousHealingCompat.GetRejectionReason() != null, "new foreign native hook rejects Arena");
        foreach (int failedAt in new[] { 1, 2, 3 })
        {
            Reset(); f = FixtureFactory.Make(); f.Register(); Harmony.FailPatchAt = failedAt;
            Need(ArenaContinuousHealingCompat.GetRejectionReason() != null, "partial hook installation rejects Arena");
            Need(f.Prefixes.All(p => Harmony.GetPatchInfo(p)?.Prefixes.Count is null or 0), "partial hook cleanup");
        }
        Reset(); f = FixtureFactory.Make(); f.Register(); Harmony.DropReceipt = true;
        Need(ArenaContinuousHealingCompat.GetRejectionReason() != null, "missing installation receipt rejects Arena");
        Reset(); f = FixtureFactory.Make(); f.Register(); ArenaContinuousHealingCompat.Initialize();
        var active = World(true); var bot = new Player { GameWorld = active.GameWorld, HealthController = new() { ThrowRead = true } };
        EndCheck(f, bot, true, "proved body survives health-read fault");
        Need(ArenaContinuousHealingCompat.GetRejectionReason() != null, "runtime fault rejects later Arena");
        var ordinary = World(false); EndCheck(f, ordinary.GameWorld.MainPlayer, false, "ordinary survives runtime failure");
        Harmony.FailUnpatch = true; ArenaContinuousHealingCompat.Shutdown();
        EndCheck(f, bot, false, "failed unpatch revokes stale guard authority");
        Harmony.FailUnpatch = false; Chainloader.PluginInfos.Clear();
        Need(ArenaContinuousHealingCompat.GetRejectionReason() == null, "optional removal recovers admission");
    }

    public static int Main(string[] args)
    {
        try
        {
            ScopeCases(); DiagnosticCases(); ContractCases();
            if (args.Length != 1) throw new InvalidOperationException("Supply the separately downloaded published DLL for metadata checks.");
            PublishedMetadata.Verify(args[0], Need);
            Console.WriteLine($"CONTINUOUS_HEALING_CHECKS_PASSED ({_checks})");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
