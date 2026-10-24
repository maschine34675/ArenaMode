using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;

namespace ArenaMode.Server.Services;

/// <summary>
/// Closes the boundary between a complete PitFireTeam transaction and Arena's
/// main-profile snapshot. Every exact 0.10.0 persistence/management boundary
/// holds a read lease for its complete synchronous call. Team Arena briefly
/// takes the write lease, waits for older work to finish, publishes Preparing +
/// file protection, then releases it. New work can only resume after protection
/// is visible and is therefore suppressed as a whole.
/// </summary>
internal static class PitFireTeamOperationGate
{
    private static readonly ReaderWriterLockSlim Gate =
        new(LockRecursionPolicy.SupportsRecursion);

    public static IDisposable EnterOperation()
    {
        Gate.EnterReadLock();
        return new Lease(write: false);
    }

    public static IDisposable ReserveProfileBoundary()
    {
        Gate.EnterWriteLock();
        return new Lease(write: true);
    }

    /// <summary>
    /// PFT 1.0 serializes requests and delayed invites through its own semaphore.
    /// Never drain a delayed invite while retaining our writer: a route may own
    /// that semaphore while waiting for our read lease. Releasing the writer
    /// lets both finish; the final writer + zero pending check is atomic against
    /// new operations, which register their logical lease under a read lease.
    /// </summary>
    public static bool TryReserveProfileBoundary(TimeSpan timeout, out IDisposable? lease)
    {
        lease = null;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero || !Gate.TryEnterWriteLock(remaining)) return false;
            if (PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.Zero))
            {
                lease = new Lease(write: true);
                return true;
            }
            Gate.ExitWriteLock();
            remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero || !PitFireTeamAsyncSideEffectGate.WaitForDrain(remaining)) return false;
        }
    }

    private sealed class Lease(bool write) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (write)
                Gate.ExitWriteLock();
            else
                Gate.ExitReadLock();
        }
    }
}

/// <summary>
/// Logical, thread-independent leases for async PFT side effects. The normal
/// ReaderWriterLockSlim decides which side wins the publication race; while its
/// writer is held, Arena drains leases that already escaped into Task.Run before
/// taking the profile snapshot. A timeout rejects Arena instead of snapshotting
/// through an unresolved main-profile mutation.
/// </summary>
internal static class PitFireTeamAsyncSideEffectGate
{
    private static readonly object Sync = new();
    private static int _pending;
    private static TaskCompletionSource<bool> _drained = CompletedSource();

    public static IDisposable Enter()
    {
        lock (Sync)
        {
            if (_pending++ == 0)
            {
                _drained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        return new Lease();
    }

    public static bool WaitForDrain(TimeSpan timeout)
    {
        Task task;
        lock (Sync)
            task = _drained.Task;

        return task.Wait(timeout);
    }

    private static void Exit()
    {
        TaskCompletionSource<bool>? drained = null;
        lock (Sync)
        {
            if (_pending <= 0)
                return;

            if (--_pending == 0)
                drained = _drained;
        }

        drained?.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CompletedSource()
    {
        var source = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Exit();
        }
    }
}

/// <summary>
/// Marks only work scheduled by PFT's exact SendGroupInvite callback. Task.Run
/// captures this immutable AsyncLocal value; restoring the parent value after
/// callback return therefore does not erase the child task's origin.
/// </summary>
internal static class PitFireTeamInviteOriginScope
{
    private sealed class Marker(string sessionId, IDisposable logicalLease) : IDisposable
    {
        private IDisposable? _logicalLease = logicalLease;
        private int _taskScheduled;

        public string SessionId { get; } = sessionId;

        public bool TaskScheduled => Volatile.Read(ref _taskScheduled) != 0;

        public bool TryWrap(ref Func<Task> function)
        {
            if (function == null || Interlocked.CompareExchange(ref _taskScheduled, 1, 0) != 0)
                return false;

            Func<Task> original = function;
            function = async () =>
            {
                try
                {
                    await original().ConfigureAwait(false);
                }
                finally
                {
                    Dispose();
                }
            };
            return true;
        }

        public void Dispose()
            => Interlocked.Exchange(ref _logicalLease, null)?.Dispose();
    }

    private sealed record ScopeState(Marker Marker, Marker? Previous);

    private static readonly AsyncLocal<Marker?> Current = new();

    public static object Enter(MongoId sessionId)
    {
        Marker? previous = Current.Value;
        var marker = new Marker(sessionId.ToString(), PitFireTeamAsyncSideEffectGate.Enter());
        Current.Value = marker;
        return new ScopeState(marker, previous);
    }

    public static void Exit(object? state)
    {
        if (state is ScopeState scope)
        {
            Current.Value = scope.Previous;
            // No Task.Run means this exact PFT branch returned without queuing a
            // delayed side effect. Otherwise the wrapped child owns the lease
            // through its complete delay, notification and optional mail call.
            if (!scope.Marker.TaskScheduled)
                scope.Marker.Dispose();
        }
    }

    public static bool TryWrapCurrentTaskRun(ref Func<Task> function)
        => Current.Value?.TryWrap(ref function) == true;

    public static bool CurrentTaskRunIsTracked
        => Current.Value?.TaskScheduled == true;

    public static void ReleaseCurrentTaskRunAfterSchedulingFailure()
        => Current.Value?.Dispose();

    public static bool TryGetMatchingSession(object[] arguments, out MongoId sessionId)
    {
        Marker? marker = Current.Value;
        if (marker != null)
        {
            foreach (object? argument in arguments)
            {
                if (argument is MongoId candidate
                    && string.Equals(candidate.ToString(), marker.SessionId, StringComparison.OrdinalIgnoreCase))
                {
                    sessionId = candidate;
                    return true;
                }
            }
        }

        sessionId = default;
        return false;
    }
}

/// <summary>
/// PFT discards the Task returned by SendGroupInvite's Task.Run. Patch the exact
/// Func&lt;Task&gt; overload while its origin scope is active and put the logical
/// lease inside that child. Registration therefore happens synchronously before
/// scheduling, while the outer PFT read lease is still held, and release happens
/// only after the full delayed lambda (including decline mail) completes.
/// </summary>
internal static class PitFireTeamInviteTaskRunPatch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(ref Func<Task> function)
        => PitFireTeamInviteOriginScope.TryWrapCurrentTaskRun(ref function);

    [HarmonyPriority(Priority.Last)]
    public static Exception? Finalizer(Exception? __exception)
    {
        if (__exception != null)
            PitFireTeamInviteOriginScope.ReleaseCurrentTaskRunAfterSchedulingFailure();
        return __exception;
    }
}

/// <summary>
/// Separate patch class so Harmony gives this lease its own __state slot rather
/// than sharing ManagementServicePrefix's exact-session scope state.
/// </summary>
internal static class PitFireTeamOperationBoundaryPatch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(out IDisposable __state)
        => __state = PitFireTeamOperationGate.EnterOperation();

