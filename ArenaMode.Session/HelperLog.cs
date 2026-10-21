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
    /// </summary>
    internal sealed class HelperLog
    {
        private readonly string _path;
        private readonly int _processId;

        public HelperLog(string path)
        {
            _path = path;
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

        private void Write(string level, string message)
        {
            string line = HelperLogFormat.FormatLine(DateTime.UtcNow, _processId, level, message) + "\r\n";
            byte[] bytes = new UTF8Encoding(false).GetBytes(line);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                    using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }

                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
