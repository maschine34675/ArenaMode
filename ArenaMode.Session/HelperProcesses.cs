// Also compiled into ArenaMode.Tests (net10.0, nullable annotations enabled; see
// ArenaSessionHelperProcessTests). This file carries none on purpose (C# 7.3 like SessionLogic.cs), hence the pragma.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ArenaMode.SessionHelper
{
    /// <summary>
    /// Process and socket helpers. Children are always started with <c>UseShellExecute = false</c>
    /// and an environment without <c>DOORSTOP_*</c> variables; an image path is read before anything
    /// is killed.
    /// </summary>
    internal static class HelperProcesses
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint ProcessSynchronize = 0x00100000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetHandleInformation(IntPtr handle, out uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder filePath, uint filePathLength, uint flags);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sort,
            int addressFamily, int tableClass, uint reserved);

        private const uint HandleFlagInherit = 1;
        private const int HighestHandleValue = 0x40000;

        // CreateFile: no access rights (attributes only), every share mode, an existing file or
        // folder, and FILE_FLAG_BACKUP_SEMANTICS so that a folder can be opened as well.
        private const uint ShareAll = 7;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;

        private const int AddressFamilyInet = 2;
        private const int AddressFamilyInet6 = 23;
        private const int TcpTableOwnerPidListener = 3;
        private const uint ErrorInsufficientBuffer = 122;
        private const uint NoError = 0;

        /// <summary>
        /// The running processes whose image is the file <paramref name="exe"/>, compared as a file (a junction or a SUBST
        /// drive gives one file several spellings). Candidates are found by process name; the caller disposes the
        /// result. Processes that cannot be inspected are not included. Never throws.
        /// </summary>
        public static List<Process> FindByImage(string exe)
        {
            var found = new List<Process>();
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(exe));
            }
            catch (Exception)
            {
                return found;
            }

            foreach (Process candidate in candidates)
            {
                bool keep = false;
                try
                {
                    if (!HasExited(candidate))
                    {
                        string image = GetImagePath(candidate);
                        keep = image != null && SameFileSystemObject(image, exe);
                    }
                }
                catch (Exception)
                {
                    keep = false;
                }

                if (keep) found.Add(candidate);
                else candidate.Dispose();
            }

            return found;
        }

        /// <summary>The running process with this id, or null when there is none (or it has exited).</summary>
        public static Process TryOpen(int processId)
        {
            Process process;
            try { process = Process.GetProcessById(processId); }
            catch (ArgumentException) { return null; }
            try
            {
                if (!HasExited(process)) return process;
                process.Dispose();
                return null;
            }
            catch
            {
                process.Dispose();
                // An unreadable writer is not an absent writer. Callers must
                // retain ownership/pid evidence and refuse copy or restart.
                throw;
            }
        }

        /// <summary>Bounded liveness query across UAC levels. Unknown access throws, never means exited.</summary>
        public static bool HasExited(Process process)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSynchronize, false, process.Id);
            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 87) return true; // The positively identified PID no longer exists.
                throw new Win32Exception(error);
            }
            try
            {
                uint wait = WaitForSingleObject(handle, 0);
                if (wait == 0) return true;
                if (wait != 258) throw new Win32Exception(Marshal.GetLastWin32Error());
                long created, exited, kernel, user;
                if (!GetProcessTimes(handle, out created, out exited, out kernel, out user))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                // Process.Start retains its original handle. If its PID was
                // recycled, the replacement cannot make that object live again.
                // Grant-bound callers additionally check their frozen start time.
                if (DateTime.FromFileTimeUtc(created) != process.StartTime.ToUniversalTime()) return true;
                wait = WaitForSingleObject(handle, 0);
                if (wait == 0) return true;
                if (wait == 258) return false;
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { CloseHandle(handle); }
        }

        /// <summary>
        /// The full image path of a process, read through <c>QueryFullProcessImageName</c> (works across
        /// 32/64 bit and for protected processes) with <c>MainModule</c> as the fallback; null when
        /// neither can be read.
        /// </summary>
        public static string GetImagePath(Process process)
        {
            int id;
            try
            {
                id = process.Id;
            }
            catch (Exception)
            {
                return null;
            }

            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, id);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var builder = new StringBuilder(1024);
                    int size = builder.Capacity;
                    if (QueryFullProcessImageName(handle, 0, builder, ref size)) return builder.ToString(0, size);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            try
            {
                return process.MainModule != null ? process.MainModule.FileName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// True when two paths name the same existing file or folder. A path is not an identity: a
        /// directory junction or a SUBST drive gives one file several spellings, and the two ways the
        /// helper learns a path disagree about them (the image of a running process is the resolved
        /// path, the path in a request is the one the game was started from). Equal spellings count
        /// as the same without touching the disk; otherwise both are resolved to their final path.
        /// False when either cannot be resolved, e.g. because it no longer exists.
        /// </summary>
        public static bool SameFileSystemObject(string a, string b)
        {
            if (HelperPathSafety.PathsEqual(a, b)) return true;
            string resolvedA;
            string resolvedB;
            return TryResolveFinalPath(a, out resolvedA) && TryResolveFinalPath(b, out resolvedB)
                && HelperPathSafety.PathsEqual(resolvedA, resolvedB);
        }

        /// <summary>
        /// The path of an existing file or folder after every junction, symbolic link and SUBST drive on
        /// the way is followed (<c>GetFinalPathNameByHandle</c>), without the <c>\\?\</c> prefix. False for
        /// a path that does not exist, cannot be opened, or resolves to something that is not a drive path.
        /// </summary>
        public static bool TryResolveFinalPath(string path, out string resolved)
        {
            resolved = null;
            if (!HelperPathSafety.IsDriveAbsolute(path)) return false;
            try
            {
                using (SafeFileHandle handle = CreateFile(path, 0, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return false;
                    var builder = new StringBuilder(1024);
                    uint length = GetFinalPathNameByHandle(handle, builder, (uint)builder.Capacity, 0);
                    if (length == 0) return false;
                    if (length >= builder.Capacity)
                    {
                        builder = new StringBuilder((int)length + 1);
                        length = GetFinalPathNameByHandle(handle, builder, (uint)builder.Capacity, 0);
                        if (length == 0 || length >= builder.Capacity) return false;
                    }

                    string text = builder.ToString();
                    const string prefix = @"\\?\";
                    if (text.StartsWith(prefix, StringComparison.Ordinal)) text = text.Substring(prefix.Length);
                    if (!HelperPathSafety.IsDriveAbsolute(text)) return false;
                    resolved = text;
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryGetStartTimeUtc(Process process, out DateTime startUtc)
        {
            startUtc = DateTime.MinValue;
            try
            {
                startUtc = process.StartTime.ToUniversalTime();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>True once the process has exited (or is gone), false on timeout.</summary>
        public static bool WaitForExit(Process process, int milliseconds)
        {
            try
            {
                return process.WaitForExit(milliseconds);
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        /// <summary>True when something accepts a TCP connection on host:port within the timeout.</summary>
        public static bool CanConnect(string host, int port, int timeoutMilliseconds)
        {
            IPAddress connected;
            return CanConnect(host, port, timeoutMilliseconds, out connected);
        }

        /// <summary>
        /// <see cref="CanConnect(string, int, int)"/> that also says which address the connection reached (the
        /// remote address of the connected socket: <c>127.0.0.1</c> or <c>::1</c> for a loopback probe), so that the
        /// owner of the listener can be looked up for that address and no other. Null when nothing connected. A host
        /// that is an IP address is probed with a socket of its own address family: a <see cref="TcpClient"/> made
        /// without one is an IPv4 socket on .NET Framework (the runtime of the helper), which cannot reach an IPv6
        /// literal such as <c>::1</c>; a host name is probed as before.
        /// </summary>
        public static bool CanConnect(string host, int port, int timeoutMilliseconds, out IPAddress connectedAddress)
        {
            connectedAddress = null;
            try
            {
                using (TcpClient client = NewProbeClient(host))
                {
                    IAsyncResult result = client.BeginConnect(host, port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(timeoutMilliseconds) || !client.Connected) return false;
                    client.EndConnect(result);
                    var remote = client.Client.RemoteEndPoint as IPEndPoint;
                    connectedAddress = remote == null ? null : UnmapIPv4(remote.Address);
                    return true;
                }
            }
            catch (Exception)
            {
                connectedAddress = null;
                return false;
            }
        }

        private static TcpClient NewProbeClient(string host)
        {
            IPAddress literal;
            return IPAddress.TryParse(host, out literal) ? new TcpClient(literal.AddressFamily) : new TcpClient();
        }

        /// <summary>
        /// The ids of the processes that listen on this TCP port, on any local address (IPv4 and IPv6).
        /// An empty list means that none was found; null means that the system table could not be read.
        /// A connect probe says that something answers, this says who: the helper needs both to tell the
        /// server it started from a program that happens to hold the port.
        /// </summary>
        public static List<int> GetListenerProcessIds(int port)
        {
            var owners = new List<int>();
            bool readAny = false;
            try
            {
                readAny |= ReadListeners(AddressFamilyInet, port, null, owners);
                readAny |= ReadListeners(AddressFamilyInet6, port, null, owners);
            }
            catch (Exception)
            {
                return null;
            }

            return readAny ? owners : null;
        }

        /// <summary>
        /// The ids of the processes that listen on this TCP port for connections to <paramref name="address"/>: a
        /// listener bound to exactly that address, or to the wildcard of its family (<c>0.0.0.0</c>, or <c>::</c>,
        /// which may also take IPv4 connections when the socket is dual-stack). A listener of another process on a
        /// different local address of the same port (<c>127.0.0.2</c> beside <c>127.0.0.1</c>) is not the one a
        /// connection to <paramref name="address"/> reaches and is not listed. Empty means that no such listener was
        /// found; null means that the system table of the address's own family could not be read (for an IPv4
        /// address the IPv6 table is read as well but only adds the wildcard listeners it holds, so it is not
        /// required). Without an address this is <see cref="GetListenerProcessIds(int)"/>.
        /// </summary>
        public static List<int> GetListenerProcessIds(int port, IPAddress address)
        {
            if (address == null) return GetListenerProcessIds(port);

            var owners = new List<int>();
            try
            {
                address = UnmapIPv4(address);
                bool v4 = address.AddressFamily == AddressFamily.InterNetwork;
                if (!ReadListeners(v4 ? AddressFamilyInet : AddressFamilyInet6, port, address, owners)) return null;
                if (v4) ReadListeners(AddressFamilyInet6, port, address, owners);
            }
            catch (Exception)
            {
                return null;
            }

            return owners;
        }

        /// <summary>
        /// The plain IPv4 address for an IPv4-mapped IPv6 one (<c>::ffff:127.0.0.1</c>), anything else unchanged. A
        /// dual-stack client socket (the default one of .NET Core and later) reports the address of an IPv4 server in
        /// that form, while the system's TCP table lists the listener under its IPv4 address.
        /// </summary>
        internal static IPAddress UnmapIPv4(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetworkV6) return address;
            byte[] bytes = address.GetAddressBytes();
            for (int i = 0; i < 10; i++)
            {
                if (bytes[i] != 0) return address;
            }

            if (bytes[10] != 0xFF || bytes[11] != 0xFF) return address;
            return new IPAddress(new[] { bytes[12], bytes[13], bytes[14], bytes[15] });
        }

        /// <summary>
        /// True when a socket listening on <paramref name="listening"/> accepts a connection made to
        /// <paramref name="connected"/>: the same address, or the wildcard of the listener's family.
        /// </summary>
        internal static bool ListenerServes(IPAddress listening, IPAddress connected)
        {
            if (listening == null || connected == null) return false;
            if (listening.Equals(connected)) return true;
            if (listening.Equals(IPAddress.Any)) return connected.AddressFamily == AddressFamily.InterNetwork;
            if (listening.Equals(IPAddress.IPv6Any)) return true; // an IPv6 listener on [::] can be dual-stack: it may take IPv4 too
            return false;
        }

        private static bool ReadListeners(int addressFamily, int port, IPAddress serves, List<int> owners)
        {
            // MIB_TCPTABLE_OWNER_PID: a DWORD count, then rows. IPv4 row: state, local address, local
            // port, remote address, remote port, pid (24 bytes). IPv6 row: local address (16 bytes),
            // scope id, local port, remote address (16 bytes), scope id, remote port, state, pid (56
            // bytes). A port is a network-order 16-bit value in the low half of its DWORD.
            bool v4 = addressFamily == AddressFamilyInet;
            int rowSize = v4 ? 24 : 56;
            int addressOffset = v4 ? 4 : 0;
            int addressLength = v4 ? 4 : 16;
            int scopeOffset = 16;
            int portOffset = v4 ? 8 : 20;
            int pidOffset = v4 ? 20 : 52;

            int size = 0;
            uint status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);
            for (int attempt = 0; attempt < 5 && status == ErrorInsufficientBuffer; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    status = GetExtendedTcpTable(buffer, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);
                    if (status != NoError) continue;

                    int count = Marshal.ReadInt32(buffer);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr row = IntPtr.Add(buffer, 4 + i * rowSize);
                        int raw = Marshal.ReadInt32(row, portOffset);
                        int localPort = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                        if (localPort != port) continue;
                        if (serves != null)
                        {
                            var bytes = new byte[addressLength];
                            Marshal.Copy(IntPtr.Add(row, addressOffset), bytes, 0, addressLength);
                            IPAddress local = v4 ? new IPAddress(bytes) : new IPAddress(bytes, Marshal.ReadInt32(row, scopeOffset));
                            if (!ListenerServes(local, serves)) continue;
                        }

                        int owner = Marshal.ReadInt32(row, pidOffset);
                        if (!owners.Contains(owner)) owners.Add(owner);
                    }

                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return false;
        }

        /// <summary>
        /// True when nothing listens on the loopback port and it can be bound. A foreign listener on
        /// every interface is caught by the connect probe, a bound-but-not-listening one by the bind.
        /// </summary>
        public static bool IsPortFree(int port)
        {
            return IsPortFree("127.0.0.1", port);
        }

        /// <summary>
        /// <see cref="IsPortFree(int)"/> for the address a server will be waited for on: nothing may accept a
        /// connection on <paramref name="host"/>:<paramref name="port"/>, and the port must be bindable on that
        /// address (a host that is not an IP address is probed through the IPv4 loopback address). An address the
        /// machine does not have cannot be bound, so it counts as not free.
        /// </summary>
        public static bool IsPortFree(string host, int port)
        {
            if (CanConnect(host, port, 300)) return false;
            IPAddress address;
            if (!IPAddress.TryParse(host, out address)) address = IPAddress.Loopback;
            try
            {
                var listener = new TcpListener(address, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// Starts a child without the shell and without any inherited DOORSTOP_ variable. A console
        /// program gets its own console window because the helper itself has none.
        /// </summary>
        public static Process Start(string executable, string arguments, string workingDirectory)
        {
            DetachInheritedHandles();
            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            };
            HelperEnvironment.RemoveDoorstopVariables(info.EnvironmentVariables);
            return Process.Start(info);
        }

        /// <summary>
        /// Makes every handle of this process non-inheritable before a child is created. The helper is
        /// started by the game and so owns copies of whatever inheritable handles the game had (sockets,
        /// pipes, the listener of a test bridge); <c>Process.Start</c> passes all of them on, and the
        /// server and the next game, which both outlive the helper, would then keep them open for their
        /// whole lifetime: a port that stays bound, a redirected output that never ends. The helper
        /// passes nothing to its children on purpose, so it only needs to clear the flag. Handle values
        /// are small multiples of four; the whole range is probed, which takes a few milliseconds.
        /// </summary>
        private static void DetachInheritedHandles()
        {
            for (int value = 4; value < HighestHandleValue; value += 4)
            {
                var handle = new IntPtr(value);
                uint flags;
                if (GetHandleInformation(handle, out flags) && (flags & HandleFlagInherit) != 0)
                {
                    SetHandleInformation(handle, HandleFlagInherit, 0);
                }
            }
        }
    }
}
