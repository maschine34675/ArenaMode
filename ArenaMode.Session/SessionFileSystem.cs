// File system side of the Arena session helper (docs/ARENA-SESSION-20261021.md, section 6):
// the sandbox that every write and delete goes through, directory junctions, reparse-point-aware
// deletion, copying with retries, and the builder of the disposable server tree.
//
// Linked source, like SessionLogic.cs: compiled into maschine-ArenaMode.Session.exe (net472) and,
// through a Compile Include with Link, into ArenaMode.Tests (net10.0), so the junction and delete
// behaviour is tested against the real file system. BCL plus kernel32 only, C# 7.3 syntax, no
// process or socket work. See SessionLogic.cs for why there are no nullable annotations.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ArenaMode.Shared.Session;
using Microsoft.Win32.SafeHandles;

namespace ArenaMode.SessionHelper
{
    /// <summary>
    /// An expected failure of the helper. The message is written to <c>helper.log</c> as it is, so it
    /// names the problem and the path.
    /// </summary>
    public sealed class HelperException : Exception
    {
        public HelperException(string message) : base(message)
        {
        }

        public HelperException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    internal static class HelperNative
    {
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FsctlSetReparsePoint = 0x000900A4;
        private const uint IoReparseTagMountPoint = 0xA0000003;
        private const int MaxReparseBuffer = 16 * 1024;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inBuffer, int inBufferSize,
            IntPtr outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveDirectory(string path);

        /// <summary>
        /// Removes a directory, or a directory link (junction or directory symbolic link) without
        /// touching what it points to: <c>RemoveDirectory</c> on a reparse point deletes the link only.
        /// </summary>
        internal static void RemoveDirectoryOrLink(string path)
        {
            if (!RemoveDirectory(ToExtendedPath(path)))
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException("RemoveDirectory failed for '" + path + "': " + new Win32Exception(error).Message);
            }
        }

        /// <summary>
        /// The <c>\\?\</c> form of a drive-absolute path, which the Win32 calls above accept beyond
        /// MAX_PATH (the managed file APIs of .NET Framework 4.6.2 and later already do).
        /// </summary>
        internal static string ToExtendedPath(string path)
        {
            if (path == null || path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
            return @"\\?\" + path;
        }

        /// <summary>
        /// Creates <paramref name="link"/> as a junction to <paramref name="target"/> without needing
        /// administrator rights: an empty directory plus FSCTL_SET_REPARSE_POINT with a mount point
        /// reparse buffer. If that fails, <c>mklink /J</c> is tried before giving up.
        /// </summary>
        internal static void CreateJunction(string link, string target)
        {
            Exception first = null;
            try
            {
                Directory.CreateDirectory(link);
                SetMountPoint(link, target);
                return;
            }
            catch (Exception ex)
            {
                first = ex;
                try
                {
                    if (Directory.Exists(link)) RemoveDirectoryOrLink(link);
                }
                catch (Exception)
                {
                    // Best effort: mklink below fails loudly if the leftover is in its way.
                }
            }

            try
            {
                RunMkLink(link, target);
            }
            catch (Exception second)
            {
                throw new HelperException("cannot create the junction '" + link + "' -> '" + target + "': "
                    + first.Message + "; mklink: " + second.Message, first);
            }
        }

        private static void SetMountPoint(string link, string target)
        {
            string printName = target.TrimEnd('\\');
            if (printName.Length <= 2) throw new IOException("a junction cannot point at a drive root");
            byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + printName);
            byte[] print = Encoding.Unicode.GetBytes(printName);
            int pathBufferLength = substitute.Length + 2 + print.Length + 2;
            if (pathBufferLength + 16 > MaxReparseBuffer) throw new IOException("the junction target path is too long");

            // REPARSE_DATA_BUFFER for IO_REPARSE_TAG_MOUNT_POINT: an 8 byte header (tag, data length,
            // reserved), four 16-bit name offsets and lengths, then both names, each NUL terminated.
            byte[] buffer = new byte[8 + 8 + pathBufferLength];
            PutUInt32(buffer, 0, IoReparseTagMountPoint);
            PutUInt16(buffer, 4, (ushort)(8 + pathBufferLength));
            PutUInt16(buffer, 8, 0);
            PutUInt16(buffer, 10, (ushort)substitute.Length);
            PutUInt16(buffer, 12, (ushort)(substitute.Length + 2));
            PutUInt16(buffer, 14, (ushort)print.Length);
            Buffer.BlockCopy(substitute, 0, buffer, 16, substitute.Length);
            Buffer.BlockCopy(print, 0, buffer, 16 + substitute.Length + 2, print.Length);

            using (SafeFileHandle handle = CreateFile(ToExtendedPath(link), GenericWrite, 0, IntPtr.Zero, OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    int returned;
                    if (!DeviceIoControl(handle, FsctlSetReparsePoint, pin.AddrOfPinnedObject(), buffer.Length,
                        IntPtr.Zero, 0, out returned, IntPtr.Zero))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    pin.Free();
                }
            }
        }

