using System.Diagnostics;
using System.Text.Json;

namespace ArenaMode.Server.Rewards;

/// <summary>
/// A diagnostic note next to the reward records: "this server paused rewards at its start". Only the session helper reads it,
/// to tell a paused normal server from one that settled the last Arena session (it matches the process id and start time of
/// the server it started). No server code, recovery, payment or lease ever reads it, so it can never authorize anything.
/// Best effort: a failure to write or remove it is ignored. Removed by the next start that finishes recovery.
/// </summary>
internal static class RewardPausedNote
{
    internal const string FileName = "rewards-paused.json";
    internal static string PathFor(string root) => Path.Combine(root, RewardStartupFiles.RelativeDirectory, FileName);

    internal static void Write(string root, string reason, bool recoveryPending, int? pid = null, DateTimeOffset? startedUtc = null)
    {
        try
        {
            if (pid is null || startedUtc is null)
            {
                using var current = Process.GetCurrentProcess();
                pid ??= current.Id;
                startedUtc ??= current.StartTime.ToUniversalTime();
            }
            string json = JsonSerializer.Serialize(new
            {
                schema = 1,
                reason = RewardUnavailableText.LeaseCode(reason),
                recoveryPending,
                serverPid = pid,
                serverStartedUtc = startedUtc.Value.UtcDateTime.ToString("O"),
                writtenUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O")
            });
            RewardStartupFiles.WriteAtomic(root, PathFor(root), json);
        }
        catch (Exception e) when (e is not OperationCanceledException) { }
    }

    internal static void Remove(string root)
    {
        try
        {
            string path = PathFor(root);
            if (!File.Exists(path)) return;
            RewardStartupFiles.RequireLocalPath(root, path);
            File.Delete(path);
        }
        catch (Exception e) when (e is not OperationCanceledException) { }
    }
}
