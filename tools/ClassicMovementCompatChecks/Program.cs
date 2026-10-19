using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;

public static class Program
{
    private static int _checks;
    private static readonly string Token = "11111111111111111111111111111111";
    private static void Need(bool pass, string label)
    { _checks++; if (!pass) throw new InvalidOperationException("ASSERT: " + label); }

    public static bool ForeignState(MovementContext context, ref BaseMovementState result)
    { context.ForeignStates++; result = new ForeignMovementState(); return false; }
    public static void ForeignSpeed(MovementContext context) { context.ForeignSpeeds++; }

    private static BaseMovementState State(Fixture fixture, MovementContext context, EPlayerState name = EPlayerState.Run)
    {
        if (context.Cache.TryGetValue(name, out var cached)) return cached;
        bool foreign = true, native = false;
        if (!fixture.Get("NoInertia")) native = true;
        else
        {
            foreach (var patch in Harmony.GetPatchInfo(fixture.Methods[0])?.Prefixes.ToArray() ?? Array.Empty<Patch>())
            {
                object[] args = { context, native };
                if (!(bool)patch.PatchMethod.Invoke(null, args)) foreign = false;
                native = (bool)args[1];
            }
            if (foreign)
            {
                object[] args = { context, null, name, false };
                native = (bool)fixture.Methods[0].Invoke(null, args);
                if (!native) return context.Cache[name] = (BaseMovementState)args[1];
            }
        }
        return context.Cache[name] = native ? context.GetNewState(name) : null;
    }

    private static void Speed(Fixture fixture, MovementContext context)
    {
        context.ManualUpdate(0.02f);
        bool run = true;
        foreach (var patch in Harmony.GetPatchInfo(fixture.Methods[1])?.Prefixes.ToArray() ?? Array.Empty<Patch>())
            if (!(bool)patch.PatchMethod.Invoke(null, new object[] { context })) run = false;
        if (run) fixture.Methods[1].Invoke(null, new object[] { context, 0.02f });
    }

    private static MovementContext Body(LocalGame game, bool local = true, bool assignContext = true, bool preview = false)
    {
        Player body = preview ? new Player() : new LocalPlayer();
        body.GameWorld = game.GameWorld; body.IsYourPlayer = local;
        var context = new MovementContext(body);
        if (assignContext) body.MovementContext = context;
        return context;
    }

    private static Player Owner(MovementContext context)
        => (Player)typeof(MovementContext).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(context);

    private static LocalGame World(bool active, bool loading = false)
    {
        var game = new LocalGame { GameWorld = new(), Profile = new(), Status = loading ? GameStatus.Running : GameStatus.Started };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = game.GameWorld; Singleton<IBotGame>.Instance = game;
        ArenaSession.IsActive = active; ArenaSession.ActiveGame = active ? game : null; ArenaSession.ActiveWorld = active ? game.GameWorld : null;
        ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = loading;
        ArenaSession.ArmedMap = loading ? game.GameWorld.LocationId : null;
        ArenaServerClient.PendingArmToken = loading ? Token : null;
        game._raidSettings.serverId = loading ? Protocol.BuildNativeEndIdentity("native", Token) : "ordinary";
        return game;
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false; ArenaClassicMovementCompat.Shutdown();
        Harmony.Bound.Clear(); Chainloader.PluginInfos.Clear(); Harmony.DropReceipt = false; Harmony.FailPatchAt = Harmony.PatchCalls = 0;
        ArenaSession.IsActive = ArenaSession.ArmedForNextRaid = ArenaServerClient.HasPendingArm = false;
        ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null; ArenaSession.ArmedMap = ArenaServerClient.PendingArmToken = null;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null; Singleton<IBotGame>.Instance = null;
        ArenaMode.Plugin.ModLogger.Warnings.Clear(); ArenaMode.Plugin.ModLogger.Debug.Clear();
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = false; ArenaMode.Diagnostics.ArenaLog.Messages.Clear();
    }

