// Also compiled into ArenaMode.Tests (net10.0, nullable annotations enabled; see
// ArenaSessionHelperProcessTests). This file carries none on purpose (C# 7.3 like SessionLogic.cs), hence the pragma.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using ArenaMode.Shared.Session;
#if NETFRAMEWORK
using System.Security.AccessControl;
#endif

namespace ArenaMode.SessionHelper
{
    /// <summary>
    /// What the helper does with one parsed command, in the order the specification fixes
    /// (docs/ARENA-SESSION-20261021.md, section 6). It lives apart from <c>Program.Main</c> so that the order is
    /// tested against the real classes; <c>Program</c> only builds the log and the runner and hands over.
    /// <list type="number">
    /// <item><description><c>status</c> only reads: no mutex, no request.</description></item>
    /// <item><description>For <c>enter</c> and <c>leave</c>: the acknowledgement of an earlier request of the same
    /// file name is deleted FIRST, before the request is read, whatever happens next, so that no failure path can
    /// leave a stale one behind for the client to find.</description></item>
    /// <item><description>The helper mutex is taken (one helper at a time, for every logon session of the user:
    /// <see cref="HelperMutex"/>).</description></item>
    /// <item><description>The request is read and validated.</description></item>
    /// <item><description>Only now is <c>&lt;request&gt;.accepted</c> written (atomically): the helper has taken the
    /// request over. The client closes the game when it sees the file. If it cannot be written the run ends here,
    /// because the client would never close the game and the helper would wait for it in vain.</description></item>
    /// <item><description>The command runs (it starts by waiting for the game to exit). While it waits it watches the
    /// request file it acted on: if the client takes the request back (the file is deleted or replaced), the
    /// run ends without touching anything.</description></item>
    /// </list>
    /// <c>watch --file</c> takes none of steps 2 to 6: the enter starts it as a second process (so that it inherits
    /// nothing from the old game), it waits for the Arena game to exit (EXIT, a crash, a kill) without the mutex,
    /// then takes the mutex (a <c>leave</c> may hold it while it restarts the game) and stops the arena server of
    /// its own session with the rules of enter and leave, so that a game closed by hand does not leave the arena
    /// server beside the normal one. After a leave there is nothing left to stop.
    /// </summary>
    internal sealed class HelperHost
    {
        private readonly HelperRunner _runner;
        private readonly HelperLog _log;
        private readonly string _mutexName;
        private readonly Action<string> _output;
        private readonly int _processId;

        // Public so a test harness can shorten the wait; the specification's value is the default.
        public int MutexWaitSeconds = 5;

        // How long the watchdog waits for the mutex once the Arena game has exited: a leave holds it from its
        // start until the normal game runs again (a few seconds for the game to exit, the normal server's start
        // when it is not running, the game start); the minutes are headroom for a slow server start.
        public int WatchMutexWaitSeconds = 180;

        public HelperHost(HelperRunner runner, HelperLog log, string mutexName, Action<string> output)
        {
            _runner = runner;
            _log = log;
            _mutexName = mutexName;
            _output = output ?? (text => { });
            using (Process current = Process.GetCurrentProcess())
            {
                _processId = current.Id;
            }
        }

