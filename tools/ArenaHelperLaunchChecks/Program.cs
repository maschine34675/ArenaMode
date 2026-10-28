using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArenaMode.Session;
using ArenaMode.Shared.Session;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "drain-probe") return DrainProbe.Run(args).GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "child")
        {
            File.WriteAllText(args[1], ArenaMiniJson.Write(args.Skip(2).ToArray()));
            Thread.Sleep(10000);
            return 0;
        }
        string root = Path.Combine(Path.GetTempPath(), "Arena launch ü " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int failures = 0;
        try
        {
            string exe = Path.Combine(root, "helper ü.exe");
            File.Copy(typeof(Program).Assembly.Location, exe);
            try
            {
                int? exit;
                ArenaHelperProcess.Start(Path.Combine(root, "missing.exe"), new string[0], root, out exit);
                throw new Exception("missing exe accepted");
            }
            catch (Win32Exception) { Console.WriteLine("PASS missing-executable"); }

            var attributes = new SecurityAttributes { Size = Marshal.SizeOf(typeof(SecurityAttributes)), Inherit = true };
            IntPtr read, write;
            if (!CreatePipe(out read, out write, ref attributes, 0)) throw new Win32Exception();
            Process child = null;
            try
            {
                if (!SetHandleInformation(read, 1, 0)) throw new Win32Exception();
                string ready = Path.Combine(root, "ready.json");
                string[] expected = { "space and ü", "quote\"here", "trailing\\", "", "a&b%PATH%", "\\\\server\\a b\\" };
                int? exit;
                int pid = ArenaHelperProcess.Start(exe, new[] { "child", ready }.Concat(expected).ToArray(), root, out exit);
                child = Process.GetProcessById(pid);
                CloseHandle(write); write = IntPtr.Zero;
                var eof = Task.Run(() =>
                {
                    uint count;
                    bool success = ReadFile(read, new byte[1], 1, out count, IntPtr.Zero);
                    int error = Marshal.GetLastWin32Error();
                    return count == 0 && (success || error == 109 /* ERROR_BROKEN_PIPE */);
                });
                bool noInheritedPipe = eof.Wait(1500) && eof.Result && !child.HasExited;
                Console.WriteLine((noInheritedPipe ? "PASS " : "FAIL ") + "no-inherited-pipe");
                if (!noInheritedPipe) failures++;
                if (!SpinWait.SpinUntil(() => File.Exists(ready) && new FileInfo(ready).Length > 0, 3000)) throw new Exception("child did not publish arguments");
                object value; string error;
                if (!ArenaMiniJson.TryParse(File.ReadAllText(ready), out value, out error)) throw new Exception(error);
                var actual = ArenaMiniJson.AsArray(value);
                if (!expected.SequenceEqual(actual.Cast<string>()) || exit.HasValue) throw new Exception("arguments or child lifetime differ");
                Console.WriteLine("PASS exact-arguments-unicode-and-spaces");
                child.Kill(); child.WaitForExit(3000);
                if (!eof.Wait(3000)) throw new Exception("pipe reader did not finish after child exit");
            }
            finally
            {
                if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); } child.Dispose(); }
                if (write != IntPtr.Zero) CloseHandle(write);
                CloseHandle(read);
            }
        }
        catch (Exception ex) { Console.WriteLine("FAIL harness: " + ex); failures++; }
        finally { Directory.Delete(root, true); }
        return failures == 0 ? 0 : 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Size; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr handle, byte[] buffer, uint size, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
