using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ArenaStartChecks")]

namespace ArenaStart;

public sealed record CopyPlan(string Source, string Destination, List<string> Files, long Bytes,
    bool ShareAssets = false, long SharedBytes = 0, bool Diagnostics = false)
{
    public long PrivateBytes => Bytes - SharedBytes;
}
public sealed record OwnedFile(string Path, long Length, string Sha256, bool Shared = false);
public sealed record Manifest(int Schema, string Root, string Source, List<OwnedFile> Files,
    List<OwnedFile>? SourceBindings = null, bool Diagnostics = false);

public static class EnvironmentCopy
{
    public const string ManifestPath = ".arena-start/manifest.json";
    public const string AccountPath = ".arena-start/account.json";
    public const string NativeLoaderConfig = """
        [Chainloader]
        HideManagerGameObject = true
        """;
    public const string NativeLogConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <variable name="clientId" value="undefined" />
          <variable name="gameVersion" value="undefined" />
          <targets>
            <target name="file" xsi:type="File" fileName="Logs/log_${var:clientId}/${var:clientId} traces.log"
                    layout="${longdate}|${var:gameVersion}|${level}|${logger}|${message} ${exception:format=tostring}" />
          </targets>
          <rules>
            <logger name="application" minlevel="Debug" writeTo="file" />
            <logger name="backend" minlevel="Info" writeTo="file" />
            <logger name="*" minlevel="Error" writeTo="file" />
          </rules>
        </nlog>
        """;
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    internal static readonly string[] CorePlugins =
        ["spt-common.dll", "spt-core.dll", "spt-custom.dll", "spt-debugging.dll", "spt-reflection.dll", "spt-singleplayer.dll"];
    internal static readonly string[] RuntimeFiles = [
        "0Harmony.dll", "argon2_sharp_core.dll", "Argon2Sharp.dll", "AsmResolver.dll", "AsmResolver.DotNet.dll",
        "AsmResolver.PE.dll", "AsmResolver.PE.File.dll", "FastCloner.dll", "JetBrains.Annotations.dll", "Mono.Cecil.dll",
        "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll", "MonoMod.Backports.dll", "MonoMod.Core.dll",
        "MonoMod.Iced.dll", "MonoMod.ILHelpers.dll", "MonoMod.RuntimeDetour.dll", "MonoMod.Utils.dll", "MudBlazor.dll",
        "SemanticVersioning.dll", "Spectre.Console.Ansi.dll", "Spectre.Console.Cli.dll", "Spectre.Console.dll",
        "SPT.Launcher.exe", "SPT.Server.exe", "SPT.Server.dll", "SPT.Server.deps.json", "SPT.Server.runtimeconfig.json",
        "SPTarkov.Common.dll", "SPTarkov.DI.dll", "SPTarkov.Reflection.dll", "SPTarkov.Server.Assets.dll",
        "SPTarkov.Server.Core.dll", "SPTarkov.Server.Web.dll", "System.IO.Hashing.dll", "Utf8StringInterpolation.dll",
        "ZLinq.dll", "ZLogger.dll", "sptLogger.json"
    ];

    public static string Root(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!))
            throw new InvalidOperationException("A drive root is not an Arena directory.");
        AssertAncestors(full);
        return full;
    }

    // Reject junctions/symlinks in every existing ancestor, including the leaf.
    public static void AssertAncestors(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Linked path refused: {current.FullName}");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Linked file refused: {path}");
    }

    public static string Inside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Split('/', '\\').Any(x => x is ".." or "." or ""))
            throw new InvalidOperationException("Invalid relative path.");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes the Arena directory.");
        AssertAncestors(path);
        return path;
    }

    public static void Disjoint(string source, string destination)
    {
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination must be separate, non-nested directories.");
    }

    public static List<string> Walk(string root, string relative)
    {
        var result = new List<string>();
        Visit(Inside(root, relative));
        return result;
        void Visit(string directory)
        {
            AssertAncestors(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"Linked entry refused: {path}");
                if ((attributes & FileAttributes.Directory) != 0) Visit(path);
                else result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
            }
        }
    }

    public static CopyPlan Plan(string source, string destination, bool shareAssets = false, bool diagnostics = false)
    {
        source = Root(source);
        destination = Root(destination);
        Disjoint(source, destination);
        if (shareAssets) SharedAssets.RequireVolume(source, destination);
        var version = FileVersionInfo.GetVersionInfo(Inside(source, "SPT_Runtime/SPT.Server.exe")).ProductVersion;
        if (version is null || !version.StartsWith("4.1.", StringComparison.Ordinal))
            throw new InvalidOperationException("This prototype requires an installed SPT 4.1 server.");
        if (shareAssets)
        {
            var assembly = Inside(source, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
            var backup = Inside(source, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak");
            if (!File.Exists(assembly) || !File.Exists(backup) || Hash(assembly) == Hash(backup))
                throw new InvalidOperationException("Start normal SPT successfully once before preparing Arena; a patched assembly and native backup are required.");
        }
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidOperationException("Destination already exists. Updates/merges are deliberately unsupported.");
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            if (!File.Exists(Inside(source, path))) throw new FileNotFoundException($"Required installed file missing: {path}");
            files.Add(path);
        }
        void AddTree(string path) { foreach (var file in Walk(source, path)) Add(file); }
        foreach (var file in new[] { "EscapeFromTarkov.exe", "UnityPlayer.dll", "winhttp.dll" }) Add(file);
        foreach (var tree in new[] { "EscapeFromTarkov_Data", "MonoBleedingEdge", "BepInEx/core", "SPT_Runtime/SPT_Data" })
            AddTree(tree);
        foreach (var file in CorePlugins) Add("BepInEx/plugins/spt/" + file);
        AddTree("BepInEx/plugins/spt/ConfigurationManager");
        Add("BepInEx/patchers/spt-prepatch.dll");
        Add("BepInEx/patchers/FixPluginTypesSerialization.dll");
        foreach (var file in RuntimeFiles) Add("SPT_Runtime/" + file);
        Add("BepInEx/plugins/maschine-Redeploy.Core.dll");
        foreach (var file in new[] { "maschine-ArenaMode.Client.dll", "maschine-ArenaMode.BigBrainCompat.dll" })
            Add("BepInEx/plugins/maschine-ArenaMode/" + file);
        foreach (var tree in new[] { "announcements", "sounds", "docs" }) AddTree("BepInEx/plugins/maschine-ArenaMode/" + tree);
        Add("SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll");
        foreach (var file in new[] { "kits.json", "ladder.json" }) Add("SPT_Runtime/user/mods/ArenaMode/config/" + file);
        if (diagnostics) Add("BepInEx/plugins/maschine-UiBridge.dll");

        // Never inherit endpoint credentials/config, native runtime caches or crash/profile material.
        files.Remove("SPT_Runtime/SPT_Data/configs/http.json");
        files.Remove("SPT_Runtime/SPT_Data/configs/backup.json");
        files.Remove("SPT_Runtime/sptLogger.json");
        files.RemoveWhere(p => p.Contains(".sync-conflict-", StringComparison.OrdinalIgnoreCase));
        var ordered = files.Order(StringComparer.Ordinal).ToList();
        SharedAssets.ValidateCatalog(ordered);
        var sharedBytes = shareAssets
            ? ordered.Where(SharedAssets.IsContent).Sum(p => new FileInfo(Inside(source, p)).Length) : 0;
        return new CopyPlan(source, destination, ordered, ordered.Sum(p => new FileInfo(Inside(source, p)).Length),
            shareAssets, sharedBytes, diagnostics);
    }

    public static void Prepare(CopyPlan plan, Action<string>? report = null)
    {
        RuntimeHost.RequireIdle();
        // Rebuild the approved selection; callers cannot inject arbitrary copy paths.
        var current = Plan(plan.Source, plan.Destination, plan.ShareAssets, plan.Diagnostics);
        if (!current.Files.SequenceEqual(plan.Files) || current.Bytes != plan.Bytes || current.SharedBytes != plan.SharedBytes)
            throw new InvalidOperationException("Source changed after planning; create a new plan.");
        var drive = new DriveInfo(Path.GetPathRoot(plan.Destination)!);
        if (drive.AvailableFreeSpace < plan.PrivateBytes + 2L * 1024 * 1024 * 1024)
            throw new IOException("Not enough space for private Arena files plus 2 GiB reserve.");
        Directory.CreateDirectory(plan.Destination);
        Directory.CreateDirectory(Inside(plan.Destination, ".arena-start"));
        using var lease = new FileStream(Inside(plan.Destination, ".arena-start/preparing"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var owned = new List<OwnedFile>();
        using var sharedLeases = new AssetLease();
        var sharedTotal = plan.ShareAssets ? plan.Files.Count(SharedAssets.IsContent) : 0;
        var privateTotal = plan.Files.Count - sharedTotal;
        var copied = 0;
        long bytes = 0;
        long nextProgress = 4L * 1024 * 1024 * 1024;
        report?.Invoke($"Hard-linked and verified source files: 0 / {sharedTotal}");
        ReportCopies();
        foreach (var relative in plan.Files)
        {
            var source = Inside(plan.Source, relative);
            var target = Inside(plan.Destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (plan.ShareAssets && SharedAssets.IsContent(relative))
            {
                var contentStream = SharedAssets.OpenReadLease(source);
                try
                {
                    SharedAssets.CreateLink(target, source);
                    var entry = new OwnedFile(relative, contentStream.Length, SharedAssets.Hash(contentStream), Shared: true);
                    sharedLeases.Add(relative, contentStream, target, entry, hashAlreadyChecked: true);
                    owned.Add(entry);
                    if (sharedLeases.Count % 500 == 0 || sharedLeases.Count == sharedTotal)
                        report?.Invoke($"Hard-linked and verified source files: {sharedLeases.Count} / {sharedTotal}");
                }
                catch { contentStream.Dispose(); throw; }
                continue;
            }
            long length = 0;
            string digest;
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    length += read;
                }
                digest = Convert.ToHexString(hash.GetHashAndReset());
            }
            owned.Add(new(relative, length, digest));
            bytes += length;
            copied++;
            if (copied % 500 == 0 || copied == privateTotal || bytes >= nextProgress)
            {
                ReportCopies();
                nextProgress = bytes + 4L * 1024 * 1024 * 1024;
            }
        }
        WriteOwned("doorstop_config.ini", """
            [General]
            enabled=true
            target_assembly=BepInEx\core\BepInEx.Preloader.dll
            redirect_output_log=false
            ignore_disable_switch=false
            [UnityMono]
            dll_search_path_override=
            debug_enabled=false
            """);
        // Deliberately use the launcher's native fixed localhost entry. Refuse any
        // existing SPT/EFT/launcher process and occupied port before starting it.
        WriteOwned("SPT_Runtime/SPT_Data/configs/http.json", JsonSerializer.Serialize(new
        {
            ip = "127.0.0.1", port = 6969, backendIp = "127.0.0.1", backendPort = 6969,
            logRequests = false, logResponseBodies = false, serverImagePathOverride = new { },
            webAuthenticationConfig = new
            {
                enabled = true, enableDefaultUser = true, allowDefaultUserFromAnyIp = false,
                requireCredentialsOnLocalhost = false,
                defaultUser = new { username = "spt", password = "spt", isAdministrator = true }
            }
        }, Json));
        WriteOwned("SPT_Runtime/SPT_Data/configs/backup.json", JsonSerializer.Serialize(new
        {
            enabled = true, maxBackups = 5, backupCooldown = 30, directory = "./user/arena-profile-backups",
            backupInterval = new { enabled = false, intervalMinutes = 120 }
        }, Json));
        WriteOwned("SPT_Runtime/sptLogger.json", JsonSerializer.Serialize(new
        {
            loggers = new object[]
            {
                new { type = "Console", logLevel = "Information", format = "%message%" },
                new { type = "File", logLevel = "Debug", format = "[%date% %time%][%level%][%logger%] %message%",
                    filePath = "./user/logs/spt/", filePattern = "arena-%DATE%.log", maxFileSizeMB = 10, maxRollingFiles = 5 }
            }
        }, Json));
        WriteOwned("NLog/NLog.config", NativeLogConfig);
        // BepInEx adds its other default settings/comments during startup. Keep
        // this file mutable, but require the SPT scene-lifetime setting below.
        WriteNew(Inside(plan.Destination, "BepInEx/config/BepInEx.cfg"), NativeLoaderConfig);
        Directory.CreateDirectory(Inside(plan.Destination, "SPT_Runtime/user/profiles"));
        RuntimeHost.RequireIdle();
        // Bind the shared asset snapshot to the game/server/patch version it was
        // prepared with. A normal installation update must not silently mix versions.
        var bindings = plan.ShareAssets
            ? owned.Where(f => IsSourceBinding(f.Path)).Select(f => f with { Shared = false }).ToList()
            : null;
        var manifest = new Manifest(plan.ShareAssets ? 2 : 1, plan.Destination, plan.Source, owned, bindings, plan.Diagnostics);
        WriteNew(Inside(plan.Destination, ManifestPath), JsonSerializer.Serialize(manifest, Json));
        // A failed copy has no valid final manifest and is never automatically resumed/deleted.
        report?.Invoke($"Environment complete: {copied} / {privateTotal} source files copied privately; " +
            $"{sharedLeases.Count} / {sharedTotal} source files shared through NTFS hardlinks. " +
            $"{plan.PrivateBytes / 1073741824d:F2} GiB copied, {plan.SharedBytes / 1073741824d:F2} GiB shared. " +
            $"Generated {owned.Count - plan.Files.Count} catalogued configuration files plus the private loader configuration and manifest. " +
            "No source player profiles, launcher settings or BepInEx configs imported.");

        void ReportCopies() => report?.Invoke($"Copied private source files: {copied} / {privateTotal}; " +
            $"{bytes / 1073741824d:F1} / {plan.PrivateBytes / 1073741824d:F1} GiB");

        void WriteOwned(string relative, string content)
        {
            var path = Inside(plan.Destination, relative);
            WriteNew(path, content);
            owned.Add(new(relative, new FileInfo(path).Length, Hash(path)));
        }
    }

    private static bool IsSourceBinding(string path) =>
        path is "EscapeFromTarkov.exe" or "UnityPlayer.dll" or "SPT_Runtime/SPT.Server.exe" or
            "SPT_Runtime/SPT.Server.dll" or "SPT_Runtime/SPT.Launcher.exe" or
            "BepInEx/plugins/spt/spt-core.dll" ||
        path.StartsWith("EscapeFromTarkov_Data/Managed/", StringComparison.Ordinal) ||
        path.StartsWith("SPT_Runtime/SPT_Data/Launcher/Patches/", StringComparison.Ordinal);

    public static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        RequireSingleLink(file.SafeFileHandle);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    internal static void RequireSingleLink(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Cannot verify file ownership.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        if (info.NumberOfLinks != 1) throw new InvalidDataException("Unexpected hardlink outside the approved shared-asset catalog.");
    }

    public static string ReadPrivate(string path)
    {
        AssertAncestors(path);
        using var stream = File.OpenRead(path);
        RequireSingleLink(stream.SafeFileHandle);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static void InspectPrivate(string path)
    {
        using var stream = File.OpenRead(path);
        RequireSingleLink(stream.SafeFileHandle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }

    public static void WriteNew(string path, string content)
    {
        AssertAncestors(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    internal static void ValidateNativeLoaderConfig(string config)
    {
        var section = "";
        var values = new List<string>();
        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            var equals = line.IndexOf('=');
            if (section == "Chainloader" && equals >= 0 && line[..equals].Trim() == "HideManagerGameObject")
                values.Add(line[(equals + 1)..].Trim());
        }
        if (values.Count != 1 || !bool.TryParse(values[0], out var hidden) || !hidden)
            throw new InvalidDataException("Private BepInEx.cfg must enable [Chainloader] HideManagerGameObject for SPT scene transitions.");
    }

    public static Manifest Verify(string root, Action<string>? report = null, SafeFileHandle? runningLease = null,
        AssetLease? assetLease = null)
    {
        using var temporaryLease = assetLease == null ? new AssetLease() : null;
        assetLease ??= temporaryLease!;
        root = Root(root);
        var manifest = JsonSerializer.Deserialize<Manifest>(ReadPrivate(Inside(root, ManifestPath)), Json)
            ?? throw new InvalidDataException("Missing copy manifest.");
        if (manifest.Schema is not (1 or 2) || !root.Equals(manifest.Root, StringComparison.OrdinalIgnoreCase) || manifest.Files.Count == 0)
            throw new InvalidDataException("Wrong manifest version/root or empty file list.");
        Disjoint(root, Root(manifest.Source));
        SharedAssets.ValidateCatalog(manifest.Files.Select(f => f.Path));
        if (manifest.Schema == 1 && manifest.Files.Any(f => f.Shared))
            throw new InvalidDataException("Shared content in a copy-only manifest.");
        if (manifest.Schema == 2)
        {
            SharedAssets.RequireVolume(manifest.Source, root);
            if (manifest.SourceBindings == null || manifest.SourceBindings.Count == 0 ||
                !manifest.SourceBindings.Select(f => f.Path).Order().SequenceEqual(
                    manifest.Files.Where(f => IsSourceBinding(f.Path)).Select(f => f.Path).Order()))
                throw new InvalidDataException("Missing source version binding.");
            var checkedBindings = 0;
            report?.Invoke($"Checking source version files: 0 / {manifest.SourceBindings.Count}");
            foreach (var binding in manifest.SourceBindings)
            {
                var path = Inside(manifest.Source, binding.Path);
                if (!File.Exists(path) || Hash(path) != binding.Sha256)
                    throw new InvalidDataException($"Source installation updated: {binding.Path}. Prepare a new Arena environment.");
                checkedBindings++;
                if (checkedBindings % 500 == 0 || checkedBindings == manifest.SourceBindings.Count)
                    report?.Invoke($"Checking source version files: {checkedBindings} / {manifest.SourceBindings.Count}");
            }
        }
        var sharedTotal = manifest.Files.Count(f => f.Shared);
        var checkedShared = 0;
        report?.Invoke($"Verified shared assets: 0 / {sharedTotal} files (no copying).");
        foreach (var file in manifest.Files.Where(f => f.Shared))
        {
            if (!SharedAssets.IsContent(file.Path))
                throw new InvalidDataException($"Non-content file marked shared: {file.Path}");
            var path = Inside(root, file.Path);
            var source = Inside(manifest.Source, file.Path);
            assetLease.Add(file.Path, SharedAssets.OpenReadLease(source), path, file);
            checkedShared++;
            if (checkedShared % 500 == 0 || checkedShared == sharedTotal)
                report?.Invoke($"Verified shared assets: {checkedShared} / {sharedTotal} files (no copying).");
        }
        // Inspect the whole environment, not just executable discovery folders.
        var allFiles = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            AssertAncestors(entry);
            if (Directory.Exists(entry)) allFiles.AddRange(Walk(root, Path.GetFileName(entry)));
            else allFiles.Add(Path.GetFileName(entry));
        }
        // Runtime-generated configs/logs/backups are mutable too: a hardlink in
        // one of those must not turn the next native save into a source write.
        var checkedPaths = 0;
        report?.Invoke($"Checking runtime file paths: 0 / {allFiles.Count}");
        foreach (var relative in allFiles)
        {
            if (relative == ".arena-start/running" && runningLease != null)
            {
                RequireSingleLink(runningLease); // Held exclusively by RuntimeHost.
            }
            else if (!assetLease.Contains(relative)) InspectPrivate(Inside(root, relative));
            checkedPaths++;
            if (checkedPaths % 500 == 0 || checkedPaths == allFiles.Count)
                report?.Invoke($"Checking runtime file paths: {checkedPaths} / {allFiles.Count}");
        }
        ValidateNativeLoaderConfig(ReadPrivate(Inside(root, "BepInEx/config/BepInEx.cfg")));
        SharedAssets.ValidateCatalog(allFiles);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedFiles = 0;
        report?.Invoke($"Checking file integrity: 0 / {manifest.Files.Count}");
        foreach (var file in manifest.Files)
        {
            if (!expected.Add(file.Path)) throw new InvalidDataException("Duplicate manifest path.");
            var path = Inside(root, file.Path);
            // Shared files were already hashed with their write/delete leases held.
            // Native launcher removes this known unused original EFT library.
            if (!file.Shared && !(file.Path == "EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll" && !File.Exists(path)))
            {
                if (!File.Exists(path) || new FileInfo(path).Length != file.Length || Hash(path) != file.Sha256)
                    throw new InvalidDataException($"Missing/changed file: {file.Path}. Prepare a fresh environment to update.");
            }
            checkedFiles++;
            if (checkedFiles % 500 == 0 || checkedFiles == manifest.Files.Count)
                report?.Invoke($"Checking file integrity: {checkedFiles} / {manifest.Files.Count}");
        }
        foreach (var file in allFiles)
        {
            if (expected.Contains(file)) continue;
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                file.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase) ||
                file.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) ||
                (file.StartsWith("SPT_Runtime/user/mods/", StringComparison.OrdinalIgnoreCase) &&
                 !file.StartsWith("SPT_Runtime/user/mods/ArenaMode/config/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Unexpected mod/executable: {file}");
        }
        ValidateProfileFiles(root);
        report?.Invoke($"Verified {checkedFiles} / {manifest.Files.Count} catalogued files " +
            $"({checkedShared} / {sharedTotal} shared assets), {checkedPaths} / {allFiles.Count} runtime file paths " +
            "and private profile boundary.");
        return manifest;
    }

    public static void ValidateProfileFiles(string root)
    {
        var directory = Inside(root, "SPT_Runtime/user/profiles");
        var profiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        var accountFile = Inside(root, AccountPath);
        if (!File.Exists(accountFile))
        {
            if (profiles.Length != 0) throw new InvalidDataException("Unowned profiles found. Importing profiles is unsupported.");
            return;
        }
        var account = JsonNode.Parse(ReadPrivate(accountFile))!;
        var id = account["profileId"]?.GetValue<string>() ?? "";
        if (id.Length != 24 || id.Any(c => !Uri.IsHexDigit(c)) || account["username"]?.GetValue<string>() != RuntimeHost.Username)
            throw new InvalidDataException("Invalid internal account receipt.");
        var expected = Inside(root, $"SPT_Runtime/user/profiles/{id}.json");
        if (profiles.Length != 1 || !profiles[0].Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the original internal Arena account is allowed. Profile imports/reset require a new environment.");
        var profile = JsonNode.Parse(ReadPrivate(expected))!;
        if (profile["info"]?["id"]?.GetValue<string>() != id ||
            profile["info"]?["username"]?.GetValue<string>() != RuntimeHost.Username)
            throw new InvalidDataException("Internal profile identity changed.");
    }
}
