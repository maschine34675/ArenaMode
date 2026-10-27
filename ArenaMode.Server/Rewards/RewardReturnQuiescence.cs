using System.Reflection;
using HarmonyLib;
using Microsoft.AspNetCore.Http;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Hosted;

namespace ArenaMode.Server.Rewards;

/// <summary>Tracks the complete native request task, including WebSockets and the next middleware.</summary>
internal sealed class RewardReturnRequestGate
{
    private readonly object _sync = new();
    private int _active;
    private bool _held;
    private int _scopedHolds;
    private TaskCompletionSource _drained = Completed();
    internal bool Held { get { lock (_sync) return _held || _scopedHolds != 0; } }
    internal int Active { get { lock (_sync) return _active; } }

    internal IDisposable? TryEnter()
    {
        lock (_sync)
        {
            if (_held || _scopedHolds != 0) return null;
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Lease(this);
        }
    }
    internal Task PauseAndDrainAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            _held = true;
            return _drained.Task.WaitAsync(ct);
        }
    }
    internal void Resume() { lock (_sync) _held = false; }
    // The server-return owner and individual restore transactions must never
    // release each other's hold. A cancelled drain removes only its own hold.
    internal async Task<IDisposable> HoldAndDrainAsync(CancellationToken ct)
    {
        Task drain;
        lock (_sync) { _scopedHolds++; drain = _drained.Task; }
        var hold = new Hold(this);
        try { await drain.WaitAsync(ct).ConfigureAwait(false); return hold; }
        catch { hold.Dispose(); throw; }
    }
    private sealed class Hold(RewardReturnRequestGate owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync) owner._scopedHolds--;
        }
    }
    private void Leave()
    {
        lock (_sync) if (--_active == 0) _drained.TrySetResult();
    }
    private static TaskCompletionSource Completed()
    {
        var value = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        value.SetResult(); return value;
    }
    private sealed class Lease(RewardReturnRequestGate owner) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Leave(); }
    }
}

internal sealed class RewardReturnSequence(RewardReturnRequestGate requests, RewardReturnRequestGate? updates = null)
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly RewardReturnRequestGate _updates = updates ?? new();
    private volatile bool _updatesStopped;
    private volatile bool _readyToStop;
    internal bool UpdatesStopped => _updatesStopped;
    internal bool ReadyToStop => _readyToStop;

    internal async Task<RewardReturnReply> PrepareAsync(Func<bool> idle, Func<CancellationToken, Task> saveAndVerify,
        Func<CancellationToken, Task> stopUpdates, CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        string stage = "http-drain";
        try
        {
            if (ReadyToStop) return new(true, "ready");
            await requests.PauseAndDrainAsync(ct);
            // Native callbacks may be saving profiles. Drain their existing tasks without cancelling
            // them, and skip new callbacks before StopAsync cancels the native loop/timer token.
            stage = "update-drain";
            await _updates.PauseAndDrainAsync(ct);
            stage = "idle";
            if (!idle()) throw new RewardDataException("return-session-not-idle");
            if (!UpdatesStopped)
            {
                // Catch a stale native save hash/readback mismatch before stopping its non-resumable loop.
                stage = "preflight-save";
                await saveAndVerify(ct);
                // StopAsync may time out with an uncooperative foreign update still executing. Once
                // cancellation was requested, keep requests blocked and RAM alive even on failure.
                _updatesStopped = true;
                stage = "stop-updates";
                await stopUpdates(ct);
            }
            else { stage = "stop-updates"; await stopUpdates(ct); } // Retry still proves the task finished.
            stage = "idle";
            if (!idle()) throw new RewardDataException("return-session-not-idle");
            stage = "final-save";
            await saveAndVerify(ct);
            _readyToStop = true;
            return new(true, "ready");
        }
        catch (Exception exception)
        {
            if (!UpdatesStopped) { _updates.Resume(); requests.Resume(); }
            string reason = exception is RewardDataException data && data.Message is
                "return-session-not-idle" or "return-profile-set-unavailable" or "return-profile-unavailable"
                or "return-profile-size" or "return-profile-set-changed" or "return-profile-save-not-confirmed"
                or "return-profile-serialization-failed" or "duplicate-reward-property"
                or "reward-root-is-linked" or "reward-path-is-linked" or "reward-path-outside-root"
                or "return-update-loop-still-running" or "return-update-loop-faulted"
                ? data.Message : exception.GetType().Name;
            ArenaContext.Logger?.Debug($"[ArenaMode] Automatic reward return paused: {stage}/{reason}.");
            return new(false, UpdatesStopped ? "return-held-preserving-profile" : "return-not-ready", UpdatesStopped);
        }
        finally { _serial.Release(); }
    }
}

