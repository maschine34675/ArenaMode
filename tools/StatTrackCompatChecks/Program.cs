using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;

public static class Program
{
    private static int _checks;
    private static readonly Dictionary<string, int> Temporary = new(), Persistent = new(), Disk = new();
    private static readonly Queue<(Dictionary<string, int> Snapshot, TaskCompletionSource Completion)> Saves = new();
    private static int _merges, _nativeShots, _nativeHits, _nativeKills, _nativeEnds;
    private static void Need(bool value, string label)
    { ++_checks; if (!value) throw new InvalidOperationException("ASSERT: " + label); }

    public static void Add(string id, bool kill, bool head, bool shot, bool hit, bool hitHead, bool boss, float damage)
    {
        string kind = shot ? "shots" : kill ? "kills" : "hits";
        Temporary[id + ":" + kind] = Temporary.GetValueOrDefault(id + ":" + kind) + 1;
    }
    public static void Merge()
    { ++_merges; foreach (var row in Temporary) Persistent[row.Key] = Persistent.GetValueOrDefault(row.Key) + row.Value; }
    public static Task BeginSave()
    {
        var tcs = new TaskCompletionSource(); Saves.Enqueue((new(Persistent), tcs)); return tcs.Task;
    }
    private static void CompleteSave()
    {
        var item = Saves.Dequeue(); Disk.Clear(); foreach (var row in item.Snapshot) Disk.Add(row.Key, row.Value);
        Temporary.Clear(); item.Completion.SetResult();
    }
    private static void InvokeForeign(Fixture f, int index, params object[] args)
    {
        bool run = true;
        foreach (var prefix in Harmony.GetPatchInfo(f.Hooks[index])?.Prefixes.ToArray() ?? Array.Empty<Patch>())
        {
            object[] values = prefix.PatchMethod.GetParameters().Select(p => args[int.Parse(p.Name[2..])]).ToArray();
            if (!(bool)prefix.PatchMethod.Invoke(null, values)) run = false;
        }
        if (run) f.Hooks[index].Invoke(null, args);
    }
    private static void Shot(Fixture f, Weapon weapon) { ++_nativeShots; InvokeForeign(f, 0, weapon); }
    private static void Hit(Fixture f, Player victim, Player attacker, Weapon weapon)
    { ++_nativeHits; InvokeForeign(f, 2, victim, EBodyPart.Head, 25f, new DamageInfo { Player = new() { iPlayer = attacker }, Weapon = weapon }); }
    private static void Kill(Fixture f, Player victim, Player attacker, Weapon weapon)
    { ++_nativeKills; InvokeForeign(f, 1, victim, attacker, new DamageInfo { Player = new() { iPlayer = attacker }, Weapon = weapon }, EBodyPart.Head, EDamageType.Bullet); }
    private static void End(Fixture f, Player body)
    { ++_nativeEnds; InvokeForeign(f, 3, body); }
    private static void Lifecycle(string method, GameWorld world, Player player = null)
    {
        if (method == nameof(GameWorld.RegisterPlayer)) world.RegisterPlayer(player);
        var info = Harmony.GetPatchInfo(typeof(GameWorld).GetMethod(method));
        foreach (var patch in (method == nameof(GameWorld.RegisterPlayer) ? info?.Postfixes : info?.Prefixes)?.ToArray() ?? Array.Empty<Patch>())
            patch.PatchMethod.Invoke(null, player == null ? new object[] { world } : new object[] { world, player });
    }
    private static LocalGame World(bool active, bool capture = true)
    {
        var game = new LocalGame { GameWorld = new() };
        game.LocalPlayer = new LocalPlayer { GameWorld = game.GameWorld, IsYourPlayer = true, Profile = game.Profile };
        game.GameWorld.RegisterPlayer(game.LocalPlayer);
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = game.GameWorld; Singleton<IBotGame>.Instance = game;
        ArenaSession.IsActive = active;
        if (active) { ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld; }
        if (capture) Lifecycle(nameof(GameWorld.OnGameStarted), game.GameWorld);
        return game;
    }
    private static Weapon Weapon(Player body) => new() { Owner = new Player.PlayerInventoryController { Player = body } };
    private static Player Bot(LocalGame game) => new() { GameWorld = game.GameWorld, Profile = new() };
    private static void Reset()
    {
        Harmony.FailUnpatch = false; ArenaStatTrackCompat.Shutdown(); Harmony.Bound.Clear(); Chainloader.PluginInfos.Clear();
        typeof(ArenaStatTrackCompat).GetField("_reportedNewer", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        Harmony.DropReceipt = false; Harmony.FailPatchAt = Harmony.PatchCalls = 0;
        ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false;
        ArenaSession.ActiveWorld = null; ArenaSession.ActiveGame = null; ArenaSession.ArmedMap = ArenaServerClient.PendingArmToken = null;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null; Singleton<IBotGame>.Instance = null;
        Redeploy.Core.GameAccess.LocalGameAccessor.ThrowRead = false;
        ArenaMode.Plugin.ModLogger.Debug.Clear(); ArenaMode.Plugin.ModLogger.Warnings.Clear();
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = false; ArenaMode.Diagnostics.ArenaLog.Messages.Clear();
        Temporary.Clear(); Persistent.Clear(); Disk.Clear(); Saves.Clear();
        _merges = _nativeShots = _nativeHits = _nativeKills = _nativeEnds = 0;
    }
    private static Fixture Start()
    {
        Reset(); var f = FixtureFactory.Make(); f.Register(); ArenaStatTrackCompat.Initialize();
        Need(ArenaStatTrackCompat.GetRejectionReason() == null, "valid independent contract admitted"); return f;
    }
    private static void CollectionCases()
    {
        var f = Start(); var ordinary = World(false); var own = Weapon(ordinary.LocalPlayer); var victim = Bot(ordinary);
        Shot(f, own); Hit(f, victim, ordinary.LocalPlayer, own); Kill(f, victim, ordinary.LocalPlayer, own);
        Need(Temporary.Count == 6 && Temporary.Values.All(v => v == 1), "ordinary instance and template producers run");
        End(f, ordinary.LocalPlayer); Need(_merges == 1 && Saves.Count == 1, "ordinary end merge creates save");
        var ordinarySnapshot = new Dictionary<string, int>(Persistent);
        var game = World(true); own = Weapon(game.LocalPlayer); victim = Bot(game);
        Shot(f, own); Hit(f, victim, game.LocalPlayer, own); Kill(f, victim, game.LocalPlayer, own);
        Need(Temporary.Count == 6 && Temporary.Values.All(v => v == 1), "Arena shot hit kill do not contaminate ordinary pending batch");
        Need(_nativeShots == 2 && _nativeHits == 2 && _nativeKills == 2, "gameplay actions remain native");
        var enemyWeapon = Weapon(victim); Hit(f, Bot(game), victim, enemyWeapon);
        Need(Temporary.Values.All(v => v == 1), "Arena AI against AI hit excluded");
        End(f, game.LocalPlayer);
        Need(_merges == 1 && Saves.Count == 1, "Arena unregister cannot remerge ordinary pending save");
        CompleteSave();
        Need(Disk.OrderBy(x => x.Key).SequenceEqual(ordinarySnapshot.OrderBy(x => x.Key)) && Temporary.Count == 0,
            "ordinary pending completion remains unchanged during Arena");
        Shot(f, own); End(f, game.LocalPlayer);
        Need(Temporary.Count == 0 && Saves.Count == 0 && _merges == 1, "Arena after pending completion stays clean");

        var oldBody = game.LocalPlayer;
        var nextBody = new LocalPlayer { GameWorld = game.GameWorld, Profile = game.Profile, IsYourPlayer = true };
        Lifecycle(nameof(GameWorld.RegisterPlayer), game.GameWorld, nextBody); game.LocalPlayer = nextBody;
        Shot(f, Weapon(nextBody)); End(f, oldBody);
        Need(Temporary.Count == 0 && _merges == 1, "respawn replacement and old unregister excluded");
        var next = World(false);
        End(f, oldBody); Shot(f, own); Kill(f, Bot(next), oldBody, own);
        Hit(f, Bot(next), oldBody, own);
        Need(Temporary.Count == 0 && _merges == 1, "late old world producer and merge excluded");
        Hit(f, victim, next.LocalPlayer, Weapon(next.LocalPlayer));
        Need(Temporary.Count == 0, "late Arena victim excluded despite ordinary attacker");
        Hit(f, Bot(next), null, own); Kill(f, Bot(next), null, own);
        Need(Temporary.Count == 0, "late damage weapon owns otherwise unattributed callbacks");
        own.Owner = null; Shot(f, own);
        Need(Temporary.Count == 0, "detached exact observed Arena weapon remains excluded");
        own.Owner = new Player.PlayerInventoryController { Player = next.LocalPlayer };
        Shot(f, own);
        Need(Temporary["instance:shots"] == 1, "same weapon transferred to ordinary body loses exemption");
        own = Weapon(next.LocalPlayer); Hit(f, Bot(next), next.LocalPlayer, own); Kill(f, Bot(next), next.LocalPlayer, own);
        End(f, next.LocalPlayer); CompleteSave();
        Need(Disk.Count == 6 && Disk.Values.All(v => v == 2), "ordinary following raid persists increment without Arena totals");
        Need(_nativeEnds == 6, "all underlying unregister calls remain native");
    }
    private static void LoadingCases()
    {
        foreach (string defect in new[] { "none", "token", "start-only", "pending", "armed", "map", "scav", "profile", "local-published", "ordinary", "wrong-world", "not-registered", "not-local" })
        {
            var f = Start(); var normal = World(false); Shot(f, Weapon(normal.LocalPlayer)); End(f, normal.LocalPlayer);
            var loading = World(false, false); var body = loading.LocalPlayer; loading.LocalPlayer = null;
            loading.Status = GameStatus.Running; Singleton<IBotGame>.Instance = null;
            ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = true;
            ArenaSession.ArmedMap = loading.GameWorld.LocationId; ArenaServerClient.PendingArmToken = Guid.NewGuid().ToString("N");
            loading.Settings.serverId = Protocol.BuildNativeEndIdentity("native-server", ArenaServerClient.PendingArmToken);
            switch (defect)
            {
                case "token": loading.Settings.serverId += "other"; break;
                case "start-only": loading.Settings.serverId = Protocol.BuildNativeStartIdentity(ArenaServerClient.PendingArmToken); break;
                case "pending": ArenaServerClient.HasPendingArm = false; break;
                case "armed": ArenaSession.ArmedForNextRaid = false; break;
                case "map": loading.Settings.location = "woods"; break;
                case "scav": loading.Settings.playerSide = ESideType.Savage; break;
                case "profile": body.Profile = new(); break;
                case "local-published": loading.LocalPlayer = body; break;
                case "ordinary": ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false; break;
                case "wrong-world": body.GameWorld = new(); break;
                case "not-registered": loading.GameWorld.RegisteredPlayers.Clear(); break;
                case "not-local": body.IsYourPlayer = false; break;
            }
            // Native registration has completed; invoke only its installed postfix.
            foreach (var p in Harmony.GetPatchInfo(typeof(GameWorld).GetMethod("RegisterPlayer")).Postfixes)
                p.PatchMethod.Invoke(null, new object[] { loading.GameWorld, body });
            ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false;
            World(false); // failure/cleanup before OnGameStarted, then a new ordinary world
            End(f, body);
            Need(_merges == (defect == "none" ? 1 : 2), "failed loading exact registration lease " + defect);
            if (defect == "none")
            { CompleteSave(); Need(Disk["instance:shots"] == 1 && Saves.Count == 0, "failed Arena load preserves prior pending ordinary save"); }
        }
        {
            var f = Start(); var game = World(false, false);
            ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = true;
            ArenaSession.ArmedMap = game.GameWorld.LocationId; ArenaServerClient.PendingArmToken = Guid.NewGuid().ToString("N");
            game.Settings.serverId = Protocol.BuildNativeEndIdentity("native", ArenaServerClient.PendingArmToken);
            Lifecycle(nameof(GameWorld.OnGameStarted), game.GameWorld);
            ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false;
            World(false); End(f, game.LocalPlayer);
            Need(_merges == 0, "zero-event body survives session cleanup before unregister");
        }
        foreach (bool exact in new[] { true, false })
        {
            var f = Start(); var normal = World(false); Shot(f, Weapon(normal.LocalPlayer)); End(f, normal.LocalPlayer);
            var game = World(false, false); var body = game.LocalPlayer;
            game.LocalPlayer = null; game.GameWorld.RegisteredPlayers.Clear(); body.Profile = null;
            game.Status = GameStatus.Running; Singleton<IBotGame>.Instance = null;
            ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = true;
            ArenaSession.ArmedMap = game.GameWorld.LocationId; ArenaServerClient.PendingArmToken = Guid.NewGuid().ToString("N");
            game.Settings.serverId = Protocol.BuildNativeEndIdentity("native", ArenaServerClient.PendingArmToken);
            var constructor = typeof(Player.SinglePlayerInventoryController).GetConstructor(new[] { typeof(Player), typeof(Profile), typeof(bool), typeof(bool) });
            foreach (var patch in Harmony.GetPatchInfo(constructor).Prefixes)
                patch.PatchMethod.Invoke(null, new object[] { body, exact ? game.Profile : new Profile() });
            ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false;
            World(false); End(f, body);
            Need(_merges == (exact ? 1 : 2), "pre-registration failure ownership " + exact);
            if (exact) { CompleteSave(); Need(Disk["instance:shots"] == 1 && Saves.Count == 0, "pre-registration failure preserves pending ordinary save"); }
        }
    }
    private static void ScopeCases()
    {
        var scope = new ArenaStatTrackScope(); var body = new object(); var world = new object(); var later = new object();
        Need(!scope.OwnsBody(body, world, world, false), "unconfirmed body not owned");
        Need(!scope.OwnsBody(body, world, later, true), "foreign world not captured");
        Need(scope.OwnsBody(body, world, world, true), "exact body captured");
        Need(scope.OwnsBody(body, world, later, false), "body lease survives world replacement");
        Need(!scope.OwnsBody(new object(), world, later, false), "same profile cannot capture unobserved old-world body");
        Need(!scope.OwnsBody(body, later, later, false), "reused body loses old-world lease");
        scope.OwnsBody(body, world, world, true); scope.Clear();
        Need(!scope.OwnsBody(body, world, later, false), "shutdown clears body leases");
        var weapon = new object(); var owner = new object();
        Need(scope.OwnsWeapon(weapon, owner, body, world, world, true), "weapon links to physical owner");
        Need(!scope.OwnsWeapon(new object(), owner, null, null, later, false), "copied weapon ids cannot inherit ownership");
        Need(!scope.OwnsWeapon(weapon, new object(), null, null, later, false), "new nonplayer owner removes detached weapon exemption");
        Need(!scope.OwnsWeapon(weapon, null, null, null, later, false), "invalidated weapon lease stays removed");
        var f = Start(); var game = World(true, false);
        Singleton<IBotGame>.Instance = new LocalGame(); Shot(f, Weapon(game.LocalPlayer));
        Need(Temporary.Count == 2, "wrong bot game fails open");
        Temporary.Clear(); Singleton<IBotGame>.Instance = game; game.GameWorld = new(); Shot(f, Weapon(game.LocalPlayer));
        Need(Temporary.Count == 2, "wrong native game world fails open");
        Temporary.Clear(); var ordinary = World(false); ArenaSession.IsActive = true;
        Shot(f, Weapon(ordinary.LocalPlayer)); Need(Temporary.Count == 2, "stale active flag cannot capture ordinary raid");
        f = Start(); game = World(true); World(false); Redeploy.Core.GameAccess.LocalGameAccessor.ThrowRead = true;
        End(f, game.LocalPlayer); Need(_merges == 0, "known retired body does not require new-world settings read");
    }
    private static void Contracts()
    {
        foreach (var defect in new[] { "identity", "attribute", "assembly-version", "method-name", "extra-producer", "extra-merge", "extra-helper-writer", "nested-writer", "extra-accessor" })
        {
            Reset(); var f = FixtureFactory.Make(defect: defect); f.Register(); ArenaStatTrackCompat.Initialize();
            Need(ArenaStatTrackCompat.GetRejectionReason() != null, "unsafe contract rejected " + defect);
        }
        Reset(); var low = FixtureFactory.Make("2.1.0"); low.Register(); ArenaStatTrackCompat.Initialize();
        Need(ArenaStatTrackCompat.GetRejectionReason() != null, "old version rejected");
        Reset(); var wrong = FixtureFactory.Make(metadataVersion: "2.2.0"); wrong.Register(); ArenaStatTrackCompat.Initialize();
        Need(ArenaStatTrackCompat.GetRejectionReason() != null, "metadata disagreement rejected");
        Reset(); var newer = FixtureFactory.Make("2.2.0"); newer.Register(); ArenaStatTrackCompat.Initialize();
        Need(ArenaStatTrackCompat.GetRejectionReason() == null && ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "forward compatible API warns once");
        ArenaStatTrackCompat.GetRejectionReason(); Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "forward warning bounded");
        Reset(); ArenaStatTrackCompat.Initialize(); Need(ArenaStatTrackCompat.GetRejectionReason() == null && Harmony.Bound.Count == 0, "absent mod unchanged");
        Reset(); var f2 = FixtureFactory.Make(); f2.Register(); Harmony.Bound[Fixture.Targets[2]].Postfixes.Clear(); ArenaStatTrackCompat.Initialize();
        Need(ArenaStatTrackCompat.GetRejectionReason() != null, "missing native foreign receipt rejected");
        Reset(); f2 = FixtureFactory.Make(); f2.Register(); Harmony.DropReceipt = true; ArenaStatTrackCompat.Initialize();
        Need(ArenaMode.Plugin.ModLogger.Warnings.Any(w => w.Contains("compatibility hooks did not install")), "missing own installation receipt rejected");
        Reset(); f2 = FixtureFactory.Make(); f2.Register(); Harmony.FailPatchAt = 3; ArenaStatTrackCompat.Initialize();
        Need(Harmony.Bound.Values.All(i => i.Prefixes.Count == 0)
            && ArenaMode.Plugin.ModLogger.Warnings.Any(w => w.Contains("injected patch failure")), "partial installation rolled back");
        f2 = Start(); Harmony.Bound[f2.Hooks[0]].Prefixes.Clear();
        Need(ArenaStatTrackCompat.GetRejectionReason() != null, "removed own hook rejects subsequent Arena");
        f2 = Start(); var game = World(true); Shot(f2, Weapon(game.LocalPlayer));
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count == 0, "developer diagnostics off");
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = true;
        for (int n = 0; n < 5; ++n) { Shot(f2, Weapon(game.LocalPlayer)); Hit(f2, Bot(game), game.LocalPlayer, Weapon(game.LocalPlayer)); Kill(f2, Bot(game), game.LocalPlayer, Weapon(game.LocalPlayer)); End(f2, game.LocalPlayer); }
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count == 4, "developer diagnostics bounded by seam");
        ArenaStatTrackCompat.Shutdown(); Shot(f2, Weapon(game.LocalPlayer)); Need(Temporary.Count == 2, "shutdown restores foreign hooks");
    }
    public static void Main(string[] args)
    {
        CollectionCases(); LoadingCases(); ScopeCases(); Contracts();
        PublishedMetadata.Verify(args[0], args[1], args[2], Need);
        Console.WriteLine($"STATTRACK_COMPAT_CHECKS_PASSED ({_checks})");
    }
}
