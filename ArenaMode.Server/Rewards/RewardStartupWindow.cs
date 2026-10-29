namespace ArenaMode.Server.Rewards;

/// <summary>
/// One synchronous pre-load callback, not a general live-server writer lock.
/// Native routes/timers have not started; unexpected profile publication closes
/// the opportunity. Foreign threads which ignore this lifecycle are unsupported.
/// </summary>
internal sealed class RewardStartupWindow(string root, Func<bool> nativeProfilesUnloaded) : IDisposable
{
    private bool _open = true;
    internal string Root { get; } = RewardStartupFiles.Canonical(root);
    internal bool IsOpen => _open && nativeProfilesUnloaded() && RewardStartupFiles.MarkerAbsent(Root);
    internal void RequireOpen()
    {
        if (!IsOpen) throw new RewardDataException("native-startup-boundary-unavailable");
    }
    public void Dispose() => _open = false;
}

internal sealed class RewardStartupLifetimeLock : IDisposable
{
    private readonly FileStream _stream;
    private RewardStartupLifetimeLock(FileStream stream) => _stream = stream;

    /// <summary>
    /// True when the file is held by someone else. Only that is the genuine contention these locks exist for;
    /// a missing folder, a too-long path or a disk fault is an unusable location (see ArenaRewardStartup).
    /// </summary>
    internal static bool IsContention(IOException exception) =>
        exception is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException
            or DriveNotFoundException or EndOfStreamException)
        && (!OperatingSystem.IsWindows() || (exception.HResult & 0xFFFF) is 32 or 33); // sharing / lock violation

    internal static RewardStartupLifetimeLock Acquire(string root)
    {
        var path = Path.Combine(root, RewardStartupFiles.RelativeDirectory, ".server.lock");
        RewardStartupFiles.RequireLocalPath(root, path, allowMissingLeaf: true);
        // Kept until server disposal, not just until the short payout callback ends.
        return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
    }

    internal static async Task<RewardStartupLifetimeLock> AcquireStartupAsync(string root, CancellationToken cancellationToken,
        TimeSpan? maximumWait = null)
    {
        var path = Path.Combine(root, RewardStartupFiles.RelativeDirectory, ".startup.lock");
        RewardStartupFiles.RequireLocalPath(root, path, allowMissingLeaf: true);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
            catch (IOException e) when (IsContention(e) && stopwatch.Elapsed < (maximumWait ?? TimeSpan.FromMinutes(2)))
            { await Task.Delay(25, cancellationToken); }
            catch (IOException e) when (IsContention(e))
            {
                // Continuing would let this server load profiles while the other
                // process is still settling them. This one exceptional startup
                // failure must propagate instead of merely disabling rewards.
                throw new InvalidOperationException("Arena reward startup coordination is still busy; stop the other server before retrying.", e);
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
