using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ArenaMode.Shared.Session;
using Microsoft.Win32.SafeHandles;

namespace ArenaMode.SessionHelper
{
    /// <summary>
    /// maschine-ArenaMode.Session.exe: switches the game between the normal session and an Arena
    /// session (docs/ARENA-SESSION-20261021.md, section 6). A GUI-subsystem executable, so it
    /// shows no console window; failed transitions can show a short notice or Retry/Cancel. Details go to
    /// <c>%LOCALAPPDATA%\maschine-ArenaMode\session\helper.log</c>. Exit code 0 means success,
    /// 1 failure. One instance at a time (named mutex); <c>status</c> only reads and so does not
    /// take the mutex. <c>enter</c> and <c>leave</c> write <c>&lt;request file&gt;.accepted</c> once they have the
    /// mutex and a valid request, before they wait for the game (see <see cref="HelperHost"/>).
    ///
    /// <code>
    /// maschine-ArenaMode.Session.exe [start [--profile &lt;id&gt;]]
    /// maschine-ArenaMode.Session.exe enter --request &lt;file&gt;
    /// maschine-ArenaMode.Session.exe leave --request &lt;file&gt;
    /// maschine-ArenaMode.Session.exe stop-server
    /// maschine-ArenaMode.Session.exe status
    /// </code>
    /// </summary>
    internal static class Program
    {
        private const uint SemFailCriticalErrors = 0x0001;
        private const uint SemNoGpFaultErrorBox = 0x0002;
        private const uint SemNoOpenFileErrorBox = 0x8000;

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr window, string text, string caption, uint flags);

        private static HelperLog _log;

        [STAThread]
        private static int Main(string[] args)
        {
            // Suppress OS crash/file-error dialogs; transition failures have their own concise notice.
            SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            string root = ArenaSessionContract.GetRuntimeRoot();
            if (root == null || !HelperPathSafety.IsDriveAbsolute(root))
            {
                return 1; // no local application data folder: nowhere safe to work or to log
            }

            try
            {
                Directory.CreateDirectory(root);
                _log = new HelperLog(ArenaSessionContract.GetHelperLogPath());
            }
            catch (Exception)
            {
                return 1;
            }

            HelperCommand command;
            string error;
            if (!HelperCommand.TryParse(args, out command, out error))
            {
                _log.Error("bad command line: " + error);
                return 1;
            }

            string version = GetVersion();
            _log.Info("maschine-ArenaMode.Session " + version + ": " + command.Name);
            var runner = new HelperRunner(root, _log, version);
            // Defer notices until Execute has released the session mutex and startup lock.
            // A desktop window must never prevent the next session attempt.
            string failureNotice = null;
            runner.FailureNotice = message => failureNotice = message;
            runner.DirectStart = profile =>
            {
                try { return HelperStartWindow.Run(new HelperDirectStart(Assembly.GetExecutingAssembly().Location, root, _log, runner), profile); }
                catch (Exception ex)
                {
                    _log.Error("direct start unavailable: " + ex.Message);
                    failureNotice = ex is HelperException ? ex.Message : "Arena could not start. Check helper.log.";
                    return false;
                }
            };

            // The mutex is named for the user (HelperMutex); without a SID nothing is touched.
            string mutexName;
            try
            {
                mutexName = HelperMutex.Name();
            }
            catch (Exception ex)
            {
                _log.Error("the helper mutex cannot be named for this Windows user: " + ex.Message);
                return 1;
            }

            // The order of the steps (stale acknowledgement, mutex, request, acknowledgement, command) is HelperHost's.
            int result = new HelperHost(runner, _log, mutexName, HelperConsole.WriteLine).Execute(command);
            if (failureNotice != null) MessageBox(IntPtr.Zero, failureNotice, "ArenaMode - session switch", 0x50030);
            return result;
        }

        private static string GetVersion()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "0.0.0" : version.ToString(3);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (_log != null) _log.Error("unhandled exception: " + e.ExceptionObject);
            }
            finally
            {
                Environment.Exit(1);
            }
        }
    }

    /// <summary>
    /// Output for <c>status</c>. The executable has no console of its own, so the line goes to the
    /// inherited standard output (a pipe or a redirect, the way a script reads it) and, when there is
    /// none, to the console of the parent process.
    /// </summary>
    internal static class HelperConsole
    {
        private const int StdOutputHandle = -11;
        private const int AttachParentProcess = -1;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareWrite = 2;
        private const uint OpenExisting = 3;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        public static void WriteLine(string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text + "\r\n");
            try
            {
                IntPtr handle = GetStdHandle(StdOutputHandle);
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    using (var stream = new FileStream(new SafeFileHandle(handle, false), FileAccess.Write))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                    }

                    return;
                }

                if (!AttachConsole(AttachParentProcess)) return;
                using (SafeFileHandle console = CreateFile("CONOUT$", GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
                {
                    if (console.IsInvalid) return;
                    using (var stream = new FileStream(console, FileAccess.Write))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                    }
                }
            }
            catch (Exception)
            {
                // status is a diagnostic; there is nowhere else to put the line.
            }
        }
    }
}
