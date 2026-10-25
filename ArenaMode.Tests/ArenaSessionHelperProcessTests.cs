using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ArenaMode.SessionHelper;
using ArenaMode.Shared.Session;

namespace ArenaMode.Tests;

/// <summary>
/// The process and socket side of the Arena session helper (docs/ARENA-SESSION-20261021.md, section 6)
/// against real stand-in processes: which file a path names (junction and SUBST spellings), who owns a
/// listening port, what <c>stop-server</c> may kill (the image is compared as a file, a server of
/// another installation is left alone) and that <c>leave</c> starts the game even when the arena server
/// cannot be cleaned up. It also covers the order of one helper run (<see cref="HelperHost"/>: stale
/// acknowledgement, mutex, request, acknowledgement, command) and the registration of the arena server
/// (<c>server.pid</c> right after the start; a failed write ends the server). A copy of <c>ping.exe</c> stands
/// in for <c>SPT.Server.exe</c> and the game: it only has to stay alive. <c>HelperRunner.cs</c>,
/// <c>HelperHost.cs</c>, <c>HelperProcesses.cs</c> and <c>HelperLog.cs</c> are linked into this project.
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

        public Process StartExe(string exe, string arguments = "-n 120 127.0.0.1")
        {
            var info = new ProcessStartInfo(exe, arguments)
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

        /// <summary>
        /// The marker of a session whose server was started from the tree copy, and nothing else: no pid file, as when
        /// the helper that started the server was killed before it wrote one.
        /// </summary>
        public void RecordMarker(string root, string sourceRuntime, string serverExe)
        {
            Directory.CreateDirectory(HelperServerTree.GetTreePath(root));
            File.WriteAllText(HelperServerTree.GetMarkerPath(root),
                HelperMarker.Create(DateTime.UtcNow, ProfileId, 6970, sourceRuntime, "hash", "test", serverExe).ToJson());
        }

        public HelperRunner Runner(string root)
        {
            Directory.CreateDirectory(root);
            return new HelperRunner(root, new HelperLog(Path.Combine(root, "helper.log")), "test")
            {
                GameSettleMilliseconds = 10,
                GameStartCheckMilliseconds = 100,
                ServerSettleMilliseconds = 10,
                StartWatchdogAfterEnter = false, // the watchdog is the helper's own exe, which this assembly is not
            };
        }

        /// <summary>The helper's host (stale acknowledgement, mutex, request, acknowledgement, command) around a runner of this area.</summary>
        public HelperHost Host(HelperRunner runner, string root, string mutexName, List<string> output)
        {
            return new HelperHost(runner, new HelperLog(Path.Combine(root, "helper.log")), mutexName, output.Add);
        }

        /// <summary>A game folder with a <c>SPT_Runtime</c> inside and a harmless <c>EscapeFromTarkov.exe</c> (a copy of ping.exe), not started.</summary>
        public GameFolder Game()
        {
            string dir = At("game");
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "EscapeFromTarkov.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);
            string runtime = Path.Combine(dir, "SPT_Runtime");
            Directory.CreateDirectory(runtime);
            return new GameFolder(dir, exe, runtime);
        }

        /// <summary>The same, with the game started: it stays alive for two minutes, so a helper waiting for it never sees it exit.</summary>
        public (GameFolder Folder, Process Process) RunningGame()
        {
            GameFolder folder = Game();
            return (folder, StartExe(folder.Exe));
        }

        /// <summary>A process id that does not run any more (the old game of a request whose game has quit).</summary>
        public static int FinishedProcessId()
        {
            using Process finished = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { UseShellExecute = false, CreateNoWindow = true })!;
            Assert.True(finished.WaitForExit(10000));
            return finished.Id;
        }

        /// <summary>Writes a request file the way the client does, for a game folder, and returns its path.</summary>
        public string WriteRequest(string path, string direction, GameFolder game, int gamePid)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, RequestJson(direction, game, gamePid));
            return path;
        }

        public static string RequestJson(string direction, GameFolder game, int gamePid)
        {
            bool enter = direction == "enter";
            return ArenaMiniJson.Write(new Dictionary<string, object?>
            {
                ["schema"] = 1,
                ["direction"] = direction,
                ["gamePid"] = gamePid,
                ["gameExe"] = game.Exe,
                ["gameDir"] = game.Dir,
                ["gameArgs"] = new List<string>
                {
                    "-token=" + ProfileId,
                    "-config={'BackendUrl':'https://127.0.0.1:6969','Version':'live','MatchingVersion':'live'}",
                    enter ? "-force-gfx-jobs" : "-arena-session",
                },
                ["profileId"] = ProfileId,
                ["sptRuntimeDir"] = game.Runtime,
                ["serverMods"] = enter
                    ? new List<object?> { new Dictionary<string, object?> { ["guid"] = "com.maschine.ArenaMode", ["folder"] = Path.Combine(game.Runtime, "user", "mods", "ArenaMode") } }
                    : new List<object?>(),
                ["policyHash"] = "hash",
            });
        }

        /// <summary>
        /// What an enter reads from the runtime of a game folder, with ping.exe as <c>SPT.Server.exe</c>: the logger
        /// configuration, an <c>http.json</c> naming <paramref name="normalPort"/>, the one server mod and the profile.
        /// </summary>
        public void FillRuntime(GameFolder game, int normalPort)
        {
            string runtime = game.Runtime;
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), Path.Combine(runtime, HelperLayout.ServerExeName));
            File.WriteAllText(Path.Combine(runtime, HelperLayout.LoggerFileName), "{ \"loggers\": [ { \"type\": \"File\", \"filePattern\": \"spt-%DATE%.log\" } ] }");
            WriteHttpJson(Path.Combine(runtime, "SPT_Data", "configs"), normalPort);
            Directory.CreateDirectory(Path.Combine(runtime, "user", "mods", "ArenaMode"));
            File.WriteAllText(Path.Combine(runtime, "user", "mods", "ArenaMode", "ArenaMode.dll"), "mod");
            Directory.CreateDirectory(Path.Combine(runtime, "user", "profiles"));
            File.WriteAllText(Path.Combine(runtime, "user", "profiles", ProfileId + ".json"), "{}");
        }

        public static void WriteHttpJson(string configsFolder, int port)
        {
            Directory.CreateDirectory(configsFolder);
            File.WriteAllText(Path.Combine(configsFolder, HelperLayout.HttpJsonFileName),
                "{ \"ip\": \"127.0.0.1\", \"port\": " + port + ", \"backendIp\": \"127.0.0.1\", \"backendPort\": " + port + " }");
        }

        /// <summary>
        /// The arena tree as a finished build leaves it for the start of the server: the tree's own server exe (a copy of
        /// ping.exe) and, if <paramref name="port"/> is given, an <c>http.json</c> naming it.
        /// </summary>
        public HelperTreeResult TreeForStart(string root, int? port)
        {
            string exe = TreeServerExe(root);
            string tree = HelperServerTree.GetTreePath(root);
            if (port.HasValue) WriteHttpJson(Path.Combine(tree, "SPT_Data", "configs"), port.Value);
            return new HelperTreeResult { TreePath = tree, Port = port ?? 6970, ServerExe = exe };
        }

        public sealed record GameFolder(string Dir, string Exe, string Runtime);

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

    // ------------------------------------------------------------------ who listens for which address

    [SkippableFact]
    public void TheOwnerLookupIsBoundToTheAddressTheConnectionReaches()
    {
        SkipUnlessWindows();
        IPAddress second = IPAddress.Parse("127.0.0.2"); // every 127.x.x.x address is a loopback address on Windows
        var listener = new TcpListener(second, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // A listener on 127.0.0.2 is not what a connection to 127.0.0.1 reaches ...
            List<int>? atFirst = HelperProcesses.GetListenerProcessIds(port, IPAddress.Loopback);
            Assert.NotNull(atFirst);
            Assert.DoesNotContain(Environment.ProcessId, atFirst!);
            Assert.DoesNotContain(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.Loopback.MapToIPv6())!);

            // ... but it is what a connection to 127.0.0.2 reaches, spelled either way (a dual-stack client socket
            // reports an IPv4 server as ::ffff:a.b.c.d)
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, second)!);
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, second.MapToIPv6())!);

            // Without an address every address counts.
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port)!);
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, null!)!);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void AnIpv6LoopbackListenerIsNotWhatAnIpv4ConnectionReaches()
    {
        SkipUnlessWindows();
        Skip.IfNot(Socket.OSSupportsIPv6, "no IPv6 here");
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.IPv6Loopback)!);
            Assert.DoesNotContain(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.Loopback)!);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void AnIpv4WildcardListenerIsWhatAnIpv4ConnectionReachesAndAnIpv6OneIsNot()
    {
        SkipUnlessWindows();
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // The probe the wait makes, then the lookup for the address it reached: the same two calls as in HelperRunner.
            Assert.True(HelperProcesses.CanConnect("127.0.0.1", port, 2000, out IPAddress? reached));
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, reached!)!);
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.Loopback)!);
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.Loopback.MapToIPv6())!);

            // 0.0.0.0 takes no connection to an IPv6 address.
            if (Socket.OSSupportsIPv6) Assert.DoesNotContain(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.IPv6Loopback)!);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void ADualStackWildcardListenerServesAnIpv4Connection()
    {
        SkipUnlessWindows();
        Skip.IfNot(Socket.OSSupportsIPv6, "no IPv6 here");
        var listener = new TcpListener(IPAddress.IPv6Any, 0);
        listener.Server.DualMode = true; // what a Kestrel server on [::] is: one socket on the IPv6 wildcard that takes IPv4 connections too
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // An IPv4 connection to it, and an IPv6 one: the owner is found for both. (Windows Server 2025 lists such a socket in the
            // IPv4 table as 0.0.0.0 as well as in the IPv6 table as [::], so this does not depend on the IPv6 read for an IPv4 address.)
            Assert.True(HelperProcesses.CanConnect("127.0.0.1", port, 2000, out IPAddress? reached));
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, reached!)!);
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.Loopback)!);
            Assert.Contains(Environment.ProcessId, HelperProcesses.GetListenerProcessIds(port, IPAddress.IPv6Loopback)!);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    [InlineData("127.0.0.2", "127.0.0.1", false)]
    [InlineData("10.0.0.2", "127.0.0.1", false)]
    [InlineData("0.0.0.0", "127.0.0.1", true)]
    [InlineData("0.0.0.0", "::1", false)]
    [InlineData("::1", "::1", true)]
    [InlineData("::1", "127.0.0.1", false)]
    [InlineData("::", "::1", true)]
    [InlineData("::", "127.0.0.1", true)] // an IPv6 wildcard listener can be dual-stack
    public void AListenerServesItsOwnAddressAndTheWildcardOfItsFamily(string listening, string connected, bool expected)
    {
        Assert.Equal(expected, HelperProcesses.ListenerServes(IPAddress.Parse(listening), IPAddress.Parse(connected)));
        Assert.False(HelperProcesses.ListenerServes(null!, IPAddress.Parse(connected)));
        Assert.False(HelperProcesses.ListenerServes(IPAddress.Parse(listening), null!));
    }

    [Theory]
    [InlineData("::ffff:127.0.0.1", "127.0.0.1")]
    [InlineData("::ffff:10.1.2.3", "10.1.2.3")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("::1", "::1")]
    [InlineData("::", "::")]
    [InlineData("2001:db8::ffff:102:304", "2001:db8::ffff:102:304")]
    public void AnIpv4MappedAddressIsTheIpv4AddressAndNothingElseIsChanged(string address, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), HelperProcesses.UnmapIPv4(IPAddress.Parse(address)));
        Assert.Null(HelperProcesses.UnmapIPv4(null!));
    }

    [SkippableFact]
    public void AConnectionReportsTheAddressItReached()
    {
        SkipUnlessWindows();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(HelperProcesses.CanConnect("127.0.0.1", port, 2000, out IPAddress? reached));
            Assert.Equal(IPAddress.Loopback, reached); // not ::ffff:127.0.0.1
        }
        finally
        {
            listener.Stop();
        }

        Assert.False(HelperProcesses.CanConnect("127.0.0.1", port, 500, out IPAddress? none));
        Assert.Null(none);
    }

    [SkippableFact]
    public void APortIsFreeOnAnAddressOnlyWhenNothingAnswersThereAndItCanBeBoundThere()
    {
        SkipUnlessWindows();
        int port = FreePort();

        Assert.True(HelperProcesses.IsPortFree(port));
        Assert.True(HelperProcesses.IsPortFree("127.0.0.1", port));
        Assert.True(HelperProcesses.IsPortFree("localhost", port)); // not an address: probed through the IPv4 loopback address
        Assert.False(HelperProcesses.IsPortFree("203.0.113.5", port)); // an address this machine does not have cannot be bound

        var listener = new TcpListener(IPAddress.Parse("127.0.0.2"), port);
        listener.Start();
        try
        {
            Assert.False(HelperProcesses.IsPortFree("127.0.0.2", port)); // answers there
            Assert.True(HelperProcesses.IsPortFree("127.0.0.1", port)); // the same port on another address is free
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

    // ------------------------------------------------------------------ one run of the helper: the order of its steps

    private static string NewMutexName() => "Local\\maschine-ArenaMode.Session.Tests-" + Guid.NewGuid().ToString("N");

    private static HelperCommand Command(params string[] args)
    {
        Assert.True(HelperCommand.TryParse(args, out HelperCommand? command, out string? error), error);
        return command!;
    }

    /// <summary>Owns a named mutex on a thread of its own until disposed: another helper instance that is running.</summary>
    private sealed class MutexOwner : IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;

        public MutexOwner(string name)
        {
            using var acquired = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                using var mutex = new Mutex(false, name);
                mutex.WaitOne();
                acquired.Set();
                _release.Wait();
                mutex.ReleaseMutex();
            });
            _thread.Start();
            Assert.True(acquired.Wait(10000), "the mutex owner did not start");
        }

        public void Dispose()
        {
            _release.Set();
            _thread.Join(10000);
            _release.Dispose();
        }
    }

    [SkippableTheory]
    [InlineData("enter")]
    [InlineData("leave")]
    public void TheRequestIsAcknowledgedBeforeTheHelperWaitsForTheGame(string direction)
    {
        SkipUnlessWindows();
        using var area = new Area();
        var (game, process) = area.RunningGame(); // alive for the whole test: the helper cannot get past its wait for the game
        string root = area.At("session");
        string requestPath = area.WriteRequest(Path.Combine(root, "request.json"), direction, game, process.Id);
        HelperRunner runner = area.Runner(root);
        runner.GameExitTimeoutSeconds = 1;
        DateTime before = DateTime.UtcNow;

        int exit = area.Host(runner, root, NewMutexName(), new List<string>()).Execute(Command(direction, "--request", requestPath));

        // The game never exited, so the run failed after its wait ...
        Assert.Equal(1, exit);
        Assert.False(process.HasExited);
        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("did not exit within", log);

        // ... and the acknowledgement had already been written, by this process, before the wait began.
        string acknowledgement = requestPath + ".accepted";
        Assert.True(File.Exists(acknowledgement), log);
        Assert.True(HelperAcknowledgement.TryParse(File.ReadAllText(acknowledgement), out int pid, out DateTime acceptedUtc, out string? error), error);
        Assert.Equal(Environment.ProcessId, pid);
        Assert.InRange(acceptedUtc, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        int accepted = log.IndexOf("request accepted", StringComparison.Ordinal);
        int waiting = log.IndexOf("waiting for the game", StringComparison.Ordinal);
        Assert.True(accepted >= 0 && waiting > accepted, log);
        Assert.False(Directory.Exists(HelperServerTree.GetTreePath(root)), "nothing was changed");
    }

    [SkippableTheory]
    [InlineData("{}", "is not usable")]
    [InlineData("not json", "is not usable")]
    public void ARequestTheHelperCannotUseGetsNoAcknowledgementAndLosesAStaleOne(string content, string fragment)
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        string requestPath = Path.Combine(root, "request.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(requestPath, content);
        File.WriteAllText(requestPath + ".accepted", "stale from an earlier request");
        File.WriteAllText(requestPath + ".accepted.111.tmp", "left by a helper that died");

        int exit = area.Host(area.Runner(root), root, NewMutexName(), new List<string>()).Execute(Command("enter", "--request", requestPath));

        Assert.Equal(1, exit);
        Assert.Contains(fragment, File.ReadAllText(Path.Combine(root, "helper.log")));
        Assert.Equal(new[] { "helper.log", "request.json" }, Directory.GetFiles(root).Select(path => Path.GetFileName(path)).OrderBy(n => n, StringComparer.Ordinal));
    }

    [SkippableFact]
    public void ARequestOfTheOtherDirectionOrNoRequestFileGetsNoAcknowledgementEither()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string leaveRequest = area.WriteRequest(Path.Combine(root, "leave.json"), "leave", game, 1234);
        string missing = Path.Combine(root, "missing.json");
        File.WriteAllText(missing + ".accepted", "stale");
        HelperHost host = area.Host(area.Runner(root), root, NewMutexName(), new List<string>());

        Assert.Equal(1, host.Execute(Command("enter", "--request", leaveRequest)));
        Assert.Equal(1, host.Execute(Command("enter", "--request", missing)));

        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("the request direction is 'leave'", log);
        Assert.Contains("cannot read the request file", log);
        Assert.False(File.Exists(leaveRequest + ".accepted"));
        Assert.False(File.Exists(missing + ".accepted"), "the stale acknowledgement of that name is gone");
    }

    [SkippableFact]
    public void ABusyMutexGetsNoAcknowledgementButTheStaleOneStillGoes()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var (game, process) = area.RunningGame();
        string root = area.At("session");
        string requestPath = area.WriteRequest(Path.Combine(root, "request.json"), "enter", game, process.Id);
        File.WriteAllText(requestPath + ".accepted", "stale from an earlier request");
        string mutexName = NewMutexName();
        HelperRunner runner = area.Runner(root);
        HelperHost host = area.Host(runner, root, mutexName, new List<string>());
        host.MutexWaitSeconds = 0;

        int exit;
        using (new MutexOwner(mutexName))
        {
            exit = host.Execute(Command("enter", "--request", requestPath));
        }

        Assert.Equal(1, exit);
        Assert.Contains("another helper instance is running", File.ReadAllText(Path.Combine(root, "helper.log")));
        Assert.False(File.Exists(requestPath + ".accepted"));
        Assert.False(process.HasExited);
    }

    [SkippableFact]
    public void AnAcknowledgementThatCannotBeWrittenEndsTheRunBeforeTheGameIsAwaited()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var (game, process) = area.RunningGame();
        string root = area.At("session");
        string requestPath = area.WriteRequest(Path.Combine(root, "request.json"), "enter", game, process.Id);
        Directory.CreateDirectory(requestPath + ".accepted"); // a folder where the file belongs
        HelperRunner runner = area.Runner(root);
        runner.GameExitTimeoutSeconds = 30; // the helper would sit here for 30 s if it went on to wait for the game
        var clock = Stopwatch.StartNew();

        int exit = area.Host(runner, root, NewMutexName(), new List<string>()).Execute(Command("enter", "--request", requestPath));

        Assert.Equal(1, exit);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "the helper waited for the game although the client could never close it");
        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("cannot acknowledge the request", log);
        Assert.DoesNotContain("waiting for the game", log);
        Assert.False(process.HasExited);
    }

    [SkippableFact]
    public void StatusNeedsNeitherAMutexNorARequest()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        string mutexName = NewMutexName();
        var output = new List<string>();
        HelperHost host = area.Host(area.Runner(root), root, mutexName, output);

        int exit;
        using (new MutexOwner(mutexName))
        {
            host.MutexWaitSeconds = 0;
            exit = host.Execute(Command("status"));
        }

        Assert.Equal(0, exit);
        string line = Assert.Single(output);
        Assert.True(ArenaMiniJson.TryParse(line, out object? status, out string? error), error);
        Assert.Equal(1, ArenaMiniJson.GetInt(status, "schema", 0));
    }

    // ------------------------------------------------------------------ a request the client takes back

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), what);
            Thread.Sleep(20);
        }
    }

    [SkippableTheory]
    [InlineData("enter", "delete")]
    [InlineData("leave", "delete")]
    [InlineData("enter", "replace")]
    [InlineData("leave", "replace")]
    public void ARequestTheClientTakesBackWhileTheHelperWaitsEndsTheRunWithoutChangingAnything(string direction, string how)
    {
        SkipUnlessWindows();
        using var area = new Area();
        var (game, process) = area.RunningGame(); // alive for the whole test: only the withdrawal can end the helper's wait
        string root = area.At("session");
        string requestPath = area.WriteRequest(Path.Combine(root, "request.json"), direction, game, process.Id);
        HelperRunner runner = area.Runner(root);
        runner.GameExitTimeoutSeconds = 120; // far more than the test takes
        HelperHost host = area.Host(runner, root, NewMutexName(), new List<string>());
        int exit = -1;
        var helper = new Thread(() => exit = host.Execute(Command(direction, "--request", requestPath)));
        helper.Start();
        try
        {
            WaitUntil(() => File.Exists(requestPath + ".accepted"), "the helper did not acknowledge the request");
            Assert.True(helper.IsAlive, "the helper waits for the game");

            // The client gives up. It deletes the request file; a new attempt writes a new file (same text, new time).
            if (how == "delete")
            {
                File.Delete(requestPath);
            }
            else
            {
                string replacement = requestPath + ".replacement";
                File.WriteAllText(replacement, File.ReadAllText(requestPath));
                File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(requestPath).AddSeconds(2));
                File.Move(replacement, requestPath, true);
            }

            Assert.True(helper.Join(TimeSpan.FromSeconds(20)), "the helper kept waiting for the game although the client took the request back");
        }
        finally
        {
            if (helper.IsAlive) helper.Join(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(1, exit);
        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("the request was withdrawn", log);
        Assert.DoesNotContain("did not exit within", log);
        Assert.False(process.HasExited, "the game was not touched");
        Assert.False(Directory.Exists(HelperServerTree.GetTreePath(root)), "nothing was changed");
    }

    [SkippableFact]
    public void ARequestThatStandsUnchangedKeepsTheHelperWaitingForTheGame()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var (game, process) = area.RunningGame();
        string root = area.At("session");
        string requestPath = area.WriteRequest(Path.Combine(root, "request.json"), "enter", game, process.Id);
        HelperRunner runner = area.Runner(root);
        runner.GameExitTimeoutSeconds = 2;

        int exit = area.Host(runner, root, NewMutexName(), new List<string>()).Execute(Command("enter", "--request", requestPath));

        // The only way out was the helper's own time limit, not a withdrawal.
        Assert.Equal(1, exit);
        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("did not exit within", log);
        Assert.DoesNotContain("withdrawn", log);
    }

    [SkippableFact]
    public void ARequestTakenBackWhileTheGameIsGoneAndTheHelperSettlesStillEndsTheRunBeforeAnythingChanges()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        area.FillRuntime(game, FreePort());
        string root = area.At("session");
        Assert.True(HelperRequest.TryParse(Area.RequestJson("enter", game, Area.FinishedProcessId()), "enter", out HelperRequest? request, out string? error), error);
        HelperRunner runner = area.Runner(root);
        runner.GameSettleMilliseconds = 20;
        int looks = 0;
        runner.RequestStillStands = () => ++looks == 1; // there at the first look, gone at the one after the game is gone

        bool entered = runner.Enter(request!);

        Assert.False(entered);
        Assert.Equal(2, looks);
        Assert.False(Directory.Exists(HelperServerTree.GetTreePath(root)), "the tree was not built");
        Assert.Contains("the request was withdrawn", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    [Fact]
    public void TheRequestWatchSeesAFileThatIsGoneOrReplacedAndNothingElse()
    {
        string folder = Path.Combine(Path.GetTempPath(), "arena-session-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "request.json");
            File.WriteAllText(path, "{}");
            var watch = HelperRequestWatch.Stamp(path);
            Assert.True(watch.StillStands(), "the file as it was stamped");

            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(3));
            Assert.False(watch.StillStands(), "the same text with another time is another file");

            File.WriteAllText(path, "{}");
            DateTime stamped = File.GetLastWriteTimeUtc(path);
            var second = HelperRequestWatch.Stamp(path);
            File.WriteAllText(path, "{ }"); // another length, the same time
            File.SetLastWriteTimeUtc(path, stamped);
            Assert.False(second.StillStands(), "a file of another length is another file");

            var third = HelperRequestWatch.Stamp(path);
            File.Delete(path);
            Assert.False(third.StillStands(), "a deleted file is gone");

            // A file that was not there when the stamp was taken cannot be told from one that is there: nothing to compare.
            Assert.True(HelperRequestWatch.Stamp(Path.Combine(folder, "never.json")).StillStands());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // ------------------------------------------------------------------ an arena server that no pid file names

    [SkippableFact]
    public void AnArenaServerNoPidFileNamesIsEndedWhenItRunsFromTheTreeCopyOfTheCallersInstallation()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("install");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        area.RecordMarker(root, runtime, treeExe);
        Process stray = area.StartExe(treeExe); // started, and the helper died before it wrote server.pid

        StopResult result = area.Runner(root).StopArenaServer(runtime);

        Assert.Equal(StopResult.Stopped, result);
        Assert.True(Exited(stray), "the unregistered server was left running");
        Assert.Contains("although no server.pid names it", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    [SkippableFact]
    public void StopServerEndsAnUnregisteredServerWhateverInstallationItBelongsTo()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        area.RecordMarker(root, area.Runtime("install"), treeExe);
        Process stray = area.StartExe(treeExe);

        // stop-server asks for no installation (the same rule as for the registered server).
        Assert.True(area.Runner(root).StopServerCommand());

        Assert.True(Exited(stray));
    }

    [SkippableFact]
    public void AnUnregisteredServerBesideAStalePidFileIsEndedAndTheStalePidFileGoes()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("install");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        area.RecordMarker(root, runtime, treeExe);
        File.WriteAllText(HelperServerTree.GetPidFilePath(root), Area.FinishedProcessId().ToString()); // names a process that is gone
        Process stray = area.StartExe(treeExe);

        StopResult result = area.Runner(root).StopArenaServer(runtime);

        Assert.Equal(StopResult.Stopped, result);
        Assert.True(Exited(stray));
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)), "the stale record is gone");
    }

    [SkippableFact]
    public void AnUnregisteredServerOfAnotherInstallationIsLeftAlone()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string mine = area.Runtime("mine");
        string other = area.Runtime("other");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        area.RecordMarker(root, other, treeExe);
        Process stray = area.StartExe(treeExe);

        StopResult result = area.Runner(root).StopArenaServer(mine);

        Assert.Equal(StopResult.OtherInstallation, result);
        Assert.False(stray.HasExited, "the session folder is shared by every installation; the server of another one is not ours to end");
    }

    [SkippableFact]
    public void AnUnregisteredServerWithoutAMarkerCannotBeVerifiedAndIsLeftAlone()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("install");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process stray = area.StartExe(treeExe);

        StopResult result = area.Runner(root).StopArenaServer(runtime);

        Assert.Equal(StopResult.Unverified, result);
        Assert.False(stray.HasExited);
        Assert.Contains("cannot be verified", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    [SkippableFact]
    public void ATreeCopyProcessUnderTheMarkerOfAnOlderHelperIsNotAnUnregisteredServerOfThisHelper()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("install");
        string root = area.At("session");
        Process other = area.StartExe(area.TreeServerExe(root));
        area.RecordMarker(root, runtime, null!); // no serverExe: the marker of an older helper, whose server ran the installation's exe

        StopResult result = area.Runner(root).StopArenaServer(runtime);

        Assert.Equal(StopResult.NothingToStop, result);
        Assert.False(other.HasExited);
    }

    [SkippableFact]
    public void TheInstallationsOwnServerIsNotTakenForAnUnregisteredArenaServer()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("install");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        area.RecordMarker(root, runtime, treeExe);
        Process normal = area.StartServer(runtime); // the user's own server: another file than the tree copy

        StopResult result = area.Runner(root).StopArenaServer(runtime);

        Assert.Equal(StopResult.NothingToStop, result);
        Assert.False(normal.HasExited);
    }

    [SkippableFact]
    public void AnUnregisteredServerThatSurvivesTheKillIsReportedAsStillRunning()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string runtime = area.Runtime("install");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        area.RecordMarker(root, runtime, treeExe);
        area.StartExe(treeExe);
        HelperRunner runner = area.Runner(root);
        runner.ServerExitWaiter = (process, milliseconds) => false;

        StopResult result = runner.StopArenaServer(runtime);

        Assert.Equal(StopResult.StillRunning, result);
        Assert.Contains("is still running; end it by hand", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    [SkippableFact]
    public void AnEnterEndsAnUnregisteredServerOfAnInterruptedStartBeforeItReplacesTheTree()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var normal = new TcpListener(IPAddress.Loopback, 0);
        normal.Start();
        try
        {
            int normalPort = ((IPEndPoint)normal.LocalEndpoint).Port;
            var game = area.Game();
            area.FillRuntime(game, normalPort);
            string root = area.At("session");
            string treeExe = area.TreeServerExe(root);
            area.RecordMarker(root, game.Runtime, treeExe);
            Process stray = area.StartExe(treeExe);
            Assert.True(HelperRequest.TryParse(Area.RequestJson("enter", game, Area.FinishedProcessId()), "enter", out HelperRequest? request, out string? parseError), parseError);
            HelperRunner runner = area.Runner(root);
            runner.ServerStartTimeoutSeconds = 1; // the stand-in never opens the arena port, so the enter fails after the build
            var starts = new List<string>();
            runner.ProcessStarter = (exe, args, directory) =>
            {
                starts.Add(exe);
                return Process.GetProcessById(area.StartExe(exe).Id);
            };

            bool entered = runner.Enter(request!);

            Assert.False(entered);
            Assert.True(Exited(stray), "the unregistered server of the interrupted start was left running");
            string log = File.ReadAllText(Path.Combine(root, "helper.log"));
            Assert.Contains("stopping the unregistered arena server", log);
            Assert.Contains("server tree ready", log); // the build went ahead only once the stray was gone
            Assert.DoesNotContain("core config:", log); // a runtime without a core config: nothing to switch on, no line
            Assert.True(log.IndexOf("stopping the unregistered arena server", StringComparison.Ordinal)
                        < log.IndexOf("server tree ready", StringComparison.Ordinal));
        }
        finally
        {
            normal.Stop();
        }
    }

    // ------------------------------------------------------------------ registering the arena server

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [SkippableFact]
    public void AServerWhosePidCannotBeWrittenIsEndedWithTheHandleAtHand()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        HelperTreeResult tree = area.TreeForStart(root, null);
        HelperRunner runner = area.Runner(root);
        runner.FileRetryAttempts = 2;
        runner.FileRetryDelayMilliseconds = 10;
        Process? started = null;
        runner.ProcessStarter = (exe, args, directory) =>
        {
            started = area.StartExe(exe);
            return Process.GetProcessById(started.Id); // the runner disposes what it gets; the test keeps its own handle
        };

        // server.pid is held open without sharing: a scanner or an editor has it at the wrong moment.
        HelperException ex;
        using (new FileStream(HelperServerTree.GetPidFilePath(root), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            ex = Assert.Throws<HelperException>(() => runner.StartArenaServer(tree));
        }

        Assert.NotNull(started);
        Assert.True(Exited(started!), "the unregistered server was left running");
        Assert.Contains("started but server.pid could not be written", ex.Message);
        Assert.Contains(started!.Id.ToString(), ex.Message);
        Assert.Contains("was ended", ex.Message);
        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("unknown to every cleanup", log);
    }

    [SkippableFact]
    public void AStalePidFileIsRemovedWhenTheServerCannotBeRegistered()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        HelperTreeResult tree = area.TreeForStart(root, null);
        HelperRunner runner = area.Runner(root);
        runner.FileRetryAttempts = 2;
        runner.FileRetryDelayMilliseconds = 10;
        runner.ProcessStarter = (exe, args, directory) => Process.GetProcessById(area.StartExe(exe).Id);

        // An earlier session left a pid file that names another process, read-only, so that the new write fails. If
        // it stayed, the next stop-server would take that pid for the server that was just started.
        string pidPath = HelperServerTree.GetPidFilePath(root);
        File.WriteAllText(pidPath, "999999");
        File.SetAttributes(pidPath, FileAttributes.ReadOnly);

        var ex = Assert.Throws<HelperException>(() => runner.StartArenaServer(tree));

        Assert.Contains("started but server.pid could not be written", ex.Message);
        Assert.False(File.Exists(pidPath), "a pid file that does not name this server must not be left behind");
    }

    [SkippableFact]
    public void AServerThatCannotBeEndedAfterAFailedRegistrationIsReportedWithItsPid()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        HelperTreeResult tree = area.TreeForStart(root, null);
        HelperRunner runner = area.Runner(root);
        runner.FileRetryAttempts = 2;
        runner.FileRetryDelayMilliseconds = 10;
        runner.ServerExitWaiter = (process, milliseconds) => false; // the kill did not take
        Process? started = null;
        runner.ProcessStarter = (exe, args, directory) =>
        {
            started = area.StartExe(exe);
            return Process.GetProcessById(started.Id);
        };

        HelperException ex;
        using (new FileStream(HelperServerTree.GetPidFilePath(root), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            ex = Assert.Throws<HelperException>(() => runner.StartArenaServer(tree));
        }

        Assert.NotNull(started);
        Assert.Contains("could NOT be ended", ex.Message);
        Assert.Contains("end the process with pid " + started!.Id, ex.Message);
        Assert.DoesNotContain("was ended", ex.Message);
        Assert.Contains("is still running; end it by hand", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    [SkippableFact]
    public void AServerWhosePidWasWrittenStaysRegisteredWhenItsPortNeverOpens()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        HelperTreeResult tree = area.TreeForStart(root, FreePort());
        HelperRunner runner = area.Runner(root);
        runner.ServerStartTimeoutSeconds = 1;
        Process? started = null;
        runner.ProcessStarter = (exe, args, directory) =>
        {
            started = area.StartExe(exe);
            return Process.GetProcessById(started.Id);
        };

        var ex = Assert.Throws<HelperException>(() => runner.StartArenaServer(tree));

        // The pid is on disk before the wait, and a failure of the wait leaves the server to stop-server (the
        // recovery runs it), not to the start: only an unregistered server is ended on the spot.
        Assert.Contains("did not open port", ex.Message);
        Assert.NotNull(started);
        Assert.Equal(started!.Id, int.Parse(File.ReadAllText(HelperServerTree.GetPidFilePath(root)).Trim()));
        Assert.False(started.HasExited);
    }

    [SkippableFact]
    public void AnEnterWhoseServerCannotBeRegisteredEndsItAndBringsTheNormalSessionBack()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var normal = new TcpListener(IPAddress.Loopback, 0);
        normal.Start();
        try
        {
            int normalPort = ((IPEndPoint)normal.LocalEndpoint).Port;
            var game = area.Game();
            area.FillRuntime(game, normalPort);
            Directory.CreateDirectory(Path.Combine(game.Runtime, "user", "patchers", "com.maschine.ArenaMode"));
            File.WriteAllText(Path.Combine(game.Runtime, "user", "patchers", "com.maschine.ArenaMode", "enums.json"), "{}");
            Directory.CreateDirectory(Path.Combine(game.Runtime, "user", "patchers", "com.foreign.unlisted"));
            File.WriteAllText(Path.Combine(game.Runtime, "user", "patchers", "com.foreign.unlisted", "enums.json"), "{}");
            string root = area.At("session");
            Directory.CreateDirectory(root);
            Assert.True(HelperRequest.TryParse(Area.RequestJson("enter", game, Area.FinishedProcessId()), "enter", out HelperRequest? request, out string? parseError), parseError);
            HelperRunner runner = area.Runner(root);
            runner.FileRetryAttempts = 2;
            runner.FileRetryDelayMilliseconds = 10;
            var starts = new List<(string Exe, string Arguments)>();
            Process? server = null;
            FileStream? pidLock = null;
            runner.ProcessStarter = (exe, args, directory) =>
            {
                starts.Add((exe, args));
                Process process = area.StartExe(exe);
                if (starts.Count == 1)
                {
                    // The arena server: from the moment it exists, server.pid is held open without sharing (a scanner
                    // or an editor at the wrong moment), so its registration fails. Earlier steps still see no pid file.
                    server = process;
                    pidLock = new FileStream(HelperServerTree.GetPidFilePath(root), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                }

                return Process.GetProcessById(process.Id);
            };

            bool entered;
            try
            {
                entered = runner.Enter(request!);
            }
            finally
            {
                pidLock?.Dispose();
            }

            // The enter failed, and what it started is gone ...
            Assert.False(entered);
            Assert.NotNull(server);
            Assert.True(Exited(server!), "the unregistered arena server was left running");
            Assert.Equal(Path.Combine(HelperServerTree.GetTreePath(root), HelperLayout.ServerExeName), starts[0].Exe);

            // ... the normal session came back: exactly one game, without the session flag, on the normal port.
            Assert.Equal(2, starts.Count);
            Assert.Equal(game.Exe, starts[1].Exe);
            Assert.DoesNotContain("-arena-session", starts[1].Arguments);
            Assert.Contains("127.0.0.1:" + normalPort, starts[1].Arguments);
            string log = File.ReadAllText(Path.Combine(root, "helper.log"));
            Assert.Contains("server prepatch folders (user\\patchers): 1 (com.maschine.ArenaMode)", log); // the count of the copied folders is logged
            Assert.Contains("enter failed: the arena server", log);
            Assert.Contains("started but server.pid could not be written", log);
            Assert.Contains("recovery finished", log);
        }
        finally
        {
            normal.Stop();
        }
    }

    [SkippableFact]
    public void TheNormalServerThatLeaveStartsIsNotRecordedAsTheArenaServer()
    {
        SkipUnlessWindows();
        using var area = new Area();
        int port = FreePort(); // nothing listens there, so leave has to start the normal server
        var game = area.Game();
        area.FillRuntime(game, port);
        string root = area.At("session");
        Assert.True(HelperRequest.TryParse(Area.RequestJson("leave", game, Area.FinishedProcessId()), "leave", out HelperRequest? request, out string? parseError), parseError);
        HelperRunner runner = area.Runner(root);
        var starts = new List<string>();
        TcpListener? listener = null;
        runner.ProcessStarter = (exe, args, directory) =>
        {
            starts.Add(exe);
            if (!string.Equals(exe, Path.Combine(game.Runtime, HelperLayout.ServerExeName), StringComparison.OrdinalIgnoreCase))
            {
                return Process.GetProcessById(area.StartExe(exe).Id);
            }

            // The "server" opens its port inside this test process, so this process is the listener's owner and
            // its own handle is the one that passes the helper's check that the started process holds the port.
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return Process.GetCurrentProcess();
        };

        bool left;
        try
        {
            left = runner.Leave(request!);
        }
        finally
        {
            listener?.Stop();
        }

        // The normal server is the user's own: it is started and left running, and no pid file names it, because
        // the arena server's pid file is what stop-server and the next enter would end.
        Assert.True(left);
        Assert.Equal(new[] { Path.Combine(game.Runtime, HelperLayout.ServerExeName), game.Exe }, starts);
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)));
        Assert.Contains("starting the normal server", File.ReadAllText(Path.Combine(root, "helper.log")));
    }

    // ------------------------------------------------------------------ waiting for the server: who owns the port

    private static TcpListener OpenListener(IPAddress address)
    {
        var listener = new TcpListener(address, 0);
        listener.Start();
        return listener;
    }

    /// <summary>A runner whose owner lookup is <paramref name="lookup"/> and whose wait for a server ends after one second.</summary>
    private static HelperRunner WaitRunner(Area area, string root, Func<int, IPAddress, List<int>> lookup)
    {
        HelperRunner runner = area.Runner(root);
        runner.ServerStartTimeoutSeconds = 1;
        runner.ListenerLookup = lookup;
        return runner;
    }

    /// <summary>A stand-in server process that is not this test process, as a handle the test may dispose.</summary>
    private static Process StandInServer(Area area)
    {
        return Process.GetProcessById(area.StartServer(area.Runtime("standin")).Id);
    }

    private static List<string> WarningsOf(string root)
    {
        return File.ReadAllLines(Path.Combine(root, "helper.log")).Where(line => line.Contains(" WARN ")).ToList();
    }

    [SkippableFact]
    public void AnOwnerTheSystemCannotNameIsAcceptedWhenThePortWasVerifiedFreeBeforeTheStartAndSaysSo()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        using Process server = StandInServer(area);
        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            HelperRunner runner = WaitRunner(area, root, (p, a) => null!); // the TCP table cannot be read

            runner.WaitForServer("127.0.0.1", port, server, "the test server", true);

            List<string> warnings = WarningsOf(root);
            Assert.Single(warnings);
            Assert.Contains("the owner of port " + port + " could not be verified", warnings[0]);
            Assert.Contains("verified free just before the start", warnings[0]);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void AnOwnerTheSystemCannotNameIsNotAcceptedWhenThePortWasNotVerifiedFreeAndTheTimeoutNamesTheReason()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        using Process server = StandInServer(area);
        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            HelperRunner runner = WaitRunner(area, root, (p, a) => null!);

            var ex = Assert.Throws<HelperException>(() => runner.WaitForServer("127.0.0.1", port, server, "the test server", false));

            Assert.Contains("did not open port " + port, ex.Message);
            Assert.Contains("the owner of port " + port + " could not be verified", ex.Message);
            Assert.Contains("not verified free just before the start", ex.Message);
            Assert.Single(WarningsOf(root)); // one warning for the whole wait, not one per round
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void ALookupThatThrowsIsATableThatCannotBeRead()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        using Process server = StandInServer(area);
        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            HelperRunner runner = WaitRunner(area, root, (p, a) => throw new InvalidOperationException("no table"));

            var ex = Assert.Throws<HelperException>(() => runner.WaitForServer("127.0.0.1", port, server, "the test server", false));
            Assert.Contains("could not be verified", ex.Message);

            runner.WaitForServer("127.0.0.1", port, server, "the test server", true);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AConnectionTheTableDoesNotLinkToAnyListenerIsNotTakenForTheServer(bool portVerifiedFree)
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        using Process server = StandInServer(area);
        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            HelperRunner runner = WaitRunner(area, root, (p, a) => new List<int>()); // the listener vanished, or is not listed yet

            var ex = Assert.Throws<HelperException>(() => runner.WaitForServer("127.0.0.1", port, server, "the test server", portVerifiedFree));

            Assert.Contains("did not open port " + port, ex.Message);
            Assert.Contains("lists no listener for it", ex.Message);
            Assert.Single(WarningsOf(root));
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnotherProcessHoldingThePortIsNotTakenForTheServer(bool portVerifiedFree)
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        using Process server = StandInServer(area);
        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            HelperRunner runner = WaitRunner(area, root, (p, a) => new List<int> { Environment.ProcessId });

            var ex = Assert.Throws<HelperException>(() => runner.WaitForServer("127.0.0.1", port, server, "the test server", portVerifiedFree));

            Assert.Contains("port " + port + " is held by pid " + Environment.ProcessId + ", not by the server that was started (pid " + server.Id + ")", ex.Message);
            Assert.Single(WarningsOf(root));
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void TheStartedProcessListeningMakesTheServerReadyWhateverElseHoldsTheOtherAddresses()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        using Process server = StandInServer(area);
        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            HelperRunner runner = WaitRunner(area, root, (p, a) => new List<int> { Environment.ProcessId, server.Id });

            runner.WaitForServer("127.0.0.1", port, server, "the test server", false);

            Assert.Empty(WarningsOf(root));
            Assert.Contains("the test server accepts connections after", File.ReadAllText(Path.Combine(root, "helper.log")));
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void AServerThatHasExitedEndsTheWaitAtOnceWhateverAnswers()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        Process server = area.StartServer(area.Runtime("standin")); // started by Process.Start, like the real server: its exit code can be read
        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            HelperRunner runner = WaitRunner(area, root, (p, a) => null!);
            server.Kill();
            Assert.True(Exited(server));

            var ex = Assert.Throws<HelperException>(() => runner.WaitForServer("127.0.0.1", port, server, "the test server", true));

            Assert.Contains("exited with code", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    [SkippableFact]
    public void TheOwnerIsLookedUpForTheAddressTheConnectionReachedAndNoOther()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        using Process server = StandInServer(area);
        var seen = new List<(int Port, IPAddress Address)>();
        HelperRunner runner = WaitRunner(area, root, (p, a) =>
        {
            seen.Add((p, a));
            return new List<int> { server.Id };
        });

        var listener = OpenListener(IPAddress.Loopback);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            runner.WaitForServer("127.0.0.1", port, server, "the test server", false);

            Assert.Equal(port, seen[0].Port);
            Assert.Equal(IPAddress.Loopback, seen[0].Address); // the IPv4 address, not its ::ffff: spelling
        }
        finally
        {
            listener.Stop();
        }

        if (!Socket.OSSupportsIPv6) return;
        seen.Clear();
        var listener6 = OpenListener(IPAddress.IPv6Loopback);
        try
        {
            int port = ((IPEndPoint)listener6.LocalEndpoint).Port;
            runner.WaitForServer("::1", port, server, "the test server", false);

            Assert.Equal(port, seen[0].Port);
            Assert.Equal(IPAddress.IPv6Loopback, seen[0].Address);
        }
        finally
        {
            listener6.Stop();
        }
    }

    // ------------------------------------------------------------------ waiting for the server: what the runner passes in

    [SkippableFact]
    public void TheArenaServersPortIsProbedAgainJustBeforeItStartsAndAnUnnamedOwnerIsAcceptedBecauseOfThat()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        int port = FreePort();
        HelperTreeResult tree = area.TreeForStart(root, port);
        HelperRunner runner = area.Runner(root);
        runner.ServerStartTimeoutSeconds = 5; // acceptance takes well under a second; a regression must fail fast, not after 180 s
        runner.ListenerLookup = (p, a) => null!;
        var order = new List<string>();
        TcpListener? listener = null;
        runner.PortFreeProbe = (host, p) =>
        {
            order.Add("probe " + host + ":" + p);
            return true;
        };
        runner.ProcessStarter = (exe, args, directory) =>
        {
            order.Add("start");
            listener = new TcpListener(IPAddress.Loopback, port); // the server opens its port
            listener.Start();
            return Process.GetProcessById(area.StartExe(exe).Id);
        };

        try
        {
            runner.StartArenaServer(tree);
        }
        finally
        {
            listener?.Stop();
        }

        Assert.Equal(new[] { "probe 127.0.0.1:" + port, "start" }, order);
        List<string> warnings = WarningsOf(root);
        Assert.Single(warnings);
        Assert.Contains("could not be verified", warnings[0]);
    }

    [SkippableFact]
    public void AnUnnamedOwnerIsNotAcceptedWhenThePortWasNotFreeJustBeforeTheStart()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        int port = FreePort();
        HelperTreeResult tree = area.TreeForStart(root, port);
        HelperRunner runner = area.Runner(root);
        runner.ServerStartTimeoutSeconds = 1;
        runner.ListenerLookup = (p, a) => null!;
        runner.PortFreeProbe = (host, p) => false; // somebody held the port after it was chosen
        TcpListener? listener = null;
        runner.ProcessStarter = (exe, args, directory) =>
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return Process.GetProcessById(area.StartExe(exe).Id);
        };

        HelperException ex;
        try
        {
            ex = Assert.Throws<HelperException>(() => runner.StartArenaServer(tree));
        }
        finally
        {
            listener?.Stop();
        }

        Assert.Contains("did not open port " + port, ex.Message);
        Assert.Contains("could not be verified", ex.Message);
        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("port " + port + " is not free just before the arena server starts", log);
    }

    [SkippableFact]
    public void AProbeThatThrowsCountsAsAPortThatIsNotFree()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        int port = FreePort();
        HelperTreeResult tree = area.TreeForStart(root, port);
        HelperRunner runner = area.Runner(root);
        runner.ServerStartTimeoutSeconds = 1;
        runner.ListenerLookup = (p, a) => null!;
        runner.PortFreeProbe = (host, p) => throw new InvalidOperationException("probe failed");
        TcpListener? listener = null;
        runner.ProcessStarter = (exe, args, directory) =>
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return Process.GetProcessById(area.StartExe(exe).Id);
        };

        try
        {
            Assert.Throws<HelperException>(() => runner.StartArenaServer(tree));
        }
        finally
        {
            listener?.Stop();
        }
    }

    [SkippableFact]
    public void ALeaveAcceptsTheNormalServerWhoseOwnerTheSystemCannotNameBecauseNothingHeldThePortBeforeItsStart()
    {
        SkipUnlessWindows();
        using var area = new Area();
        int port = FreePort(); // nothing listens there, so leave has to start the normal server
        var game = area.Game();
        area.FillRuntime(game, port);
        string root = area.At("session");
        Assert.True(HelperRequest.TryParse(Area.RequestJson("leave", game, Area.FinishedProcessId()), "leave", out HelperRequest? request, out string? parseError), parseError);
        HelperRunner runner = area.Runner(root);
        runner.ServerStartTimeoutSeconds = 2;
        runner.ListenerLookup = (p, a) => null!;
        var starts = new List<string>();
        TcpListener? listener = null;
        runner.ProcessStarter = (exe, args, directory) =>
        {
            starts.Add(exe);
            if (string.Equals(exe, Path.Combine(game.Runtime, HelperLayout.ServerExeName), StringComparison.OrdinalIgnoreCase))
            {
                listener = new TcpListener(IPAddress.Loopback, port); // the normal server opens its port
                listener.Start();
            }

            return Process.GetProcessById(area.StartExe(exe).Id); // a stand-in, never this process
        };

        bool left;
        try
        {
            left = runner.Leave(request!);
        }
        finally
        {
            listener?.Stop();
        }

        Assert.True(left);
        Assert.Equal(new[] { Path.Combine(game.Runtime, HelperLayout.ServerExeName), game.Exe }, starts);
        List<string> warnings = WarningsOf(root);
        Assert.Single(warnings);
        Assert.Contains("could not be verified", warnings[0]);
    }

    [SkippableFact]
    public void AnEnterProbesTheArenaPortOnTheNormalServersAddressWarnsOnceAboutLoggerPathsAndTheBackupDirectoryAndSwitchesOnTheProfileCleanUp()
    {
        SkipUnlessWindows();
        using var area = new Area();
        int normalPort = FreePort();
        var game = area.Game();
        area.FillRuntime(game, normalPort);
        File.WriteAllText(Path.Combine(game.Runtime, "SPT_Data", "configs", HelperLayout.HttpJsonFileName), // the server's own address is not the usual 127.0.0.1
            "{ \"ip\": \"127.0.0.2\", \"port\": " + normalPort + ", \"backendIp\": \"127.0.0.2\", \"backendPort\": " + normalPort + " }");
        string absolute = area.At("real runtime", "user", "logs", "spt");
        File.WriteAllText(Path.Combine(game.Runtime, HelperLayout.LoggerFileName),
            "{ \"loggers\": [ { \"type\": \"File\", \"filePath\": \"" + absolute.Replace("\\", "\\\\") + "\", \"filePattern\": \"spt-%DATE%.log\" }, "
            + "{ \"type\": \"File\", \"filePath\": \"./user/logs/requests/\", \"filePattern\": \"requests-%DATE%.log\" } ] }");
        string externalBackups = area.At("real runtime", "user", "profiles", "backups"); // the normal server's backup pool
        File.WriteAllText(Path.Combine(game.Runtime, "SPT_Data", "configs", HelperLayout.BackupJsonFileName),
            "{ \"enabled\": true, \"maxBackups\": 15, \"directory\": \"" + externalBackups.Replace("\\", "\\\\") + "\" }");
        File.WriteAllText(Path.Combine(game.Runtime, "SPT_Data", "configs", "core.json"), // SPT's shipped clean-up flags: off
            "{ \"fixes\": { \"removeModItemsFromProfile\": false, \"removeInvalidTradersFromProfile\": false } }");
        string root = area.At("session");
        Assert.True(HelperRequest.TryParse(Area.RequestJson("enter", game, Area.FinishedProcessId()), "enter", out HelperRequest? request, out string? parseError), parseError);
        HelperRunner runner = area.Runner(root);
        var probes = new List<(string Host, int Port)>();
        runner.PortFreeProbe = (host, p) =>
        {
            probes.Add((host, p));
            return p != 6970; // 6970 is taken
        };
        runner.ProcessStarter = (exe, args, directory) => null!; // nothing starts: the enter stops at the server, and so does the recovery

        bool entered = runner.Enter(request!);

        Assert.False(entered);
        Assert.Equal(new[] { ("127.0.0.2", 6970), ("127.0.0.2", 6971), ("127.0.0.2", 6971) }, probes); // two to choose, one just before the start
        string log = File.ReadAllText(Path.Combine(root, "helper.log"));
        Assert.Contains("port 6970 is taken, the arena server uses 6971", log);
        List<string> warnings = WarningsOf(root).Where(line => line.Contains("sptLogger.json")).ToList();
        Assert.Single(warnings);
        Assert.Contains("1 logger path(s) did not lie below user", warnings[0]);
        Assert.Contains("'" + absolute + "' -> './user/logs/spt/'", warnings[0]);
        Assert.DoesNotContain("requests", warnings[0]);
        string copy = File.ReadAllText(Path.Combine(HelperServerTree.GetTreePath(root), "sptLogger.json"));
        Assert.Contains("\"filePath\": \"./user/logs/spt/\"", copy);

        // The backup directory the same way: one Warning, the tree's copy confined, the runtime's file untouched.
        List<string> backupWarnings = WarningsOf(root).Where(line => line.Contains("backup config")).ToList();
        Assert.Single(backupWarnings);
        Assert.Contains("1 backup directory value(s) did not lie below user", backupWarnings[0]);
        Assert.Contains("backup.json: '" + externalBackups + "' -> './user/profiles/backups'", backupWarnings[0]);
        Assert.Contains("\"directory\": \"./user/profiles/backups\"", File.ReadAllText(Path.Combine(HelperServerTree.GetTreePath(root), "SPT_Data", "configs", "backup.json")));
        Assert.Contains(externalBackups.Replace("\\", "\\\\"), File.ReadAllText(Path.Combine(game.Runtime, "SPT_Data", "configs", "backup.json")));

        // The core config: one Info line (SPT ships the flags off, so it is no Warning), the copy cleans, the runtime's file stays.
        List<string> cleanup = LogOf(root).Split('\n').Where(line => line.Contains("core config:")).ToList();
        Assert.Single(cleanup);
        Assert.Contains(" INFO ", cleanup[0]);
        Assert.Contains("core.json: fixes.removeModItemsFromProfile: false -> true, fixes.removeInvalidTradersFromProfile: false -> true", cleanup[0]);
        Assert.DoesNotContain(WarningsOf(root), line => line.Contains("core config:"));
        Assert.Contains("\"removeModItemsFromProfile\": true", File.ReadAllText(Path.Combine(HelperServerTree.GetTreePath(root), "SPT_Data", "configs", "core.json")));
        Assert.Contains("\"removeModItemsFromProfile\": false", File.ReadAllText(Path.Combine(game.Runtime, "SPT_Data", "configs", "core.json")));
    }
    // ------------------------------------------------------------------ the watchdog after enter (watch --file)

    /// <summary>What the enter writes into watch.json for the game and the registered server of a session root.</summary>
    private static HelperWatchFile WatchFor(string root, Area.GameFolder game, Process arenaGame, int serverPid, string? markerCreatedUtc = null)
    {
        if (markerCreatedUtc == null)
        {
            Assert.True(HelperMarker.TryParse(File.ReadAllText(HelperServerTree.GetMarkerPath(root)), out HelperMarker? marker, out string? error), error);
            markerCreatedUtc = marker!.CreatedUtc;
        }

        return new HelperWatchFile
        {
            GamePid = arenaGame.Id,
            GameExe = Path.GetFileName(game.Exe),
            GameStartUtc = arenaGame.StartTime.ToUniversalTime().ToString("o"),
            ServerPid = serverPid,
            MarkerCreatedUtc = markerCreatedUtc,
            SptRuntimeDir = game.Runtime,
        };
    }

    /// <summary>The helper log while another thread appends to it; empty until the first line created the file.</summary>
    private static string LogOf(string root)
    {
        string path = Path.Combine(root, "helper.log");
        return File.Exists(path) ? HelperFileSystem.ReadAllTextShared(path) : string.Empty;
    }

    /// <summary>
    /// The watchdog's two halves, driven directly: the runner holds the game (a long-lived stand-in) and the watch file of a
    /// registered arena server; once the test ends the game, the watch must end the server and its pid file and say so.
    /// </summary>
    [SkippableFact]
    public void TheWatchStopsTheArenaServerWhenTheArenaGameExits()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, game.Runtime, "session", treeExe);
        Process arenaGame = area.StartExe(game.Exe);
        HelperRunner runner = area.Runner(root);
        runner.ArenaGame = Process.GetProcessById(arenaGame.Id);
        runner.Watch = WatchFor(root, game, arenaGame, server.Id);
        HelperHost host = area.Host(runner, root, NewMutexName(), new List<string>());

        Task watch = Task.Run(host.WatchArenaGame);
        WaitUntil(() => LogOf(root).Contains("watching the Arena game (pid " + arenaGame.Id + ")"), "the watch did not announce the game");
        Assert.False(watch.IsCompleted, "the watch returned while the game was still running");
        Assert.False(server.HasExited, "the server was stopped before the game ended");
        arenaGame.Kill();
        Assert.True(watch.Wait(TimeSpan.FromSeconds(20)), "the watch did not finish after the game ended");

        Assert.True(Exited(server), "the arena server outlived the game");
        Assert.False(File.Exists(HelperServerTree.GetPidFilePath(root)), "server.pid was left behind");
        string log = LogOf(root);
        Assert.Contains("the Arena game exited", log);
        Assert.Contains("; stopping the arena server", log);
        Assert.Contains("arena server after the game: Stopped", log);
    }

    /// <summary>
    /// A leave holds the mutex from its start until the normal game runs again, and it stops the arena server itself. The
    /// watch, which sees the game exit meanwhile, must wait for the mutex instead of killing alongside, and then find
    /// nothing left to stop.
    /// </summary>
    [SkippableFact]
    public void TheWatchWaitsForALeaveThatHoldsTheMutexAndThenFindsNothingLeftToStop()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, game.Runtime, "session", treeExe);
        Process arenaGame = area.StartExe(game.Exe);
        HelperRunner runner = area.Runner(root);
        runner.ArenaGame = Process.GetProcessById(arenaGame.Id);
        runner.Watch = WatchFor(root, game, arenaGame, server.Id);
        string mutexName = NewMutexName();
        HelperHost host = area.Host(runner, root, mutexName, new List<string>());
        host.WatchMutexWaitSeconds = 30;

        Task watch;
        using (new MutexOwner(mutexName))
        {
            watch = Task.Run(host.WatchArenaGame);
            WaitUntil(() => LogOf(root).Contains("watching the Arena game"), "the watch did not announce the game");
            arenaGame.Kill();
            WaitUntil(() => LogOf(root).Contains("; stopping the arena server"), "the watch did not see the game exit");
            Thread.Sleep(500); // a grace period: the watch is now waiting for the mutex
            Assert.False(watch.IsCompleted, "the watch did not wait for the mutex");
            Assert.False(server.HasExited, "the watch killed the server without the mutex");
            Assert.DoesNotContain("arena server after the game", LogOf(root));
            // The leave's own work: it stops the arena server and takes the pid file with it.
            server.Kill();
            Assert.True(Exited(server));
            File.Delete(HelperServerTree.GetPidFilePath(root));
        }

        Assert.True(watch.Wait(TimeSpan.FromSeconds(20)), "the watch did not finish once the mutex was free");
        string log = LogOf(root);
        Assert.Contains("arena server after the game: NothingToStop", log);
        Assert.DoesNotContain("another helper holds the mutex", log);
    }

    /// <summary>A mutex that stays busy beyond the watch's patience: nothing is killed, the log says who gets the server.</summary>
    [SkippableFact]
    public void TheWatchLeavesTheServerToAHelperThatKeepsTheMutex()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, game.Runtime, "session", treeExe);
        Process arenaGame = area.StartExe(game.Exe);
        HelperRunner runner = area.Runner(root);
        runner.ArenaGame = Process.GetProcessById(arenaGame.Id);
        runner.Watch = WatchFor(root, game, arenaGame, server.Id);
        string mutexName = NewMutexName();
        HelperHost host = area.Host(runner, root, mutexName, new List<string>());
        host.WatchMutexWaitSeconds = 0;

        Task watch;
        using (new MutexOwner(mutexName))
        {
            watch = Task.Run(host.WatchArenaGame);
            WaitUntil(() => LogOf(root).Contains("watching the Arena game"), "the watch did not announce the game");
            arenaGame.Kill();
            Assert.True(watch.Wait(TimeSpan.FromSeconds(20)), "the watch did not give up on the busy mutex");
        }

        Assert.False(server.HasExited, "the watch killed the server although another helper held the mutex");
        Assert.True(File.Exists(HelperServerTree.GetPidFilePath(root)));
        string log = LogOf(root);
        Assert.Contains("another helper holds the mutex for more than 0 s; the arena server is left to it", log);
        Assert.DoesNotContain("arena server after the game", log);
        Assert.Single(WarningsOf(root).Where(line => line.Contains("another helper holds the mutex")));
    }

    /// <summary>
    /// A watchdog whose game lingered may wake after the next session began: server.pid then names the newer server and
    /// the marker is the newer session's. Nothing of the newer session may be touched.
    /// </summary>
    [SkippableFact]
    public void TheWatchLeavesANewerSessionsServerAlone()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process older = area.StartExe(treeExe);
        Process newer = area.StartExe(treeExe);
        area.Record(newer, game.Runtime, "session", treeExe); // the newer enter's marker and pid file
        Process arenaGame = area.StartExe(game.Exe);
        HelperRunner runner = area.Runner(root);
        runner.ArenaGame = Process.GetProcessById(arenaGame.Id);
        runner.Watch = WatchFor(root, game, arenaGame, older.Id); // the newer enter's marker, this session's server pid
        HelperHost host = area.Host(runner, root, NewMutexName(), new List<string>());

        Task watch = Task.Run(host.WatchArenaGame);
        WaitUntil(() => LogOf(root).Contains("watching the Arena game"), "the watch did not announce the game");
        arenaGame.Kill();
        Assert.True(watch.Wait(TimeSpan.FromSeconds(20)));

        Assert.False(older.HasExited, "the older server was killed; the newer enter had already replaced the record");
        Assert.False(newer.HasExited, "the newer session's server was killed by the older watchdog");
        Assert.Equal(newer.Id.ToString(), File.ReadAllText(HelperServerTree.GetPidFilePath(root)).Trim());
        string log = LogOf(root);
        Assert.Contains("server.pid names pid " + newer.Id + ", not this session's server (pid " + older.Id + "): a newer session owns the record", log);
        Assert.DoesNotContain("arena server after the game", log);

        // The same when the marker is a newer session's, whatever the pid file says, and once the pid file is gone.
        runner.Watch.MarkerCreatedUtc = "2026-01-01T00:00:00Z";
        Assert.Equal(StopResult.NothingToStop, runner.StopAfterGameExit());
        File.Delete(HelperServerTree.GetPidFilePath(root));
        Assert.Equal(StopResult.NothingToStop, runner.StopAfterGameExit());
        Assert.False(newer.HasExited);
        Assert.False(older.HasExited);
        Assert.Contains("the marker in place is a newer session's", LogOf(root));
    }

    /// <summary>The installation rule of enter and leave holds for the watchdog: the server of another installation is left alone.</summary>
    [SkippableFact]
    public void TheWatchLeavesTheServerOfAnotherInstallationAlone()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string other = area.Runtime("other", "SPT_Runtime");
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, other, "session", treeExe);
        Process arenaGame = area.StartExe(game.Exe);
        HelperRunner runner = area.Runner(root);
        runner.ArenaGame = Process.GetProcessById(arenaGame.Id);
        runner.Watch = WatchFor(root, game, arenaGame, server.Id);
        HelperHost host = area.Host(runner, root, NewMutexName(), new List<string>());

        Task watch = Task.Run(host.WatchArenaGame);
        WaitUntil(() => LogOf(root).Contains("watching the Arena game"), "the watch did not announce the game");
        arenaGame.Kill();
        Assert.True(watch.Wait(TimeSpan.FromSeconds(20)));

        Assert.False(server.HasExited, "the server of another installation was killed");
        Assert.True(File.Exists(HelperServerTree.GetPidFilePath(root)));
        Assert.Contains("arena server after the game: OtherInstallation", LogOf(root));
    }

    /// <summary>
    /// The watch command as the enter starts it: it reads watch.json, opens the game by pid, takes no mutex while it waits,
    /// and stops the server once the game is gone. A game that is already gone when the command starts is stopped at once.
    /// </summary>
    [SkippableFact]
    public void TheWatchCommandReadsItsFileWaitsForTheGameAndStopsTheServer()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, game.Runtime, "session", treeExe);
        Process arenaGame = area.StartExe(game.Exe);
        string path = Path.Combine(root, HelperLayout.WatchFileName);
        File.WriteAllText(path, WatchFor(root, game, arenaGame, server.Id).ToJson());
        string mutexName = NewMutexName();
        HelperHost host = area.Host(area.Runner(root), root, mutexName, new List<string>());

        Task<int> run = Task.Run(() => host.Execute(Command("watch", "--file", path)));
        WaitUntil(() => LogOf(root).Contains("watching the Arena game (pid " + arenaGame.Id + ")"), "the watch command did not announce the game");
        using (new MutexOwner(mutexName))
        {
            // Another helper (a leave) can take the mutex while the watch waits for the game.
        }

        Assert.False(run.IsCompleted);
        arenaGame.Kill();
        Assert.True(run.Wait(TimeSpan.FromSeconds(20)), "the watch command did not finish after the game ended");
        Assert.Equal(0, run.Result);
        Assert.True(Exited(server), "the arena server outlived the game");
        Assert.Contains("arena server after the game: Stopped", LogOf(root));

        // The game is already gone when the watch starts (the pid finished long ago): the stop follows at once.
        Process second = area.StartExe(treeExe);
        area.Record(second, game.Runtime, "session", treeExe);
        var gone = new HelperWatchFile
        {
            GamePid = Area.FinishedProcessId(), GameExe = Path.GetFileName(game.Exe), GameStartUtc = DateTime.UtcNow.ToString("o"),
            ServerPid = second.Id, SptRuntimeDir = game.Runtime,
        };
        File.WriteAllText(path, gone.ToJson());
        Assert.Equal(0, area.Host(area.Runner(root), root, NewMutexName(), new List<string>()).Execute(Command("watch", "--file", path)));
        Assert.True(Exited(second));
        Assert.Contains("is already gone", LogOf(root));
    }

    /// <summary>A pid that now belongs to another program is a game that has exited, never something to wait for.</summary>
    [SkippableFact]
    public void TheWatchCommandDoesNotWaitForAReusedPid()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, game.Runtime, "session", treeExe);
        using Process other = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            string path = Path.Combine(root, HelperLayout.WatchFileName);
            var watch = new HelperWatchFile
            {
                GamePid = other.Id, GameExe = Path.GetFileName(game.Exe), GameStartUtc = other.StartTime.ToUniversalTime().ToString("o"),
                ServerPid = server.Id, SptRuntimeDir = game.Runtime,
            };
            File.WriteAllText(path, watch.ToJson());
            var clock = Stopwatch.StartNew();

            int exit = area.Host(area.Runner(root), root, NewMutexName(), new List<string>()).Execute(Command("watch", "--file", path));

            Assert.Equal(0, exit);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "the watch waited for a process that is not the game");
            Assert.False(other.HasExited);
            Assert.True(Exited(server));
            Assert.Contains("is not the Arena game any more", LogOf(root));
        }
        finally
        {
            try { other.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>The start-time rule: the recorded start must lie within 2 s of the process's; otherwise the pid was reused.</summary>
    [SkippableFact]
    public void TheWatchCommandTellsAReusedPidByItsStartTime()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, game.Runtime, "session", treeExe);
        Process arenaGame = area.StartExe(game.Exe);
        string path = Path.Combine(root, HelperLayout.WatchFileName);
        HelperWatchFile watch = WatchFor(root, game, arenaGame, server.Id);

        // Recorded an hour earlier: the same exe name, but not the process that was started then.
        watch.GameStartUtc = arenaGame.StartTime.ToUniversalTime().AddHours(-1).ToString("o");
        File.WriteAllText(path, watch.ToJson());
        var clock = Stopwatch.StartNew();
        Assert.Equal(0, area.Host(area.Runner(root), root, NewMutexName(), new List<string>()).Execute(Command("watch", "--file", path)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "the watch waited for a process whose start time is not the recorded one");
        Assert.False(arenaGame.HasExited);
        Assert.True(Exited(server));
        Assert.Contains("is not the Arena game any more (another start time)", LogOf(root));

        // Recorded one second off: still the game, so the watch waits for it.
        Process second = area.StartExe(treeExe);
        area.Record(second, game.Runtime, "session", treeExe);
        watch = WatchFor(root, game, arenaGame, second.Id);
        watch.GameStartUtc = arenaGame.StartTime.ToUniversalTime().AddSeconds(-1).ToString("o");
        File.WriteAllText(path, watch.ToJson());
        Task<int> run = Task.Run(() => area.Host(area.Runner(root), root, NewMutexName(), new List<string>()).Execute(Command("watch", "--file", path)));
        WaitUntil(() => LogOf(root).Contains("watching the Arena game (pid " + arenaGame.Id + ")"), "a start time one second off was not accepted");
        Assert.False(run.IsCompleted);
        arenaGame.Kill();
        Assert.True(run.Wait(TimeSpan.FromSeconds(20)));
        Assert.True(Exited(second));
    }

    /// <summary>A pid file that cannot be read at the moment of the stop: nothing is killed on a guess.</summary>
    [SkippableFact]
    public void TheWatchKillsNothingWhenThePidFileCannotBeRead()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        Process server = area.StartExe(treeExe);
        area.Record(server, game.Runtime, "session", treeExe);
        HelperRunner runner = area.Runner(root);
        runner.Watch = WatchFor(root, game, server, server.Id); // the game does not matter here: StopAfterGameExit is driven directly

        StopResult result;
        using (new FileStream(HelperServerTree.GetPidFilePath(root), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = runner.StopAfterGameExit();
        }

        Assert.Equal(StopResult.NothingToStop, result);
        Assert.False(server.HasExited, "the server was killed although its record could not be read");
        Assert.Contains("server.pid cannot be read right now", LogOf(root));
        Assert.Single(WarningsOf(root), line => line.Contains("server.pid cannot be read"));
    }

    /// <summary>
    /// The enter's last step as a unit: StartWatchdog writes watch.json for the game and server at hand and starts the
    /// helper's own exe as `watch --file` through the runner's process starter (the one that inherits nothing).
    /// </summary>
    [SkippableFact]
    public void TheEnterWritesTheWatchFileAndStartsTheWatchFromItsOwnExe()
    {
        SkipUnlessWindows();
        using var area = new Area();
        var game = area.Game();
        string root = area.At("session");
        string treeExe = area.TreeServerExe(root);
        area.RecordMarker(root, game.Runtime, treeExe);
        Assert.True(HelperMarker.TryParse(File.ReadAllText(HelperServerTree.GetMarkerPath(root)), out HelperMarker? marker, out string? markerError), markerError);
        Process arenaGame = area.StartExe(game.Exe);
        Assert.True(HelperRequest.TryParse(Area.RequestJson("enter", game, Area.FinishedProcessId()), "enter", out HelperRequest? request, out string? parseError), parseError);
        HelperRunner runner = area.Runner(root);
        runner.StartWatchdogAfterEnter = true;
        runner.ArenaGame = Process.GetProcessById(arenaGame.Id);
        runner.StartedServerPid = 4242;
        var starts = new List<(string Exe, string Arguments, string Directory)>();
        runner.ProcessStarter = (exe, args, directory) =>
        {
            starts.Add((exe, args, directory));
            return Process.GetProcessById(area.StartExe(treeExe).Id); // stands in for the watch process
        };

        runner.StartWatchdog(request!);

        string path = Path.Combine(root, HelperLayout.WatchFileName);
        Assert.Single(starts);
        Assert.Equal(typeof(HelperRunner).Assembly.Location, starts[0].Exe);
        Assert.Equal(HelperCommandLine.BuildCommandLine(new List<string> { HelperCommand.Watch, "--file", path }), starts[0].Arguments);
        Assert.Equal(root, starts[0].Directory);
        Assert.True(HelperWatchFile.TryParse(File.ReadAllText(path), out HelperWatchFile? written, out string? error), error);
        Assert.Equal(arenaGame.Id, written!.GamePid);
        Assert.Equal("EscapeFromTarkov.exe", written.GameExe);
        Assert.Equal(arenaGame.StartTime.ToUniversalTime().ToString("o"), written.GameStartUtc);
        Assert.Equal(4242, written.ServerPid);
        Assert.Equal(marker!.CreatedUtc, written.MarkerCreatedUtc);
        Assert.Equal(game.Runtime, written.SptRuntimeDir);
        Assert.Null(runner.ArenaGame);
        Assert.Contains("watchdog started (pid ", LogOf(root));
        Assert.Contains("the arena server (pid 4242) ends with the game (pid " + arenaGame.Id + ")", LogOf(root));

        // A watch that cannot be started is a Warning, not a failed enter.
        runner.ArenaGame = Process.GetProcessById(arenaGame.Id);
        runner.ProcessStarter = (exe, args, directory) => null!;
        runner.StartWatchdog(request!);
        Assert.Single(WarningsOf(root), line => line.Contains("no watchdog for this session (the watchdog did not start)"));
    }

    [SkippableFact]
    public void TheWatchCommandRefusesAFileItCannotRead()
    {
        SkipUnlessWindows();
        using var area = new Area();
        string root = area.At("session");
        HelperHost host = area.Host(area.Runner(root), root, NewMutexName(), new List<string>());

        Assert.Equal(1, host.Execute(Command("watch", "--file", Path.Combine(root, "missing.json"))));
        File.WriteAllText(Path.Combine(root, "bad.json"), "{ \"schema\": 1 }");
        Assert.Equal(1, host.Execute(Command("watch", "--file", Path.Combine(root, "bad.json"))));

        string log = LogOf(root);
        Assert.Contains("cannot read the watch file", log);
        Assert.Contains("the watch file needs gamePid, gameExe and sptRuntimeDir", log);
    }

    [Fact]
    public void TheWatchCommandAndItsFileParse()
    {
        Assert.False(HelperCommand.TryParse(new[] { "watch" }, out _, out string? error));
        Assert.Equal("'watch' needs --file <file>", error);
        Assert.True(HelperCommand.TryParse(new[] { "watch", "--file", "C:\\x\\watch.json" }, out HelperCommand? command, out error), error);
        Assert.Equal(HelperCommand.Watch, command!.Name);
        Assert.Equal("C:\\x\\watch.json", command.RequestPath);
        Assert.False(command.NeedsRequest);
        Assert.True(HelperCommand.TryParse(new[] { "watch", "--file=w.json" }, out command, out _));
        Assert.Equal("w.json", command!.RequestPath);
        Assert.False(HelperCommand.TryParse(new[] { "watch", "--file" }, out _, out error));
        Assert.Equal("--file needs a file name", error);
        Assert.False(HelperCommand.TryParse(new[] { "enter", "--file", "x" }, out _, out error));
        Assert.Contains("unexpected argument '--file'", error);

        var file = new HelperWatchFile { GamePid = 4242, GameExe = "EscapeFromTarkov.exe", GameStartUtc = "2026-10-23T12:00:00.0000000Z", ServerPid = 77, MarkerCreatedUtc = "2026-10-23T12:00:00Z", SptRuntimeDir = @"C:\SPT41\SPT_Runtime" };
        Assert.True(HelperWatchFile.TryParse(file.ToJson(), out HelperWatchFile? parsed, out error), error);
        Assert.Equal((4242, "EscapeFromTarkov.exe", "2026-10-23T12:00:00.0000000Z", 77, "2026-10-23T12:00:00Z", @"C:\SPT41\SPT_Runtime"),
            (parsed!.GamePid, parsed.GameExe, parsed.GameStartUtc, parsed.ServerPid, parsed.MarkerCreatedUtc, parsed.SptRuntimeDir));
        Assert.False(HelperWatchFile.TryParse("{ \"schema\": 1, \"gameExe\": \"x\" }", out _, out error));
        Assert.Equal("the watch file needs gamePid, gameExe and sptRuntimeDir", error);
        Assert.False(HelperWatchFile.TryParse("[]", out _, out error));
        Assert.Equal(180, new HelperHost(new HelperRunner(Path.GetTempPath(), new HelperLog(Path.Combine(Path.GetTempPath(), "arena-watch-pin.log")), "t"), new HelperLog(Path.Combine(Path.GetTempPath(), "arena-watch-pin.log")), NewMutexName(), _ => { }).WatchMutexWaitSeconds);
        Assert.True(new HelperRunner(Path.GetTempPath(), new HelperLog(Path.Combine(Path.GetTempPath(), "arena-watch-pin.log")), "t").StartWatchdogAfterEnter);
    }
}
