using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ArenaMode.SessionHelper;

namespace ArenaMode.Tests;

/// <summary>
/// The process and socket side of the Arena session helper (docs/ARENA-SESSION-20261021.md, section 6)
/// against real stand-in processes: which file a path names (junction and SUBST spellings), who owns a
/// listening port, what <c>stop-server</c> may kill (the image is compared as a file, a server of
/// another installation is left alone) and that <c>leave</c> starts the game even when the arena server
/// cannot be cleaned up. A copy of <c>ping.exe</c> stands in for <c>SPT.Server.exe</c>: it only has to
/// stay alive. <c>HelperRunner.cs</c>, <c>HelperProcesses.cs</c> and <c>HelperLog.cs</c> are linked into
/// this project.
/// </summary>
public sealed class ArenaSessionHelperProcessTests
{
    private const string ProfileId = "6ab7db1164f04b565cf78be7";

    private static void SkipUnlessWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "the helper is a Windows program; these checks use real processes, NTFS junctions and Win32 calls");
    }

    /// <summary>A temporary folder with the processes started in it; everything is cleaned up on dispose.</summary>
    private sealed class Area : IDisposable
    {
        private readonly string _parent;
        private readonly List<Process> _started = new();

        public Area()
        {
            _parent = Path.Combine(Path.GetTempPath(), "arena-session-process-tests");
            Base = Path.Combine(_parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Base);
        }

        public string Base { get; }

        public string At(params string[] parts) => Path.Combine(new[] { Base }.Concat(parts).ToArray());

        /// <summary>An SPT runtime folder whose SPT.Server.exe is a copy of ping.exe (alive for two minutes).</summary>
        public string Runtime(params string[] parts)
        {
            string runtime = At(parts);
            Directory.CreateDirectory(runtime);
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), Path.Combine(runtime, HelperLayout.ServerExeName));
            return runtime;
        }

        public Process StartServer(string runtime) => StartExe(Path.Combine(runtime, HelperLayout.ServerExeName));

        public Process StartExe(string exe)
        {
            var info = new ProcessStartInfo(exe, "-n 120 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process process = Process.Start(info)!;
            _started.Add(process);
            return process;
        }

        /// <summary>
        /// The arena server's own copy of the exe, as the helper's build makes it: a copy of ping.exe as
        /// <c>SPT.Server.exe</c> in the tree of the session root <paramref name="root"/>. Returns its path.
        /// </summary>
        public string TreeServerExe(string root)
        {
            string tree = HelperServerTree.GetTreePath(root);
            Directory.CreateDirectory(tree);
            string exe = Path.Combine(tree, HelperLayout.ServerExeName);
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);
            return exe;
        }

        /// <summary>
        /// The session root with the marker and the pid file of a server the helper "started". The marker is
        /// the one of an older helper (no <c>serverExe</c>: its server ran the installation's own exe) unless
        /// <paramref name="serverExe"/> names the tree copy that this helper starts the server from.
        /// </summary>
        public string Record(Process server, string sourceRuntime, string name = "session", string? serverExe = null)
        {
            string root = At(name);
            Directory.CreateDirectory(HelperServerTree.GetTreePath(root));
            File.WriteAllText(HelperServerTree.GetMarkerPath(root),
                HelperMarker.Create(DateTime.UtcNow, ProfileId, 6970, sourceRuntime, "hash", "test", serverExe!).ToJson());
            File.WriteAllText(HelperServerTree.GetPidFilePath(root), server.Id.ToString());
            return root;
        }

        public HelperRunner Runner(string root)
        {
            Directory.CreateDirectory(root);
            return new HelperRunner(root, new HelperLog(Path.Combine(root, "helper.log")), "test")
            {
                GameSettleMilliseconds = 10,
                GameStartCheckMilliseconds = 100,
                ServerSettleMilliseconds = 10,
            };
        }

        /// <summary>A junction <paramref name="link"/> to <paramref name="target"/>, made from a folder of its own.</summary>
        public void Junction(string link, string target)
        {
            string holder = Path.GetDirectoryName(link)!;
            Directory.CreateDirectory(holder);
            new HelperSandbox(holder, _ => { }).CreateJunction(link, target);
        }

        public void Dispose()
        {
            foreach (Process process in _started)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception)
                {
                    // Already gone.
                }

                process.Dispose();
            }

            try
            {
                new HelperSandbox(_parent, _ => { }).DeleteTree(Base);
            }
            catch (Exception)
            {
                // A leftover temp folder is not a test failure.
            }
        }
    }

    private static bool Exited(Process process) => process.WaitForExit(5000);

    // ------------------------------------------------------------------ which file a path names

    [SkippableFact]
    public void ASpellingThroughAJunctionIsTheSameFileAsTheRealPath()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string real = area.Runtime("real", "SPT_Runtime");
        area.Junction(area.At("holder", "jx"), area.At("real"));

        string viaJunction = area.At("holder", "jx", "SPT_Runtime", HelperLayout.ServerExeName);
        string direct = Path.Combine(real, HelperLayout.ServerExeName);

        Assert.True(HelperProcesses.SameFileSystemObject(viaJunction, direct));
        Assert.True(HelperProcesses.SameFileSystemObject(area.At("holder", "jx", "SPT_Runtime"), real));
        Assert.True(HelperProcesses.SameFileSystemObject(direct.ToUpperInvariant(), direct));
    }

    [SkippableFact]
    public void DifferentOrMissingFilesAreNotTheSameObject()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string a = area.Runtime("a", "SPT_Runtime");
        string b = area.Runtime("b", "SPT_Runtime");

        Assert.False(HelperProcesses.SameFileSystemObject(Path.Combine(a, HelperLayout.ServerExeName), Path.Combine(b, HelperLayout.ServerExeName)));
        Assert.False(HelperProcesses.SameFileSystemObject(a, b));
        Assert.False(HelperProcesses.SameFileSystemObject(Path.Combine(a, "missing.exe"), Path.Combine(b, "missing2.exe")));
        Assert.False(HelperProcesses.SameFileSystemObject(null!, a));
        Assert.False(HelperProcesses.SameFileSystemObject(@"\\server\share\x.exe", a));
    }

    [SkippableFact]
    public void TheFinalPathHasNoExtendedPrefixAndFollowsTheJunction()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string real = area.Runtime("real", "SPT_Runtime");
        area.Junction(area.At("holder", "jx"), area.At("real"));

        Assert.True(HelperProcesses.TryResolveFinalPath(area.At("holder", "jx", "SPT_Runtime", HelperLayout.ServerExeName), out string? throughJunction));
        Assert.True(HelperProcesses.TryResolveFinalPath(Path.Combine(real, HelperLayout.ServerExeName), out string? direct));
        Assert.False(throughJunction!.StartsWith(@"\\?\", StringComparison.Ordinal));
        Assert.False(throughJunction.Contains("holder", StringComparison.OrdinalIgnoreCase));
        Assert.True(HelperPathSafety.PathsEqual(throughJunction, direct!));
        Assert.False(HelperProcesses.TryResolveFinalPath(area.At("nothing", "here.exe"), out _));
        Assert.False(HelperProcesses.TryResolveFinalPath("relative\\path.exe", out _));
    }

    // ------------------------------------------------------------------ who listens on a port

    [SkippableFact]
    public void TheOwnerOfAListeningPortIsFoundByProcessId()
    {
        SkipUnlessWindows();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            List<int>? owners = HelperProcesses.GetListenerProcessIds(port);
            Assert.NotNull(owners);
            Assert.Contains(Environment.ProcessId, owners!);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void ANonListeningPortHasNoOwner()
    {
        SkipUnlessWindows();
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        List<int>? owners = HelperProcesses.GetListenerProcessIds(port);
        Assert.NotNull(owners);
        Assert.DoesNotContain(Environment.ProcessId, owners!);
    }

    [SkippableFact]
    public void AnIpv6ListenerIsFoundToo()
    {
        SkipUnlessWindows();
        Skip.IfNot(Socket.OSSupportsIPv6, "no IPv6 here");
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port)!);
        }
        finally
        {
            listener.Stop();
        }
    }

    // ------------------------------------------------------------------ stop-server

    [SkippableFact]
    public void AServerStartedThroughAJunctionIsStoppedAsTheRecordedServer()
    {
        SkipUnlessWindows();
        using var area = new Area();
        area.Runtime("real", "SPT_Runtime");
        area.Junction(area.At("holder", "jx"), area.At("real"));

        // The game was started as <junction>\...\EscapeFromTarkov.exe, so the runtime in the marker and the
        // path the server is started from use the junction. A process reports its resolved image path.
        string linked = area.At("holder", "jx", "SPT_Runtime");
        Process server = area.StartServer(linked);
        string root = area.Record(server, linked);

        StopResult result = area.Runner(root).StopArenaServer(area.At("real", "SPT_Runtime"));

        Assert.Equal(StopResult.Stopped, result);
        Assert.True(Exited(server));
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)));
    }

    [SkippableFact]
    public void AServerRecordedWithTheRealPathIsStoppedFromTheJunctionSpellingToo()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string real = area.Runtime("real", "SPT_Runtime");
        area.Junction(area.At("holder", "jx"), area.At("real"));
        Process server = area.StartServer(real);
        string root = area.Record(server, real);

        Assert.Equal(StopResult.Stopped, area.Runner(root).StopArenaServer(area.At("holder", "jx", "SPT_Runtime")));
        Assert.True(Exited(server));
    }

    [SkippableFact]
    public void TheArenaServerOfAnotherInstallationIsLeftAlone()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string first = area.Runtime("one", "SPT_Runtime");
        string second = area.Runtime("two", "SPT_Runtime");
        Process server = area.StartServer(first);
        string root = area.Record(server, first);
        HelperRunner runner = area.Runner(root);

        Assert.Equal(StopResult.OtherInstallation, runner.StopArenaServer(second));

        Assert.False(server.HasExited);
        Assert.True(File.Exists(HelperServerTree.GetPidFilePath(root)));
        Assert.True(File.Exists(HelperServerTree.GetMarkerPath(root)));

        // The stop-server command has no installation of its own and stops whichever server was recorded.
        Assert.Equal(StopResult.Stopped, runner.StopArenaServer(null!));
        Assert.True(Exited(server));
    }

    [SkippableFact]
    public void TheArenaServerOfTheCallersOwnInstallationIsStopped()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("one", "SPT_Runtime");
        Process server = area.StartServer(runtime);
        string root = area.Record(server, runtime);

        Assert.Equal(StopResult.Stopped, area.Runner(root).StopArenaServer(runtime));
        Assert.True(Exited(server));
    }

    [SkippableFact]
    public void ARecordedPidOfAnotherProgramIsNotStoppedAndLosesItsPidFile()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("one", "SPT_Runtime");
        Process impostor = area.StartServer(area.Runtime("elsewhere", "SPT_Runtime"));
        string root = area.Record(impostor, runtime);

        Assert.Equal(StopResult.NotOurs, area.Runner(root).StopArenaServer(runtime));

        Assert.False(impostor.HasExited);
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)));
    }

    // ------------------------------------------------------------------ stop-server: the server runs from the tree

    [SkippableFact]
    public void AServerRunningFromTheTreeCopyIsStoppedAsTheRecordedServer()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("real", "SPT_Runtime");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, runtime, serverExe: treeExe);

        StopResult result = area.Runner(root).StopArenaServer(runtime);

        Assert.Equal(StopResult.Stopped, result);
        Assert.True(Exited(server));
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)));
        Assert.Contains("stopping the arena server", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    [SkippableFact]
    public void AMarkerOfThisHelperDoesNotAcceptTheInstallationsOwnExe()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("real", "SPT_Runtime");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);

        // The recorded pid now belongs to the installation's own server (the normal one, say): same exe as the
        // image an older helper would have started, but not the tree copy this marker names.
        Process normalServer = area.StartServer(runtime);
        area.Record(normalServer, runtime, serverExe: treeExe);

        Assert.Equal(StopResult.NotOurs, area.Runner(root).StopArenaServer(runtime));

        Assert.False(normalServer.HasExited);
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)));
    }

    [SkippableFact]
    public void AServerStartedByAnOlderHelperFromTheInstallationIsStillStoppedOnce()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("real", "SPT_Runtime");
        string root = area.At("session");

        // An existing session: the marker has no serverExe, the server ran <runtime>\SPT.Server.exe.
        Process server = area.StartServer(runtime);
        area.Record(server, runtime);

        Assert.Equal(StopResult.Stopped, area.Runner(root).StopArenaServer(runtime));

        Assert.True(Exited(server));
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)));
        Assert.Contains("older helper", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    [SkippableFact]
    public void AMarkerOfAnOlderHelperDoesNotAcceptACopyInTheTree()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("real", "SPT_Runtime");
        string root = area.At("session");
        Process server = area.StartExe(area.TreeServerExe(root));
        area.Record(server, runtime);

        Assert.Equal(StopResult.NotOurs, area.Runner(root).StopArenaServer(runtime));

        Assert.False(server.HasExited);
    }

    [SkippableFact]
    public void ATreeServerIsRecognisedWhateverSpellingTheSessionRootHas()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("real", "SPT_Runtime");
        string realRoot = area.At("real", "session");
        string treeExe = area.TreeServerExe(realRoot);
        area.Junction(area.At("holder", "jx"), area.At("real"));

        // The process reports its resolved image path; the helper's session root is spelled through a junction.
        Process server = area.StartExe(treeExe);
        area.Record(server, runtime, "real\\session", serverExe: treeExe);
        string linkedRoot = area.At("holder", "jx", "session");

        Assert.Equal(StopResult.Stopped, area.Runner(linkedRoot).StopArenaServer(runtime));
        Assert.True(Exited(server));
    }

    [SkippableFact]
    public void TheTreeServerOfAnotherInstallationIsLeftAlone()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string first = area.Runtime("one", "SPT_Runtime");
        string second = area.Runtime("two", "SPT_Runtime");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, first, serverExe: treeExe);
        HelperRunner runner = area.Runner(root);

        Assert.Equal(StopResult.OtherInstallation, runner.StopArenaServer(second));

        Assert.False(server.HasExited);
        Assert.True(File.Exists(HelperServerTree.GetPidFilePath(root)));

        Assert.Equal(StopResult.Stopped, runner.StopArenaServer(null!));
        Assert.True(Exited(server));
    }

    [SkippableFact]
    public void StatusNamesTheExeTheServerWasStartedFrom()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("real", "SPT_Runtime");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, runtime, serverExe: treeExe);

        Assert.True(ArenaMode.Shared.Session.ArenaMiniJson.TryParse(area.Runner(root).Status(), out object? status, out _));

        var marker = ArenaMode.Shared.Session.ArenaMiniJson.AsObject(ArenaMode.Shared.Session.ArenaMiniJson.AsObject(status)!["marker"])!;
        Assert.Equal(treeExe, ArenaMode.Shared.Session.ArenaMiniJson.GetString(marker, "serverExe"));
        var pid = ArenaMode.Shared.Session.ArenaMiniJson.AsObject(ArenaMode.Shared.Session.ArenaMiniJson.AsObject(status)!["serverPid"])!;
        Assert.True(HelperProcesses.SameFileSystemObject(treeExe, (string)pid["image"]!));
    }

    // ------------------------------------------------------------------ leave

    [SkippableFact]
    public void LeaveStartsTheGameEvenWhenTheArenaServerCannotBeCleanedUp()
    {
        SkipUnlessWindows();
        using var area = new Area();

        // A normal server that "already listens", and a game executable that is a harmless stand-in.
        var normal = new TcpListener(IPAddress.Loopback, 0);
        normal.Start();
        try
        {
            int port = ((IPEndPoint)normal.LocalEndpoint).Port;
            string gameDir = area.At("game");
            string runtime = Path.Combine(gameDir, "SPT_Runtime");
            Directory.CreateDirectory(Path.Combine(runtime, "SPT_Data", "configs"));
            File.WriteAllText(Path.Combine(runtime, "SPT_Data", "configs", "http.json"),
                "{ \"ip\": \"127.0.0.1\", \"port\": " + port + ", \"backendIp\": \"127.0.0.1\", \"backendPort\": " + port + " }");
            string gameExe = Path.Combine(gameDir, "EscapeFromTarkov.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), gameExe);

            // The arena session's game has already exited: a pid that no longer runs.
            using Process finished = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { UseShellExecute = false, CreateNoWindow = true })!;
            Assert.True(finished.WaitForExit(10000));

            string json = ArenaMode.Shared.Session.ArenaMiniJson.Write(new Dictionary<string, object?>
            {
                ["schema"] = 1,
                ["direction"] = "leave",
                ["gamePid"] = finished.Id,
                ["gameExe"] = gameExe,
                ["gameDir"] = gameDir,
                ["gameArgs"] = new List<string>
                {
                    "-token=" + ProfileId,
                    "-config={'BackendUrl':'https://127.0.0.1:6970','Version':'live','MatchingVersion':'live'}",
                    "-arena-session",
                },
                ["profileId"] = ProfileId,
                ["sptRuntimeDir"] = runtime,
                ["serverMods"] = new List<object?>(),
                ["policyHash"] = "hash",
            });
            Assert.True(HelperRequest.TryParse(json, "leave", out HelperRequest? request, out string? error), error);

            // server.pid is open without sharing: a scanner or an editor holds it at the wrong moment.
            string root = area.At("session");
            Directory.CreateDirectory(root);
            string pidFile = HelperServerTree.GetPidFilePath(root);
            File.WriteAllText(pidFile, "12345");
            bool left;
            using (new FileStream(pidFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                left = area.Runner(root).Leave(request!);
            }

            Assert.True(left);
            string log = File.ReadAllText(Path.Combine(root, "helper.log"));
            Assert.Contains("the arena server could not be stopped", log);
            Assert.Contains("starting the game", log);
            Assert.Contains("leave finished", log);
        }
        finally
        {
            normal.Stop();
        }
    }
}
