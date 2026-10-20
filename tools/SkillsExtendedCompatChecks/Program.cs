using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using ArenaMode.Session;
using ArenaMode.Shared;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;

public static class Program
{
    private static int _checks, _medical, _nato, _eastern, _doors, _other;
    private static readonly Dictionary<string, float> Receipts = new();
    private const string Token = "67a438725fa04c4a9e52205baec1b521";
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly MethodInfo NativeStart = typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));
    private static void Need(bool condition, string label)
    { ++_checks; if (!condition) throw new InvalidOperationException("ASSERT: " + label); }
    public static float Reserve(string key, float amount)
    { Receipts[key] = amount; return amount; }
    public static string Key(GameWorld world) => world.MainPlayer?.Profile?.Id ?? "no-player";
    public static void Credit(Player player, int kind)
    {
        if (kind == 0) ++_medical;
        if (kind == 1) ++_nato;
        if (kind == 2) ++_eastern;
    }
    public static void DoorFix() => ++_doors;
    public static void OtherMedical(IHealthEffect effect) => ++_other;
    public static void OtherMastering(Mastering mastering) => ++_other;

    private sealed record Fixture(Type Plugin, Type Runtime, MethodInfo Boot, MethodInfo Reward,
        MethodInfo Started, MethodInfo Medical, MethodInfo Nato, MethodInfo Eastern, BepInPlugin Metadata);

    private static Fixture Make(string version = "3.1.4", string mismatch = null, string assemblyName = "SkillsExtended.Client.Skills",
        bool badAttribute = false, bool extraWriter = false, bool extraBodyReader = false, bool missingSubscription = false)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName) { Version = new(mismatch ?? version) },
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("skills-fixture");
        var plugin = module.DefineType("SkillsExtended.SkillsExtendedPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        var attr = new BepInPlugin(ArenaSkillsExtendedContract.PluginGuid, "fixture", version);
        if (!badAttribute) plugin.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { attr.GUID, attr.Name, version }));
        var ledger = module.DefineType("SkillsExtended.Skills.Signals.SignalsXpLedger", TypeAttributes.NotPublic);
        var reserve = ledger.DefineMethod("Reserve", MethodAttributes.Public | MethodAttributes.Static,
            typeof(float), new[] { typeof(string), typeof(float) });
        var il = reserve.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Reserve))); il.Emit(OpCodes.Ret);
        var reserveMethod = ledger.CreateType().GetMethod("Reserve");
        var runtime = module.DefineType("SkillsExtended.Skills.Signals.SignalsRuntime", TypeAttributes.Public, typeof(UnityEngine.MonoBehaviour));
        runtime.DefineDefaultConstructor(MethodAttributes.Public);
        var field = runtime.DefineField("_world", typeof(GameWorld), FieldAttributes.Public);
        var getter = runtime.DefineMethod("get_World", MethodAttributes.Public | MethodAttributes.SpecialName, typeof(GameWorld), Type.EmptyTypes);
        il = getter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
        var property = runtime.DefineProperty("World", PropertyAttributes.None, typeof(GameWorld), null); property.SetGetMethod(getter);
        var boot = runtime.DefineMethod("Boot", MethodAttributes.Public | MethodAttributes.Static, typeof(Task), new[] { typeof(GameWorld) });
        il = boot.GetILGenerator(); il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask)).GetGetMethod()); il.Emit(OpCodes.Ret);
        var reward = runtime.DefineMethod("ApplyXp", MethodAttributes.Private, typeof(void), Type.EmptyTypes);
        il = reward.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, getter);
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Key))); il.Emit(OpCodes.Ldc_R4, 10f);
        il.Emit(OpCodes.Call, reserveMethod); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
        if (extraWriter)
        {
            var writer = runtime.DefineMethod("OtherReward", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            il = writer.GetILGenerator(); il.Emit(OpCodes.Ldstr, "unguarded"); il.Emit(OpCodes.Ldc_R4, 10f);
            il.Emit(OpCodes.Call, reserveMethod); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
        }
        var runtimeType = runtime.CreateType();
        var start = module.DefineType("SkillsExtended.Skills.Shared.Patches.OnGameStartedPatch", TypeAttributes.NotPublic);
        var player = start.DefineField("Player", typeof(Player), FieldAttributes.Private | FieldAttributes.Static);
        var callbacks = new MethodBuilder[3];
        for (int i = 0; i < callbacks.Length; ++i)
        {
            callbacks[i] = start.DefineMethod(new[] { "ApplyMedicalXp", "ApplyNatoRifleXp", "ApplyEasternRifleXp" }[i],
                MethodAttributes.Private | MethodAttributes.Static, typeof(void), new[] { i == 0 ? typeof(IHealthEffect) : typeof(Mastering) });
            il = callbacks[i].GetILGenerator(); il.Emit(OpCodes.Ldsfld, player); il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Credit))); il.Emit(OpCodes.Ret);
        }
        var started = start.DefineMethod("Postfix", MethodAttributes.Private | MethodAttributes.Static, typeof(void), new[] { typeof(GameWorld) });
        il = started.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, typeof(GameWorld).GetField(nameof(GameWorld.MainPlayer)));
        il.Emit(OpCodes.Stsfld, player);
        for (int i = 0; i < callbacks.Length; ++i)
        {
            if (i == 2 && missingSubscription) continue;
            il.Emit(OpCodes.Ldsfld, player);
            il.Emit(OpCodes.Callvirt, typeof(Player).GetProperty(i == 0 ? nameof(Player.ActiveHealthController) : nameof(Player.Skills)).GetGetMethod());
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldftn, callbacks[i]);
            var delegateType = i == 0 ? typeof(Action<IHealthEffect>) : typeof(Action<Mastering>);
            il.Emit(OpCodes.Newobj, delegateType.GetConstructor(new[] { typeof(object), typeof(IntPtr) }));
            il.Emit(OpCodes.Callvirt, i == 0 ? typeof(ActiveHealthController).GetEvent(nameof(ActiveHealthController.EffectStartedEvent)).GetAddMethod()
                : typeof(SkillManager).GetEvent(nameof(SkillManager.OnMasteringExperienceChanged)).GetAddMethod());
        }
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(DoorFix))); il.Emit(OpCodes.Ret);
        if (extraBodyReader)
        {
            var reader = start.DefineMethod("UnknownBodyConsumer", MethodAttributes.Private | MethodAttributes.Static, typeof(Player), Type.EmptyTypes);
            il = reader.GetILGenerator(); il.Emit(OpCodes.Ldsfld, player); il.Emit(OpCodes.Ret);
        }
        var startType = start.CreateType();
        return new(plugin.CreateType(), runtimeType, runtimeType.GetMethod("Boot"),
            runtimeType.GetMethod("ApplyXp", BindingFlags.NonPublic | BindingFlags.Instance),
            startType.GetMethod("Postfix", Static), startType.GetMethod("ApplyMedicalXp", Static),
            startType.GetMethod("ApplyNatoRifleXp", Static), startType.GetMethod("ApplyEasternRifleXp", Static), attr);
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false; ArenaSkillsExtendedCompat.Shutdown();
        typeof(ArenaSkillsExtendedCompat).GetField("_reportedNewer", Static).SetValue(null, false);
        Chainloader.PluginInfos.Clear(); Harmony.Bound.Clear(); Harmony.FailPatch = false; AccessTools.MissingSettings = false;
        LocalGameAccessor.FailRead = false;
        ArenaSession.ArmedForNextRaid = ArenaSession.IsActive = false;
        ArenaSession.ArmedMap = null; ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null; Singleton<IBotGame>.Instance = null;
        ArenaMode.Plugin.ModLogger.Info.Clear(); ArenaMode.Plugin.ModLogger.Warnings.Clear();
        Receipts.Clear(); _medical = _nato = _eastern = _doors = _other = 0;
    }
    private static void Install(Fixture fixture, bool nativeReceipt = true)
    {
        Chainloader.PluginInfos[ArenaSkillsExtendedContract.PluginGuid] = new()
        { Instance = (BaseUnityPlugin)Activator.CreateInstance(fixture.Plugin), Metadata = fixture.Metadata };
        Harmony.Bound[NativeStart] = new();
        if (nativeReceipt) Harmony.Bound[NativeStart].Postfixes.Add(new() { owner = "foreign", PatchMethod = fixture.Started });
    }
    private static LocalGame World(bool armed = false, string profileId = "same-profile")
    {
        var world = new GameWorld { LocationId = "bigmap" };
        var profile = new Profile { Id = profileId };
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
    private static object Runtime(Fixture f, GameWorld world)
    { var runtime = Activator.CreateInstance(f.Runtime); f.Runtime.GetField("_world").SetValue(runtime, world); return runtime; }
    public static bool Call(MethodInfo method, object instance, params object[] args)
    {
        bool run = true;
        var info = Harmony.GetPatchInfo(method);
        foreach (var patch in info?.Prefixes.ToArray() ?? Array.Empty<Patch>())
        {
            object[] supplied = patch.PatchMethod.GetParameters().Select(p => p.Name == "__instance" ? instance : args[int.Parse(p.Name[2..])]).ToArray();
            var result = patch.PatchMethod.Invoke(null, supplied);
            if (result is false) run = false;
        }
        if (run) method.Invoke(instance, args);
        foreach (var patch in info?.Postfixes.ToArray() ?? Array.Empty<Patch>())
        {
            object[] supplied = patch.PatchMethod.GetParameters().Select(p => args[int.Parse(p.Name[2..])]).ToArray();
            patch.PatchMethod.Invoke(null, supplied);
        }
        return run;
    }

    private static void ContractCases()
    {
        foreach (string version in new[] { "3.1.4", "3.1.5", "4.0.0" })
        {
            Reset(); var f = Make(version); Install(f);
            Need(ArenaSkillsExtendedCompat.GetRejectionReason() == null, "reviewed and forward contracts accepted");
            Need(ArenaSkillsExtendedCompat.GetRejectionReason() == null && Harmony.GetPatchInfo(f.Reward).Prefixes.Count == 1, "idempotent adapter");
            Need(ArenaMode.Plugin.ModLogger.Warnings.Count == (version == "3.1.4" ? 0 : 1), "forward version warning once");
        }
        var invalid = new[] { Make("3.1.3"), Make(mismatch: "3.1.5"), Make(assemblyName: "wrong"),
            Make(badAttribute: true), Make(extraWriter: true), Make(extraBodyReader: true), Make(missingSubscription: true) };
        foreach (var f in invalid)
        {
            Reset(); Install(f);
            Need(ArenaSkillsExtendedCompat.GetRejectionReason() != null, "unsafe contract rejected");
            var game = World(); Call(f.Reward, Runtime(f, game.GameWorld));
            Need(Receipts.Count == 1, "rejected contract leaves ordinary progression alone");
        }
        Reset(); var missing = Make(); Install(missing, false);
        Need(ArenaSkillsExtendedCompat.GetRejectionReason() != null, "missing native subscription receipt rejected");
        Reset(); ArenaSkillsExtendedCompat.Initialize(); Need(ArenaSkillsExtendedCompat.GetRejectionReason() == null, "absent mod optional");
        var later = Make(); Install(later); Need(ArenaSkillsExtendedCompat.GetRejectionReason() == null, "late plugin discovered");
    }

    private static void LifecycleCases()
    {
        Reset(); var f = Make(); Install(f); ArenaSkillsExtendedCompat.Initialize();
        var normal = World(profileId: "ordinary-before"); var oldOrdinary = Runtime(f, normal.GameWorld);
        Call(f.Started, null, normal.GameWorld);
        normal.LocalPlayer.ActiveHealthController.Fire(); normal.LocalPlayer.Skills.Fire(); Call(f.Reward, oldOrdinary);
        Need(_medical == 1 && _nato == 1 && _eastern == 1 && Receipts["ordinary-before"] == 10, "ordinary XP and receipt positive control");
        var arena = World(true, "arena"); var runtime = Runtime(f, arena.GameWorld); var initial = arena.LocalPlayer;
        initial.ActiveHealthController.EffectStartedEvent += OtherMedical; initial.Skills.OnMasteringExperienceChanged += OtherMastering;
        Call(f.Boot, null, arena.GameWorld); Call(f.Started, null, arena.GameWorld);
        Need(_doors == 2, "original door fix retained");
        Need(initial.ActiveHealthController.Subscribers == 1 && initial.Skills.Subscribers == 1, "only foreign Arena XP handlers detached");
        initial.ActiveHealthController.Fire(); initial.Skills.Fire();
        Call(f.Medical, null, new Effect()); Call(f.Nato, null, new Mastering()); Call(f.Eastern, null, new Mastering());
        Need(_medical == 1 && _nato == 1 && _eastern == 1 && _other == 2, "Arena callbacks suspended while other subscribers run");
        Need(!Call(f.Reward, runtime) && !Receipts.ContainsKey("arena"), "Arena Signals never reserve a receipt");
        ArenaSession.IsActive = true; ArenaSession.ActiveGame = arena; ArenaSession.ActiveWorld = arena.GameWorld;
        ArenaSession.ArmedForNextRaid = false; ArenaServerClient.HasPendingArm = false;
        for (int swap = 0; swap < 4; ++swap)
        {
            arena.LocalPlayer = arena.GameWorld.MainPlayer = new Player { GameWorld = arena.GameWorld, Profile = arena.Profile };
            arena.LocalPlayer.ActiveHealthController.Fire(); arena.LocalPlayer.Skills.Fire();
            Need(!Call(f.Reward, runtime) && _medical == 1 && _nato == 1 && _eastern == 1,
                "initial-kit death respawn and live PFT replacements stay suspended");
        }
        // An ordinary receipt work item retains its own world while Arena is active.
        Need(Call(f.Reward, oldOrdinary) && Receipts.Count == 1, "pending ordinary reward survives Arena");
        ArenaSession.IsActive = false;
        var next = World(profileId: "ordinary-after"); Call(f.Started, null, next.GameWorld);
        next.LocalPlayer.ActiveHealthController.Fire(); next.LocalPlayer.Skills.Fire(); Call(f.Reward, Runtime(f, next.GameWorld));
        Need(_medical == 2 && _nato == 2 && _eastern == 2 && Receipts.Count == 2, "next ordinary raid fully resumes");
        initial.ActiveHealthController.Fire(); initial.Skills.Fire();
        Need(_medical == 2 && _nato == 2 && _eastern == 2, "retired Arena body cannot credit later ordinary player");
        arena.GameWorld.MainPlayer = next.LocalPlayer; // Delayed stale foreign runtime after singleton/body reuse.
        Need(!Call(f.Reward, runtime) && Receipts.Count == 2, "late Arena runtime cannot write next ordinary receipt");
        Need(ArenaMode.Plugin.ModLogger.Info.Count == 1, "scoped limitation logged once per Arena world");
    }

    private static void ScopeCases()
    {
        Reset(); var f = Make(); Install(f); ArenaSkillsExtendedCompat.Initialize();
        void Allowed(string label, Action<LocalGame> change)
        {
            var game = World(true); change(game);
            Need(Call(f.Reward, Runtime(f, game.GameWorld)), label);
        }
        Allowed("unconfirmed arm untouched", g => ArenaServerClient.HasPendingArm = false);
        Allowed("wrong arm token untouched", g => ArenaServerClient.PendingArmToken = "bad");
        Allowed("start request marker is not confirmation", g => g._raidSettings.serverId = Protocol.BuildNativeStartIdentity(Token));
        Allowed("wrong armed map untouched", g => ArenaSession.ArmedMap = "factory4_day");
        Allowed("wrong native map untouched", g => g._raidSettings.location = "factory4_day");
        Allowed("Scav remains ordinary", g => g._raidSettings.playerSide = ESideType.Savage);
        Allowed("stopped world not newly leased", g => g.Status = GameStatus.Stopped);
        Allowed("wrong current world untouched", g => Singleton<GameWorld>.Instance = new() { LocationId = "bigmap" });
        Allowed("wrong current game world untouched", g => {
            var other = new LocalGame { GameWorld = new(), _raidSettings = g._raidSettings, Profile = g.Profile, LocalPlayer = g.LocalPlayer };
            Singleton<AbstractGame>.Instance = other; Singleton<IBotGame>.Instance = other;
        });
        Allowed("wrong bot game untouched when started", g => Singleton<IBotGame>.Instance = new LocalGame());
        Allowed("loading local profile mismatch untouched", g => { g.Status = GameStatus.Running; g.Profile = new(); });
        Allowed("loading body/world mismatch untouched", g => { g.Status = GameStatus.Running; g.GameWorld.MainPlayer = new(); });
        Allowed("loading nonlocal body untouched", g => { g.Status = GameStatus.Running; g.LocalPlayer.IsYourPlayer = false; });
        // Real first InitLevel Boot has no LocalGame yet. The SpawnLoot
        // completion repeats Boot after Running/native identity/player creation.
        var earlyWorld = new GameWorld { LocationId = "bigmap" };
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = earlyWorld;
        Singleton<IBotGame>.Instance = null;
        int announced = ArenaMode.Plugin.ModLogger.Info.Count;
        Call(f.Boot, null, earlyWorld);
        Need(ArenaMode.Plugin.ModLogger.Info.Count == announced, "initial pre-game Boot cannot claim ownership");
        var loading = World(true); loading.Status = GameStatus.Running;
        loading.GameWorld = earlyWorld; loading.LocalPlayer.GameWorld = earlyWorld; earlyWorld.MainPlayer = loading.LocalPlayer;
        Singleton<GameWorld>.Instance = earlyWorld;
        Singleton<IBotGame>.Instance = null;
        Call(f.Boot, null, loading.GameWorld);
        Need(ArenaMode.Plugin.ModLogger.Info.Count == announced + 1, "second Boot captures confirmed loading world before bots");
        World(); // Loading cancelled before any XP delivery or foreign OnGameStarted.
        Need(!Call(f.Reward, Runtime(f, loading.GameWorld)), "failed loading late reward remains owned");
        var scope = new ArenaSkillsExtendedScope(); var w1 = new object(); var w2 = new object(); var body = new object();
        Need(!scope.OwnsWorld(w1, w2, true), "foreign same-map world not captured");
        Need(!scope.OwnsWorld(w1, w1, false), "ordinary world not captured");
        Need(scope.OwnsWorld(w1, w1, true) && scope.OwnsBody(body, w1), "exact physical body captured");
        Need(!scope.OwnsBody(body, w2), "reused body loses old-world authority");
    }

    private static void FailureCases()
    {
        Reset(); var f = Make(); Install(f); Harmony.FailPatch = true;
        Need(ArenaSkillsExtendedCompat.GetRejectionReason() != null, "partial install rejects");
        Need(Harmony.GetPatchInfo(f.Boot).Prefixes.Count == 0, "partial install rolled back");
        Reset(); f = Make(); Install(f); AccessTools.MissingSettings = true;
        Need(ArenaSkillsExtendedCompat.GetRejectionReason() != null, "native identity field drift rejects");
        Reset(); f = Make(); Install(f); ArenaSkillsExtendedCompat.Initialize();
        Harmony.Bound[f.Reward].Prefixes.Clear();
        Need(ArenaSkillsExtendedCompat.GetRejectionReason() != null, "removed reward guard rejects");
        Reset(); f = Make(); Install(f); ArenaSkillsExtendedCompat.Initialize(); var game = World();
        LocalGameAccessor.FailRead = true;
        Need(Call(f.Reward, Runtime(f, game.GameWorld)), "unknown ordinary identity fails open");
        Need(ArenaSkillsExtendedCompat.GetRejectionReason() != null, "runtime fault rejects next Arena");
        Reset(); f = Make(); Install(f); ArenaSkillsExtendedCompat.Initialize(); var arena = World(true);
        Call(f.Boot, null, arena.GameWorld); World(); LocalGameAccessor.FailRead = true;
        Need(!Call(f.Reward, Runtime(f, arena.GameWorld)), "known old Arena scope survives unrelated identity failure");
        Reset(); f = Make(); Install(f); ArenaSkillsExtendedCompat.Initialize();
        Harmony.Bound[f.Reward].Prefixes.Add(new() { owner = "another", PatchMethod = typeof(Program).GetMethod(nameof(DoorFix)) });
        ArenaSkillsExtendedCompat.Shutdown();
        Need(Harmony.Bound[f.Reward].Prefixes.Count == 1 && Harmony.Bound[NativeStart].Postfixes.Count == 1, "shutdown leaves foreign patches");
        Reset(); f = Make(); Install(f); ArenaSkillsExtendedCompat.Initialize(); Harmony.FailUnpatch = true;
        ArenaSkillsExtendedCompat.Shutdown(); var after = World(true);
        Need(Call(f.Reward, Runtime(f, after.GameWorld)), "surviving hook inert after unpatch fault");
    }

    public static void Main(string[] args)
    {
        ContractCases(); LifecycleCases(); ScopeCases(); FailureCases(); Reset();
        if (args.Length >= 1) PublishedMetadata.Verify(args[0], Need);
        if (args.Length >= 2) PublishedMetadata.VerifyNative(args[1], Need);
        Console.WriteLine($"SKILLS_EXTENDED_COMPAT_CHECKS_PASSED ({_checks})");
    }
}
