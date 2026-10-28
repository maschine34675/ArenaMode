using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ArenaMode.SessionHelper;

namespace ArenaMode.Session;

/// <summary>Starts the helper without inheriting EFT sockets, pipes or file handles.</summary>
internal static class ArenaHelperProcess
{
    internal static int Start(string executable, string[] arguments, string workingDirectory, out int? exitCode)
    {
        var argv = new string[arguments.Length + 1];
        argv[0] = executable;
        Array.Copy(arguments, 0, argv, 1, arguments.Length);
        var commandLine = new StringBuilder(HelperCommandLine.BuildCommandLine(argv));
        var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)) };
        ProcessInformation process;
        // Unity/Mono Process.Start can inherit handles even without redirected standard streams.
        // Clearing their INHERIT flag inside the child cannot release references it already inherited.
        if (!CreateProcessW(executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,
            0x08000000 /* CREATE_NO_WINDOW */, IntPtr.Zero, workingDirectory, ref startup, out process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            exitCode = null;
            uint wait = WaitForSingleObject(process.Process, 0);
            if (wait == 0)
            {
                uint code;
                if (!GetExitCodeProcess(process.Process, out code)) throw new Win32Exception(Marshal.GetLastWin32Error());
                exitCode = unchecked((int)code);
            }
            else if (wait != 258 /* WAIT_TIMEOUT */) throw new Win32Exception(Marshal.GetLastWin32Error());
            return checked((int)process.ProcessId);
        }
        finally
        {
            CloseHandle(process.Thread);
            CloseHandle(process.Process);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
