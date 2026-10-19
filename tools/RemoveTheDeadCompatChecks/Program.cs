using System.Collections;
using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;

public static class Program
{
    private static int _checks;
    private const string Token = "b6c06414ec214e89a0bf16a7975d78b0";
    private static void Need(bool condition, string label)
    { _checks++; if (!condition) throw new InvalidOperationException("ASSERT: " + label); }

    // Authored fixture models Harmony's bool prefix, not native Mono detouring.
    public static bool BeforeForeignRemoval(object instance)
    {
        var target = instance.GetType().GetMethod("RemoveBotAndWeapons", BindingFlags.Instance | BindingFlags.NonPublic);
        var patches = Harmony.GetPatchInfo(target)?.Prefixes.ToArray() ?? Array.Empty<Patch>();
        bool allowed = true;
        foreach (var patch in patches)
            if (!(bool)patch.PatchMethod.Invoke(null, null)) allowed = false;
        return allowed;
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false;
        ArenaRemoveTheDeadCompat.Shutdown();
        Chainloader.PluginInfos.Clear(); Harmony.Bound.Clear();
        Harmony.FailPatch = Harmony.DropReceipt = false;
        AccessTools.MissingSettings = LocalGameAccessor.FailRead = false;
        ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = false;
        ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null; ArenaSession.ArmedMap = null;
        ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null; Singleton<IBotGame>.Instance = null;
        ArenaMode.Plugin.ModLogger.Info.Clear(); ArenaMode.Plugin.ModLogger.Warnings.Clear();
        UnityEngine.Object.Destroyed = 0;
    }

    private static void Install(Fixture f)
        => Chainloader.PluginInfos[ArenaRemoveTheDeadContract.PluginGuid] = new() { Instance = f.Instance, Metadata = f.Metadata };

    private static LocalGame World(bool arm = false)
    {
        var game = new LocalGame { GameWorld = new() { LocationId = "factory4_day" },
            _raidSettings = new() { location = "factory4_day", playerSide = ESideType.Pmc, serverId = "ordinary" } };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = game.GameWorld; Singleton<IBotGame>.Instance = game;
        if (arm)
        {
            ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmedMap = game.GameWorld.LocationId;
            ArenaServerClient.HasPendingArm = true; ArenaServerClient.PendingArmToken = Token;
            game._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native-server", Token);
        }
        return game;
    }

    private static void Active(LocalGame game)
    {
        ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld; ArenaSession.IsActive = true;
        ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null; ArenaSession.ArmedForNextRaid = false;
    }

    private static void Paths(Fixture f, bool allowed, string reason)
    {
        int before = UnityEngine.Object.Destroyed;
        f.Manual.Invoke(f.Instance, null);
        Need(UnityEngine.Object.Destroyed == before + (allowed ? 2 : 0), reason + " manual");
        before = UnityEngine.Object.Destroyed;
        var queued = (IEnumerator)f.Queue.Invoke(f.Instance, null);
        queued.MoveNext();
        Need(UnityEngine.Object.Destroyed == before + (allowed ? 2 : 0), reason + " automatic iterator");
        before = UnityEngine.Object.Destroyed;
        f.Instance.GetType().GetMethod("RTDButtonActionDrawer").Invoke(f.Instance, new object[] { null });
        Need(UnityEngine.Object.Destroyed == before + (allowed ? 2 : 0), reason + " F12 button");
        Need((int)f.Instance.GetType().GetField("UntouchedConfig").GetValue(f.Instance) == 37
            && (float)f.Instance.GetType().GetField("UntouchedTimer").GetValue(f.Instance) == 721.5f,
            reason + " leaves foreign settings and timer unchanged");
    }

