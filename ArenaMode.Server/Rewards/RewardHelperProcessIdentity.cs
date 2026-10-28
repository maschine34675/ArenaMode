using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ArenaMode.Server.Rewards;

// Module enumeration requires access to the other process's memory. Identity
// checks need only limited query rights, including across UAC integrity levels.
internal static class RewardHelperProcessIdentity
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitTimeout = 258;

    internal static bool Matches(int pid, DateTimeOffset startedUtc, string expectedImage)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0) return false;
        try
        {
            // Keep all observations on one process object, so PID reuse cannot
            // combine the old creation time with a replacement's image path.
            using var process = OpenProcess(QueryLimitedInformation | Synchronize, false, pid);
            if (process.IsInvalid || WaitForSingleObject(process, 0) != WaitTimeout
                || !GetProcessTimes(process, out long created, out _, out _, out _)
                || DateTime.FromFileTimeUtc(created) != startedUtc.UtcDateTime) return false;
            var image = new StringBuilder(32768);
            int length = image.Capacity;
            return QueryFullProcessImageName(process, 0, image, ref length)
                && string.Equals(image.ToString(), expectedImage, StringComparison.OrdinalIgnoreCase)
                && WaitForSingleObject(process, 0) == WaitTimeout;
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder image, ref int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}
