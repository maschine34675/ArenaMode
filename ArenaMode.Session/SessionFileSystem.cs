// File system side of the Arena session helper (docs/ARENA-SESSION-20261021.md, section 6):
// the sandbox that every write and delete goes through, directory junctions, reparse-point-aware
// deletion, copying with retries, and the builder of the disposable server tree. The files the
// helper writes outside the sandbox are helper.log (HelperLog) and the acknowledgement of a request,
// which sits beside the request file the client chose (HelperAcknowledgementFile at the end of this
// file). Every file any of these writes creates or truncates is opened without following a link
// (HelperNative.OpenFileForWriting), and the sandbox removes a link at the destination first.
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
        private const uint OpenAlways = 4;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileAttributeReparsePoint = 0x400;
        private const uint FsctlSetReparsePoint = 0x000900A4;
        private const uint IoReparseTagMountPoint = 0xA0000003;
        private const int MaxReparseBuffer = 16 * 1024;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

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
        /// Opens a plain file for writing and never follows a reparse point at the last component of the path: the
        /// file is opened with <c>FILE_FLAG_OPEN_REPARSE_POINT</c>, so a file symbolic link (a dangling one included)
        /// is opened itself and not its target, and the handle's attributes are checked before anything is written.
        /// A file link is refused with a <see cref="HelperException"/> (nothing was changed), so a link that appears
        /// after a caller looked at the path still cannot redirect the write. A junction and a folder never get that
        /// far: without <c>FILE_FLAG_BACKUP_SEMANTICS</c> <c>CreateFile</c> cannot open a folder for writing and fails
        /// with "access denied", so they surface as an <see cref="UnauthorizedAccessException"/> and nothing is
        /// changed either (the sandbox's retry repeats such a failure for about three seconds, then gives up). A
        /// missing file is created. With <paramref name="truncate"/> the file is emptied (after the check), otherwise
        /// the position is the end of the file (an append). Other failures are the exceptions the managed file API
        /// throws for them (<see cref="IOException"/> for a sharing violation, <see cref="UnauthorizedAccessException"/>
        /// for a read-only file), so the sandbox's retry treats them as it always did.
        /// </summary>
        internal static FileStream OpenFileForWriting(string path, bool truncate, FileShare share)
        {
            SafeFileHandle handle = CreateFile(ToExtendedPath(path), GenericWrite, (uint)share & 7, IntPtr.Zero, OpenAlways,
                FileFlagOpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw OpenFailure(error, path);
            }

            bool owned = true;
            try
            {
                ByHandleFileInformation information;
                if (!GetFileInformationByHandle(handle, out information)) throw OpenFailure(Marshal.GetLastWin32Error(), path);
                // Only a file link gets here (the open above already failed for a junction or a folder, see the summary).
                if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
                {
                    throw new HelperException("refusing to write to '" + path + "': it is a symbolic link or junction, which a write never follows");
                }

                var stream = new FileStream(handle, FileAccess.Write);
                owned = false; // the stream closes the handle now
                try
                {
                    if (truncate) stream.SetLength(0);
                    else stream.Seek(0, SeekOrigin.End);
                    return stream;
                }
                catch (Exception)
                {
                    stream.Dispose();
                    throw;
                }
            }
            finally
            {
                if (owned) handle.Dispose();
            }
        }

        private static Exception OpenFailure(int error, string path)
        {
            string message = "cannot open '" + path + "' for writing: " + new Win32Exception(error).Message;
            switch (error)
            {
                case 2:
                    return new FileNotFoundException(message, path);
                case 3:
                    return new DirectoryNotFoundException(message);
                case 5:
                    return new UnauthorizedAccessException(message);
                default:
                    return new IOException(message, unchecked((int)0x80070000) | (error & 0xFFFF));
            }
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

        /// <summary>Number of files <see cref="CopyFile"/>, <see cref="CopyFileIfChanged"/> and <see cref="CopyDirectory"/> copied so far.</summary>
        public int FilesCopied { get; private set; }

        /// <summary>Number of files <see cref="CopyFileIfChanged"/> found unchanged and left alone so far.</summary>
        public int FilesUnchanged { get; private set; }

        /// <summary>Number of junctions created so far.</summary>
        public int JunctionsCreated { get; private set; }

        /// <summary>
        /// Tests only: called with the destination of a write (<see cref="WriteAllText"/>, a copy) after the link at it was
        /// removed and just before the file is opened, so a test can plant a link in the instant that the removal cannot
        /// cover. The open must then refuse the link (<see cref="HelperNative.OpenFileForWriting"/>). Null in the helper.
        /// </summary>
        internal Action<string> BeforeWriteOpen { get; set; }

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

        /// <summary>
        /// Writes <paramref name="text"/> (UTF-8 without a byte order mark) as the whole content of a file. A link
        /// where the file goes (a file symbolic link, a junction, a dangling one) is removed as a link first and the file
        /// is then written in its place, like a copy does: the write never follows the link, whatever it points to.
        /// The file is also opened without following a link (<see cref="HelperNative.OpenFileForWriting"/>), so a link
        /// that appears between the removal and the open is refused (<see cref="HelperException"/>) and not followed.
        /// </summary>
        public void WriteAllText(string path, string text)
        {
            string full = RequireClean(path);
            string parent = HelperPathSafety.GetParent(full);
            if (parent != null && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
            byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
            RemoveLinkAt(full);
            Retry("write " + full, delegate
            {
                if (BeforeWriteOpen != null) BeforeWriteOpen(full);
                using (FileStream output = HelperNative.OpenFileForWriting(full, true, FileShare.Read))
                {
                    output.Write(bytes, 0, bytes.Length);
                }
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
        /// <see cref="CopyFile"/> unless the destination is already a copy of the source: a file whose
        /// length and last-write time (UTC) match is left alone, which is what a copy made by this
        /// sandbox looks like because it keeps the source's timestamp. Returns true when the file was
        /// copied. A locked file is retried like any other copy.
        /// </summary>
        public bool CopyFileIfChanged(string source, string destination)
        {
            string full = RequireClean(destination);
            if (IsCopyOf(source, full))
            {
                FilesUnchanged++;
                return false;
            }

            string parent = HelperPathSafety.GetParent(full);
            if (parent != null && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
            CopyFileCore(source, full);
            return true;
        }

        private static bool IsCopyOf(string source, string destination)
        {
            try
            {
                var from = new FileInfo(source);
                var to = new FileInfo(destination);
                if (!from.Exists || !to.Exists) return false;
                if ((to.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
                return HelperServerBinaries.IsUnchanged(from.Length, from.LastWriteTimeUtc, to.Length, to.LastWriteTimeUtc);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
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

        /// <summary>
        /// Copies a server mod folder like <see cref="CopyDirectory"/>, except that a subfolder holding
        /// only Unity asset bundles (<c>*.bundle</c> at any depth, at least one, no links) becomes a
        /// junction to the installation's folder. SPT serves bundles read-only and neither SPT nor a
        /// mod writes them (the hash cache lives in <c>user\cache</c>), so the link costs no isolation,
        /// while a copy would duplicate gigabytes at every enter (WTT-ContentBackport ships about 8 GB).
        /// </summary>
        public void CopyModDirectory(string source, string destination)
        {
            string full = RequireClean(destination);
            if (!Directory.Exists(source)) throw new HelperException("cannot copy '" + source + "': the folder does not exist");
            CopyModDirectoryCore(source, full, 0);
        }

        /// <summary>Number of bundle-only folders <see cref="CopyModDirectory"/> linked instead of copying.</summary>
        public int BundleFoldersLinked { get; private set; }

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

        private void CopyModDirectoryCore(string source, string destination, int depth)
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

                    if (IsBundleOnlyFolder(entry.FullName, depth + 1))
                    {
                        CreateJunction(target, entry.FullName);
                        BundleFoldersLinked++;
                        continue;
                    }

                    CopyModDirectoryCore(entry.FullName, target, depth + 1);
                }
                else
                {
                    CopyFileCore(entry.FullName, target);
                }
            }
        }

        /// <summary>
        /// True for a folder whose files, at any depth, are all <c>*.bundle</c> files and that holds at
        /// least one of them. Any other file, any link and an empty folder answer false, so such a
        /// folder is copied as before.
        /// </summary>
        internal static bool IsBundleOnlyFolder(string folder, int depth)
        {
            if (depth > MaxDepth) return false;
            bool anyBundle = false;
            foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if (HelperPathSafety.IsReparsePoint(entry.Attributes)) return false;
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    if (!IsBundleOnlyFolder(entry.FullName, depth + 1)) return false;
                    anyBundle = true;
                }
                else if (string.Equals(Path.GetExtension(entry.Name), ".bundle", StringComparison.OrdinalIgnoreCase))
                {
                    anyBundle = true;
                }
                else
                {
                    return false;
                }
            }

            return anyBundle;
        }

        // A link where a write goes would send the write to its target, wherever that is: replace the link itself
        // (removed as a link, never followed) and write a plain file. Used by every write that creates or truncates
        // a file of the sandbox; the open of the file refuses a link as well (HelperNative.OpenFileForWriting), for
        // the instant between this check and the open.
        private void RemoveLinkAt(string destination)
        {
            FileAttributes existing;
            if (HelperFileSystem.TryGetAttributes(destination, out existing) && HelperPathSafety.IsReparsePoint(existing))
            {
                _log("'" + destination + "' is a link: removing the link itself (never its target) before writing the file");
                DeleteEntry(destination, 0);
            }
        }

        private void CopyFileCore(string source, string destination)
        {
            RemoveLinkAt(destination);

            Retry("copy " + source, delegate
            {
                if (BeforeWriteOpen != null) BeforeWriteOpen(destination);

                // The source may be open in another process (the normal server, a game); share
                // everything so a reader never blocks on a writer.
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (FileStream output = HelperNative.OpenFileForWriting(destination, true, FileShare.None))
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
            Patchers = new List<string>();
            UnsharedFolders = new List<string>();
            LogPathRewrites = new List<string>();
            BackupDirectoryRewrites = new List<string>();
            ProfileCleanupChanges = new List<string>();
        }

        public string TreePath { get; set; }

        public int Port { get; set; }

        /// <summary>The <c>SPT.Server.exe</c> in the tree, the image the arena server is started from.</summary>
        public string ServerExe { get; set; }

        /// <summary>Names of the SPT_Data folders shared through a junction.</summary>
        public List<string> Junctions { get; private set; }

        /// <summary>Folder names of the server mods that were copied.</summary>
        public List<string> Mods { get; private set; }

        /// <summary>
        /// Names of the <c>user\patchers\&lt;ModGuid&gt;</c> folders (server prepatch definitions) that were copied:
        /// one per listed server mod whose GUID has such a folder in the runtime.
        /// </summary>
        public List<string> Patchers { get; private set; }

        /// <summary>Every file the build copied (server binaries, configs, mods, profile).</summary>
        public int FilesCopied { get; set; }

        /// <summary>Server binaries copied into the tree root by this build.</summary>
        public int ServerFilesCopied { get; set; }

        /// <summary>Server binaries a previous build had already copied and that were left as they were.</summary>
        public int ServerFilesUnchanged { get; set; }

        /// <summary>
        /// Top-level folders of the runtime that the tree neither links nor copies (everything except
        /// <c>SPT_Data</c> and <c>user</c>). A server whose <c>deps.json</c> pointed into such a folder
        /// would not find it next to the copied exe; the runner warns about it.
        /// </summary>
        public List<string> UnsharedFolders { get; private set; }

        /// <summary>
        /// The <c>filePath</c> values of the runtime's <c>sptLogger.json</c> that were not relative paths below
        /// <c>user</c> and were moved to <c>./user/logs/&lt;name&gt;/</c> in the tree's copy (each entry reads
        /// <c>'old' -> 'new'</c>); empty when the logger configuration needed no change. The runner logs one warning
        /// for them.
        /// </summary>
        public List<string> LogPathRewrites { get; private set; }

        /// <summary>
        /// One entry per backup config file of the runtime (<see cref="HelperLayout.IsBackupConfigFile"/>) whose
        /// <c>directory</c> was not a relative path below <c>user</c>, or was missing, and was replaced by or set to
        /// <see cref="HelperLayout.PrivateBackupDirectory"/> in the tree's copy (<c>&lt;file&gt;: 'old' -> 'new'</c>,
        /// <c>&lt;file&gt;: (no directory member) -> 'new'</c>); empty when no such file exists or none needed a change.
        /// The runner logs one warning for them.
        /// </summary>
        public List<string> BackupDirectoryRewrites { get; private set; }

        /// <summary>
        /// One entry per core config file of the runtime (<see cref="HelperLayout.IsCoreConfigFile"/>) whose profile clean-up
        /// flags had to be switched on in the tree's copy (<c>&lt;file&gt;: fixes.removeModItemsFromProfile: false -> true, ...</c>);
        /// empty when no such file exists or both flags were already on. The runner logs one Info line for them.
        /// </summary>
        public List<string> ProfileCleanupChanges { get; private set; }
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
        /// Empties the old tree (junctions as links) and materialises a new one for an
        /// <c>enter</c> request: the server binaries (every top-level file of the runtime that
        /// <see cref="HelperServerBinaries"/> selects, so that <c>SPT.Server.exe</c> runs from the tree
        /// and <c>AppContext.BaseDirectory</c> points into it), the patched <c>sptLogger.json</c>,
        /// <c>SPT_Data</c> (junctions plus a copied <c>configs</c> with the arena port in
        /// <c>http.json</c>), <c>user\mods</c> with a copy of each listed server mod, <c>user\patchers\&lt;ModGuid&gt;</c>
        /// (server prepatch definitions) of every listed mod that has such a folder, the profile, its
        /// profile data, certificates and credentials, and last the marker that turns the tree into an
        /// Arena instance. A tree without a marker is therefore never mistaken for a finished one.
        /// The server binaries of the previous tree stay in place and are copied only when the
        /// runtime's file differs in length or last-write time: a session switch otherwise copied
        /// about twenty megabytes of unchanged libraries every time.
        /// </summary>
        public static HelperTreeResult Build(HelperRequest request, int arenaPort, string runtimeRoot, string helperVersion,
            DateTime nowUtc, Action<string> log)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (!request.IsEnter) throw new HelperException("only an 'enter' request builds the server tree");
            if (arenaPort < 1 || arenaPort > 65535) throw new HelperException("invalid arena port " + arenaPort);
            if (!HelperRequest.IsValidProfileId(request.ProfileId)) throw new HelperException("the profile id is not valid");
            string versionError;
            if (!request.ValidateVersionPlan(out versionError)) throw new HelperException(versionError);

            string root;
            if (!HelperPathSafety.TryNormalize(runtimeRoot, out root)) throw new HelperException("the runtime root is not usable: " + runtimeRoot);
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);

            var box = new HelperSandbox(root, log);
            string source = request.SptRuntimeDir;
            string tree = GetTreePath(root);
            var result = new HelperTreeResult { TreePath = tree, Port = arenaPort };

            // The exe the server is started from lives in the tree. Without the runtime's own it cannot be built.
            if (!File.Exists(Path.Combine(source, HelperLayout.ServerExeName))) throw new HelperException("no " + HelperLayout.ServerExeName + " in " + source);
            var topLevelFiles = new List<string>();
            foreach (string path in Directory.GetFiles(source)) topLevelFiles.Add(Path.GetFileName(path));
            List<string> binaries = HelperServerBinaries.Select(topLevelFiles);
            result.ServerExe = Path.Combine(tree, HelperLayout.ServerExeName);
            foreach (string path in Directory.GetDirectories(source))
            {
                string folder = Path.GetFileName(path);
                if (!string.Equals(folder, HelperLayout.SptDataFolder, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(folder, HelperLayout.UserFolder, StringComparison.OrdinalIgnoreCase))
                {
                    result.UnsharedFolders.Add(folder);
                }
            }

            // This guard also covers callers other than HelperRunner. Results must have been
            // durably returned by the stopped-server path before any old user data is removed.
            HelperRewardTransfer.RequireDrainedBeforeRebuild(root);
            PrepareTree(box, tree, binaries);

            // The server binaries: the files the exe, its dll and deps.json need, beside the exe.
            foreach (string name in binaries)
            {
                if (box.CopyFileIfChanged(Path.Combine(source, name), Path.Combine(tree, name))) result.ServerFilesCopied++;
                else result.ServerFilesUnchanged++;
            }

            // sptLogger.json: the server refuses to run from a folder without one, and its file
            // names get a prefix so the arena logs never share a name with the normal server's.
            string loggerSource = Path.Combine(source, HelperLayout.LoggerFileName);
            if (!File.Exists(loggerSource)) throw new HelperException("no " + HelperLayout.LoggerFileName + " in " + source);
            string patchedLogger;
            int patterns;
            List<string> movedPaths;
            string error;
            if (!HelperJsonText.TryPrefixLogFilePatterns(HelperFileSystem.ReadAllTextShared(loggerSource),
                HelperLayout.LogFilePatternPrefix, out patchedLogger, out patterns, out movedPaths, out error))
            {
                throw new HelperException(HelperLayout.LoggerFileName + ": " + error);
            }

            // A logger path outside user (an absolute one, one that climbs out of the tree, one into the SPT_Data
            // junctions) is moved below user\logs, so that the arena server never logs into the real runtime.
            result.LogPathRewrites.AddRange(movedPaths);
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

            // Backup config (review 2026-10-23): SPT's profile backup writes to its `directory`, resolved against the working
            // directory, deletes the oldest folders there and restores from the newest. In every file the loader reads as
            // the backup config (IsBackupConfigFile: backup.json, backup.jsonc, ...) a directory outside user (an absolute one,
            // one that climbs out of the tree, one into the SPT_Data junctions) is replaced by the folder the shipped file names,
            // below user\profiles, and a missing member is added (SPT's code default is the empty string: the drive root), so
            // that the arena server's backups of the disposable profile never land in the normal server's pool. A runtime
            // without such a file is left as it is: the helper invents no config file.
            string configsTarget = Path.Combine(dataTarget, HelperLayout.ConfigsFolder);
            var configFiles = Directory.Exists(configsTarget) ? new List<string>(Directory.GetFiles(configsTarget)) : new List<string>();
            configFiles.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string configFile in configFiles)
            {
                string configName = Path.GetFileName(configFile);
                if (!HelperLayout.IsBackupConfigFile(configName)) continue;
                string patchedBackup;
                string backupRewrite;
                if (!HelperJsonText.TryConfineBackupDirectory(HelperFileSystem.ReadAllTextShared(configFile), out patchedBackup, out backupRewrite, out error))
                {
                    throw new HelperException(configName + ": " + error);
                }

                if (backupRewrite != null)
                {
                    box.WriteAllText(configFile, patchedBackup);
                    result.BackupDirectoryRewrites.Add(configName + ": " + backupRewrite);
                }
            }

            // Core config (review of the release 0.2.0, 2026-10-25): the session leaves out the mods the policy excludes, and
            // with SPT's shipped fixes (removeModItemsFromProfile and removeInvalidTradersFromProfile both false) an item,
            // mail attachment, suit or trader record of such a mod in the profile makes the server mark the whole profile
            // invalid, so the session's game start is refused. The copy switches SPT's own clean-up on: it deletes those
            // entries from the disposable profile copy, which is discarded with the session. Every file the loader reads as
            // the core config is handled; a runtime without one is left as it is.
            foreach (string configFile in configFiles)
            {
                string configName = Path.GetFileName(configFile);
                if (!HelperLayout.IsCoreConfigFile(configName)) continue;
                string patchedCore;
                List<string> coreChanges;
                if (!HelperJsonText.TryEnableProfileCleanup(HelperFileSystem.ReadAllTextShared(configFile), out patchedCore, out coreChanges, out error))
                {
                    throw new HelperException(configName + ": " + error);
                }

                if (coreChanges.Count > 0)
                {
                    box.WriteAllText(configFile, patchedCore);
                    result.ProfileCleanupChanges.Add(configName + ": " + string.Join(", ", coreChanges.ToArray()));
                }
            }

            // user\mods: a real copy of each allowed server mod (not a junction), so that what a mod
            // writes beside its own files, found through its assembly location, the working directory
            // or the exe's folder (AppContext.BaseDirectory, which is the tree because the server is
            // started from the copy of the exe above), stays in the disposable tree. The SPT_Data
            // folders that are junctions are the exception: they are shared with the installation,
            // and so are folders inside a mod that hold only asset bundles (CopyModDirectory).
            string userSource = Path.Combine(source, HelperLayout.UserFolder);
            string userTarget = Path.Combine(tree, HelperLayout.UserFolder);
            string modsTarget = Path.Combine(userTarget, HelperLayout.ModsFolder);
            box.CreateDirectory(modsTarget);
            foreach (HelperServerMod mod in request.ServerMods)
            {
                if (!Directory.Exists(mod.Folder)) throw new HelperException("the server mod '" + mod.Guid + "' has no folder at " + mod.Folder);
                box.CopyModDirectory(mod.Folder, Path.Combine(modsTarget, mod.FolderName));
                result.Mods.Add(mod.FolderName);
            }

            // user\patchers\<ModGuid>: SPT 4.1.6 loads the server prepatch (enum extension) definitions of a mod
            // that declares HasPrepatcher from there, before it loads any mod, and refuses to load the mods when
            // the folder of such a mod is missing. Exactly the folders of the listed mods are copied (a copy, like
            // the mods): the prepatch pass applies EVERY folder it finds in user\patchers, so a folder of a mod the
            // policy left out must not come along. A listed mod without a folder here has no prepatcher.
            string patchersSource = Path.Combine(userSource, HelperLayout.PatchersFolder);
            string patchersTarget = Path.Combine(userTarget, HelperLayout.PatchersFolder);
            var patcherGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HelperServerMod mod in request.ServerMods)
            {
                string folder = mod.PatcherFolderName;
                if (folder == null)
                {
                    if (log != null) log("the server mod GUID '" + mod.Guid + "' is not a plain folder name, so no prepatch folder is looked up for it");
                    continue;
                }

                if (!patcherGuids.Add(folder)) continue;
                string patcherSource = Path.Combine(patchersSource, folder);
                if (!Directory.Exists(patcherSource)) continue;
                box.CopyDirectory(patcherSource, Path.Combine(patchersTarget, folder));
                result.Patchers.Add(folder);
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

            // The bundle hash cache: its keys are relative paths (user/mods/<mod>/...) and the linked
            // bundle folders keep the installation's files, so carrying it over spares the server
            // from hashing every bundle again on the first start of a fresh tree.
            string bundleCache = Path.Combine(Path.Combine(userSource, HelperLayout.CacheFolder), HelperLayout.BundleHashCacheFileName);
            if (File.Exists(bundleCache))
                box.CopyFile(bundleCache, Path.Combine(Path.Combine(userTarget, HelperLayout.CacheFolder), HelperLayout.BundleHashCacheFileName));

            // Only the enabled, server-issued reward delegation travels with the disposable
            // profile. Ordinary enrollment, payment history and normal profiles never come back.
            HelperRewardTransfer.CopyHandoff(source, request.ProfileId, tree);

            // The marker commits the private tree. Optional reward control binds to this exact marker.
            HelperMarker marker = HelperMarker.Create(nowUtc, request.ProfileId, arenaPort, source, request.PolicyHash, helperVersion, result.ServerExe);
            marker.VersionPlan = request.VersionPlan;
            marker.RewardControlId = request.RewardControlId;
            box.WriteAllText(Path.Combine(tree, ArenaSessionContract.MarkerFileName), marker.ToJson());
            HelperRewardControlTransfer.TryCopy(source, request.ProfileId, request.RewardControlId, tree, marker, nowUtc, log);

            result.FilesCopied = box.FilesCopied;
            return result;
        }

        /// <summary>
        /// Makes the tree root ready for a new build: the marker goes first (a half-cleaned tree must
        /// not look finished), then everything except the plain files that are server binaries of the
        /// runtime being copied now. A tree that is a link or a plain file is removed as that; a
        /// folder or link inside it is removed without entering the link.
        /// </summary>
        private static void PrepareTree(HelperSandbox box, string tree, List<string> binaries)
        {
            FileAttributes attributes;
            if (HelperFileSystem.TryGetAttributes(tree, out attributes))
            {
                if (HelperPathSafety.IsReparsePoint(attributes) || (attributes & FileAttributes.Directory) == 0)
                {
                    box.DeleteTree(tree);
                }
                else
                {
                    box.DeleteFile(Path.Combine(tree, ArenaSessionContract.MarkerFileName));
                    foreach (string entry in Directory.GetFileSystemEntries(tree))
                    {
                        FileAttributes entryAttributes;
                        bool plainFile = HelperFileSystem.TryGetAttributes(entry, out entryAttributes)
                            && (entryAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
                        if (HelperServerBinaries.KeepsTreeEntry(Path.GetFileName(entry), plainFile, binaries)) continue;
                        box.DeleteTree(entry);
                    }
                }
            }

            box.CreateDirectory(tree);
        }

        private static void CopyIfExists(HelperSandbox box, string source, string destination)
        {
            if (Directory.Exists(source)) box.CopyDirectory(source, destination);
        }
    }

    /// <summary>
    /// The file work behind <see cref="HelperAcknowledgement"/> (docs/ARENA-SESSION-20261021.md, section 6): removing
    /// the acknowledgement an earlier request of the same file name left behind, and writing the new one atomically.
    /// The acknowledgement sits beside the request file, wherever the client put it, so it does not go through a
    /// <see cref="HelperSandbox"/> (which is bound to the session runtime root); it only ever touches the three
    /// names <see cref="HelperAcknowledgement"/> derives from the request path.
    /// </summary>
    public static class HelperAcknowledgementFile
    {
        private const int Attempts = 5;
        private const int DelayMilliseconds = 100;

        /// <summary>
        /// Deletes <c>&lt;request&gt;.accepted</c> from an earlier request, plus the temporary files a helper that
        /// died while writing it left. Done before the new request is even read: an acknowledgement that survived
        /// would tell the client to close the game for a helper that has not taken anything over. Nothing there is
        /// success. False, with the reason in <paramref name="problem"/>, when the stale file could not be removed;
        /// it never throws.
        /// </summary>
        public static bool DiscardStale(string requestPath, out string problem)
        {
            problem = null;
            string request;
            if (!HelperPathSafety.TryNormalize(requestPath, out request))
            {
                problem = "the request path is not an absolute path on a drive: " + requestPath;
                return false;
            }

            try
            {
                RemoveLeftoverTemporaries(request);

                string path = HelperAcknowledgement.GetPath(request);
                FileAttributes attributes;
                if (!HelperFileSystem.TryGetAttributes(path, out attributes)) return true;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    problem = "'" + path + "' is a folder";
                    return false;
                }

                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                        File.Delete(path);
                        return true;
                    }
                    catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < Attempts)
                    {
                        Thread.Sleep(DelayMilliseconds);
                    }
                }
            }
            catch (Exception ex)
            {
                problem = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Writes the acknowledgement of <paramref name="requestPath"/> and returns its path. The content goes to a
        /// temporary file of its own first (flushed to disk, closed), and that file is then moved onto
        /// <c>&lt;request&gt;.accepted</c> in one step: a client that polls for the final name sees either no file or
        /// the complete one, never half of it. <paramref name="beforePublish"/> (tests) is called with the temporary
        /// file's path after it is complete and before it is moved. Throws a <see cref="HelperException"/> when the
        /// file cannot be published; the temporary file is removed then.
        /// </summary>
        public static string Write(string requestPath, int helperPid, DateTime acceptedUtc, Action<string> beforePublish)
        {
            string request;
            if (!HelperPathSafety.TryNormalize(requestPath, out request))
            {
                throw new HelperException("the request path is not an absolute path on a drive: " + requestPath);
            }

            string path = HelperAcknowledgement.GetPath(request);
            string temporary = HelperAcknowledgement.GetTempPath(request, helperPid);
            byte[] bytes = new UTF8Encoding(false).GetBytes(HelperAcknowledgement.BuildContent(helperPid, acceptedUtc));
            try
            {
                // Opened without following a link: a link planted under the temporary name is refused, not written through.
                using (FileStream stream = HelperNative.OpenFileForWriting(temporary, true, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (beforePublish != null) beforePublish(temporary);
                Publish(temporary, path);
                return path;
            }
            catch (Exception ex)
            {
                TryDelete(temporary);
                if (ex is HelperException) throw;
                throw new HelperException("cannot write the acknowledgement '" + path + "': " + ex.Message, ex);
            }
        }

        // Moves the finished temporary file onto the final name. An existing file (a stale one that
        // DiscardStale could not remove, say) is replaced in the same step; a scanner that has the new file open for
        // a moment is waited out.
        private static void Publish(string temporary, string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (HelperFileSystem.PathExists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < Attempts)
                {
                    Thread.Sleep(DelayMilliseconds);
                }
            }
        }

        private static void RemoveLeftoverTemporaries(string request)
        {
            string directory = Path.GetDirectoryName(request);
            string requestName = Path.GetFileName(request);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            foreach (string candidate in Directory.GetFiles(directory, requestName + HelperAcknowledgement.FileSuffix + ".*"))
            {
                if (!HelperAcknowledgement.IsTempName(requestName, Path.GetFileName(candidate))) continue;
                TryDelete(candidate);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // A leftover temporary file is harmless: the next DiscardStale removes it.
            }
        }
    }
}
