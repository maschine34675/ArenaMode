using System.Diagnostics;
using ArenaStart;

internal static class ProcessStateChecks
{
    internal static int Run(string scratch)
    {
        RuntimeHost.RequireIdle();
        var folder = Path.Combine(scratch, "native-process-lifetime");
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "SPT.Server.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);
        using var process = Process.Start(new ProcessStartInfo(executable, "/d /q")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = folder
        }) ?? throw new IOException("Process lifetime fixture failed to start.");
        var checks = 0;
        void Check(bool result, string reason)
        {
            checks++;
            if (!result) throw new InvalidOperationException(reason);
        }
        try
        {
            // Keep both a native handle and the real enumeration snapshot across
            // exit: the process can disappear after enumeration but before use.
            _ = process.Handle;
            Check(RuntimeHost.HasRunningProcess("SPT.Server"), "live server fixture was ignored");
            var rejected = false;
            try { RuntimeHost.RequireIdle(); }
            catch (InvalidOperationException error) when (error.Message.Contains("Close SPT.Server")) { rejected = true; }
            Check(rejected, "live server did not block environment changes");
            var snapshot = Process.GetProcessesByName("SPT.Server");
            try
            {
                process.StandardInput.WriteLine("exit 0");
                process.StandardInput.Flush();
                Check(process.WaitForExit(10000) && process.ExitCode == 0, "fixture did not exit normally");
                Check(snapshot.Any(p => p.Id == process.Id && p.HasExited), "stale enumeration negative control missing");
                Check(!RuntimeHost.AnyRunning(snapshot), "stale enumeration was treated as running");
            }
            finally { foreach (var item in snapshot) item.Dispose(); }
            Check(!RuntimeHost.HasRunningProcess("SPT.Server"), "terminated process was treated as running");
            RuntimeHost.RequireIdle();
            checks++;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
        }
        Console.WriteLine($"PROCESS_STATE_CHECKS_PASS ({checks} assertions; live + stale Windows process snapshot)");
        return checks;
    }
}