    [HarmonyPriority(Priority.Last)]
    public static Exception? Finalizer(Exception? __exception, IDisposable? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}

internal static class PitFireTeamInviteOriginPatch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(object[] __args, out object? __state)
    {
        __state = null;
        foreach (object? argument in __args)
        {
            if (argument is MongoId sessionId)
            {
                __state = PitFireTeamInviteOriginScope.Enter(sessionId);
                return;
            }
        }
    }

    [HarmonyPriority(Priority.Last)]
    public static Exception? Finalizer(Exception? __exception, object? __state)
    {
        PitFireTeamInviteOriginScope.Exit(__state);
        return __exception;
    }
}

internal sealed class PitFireTeamAsyncNotificationState(
    IDisposable synchronousLease,
    IDisposable logicalLease)
{
    private IDisposable? _synchronousLease = synchronousLease;
    private IDisposable? _logicalLease = logicalLease;
    private bool _logicalLeaseHandedOff;

    public void HandOff(ref Task result)
    {
        ReleaseSynchronous();
        IDisposable? lease = Interlocked.Exchange(ref _logicalLease, null);
        if (lease == null)
            return;

        _logicalLeaseHandedOff = true;
        result = AwaitAndRelease(result ?? Task.CompletedTask, lease);
    }

    public void ReleaseAfterSynchronousFailure()
    {
        ReleaseSynchronous();
        if (!_logicalLeaseHandedOff)
            Interlocked.Exchange(ref _logicalLease, null)?.Dispose();
    }

    private void ReleaseSynchronous()
        => Interlocked.Exchange(ref _synchronousLease, null)?.Dispose();

    private static async Task AwaitAndRelease(Task original, IDisposable lease)
    {
        try
        {
            await original.ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }
}

internal static class PitFireTeamDelayedInviteSideEffectPatch
{
    [HarmonyPriority(Priority.First)]
    public static bool NotificationPrefix(
        object[] __args,
        ref Task __result,
        out PitFireTeamAsyncNotificationState? __state)
    {
        __state = null;
        if (!PitFireTeamInviteOriginScope.TryGetMatchingSession(__args, out MongoId sessionId))
            return true;

        // The exact SendGroupInvite Task.Run already owns a logical lease from
        // before it was scheduled until after this notification and optional
        // mail. Taking a nested read lease here would deadlock against Arena's
        // writer, which is intentionally waiting for that child to drain.
        if (PitFireTeamInviteOriginScope.CurrentTaskRunIsTracked)
            return true;

        IDisposable synchronousLease = PitFireTeamOperationGate.EnterOperation();
        IDisposable logicalLease = PitFireTeamAsyncSideEffectGate.Enter();
        __state = new PitFireTeamAsyncNotificationState(synchronousLease, logicalLease);

        if (ArenaContext.Sessions?.IsExternalProfileWriteProtected(sessionId) != true)
            return true;

        ArenaContext.Logger?.Debug(
            $"[ArenaMode] Suppressed delayed PitFireTeam invite notification for protected session {sessionId}.");
        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    public static void NotificationPostfix(
        ref Task __result,
        PitFireTeamAsyncNotificationState? __state)
        => __state?.HandOff(ref __result);

    [HarmonyPriority(Priority.Last)]
    public static Exception? NotificationFinalizer(
        Exception? __exception,
        PitFireTeamAsyncNotificationState? __state)
    {
        __state?.ReleaseAfterSynchronousFailure();
        return __exception;
    }

    [HarmonyPriority(Priority.First)]
    public static bool MailPrefix(object[] __args, out IDisposable? __state)
    {
        __state = null;
        if (!PitFireTeamInviteOriginScope.TryGetMatchingSession(__args, out MongoId sessionId))
            return true;

        if (PitFireTeamInviteOriginScope.CurrentTaskRunIsTracked)
            return true;

        __state = PitFireTeamOperationGate.EnterOperation();
        if (ArenaContext.Sessions?.IsExternalProfileWriteProtected(sessionId) != true)
            return true;

        ArenaContext.Logger?.Debug(
            $"[ArenaMode] Suppressed delayed PitFireTeam invite mail for protected session {sessionId}.");
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    public static Exception? MailFinalizer(Exception? __exception, IDisposable? __state)
    {
        __state?.Dispose();
        return __exception;
    }
}
