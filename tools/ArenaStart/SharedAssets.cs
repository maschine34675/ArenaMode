using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace ArenaStart;

// This is an explicit content catalog, not a generic "everything except DLLs" filter.
// No directory links, ACL/attribute changes or writable files are shared.
public static class SharedAssets
{
    public static bool IsContent(string relative)
    {
        if (relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(s => s is "" or "." or ".."))
            return false;
        const string data = "EscapeFromTarkov_Data/";
        if (!relative.StartsWith(data, StringComparison.Ordinal)) return false;
        var tail = relative[data.Length..];
        if (Regex.IsMatch(tail, @"^(sharedassets[0-9]+|resources)\.(assets(\.resS)?|resS|resource)$",
                RegexOptions.CultureInvariant)) return true;
        if (tail is "Resources/unity default resources" or "Resources/unity_builtin_extra") return true;
        var extension = Path.GetExtension(tail);
        if (tail.StartsWith("StreamingAssets/Windows/", StringComparison.Ordinal))
            return extension is ".bundle" or ""; // Extensionless Unity bundles are common.
        if (tail.StartsWith("StreamingAssets/Acoustics/", StringComparison.Ordinal))
            return extension is ".xrageo" or ".xramap";
        if (tail.StartsWith("StreamingAssets/AudioBakeData/", StringComparison.Ordinal))
            return extension == ".audiobakedata";
        if (tail.StartsWith("StreamingAssets/Culling_Data/", StringComparison.Ordinal))
            return extension == ".bytes";
        return false;
    }

    public static void RequireVolume(string source, string destination)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Shared assets require Windows NTFS.");
        var sourceDrive = new DriveInfo(Path.GetPathRoot(source)!);
        var destinationDrive = new DriveInfo(Path.GetPathRoot(destination)!);
        if (!sourceDrive.Name.Equals(destinationDrive.Name, StringComparison.OrdinalIgnoreCase) ||
            sourceDrive.DriveFormat != "NTFS")
            throw new InvalidOperationException("Arena's managed directory must be on the same NTFS volume as SPT. No full-copy fallback is performed.");
    }

    internal static void ValidateCatalog(IEnumerable<string> files)
    {
        foreach (var path in files)
        {
            // RestoreRecurse processes every *.spt-bak in the runtime, regardless
            // of patch catalog. Only the reviewed private native backup is allowed.
            if (path.EndsWith(".spt-bak", StringComparison.OrdinalIgnoreCase) &&
                path != "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak")
                throw new InvalidDataException($"Unexpected native patch backup: {path}");
            const string patchPrefix = "SPT_Runtime/SPT_Data/Launcher/Patches/";
            if (path.StartsWith(patchPrefix, StringComparison.Ordinal) &&
                path.EndsWith(".delta", StringComparison.OrdinalIgnoreCase) &&
                path != patchPrefix + "SPT-core/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.delta")
                throw new InvalidDataException($"Unreviewed native launcher patch: {path}");
        }
    }

    internal static FileStream OpenReadLease(string path) =>
        // Thousands of long-lived handles must not each retain a 64 KiB buffer.
        // SHA256.HashData supplies its own bounded read buffer.
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);

    internal static string Hash(FileStream stream)
    {
        stream.Position = 0;
        var result = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        return result;
    }

    internal static void CreateLink(string destination, string source)
    {
        if (!CreateHardLink(destination, source, IntPtr.Zero))
            throw new IOException($"Could not share asset {Path.GetFileName(destination)}.",
                new Win32Exception(Marshal.GetLastWin32Error()));
    }

    internal static bool SameFile(SafeFileHandle first, SafeFileHandle second)
    {
        var a = Info(first);
        var b = Info(second);
        return a.VolumeSerial == b.VolumeSerial && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow;
    }

    internal static FileInformation Info(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Cannot verify shared asset identity.", new Win32Exception(Marshal.GetLastWin32Error()));
        return info;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }
}

// File sharing restrictions apply through both hardlink names. Keep these
// handles until ALL clients have closed, even if the private server has failed.
// Forced process termination releases them; this is not an OS sandbox.
public sealed class AssetLease : IDisposable
{
    private readonly Dictionary<string, (FileStream Source, FileStream Target, string Path, OwnedFile Entry)> files = new(StringComparer.OrdinalIgnoreCase);
    public int Count => files.Count;

    internal void Add(string relative, FileStream source, string destination, OwnedFile expected, bool hashAlreadyChecked = false)
    {
        FileStream? target = null;
        try
        {
            target = SharedAssets.OpenReadLease(destination);
            if (files.TryGetValue(relative, out var retained))
            {
                // Reverification during rebuild can reuse a continuously held
                // read lease, but only for exactly the same paths/identity/hash.
                if (retained.Entry != expected || !string.Equals(retained.Path, destination, StringComparison.OrdinalIgnoreCase) ||
                    !SharedAssets.SameFile(source.SafeFileHandle, retained.Source.SafeFileHandle) ||
                    !SharedAssets.SameFile(target.SafeFileHandle, retained.Target.SafeFileHandle))
                    throw new InvalidDataException("Retained shared asset does not match this runtime/catalog: " + relative);
                source.Dispose();
                return;
            }
            if (!SharedAssets.SameFile(source.SafeFileHandle, target.SafeFileHandle) ||
                source.Length != expected.Length || (!hashAlreadyChecked && SharedAssets.Hash(source) != expected.Sha256))
                throw new InvalidDataException($"Shared asset identity/content changed: {relative}. Prepare a fresh environment.");
            files.Add(relative, (source, target, destination, expected));
            target = null;
        }
        catch { source.Dispose(); throw; }
        finally { target?.Dispose(); }
    }

    internal bool Contains(string relative) => files.ContainsKey(relative);

    public void Dispose()
    {
        foreach (var pair in files.Values)
        {
            pair.Target.Dispose();
            pair.Source.Dispose();
        }
        files.Clear();
    }
}
