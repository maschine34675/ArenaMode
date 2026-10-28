using System.Diagnostics;
using ArenaMode.Server.Rewards;
using ArenaMode.SessionHelper;
using Xunit;

namespace ArenaMode.Tests;

public sealed class RewardHelperProcessIdentityTests
{
    [SkippableFact]
    public void ExactLiveWindowsProcessMatchesButChangedIdentityDoesNot()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows native process identity check.");
        using var current = Process.GetCurrentProcess();
        var started = new DateTimeOffset(current.StartTime.ToUniversalTime());
        var image = Environment.ProcessPath!;
        Assert.True(RewardHelperProcessIdentity.Matches(current.Id, started, image));
        Assert.True(RewardHelperProcessIdentity.Matches(current.Id, started, image.ToUpperInvariant()));
        Assert.False(RewardHelperProcessIdentity.Matches(current.Id, started.AddTicks(1), image));
        Assert.False(RewardHelperProcessIdentity.Matches(current.Id, started, image + ".other"));
        Assert.False(RewardHelperProcessIdentity.Matches(0, started, image));
        Assert.False(RewardHelperProcessIdentity.Matches(int.MaxValue, started, image));
        Assert.False(HelperProcesses.HasExited(current));
        using var opened = HelperProcesses.TryOpen(current.Id);
        Assert.NotNull(opened);
        Assert.Null(HelperProcesses.TryOpen(int.MaxValue));
    }

    [SkippableFact]
    public void ExitedProcessWithStillActiveNumericExitCodeCannotAuthenticate()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows native process identity check.");
        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Arguments = "/c exit 259", UseShellExecute = false, CreateNoWindow = true
        })!;
        var started = new DateTimeOffset(child.StartTime.ToUniversalTime());
        Assert.True(child.WaitForExit(10000));
        Assert.Equal(259, child.ExitCode);
        Assert.False(RewardHelperProcessIdentity.Matches(child.Id, started, child.StartInfo.FileName));
        Assert.True(HelperProcesses.HasExited(child));
        Assert.Null(HelperProcesses.TryOpen(child.Id));
    }
}
