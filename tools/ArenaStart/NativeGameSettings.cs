using System.Text;
using System.Text.Json;

namespace ArenaStart;

// SPT.Custom redirects SettingsManager's five JSON providers to this private
// working directory. These files remain mutable; never add them to the manifest
// of immutable game files or share them with the ordinary installation.
internal static class NativeGameSettings
{
    internal const string RelativeDirectory = "SPT_Runtime/user/sptSettings";
    internal const int MaxFileBytes = 1024 * 1024;
    private static readonly string[] Names =
        ["Control.ini", "Game.ini", "Graphics.ini", "PostFx.ini", "Sound.ini"];

    // RuntimeHost calls this only after Verify, under its exclusive running lease,
    // before starting any server/client. The verified source is the only import source.
    internal static void CopyFromSource(Manifest verified, Action<string>? report = null)
    {
        var source = EnvironmentCopy.Root(verified.Source);
        var root = EnvironmentCopy.Root(verified.Root);
        EnvironmentCopy.Disjoint(source, root);
        var sourceDirectory = EnvironmentCopy.Inside(source, RelativeDirectory);
        var targetDirectory = EnvironmentCopy.Inside(root, RelativeDirectory);
        if (File.Exists(sourceDirectory) || File.Exists(targetDirectory))
            throw new InvalidDataException("The game-settings path must be a directory.");

        var messages = new RuntimeReporter(report ?? (_ => { }));
        var sources = new List<FileStream>();
        var pending = new List<PendingSetting>();
        try
        {
            // Capture every available source before modifying any destination.
            // Retain read leases so a normal settings writer cannot alter this batch.
            foreach (var name in Names)
            {
                var inputPath = EnvironmentCopy.Inside(source, RelativeDirectory + "/" + name);
                if (Directory.Exists(inputPath))
                    throw new InvalidDataException($"Game settings '{name}' must be a file.");
                if (!File.Exists(inputPath)) continue; // Never delete private settings because a source is absent.
                try
                {
                    var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    sources.Add(input);
                    EnvironmentCopy.RequireSingleLink(input.SafeFileHandle);
                    if (input.Length == 0 || input.Length > MaxFileBytes)
                        throw new InvalidDataException("Expected a non-empty settings file no larger than 1 MiB.");
                    var bytes = new byte[checked((int)input.Length)];
                    input.ReadExactly(bytes);
                    // Preserve native bytes/encoding. Native readers also recognize BOMs.
                    using var buffer = new MemoryStream(bytes, writable: false);
                    using var reader = new StreamReader(buffer, new UTF8Encoding(false, true),
                        detectEncodingFromByteOrderMarks: true);
                    using var json = JsonDocument.Parse(reader.ReadToEnd(), new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
                    });
                    if (json.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Expected a JSON object.");
                    pending.Add(new(name, EnvironmentCopy.Inside(root, RelativeDirectory + "/" + name), bytes));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    JsonException or DecoderFallbackException)
                {
                    throw new InvalidDataException($"Cannot import game settings '{name}' from normal SPT: {error.Message}", error);
                }
            }

            messages.Write($"Copying game settings: 0 / {pending.Count} files.");
            if (pending.Count == 0)
            {
                messages.Write("No source game settings found; keeping private settings.");
                return;
            }
            // Check and lease all existing targets before staging/replacing any file.
            foreach (var item in pending)
            {
                if (Directory.Exists(item.Target))
                    throw new InvalidDataException($"Private game settings '{item.Name}' must be a file.");
                if (!File.Exists(item.Target)) continue;
                item.TargetLease = new FileStream(item.Target, FileMode.Open, FileAccess.Read, FileShare.None);
                EnvironmentCopy.RequireSingleLink(item.TargetLease.SafeFileHandle);
                if ((File.GetAttributes(item.Target) & FileAttributes.ReadOnly) != 0)
                    throw new InvalidDataException($"Private game settings '{item.Name}' are read-only.");
            }
            Directory.CreateDirectory(targetDirectory);
            foreach (var item in pending)
            {
                var temporary = EnvironmentCopy.Inside(root,
                    RelativeDirectory + "/.arena-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
                using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                item.Temporary = temporary; // Only own a temp file after CreateNew succeeds.
                EnvironmentCopy.RequireSingleLink(output.SafeFileHandle);
                output.Write(item.Bytes);
                output.Flush(flushToDisk: true);
            }
            var copied = 0;
            foreach (var item in pending)
            {
                item.TargetLease?.Dispose();
                item.TargetLease = null;
                EnvironmentCopy.AssertAncestors(item.Target);
                if (File.Exists(item.Target)) EnvironmentCopy.InspectPrivate(item.Target);
                // Same-volume atomic replacement; never truncate/write through a target alias.
                File.Move(item.Temporary!, item.Target, overwrite: true);
                item.Temporary = null;
                messages.Write($"Copying game settings: {++copied} / {pending.Count} files.");
            }
            messages.Write($"Copied {copied} game-settings files from normal SPT; " +
                $"{Names.Length - copied} missing source files left unchanged. No settings are written back.");
        }
        finally
        {
            foreach (var item in pending)
            {
                item.TargetLease?.Dispose();
                if (item.Temporary == null) continue;
                try { File.Delete(item.Temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    messages.Write("Could not remove a private temporary settings file: " + error.Message);
                }
            }
            foreach (var input in sources) input.Dispose();
        }
    }

    private sealed class PendingSetting(string name, string target, byte[] bytes)
    {
        internal readonly string Name = name, Target = target;
        internal readonly byte[] Bytes = bytes;
        internal FileStream? TargetLease;
        internal string? Temporary;
    }
}