        /// <summary>Runs the command; the result is the process exit code: 0 on success, 1 on failure.</summary>
        public int Execute(HelperCommand command)
        {
            try
            {
                if (command.Name == HelperCommand.Status)
                {
                    _output(_runner.Status());
                    return 0;
                }

                if (command.Name == HelperCommand.Watch)
                {
                    // No mutex while the game runs: a leave must be able to take it. The stop takes it (WatchArenaGame).
                    if (!_runner.LoadWatch(command.RequestPath)) return 1;
                    WatchArenaGame();
                    return 0;
                }

                string requestPath = null;
                if (command.NeedsRequest)
                {
                    try
                    {
                        requestPath = Path.GetFullPath(command.RequestPath);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("the request path '" + command.RequestPath + "' is not usable: " + ex.Message);
                        return 1;
                    }

                    string problem;
                    if (!HelperAcknowledgementFile.DiscardStale(requestPath, out problem))
                    {
                        // The final write replaces the file in one step, so this is not fatal; it only means the
                        // client may already have seen the old file (its own check of helperPid catches that).
                        _log.Warn("an acknowledgement of an earlier request could not be removed: " + problem);
                    }
                }

                int exit;
                using (Mutex mutex = HelperMutex.Open(_mutexName))
                {
                    bool owned;
                    try
                    {
                        owned = mutex.WaitOne(TimeSpan.FromSeconds(MutexWaitSeconds));
                    }
                    catch (AbandonedMutexException)
                    {
                        _log.Warn("a previous helper instance ended without releasing the mutex");
                        owned = true;
                    }

                    if (!owned)
                    {
                        _log.Error("another helper instance is running; exiting");
                        return 1;
                    }

                    try
                    {
                        exit = Run(command, requestPath) ? 0 : 1;
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }

                return exit;
            }
            catch (Exception ex)
            {
                _log.Error("unexpected failure: " + ex);
                return 1;
            }
        }

        /// <summary>
        /// The watchdog: waits for the Arena game to exit (none to wait for when it is already gone), then takes the
        /// mutex and stops the arena server of its own session (<see cref="HelperRunner.StopAfterGameExit"/>: the rules of
        /// enter and leave, bound to the server the enter started). A mutex that stays busy beyond
        /// <see cref="WatchMutexWaitSeconds"/> leaves the server to whoever holds it (and to the next enter, leave
        /// or stop-server), never kills anything without the mutex, and is logged. Internal for the test harness.
        /// </summary>
        internal void WatchArenaGame()
        {
            Process game = _runner.ArenaGame;
            if (game != null)
            {
                int pid;
                try { pid = game.Id; } catch (Exception) { pid = -1; }
                _log.Info("watching the Arena game (pid " + pid + "): the arena server ends with it");
                _runner.WaitForArenaGameExit();
                _log.Info("the Arena game exited; stopping the arena server");
            }

            using (Mutex mutex = HelperMutex.Open(_mutexName))
            {
                bool owned;
                try
                {
                    owned = mutex.WaitOne(TimeSpan.FromSeconds(WatchMutexWaitSeconds));
                }
                catch (AbandonedMutexException)
                {
                    _log.Warn("a previous helper instance ended without releasing the mutex");
                    owned = true;
                }

                if (!owned)
                {
                    _log.Warn("another helper holds the mutex for more than " + WatchMutexWaitSeconds + " s; the arena server is left to it"
                        + " (the next enter, leave or stop-server ends a server that is still there)");
                    return;
                }

                try
                {
                    _runner.StopAfterGameExit();
                }
                catch (Exception ex)
                {
                    _log.Error("the arena server could not be stopped after the game: " + ex);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private bool Run(HelperCommand command, string requestPath)
        {
            if (command.Name == HelperCommand.StopServer) return _runner.StopServerCommand();

            // The stamp of the file is taken before it is read: a request that is replaced in between then differs
            // from the stamp, and the helper steps aside.
            HelperRequestWatch watch = HelperRequestWatch.Stamp(requestPath);
            HelperRequest request = LoadRequest(command, requestPath);
            if (request == null) return false;
            if (!Acknowledge(requestPath)) return false;

            _runner.RequestStillStands = watch.StillStands;
            try
            {
                return command.Name == HelperCommand.Enter ? _runner.Enter(request) : _runner.Leave(request);
            }
            finally
            {
                _runner.RequestStillStands = null;
            }
        }

        private HelperRequest LoadRequest(HelperCommand command, string requestPath)
        {
            string json;
            try
            {
                json = HelperFileSystem.ReadAllTextShared(requestPath);
            }
            catch (Exception ex)
            {
                _log.Error("cannot read the request file '" + command.RequestPath + "': " + ex.Message);
                return null;
            }

            HelperRequest request;
            string error;
            if (!HelperRequest.TryParse(json, command.Name, out request, out error))
            {
                _log.Error("the request file '" + command.RequestPath + "' is not usable: " + error);
                return null;
            }

            return request;
        }

        private bool Acknowledge(string requestPath)
        {
            try
            {
                string path = HelperAcknowledgementFile.Write(requestPath, _processId, DateTime.UtcNow, null);
                _log.Info("request accepted; acknowledgement written to " + path);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("cannot acknowledge the request (" + ex.Message + "); the client would not close the game, so nothing was changed"
                    + " and the game was not touched");
                return false;
            }
        }
    }

    /// <summary>
    /// The helper's mutex: <see cref="ArenaSessionContract.HelperMutexNamePrefix"/> followed by the SID of the user the
    /// helper runs as, one object for every Windows session of that user, because the runtime root it guards is one per
    /// user (review 2026-10-23: a <c>Local\</c> mutex is one per Windows session, the Terminal Services session a logon
    /// lands in, so a console and a Remote Desktop session of the same account did not serialise). The shipping helper
    /// creates it with an access list that grants only that user and SYSTEM: the default list of a <c>Global\</c> object
    /// comes from the creating token (measured for an elevated administrator: SYSTEM, the Administrators and the creating
    /// logon session, no user SID), so whether another session of the same user could open it would depend on that
    /// token; the explicit list makes it independent of elevation and of the session. Once the helper holds the mutex no
    /// other account can acquire it; an account that creates the name first only denies service (the helper ends with
    /// an error). The tests run this code on net10.0, where System.Threading.AccessControl is not part of the runtime,
    /// and create it with the default list; tools/ArenaSessionHelperNet472Checks reads the name and the list of the
    /// shipping build.
    /// </summary>
    internal static class HelperMutex
    {
        /// <summary>The name for the user this process runs as; a <see cref="HelperException"/> when it has no SID.</summary>
        public static string Name()
        {
            SecurityIdentifier user = CurrentUserSid();
            if (user == null) throw new HelperException("the SID of the current Windows user could not be determined");
            return NameFor(user.Value);
        }

        public static string NameFor(string sid)
        {
            return ArenaSessionContract.HelperMutexNamePrefix + sid;
        }

        /// <summary>
        /// Creates the mutex, or opens the one of that name, never owned at the start. A mutex of that name that exists
        /// but cannot be opened (another account created it) is a <see cref="HelperException"/>.
        /// </summary>
        public static Mutex Open(string name)
        {
#if NETFRAMEWORK
            var security = new MutexSecurity();
            SecurityIdentifier user = CurrentUserSid();
            if (user != null) security.AddAccessRule(new MutexAccessRule(user, MutexRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));
            bool createdNew;
            try
            {
                return new Mutex(false, name, out createdNew, security);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new HelperException("the helper mutex " + name + " exists but cannot be opened (created by another account?): " + ex.Message);
            }
#else
            return new Mutex(false, name);
#endif
        }

        private static SecurityIdentifier CurrentUserSid()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return identity.User;
            }
        }
    }

    /// <summary>
    /// Whether the request file the helper acted on is still the file it read. The client takes a request back by
    /// deleting the file when it gives up waiting for the acknowledgement, and a new attempt writes a new file; a
    /// helper that acknowledged late must then not switch the session when the player closes the game by hand. The
    /// file counts as the same while it exists with the same last-write time and length as when it was stamped (a
    /// rewrite with identical text is a new file all the same). A file that cannot be inspected counts as unchanged:
    /// only a request that is really gone or replaced makes the helper step aside.
    /// </summary>
    internal sealed class HelperRequestWatch
    {
        private readonly string _path;
        private readonly bool _known;
        private readonly DateTime _writtenUtc;
        private readonly long _length;

        private HelperRequestWatch(string path, bool known, DateTime writtenUtc, long length)
        {
            _path = path;
            _known = known;
            _writtenUtc = writtenUtc;
            _length = length;
        }

        /// <summary>Takes the stamp of the file; call it before the file is read. Never throws.</summary>
        public static HelperRequestWatch Stamp(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return new HelperRequestWatch(path, false, DateTime.MinValue, -1);
                return new HelperRequestWatch(path, true, info.LastWriteTimeUtc, info.Length);
            }
            catch (Exception)
            {
                return new HelperRequestWatch(path, false, DateTime.MinValue, -1);
            }
        }

        /// <summary>False when the file is gone or is not the one that was stamped. Never throws.</summary>
        public bool StillStands()
        {
            if (!_known) return true;
            try
            {
                var info = new FileInfo(_path);
                return info.Exists && info.LastWriteTimeUtc == _writtenUtc && info.Length == _length;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
