using System.Diagnostics;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// Consumes explicitly approved receipts before native profiles/HTTP/timers start.
/// Does nothing on existing installations without the separate enrollment store.
/// No client route, enrollment producer, payout rate or live reload is installed.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Watermark + 1)]
public sealed class ArenaRewardStartup(
    SaveServer saves,
    IReadOnlyList<DependencyInjectionContainer> containers,
    ISptLogger<ArenaRewardStartup> logger) : IOnLoad, IDisposable
{
    private int _entered;
    private RewardStartupLifetimeLock? _lifetimeLock;

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _entered, 1) != 0) return;
        var root = RewardStartupFiles.Canonical(Directory.GetCurrentDirectory());
        // Absence is the untouched-install default; an access failure is not
        // absence and must not bypass interprocess coordination during a payout.
        FileAttributes stateAttributes;
        try { stateAttributes = File.GetAttributes(Path.Combine(root, RewardStartupFiles.RelativeDirectory)); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((stateAttributes & FileAttributes.Directory) == 0)
            throw new InvalidOperationException("Arena reward state path is not a directory.");
        // Every participating startup must wait BEFORE checking peers/refusing
        // rewards. Otherwise a second server could continue into native load while
        // the first is committing. Failure/cancellation here intentionally aborts
        // this startup; it is not swallowed by the reward-only refusal below.
        using var startupGate = await RewardStartupLifetimeLock.AcquireStartupAsync(root, cancellationToken);
        try
        {
            var reason = GetAdmissionFailure(
                typeof(SaveServer).Assembly.GetName().Version,
                Assembly.GetEntryAssembly()?.GetName().Name,
                root,
                AppContext.BaseDirectory,
                saves.GetProfiles().Count,
                containers.Select(c => new StartupCallback(c.ParentType, c.Type, c.InjectableAttribute.TypePriority)),
                FindConflictingServer(root));
            if (reason is not null) throw new RewardDataException(reason);
            if (!RewardStartupFiles.MarkerAbsent(root)) return;
            _lifetimeLock = RewardStartupLifetimeLock.Acquire(root);
            using var window = new RewardStartupWindow(root, () => saves.GetProfiles().Count == 0);
            foreach (var outcome in await RewardStartupProcessor.ProcessAsync(window, cancellationToken))
            {
                if (outcome.Result.Status == RewardSettlementStatus.Paid)
                    logger.Info($"[ArenaMode] Arena reward delivered by mail: {outcome.Roubles} RUB.");
                else if (outcome.Result.Status is RewardSettlementStatus.Quarantined or RewardSettlementStatus.Rejected)
                    logger.Warning($"[ArenaMode] Arena reward withheld ({outcome.Result.Reason}); no profile repair was attempted.");
                else
                    logger.Debug($"[ArenaMode] Startup reward: {outcome.Result.Status} ({outcome.Result.Reason}).");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            // A refusal affects rewards, never ordinary server startup or raids.
            var reason = e is RewardDataException ? e.Message : e.GetType().Name;
            logger.Warning($"[ArenaMode] Startup rewards unavailable ({reason}); profiles will load normally.");
        }
    }

    internal sealed record StartupCallback(Type Parent, Type Contract, int Priority);

    internal static string? GetAdmissionFailure(Version? version, string? entryName, string root, string applicationRoot,
        int loadedProfiles, IEnumerable<StartupCallback> callbacks, bool conflictingServer)
    {
        // The early Program callback was verified against installed 4.1.6. Unknown
        // platform contracts defer rewards; they do not make Arena unavailable.
        if (version != new Version(4, 1, 6, 0)) return "unreviewed-native-startup-version";
        if (entryName is not ("SPT.Server" or "SPT.Server.Linux" or "SPT.Server.Mac")) return "not-native-server-startup";
        if (!string.Equals(RewardStartupFiles.Canonical(root), RewardStartupFiles.Canonical(applicationRoot), RewardStartupFiles.PathComparison))
            return "runtime-root-mismatch";
        if (loadedProfiles != 0) return "native-profiles-already-loaded";
        if (conflictingServer) return "another-server-or-unknown-process";
        if (callbacks.Any(c => c.Contract == typeof(IOnLoad) && c.Priority is >= OnLoadOrder.Watermark and <= OnLoadOrder.Watermark + 1
                               && c.Parent != typeof(ArenaRewardStartup) && c.Parent != typeof(Watermark)))
            return "earlier-startup-callback";
        return null;
    }

    private static bool FindConflictingServer(string root)
    {
        // The normal SPT port availability check runs AFTER pre-load callbacks.
        // Inspect peers first, and refuse if a potentially relevant process cannot
        // be identified. The lifetime file lock additionally covers cooperating
        // Arena versions. Arbitrarily renamed external profile writers are outside
        // this contract, just like editors or foreign constructor-spawned threads.
        if (!CurrentImageOwnsRoot(root, Environment.ProcessPath)) return true;
        using var current = Process.GetCurrentProcess();
        var processName = current.ProcessName;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    // A peer's executable directory does not prove its CWD: a
                    // second copy could still use our profile tree. V1 defers for
                    // every recognized server peer, including independent roots.
                    if (IsServerPeer(process.ProcessName, processName)) return true;
                }
                catch (InvalidOperationException) { if (!process.HasExited) return true; }
                catch (System.ComponentModel.Win32Exception) { return true; }
                catch (NotSupportedException) { return true; }
            }
        }
        return false;
    }

    internal static bool CurrentImageOwnsRoot(string root, string? image)
    {
        if (string.IsNullOrWhiteSpace(image) || !Path.IsPathFullyQualified(image)
            || !Path.GetFileName(image).StartsWith("SPT.Server", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            RewardStartupFiles.RequireLocalPath(root, image);
            return string.Equals(RewardStartupFiles.Canonical(Path.GetDirectoryName(image)!),
                RewardStartupFiles.Canonical(root), RewardStartupFiles.PathComparison);
        }
        catch { return false; }
    }

    internal static bool IsServerPeer(string candidateName, string currentName) =>
        candidateName.StartsWith("SPT.Server", StringComparison.OrdinalIgnoreCase)
        || string.Equals(candidateName, currentName, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _lifetimeLock?.Dispose();
}
