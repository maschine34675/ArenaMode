using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Diagnostics;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using JsonType;
using Redeploy.Core.GameAccess;

using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Rebind continuous loading to exact Arena replacement bodies and retire delayed work.</summary>
internal static class ArenaContinuousLoadAmmoCompat
{
    private static readonly Harmony Patches = new(Plugin.PluginGuid + ".ContinuousLoadAmmoCompat");
    private static readonly ArenaContinuousLoadAmmoScope Scope = new();
    private static ArenaContinuousLoadAmmoContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _runtimeFault, _reportedNewer;
    private static string _failure, _lastWarning;
    private static int _reported;
    [ThreadStatic] private static DisposeFrame _disposing;
    private sealed class DisposeFrame { internal object Controller; internal DisposeFrame Previous; }

    internal static void Initialize() { if (_initialized) return; _initialized = true; Refresh(); }
    internal static string GetRejectionReason() { if (!_initialized) Initialize(); else Refresh(); return _failure; }
    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ArenaContinuousLoadAmmoContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; _runtimeFault = false; return; }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Continuous Load Ammo has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == ArenaContinuousLoadAmmoContract.Normalize(info.Metadata?.Version))
            { _contract.VerifyNativeBindings(); VerifyOwnPatches(); return; }
            ClearBinding();
            var contract = ArenaContinuousLoadAmmoContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out var version);
            _patchAttempted = true;
            Patch(contract.Register, nameof(BeforeRegister), nameof(AfterRegister));
            Patch(contract.Dispose, nameof(BeforeDispose), finalizer: nameof(AfterDispose));
            Patch(contract.OnDispose, nameof(BeforeStaticDispose));
            Patch(contract.State, nameof(BeforeState));
            Patch(contract.Outside, nameof(BeforeOutside));
            Patch(contract.EndPostfix, nameof(BeforeEndBroadcast));
            foreach (var start in contract.Starts) Patch(start, postfix: nameof(AfterProcessStart));
            _contract = contract; _plugin = info.Instance; _version = version;
            VerifyOwnPatches(); _failure = null;
            Plugin.ModLogger.LogDebug("Arena Continuous Load Ammo: exact-controller rebind and delayed completion guards ready.");
            if (version > ArenaContinuousLoadAmmoContract.ReviewedVersion && !_reportedNewer)
            { _reportedNewer = true; Plugin.ModLogger.LogWarning("Arena Continuous Load Ammo " + version + " is newer than reviewed 1.2.0; its APIs were checked, but runtime compatibility is unverified."); }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }
    private static void Patch(MethodInfo target, string prefix = null, string postfix = null, string finalizer = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix), finalizer: finalizer == null ? null : Hook(finalizer));
    private static HarmonyMethod Hook(string name) => new(typeof(ArenaContinuousLoadAmmoCompat), name) { priority = Priority.First };
    private static void VerifyOwnPatches()
    {
        void Check(MethodInfo target, string name, string kind)
        {
            var info = Harmony.GetPatchInfo(target);
            var entries = kind == "prefix" ? info?.Prefixes : kind == "postfix" ? info?.Postfixes : info?.Finalizers;
            var method = typeof(ArenaContinuousLoadAmmoCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (entries == null || entries.Count(p => p.owner == Patches.Id && p.PatchMethod == method) != 1)
                throw new InvalidOperationException("Continuous Load Ammo compatibility hook missing: " + name);
        }
        Check(_contract.Register, nameof(BeforeRegister), "prefix"); Check(_contract.Register, nameof(AfterRegister), "postfix");
        Check(_contract.Dispose, nameof(BeforeDispose), "prefix"); Check(_contract.Dispose, nameof(AfterDispose), "finalizer");
        Check(_contract.OnDispose, nameof(BeforeStaticDispose), "prefix"); Check(_contract.State, nameof(BeforeState), "prefix");
        Check(_contract.Outside, nameof(BeforeOutside), "prefix"); Check(_contract.EndPostfix, nameof(BeforeEndBroadcast), "prefix");
        foreach (var start in _contract.Starts)
        {
            Check(start, nameof(AfterProcessStart), "postfix");
            var capture = typeof(ArenaContinuousLoadAmmoCompat).GetMethod(nameof(AfterProcessStart), BindingFlags.NonPublic | BindingFlags.Static);
            var sorted = PatchProcessor.GetSortedPatchMethods(start, Harmony.GetPatchInfo(start).Postfixes.ToArray());
            // A completed Task can broadcast synchronously in the foreign postfix.
            // Check the real priority + before/after graph, not Priority.First alone.
            int captureIndex = sorted.IndexOf(capture), broadcastIndex = sorted.IndexOf(_contract.EndPostfix);
            if (captureIndex < 0 || broadcastIndex < 0 || captureIndex >= broadcastIndex)
                throw new InvalidOperationException("Continuous Load Ammo completion capture must run before its broadcast postfix");
        }
    }

    private static bool ExactRaid(GameWorld world)
    {
        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        bool pair = game != null && world != null && ReferenceEquals(game.GameWorld, world)
            && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, world);
        // Initial local registration precedes BotsController.Init, as in the movement adapter.
        bool initial = pair && game.Status == GameStatus.Running && ReferenceEquals(game.LocalPlayer, null)
            && world.MainPlayer is LocalPlayer p && p.IsYourPlayer && ReferenceEquals(p.Profile, game.Profile);
        pair &= initial || Singleton<IBotGame>.Instantiated && ReferenceEquals(Singleton<IBotGame>.Instance, game);
        var settings = pair ? LocalGameAccessor.GetLocalRaidSettings(game) : null;
        return ArenaContinuousLoadAmmoScope.OwnsRaid(pair,
            game != null && (game.Status == GameStatus.Running || game.Status == GameStatus.Starting || game.Status == GameStatus.Started),
            settings?.playerSide == ESideType.Pmc, world?.LocationId, settings?.location,
            pair && ArenaSession.IsExactActiveRaid(game, world), ArenaSession.ArmedForNextRaid,
            ArenaServerClient.HasPendingArm, ArenaSession.ArmedMap, ArenaServerClient.PendingArmToken, settings?.serverId);
    }
    private static bool BeforeRegister(GameWorld __0, IPlayer __1, out bool __state)
    {
        __state = false;
        if (_contract == null || __0 is HideoutGameWorld || __1 is not LocalPlayer body || !body.IsYourPlayer) return true;
        try
        {
            if (!ReferenceEquals(body.GameWorld, __0) || !ReferenceEquals(__0.MainPlayer, body) || !ExactRaid(__0)) return true;
            var old = _contract.Current.GetValue(null); var lease = Scope.Find(old);
            if (lease != null && !lease.Disposed && ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.Inventory, body.InventoryController)) return false;
            // Dispose before upstream replaces its static pointer; its own UI and preset cancellation stay authoritative.
            if (lease != null && !lease.Disposed) _contract.Dispose.Invoke(old, null);
            __state = true; return true;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static void AfterRegister(GameWorld __0, IPlayer __1, bool __state)
    {
        if (_contract == null || !__state || __1 is not Player body) return;
        try
        {
            var controller = _contract.Current.GetValue(null);
            if (controller == null || !ReferenceEquals(_contract.Body.GetValue(controller), body)
                || !ReferenceEquals(_contract.Inventory.GetValue(controller), body.InventoryController))
                throw new InvalidOperationException("Continuous Load Ammo registered an unrelated controller");
            var lease = Scope.BindController(controller, body, body.GameWorld, body.InventoryController, __0, __0.MainPlayer, ExactRaid(__0), body.IsYourPlayer);
            if (lease == null) throw new InvalidOperationException("Continuous Load Ammo registration lost exact Arena ownership");
            // The upstream global completion event has no body identity. Arena routes only the originating native process instead.
            _contract.EndEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), controller, _contract.LoadingEnd));
            Report(1, "bound current Arena inventory controller");
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeDispose(object __instance, out DisposeFrame __state)
    {
        __state = null;
        if (_contract == null) return true;
        var lease = Scope.Find(__instance);
        if (lease != null && !ArenaContinuousLoadAmmoScope.Retire(lease)) return false;
        __state = new DisposeFrame { Controller = __instance, Previous = _disposing }; _disposing = __state;
        if (lease != null)
        {
            // Upstream skips these removals when Unity already regards its body as destroyed.
            _contract.EndEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), __instance, _contract.LoadingEnd));
            _contract.CloseEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), __instance, _contract.Outside));
            Report(2, "retired old Arena controller and its global event subscriptions");
        }
        return true;
    }
    private static Exception AfterDispose(Exception __exception, DisposeFrame __state)
    {
        if (__state != null) _disposing = __state.Previous;
        return __exception;
    }
    private static bool BeforeStaticDispose()
    {
        if (_contract == null) return true;
        var current = _contract.Current.GetValue(null); var disposing = _disposing?.Controller;
        return ArenaContinuousLoadAmmoScope.MayClearStatic(disposing, current, Scope.Find(disposing) != null, Scope.Find(current) != null);
    }
    private static bool Current(ArenaContinuousLoadAmmoScope.Lease lease)
    {
        if (_contract == null || lease == null || !ReferenceEquals(Scope.Find(lease.Controller), lease)
            || lease.Body is not Player body || body == null) return false;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        return ArenaContinuousLoadAmmoScope.IsCurrent(lease, _contract.Current.GetValue(null), world, world?.MainPlayer,
            body.InventoryController, body.HealthController?.IsAlive == true, ExactRaid(world));
    }
    private static bool BeforeOutside(object __instance)
        => _contract == null || Scope.Find(__instance) is not { } lease || Current(lease);
    private static bool BeforeState(object __instance, bool __0, ref Task __result)
    {
        var lease = _contract == null ? null : Scope.Find(__instance);
        if (lease == null) return true;
        __result = Task.CompletedTask;
        if (!Current(lease)) return false;
        long ticket = ++lease.Revision;
        if (__0) { lease.HasLimit = true; return true; } // Keep the foreign starting animation/configuration.
        var body = (Player)lease.Body;
        body.MovementContext.SetPhysicalCondition(EPhysicalCondition.SprintDisabled, false);
        __result = FinishAfterDelay(lease, ticket);
        return false;
    }
    private static async Task FinishAfterDelay(ArenaContinuousLoadAmmoScope.Lease lease, long ticket)
    {
        await Task.Delay(800);
        try
        {
            if (_contract == null || !ArenaContinuousLoadAmmoScope.MayResume(lease, ticket, Current(lease),
                (bool)_contract.Active.GetValue(lease.Controller), (bool)_contract.MultiSelect.GetValue(null))) return;
            var body = (Player)lease.Body;
            if (body.HandsIsEmpty) body.TrySetLastEquippedWeapon();
            if (lease.HasLimit) { body.MovementContext.RemoveStateSpeedLimit(Player.ESpeedLimit.BarbedWire); lease.HasLimit = false; }
            Report(4, "completed current Arena load without crossing a replacement or newer load");
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void AfterProcessStart(object __instance, Task<IResult> __result)
    {
        if (_contract == null || __result == null) return;
        try
        {
            var inventory = __instance.GetType().GetField("_inventoryController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance);
            var lease = Scope.ForInventory(inventory);
            if (lease == null) return;
            var completion = Scope.CaptureTask(__result, lease);
            _ = CompleteOwnedProcess(__result, completion);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeEndBroadcast(Task<IResult> __0) => _contract == null || Scope.ForTask(__0) == null;
    private static async Task CompleteOwnedProcess(Task<IResult> task, ArenaContinuousLoadAmmoScope.Completion completion)
    {
        try
        {
            await task;
            // The native outer Load/UnloadMagazine continuation clears _loadProcess after Start completes.
            await Task.Yield();
            var lease = completion.Owner;
            if (_contract == null || !ArenaContinuousLoadAmmoScope.IsLatest(completion) || !Current(lease)
                || (bool)_contract.Active.GetValue(lease.Controller)) return;
            _contract.LoadingEnd.Invoke(lease.Controller, null);
        }
        catch (OperationCanceledException) { } // Native cancellation is expected, never a compatibility failure.
        catch (Exception ex) { Fault(ex); }
    }
    private static void Report(int bit, string message)
    {
        if (!ArenaLog.DeveloperEnabled || (_reported & bit) != 0) return;
        _reported |= bit; ArenaLog.Developer("Continuous Load Ammo: " + message + ".");
    }
    private static void Fault(Exception ex) { _runtimeFault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Arena cannot safely isolate Continuous Load Ammo: " + ex.GetBaseException().Message
            + ". Update or remove Continuous Load Ammo, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return; _lastWarning = _failure; Plugin.ModLogger.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        _contract = null; _plugin = null; _version = null; _disposing = null; _reported = 0; Scope.Clear();
        if (!_patchAttempted) return; _patchAttempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown() { ClearBinding(); _initialized = _runtimeFault = false; _failure = _lastWarning = null; }
}