internal static class RewardReturnQuiescence
{
    private const string HarmonyId = "com.maschine.ArenaMode.return-request-gate";
    internal static readonly RewardReturnRequestGate Requests = new();
    internal static readonly RewardReturnRequestGate Updates = new();
    internal static readonly RewardReturnSequence Sequence = new(Requests, Updates);
    private static readonly object InstallSync = new();
    private static bool _dispatcherInstalled;
    internal static bool Enabled { get; private set; }

    internal static bool TryEnable()
    {
        if (Enabled || _dispatcherInstalled) return true;
        var harmony = new Harmony(HarmonyId);
        string stage = "native-version";
        try
        {
            if (typeof(HttpServer).Assembly.GetName().Version != new Version(4, 1, 6, 0))
                throw new NotSupportedException();
            stage = "http-contract";
            var target = typeof(HttpServer).GetMethod(nameof(HttpServer.HandleRequestAsync), BindingFlags.Public | BindingFlags.Instance,
                null, [typeof(HttpContext), typeof(RequestDelegate), typeof(CancellationToken)], null);
            if (target?.ReturnType != typeof(Task)) throw new MissingMethodException();
            stage = "update-contract";
            var execute = typeof(SPTStartupHostedService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance,
                null, [typeof(CancellationToken)], null);
            if (execute?.ReturnType != typeof(Task)) throw new MissingMethodException();
            _ = ResolveComponentField(typeof(SPTStartupHostedService));
            stage = "update-patch";
            // The actual Harmony build cannot rewrite this native MoveNext exception region.
            // Patch its small async ENTRY instead, before it starts the native dispatcher.
            harmony.Patch(execute, prefix: new HarmonyMethod(typeof(RewardReturnQuiescence), nameof(DispatcherPrefix)));
            stage = "http-patch";
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(RewardReturnQuiescence), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(RewardReturnQuiescence), nameof(Postfix)),
                finalizer: new HarmonyMethod(typeof(RewardReturnQuiescence), nameof(Finalizer)));
            _dispatcherInstalled = true;
            ArenaContext.Logger?.Debug("[ArenaMode] Automatic reward return: waiting for native callback registration.");
            return true;
        }
        catch (Exception exception)
        {
            RollBack(harmony);
            // Fixed stages and exception types only: this path must never expose a grant,
            // request body, profile contents or arbitrary exception messages.
            ArenaContext.Logger?.Debug($"[ArenaMode] Automatic reward return unavailable: {stage}/{exception.GetType().Name}.");
            return false;
        }
    }

    private static void DispatcherPrefix(SPTStartupHostedService __instance)
    {
        lock (InstallSync)
        {
            if (Enabled || !_dispatcherInstalled) return;
            var harmony = new Harmony(HarmonyId);
            string stage = "update-components";
            try
            {
                var components = ReadComponents(ResolveComponentField(typeof(SPTStartupHostedService)), __instance);
                var targets = ResolveUpdateTargets(components);
                stage = "update-component-patch";
                PatchTargets(targets, target => harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(RewardReturnQuiescence), nameof(UpdatePrefix)),
                    postfix: new HarmonyMethod(typeof(RewardReturnQuiescence), nameof(UpdatePostfix)),
                    finalizer: new HarmonyMethod(typeof(RewardReturnQuiescence), nameof(Finalizer))), () => RollBack(harmony));
                Enabled = true;
                ArenaContext.Logger?.Debug($"[ArenaMode] Automatic reward return: native request/update gates enabled ({targets.Length} callbacks).");
            }
            catch (Exception exception)
            {
                RollBack(harmony);
                ArenaContext.Logger?.Debug($"[ArenaMode] Automatic reward return unavailable: {stage}/{exception.GetType().Name}.");
            }
        }
    }

    internal static FieldInfo ResolveComponentField(Type dispatcher)
    {
        var fields = dispatcher.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(IEnumerable<IOnUpdate>)).ToArray();
        if (fields.Length != 1) throw new MissingFieldException();
        return fields[0];
    }

    internal static IOnUpdate[] ReadComponents(FieldInfo field, object dispatcher)
    {
        // Microsoft DI materializes this registration as IOnUpdate[]. Do not enumerate a
        // dynamic provider that could give the dispatcher a different second component set.
        if (field.GetValue(dispatcher) is not IOnUpdate[] { Length: > 0 and <= 1024 } components
            || components.Any(component => component is null)) throw new NotSupportedException();
        return components;
    }

    internal static MethodInfo[] ResolveUpdateTargets(IEnumerable<IOnUpdate> components)
    {
        var methods = new Dictionary<(Module, int), MethodInfo>();
        foreach (var component in components)
        {
            var type = component.GetType();
            var map = type.GetInterfaceMap(typeof(IOnUpdate));
            var original = typeof(IOnUpdate).GetMethod(nameof(IOnUpdate.OnUpdateAsync), [typeof(long), typeof(CancellationToken)])!;
            int index = Array.FindIndex(map.InterfaceMethods, method => method.Equals(original));
            if (index < 0) throw new MissingMethodException();
            var target = map.TargetMethods[index];
            if (target.IsAbstract || target.IsStatic || target.ContainsGenericParameters || target.DeclaringType is null
                || target.DeclaringType.IsInterface || target.DeclaringType.IsGenericType || target.GetMethodBody() is null
                || target.ReturnType != typeof(Task<bool>)
                || !target.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(long), typeof(CancellationToken)]))
                throw new MissingMethodException();
            methods.TryAdd((target.Module, target.MetadataToken), target);
        }
        if (methods.Count == 0) throw new MissingMethodException();
        return methods.Values.ToArray();
    }

    internal static void PatchTargets(IEnumerable<MethodInfo> targets, Action<MethodInfo> patch, Action rollback)
    {
        try { foreach (var target in targets) patch(target); }
        catch { try { rollback(); } catch { } throw; }
    }

    private static void RollBack(Harmony harmony)
    {
        Enabled = false;
        _dispatcherInstalled = false;
        try { harmony.UnpatchSelf(); } catch { }
    }

    private static bool UpdatePrefix(ref Task<bool> __result, out IDisposable? __state)
    {
        __state = null;
        return !Enabled || TryEnterUpdate(Updates, ref __result, out __state);
    }

    internal static bool TryEnterUpdate(RewardReturnRequestGate gate, ref Task<bool> result, out IDisposable? lease)
    {
        lease = gate.TryEnter();
        if (lease is not null) return true;
        result = Task.FromResult(false);
        return false;
    }

    private static void UpdatePostfix(ref Task<bool> __result, IDisposable? __state)
    {
        if (__state is not null) __result = TrackUpdate(__result, __state);
    }

    internal static async Task<bool> TrackUpdate(Task<bool> update, IDisposable lease)
    {
        try { return await update.ConfigureAwait(false); }
        finally { lease.Dispose(); }
    }

    private static bool Prefix(HttpContext __0, ref Task __result, out IDisposable? __state)
    {
        __state = null;
        if (!Enabled || __0.Request.Path == RewardReturnControl.Route || __0.Request.Path == RewardEntryControl.Route) return true;
        __state = Requests.TryEnter();
        if (__state is not null) return true;
        __result = Reject(__0);
        return false;
    }
    private static void Postfix(ref Task __result, IDisposable? __state)
    {
        if (__state is not null) __result = Track(__result, __state);
    }
    private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
    {
        if (__exception is not null) __state?.Dispose();
        return __exception;
    }
    internal static async Task Track(Task request, IDisposable lease)
    {
        try { await request.ConfigureAwait(false); }
        finally { lease.Dispose(); }
    }
    private static async Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.RetryAfter = "5";
        await context.Response.WriteAsync("Arena is returning to the normal session.", context.RequestAborted);
    }
}
