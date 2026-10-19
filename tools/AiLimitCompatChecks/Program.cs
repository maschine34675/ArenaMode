using System.Reflection;
using System.Reflection.Emit;
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
    private static int _checks, _originalCalls;
    private static readonly string Token = "b6c06414ec214e89a0bf16a7975d78b0";
    private static void Need(bool condition, string label)
    { _checks++; if (!condition) throw new InvalidOperationException("ASSERT: " + label); }

    public static void OriginalEnable() => _originalCalls++;
    private sealed record Fixture(Type Plugin, MethodInfo Enable, BepInPlugin Metadata);
    private static Fixture Make(string version = "1.9.2", string assemblyVersion = null,
        string metadataVersion = null, string assemblyName = "dvize.AILimit", string guid = "com.dvize.ailimit",
        bool badComponent = false, bool badSignature = false, bool indirectEntry = false, bool missingAttribute = false)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName)
            { Version = new(assemblyVersion ?? version) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("compat-fixture");
        var plugin = module.DefineType("AIlimit.AILimitPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        var attributeCtor = typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) });
        if (!missingAttribute) plugin.SetCustomAttribute(new CustomAttributeBuilder(attributeCtor, new object[] { guid, "fixture", version }));
        Type pluginType = plugin.CreateType();
        var component = module.DefineType("AILimit.AILimitComponent", TypeAttributes.Public,
            badComponent ? typeof(object) : typeof(UnityEngine.MonoBehaviour));
        var enable = component.DefineMethod("Enable", MethodAttributes.Public | MethodAttributes.Static,
            badSignature ? typeof(bool) : typeof(void), Type.EmptyTypes);
        var body = enable.GetILGenerator();
        body.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(OriginalEnable)));
        if (badSignature) body.Emit(OpCodes.Ldc_I4_0);
        body.Emit(OpCodes.Ret);
        Type componentType = component.CreateType();
        MethodInfo enableMethod = componentType.GetMethod("Enable");
        var entry = module.DefineType("AIlimit.NewGamePatch", TypeAttributes.NotPublic);
        var prefix = entry.DefineMethod("PatchPrefix", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var prefixBody = prefix.GetILGenerator();
        if (indirectEntry) prefixBody.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(OriginalEnable)));
        prefixBody.Emit(OpCodes.Call, enableMethod);
        if (badSignature) prefixBody.Emit(OpCodes.Pop);
        prefixBody.Emit(OpCodes.Ret);
        entry.CreateType();
        return new(pluginType, enableMethod, new(guid, "fixture", metadataVersion ?? version));
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false;
        ArenaAiLimitCompat.Shutdown();
        Chainloader.PluginInfos.Clear(); Harmony.Bound.Clear();
        Harmony.FailPatch = false; AccessTools.MissingSettings = false; LocalGameAccessor.FailRead = false;
        ArenaSession.ArmedForNextRaid = ArenaSession.IsActive = false;
        ArenaSession.ArmedMap = null; ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null; Singleton<IBotGame>.Instance = null;
        ArenaMode.Plugin.ModLogger.Info.Clear(); ArenaMode.Plugin.ModLogger.Warnings.Clear();
        _originalCalls = 0;
    }

    private static void Install(Fixture fixture)
        => Chainloader.PluginInfos[ArenaAiLimitContract.PluginGuid] = new()
        { Instance = (BaseUnityPlugin)Activator.CreateInstance(fixture.Plugin), Metadata = fixture.Metadata };

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

    // Execute the installed production prefix and then the original fixture.
    // This models Harmony bool-prefix admission, not native Mono detouring.
    private static bool Call(Fixture fixture)
    {
        bool run = true;
        var patches = Harmony.GetPatchInfo(fixture.Enable)?.Prefixes.ToArray() ?? Array.Empty<Patch>();
        foreach (var patch in patches)
            if (patch.PatchMethod != null && !(bool)patch.PatchMethod.Invoke(null, null)) run = false;
        if (run) fixture.Enable.Invoke(null, null);
        return run;
    }

    private static void ContractCases()
    {
        foreach (string version in new[] { "1.9.2", "1.9.3", "2.0.0" })
        {
            Reset(); var fixture = Make(version); Install(fixture);
            Need(ArenaAiLimitCompat.GetRejectionReason() == null, "minimum and newer matching contracts accepted " + version);
            Need(Harmony.GetPatchInfo(fixture.Enable)?.Prefixes.Count == 1, "one guard installed");
            Need(ArenaAiLimitCompat.GetRejectionReason() == null && Harmony.GetPatchInfo(fixture.Enable).Prefixes.Count == 1, "idempotent discovery");
        }
        var invalid = new[] { Make("1.9.1"), Make(assemblyVersion: "1.9.3"), Make(metadataVersion: "1.9.3"),
            Make(assemblyName: "not-ai-limit"), Make(guid: "other"), Make(badComponent: true),
            Make(badSignature: true), Make(indirectEntry: true), Make(missingAttribute: true) };
        foreach (var fixture in invalid)
        {
            Reset(); Install(fixture);
            Need(ArenaAiLimitCompat.GetRejectionReason()?.Contains("Ordinary raids remain available") == true, "malformed contract rejects Arena");
            World(); Need(Call(fixture) && _originalCalls == 1, "malformed contract leaves ordinary original");
        }
        Reset(); ArenaAiLimitCompat.Initialize();
        Need(ArenaAiLimitCompat.GetRejectionReason() == null, "optional plugin absent");
        var later = Make(); Install(later);
        Need(ArenaAiLimitCompat.GetRejectionReason() == null && Harmony.GetPatchInfo(later.Enable).Prefixes.Count == 1, "late plugin initialization discovered before arming");
    }

    private static void ScopeCases()
    {
        Reset(); var fixture = Make(); Install(fixture); ArenaAiLimitCompat.Initialize();
        Need(Call(fixture), "menu without world runs original");
        var game = World(); Need(Call(fixture), "ordinary PMC runs original");
        game._raidSettings.playerSide = ESideType.Savage; Need(Call(fixture), "ordinary Scav runs original");
        var hideout = new HideoutGame(); Singleton<AbstractGame>.Instance = hideout; Singleton<IBotGame>.Instance = hideout;
        Need(Call(fixture), "hideout runs original");
        game = World(arm: true);
        int before = _originalCalls;
        Need(!Call(fixture) && _originalCalls == before, "confirmed early Arena bypasses component construction");
        Need(!Call(fixture) && ArenaMode.Plugin.ModLogger.Info.Count == 1, "repeated early enable stays paused and reports once");
        game._raidSettings.serverId = Protocol.BuildNativeStartIdentity(Token);
        Need(Call(fixture), "request-only start marker is not a completed native start");
        game._raidSettings.serverId = "native-server|arenamode:" + new string('0', 32);
        Need(Call(fixture), "wrong token runs original");
        game._raidSettings.serverId = Protocol.BuildNativeEndIdentity("native-server", Token);
        ArenaServerClient.PendingArmToken = "bad-token"; Need(Call(fixture), "malformed token runs original");
        ArenaServerClient.PendingArmToken = Token;
        ArenaServerClient.HasPendingArm = false; Need(Call(fixture), "unconfirmed selection runs original");
        ArenaServerClient.HasPendingArm = true;
        ArenaSession.ArmedForNextRaid = false; Need(Call(fixture), "cancelled selection runs original");
        ArenaSession.ArmedForNextRaid = true;
        ArenaSession.ArmedMap = "bigmap"; Need(Call(fixture), "different armed map runs original");
        ArenaSession.ArmedMap = "factory4_day";
        game._raidSettings.location = "bigmap"; Need(Call(fixture), "native settings map mismatch runs original");
        game._raidSettings.location = "factory4_day";
        game._raidSettings.playerSide = ESideType.Savage; Need(Call(fixture), "stale arm cannot suppress Scav");
        game._raidSettings.playerSide = ESideType.Pmc;
        Singleton<GameWorld>.Instance = new() { LocationId = "factory4_day" }; Need(Call(fixture), "foreign same-map world runs original");
        Singleton<GameWorld>.Instance = game.GameWorld;
        Singleton<IBotGame>.Instance = new LocalGame(); Need(Call(fixture), "foreign bot game runs original");
        Singleton<IBotGame>.Instance = game;
        game.Status = GameStatus.Stopped; Need(Call(fixture), "stopped native raid runs original");
        game.Status = GameStatus.Started;
        var settings = game._raidSettings; game._raidSettings = null; Need(Call(fixture), "missing native settings runs original"); game._raidSettings = settings;

        ArenaSession.IsActive = true; ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld;
        ArenaSession.ArmedForNextRaid = false; ArenaServerClient.HasPendingArm = false; ArenaServerClient.PendingArmToken = null;
        Need(!Call(fixture), "exact active raid stays paused after token consumption");
        Need(!Call(fixture), "later enable attempts stay paused without a retained arm");
        World(); Need(Call(fixture), "same-map next ordinary raid survives stale active latch");
        ArenaSession.IsActive = false;
        Need(Call(fixture), "normal raid after cleanup runs original");
        ArenaAiLimitCompat.Shutdown(); World(arm: true);
        Need(Call(fixture), "shutdown removes only our suppression");
    }

    private static void FailureCases()
    {
        Reset(); var fixture = Make(); Install(fixture); Harmony.FailPatch = true;
        Need(ArenaAiLimitCompat.GetRejectionReason() != null, "partial patch failure rejects Arena");
        Need(Harmony.GetPatchInfo(fixture.Enable)?.Prefixes.Count == 0, "partial patch cleaned");
        World(); Need(Call(fixture), "partial failure ordinary positive control");
        Reset(); fixture = Make(); Install(fixture); AccessTools.MissingSettings = true;
        Need(ArenaAiLimitCompat.GetRejectionReason() != null, "native field drift rejects before arming");
        Need(Call(fixture), "native field drift original remains callable");
        Reset(); fixture = Make(); Install(fixture); ArenaAiLimitCompat.Initialize();
        Harmony.Bound[fixture.Enable].Prefixes.Clear();
        Need(ArenaAiLimitCompat.GetRejectionReason() != null, "removed guard detected by final pre-arm gate");
        Reset(); fixture = Make(); Install(fixture); ArenaAiLimitCompat.Initialize(); World(); LocalGameAccessor.FailRead = true;
        Need(Call(fixture), "runtime identity fault cannot suppress an unproven ordinary world");
        Need(ArenaAiLimitCompat.GetRejectionReason() != null, "runtime fault closes later Arena admission");
        Reset(); fixture = Make(); Install(fixture); ArenaAiLimitCompat.Initialize();
        Harmony.Bound[fixture.Enable].Prefixes.Add(new() { owner = "other-mod" });
        ArenaAiLimitCompat.Shutdown();
        Need(Harmony.GetPatchInfo(fixture.Enable).Prefixes.Count == 1 && Harmony.GetPatchInfo(fixture.Enable).Prefixes[0].owner == "other-mod", "shutdown preserves foreign patches");
        Reset(); fixture = Make(); Install(fixture); ArenaAiLimitCompat.Initialize(); Harmony.FailUnpatch = true;
        ArenaAiLimitCompat.Shutdown(); World(arm: true);
        Need(Call(fixture), "surviving prefix after unpatch failure is inert");
    }

    public static void Main(string[] args)
    {
        ContractCases(); ScopeCases(); FailureCases(); Reset();
        if (args.Length == 1) PublishedMetadata.Verify(args[0], Need);
        Console.WriteLine($"AI_LIMIT_COMPAT_CHECKS_PASSED ({_checks})");
    }
}
