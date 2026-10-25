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
    /// The commands of the helper (docs/ARENA-SESSION-20261021.md, section 6): enter, leave, stop-server, status
    /// and the watch that an enter starts as a second process. Each returns true on success and logs what it
    /// does; it throws nothing the caller has to understand, expected failures are
    /// <see cref="HelperException"/>s that are caught and logged here.
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

        // The Arena game of this watchdog run (the watch command opened it by pid, or a test harness set it): what
        // WaitForArenaGameExit waits for. Null when the game is already gone.
        public Process ArenaGame { get; internal set; }

        // What the enter wrote into watch.json and the watch command read back (section 6, step 7).
        public HelperWatchFile Watch { get; internal set; }

        // False in a test harness: the watchdog is a second process of the helper's own exe, which a test does not
        // have (the runner is compiled into the test assembly). Program keeps the default.
        public bool StartWatchdogAfterEnter = true;

        // The pid of the arena server this run started (StartArenaServer); -1 before. Settable for a test harness
        // that drives StartWatchdog without a server start.
        public int StartedServerPid { get; internal set; } = -1;

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

                if (tree.BackupDirectoryRewrites.Count > 0)
                {
                    _log.Warn("backup config: " + tree.BackupDirectoryRewrites.Count + " backup directory value(s) did not lie below user (or were missing) and were"
                        + " set to the private folder in the tree's copy, so that the arena server's profile backups stay out of the normal server's backup pool"
                        + " (its cleanup deletes there): " + string.Join("; ", tree.BackupDirectoryRewrites.ToArray()));
                }

                // Info, not Warning: SPT ships both flags off, so most installations get this at every enter.
                if (tree.ProfileCleanupChanges.Count > 0)
                {
                    _log.Info("core config: SPT's profile clean-up switched on in the tree's copy, so that items, clothing and trader records of the"
                        + " mods the session leaves out are deleted from the disposable profile copy instead of making it unloadable: "
                        + string.Join("; ", tree.ProfileCleanupChanges.ToArray()));
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

                StartGame(request, arguments, true);
                StartWatchdog(request);
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

        // ------------------------------------------------------------------ after the Arena game

        /// <summary>
        /// Waits for the Arena game to exit, however long the session lasts (EXIT, a crash and a kill all end the
        /// process). No exit code: the watch opened the game by pid, and such a process object yields none on .NET
        /// Framework. A wait that fails (the process cannot be asked) counts as exited: the stop that follows is
        /// bound to this session's server and harmless when the game still runs.
        /// </summary>
        public void WaitForArenaGameExit()
        {
            Process game = ArenaGame;
            if (game == null) return;
            try
            {
                game.WaitForExit();
            }
            catch (Exception ex)
            {
                _log.Warn("the wait for the Arena game failed (" + ex.Message + "); treating it as exited");
            }
        }

        /// <summary>
        /// The enter's last step: writes <c>watch.json</c> (which game, which server, which installation) into the session
        /// root and starts the helper's own exe as <c>watch --file</c>. A second process because this one is a child of
        /// the old game and holds copies of whatever handles that game had made inheritable (sockets, pipes); it ends
        /// moments from now as it always did, and the watchdog, started through <see cref="HelperProcesses.Start"/>,
        /// inherits nothing. A watchdog that cannot be started is a Warning: the session runs as before 2026-10-23
        /// (the arena server lingers until the next enter, leave or stop-server).
        /// </summary>
        internal void StartWatchdog(HelperRequest request)
        {
            Process game = ArenaGame;
            try
            {
                if (game == null) return;
                DateTime startUtc;
                HelperMarker marker = ReadMarker();
                var watch = new HelperWatchFile
                {
                    GamePid = game.Id,
                    GameExe = Path.GetFileName(request.GameExe),
                    GameStartUtc = HelperProcesses.TryGetStartTimeUtc(game, out startUtc) ? startUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture) : null,
                    ServerPid = StartedServerPid,
                    MarkerCreatedUtc = marker != null ? marker.CreatedUtc : null,
                    SptRuntimeDir = request.SptRuntimeDir,
                };
                Watch = watch;
                if (!StartWatchdogAfterEnter) return;

                string path = Path.Combine(_runtimeRoot, HelperLayout.WatchFileName);
                var box = new HelperSandbox(_runtimeRoot, Info) { RetryAttempts = FileRetryAttempts, RetryDelayMilliseconds = FileRetryDelayMilliseconds };
                box.WriteAllText(path, watch.ToJson());
                string exe = typeof(HelperRunner).Assembly.Location;
                using (Process watchdog = StartProcess(exe, HelperCommandLine.BuildCommandLine(new List<string> { HelperCommand.Watch, "--file", path }), _runtimeRoot))
                {
                    if (watchdog == null) throw new HelperException("the watchdog did not start");
                    _log.Info("watchdog started (pid " + watchdog.Id + "): the arena server (pid " + watch.ServerPid + ") ends with the game (pid " + watch.GamePid + ")");
                }
            }
            catch (Exception ex)
            {
                _log.Warn("no watchdog for this session (" + ex.Message + "): the arena server will linger after the game until the next enter, leave or stop-server");
            }
            finally
            {
                // The enter helper ends now; the watchdog holds its own handle to the game.
                if (game != null) game.Dispose();
                ArenaGame = null;
            }
        }

        /// <summary>
        /// The watch command's first step: reads <c>watch.json</c> and opens the game it names. A pid that is gone, or
        /// that now belongs to another image or to a process started at another time (the number was reused), counts
        /// as a game that has already exited: <see cref="ArenaGame"/> stays null and the stop follows at once. False
        /// (with the reason logged) when the file cannot be read: nothing is stopped on a guess.
        /// </summary>
        public bool LoadWatch(string path)
        {
            HelperWatchFile watch;
            string error;
            try
            {
                if (!HelperWatchFile.TryParse(HelperFileSystem.ReadAllTextShared(path), out watch, out error))
                {
                    _log.Error("the watch file '" + path + "' is not usable: " + error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Error("cannot read the watch file '" + path + "': " + ex.Message);
                return false;
            }

            Watch = watch;
            Process game = HelperProcesses.TryOpen(watch.GamePid);
            if (game == null)
            {
                _log.Info("the Arena game (pid " + watch.GamePid + ") is already gone");
                return true;
            }

            string image = HelperProcesses.GetImagePath(game);
            DateTime startUtc, expectedUtc;
            bool sameImage = image == null || string.Equals(Path.GetFileName(image), watch.GameExe, StringComparison.OrdinalIgnoreCase);
            bool sameStart = watch.GameStartUtc == null || !HelperProcesses.TryGetStartTimeUtc(game, out startUtc)
                || !DateTime.TryParse(watch.GameStartUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out expectedUtc)
                || Math.Abs((startUtc.ToUniversalTime() - expectedUtc.ToUniversalTime()).TotalSeconds) <= 2.0;
            if (!sameImage || !sameStart)
            {
                _log.Info("pid " + watch.GamePid + " is not the Arena game any more (" + (sameImage ? "another start time" : image) + "): it has exited");
                game.Dispose();
                return true;
            }

            ArenaGame = game;
            return true;
        }

        /// <summary>
        /// The second half of the watchdog: the arena server of the installation the enter was for is stopped with the
        /// rules of enter and leave (<see cref="StopArenaServer"/>: a server of another installation is left alone,
        /// Unverified and StillRunning are logged). Bound to its own session: when <c>server.pid</c> names another process
        /// than the one this enter started, or the marker is a newer session's, a later enter owns the record and
        /// nothing is touched (a watchdog whose game lingered may wake up after the next session began). After a
        /// leave there is nothing left to stop.
        /// </summary>
        public StopResult StopAfterGameExit()
        {
            HelperWatchFile watch = Watch;
            if (watch != null)
            {
                // The marker first: a newer enter rewrote it (and the pid file) for its own session.
                HelperMarker marker = ReadMarker();
                if (marker != null && !string.IsNullOrEmpty(watch.MarkerCreatedUtc) && !string.Equals(marker.CreatedUtc, watch.MarkerCreatedUtc, StringComparison.Ordinal))
                {
                    _log.Info("the marker in place is a newer session's (" + marker.CreatedUtc + ", this session: " + watch.MarkerCreatedUtc + "): nothing of this session is left to stop");
                    return StopResult.NothingToStop;
                }

                int recorded;
                switch (ReadRecordedPid(out recorded))
                {
                    case PidFileState.Present:
                        if (recorded != watch.ServerPid)
                        {
                            _log.Info("server.pid names pid " + recorded + ", not this session's server (pid " + watch.ServerPid + "): a newer session owns the record, nothing to stop here");
                            return StopResult.NothingToStop;
                        }
                        break;
                    case PidFileState.Unreadable:
                        // Nothing is killed on a guess: whatever holds the file (a scanner, an editor) lets go, and the
                        // next enter, leave or stop-server finds the record.
                        _log.Warn("server.pid cannot be read right now; the arena server is left to the next enter, leave or stop-server");
                        return StopResult.NothingToStop;
                }
            }

            StopResult result = StopArenaServer(watch != null ? watch.SptRuntimeDir : null);
            _log.Info("arena server after the game: " + result);
            return result;
        }

        private enum PidFileState { Absent, Present, Unreadable }

        /// <summary><c>server.pid</c>: absent, present with its pid, or there but not readable (locked, or not a pid).</summary>
        private PidFileState ReadRecordedPid(out int pid)
        {
            pid = 0;
            string path = HelperServerTree.GetPidFilePath(_runtimeRoot);
            try
            {
                if (!File.Exists(path)) return PidFileState.Absent;
                return HelperPidFile.TryParsePid(HelperFileSystem.ReadAllTextShared(path), out pid) ? PidFileState.Present : PidFileState.Unreadable;
            }
            catch (Exception)
            {
                return PidFileState.Unreadable;
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
                StartedServerPid = server.Id;
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
            StartGame(request, arguments, false);
        }

        // keepForWatch: the Arena game of an enter stays open in ArenaGame for the watchdog; the normal game of a
        // leave is let go.
        private void StartGame(HelperRequest request, List<string> arguments, bool keepForWatch)
        {
            if (!File.Exists(request.GameExe)) throw new HelperException("the game is missing: " + request.GameExe);
            string commandLine = HelperCommandLine.BuildCommandLine(arguments);
            _log.Info("starting the game " + request.GameExe + " in " + request.GameDir + ": " + HelperCommandLine.RedactForLog(arguments));
            Process game = StartProcess(request.GameExe, commandLine, request.GameDir);
            try
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

                if (keepForWatch)
                {
                    ArenaGame = game;
                    game = null;
                }
            }
            finally
            {
                if (game != null) game.Dispose();
            }
        }
    }
}