        private static void PutUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void PutUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void RunMkLink(string link, string target)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000)) throw new IOException("mklink did not finish");
                if (process.ExitCode != 0) throw new IOException("mklink exited with " + process.ExitCode + ": " + output.Trim());
            }
        }
    }

    /// <summary>
    /// Every write and delete of the helper goes through one of these, constructed with the session
    /// runtime root (<c>%LOCALAPPDATA%\maschine-ArenaMode\session</c>). A path outside the root is
    /// refused, so is a path whose way down from the root crosses a reparse point (a junction the
    /// helper did not know about would redirect the operation), and deleting never follows a
    /// junction: it is removed as a link and its target is left alone.
    /// </summary>
    public sealed class HelperSandbox
    {
        private const int MaxDepth = 100;

        private readonly string _root;
        private readonly Action<string> _log;

        public HelperSandbox(string root, Action<string> log)
        {
            string normalised;
            if (!HelperPathSafety.TryNormalize(root, out normalised) || normalised.Length <= 3)
            {
                throw new HelperException("the sandbox root must be a folder below a drive root: " + root);
            }

            _root = normalised;
            _log = log ?? (message => { });
            RetryAttempts = 6;
            RetryDelayMilliseconds = 200;
        }

        public string Root
        {
            get { return _root; }
        }

        /// <summary>How often a copy or delete that fails on a locked file is tried.</summary>
        public int RetryAttempts { get; set; }

        public int RetryDelayMilliseconds { get; set; }

        /// <summary>Number of files <see cref="CopyFile"/> and <see cref="CopyDirectory"/> copied so far.</summary>
        public int FilesCopied { get; private set; }

        /// <summary>Number of junctions created so far.</summary>
        public int JunctionsCreated { get; private set; }

        // ------------------------------------------------------------------ checks

        /// <summary>The normalised path, which must lie strictly below the root.</summary>
        public string Require(string path)
        {
            string full;
            if (!HelperPathSafety.TryNormalize(path, out full) || !HelperPathSafety.IsStrictlyUnder(full, _root))
            {
                throw new HelperException("refusing to touch '" + path + "': it is outside " + _root);
            }

            return full;
        }

        /// <summary>
        /// <see cref="Require"/> plus the check that neither the root nor any folder between the root
        /// and the parent of the path is a reparse point.
        /// </summary>
        public string RequireClean(string path)
        {
            string full = Require(path);
            RequireNoReparsePointAbove(full);
            return full;
        }

        private void RequireNoReparsePointAbove(string full)
        {
            string parent = HelperPathSafety.GetParent(full);
            List<string> segments = parent == null ? null : HelperPathSafety.RelativeSegments(parent, _root);
            if (segments == null) throw new HelperException("refusing to touch '" + full + "': its parent is outside " + _root);

            string current = _root;
            if (HelperFileSystem.IsReparsePoint(current))
            {
                throw new HelperException("refusing to work inside '" + _root + "': it is a junction or symbolic link");
            }

            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                if (!HelperFileSystem.PathExists(current)) break;
                if (HelperFileSystem.IsReparsePoint(current))
                {
                    throw new HelperException("refusing to touch '" + full + "': '" + current + "' on the way is a junction or symbolic link");
                }
            }
        }

        // ------------------------------------------------------------------ operations

        public void CreateDirectory(string path)
        {
            string full = RequireClean(path);
            Directory.CreateDirectory(full);
        }

        public void WriteAllText(string path, string text)
        {
            string full = RequireClean(path);
            string parent = HelperPathSafety.GetParent(full);
            if (parent != null && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
            Retry("write " + full, delegate
            {
                File.WriteAllText(full, text, new UTF8Encoding(false));
            });
        }

        public void CopyFile(string source, string destination)
        {
            string full = RequireClean(destination);
            string parent = HelperPathSafety.GetParent(full);
            if (parent != null && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
            CopyFileCore(source, full);
        }

        /// <summary>
        /// Copies a folder tree. Reparse points found inside the source are skipped (and logged):
        /// following one could leave the tree the caller named or loop forever.
        /// </summary>
        public void CopyDirectory(string source, string destination)
        {
            string full = RequireClean(destination);
            if (!Directory.Exists(source)) throw new HelperException("cannot copy '" + source + "': the folder does not exist");
            CopyDirectoryCore(source, full, 0);
        }

        public void CreateJunction(string junctionPath, string targetDirectory)
        {
            string link = RequireClean(junctionPath);
            string target;
            if (!HelperPathSafety.TryNormalize(targetDirectory, out target) || !Directory.Exists(target))
            {
                throw new HelperException("the junction target '" + targetDirectory + "' is not an existing folder on a local drive");
            }

            if (HelperPathSafety.IsSameOrUnder(target, _root))
            {
                throw new HelperException("the junction target '" + target + "' lies inside the session tree");
            }

            if (HelperFileSystem.PathExists(link)) throw new HelperException("cannot create the junction '" + link + "': the path exists");
            string parent = HelperPathSafety.GetParent(link);
            if (parent != null && !Directory.Exists(parent)) Directory.CreateDirectory(parent);

            HelperNative.CreateJunction(link, target);
            if (!HelperFileSystem.IsReparsePoint(link))
            {
                throw new HelperException("the junction '" + link + "' was not created");
            }

            JunctionsCreated++;
        }

        /// <summary>
        /// Deletes a file, a folder tree or a junction below the root. Junctions and symbolic links are
        /// removed as links, never entered. A path that does not exist is not an error.
        /// </summary>
        public void DeleteTree(string path)
        {
            string full = RequireClean(path);
            DeleteEntry(full, 0);
        }

        public void DeleteFile(string path)
        {
            DeleteTree(path);
        }

        // ------------------------------------------------------------------ internals

        private void CopyDirectoryCore(string source, string destination, int depth)
        {
            if (depth > MaxDepth) throw new HelperException("cannot copy '" + source + "': the folder tree is deeper than " + MaxDepth);
            Directory.CreateDirectory(destination);
            foreach (FileSystemInfo entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
            {
                string target = Path.Combine(destination, entry.Name);
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    if (HelperPathSafety.IsReparsePoint(entry.Attributes))
                    {
                        _log("skipped the reparse point '" + entry.FullName + "' while copying");
                        continue;
                    }

                    CopyDirectoryCore(entry.FullName, target, depth + 1);
                }
                else
                {
                    CopyFileCore(entry.FullName, target);
                }
            }
        }

        private void CopyFileCore(string source, string destination)
        {
            Retry("copy " + source, delegate
            {
                // The source may be open in another process (the normal server, a game); share
                // everything so a reader never blocks on a writer.
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
            });
            try
            {
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            }
            catch (Exception)
            {
                // The timestamp is a courtesy; the content is what matters.
            }

            FilesCopied++;
        }

        private void DeleteEntry(string path, int depth)
        {
            FileAttributes attributes;
            if (!HelperFileSystem.TryGetAttributes(path, out attributes)) return;

            if ((attributes & FileAttributes.Directory) == 0)
            {
                Retry("delete " + path, delegate
                {
                    ClearReadOnly(path, attributes);
                    File.Delete(path);
                });
                return;
            }

            if (HelperPathSafety.IsReparsePoint(attributes))
            {
                _log("removing the link '" + path + "' without entering it");
                Retry("remove link " + path, delegate
                {
                    ClearReadOnly(path, attributes);
                    HelperNative.RemoveDirectoryOrLink(path);
                });
                return;
            }

            if (depth > MaxDepth) throw new HelperException("cannot delete '" + path + "': the folder tree is deeper than " + MaxDepth);
            foreach (string child in Directory.GetFileSystemEntries(path)) DeleteEntry(child, depth + 1);
            Retry("delete " + path, delegate
            {
                ClearReadOnly(path, attributes);
                Directory.Delete(path, false);
            });
        }

        private static void ClearReadOnly(string path, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReadOnly) == 0) return;
            try
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch (Exception)
            {
                // The delete that follows reports the real problem.
            }
        }

        private void Retry(string what, Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (FileNotFoundException)
                {
                    throw;
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < RetryAttempts)
                {
                    _log("retry " + attempt + " (" + what + "): " + ex.Message);
                    Thread.Sleep(RetryDelayMilliseconds * attempt);
                }
            }
        }
    }

    /// <summary>Small file system queries that do not follow reparse points and do not throw.</summary>
    public static class HelperFileSystem
    {
        /// <summary>The attributes of the path itself (a junction is not resolved); false when nothing is there.</summary>
        public static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            attributes = 0;
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        /// <summary>True when something is at the path, a dangling junction included.</summary>
        public static bool PathExists(string path)
        {
            FileAttributes attributes;
            return TryGetAttributes(path, out attributes);
        }

        /// <summary>True when the path is a junction or symbolic link (even a dangling one).</summary>
        public static bool IsReparsePoint(string path)
        {
            FileAttributes attributes;
            return TryGetAttributes(path, out attributes) && HelperPathSafety.IsReparsePoint(attributes);
        }

        /// <summary>Reads a text file while other processes may have it open for reading and writing.</summary>
        public static string ReadAllTextShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
            {
                return reader.ReadToEnd();
            }
        }
    }

    /// <summary>What <see cref="HelperServerTree.Build"/> made.</summary>
    public sealed class HelperTreeResult
    {
        public HelperTreeResult()
        {
            Junctions = new List<string>();
            Mods = new List<string>();
        }

        public string TreePath { get; set; }

        public int Port { get; set; }

        /// <summary>Names of the SPT_Data folders shared through a junction.</summary>
        public List<string> Junctions { get; private set; }

        /// <summary>Folder names of the server mods that were copied.</summary>
        public List<string> Mods { get; private set; }

        public int FilesCopied { get; set; }
    }

    /// <summary>Builds the disposable server tree <c>&lt;runtime root&gt;\server</c> of an Arena session.</summary>
    public static class HelperServerTree
    {
        public static string GetTreePath(string runtimeRoot)
        {
            return Path.Combine(runtimeRoot, ArenaSessionContract.ServerTreeFolderName);
        }

        public static string GetMarkerPath(string runtimeRoot)
        {
            return Path.Combine(GetTreePath(runtimeRoot), ArenaSessionContract.MarkerFileName);
        }

        public static string GetPidFilePath(string runtimeRoot)
        {
            return Path.Combine(runtimeRoot, ArenaSessionContract.ServerPidFileName);
        }

        /// <summary>
        /// Deletes the old tree (junctions as links) and materialises a new one for an
        /// <c>enter</c> request: the patched <c>sptLogger.json</c>, <c>SPT_Data</c> (junctions plus a
        /// copied <c>configs</c> with the arena port in <c>http.json</c>), <c>user\mods</c> with a
        /// copy of each listed server mod, the profile, its profile data, certificates and
        /// credentials, and last the marker that turns the tree into an Arena instance. A tree
        /// without a marker is therefore never mistaken for a finished one.
        /// </summary>
        public static HelperTreeResult Build(HelperRequest request, int arenaPort, string runtimeRoot, string helperVersion,
            DateTime nowUtc, Action<string> log)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (!request.IsEnter) throw new HelperException("only an 'enter' request builds the server tree");
            if (arenaPort < 1 || arenaPort > 65535) throw new HelperException("invalid arena port " + arenaPort);
            if (!HelperRequest.IsValidProfileId(request.ProfileId)) throw new HelperException("the profile id is not valid");

            string root;
            if (!HelperPathSafety.TryNormalize(runtimeRoot, out root)) throw new HelperException("the runtime root is not usable: " + runtimeRoot);
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);

            var box = new HelperSandbox(root, log);
            string source = request.SptRuntimeDir;
            string tree = GetTreePath(root);
            var result = new HelperTreeResult { TreePath = tree, Port = arenaPort };

            box.DeleteTree(tree);
            box.CreateDirectory(tree);

            // sptLogger.json: the server refuses to run from a folder without one, and its file
            // names get a prefix so the arena logs never share a name with the normal server's.
            string loggerSource = Path.Combine(source, HelperLayout.LoggerFileName);
            if (!File.Exists(loggerSource)) throw new HelperException("no " + HelperLayout.LoggerFileName + " in " + source);
            string patchedLogger;
            int patterns;
            string error;
            if (!HelperJsonText.TryPrefixLogFilePatterns(HelperFileSystem.ReadAllTextShared(loggerSource),
                HelperLayout.LogFilePatternPrefix, out patchedLogger, out patterns, out error))
            {
                throw new HelperException(HelperLayout.LoggerFileName + ": " + error);
            }

            box.WriteAllText(Path.Combine(tree, HelperLayout.LoggerFileName), patchedLogger);

            // SPT_Data: shared folders by junction, small files and configs by copy.
            string dataSource = Path.Combine(source, HelperLayout.SptDataFolder);
            if (!Directory.Exists(dataSource)) throw new HelperException("no " + HelperLayout.SptDataFolder + " folder in " + source);
            string dataTarget = Path.Combine(tree, HelperLayout.SptDataFolder);
            box.CreateDirectory(dataTarget);
            var entries = new List<string>(Directory.GetFileSystemEntries(dataSource));
            entries.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);
                HelperSptDataAction action = HelperTreePlan.Classify(name, Directory.Exists(entry));
                string destination = Path.Combine(dataTarget, name);
                if (action == HelperSptDataAction.Junction)
                {
                    box.CreateJunction(destination, entry);
                    result.Junctions.Add(name);
                }
                else if (action == HelperSptDataAction.CopyDirectory)
                {
                    box.CopyDirectory(entry, destination);
                }
                else
                {
                    box.CopyFile(entry, destination);
                }
            }

            string httpJson = Path.Combine(Path.Combine(dataTarget, HelperLayout.ConfigsFolder), HelperLayout.HttpJsonFileName);
            if (!File.Exists(httpJson)) throw new HelperException("no " + HelperLayout.HttpJsonFileName + " in " + Path.Combine(dataSource, HelperLayout.ConfigsFolder));
            string patchedHttp;
            if (!HelperJsonText.TryPatchHttpPorts(HelperFileSystem.ReadAllTextShared(httpJson), arenaPort, out patchedHttp, out error))
            {
                throw new HelperException(HelperLayout.HttpJsonFileName + ": " + error);
            }

            box.WriteAllText(httpJson, patchedHttp);

            // user\mods: a real copy of each allowed server mod (not a junction), so that what a mod
            // writes beside its own files, found through its assembly location or the working
            // directory, stays in the disposable tree. Only the helper's own operations are confined
            // by this: the server is started from the real runtime's exe, so a mod that resolves its
            // paths from AppContext.BaseDirectory (or AppDomain.CurrentDomain.BaseDirectory) still
            // reads and writes the real installation, not this copy.
            string userSource = Path.Combine(source, HelperLayout.UserFolder);
            string userTarget = Path.Combine(tree, HelperLayout.UserFolder);
            string modsTarget = Path.Combine(userTarget, HelperLayout.ModsFolder);
            box.CreateDirectory(modsTarget);
            foreach (HelperServerMod mod in request.ServerMods)
            {
                if (!Directory.Exists(mod.Folder)) throw new HelperException("the server mod '" + mod.Guid + "' has no folder at " + mod.Folder);
                box.CopyDirectory(mod.Folder, Path.Combine(modsTarget, mod.FolderName));
                result.Mods.Add(mod.FolderName);
            }

            // The profile: a fresh copy of exactly this one file (never a pattern: the profiles
            // folder may hold backups and sync conflicts of it).
            string profileName = request.ProfileId + ".json";
            string profileSource = Path.Combine(Path.Combine(userSource, HelperLayout.ProfilesFolder), profileName);
            if (!File.Exists(profileSource)) throw new HelperException("no profile file at " + profileSource);
            box.CopyFile(profileSource, Path.Combine(Path.Combine(userTarget, HelperLayout.ProfilesFolder), profileName));

            CopyIfExists(box, Path.Combine(Path.Combine(userSource, HelperLayout.ProfileDataFolder), request.ProfileId),
                Path.Combine(Path.Combine(userTarget, HelperLayout.ProfileDataFolder), request.ProfileId));
            CopyIfExists(box, Path.Combine(userSource, HelperLayout.CertsFolder), Path.Combine(userTarget, HelperLayout.CertsFolder));
            CopyIfExists(box, Path.Combine(userSource, HelperLayout.CredentialsFolder), Path.Combine(userTarget, HelperLayout.CredentialsFolder));

            // The marker comes last.
            HelperMarker marker = HelperMarker.Create(nowUtc, request.ProfileId, arenaPort, source, request.PolicyHash, helperVersion);
            box.WriteAllText(Path.Combine(tree, ArenaSessionContract.MarkerFileName), marker.ToJson());

            result.FilesCopied = box.FilesCopied;
            return result;
        }

        private static void CopyIfExists(HelperSandbox box, string source, string destination)
        {
            if (Directory.Exists(source)) box.CopyDirectory(source, destination);
        }
    }
}