    private static void ScopeCases()
    {
        Reset(); var f = FixtureFactory.Make(); Install(f); ArenaRemoveTheDeadCompat.Initialize();
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() == null, "reviewed contract admitted");
        Paths(f, true, "menu");
        var game = World(); Paths(f, true, "ordinary PMC");
        game._raidSettings.playerSide = ESideType.Savage; Paths(f, true, "ordinary Scav");
        var hideout = new HideoutGame(); Singleton<AbstractGame>.Instance = hideout; Singleton<IBotGame>.Instance = hideout;
        Paths(f, true, "hideout");
        game = World(true); Paths(f, false, "server-confirmed early Arena");
        Need(ArenaMode.Plugin.ModLogger.Info.Count == 1, "one pause notice per world");
        game._raidSettings.serverId = Protocol.BuildNativeStartIdentity(Token); Paths(f, true, "request-only marker");
        game._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native-server", new string('a', 32)); Paths(f, true, "wrong token");
        game._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native-server", Token);
        ArenaServerClient.PendingArmToken = "bad"; Paths(f, true, "malformed token"); ArenaServerClient.PendingArmToken = Token;
        ArenaServerClient.HasPendingArm = false; Paths(f, true, "unconfirmed arm"); ArenaServerClient.HasPendingArm = true;
        ArenaSession.ArmedForNextRaid = false; Paths(f, true, "cancelled arm"); ArenaSession.ArmedForNextRaid = true;
        ArenaSession.ArmedMap = "woods"; Paths(f, true, "other armed map"); ArenaSession.ArmedMap = "factory4_day";
        game.Status = GameStatus.Starting; Paths(f, true, "incomplete native start"); game.Status = GameStatus.Started;
        game._raidSettings.playerSide = ESideType.Savage; Paths(f, true, "Scav cannot inherit arm"); game._raidSettings.playerSide = ESideType.Pmc;
        game._raidSettings.location = "woods"; Paths(f, true, "mismatched native map"); game._raidSettings.location = "factory4_day";
        game.GameWorld.LocationId = ""; Paths(f, true, "missing native map"); game.GameWorld.LocationId = "factory4_day";
        Singleton<GameWorld>.Instance = new() { LocationId = "factory4_day" }; Paths(f, true, "foreign same-map world"); Singleton<GameWorld>.Instance = game.GameWorld;
        Singleton<IBotGame>.Instance = new LocalGame(); Paths(f, true, "foreign bot game"); Singleton<IBotGame>.Instance = game;
        var settings = game._raidSettings; game._raidSettings = null; Paths(f, true, "missing settings"); game._raidSettings = settings;
        Active(game); Paths(f, false, "active Arena after token consumption");
        game._raidSettings = null; Paths(f, false, "proved active Arena survives missing settings"); game._raidSettings = settings;
        game.GameWorld.LocationId = null; Paths(f, false, "proved active Arena survives missing location"); game.GameWorld.LocationId = "factory4_day";
        foreach (var status in new[] { GameStatus.Stopping, GameStatus.SoftStopping, GameStatus.Stopped })
        { game.Status = status; Paths(f, false, "exact active Arena retained during " + status); }
        game = World(); Paths(f, true, "next same-map ordinary raid ignores old active pair");
        ArenaSession.IsActive = false; Paths(f, true, "ordinary after Arena end");
    }

    private static void ContractCases()
    {
        Reset(); ArenaRemoveTheDeadCompat.Initialize();
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() == null, "optional absence");
        var late = FixtureFactory.Make(); Install(late);
        var lateProblem = ArenaRemoveTheDeadCompat.GetRejectionReason();
        Need(lateProblem == null, "late plugin discovery: " + lateProblem);
        Need(Harmony.GetPatchInfo(late.Remove)?.Prefixes.Count == 1, "real prefix receipt present");
        ArenaRemoveTheDeadCompat.GetRejectionReason();
        Need(Harmony.GetPatchInfo(late.Remove).Prefixes.Count == 1, "discovery idempotent");
        foreach (var f in new[] { FixtureFactory.Make("4.1.4"), FixtureFactory.Make("5.0.0") })
        {
            Install(f); Need(ArenaRemoveTheDeadCompat.GetRejectionReason() == null, "newer checked contract admitted");
            World(true); Paths(f, false, "newer contract protects Arena");
        }
        Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "newer-version advisory once per process");
        var malformed = new[] { FixtureFactory.Make("4.1.2"), FixtureFactory.Make(assemblyName: "not-RTD"),
            FixtureFactory.Make(assemblyVersion: "4.1.4"), FixtureFactory.Make(metadataVersion: "4.1.4"),
            FixtureFactory.Make(guid: "com.jbs4bmx.removethedead"), FixtureFactory.Make(defect: "attribute"),
            FixtureFactory.Make(defect: "signature"), FixtureFactory.Make(defect: "iterator"),
            FixtureFactory.Make(defect: "auto-chain"), FixtureFactory.Make(defect: "manual-chain"),
            FixtureFactory.Make(defect: "weapon-chain"), FixtureFactory.Make(defect: "update-chain"),
            FixtureFactory.Make(defect: "button-chain"), FixtureFactory.Make(defect: "bypass"),
            FixtureFactory.Make(defect: "helper-bypass") };
        foreach (var f in malformed)
        {
            Reset(); Install(f);
            Need(ArenaRemoveTheDeadCompat.GetRejectionReason()?.Contains("Ordinary raids remain available") == true,
                "changed metadata or cleanup chain rejects Arena");
            Need(Harmony.GetPatchInfo(f.Remove)?.Prefixes.Count is null or 0, "malformed contract installs no guard");
        }
        var badBase = FixtureFactory.Make(defect: "base");
        bool refused = false;
        try { ArenaRemoveTheDeadContract.Resolve(badBase.Remove.DeclaringType, badBase.Metadata.GUID, badBase.Metadata.Version, out _); }
        catch (MissingMemberException) { refused = true; }
        Need(refused, "wrong plugin inheritance refused");
        Reset(); Chainloader.PluginInfos[ArenaRemoveTheDeadContract.PluginGuid] = new();
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() != null, "unloaded plugin instance refuses Arena");
        Chainloader.PluginInfos.Clear();
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() == null, "absence restored without saved changes");
    }

    private static void FailureCases()
    {
        foreach (string failure in new[] { "partial", "receipt", "field" })
        {
            Reset(); var f = FixtureFactory.Make(); Install(f);
            Harmony.FailPatch = failure == "partial"; Harmony.DropReceipt = failure == "receipt"; AccessTools.MissingSettings = failure == "field";
            Need(ArenaRemoveTheDeadCompat.GetRejectionReason() != null, "unsafe hook installation refuses Arena " + failure);
            World(); Paths(f, true, "ordinary after failed installation " + failure);
            Need(Harmony.GetPatchInfo(f.Remove)?.Prefixes.Count is null or 0, "partial install rolled back " + failure);
        }
        Reset(); var installed = FixtureFactory.Make(); Install(installed); ArenaRemoveTheDeadCompat.Initialize();
        Harmony.Bound[installed.Remove].Prefixes.Clear();
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() != null, "removed prefix refuses Arena");
        Reset(); installed = FixtureFactory.Make(); Install(installed); ArenaRemoveTheDeadCompat.Initialize();
        var game = World(); LocalGameAccessor.FailRead = true;
        Paths(installed, true, "unreadable ordinary identity");
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() != null, "runtime failure latches admission refusal");
        LocalGameAccessor.FailRead = false;
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() != null, "menu does not repair runtime fault");
        Reset(); installed = FixtureFactory.Make(); Install(installed); ArenaRemoveTheDeadCompat.Initialize();
        game = World(true); Active(game); LocalGameAccessor.FailRead = true;
        Paths(installed, false, "proved active Arena survives settings-read fault");
        Paths(installed, false, "repeated active Arena fault retains guard");
        Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "runtime fault warning bounded");
        Need(ArenaRemoveTheDeadCompat.GetRejectionReason() != null, "active runtime fault prevents next admission");
        ArenaSession.IsActive = false; game = World();
        Paths(installed, true, "unreadable next ordinary raid not claimed");
        LocalGameAccessor.FailRead = false;
        Harmony.FailUnpatch = true; ArenaRemoveTheDeadCompat.Shutdown();
        Need(Harmony.GetPatchInfo(installed.Remove).Prefixes.Count == 1, "failed unpatch leaves actual receipt for inert test");
        game = World(true); Active(game);
        Paths(installed, true, "shutdown revokes authority before unpatch failure");
        Harmony.FailUnpatch = false; Reset();
    }

    public static int Main(string[] args)
    {
        try
        {
            ContractCases(); ScopeCases(); FailureCases();
            if (args.Length != 1) throw new InvalidOperationException("Supply the separately downloaded 4.1.3 DLL for metadata-only verification.");
            PublishedMetadata.Verify(args[0], Need);
            Console.WriteLine($"REMOVE_THE_DEAD_CHECKS_PASSED ({_checks})");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("REMOVE_THE_DEAD_CHECKS_FAILED: " + ex); return 1; }
    }
}
