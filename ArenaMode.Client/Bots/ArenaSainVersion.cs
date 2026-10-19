using System;
using System.Reflection;
using System.Threading;

namespace ArenaMode.Bots;

/// <summary>
/// Admission for the adapters reviewed against SAIN 4.5.1. Newer builds may
/// attempt their existing contracts; each adapter still validates its own API
/// and, where required, getter/method IL before installing any hooks.
/// </summary>
internal static class ArenaSainVersion
{
    private static readonly Version Minimum = new(4, 5, 1, 0);
    private static int _reportedNewer;

    internal static void Require(Assembly assembly)
    {
        var identity = assembly?.GetName();
        var actual = identity?.Version;
        if (identity?.Name != "SAIN" || actual == null || actual.Build < 0)
            throw new InvalidOperationException("expected a known SAIN assembly version, at least 4.5.1");

        // A missing revision and revision zero describe the same release.
        actual = new Version(actual.Major, actual.Minor, actual.Build, Math.Max(0, actual.Revision));
        if (actual < Minimum)
            throw new InvalidOperationException($"SAIN {actual} is older than the required 4.5.1");

        // Several independent adapters bind at startup and the door guard can
        // rebind after AssemblyLoad. Do not repeat this advisory per adapter,
        // bot, raid or rebind; real contract failures retain their own warnings.
        if (actual > Minimum && Interlocked.Exchange(ref _reportedNewer, 1) == 0)
            Plugin.ModLogger.LogWarning($"Arena SAIN support was tested with 4.5.1; installed {actual} is newer. "
                + "Trying the existing APIs with runtime safety checks.");
    }
}
