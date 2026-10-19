using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Compat;
using ArenaMode.Pickups;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;
using UnityEngine;

public static class Program
{
    private static int _checks, _originalCalls, _gcWrites;
    private static bool _foreignOption, _foreignRaidStarted, _simInRaid;
    private sealed record Fixture(Type Plugin, MethodInfo Cleanup, BepInPlugin Metadata);
    private static void Need(bool value, string name)
    { _checks++; if (!value) throw new InvalidOperationException("ASSERT: " + name); }

    // Independent foreign-boundary fixture, not Smoothie code. Observe whether
    // the production prefix admits calls, menu reset and an enabled GC writer.
    public static void OriginalCleanup()
    {
        _originalCalls++;
        if (!_simInRaid) _foreignRaidStarted = false;
        if (_foreignOption && _foreignRaidStarted) _gcWrites++;
    }

    private static Fixture Make(string version = "1.0.8", string assemblyVersion = null, string metadataVersion = null,
        string name = "Smoothie", string guid = "com.tominocz.smoothie", bool missingAttribute = false,
        bool wrongPlugin = false, bool badSignature = false, bool publicMethod = false, bool nonStaticType = false)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name)
            { Version = new(assemblyVersion ?? version) }, AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("independent-boundary-fixture");
        var plugin = module.DefineType(wrongPlugin ? "Other.Plugin" : "Smoothie.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        if (!missingAttribute) plugin.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(BepInPlugin).GetConstructor([typeof(string), typeof(string), typeof(string)]), [guid, "fixture", version]));
        var pluginType = plugin.CreateType();
        var diagnostics = module.DefineType("Smoothie.Diagnostics", nonStaticType ? TypeAttributes.NotPublic :
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = diagnostics.DefineMethod("KeepCleanupPaused", MethodAttributes.Static |
            (publicMethod ? MethodAttributes.Public : MethodAttributes.Assembly), badSignature ? typeof(bool) : typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(OriginalCleanup)));
        if (badSignature) il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        var target = diagnostics.CreateType().GetMethod("KeepCleanupPaused", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return new(pluginType, target, new(guid, "fixture", metadataVersion ?? version));
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false; ArenaSmoothieCompat.Shutdown();
        Harmony.Bound.Clear(); Harmony.FailPatch = Harmony.DropReceipt = false;
        AccessTools.MissingSettings = LocalGameAccessor.FailRead = false;
        Chainloader.PluginInfos.Clear();
        ArenaSession.IsActive = false; ArenaSession.ActiveGame = null; ArenaSession.ActiveWorld = null;
        Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null; Singleton<IBotGame>.Instance = null;
        CameraManager.Instance = null; GameObject.All.Clear();
        LayerMask.Layers = new() { ["Loot"] = 11, ["Interactive"] = 13 };
        _originalCalls = _gcWrites = 0; _foreignOption = _foreignRaidStarted = _simInRaid = false;
    }
    private static void Install(Fixture f)
        => Chainloader.PluginInfos[ArenaSmoothieContract.PluginGuid] = new()
        { Instance = (BaseUnityPlugin)Activator.CreateInstance(f.Plugin), Metadata = f.Metadata };
    private static LocalGame World(bool arena = false)
    {
        var game = new LocalGame { GameWorld = new() { LocationId = "factory4_day" },
            _raidSettings = new() { location = "factory4_day", playerSide = ESideType.Pmc } };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = game.GameWorld; Singleton<IBotGame>.Instance = game;
        ArenaSession.IsActive = arena;
        if (arena) { ArenaSession.ActiveGame = game; ArenaSession.ActiveWorld = game.GameWorld; }
        _simInRaid = _foreignRaidStarted = true;
        return game;
    }
    private static bool Call(Fixture f)
    {
        bool original = true;
        foreach (var p in Harmony.GetPatchInfo(f.Cleanup)?.Prefixes.ToArray() ?? [])
            if (!(bool)p.PatchMethod.Invoke(null, null)) original = false;
        if (original) f.Cleanup.Invoke(null, null);
        return original;
    }
    private static Camera Camera(int mask)
        => new GameObject("camera").AddComponent<Camera>() is var camera ? SetMask(camera, mask) : null;
    private static Camera SetMask(Camera camera, int mask) { camera.cullingMask = mask; return camera; }

    private static void ContractCases()
    {
        foreach (var version in new[] { "1.0.8", "1.0.9", "2.0.0" })
        {
            Reset(); var f = Make(version); Install(f);
            Need(ArenaSmoothieCompat.GetRejectionReason() == null, "checked version accepted " + version);
            Need(Harmony.GetPatchInfo(f.Cleanup)?.Prefixes.Count == 1, "checked contract installs prefix");
            ArenaSmoothieCompat.Initialize(); ArenaSmoothieCompat.GetRejectionReason();
            Need(Harmony.GetPatchInfo(f.Cleanup).Prefixes.Count == 1, "discovery is idempotent");
        }
        foreach (var f in new[] { Make("1.0.7"), Make(assemblyVersion:"1.0.9"), Make(metadataVersion:"1.0.9"),
            Make(name:"not-smoothie"), Make(guid:"other"), Make(missingAttribute:true), Make(wrongPlugin:true),
            Make(badSignature:true), Make(publicMethod:true), Make(nonStaticType:true) })
        {
            Reset(); Install(f); Need(ArenaSmoothieCompat.GetRejectionReason() == null, "invalid optional contract never blocks Arena");
            Need(Harmony.GetPatchInfo(f.Cleanup)?.Prefixes.Count is null or 0, "invalid optional contract installs nothing");
            World(true); Need(Call(f), "invalid contract preserves foreign original even in Arena");
            Need(ArenaSmoothieCompat.PickupMarkerLayer(Camera(-1)) == -1, "invalid contract does not guess visual exclusion");
        }
        Reset(); ArenaSmoothieCompat.Initialize(); Need(ArenaSmoothieCompat.GetRejectionReason() == null, "absent plugin allowed");
        Need(ArenaSmoothieCompat.PickupMarkerLayer(Camera(-1)) == -1, "absent plugin retains ordinary marker layers");
        var later = Make(); Install(later); ArenaSmoothieCompat.GetRejectionReason();
        Need(Harmony.GetPatchInfo(later.Cleanup)?.Prefixes.Count == 1, "late plugin discovered at admission");
        Reset(); later = Make();
        Chainloader.PluginInfos[ArenaSmoothieContract.PluginGuid] = new() { Metadata = later.Metadata };
        Need(ArenaSmoothieCompat.GetRejectionReason() == null && Harmony.Bound.Count == 0, "pending plugin metadata is not an adapter failure");
        Install(later); ArenaSmoothieCompat.GetRejectionReason();
        Need(Harmony.GetPatchInfo(later.Cleanup)?.Prefixes.Count == 1, "pending plugin can finish loading before admission");
    }

    private static void ScopeCases()
    {
        Reset(); var f = Make(); Install(f); ArenaSmoothieCompat.Initialize();
        Need(Call(f), "menu cleanup reset runs original");
        _foreignOption = true;
        var ordinary = World(); Need(Call(f) && _gcWrites == 1, "ordinary PMC keeps enabled foreign GC writer");
        ordinary._raidSettings.playerSide = ESideType.Savage;
        Need(Call(f) && _gcWrites == 2, "ordinary Scav keeps enabled foreign GC writer");
        var game = World(true); int before = _originalCalls;
        Need(!Call(f) && _originalCalls == before && _gcWrites == 2, "exact Arena isolates optional GC writer");
        Need(_foreignOption && _foreignRaidStarted, "Arena suppression never changes foreign state");
        for (int i = 0; i < 20; i++) Need(!Call(f), "repeated exact Arena invocation stays isolated");
        game._raidSettings.playerSide = ESideType.Savage; Need(Call(f), "stale active flag cannot suppress Scav");
        game._raidSettings.playerSide = ESideType.Pmc;
        game._raidSettings.location = "bigmap"; Need(Call(f), "native map mismatch runs original");
        game._raidSettings.location = "factory4_day";
        game.GameWorld.LocationId = ""; Need(Call(f), "empty world map runs original"); game.GameWorld.LocationId = "factory4_day";
        var settings = game._raidSettings; game._raidSettings = null; Need(Call(f), "missing native settings runs original"); game._raidSettings = settings;
        Singleton<GameWorld>.Instance = new() { LocationId = "factory4_day" };
        ArenaSession.ActiveWorld = Singleton<GameWorld>.Instance;
        Need(Call(f), "foreign world runs original");
        Singleton<GameWorld>.Instance = game.GameWorld; ArenaSession.ActiveWorld = game.GameWorld;
        Singleton<IBotGame>.Instance = new LocalGame(); Need(Call(f), "foreign bot game runs original"); Singleton<IBotGame>.Instance = game;
        ArenaSession.ActiveWorld = new(); Need(Call(f), "different active world cannot inherit Arena authority"); ArenaSession.ActiveWorld = game.GameWorld;
        ArenaSession.ActiveGame = new LocalGame(); Need(Call(f), "different active game cannot inherit Arena authority"); ArenaSession.ActiveGame = game;
        game.Status = GameStatus.Stopping; Need(Call(f), "stopping raid runs original"); game.Status = GameStatus.Started;
        ArenaSession.IsActive = false; Need(Call(f), "ended Arena runs original");
        _simInRaid = false; Singleton<GameWorld>.Instance = null;
        Need(Call(f) && !_foreignRaidStarted, "post Arena menu resets foreign lifecycle normally");
        World(); before = _gcWrites; Need(Call(f) && _gcWrites == before + 1, "ordinary after Arena gets its original GC writer");
        _foreignOption = false; before = _gcWrites; Need(Call(f) && _gcWrites == before, "default off foreign option remains off");
        var hideout = new HideoutGame(); Singleton<AbstractGame>.Instance = hideout; Singleton<IBotGame>.Instance = hideout;
        Need(Call(f), "hideout remains original");
    }

    private static void VisualCases()
    {
        Reset(); var f = Make(); Install(f); ArenaSmoothieCompat.Initialize(); World(true);
        var main = Camera((1 << 0) | (1 << 11) | (1 << 13));
        var optic = Camera((1 << 0) | (1 << 13));
        CameraManager.Instance = new() { Camera = main, OpticCameraManager = new() { Camera = optic } };
        Need(ArenaSmoothieCompat.PickupMarkerLayer(main) == 13, "both cameras must see the selected excluded layer");
        Need(ArenaSmoothieScope.MarkerLayer(true, true, 1, -1, 11, 13) == -1, "main camera mask cannot be bypassed by optic visibility");
        int mainMask = main.cullingMask, opticMask = optic.cullingMask;
        var weapon = new GameObject("native pooled weapon") { layer = 6 };
        var weaponChild = new GameObject("native pooled optic") { layer = 8 }; weaponChild.transform.SetParent(weapon.transform, false);
        var wall = new GameObject("Arena area wall") { layer = 0 };
        var view = new ArenaPickupView(new() { Id = "health", Kind = ArenaPickupKind.Health }, new(0,0,0));
        var root = GameObject.All.Single(g => g.name == "Arena pickup marker health");
        Need(root.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 13), "all private marker descendants excluded at construction");
        view.Tick(new() { Phase = ArenaPickupPhase.Available }, false, weapon);
        Need(weapon.layer == 6 && weaponChild.layer == 8 && wall.layer == 0 && weapon.transform.parent == null, "pool and wall layers and parent untouched");
        Need(main.cullingMask == mainMask && optic.cullingMask == opticMask, "native camera masks never changed");
        optic.cullingMask |= 1 << 11;
        view.Tick(new() { Phase = ArenaPickupPhase.Available }, false, weapon);
        Need(root.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 11), "camera changes reselect visible excluded layer");
        main.cullingMask = optic.cullingMask = 1;
        int warnings = ArenaMode.Plugin.ModLogger.Warnings.Count;
        view.Tick(new() { Phase = ArenaPickupPhase.Available }, false, weapon);
        Need(root.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 0), "unavailable visible exclusion preserves Default");
        for(int i=0;i<5;i++) view.Tick(new() { Phase = ArenaPickupPhase.Available }, false, weapon);
        Need(ArenaMode.Plugin.ModLogger.Warnings.Count == warnings + 1, "unavailable layer warns once not each frame");
        main.cullingMask = -1; CameraManager.Instance.OpticCameraManager = null;
        LayerMask.Layers["Loot"] = 32; LayerMask.Layers["Interactive"] = -1;
        Need(ArenaSmoothieCompat.PickupMarkerLayer(main) == -1, "missing and out of range layers are rejected");
        LayerMask.Layers["Loot"] = -1; LayerMask.Layers["Interactive"] = 31;
        Need(ArenaSmoothieCompat.PickupMarkerLayer(main) == 31, "valid high bit layer remains visible");
        Need(ArenaSmoothieCompat.PickupMarkerLayer(null) == -1, "unknown camera does not guess mask");
        view.Dispose(); Need(root.Destroyed && !weapon.Destroyed && !weaponChild.Destroyed && !wall.Destroyed, "dispose destroys private tree only");
        Reset(); f = Make(); Install(f); ArenaSmoothieCompat.Initialize();
        var late = new ArenaPickupView(new() { Id = "late", Kind = ArenaPickupKind.DoubleDamage }, new());
        root = GameObject.All.Single(g => g.name == "Arena pickup marker late");
        Need(root.layer == 0 && !root.activeSelf, "unknown camera creates inactive Default marker");
        late.Tick(new() { Phase = ArenaPickupPhase.Available }, false, null);
        Need(!root.activeSelf, "unknown camera keeps marker unavailable to active scene scans");
        CameraManager.Instance = new() { Camera = Camera(-1) };
        late.Tick(new() { Phase = ArenaPickupPhase.Available }, false, null);
        Need(root.layer == 11, "late camera safely updates only existing marker");
        late.Dispose();
    }

    private static void FailureCases()
    {
        foreach (var fault in new[] { "patch", "receipt", "settings" })
        {
            Reset(); var f = Make(); Install(f);
            Harmony.FailPatch = fault == "patch"; Harmony.DropReceipt = fault == "receipt"; AccessTools.MissingSettings = fault == "settings";
            int warnings = ArenaMode.Plugin.ModLogger.Warnings.Count;
            Need(ArenaSmoothieCompat.GetRejectionReason() == null, "adapter fault does not block Arena");
            World(true); Need(Call(f), "partial installation is inert");
            Need(ArenaSmoothieCompat.PickupMarkerLayer(Camera(-1)) == -1, "failed adapter does not change marker layers");
            for(int i=0;i<5;i++) ArenaSmoothieCompat.GetRejectionReason();
            Need(ArenaMode.Plugin.ModLogger.Warnings.Count <= warnings + 1, "adapter warnings bounded across repeated admission");
        }
        Reset(); var valid = Make(); Install(valid); ArenaSmoothieCompat.Initialize(); World(true);
        LocalGameAccessor.FailRead = true; Need(Call(valid), "runtime scope fault runs original");
        LocalGameAccessor.FailRead = false; Need(Call(valid), "runtime scope fault leaves adapter inert until restart");
        World(); Need(Call(valid), "ordinary calls stay original after runtime fault");
        Reset(); valid = Make(); Install(valid); ArenaSmoothieCompat.Initialize(); World(true);
        Harmony.FailUnpatch = true; ArenaSmoothieCompat.Shutdown();
        Need(Call(valid), "failed unpatch leaves surviving prefix inert");
        World(); Need(Call(valid), "ordinary calls remain original after failed unpatch");
        Reset(); valid = Make(); Install(valid); Harmony.FailPatch = Harmony.FailUnpatch = true;
        Need(ArenaSmoothieCompat.GetRejectionReason() == null, "partial patch plus failed cleanup never escalates initialization");
        World(true); Need(Call(valid), "partial patch surviving cleanup is inert");
        Reset(); valid = Make(); Install(valid); ArenaSmoothieCompat.Initialize();
        Harmony.Bound.Clear(); Need(ArenaSmoothieCompat.GetRejectionReason() == null, "removed receipt stays optional");
        Need(ArenaSmoothieCompat.PickupMarkerLayer(Camera(-1)) == -1, "removed receipt disables adapter");
        Reset(); valid = Make(); Install(valid); ArenaSmoothieCompat.Initialize();
        Chainloader.PluginInfos.Clear(); ArenaSmoothieCompat.GetRejectionReason(); World(true);
        Need(Call(valid) && Harmony.GetPatchInfo(valid.Cleanup)?.Prefixes.Count == 0, "unloaded optional mod removes only our patch");
    }

    public static int Main(string[] args)
    {
        try
        {
            ContractCases(); ScopeCases(); VisualCases(); FailureCases();
            if (args.Length > 0) PublishedMetadata.Verify(args[0], Need);
            Console.WriteLine($"SMOOTHIE_COMPAT_CHECKS_PASSED ({_checks})"); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Reset(); }
    }
}
