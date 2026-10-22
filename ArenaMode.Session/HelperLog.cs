// Also compiled into ArenaMode.Tests (net10.0, nullable annotations enabled; see
// ArenaSessionHelperProcessTests). This file carries none on purpose (C# 7.3 like SessionLogic.cs), hence the pragma.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    /// <summary>
    /// <c>helper.log</c> in the session runtime root. Every helper run appends to it; at the start of
    /// a run a file of 1 MB or more is moved to <c>helper.log.1</c> (replacing an older one). Writing
    /// never throws: a log that cannot be written must not stop a session switch.
    /// The log is written outside the sandbox (its own file, no copy of anything), and it never follows a link
    /// either: a symbolic link or junction under the log's name is removed as a link (its target is not
    /// touched) and the log goes to a plain file in its place, with a first line saying so; the file is also
    /// opened without following a link (<see cref="HelperNative.OpenFileForWriting"/>), so a link that
    /// appears after that check makes the write end without writing anything.
    /// </summary>
    internal sealed class HelperLog
    {
        private readonly string _path;
        private readonly string _fullPath;
        private readonly int _processId;

        public HelperLog(string path)
        {
            _path = path;
            _fullPath = FullPathOf(path);
            using (Process current = Process.GetCurrentProcess())
            {
                _processId = current.Id;
            }

            Rotate();
        }

        public string FilePath
        {
            get { return _path; }
        }

        /// <summary>
        /// Tests only: called with the log's full path after the check for a link and just before the file is opened, so
        /// that a test can plant a link in the instant the check cannot cover (the open must then refuse it). Null in the helper.
        /// </summary>
        internal Action<string> BeforeOpen { get; set; }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warn(string message)
        {
            Write("WARN", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Rotate()
        {
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists || !HelperLogFormat.ShouldRotate(info.Length)) return;
                string older = _path + ".1";
                if (File.Exists(older)) File.Delete(older);
                File.Move(_path, older);
            }
            catch (Exception)
            {
                // Another helper instance may hold the file; appending to the big file is fine.
            }
        }

        private static string FullPathOf(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }

        private void Write(string level, string message)
        {
            string line = HelperLogFormat.FormatLine(DateTime.UtcNow, _processId, level, message) + "\r\n";
            byte[] bytes = new UTF8Encoding(false).GetBytes(line);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string directory = Path.GetDirectoryName(_fullPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                    byte[] payload = ReplaceLink(bytes);
                    if (BeforeOpen != null) BeforeOpen(_fullPath);
                    using (FileStream stream = HelperNative.OpenFileForWriting(_fullPath, false, FileShare.ReadWrite | FileShare.Delete))
                    {
                        stream.Write(payload, 0, payload.Length);
                    }

                    return;
                }
                catch (HelperException)
                {
                    // A link under the log's name that could not be removed, or one that appeared after the check:
                    // the write is refused, and retrying cannot change that.
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(50);
                }
            }
        }

        // The log never follows a link. One under its name (a file symbolic link, a junction, a dangling one) is
        // removed as a link, so that the log works and the target stays as it is; the line that follows then comes
        // after a first line that says so. Returns the bytes to write.
        private byte[] ReplaceLink(byte[] bytes)
        {
            FileAttributes attributes;
            if (!HelperFileSystem.TryGetAttributes(_fullPath, out attributes) || !HelperPathSafety.IsReparsePoint(attributes)) return bytes;

            string directory = Path.GetDirectoryName(_fullPath);
            new HelperSandbox(directory, null).DeleteFile(_fullPath);
            string notice = HelperLogFormat.FormatLine(DateTime.UtcNow, _processId, "WARN",
                "helper.log was a symbolic link or junction; the link itself was removed (its target was not touched) and this plain file takes its place") + "\r\n";
            byte[] first = new UTF8Encoding(false).GetBytes(notice);
            var combined = new byte[first.Length + bytes.Length];
            Buffer.BlockCopy(first, 0, combined, 0, first.Length);
            Buffer.BlockCopy(bytes, 0, combined, first.Length, bytes.Length);
            return combined;
        }
    }
}
