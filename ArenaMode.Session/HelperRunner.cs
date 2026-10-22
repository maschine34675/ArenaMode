// Also compiled into ArenaMode.Tests (net10.0, nullable annotations enabled; see
// ArenaSessionHelperProcessTests). This file carries none on purpose (C# 7.3 like SessionLogic.cs), hence the pragma.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    /// <summary>What <see cref="HelperRunner.StopArenaServer"/> found and did.</summary>
    internal enum StopResult
    {
        /// <summary>No pid file, or the process it names had already gone.</summary>
        NothingToStop,

        /// <summary>The arena server was running and has been killed.</summary>
        Stopped,

        /// <summary>The pid names a process that is not the server the helper started (the pid was reused).</summary>
        NotOurs,

        /// <summary>A process runs under the recorded pid but its identity could not be established; it was left alone.</summary>
        Unverified,

        /// <summary>The arena server was killed but did not exit in time.</summary>
        StillRunning,

        /// <summary>
        /// The recorded arena server is alive and belongs to another SPT installation than the caller's.
        /// The session folder is shared by every installation of this Windows user, so it was left alone.
        /// </summary>
        OtherInstallation,
    }

    /// <summary>
    /// The four commands of the helper (docs/ARENA-SESSION-20261021.md, section 6). Each returns true
    /// on success and logs what it does; it throws nothing the caller has to understand, expected
    /// failures are <see cref="HelperException"/>s that are caught and logged here.
    /// </summary>
    internal sealed class HelperRunner
    {
        private readonly string _runtimeRoot;
        private readonly HelperLog _log;
        private readonly string _version;

        // Public so a test harness can shorten the waits; the specification's values are the defaults.
        public int GameExitTimeoutSeconds = HelperLayout.GameExitTimeoutSeconds;
        public int ServerStartTimeoutSeconds = HelperLayout.ServerStartTimeoutSeconds;
        public int ServerSettleMilliseconds = 2000;
        public int GameSettleMilliseconds = 1500;
        public int GameStartCheckMilliseconds = 2000;

        // How hard the writes of the helper's own bookkeeping (server.pid) are retried before they count as failed.
        public int FileRetryAttempts = 6;
        public int FileRetryDelayMilliseconds = 200;

        // How the helper starts the servers and the game (executable, arguments, working directory). The default is
        // HelperProcesses.Start; a test harness substitutes a stand-in that stays alive without a real server.
        public Func<string, string, string, Process> ProcessStarter = HelperProcesses.Start;

        // How long the helper waits for a server process it has killed (process, milliseconds; true = it exited).
        // The default is HelperProcesses.WaitForExit; a test harness substitutes one that reports "still running".
        public Func<Process, int, bool> ServerExitWaiter = HelperProcesses.WaitForExit;

        // Who listens on a port for connections to an address (port, the address the connection reached): the ids of the
        // listening processes, an empty list when the table names none, null when the system's TCP table cannot be read.
        // The default is HelperProcesses.GetListenerProcessIds; a test harness substitutes one that cannot read the table.
        public Func<int, IPAddress, List<int>> ListenerLookup = HelperProcesses.GetListenerProcessIds;

        // The helper's own probe of a port it is about to start a server on (host the server will be waited for on, port;
        // true = nothing accepts a connection there and the port can be bound). The default is HelperProcesses.IsPortFree.
        public Func<string, int, bool> PortFreeProbe = HelperProcesses.IsPortFree;

        // True while the request file the helper acted on is still there unchanged. HelperHost sets it for the
        // duration of one run; null means nobody watches (a test that drives the runner directly). It is consulted
        // while the helper waits for the game to exit: a client that gave up has taken the request back, and a
        // helper that acknowledged late must not switch the session when the player closes the game by hand.
        public Func<bool> RequestStillStands;

        // True once a game process exists; a recovery must never start another one.
        private bool _gameStarted;

        public HelperRunner(string runtimeRoot, HelperLog log, string version)
        {
            _runtimeRoot = runtimeRoot;
            _log = log;
            _version = version;
        }

        // ------------------------------------------------------------------ enter

        public bool Enter(HelperRequest request)
        {
            _log.Info("enter: " + request.ServerMods.Count + " server mods, policy " + (request.PolicyHash ?? "(none)"));
            if (!AwaitGame(request)) return false;

            _gameStarted = false;
            try
            {
                HelperHttpEndpoint normal = ReadNormalEndpoint(request);

                StopResult stopped = StopArenaServer(request.SptRuntimeDir);
                if (stopped == StopResult.OtherInstallation)
                {
                    // Its tree is the one Build would delete next and its port the one it would reuse.
                    throw new HelperException("the arena server of another SPT installation is running, so this installation cannot start its own "
                        + "(the session folder is shared by all installations of this Windows user); close that game or run stop-server");
                }

                if (stopped == StopResult.Unverified || stopped == StopResult.StillRunning)
                {
                    throw new HelperException("the previous arena server could not be stopped (" + stopped + ")");
                }

                int port = HelperPorts.PickArenaPort(candidate => ProbePort(normal.ServerHost, candidate), normal.ServerPort);
                if (port < 0) throw new HelperException("no free port between " + ArenaSessionContract.ArenaPort + " and " + ArenaSessionContract.FallbackPortLast);
                if (port != ArenaSessionContract.ArenaPort) _log.Warn("port " + ArenaSessionContract.ArenaPort + " is taken, the arena server uses " + port);

                HelperTreeResult tree = HelperServerTree.Build(request, port, _runtimeRoot, _version, DateTime.UtcNow, Info);
                _log.Info("server tree ready at " + tree.TreePath + ": " + tree.Junctions.Count + " junctions, " + tree.Mods.Count + " server mods, "
                    + "server prepatch folders (user\\patchers): " + tree.Patchers.Count + (tree.Patchers.Count == 0 ? string.Empty : " (" + string.Join(", ", tree.Patchers.ToArray()) + ")")
                    + ", " + tree.FilesCopied + " files copied, server binaries " + tree.ServerFilesCopied + " copied and " + tree.ServerFilesUnchanged + " unchanged");
                if (tree.LogPathRewrites.Count > 0)
                {
                    _log.Warn("sptLogger.json: " + tree.LogPathRewrites.Count + " logger path(s) did not lie below user and were moved below user\\" + HelperLayout.LogsFolder
                        + " in the tree's copy, so that the arena server does not log into the real runtime: " + string.Join("; ", tree.LogPathRewrites.ToArray()));
                }

                if (tree.UnsharedFolders.Count > 0)
                {
                    _log.Warn("the runtime has folders the arena tree does not contain (" + string.Join(", ", tree.UnsharedFolders.ToArray())
                        + "); the server, which runs from the copy of its exe, will not find files in them");
                }

                StartArenaServer(tree);

                List<string> arguments;
                string error;
                if (!HelperCommandLine.TryRewriteGameArguments(request.GameArgs, true, port, out arguments, out error))
                {
                    throw new HelperException("cannot build the game arguments: " + error);
                }

                StartGame(request, arguments);
                _log.Info("enter finished");
                return true;
            }
            catch (Exception ex)
            {
                LogFailure("enter failed", ex);

                // The old game is gone. Unless the Arena game was started, bring the normal session
                // back instead of leaving the player with no game at all. Never start a second game.
                if (!_gameStarted) RecoverNormalSession(request);
                return false;
            }
        }

        // ------------------------------------------------------------------ leave

        public bool Leave(HelperRequest request)
        {
            _log.Info("leave: returning to the normal session");
            if (!AwaitGame(request)) return false;

            try
            {
                StartNormalSession(request);
                _log.Info("leave finished");
                return true;
            }
            catch (Exception ex)
            {
                LogFailure("leave failed", ex);
                return false;
            }
        }

        // The steps shared by leave and by the recovery after a failed enter.
        private void StartNormalSession(HelperRequest request)
        {
            // Starting the game must not depend on cleaning up the arena server: a pid file that cannot be
            // read or deleted for a moment (a scanner, an editor) would otherwise cost the player the
            // game on leave, and on the recovery after a failed enter as well.
            try
            {
                StopResult stopped = StopArenaServer(request.SptRuntimeDir);
                if (stopped == StopResult.Unverified || stopped == StopResult.StillRunning || stopped == StopResult.OtherInstallation)
                {
                    _log.Warn("the arena server was not stopped (" + stopped + "); continuing, it listens on its own port");
                }
            }
            catch (Exception ex)
            {
                _log.Warn("the arena server could not be stopped (" + ex.Message + "); continuing, it listens on its own port");
            }

            HelperHttpEndpoint normal = ReadNormalEndpoint(request);
            if (HelperProcesses.CanConnect(normal.ServerHost, normal.ServerPort, 1000))
            {
                _log.Info("the normal server already listens on " + normal.ServerHost + ":" + normal.ServerPort);
            }
            else
            {
                string serverExe = Path.Combine(request.SptRuntimeDir, HelperLayout.ServerExeName);
                if (!File.Exists(serverExe)) throw new HelperException("the SPT server is missing: " + serverExe);
                _log.Info("starting the normal server " + serverExe + " in " + request.SptRuntimeDir);
                using (Process server = StartProcess(serverExe, string.Empty, request.SptRuntimeDir))
                {
                    if (server == null) throw new HelperException("the normal server did not start");

                    // The connect probe above, a moment ago, is the helper's own proof that nothing held this port.
                    WaitForServer(normal.ServerHost, normal.ServerPort, server, "the normal server", true);
                }
            }

            List<string> arguments;
            string error;
            if (!HelperCommandLine.TryRewriteGameArguments(request.GameArgs, false, normal.BackendPort, out arguments, out error))
            {
                throw new HelperException("cannot build the game arguments: " + error);
            }

            StartGame(request, arguments);
        }

        // A failed enter happens after the old game has exited. Starting the normal session again leaves
        // the user with a working game instead of nothing (the specification does not ask for this).
        private void RecoverNormalSession(HelperRequest request)
        {
            try
            {
                _log.Warn("recovering: starting the normal session again");
                StartNormalSession(request);
                _log.Info("recovery finished: the normal session was started");
            }
            catch (Exception ex)
            {
                LogFailure("recovery failed", ex);
            }
        }

        // ------------------------------------------------------------------ stop-server

        /// <summary>
        /// Kills the arena server the helper recorded in <c>server.pid</c>, but only after the process
        /// image is shown to be the exe the marker says the server was started from and the process
        /// start time agrees with the pid file. Anything else is left alone. That exe is the copy
        /// <c>&lt;tree&gt;\SPT.Server.exe</c> for a marker of this helper, and <c>SPT.Server.exe</c> of the
        /// marker's <c>sourceRuntime</c> for a marker without <c>serverExe</c> (a server started by an
        /// older helper, which ran the installation's own exe): such a session is still stopped once.
        /// A marker of this helper does not accept the installation's exe, which is also the image of the
        /// normal server. The image is compared as a file, not as text: the process reports the
        /// resolved path, the tree and the marker keep the spelling they were made from, and a junction
        /// or SUBST drive makes the two differ.
        /// </summary>
        /// <param name="callerRuntime">The runtime of the installation that asks. The session folder, its
        /// mutex and its pid file are shared by every installation of the Windows user, so a server that
        /// belongs to another installation is not stopped (<see cref="StopResult.OtherInstallation"/>).
        /// Null for the <c>stop-server</c> command, which stops whichever server was recorded.</param>
        public StopResult StopArenaServer(string callerRuntime)
        {
            StopResult registered = StopRegisteredServer(callerRuntime);
            if (registered != StopResult.NothingToStop && registered != StopResult.NotOurs) return registered;

            // No usable record of a running server. A helper that was killed between the start of the server and the
            // write of server.pid leaves one that no record names; it holds the tree open and its port.
            StopResult unregistered = StopUnregisteredServers(callerRuntime);
            return unregistered == StopResult.NothingToStop ? registered : unregistered;
        }

        private StopResult StopRegisteredServer(string callerRuntime)
        {
            string pidPath = HelperServerTree.GetPidFilePath(_runtimeRoot);
            if (!File.Exists(pidPath)) return StopResult.NothingToStop;

            var box = new HelperSandbox(_runtimeRoot, Info);
            int pid;
            if (!HelperPidFile.TryParsePid(HelperFileSystem.ReadAllTextShared(pidPath), out pid))
            {
                _log.Warn("server.pid is unreadable, removing it");
                box.DeleteFile(pidPath);
                return StopResult.NothingToStop;
            }

            using (Process process = HelperProcesses.TryOpen(pid))
            {
                if (process == null)
                {
                    _log.Info("the arena server (pid " + pid + ") is not running any more");
                    box.DeleteFile(pidPath);
                    return StopResult.NothingToStop;
                }

                HelperMarker marker = ReadMarker();
                if (marker == null || string.IsNullOrEmpty(marker.SourceRuntime))
                {
                    _log.Warn("pid " + pid + " is running but the arena marker is missing, so its origin cannot be verified; leaving it alone");
                    return StopResult.Unverified;
                }

                // The tree copy is recomputed from this helper's own session root; the marker only says that the
                // server was started from it. A kill decision does not rest on a path read from a file.
                string expected = HelperServerIdentity.ExpectedImage(marker, Path.Combine(HelperServerTree.GetTreePath(_runtimeRoot), HelperLayout.ServerExeName));
                if (!marker.RunsFromTree)
                {
                    _log.Info("the marker is from an older helper (" + (marker.HelperVersion ?? "unknown") + "): its server ran the installation's own "
                        + HelperLayout.ServerExeName);
                }

                string image = HelperProcesses.GetImagePath(process);
                if (image == null)
                {
                    _log.Warn("the image of pid " + pid + " cannot be read; leaving it alone");
                    return StopResult.Unverified;
                }

                if (!HelperProcesses.SameFileSystemObject(image, expected))
                {
                    _log.Info("pid " + pid + " is " + image + ", not " + expected + ": the pid was reused, not stopping it");
                    box.DeleteFile(pidPath);
                    return StopResult.NotOurs;
                }

                DateTime started;
                if (!HelperProcesses.TryGetStartTimeUtc(process, out started))
                {
                    _log.Warn("the start time of pid " + pid + " cannot be read; leaving it alone");
                    return StopResult.Unverified;
                }

                DateTime written = File.GetLastWriteTimeUtc(pidPath);
                if (!HelperPidFile.ProcessMatchesPidFile(written, started))
                {
                    _log.Info("pid " + pid + " started at " + started.ToString("o") + " but server.pid was written at " + written.ToString("o")
                        + ": it is not the server the helper recorded, not stopping it");
                    box.DeleteFile(pidPath);
                    return StopResult.NotOurs;
                }

                if (callerRuntime != null && !HelperProcesses.SameFileSystemObject(marker.SourceRuntime, callerRuntime))
                {
                    _log.Warn("the arena server (pid " + pid + ") belongs to the SPT installation at " + marker.SourceRuntime
                        + ", not to " + callerRuntime + "; leaving it alone");
                    return StopResult.OtherInstallation;
                }

                _log.Info("stopping the arena server (pid " + pid + ", " + image + ")");
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    _log.Warn("Kill failed: " + ex.Message);
                }

                if (!ServerExitWaiter(process, 15000))
                {
                    _log.Error("the arena server (pid " + pid + ") is still running");
                    return StopResult.StillRunning;
                }
            }

            box.DeleteFile(pidPath);
            return StopResult.Stopped;
        }

        /// <summary>
        /// Ends arena servers that run from the tree's own <c>SPT.Server.exe</c> although no <c>server.pid</c> names them
        /// (the helper that started one was killed before it wrote the file, or the file was lost). The tree copy is
        /// started by a helper and by nothing else, so the image identifies them, compared as a file like everywhere
        /// else. The ownership rule of the registered server holds here too: the marker must name the caller's own
        /// installation, and without a marker the origin cannot be verified, so the process is left alone
        /// (<see cref="StopResult.Unverified"/>). Without such a process the result is <see cref="StopResult.NothingToStop"/>.
        /// </summary>
        private StopResult StopUnregisteredServers(string callerRuntime)
        {
            string expected = Path.Combine(HelperServerTree.GetTreePath(_runtimeRoot), HelperLayout.ServerExeName);
            if (!File.Exists(expected)) return StopResult.NothingToStop;

            List<Process> strays = HelperProcesses.FindByImage(expected);
            try
            {
                if (strays.Count == 0) return StopResult.NothingToStop;

                var ids = new List<string>();
                foreach (Process stray in strays) ids.Add("pid " + stray.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string named = string.Join(", ", ids.ToArray());
                _log.Warn("an arena server runs from " + expected + " (" + named + ") although no server.pid names it: an interrupted start");

                HelperMarker marker = ReadMarker();
                if (marker == null || string.IsNullOrEmpty(marker.SourceRuntime))
                {
                    _log.Warn("the arena marker is missing, so the origin of " + named + " cannot be verified; leaving it alone (end it by hand)");
                    return StopResult.Unverified;
                }

                if (!marker.RunsFromTree)
                {
                    // The marker of an older helper, whose server ran the installation's own exe: a process from the tree
                    // copy is not one that helper started, and what it is, is not for this rule to decide.
                    _log.Info("the marker is from an older helper (" + (marker.HelperVersion ?? "unknown") + "), whose server did not run from the tree; leaving "
                        + named + " alone");
                    return StopResult.NothingToStop;
                }

                if (callerRuntime != null && !HelperProcesses.SameFileSystemObject(marker.SourceRuntime, callerRuntime))
                {
                    _log.Warn("the arena server (" + named + ") belongs to the SPT installation at " + marker.SourceRuntime
                        + ", not to " + callerRuntime + "; leaving it alone");
                    return StopResult.OtherInstallation;
                }

                foreach (Process stray in strays)
                {
                    _log.Info("stopping the unregistered arena server (pid " + stray.Id + ")");
                    try
                    {
                        stray.Kill();
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("Kill failed: " + ex.Message);
                    }
                }

                foreach (Process stray in strays)
                {
                    if (!ServerExitWaiter(stray, 15000))
                    {
                        _log.Error("the unregistered arena server (pid " + stray.Id + ") is still running; end it by hand");
                        return StopResult.StillRunning;
                    }
                }

                return StopResult.Stopped;
            }
            finally
            {
                foreach (Process stray in strays) stray.Dispose();
            }
        }

        public bool StopServerCommand()
        {
            try
            {
                StopResult result = StopArenaServer(null);
                _log.Info("stop-server: " + result);
                return result != StopResult.StillRunning && result != StopResult.Unverified;
            }
            catch (Exception ex)
            {
                LogFailure("stop-server failed", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ status

        /// <summary>One JSON line: the tree, the marker, the recorded pid and the port state.</summary>
        public string Status()
        {
            var report = new Dictionary<string, object>();
            report["schema"] = ArenaSessionContract.SchemaVersion;
            report["helperVersion"] = _version;
            report["runtimeRoot"] = _runtimeRoot;

            string tree = HelperServerTree.GetTreePath(_runtimeRoot);
            var treeInfo = new Dictionary<string, object>();
            treeInfo["path"] = tree;
            treeInfo["exists"] = Directory.Exists(tree);
            treeInfo["reparsePoint"] = HelperFileSystem.IsReparsePoint(tree);
            report["tree"] = treeInfo;

            HelperMarker marker = ReadMarker();
            if (marker == null)
            {
                report["marker"] = null;
            }
            else
            {
                var markerInfo = new Dictionary<string, object>();
                markerInfo["createdUtc"] = marker.CreatedUtc;
                markerInfo["profileId"] = marker.ProfileId;
                markerInfo["port"] = marker.Port;
                markerInfo["sourceRuntime"] = marker.SourceRuntime;
                markerInfo["serverExe"] = marker.ServerExe;
                markerInfo["policyHash"] = marker.PolicyHash;
                markerInfo["helperVersion"] = marker.HelperVersion;
                report["marker"] = markerInfo;
            }

            var pidInfo = new Dictionary<string, object>();
            string pidPath = HelperServerTree.GetPidFilePath(_runtimeRoot);
            int pid = 0;
            bool hasPid = false;
            try
            {
                hasPid = File.Exists(pidPath) && HelperPidFile.TryParsePid(HelperFileSystem.ReadAllTextShared(pidPath), out pid);
            }
            catch (Exception)
            {
                hasPid = false;
            }

            pidInfo["pid"] = hasPid ? (object)pid : null;
            bool running = false;
            string image = null;
            if (hasPid)
            {
                using (Process process = HelperProcesses.TryOpen(pid))
                {
                    running = process != null;
                    if (process != null) image = HelperProcesses.GetImagePath(process);
                }
            }

            pidInfo["running"] = running;
            pidInfo["image"] = image;
            report["serverPid"] = pidInfo;

            var portInfo = new Dictionary<string, object>();
            portInfo["port"] = marker == null ? null : (object)marker.Port;
            portInfo["listening"] = marker != null && marker.Port > 0 && HelperProcesses.CanConnect("127.0.0.1", marker.Port, 500);
            report["portState"] = portInfo;

            return ArenaMiniJson.Write(report);
        }

        // ------------------------------------------------------------------ pieces

        private void Info(string message)
        {
            _log.Info(message);
        }

        private void LogFailure(string what, Exception ex)
        {
            if (ex is HelperException) _log.Error(what + ": " + ex.Message);
            else _log.Error(what + ": " + ex);
        }

        private HelperMarker ReadMarker()
        {
            string path = HelperServerTree.GetMarkerPath(_runtimeRoot);
            try
            {
                if (!File.Exists(path)) return null;
                HelperMarker marker;
                string error;
                return HelperMarker.TryParse(HelperFileSystem.ReadAllTextShared(path), out marker, out error) ? marker : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private HelperHttpEndpoint ReadNormalEndpoint(HelperRequest request)
        {
            string path = Path.Combine(Path.Combine(Path.Combine(request.SptRuntimeDir, HelperLayout.SptDataFolder), HelperLayout.ConfigsFolder),
                HelperLayout.HttpJsonFileName);
            if (!File.Exists(path)) throw new HelperException("the normal server's " + HelperLayout.HttpJsonFileName + " is missing: " + path);
            HelperHttpEndpoint endpoint;
            string error;
            if (!HelperJsonText.TryReadEndpoint(HelperFileSystem.ReadAllTextShared(path), out endpoint, out error))
            {
                throw new HelperException(path + ": " + error);
            }

            return endpoint;
        }

        private enum GameWait
        {
            Exited,
            TimedOut,

            /// <summary>The client took the request back while the helper waited (see <see cref="RequestStillStands"/>).</summary>
            Withdrawn,
        }

        /// <summary>
        /// Waits for the old game to exit. False, with the reason logged, when the game did not exit in time or the
        /// client withdrew the request meanwhile: nothing has been changed in either case.
        /// </summary>
        private bool AwaitGame(HelperRequest request)
        {
            switch (WaitForGameExit(request))
            {
                case GameWait.Exited:
                    return true;
                case GameWait.Withdrawn:
                    _log.Warn("the request was withdrawn (its file is gone or was replaced) while the helper waited for the game (pid "
                        + request.GamePid + "); nothing was changed and the game was not touched");
                    return false;
                default:
                    _log.Error("the game (pid " + request.GamePid + ") did not exit within " + GameExitTimeoutSeconds + " s; nothing was changed and the game was not touched");
                    return false;
            }
        }

        private bool RequestWithdrawn()
        {
            Func<bool> stands = RequestStillStands;
            if (stands == null) return false;
            try
            {
                return !stands();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Waits for the old game process to exit. A pid that now belongs to a program with another
        /// file name counts as exited (the game is gone and the number was reused). Never kills.
        /// Looks at the request file in every round (about twice a second) and once more after the game is gone.
        /// </summary>
        private GameWait WaitForGameExit(HelperRequest request)
        {
            var clock = Stopwatch.StartNew();
            bool announced = false;
            string expectedName = Path.GetFileName(request.GameExe);
            while (true)
            {
                if (RequestWithdrawn()) return GameWait.Withdrawn;
                using (Process process = HelperProcesses.TryOpen(request.GamePid))
                {
                    if (process == null) break;

                    string image = HelperProcesses.GetImagePath(process);
                    if (image != null && !string.Equals(Path.GetFileName(image), expectedName, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Info("pid " + request.GamePid + " is " + image + " now, not " + expectedName + ": the game has exited");
                        break;
                    }

                    if (!announced)
                    {
                        _log.Info("waiting for the game (pid " + request.GamePid + ") to exit");
                        announced = true;
                    }

                    if (clock.Elapsed.TotalSeconds >= GameExitTimeoutSeconds) return GameWait.TimedOut;
                    HelperProcesses.WaitForExit(process, 500);
                }
            }

            _log.Info("the game has exited" + (announced ? " after " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s" : string.Empty));
            Thread.Sleep(GameSettleMilliseconds);
            return RequestWithdrawn() ? GameWait.Withdrawn : GameWait.Exited;
        }

        // The arena server runs from its own copy of the exe in the tree: AppContext.BaseDirectory, which some
        // server mods resolve their paths from, is then the disposable tree and not the installation.
        // Internal so that a test drives it against a stand-in process (ProcessStarter).
        internal void StartArenaServer(HelperTreeResult tree)
        {
            string serverExe = tree.ServerExe;
            if (!File.Exists(serverExe)) throw new HelperException("the SPT server is missing from the arena tree: " + serverExe);

            // Everything the registration needs exists before the process does, so that writing server.pid is
            // the very next thing that happens once Process.Start returns.
            var box = new HelperSandbox(_runtimeRoot, Info) { RetryAttempts = FileRetryAttempts, RetryDelayMilliseconds = FileRetryDelayMilliseconds };
            string pidPath = HelperServerTree.GetPidFilePath(_runtimeRoot);

            // The port was probed when it was chosen, before the tree was built, which takes seconds. The probe repeated
            // here, just before the start, is what lets the wait below accept a listener that the system's TCP table
            // cannot name (see WaitForServer).
            string probeHost = "127.0.0.1";
            try
            {
                probeHost = ReadArenaEndpoint(tree.TreePath).ServerHost;
            }
            catch (Exception)
            {
                // The read after the start reports an unusable http.json; here the loopback address stands in.
            }

            bool portFree = ProbePort(probeHost, tree.Port);
            if (!portFree)
            {
                _log.Warn("port " + tree.Port + " is not free just before the arena server starts; the server will be accepted only if the system names it as the listener");
            }

            _log.Info("starting the arena server " + serverExe + " in " + tree.TreePath + " (port " + tree.Port + ")");
            using (Process server = StartProcess(serverExe, string.Empty, tree.TreePath))
            {
                if (server == null) throw new HelperException("the arena server did not start");
                RegisterStartedServer(server, box, pidPath);
                _log.Info("arena server pid " + server.Id);

                HelperHttpEndpoint arena = ReadArenaEndpoint(tree.TreePath);
                WaitForServer(arena.ServerHost, tree.Port, server, "the arena server", portFree);
            }
        }

        private bool ProbePort(string host, int port)
        {
            try
            {
                return PortFreeProbe(host, port);
            }
            catch (Exception)
            {
                return false; // a probe that fails proves nothing
            }
        }

        // server.pid is the only record that lets stop-server, leave and the next enter find this process. A server
        // whose pid could not be recorded would run on, unknown to every later cleanup, and the next enter would
        // clean the tree it runs from. So the failed registration ends the server right here, with the handle that is
        // at hand (disposing the handle would not), and fails the enter with a line that says what happened; the
        // recovery then brings the normal session back. Once the pid is written, a later failure (the port never
        // opens, say) leaves the server registered, and the recovery's stop-server ends it.
        private void RegisterStartedServer(Process server, HelperSandbox box, string pidPath)
        {
            int pid = server.Id;
            try
            {
                box.WriteAllText(pidPath, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                string outcome = KillUnregisteredServer(server, pid);
                try
                {
                    box.DeleteFile(pidPath);
                }
                catch (Exception)
                {
                    // Whatever half-written content remains is rejected as unreadable by the next stop-server.
                }

                throw new HelperException("the arena server (pid " + pid + ") started but server.pid could not be written (" + ex.Message + "); " + outcome, ex);
            }
        }

        private string KillUnregisteredServer(Process server, int pid)
        {
            _log.Error("server.pid could not be written, so the arena server (pid " + pid + ") that was just started would be unknown to every cleanup; ending it");
            try
            {
                server.Kill();
            }
            catch (Exception ex)
            {
                _log.Warn("Kill failed: " + ex.Message);
            }

            if (ServerExitWaiter(server, 15000)) return "the server was ended, so no unregistered server stays behind";
            _log.Error("the unregistered arena server (pid " + pid + ") is still running; end it by hand");
            return "it could NOT be ended and keeps running unregistered: end the process with pid " + pid + " by hand";
        }

        private Process StartProcess(string executable, string arguments, string workingDirectory)
        {
            return ProcessStarter(executable, arguments, workingDirectory);
        }

        private HelperHttpEndpoint ReadArenaEndpoint(string treePath)
        {
            string path = Path.Combine(Path.Combine(Path.Combine(treePath, HelperLayout.SptDataFolder), HelperLayout.ConfigsFolder),
                HelperLayout.HttpJsonFileName);
            HelperHttpEndpoint endpoint;
            string error;
            if (!HelperJsonText.TryReadEndpoint(HelperFileSystem.ReadAllTextShared(path), out endpoint, out error))
            {
                throw new HelperException(path + ": " + error);
            }

            return endpoint;
        }

        /// <summary>
        /// Waits until the server that was started accepts connections on <paramref name="host"/>:<paramref name="port"/>
        /// (<see cref="ServerStartTimeoutSeconds"/> at most). An accepted connection alone proves nothing: a program that took
        /// the port while the server was still starting would be taken for the server, and the game would be started against
        /// it with the player's token. So the owner of the listener is looked up for the address the connection reached
        /// (<see cref="ListenerLookup"/>, not for every local address) and decides:
        /// the started process listens: ready. Other processes listen, or the table lists no listener at all (it
        /// vanished, or it is not up yet): keep waiting. The table cannot be read: ready only when
        /// <paramref name="portVerifiedFree"/> says that the helper's own probe found the port free just before the start
        /// (then nothing but the started server can have taken it), with one warning that the owner could not be verified;
        /// otherwise keep waiting. A wait that runs into the timeout names the last of these reasons.
        /// </summary>
        internal void WaitForServer(string host, int port, Process server, string what, bool portVerifiedFree)
        {
            var clock = Stopwatch.StartNew();
            _log.Info("waiting for " + what + " on " + host + ":" + port);
            string blocker = null; // why the last accepted connection was not taken for the server
            bool warnedHeld = false;
            bool warnedNone = false;
            bool warnedUnverified = false;
            while (true)
            {
                // First: a server that has exited can no longer be the one that answers, whatever else holds
                // the port by now.
                if (server != null && server.HasExited)
                {
                    throw new HelperException(what + " exited with code " + server.ExitCode + " before it opened port " + port
                        + (blocker == null ? string.Empty : " (" + blocker + ")"));
                }

                IPAddress reached;
                if (HelperProcesses.CanConnect(host, port, 500, out reached))
                {
                    List<int> owners = server == null ? null : LookUpListeners(port, reached);
                    if (owners != null && owners.Contains(server.Id))
                    {
                        _log.Info(what + " accepts connections after " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s");
                        Thread.Sleep(ServerSettleMilliseconds);
                        return;
                    }

                    if (owners == null)
                    {
                        blocker = "the owner of port " + port + " could not be verified: the system's TCP table cannot be read and the port was not verified free just before the start";
                        if (portVerifiedFree)
                        {
                            _log.Warn("the owner of port " + port + " could not be verified (the system's TCP table cannot be read); accepting the connection as "
                                + what + " because the port was verified free just before the start");
                            _log.Info(what + " accepts connections after " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s");
                            Thread.Sleep(ServerSettleMilliseconds);
                            return;
                        }

                        if (!warnedUnverified)
                        {
                            warnedUnverified = true;
                            _log.Warn(blocker + "; waiting");
                        }
                    }
                    else if (owners.Count == 0)
                    {
                        blocker = "port " + port + " accepts connections but the system's TCP table lists no listener for it";
                        if (!warnedNone)
                        {
                            warnedNone = true;
                            _log.Warn(blocker + "; waiting");
                        }
                    }
                    else
                    {
                        var ids = new List<string>();
                        foreach (int owner in owners) ids.Add("pid " + owner.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        blocker = "port " + port + " is held by " + string.Join(", ", ids.ToArray())
                            + ", not by the server that was started (pid " + server.Id + ")";
                        if (!warnedHeld)
                        {
                            warnedHeld = true;
                            _log.Warn(blocker + "; waiting");
                        }
                    }
                }

                if (clock.Elapsed.TotalSeconds >= ServerStartTimeoutSeconds)
                {
                    throw new HelperException(what + " did not open port " + port + " within " + ServerStartTimeoutSeconds + " s"
                        + (blocker == null ? string.Empty : " (" + blocker + ")"));
                }

                Thread.Sleep(500);
            }
        }

        // A lookup that throws is a table that cannot be read.
        private List<int> LookUpListeners(int port, IPAddress address)
        {
            try
            {
                return ListenerLookup(port, address);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void StartGame(HelperRequest request, List<string> arguments)
        {
            if (!File.Exists(request.GameExe)) throw new HelperException("the game is missing: " + request.GameExe);
            string commandLine = HelperCommandLine.BuildCommandLine(arguments);
            _log.Info("starting the game " + request.GameExe + " in " + request.GameDir + ": " + HelperCommandLine.RedactForLog(arguments));
            using (Process game = StartProcess(request.GameExe, commandLine, request.GameDir))
            {
                if (game == null) throw new HelperException("the game did not start");
                _gameStarted = true;
                _log.Info("game pid " + game.Id);

                // The start itself succeeded. A game that is gone two seconds later is worth a
                // warning, but never a second start: that could run two games.
                if (HelperProcesses.WaitForExit(game, GameStartCheckMilliseconds))
                {
                    _log.Warn("the game exited " + (GameStartCheckMilliseconds / 1000.0).ToString("0.#") + " s after the start with code " + game.ExitCode);
                }
            }
        }
    }
}
