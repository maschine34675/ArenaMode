using System.Reflection;
using ArenaMode.Server.Rewards;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// A reward folder that cannot be used (the SPT folder or a parent is a link, access is denied, the path is
/// unavailable) turns rewards off; it must never keep SPT itself from starting. A held lock or a live private
/// owner stays fatal, because there something else may still own settlement.
/// </summary>
[Collection(RewardNativeLoadCollection.Name)]
public sealed class RewardStartupAdmissionTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ArenaMode.Admission.Tests", Guid.NewGuid().ToString("N"));
    private string Real => Path.Combine(_home, "real");
    private string Alias => Path.Combine(_home, "alias");

    public RewardStartupAdmissionTests()
    {
        Directory.CreateDirectory(Real);
        File.WriteAllText(Path.Combine(_home, ".owner"), "synthetic-admission");
    }

    private static SortedSet<string> Names(string root) => new(Directory.Exists(root)
        ? Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(root, p))
        : [], StringComparer.Ordinal);

    [Fact]
    public async Task ARootBehindALinkSkipsRewardsAndWritesNothing()
    {
        Directory.CreateSymbolicLink(Alias, Real);
        var before = Names(Real);

        var admission = await ArenaRewardStartup.AdmitAsync(RewardStartupFiles.Canonical(Alias), CancellationToken.None);

        Assert.Equal("reward-storage-linked", admission.Unusable);
        Assert.False(admission.Proceed);
        Assert.Null(admission.Owner);
        Assert.Equal(before, Names(Real));
    }

    [Fact]
    public async Task AFolderBelowALinkedRewardDirectorySkipsRewardsToo()
    {
        string user = Path.Combine(Real, "user"), elsewhere = Path.Combine(_home, "elsewhere");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(elsewhere);
        Directory.CreateSymbolicLink(Path.Combine(user, "arena-rewards"), elsewhere);

        var admission = await ArenaRewardStartup.AdmitAsync(RewardStartupFiles.Canonical(Real), CancellationToken.None);

        Assert.Equal("reward-storage-linked", admission.Unusable);
        Assert.Empty(Names(elsewhere));
    }

    [Fact]
    public async Task AGenuinelyHeldServerLockStillStopsStartupAndReleasesTheStartupGate()
    {
        string root = RewardStartupFiles.Canonical(Real);
        Assert.True(RewardStartupDefaults.PrepareDirectory(root));
        using (RewardStartupLifetimeLock.Acquire(root))
        {
            var error = await Assert.ThrowsAsync<RewardOfflineException>(() => ArenaRewardStartup.AdmitAsync(root, CancellationToken.None));
            Assert.Contains("owns this installation", error.Message);
        }
        // The failed attempt did not keep the startup gate: the next startup is not locked out.
        using var gate = await RewardStartupLifetimeLock.AcquireStartupAsync(root, CancellationToken.None, TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task APlainRootAdmitsAndHoldsBothLocks()
    {
        string root = RewardStartupFiles.Canonical(Real);
        var admission = await ArenaRewardStartup.AdmitAsync(root, CancellationToken.None);
        try
        {
            Assert.True(admission.Proceed);
            Assert.Null(admission.Unusable);
            Assert.NotNull(admission.Gate);
            Assert.NotNull(admission.Lifetime);
            Assert.Throws<IOException>(() => RewardStartupLifetimeLock.Acquire(root));
        }
        finally { admission.Gate?.Dispose(); admission.Lifetime?.Dispose(); }
    }

    [Fact]
    public async Task APrivateSessionRootBehindALinkIsNotSkipped()
    {
        // A private Arena server owns the original runtime's lifetime lock; it cannot opt out of that.
        File.WriteAllText(Path.Combine(Real, "arena-session.json"), "{}");
        Directory.CreateSymbolicLink(Alias, Real);
        await Assert.ThrowsAsync<RewardDataException>(() => ArenaRewardStartup.AdmitAsync(RewardStartupFiles.Canonical(Alias), CancellationToken.None));
    }

    [Fact]
    public async Task AnActivePrivateSessionBehindALinkIsNotSkippedEither()
    {
        // Another spelling of an install that a private session runs from: the pointer is visible through the link.
        Directory.CreateDirectory(Path.Combine(Real, "user", "arena-rewards"));
        File.WriteAllText(RewardOfflineSession.ActivePath(Real), "{}");
        Directory.CreateSymbolicLink(Alias, Real);
        await Assert.ThrowsAsync<RewardDataException>(() => ArenaRewardStartup.AdmitAsync(RewardStartupFiles.Canonical(Alias), CancellationToken.None));
    }

    [Fact]
    public void AnUnreadableRewardFolderIsNotMistakenForAnActivePrivateSession()
    {
        // The pointer lives inside the folder that is unusable; failing to look must not turn "skip" back into "stop".
        File.WriteAllText(Path.Combine(Real, "user"), "not a folder");
        string root = RewardStartupFiles.Canonical(Real);
        Assert.False(ArenaRewardStartup.LooksPrivate(root));
        Assert.Equal("reward-storage-access-denied", ArenaRewardStartup.UnusableStorage(new UnauthorizedAccessException(), root));
    }

    [Theory]
    [InlineData("Deferred", "rewards-enrolled-no-pending-result", "and rewards were enrolled again")]
    [InlineData("Quarantined", "profile-not-confirmed-clean", "did not succeed")]
    [InlineData("Rejected", "invalid-claim", "did not succeed")]
    [InlineData("Deferred", "startup-storage-unavailable", "did not succeed")]
    [InlineData("Deferred", "rewards-not-enabled", "nothing was enrolled")]
    public void TheRecoveredLogLineOnlyClaimsEnrollmentThatHappened(string status, string reason, string expected)
    {
        var notice = new RewardAuthorityNotice(true, "profile-reward-block-missing", "", "user/arena-rewards/x.archived-y");
        var outcome = new RewardStartupOutcome("0123456789abcdef01234567", new(Enum.Parse<RewardSettlementStatus>(status), reason), 0, 0, notice);
        string text = ArenaRewardStartup.DescribeRecovered(outcome, "the profile lost its reward ledger", notice);
        Assert.Contains(expected, text);
        Assert.Contains("x.archived-y", text);
        if (expected != "and rewards were enrolled again") Assert.DoesNotContain("were enrolled again", text);
    }

    [Fact]
    public void OnlyUnusableStorageIsSkippedNeverContentionOrOwnership()
    {
        string root = RewardStartupFiles.Canonical(Real);
        Assert.Equal("reward-storage-linked", ArenaRewardStartup.UnusableStorage(new RewardDataException("reward-root-is-linked"), root));
        Assert.Equal("reward-storage-linked", ArenaRewardStartup.UnusableStorage(new RewardDataException("reward-path-is-linked"), root));
        Assert.Equal("reward-storage-access-denied", ArenaRewardStartup.UnusableStorage(new UnauthorizedAccessException(), root));
        Assert.Equal("reward-storage-unavailable", ArenaRewardStartup.UnusableStorage(new DirectoryNotFoundException(), root));
        Assert.Equal("reward-storage-unavailable", ArenaRewardStartup.UnusableStorage(new PathTooLongException(), root));
        // Everything else keeps stopping startup.
        Assert.Null(ArenaRewardStartup.UnusableStorage(new RewardOfflineException("owner"), root));
        Assert.Null(ArenaRewardStartup.UnusableStorage(new InvalidOperationException("busy"), root));
        Assert.Null(ArenaRewardStartup.UnusableStorage(new RewardDataException("invalid-binding"), root));
        Assert.Null(ArenaRewardStartup.UnusableStorage(new OperationCanceledException(), root));
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(ArenaRewardStartup.UnusableStorage(new IOException("sharing", unchecked((int)0x80070020)), root));
            Assert.Null(ArenaRewardStartup.UnusableStorage(new IOException("lock", unchecked((int)0x80070021)), root));
            Assert.Equal("reward-storage-unavailable", ArenaRewardStartup.UnusableStorage(new IOException("disk full", unchecked((int)0x80070070)), root));
            Assert.Equal("reward-storage-unavailable", ArenaRewardStartup.UnusableStorage(new IOException("disk"), root));
        }
        // A marker in the folder makes it a private tree: no skipping.
        File.WriteAllText(Path.Combine(Real, "arena-session.json"), "{}");
        Assert.Null(ArenaRewardStartup.UnusableStorage(new UnauthorizedAccessException(), root));
    }

    [Fact]
    public void OnlyAHeldFileCountsAsContention()
    {
        Assert.False(RewardStartupLifetimeLock.IsContention(new FileNotFoundException()));
        Assert.False(RewardStartupLifetimeLock.IsContention(new DirectoryNotFoundException()));
        Assert.False(RewardStartupLifetimeLock.IsContention(new PathTooLongException()));
        if (OperatingSystem.IsWindows())
        {
            Assert.True(RewardStartupLifetimeLock.IsContention(new IOException("sharing", unchecked((int)0x80070020))));
            Assert.False(RewardStartupLifetimeLock.IsContention(new IOException("disk", unchecked((int)0x80070070))));
        }
        // And the real thing: a second open of a file held with FileShare.None.
        string path = Path.Combine(Real, "held.lock");
        using var held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var error = Assert.Throws<IOException>(() => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        Assert.True(RewardStartupLifetimeLock.IsContention(error));
    }

    [Fact]
    public async Task StartupThroughALinkLogsOneWarningAndReturns()
    {
        Directory.CreateSymbolicLink(Alias, Real);
        var logger = DispatchProxy.Create<ISptLogger<ArenaRewardStartup>, ArenaServerInstanceTests.RecordingLogger>();
        var log = (ArenaServerInstanceTests.RecordingLogger)(object)logger;
        var startup = new ArenaRewardStartup(null!, [], logger);
        var previous = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(Alias);
            await startup.OnLoadAsync(CancellationToken.None); // used to throw reward-root-is-linked and abort SPT
        }
        finally { Directory.SetCurrentDirectory(previous); }

        var warning = Assert.Single(log.Lines, l => l.Level == "Warning");
        Assert.Contains("unavailable", warning.Text);
        Assert.Contains("link", warning.Text);
        Assert.Empty(Names(Real));
        startup.Dispose();
    }

    public void Dispose()
    {
        string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.Admission.Tests"));
        if (Path.GetDirectoryName(Path.GetFullPath(_home)) != expected
            || File.ReadAllText(Path.Combine(_home, ".owner")) != "synthetic-admission") throw new InvalidOperationException("Unowned fixture.");
        if (Directory.Exists(Alias)) Directory.Delete(Alias); // removes the link only
        if (Directory.Exists(Path.Combine(Real, "user")))
            foreach (var link in Directory.EnumerateDirectories(Path.Combine(Real, "user"), "*", SearchOption.TopDirectoryOnly)
                         .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0).ToArray()) Directory.Delete(link);
        Directory.Delete(_home, true);
    }
}