    private static Fixture Bind()
    {
        Reset(); var f = FixtureFactory.Make(); f.Register(); ArenaClassicMovementCompat.Initialize();
        Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "reviewed contract accepted");
        return f;
    }

    private static void CheckBody(Fixture f, MovementContext context, bool arena, string label)
    {
        Need(State(f, context) is NativeMovementState == arena, label + " state authority");
        Speed(f, context);
        Need(context.ForeignSpeeds == (arena ? 0 : 1), label + " speed authority");
        Need(context.NativeSpeeds == 1, label + " native update retained");
    }

    private static void ScopeCases()
    {
        var f = Bind(); var ordinary = World(false);
        CheckBody(f, Body(ordinary), false, "ordinary PMC");
        ordinary._raidSettings.playerSide = ESideType.Savage;
        CheckBody(f, Body(ordinary), false, "ordinary Scav"); CheckBody(f, Body(ordinary, false), false, "ordinary bot");
        var game = World(true);
        var current = Body(game); CheckBody(f, current, true, "Arena local");
        CheckBody(f, Body(game, false), true, "Arena bot");
        CheckBody(f, Body(game, preview: true), false, "scene preview Player");
        var noProfile = Body(game); Owner(noProfile).Profile = null; CheckBody(f, noProfile, false, "uninitialized local body");
        var foreignWorld = Body(ordinary); CheckBody(f, foreignWorld, false, "foreign body world");
        var differentContext = Body(game); Owner(differentContext).MovementContext = new(Owner(differentContext));
        CheckBody(f, differentContext, false, "unrelated context");
        CheckBody(f, Body(game), true, "respawn local"); CheckBody(f, Body(game, false), true, "respawn bot");
        // A cached native state never becomes a foreign subclass on a later tick.
        var original = State(f, current); Need(ReferenceEquals(original, State(f, current)), "native cache reused");
        Need(current.ForeignStates == 0, "foreign state never entered Arena cache");
        // There is deliberately no fast-movement setting in this adapter. Native
        // caches survive turning Arena's bonuses off and back on mid-raid.
        foreach (var state in Enum.GetValues<EPlayerState>()) Need(State(f, current, state) is NativeMovementState, "all cached classes native without mutator predicate");
        var oldBody = Owner(current); var oldWorld = game.GameWorld;
        var next = World(false);
        Speed(f, current); Need(current.ForeignSpeeds == 0, "retired context stays native");
        CheckBody(f, Body(next), false, "following same-map ordinary");
        oldBody.GameWorld = next.GameWorld;
        Speed(f, current); Need(current.ForeignSpeeds == 1, "reused body world loses lease");
        // No profile-name or profile-object match lets a new ordinary context inherit it.
        var newBody = Body(next); Owner(newBody).Profile = oldBody.Profile;
        CheckBody(f, newBody, false, "same-profile new ordinary body");

        game = World(true); var unknownBotGame = new LocalGame(); Singleton<IBotGame>.Instance = unknownBotGame;
        CheckBody(f, Body(game, false), false, "wrong bot game");
        Singleton<IBotGame>.Instance = game; Singleton<GameWorld>.Instance = new();
        CheckBody(f, Body(game), false, "wrong current world");
        Singleton<GameWorld>.Instance = game.GameWorld; Singleton<AbstractGame>.Instance = new HideoutGame();
        CheckBody(f, Body(game), false, "hideout abstract game");
        var scope = new ArenaClassicMovementScope(); var contextKey = new object(); var bodyKey = new object(); var worldKey = new object();
        Need(scope.Owns(contextKey, bodyKey, worldKey, contextKey, worldKey, true, false, true), "scope lease established");
        Need(!scope.Owns(contextKey, new object(), worldKey, contextKey, worldKey, true, false, false), "context owner reuse loses lease");
        Need(scope.Owns(contextKey, bodyKey, worldKey, contextKey, worldKey, true, false, true), "scope lease reestablished");
        scope.Clear(); Need(!scope.Owns(contextKey, bodyKey, worldKey, contextKey, worldKey, true, false, false), "scope cleanup releases lease");
    }

    private static void LoadingCases()
    {
        var f = Bind(); var game = World(false, true); Singleton<IBotGame>.Instance = null;
        var local = Body(game, assignContext: false); Owner(local).Profile = game.Profile;
        Need(State(f, local) is NativeMovementState, "pre-IBotGame initial local construction");
        Owner(local).MovementContext = local; game.LocalPlayer = (LocalPlayer)Owner(local);
        Speed(f, local); Need(local.ForeignSpeeds == 0, "leased pre-bot local speed");
        // New bodies during that gap have no generic missing-IBotGame privilege.
        CheckBody(f, Body(game, false), false, "pre-bot unrelated loading body");
        Singleton<IBotGame>.Instance = game;
        CheckBody(f, Body(game, false), true, "confirmed loading bot");
        var cases = new (string Label, Action<LocalGame> Change)[] {
            ("unconfirmed start marker", g => g._raidSettings.serverId = Protocol.BuildNativeStartIdentity(Token)),
            ("foreign end token", g => g._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native", "22222222222222222222222222222222")),
            ("selected map only", g => { ArenaSession.ArmedForNextRaid = false; }),
            ("no pending request", g => { ArenaServerClient.HasPendingArm = false; }),
            ("invalid arm token", g => { ArenaServerClient.PendingArmToken = "x"; g._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native", "x"); }),
            ("wrong armed map", g => { ArenaSession.ArmedMap = "laboratory"; }),
            ("wrong native map", g => g._raidSettings.location = "laboratory"),
            ("scav cannot own Arena", g => g._raidSettings.playerSide = ESideType.Savage),
            ("stopped cannot create Arena lease", g => g.Status = GameStatus.Stopped)
        };
        foreach (var entry in cases)
        {
            game = World(false, true); entry.Change(game); CheckBody(f, Body(game), false, entry.Label);
        }
        game = World(false, true); Singleton<IBotGame>.Instance = null;
        var fakeProfile = Body(game, assignContext: false);
        Need(State(f, fakeProfile) is ForeignMovementState, "initial local requires exact profile object");
        var fakeLocal = Body(game, local: false, assignContext: false); Owner(fakeLocal).Profile = game.Profile;
        Need(State(f, fakeLocal) is ForeignMovementState, "initial local requires IsYourPlayer");
        var alreadyOwned = Body(game, assignContext: false); Owner(alreadyOwned).Profile = game.Profile; game.LocalPlayer = new();
        Need(State(f, alreadyOwned) is ForeignMovementState, "initial local requires missing native owner");
        game = World(false, true); var failed = Body(game); CheckBody(f, failed, true, "confirmed load before failure");
        game = World(false); CheckBody(f, Body(game), false, "ordinary after failed load");
    }

    private static void ContractCases()
    {
        Reset(); ArenaClassicMovementCompat.Initialize(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "optional absence");
        var f = FixtureFactory.Make(); f.Register(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "late optional discovery");
        ArenaClassicMovementCompat.GetRejectionReason(); Need(f.Methods.All(m => Harmony.GetPatchInfo(m).Prefixes.Count == 1), "idempotent guard binding");
        foreach (var version in new[] { "1.1.4", "2.0.0" })
        {
            f = FixtureFactory.Make(version, assemblyVersion: version); f.Register();
            Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "forward checked version");
        }
        Need(ArenaMode.Plugin.ModLogger.Warnings.Count == 1, "newer warning bounded");
        foreach (var bad in new[] {
            FixtureFactory.Make("1.1.2"), FixtureFactory.Make(metadataVersion: "1.1.4"), FixtureFactory.Make(assemblyVersion: "9.0.0"),
            FixtureFactory.Make(assemblyName: "Unrelated"), FixtureFactory.Make(guid: "COM.boogle.classicmovement"),
            FixtureFactory.Make(defect: "identity"), FixtureFactory.Make(defect: "attribute"), FixtureFactory.Make(defect: "result"),
            FixtureFactory.Make(defect: "instance"), FixtureFactory.Make(defect: "name"), FixtureFactory.Make(defect: "config-null") })
        {
            bad.Register(); Need(ArenaClassicMovementCompat.GetRejectionReason() != null, "changed contract rejected");
            Need(bad.Methods.All(m => Harmony.GetPatchInfo(m)?.Prefixes.Count is null or 0), "foreign hooks preserved on rejection");
        }
        f = FixtureFactory.Make(); f.Set("NostalgiaMode", true); f.Register();
        Need(ArenaClassicMovementCompat.GetRejectionReason()?.Contains("NostalgiaMode") == true, "unsafe legacy mode rejected explicitly");
        CheckBody(f, Body(World(false)), false, "ordinary legacy mode unaffected");
        f.Set("NostalgiaMode", false); f.Register(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "ordinary config not mutated");
        Need(f.Get("NoInertia") && !f.Get("NostalgiaMode"), "foreign settings unchanged");
        Harmony.GetPatchInfo(typeof(MovementContext).GetMethod("GetNewState")).Prefixes.Clear();
        Need(ArenaClassicMovementCompat.GetRejectionReason() != null, "missing foreign state receipt rejects");
        f.Register(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "binding recovers with exact receipts");
        Harmony.GetPatchInfo(typeof(MovementContext).GetMethod("ManualUpdate")).Postfixes.Clear();
        Need(ArenaClassicMovementCompat.GetRejectionReason() != null, "missing foreign speed receipt rejects");
        f.Register(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "rebind after speed receipt restored");
        Harmony.GetPatchInfo(typeof(MovementContext).GetMethod("GetNewState")).Postfixes.Add(new() { owner = "foreign", PatchMethod = f.Methods[0] });
        Need(ArenaClassicMovementCompat.GetRejectionReason() != null, "unexpected foreign hook rejects");
        f.Register(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "new exact bind");
        Harmony.GetPatchInfo(f.Methods[1]).Prefixes.Clear(); Need(ArenaClassicMovementCompat.GetRejectionReason() != null, "lost own receipt rejects");
        Reset(); f = FixtureFactory.Make(noInertia: false); f.Register(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "NoInertia false conditional receipt");
        var game = World(true); var body = Body(game); Need(State(f, body) is NativeMovementState, "NoInertia false native factory remains");
        Speed(f, body); Need(body.ForeignSpeeds == 0, "NoInertia false speed conflict still isolated");
        foreach (int partial in new[] { 1, 2 })
        {
            Reset(); f = FixtureFactory.Make(); f.Register(); Harmony.FailPatchAt = partial;
            Need(ArenaClassicMovementCompat.GetRejectionReason() != null, "partial installation rejects");
            Need(f.Methods.All(m => Harmony.GetPatchInfo(m)?.Prefixes.Count is null or 0), "partial installation cleaned");
        }
        Reset(); f = FixtureFactory.Make(); f.Register(); Harmony.DropReceipt = true;
        Need(ArenaClassicMovementCompat.GetRejectionReason() != null, "missing own receipt rejects");
        f = Bind(); var owned = Body(World(true)); CheckBody(f, owned, true, "owned before cleanup");
        Harmony.FailUnpatch = true; ArenaClassicMovementCompat.Shutdown(); Speed(f, owned);
        Need(owned.ForeignSpeeds == 1, "stale prefix has no authority after cleanup");
        Harmony.FailUnpatch = false; Chainloader.PluginInfos.Clear(); Need(ArenaClassicMovementCompat.GetRejectionReason() == null, "removal permits ordinary admission");
    }

    private static void Diagnostics()
    {
        var f = Bind(); var game = World(true); var local = Body(game); var bot = Body(game, false);
        CheckBody(f, local, true, "default quiet local"); Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count == 0, "default diagnostics quiet");
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = true;
        for (int i = 0; i < 20; ++i)
        { State(f, local, (EPlayerState)(i % 6)); State(f, bot, (EPlayerState)(i % 6)); Speed(f, local); Speed(f, bot); }
        Need(ArenaMode.Diagnostics.ArenaLog.Messages.Count == 4, "developer reports bounded to four");
        ArenaMode.Diagnostics.ArenaLog.DeveloperEnabled = false; Speed(f, local);
        Need(local.ForeignSpeeds == 0, "diagnostics switch cannot disable guard");
    }

    public static int Main(string[] args)
    {
        try
        {
            ScopeCases(); LoadingCases(); ContractCases(); Diagnostics();
            if (args.Length != 1) throw new InvalidOperationException("Provide separately downloaded ClassicMovement 1.1.3 DLL for metadata inspection.");
            PublishedMetadata.Verify(args[0], Need);
            Console.WriteLine($"CLASSIC_MOVEMENT_COMPAT_PASSED ({_checks})"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
