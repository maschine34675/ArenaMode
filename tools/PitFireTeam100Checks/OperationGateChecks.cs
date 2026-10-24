using System.Reflection;
using ArenaMode.Server.Services;

internal static class OperationGateChecks
{
    internal static void Verify(Action<bool, string> check)
    {
        check(RunCycle(retainWriterMutant: false), "PFT mode semaphore / delayed invite / callback cycle drains without holding Arena writer.");
        check(!RunCycle(retainWriterMutant: true), "Negative control: the old writer-held drain times out on the same cycle.");

        check(PitFireTeamOperationGate.TryReserveProfileBoundary(TimeSpan.FromSeconds(1), out var reservation), "Settled snapshot reservation succeeds.");
        var attempted = new ManualResetEventSlim();
        var entered = new ManualResetEventSlim();
        Task nextOperation = Task.Factory.StartNew(() =>
        {
            attempted.Set();
            using var operation = PitFireTeamOperationGate.EnterOperation();
            using var asyncEffect = PitFireTeamAsyncSideEffectGate.Enter();
            entered.Set();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        attempted.Wait(TimeSpan.FromSeconds(1));
        var gate = (ReaderWriterLockSlim)typeof(PitFireTeamOperationGate).GetField("Gate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        check(SpinWait.SpinUntil(() => gate.WaitingReadCount == 1, TimeSpan.FromSeconds(1)) && !entered.IsSet,
            "No new logical side effect can cross the reserved publication boundary.");
        reservation!.Dispose();
        check(nextOperation.Wait(TimeSpan.FromSeconds(1)) && entered.IsSet, "Ordinary operations resume after publication.");
    }

    private static bool RunCycle(bool retainWriterMutant)
    {
        using var mode = new SemaphoreSlim(1, 1);
        using var routeOwnsMode = new ManualResetEventSlim();
        using var releaseRoute = new ManualResetEventSlim();
        Task route = Task.Factory.StartNew(() =>
        {
            mode.Wait();
            routeOwnsMode.Set();
            releaseRoute.Wait();
            using (PitFireTeamOperationGate.EnterOperation()) { }
            mode.Release();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (!routeOwnsMode.Wait(TimeSpan.FromSeconds(1))) throw new Exception("Fixture route failed to acquire mode gate.");
        IDisposable initialRead = PitFireTeamOperationGate.EnterOperation();
        IDisposable pending = PitFireTeamAsyncSideEffectGate.Enter();
        Task invite = Task.Factory.StartNew(() =>
        {
            mode.Wait();
            pending.Dispose();
            mode.Release();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<bool> arm = Task.Factory.StartNew(() =>
        {
            if (retainWriterMutant)
            {
                using var oldBoundary = PitFireTeamOperationGate.ReserveProfileBoundary();
                return PitFireTeamAsyncSideEffectGate.WaitForDrain(TimeSpan.FromMilliseconds(300));
            }
            bool admitted = PitFireTeamOperationGate.TryReserveProfileBoundary(TimeSpan.FromSeconds(2), out var boundary);
            boundary?.Dispose();
            return admitted;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var gate = (ReaderWriterLockSlim)typeof(PitFireTeamOperationGate).GetField("Gate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        if (!SpinWait.SpinUntil(() => gate.WaitingWriteCount == 1, TimeSpan.FromSeconds(1))) throw new Exception("Fixture arm did not reach writer queue.");
        initialRead.Dispose();
        if (!SpinWait.SpinUntil(() => gate.WaitingWriteCount == 0, TimeSpan.FromSeconds(1))) throw new Exception("Fixture writer was not acquired.");
        releaseRoute.Set();
        if (!Task.WaitAll([route, invite, arm], TimeSpan.FromSeconds(3))) throw new Exception("PFT cycle fixture did not settle.");
        return arm.Result;
    }
}
