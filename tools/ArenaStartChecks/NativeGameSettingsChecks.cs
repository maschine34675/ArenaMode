using System.Text;
using System.Text.Json;
using ArenaStart;

internal static class NativeGameSettingsChecks
{
    private static readonly string[] Names = ["Control.ini", "Game.ini", "Graphics.ini", "PostFx.ini", "Sound.ini"];

    internal static int Run(string scratch)
    {
        var caseRoot = Path.Combine(scratch, "native-game-settings");
        Directory.CreateDirectory(caseRoot);
        var count = 0;
        var caseNumber = 0;

        void Check(bool condition, string reason)
        {
            count++;
            if (!condition) throw new Exception("Native game settings: " + reason);
        }

        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            {
                count++;
                return;
            }
            throw new Exception("Accepted negative native game settings control: " + reason);
        }

        Manifest NewCase()
        {
            var root = Path.Combine(caseRoot, (++caseNumber).ToString());
            var source = Path.Combine(root, "source installation");
            var destination = Path.Combine(root, "private Arena");
            Directory.CreateDirectory(Path.Combine(source, NativeGameSettings.RelativeDirectory));
            Directory.CreateDirectory(destination);
            Directory.CreateDirectory(Path.Combine(destination, ".arena-start"));
            var encodings = new Encoding[]
            {
                new UTF8Encoding(true), Encoding.Unicode, new UTF8Encoding(false),
                Encoding.BigEndianUnicode, new UTF8Encoding(false)
            };
            for (var i = 0; i < Names.Length; i++)
            {
                var json = JsonSerializer.Serialize(new { fixture = Names[i], text = "äöß", values = new[] { i, i + 1 } });
                File.WriteAllBytes(Path.Combine(source, NativeGameSettings.RelativeDirectory, Names[i]),
                    [.. encodings[i].GetPreamble(), .. encodings[i].GetBytes(json)]);
            }
            return new Manifest(2, destination, source, []);
        }

        static string Source(Manifest manifest, string name) => Path.Combine(manifest.Source, NativeGameSettings.RelativeDirectory, name);
        static string Target(Manifest manifest, string name) => Path.Combine(manifest.Root, NativeGameSettings.RelativeDirectory, name);
        static Dictionary<string, byte[]> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        static bool Same(Dictionary<string, byte[]> before, string directory)
        {
            var after = Snapshot(directory);
            return before.Count == after.Count && before.All(item =>
                after.TryGetValue(item.Key, out var bytes) && bytes.SequenceEqual(item.Value));
        }

        void CheckCopied(Manifest manifest)
        {
            foreach (var name in Names)
                Check(File.ReadAllBytes(Source(manifest, name)).SequenceEqual(File.ReadAllBytes(Target(manifest, name))),
                    "bytes/BOM changed for " + name);
        }

        void CheckProgress(List<string> messages, int copied)
        {
            var actual = messages.Where(message => message.StartsWith("Copying game settings:", StringComparison.Ordinal)).ToArray();
            var expected = Enumerable.Range(0, copied + 1).Select(n => $"Copying game settings: {n} / {copied} files.");
            Check(actual.SequenceEqual(expected), "progress is not exactly zero then each successful replacement");
        }

        void RejectBeforeReplacement(Manifest manifest, string reason)
        {
            var before = Snapshot(manifest.Root);
            var messages = new List<string>();
            Reject(() => NativeGameSettings.CopyFromSource(manifest, messages.Add), reason);
            Check(Same(before, manifest.Root), "preflight failure changed private files or leaked scratch files: " + reason);
            Check(!messages.Any(message => message.StartsWith("Copying game settings:", StringComparison.Ordinal) &&
                !message.StartsWith("Copying game settings: 0 / ", StringComparison.Ordinal)),
                "preflight failure reported a successful replacement: " + reason);
        }

        var basic = NewCase();
        var original = Snapshot(basic.Source);
        var messages = new List<string>();
        NativeGameSettings.CopyFromSource(basic, messages.Add);
        CheckCopied(basic);
        CheckProgress(messages, 5);
        Check(Same(original, basic.Source), "initial import changed source settings");
        File.WriteAllText(Target(basic, "Control.ini"), """{"privateChange":true}""");
        Check(Same(original, basic.Source), "writing private settings wrote back through a link");
        messages.Clear();
        NativeGameSettings.CopyFromSource(basic, messages.Add);
        CheckCopied(basic);
        CheckProgress(messages, 5);
        Check(Same(original, basic.Source), "repeated import changed source settings");
        Check(Directory.GetFiles(basic.Root, "*", SearchOption.AllDirectories).Length == 5,
            "successful import leaked temporary files");

        var missing = NewCase();
        NativeGameSettings.CopyFromSource(missing);
        File.WriteAllText(Target(missing, "Game.ini"), """{"privateSetting":"keep"}""");
        var kept = File.ReadAllBytes(Target(missing, "Game.ini"));
        File.Delete(Source(missing, "Game.ini"));
        File.WriteAllText(Source(missing, "Unknown.ini"), """{"ignored":true}""");
        File.WriteAllText(Source(missing, "Sound.backup.ini"), """{"ignoredBackup":true}""");
        messages.Clear();
        NativeGameSettings.CopyFromSource(missing, messages.Add);
        CheckProgress(messages, 4);
        Check(File.ReadAllBytes(Target(missing, "Game.ini")).SequenceEqual(kept), "missing source removed/replaced private settings");
        Check(!File.Exists(Target(missing, "Unknown.ini")) && !File.Exists(Target(missing, "Sound.backup.ini")),
            "non-allowlisted file imported");
        foreach (var name in Names) File.Delete(Source(missing, name));
        var privateBeforeMissing = Snapshot(missing.Root);
        messages.Clear();
        NativeGameSettings.CopyFromSource(missing, messages.Add);
        CheckProgress(messages, 0);
        Check(messages.Contains("No source game settings found; keeping private settings."), "missing-source explanation absent");
        Check(Same(privateBeforeMissing, missing.Root), "zero source files changed private settings");

        foreach (var bad in new[]
        {
            "{",
            new string(' ', NativeGameSettings.MaxFileBytes + 1),
            "[]",
            "null",
            "\"not an object\""
        })
        {
            var invalid = NewCase();
            NativeGameSettings.CopyFromSource(invalid);
            // Make earlier sources visibly different so a streaming importer would
            // damage the destination before discovering the final invalid source.
            foreach (var name in Names) File.WriteAllText(Source(invalid, name), """{"newValue":true}""");
            File.WriteAllText(Source(invalid, "Sound.ini"), bad);
            RejectBeforeReplacement(invalid, "invalid/oversized final source");
        }

        var sourceLink = NewCase();
        NativeGameSettings.CopyFromSource(sourceLink);
        var sourceAlias = Path.Combine(caseRoot, "source-hardlink.ini");
        Check(NativeLinks.CreateHardLink(sourceAlias, Source(sourceLink, "Sound.ini"), IntPtr.Zero), "source hardlink fixture failed");
        RejectBeforeReplacement(sourceLink, "hard-linked source");
        File.Delete(sourceAlias);

        var targetLink = NewCase();
        NativeGameSettings.CopyFromSource(targetLink);
        var targetAlias = Path.Combine(caseRoot, "target-hardlink.ini");
        Check(NativeLinks.CreateHardLink(targetAlias, Target(targetLink, "Sound.ini"), IntPtr.Zero), "target hardlink fixture failed");
        var aliasBefore = File.ReadAllBytes(targetAlias);
        RejectBeforeReplacement(targetLink, "hard-linked target");
        Check(File.ReadAllBytes(targetAlias).SequenceEqual(aliasBefore), "linked external target was written");
        File.Delete(targetAlias);

        var sourceLocked = NewCase();
        NativeGameSettings.CopyFromSource(sourceLocked);
        using (var writer = new FileStream(Source(sourceLocked, "Sound.ini"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            RejectBeforeReplacement(sourceLocked, "source already has a writer");

        var targetLocked = NewCase();
        NativeGameSettings.CopyFromSource(targetLocked);
        var lockedBefore = Snapshot(targetLocked.Root);
        foreach (var name in Names) File.WriteAllText(Source(targetLocked, name), """{"changedBeforeTargetLock":true}""");
        messages.Clear();
        using (var locked = new FileStream(Target(targetLocked, "Sound.ini"), FileMode.Open, FileAccess.Read, FileShare.None))
            Reject(() => NativeGameSettings.CopyFromSource(targetLocked, messages.Add), "locked target");
        Check(Same(lockedBefore, targetLocked.Root), "locked final target allowed earlier replacements or leaked temporary files");
        Check(!messages.Any(message => message.StartsWith("Copying game settings:", StringComparison.Ordinal) &&
            !message.StartsWith("Copying game settings: 0 / ", StringComparison.Ordinal)), "locked target reported replacement");

        var readOnly = NewCase();
        NativeGameSettings.CopyFromSource(readOnly);
        var readOnlyPath = Target(readOnly, "Sound.ini");
        File.SetAttributes(readOnlyPath, File.GetAttributes(readOnlyPath) | FileAttributes.ReadOnly);
        try { RejectBeforeReplacement(readOnly, "read-only target"); }
        finally { File.SetAttributes(readOnlyPath, File.GetAttributes(readOnlyPath) & ~FileAttributes.ReadOnly); }

        var interrupted = NewCase();
        NativeGameSettings.CopyFromSource(interrupted);
        foreach (var name in Names) File.WriteAllText(Source(interrupted, name), """{"changedForCommitFailure":true}""");
        var interruptedSource = Snapshot(interrupted.Source);
        messages.Clear();
        var removedTemporary = false;
        var sourceStayedLeased = false;
        Reject(() => NativeGameSettings.CopyFromSource(interrupted, message =>
        {
            messages.Add(message);
            if (message != "Copying game settings: 1 / 5 files.") return;
            try { File.WriteAllText(Source(interrupted, "Sound.ini"), """{"unexpectedWrite":true}"""); }
            catch (IOException) { sourceStayedLeased = true; }
            // A real post-preflight filesystem failure: remove one queued private
            // staged file after the first successful atomic replacement.
            var queued = Directory.GetFiles(Path.Combine(interrupted.Root, NativeGameSettings.RelativeDirectory), ".arena-settings-*.tmp");
            if (queued.Length == 0) return;
            File.Delete(queued[0]);
            removedTemporary = true;
        }), "queued temporary file disappeared during commit");
        Check(removedTemporary, "commit failure injection never reached staged files");
        Check(sourceStayedLeased && Same(interruptedSource, interrupted.Source), "source capture leases were released before commit finished");
        Check(!messages.Contains("Copying game settings: 5 / 5 files."), "interrupted commit reported completion");
        Check(Directory.GetFiles(interrupted.Root, "*", SearchOption.AllDirectories).Length == Names.Length,
            "failed commit leaked temporary files or lost a target");
        foreach (var name in Names)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Target(interrupted, name)));
            Check(json.RootElement.ValueKind == JsonValueKind.Object, "failed commit left a partial target file");
        }

        foreach (var atSource in new[] { true, false })
        {
            var linkedFile = NewCase();
            NativeGameSettings.CopyFromSource(linkedFile);
            var path = atSource ? Source(linkedFile, "Sound.ini") : Target(linkedFile, "Sound.ini");
            var outside = Path.Combine(caseRoot, $"outside-file-{atSource}.ini");
            File.WriteAllBytes(outside, File.ReadAllBytes(path));
            var outsideBefore = File.ReadAllBytes(outside);
            File.Delete(path);
            File.CreateSymbolicLink(path, outside);
            RejectBeforeReplacement(linkedFile, (atSource ? "source" : "target") + " file symlink");
            Check(File.ReadAllBytes(outside).SequenceEqual(outsideBefore), "symlink target changed");
            File.Delete(path);

            var linkedDirectory = NewCase();
            NativeGameSettings.CopyFromSource(linkedDirectory);
            var directory = Path.Combine(atSource ? linkedDirectory.Source : linkedDirectory.Root, NativeGameSettings.RelativeDirectory);
            var externalDirectory = Path.Combine(caseRoot, $"outside-directory-{atSource}");
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(caseRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(externalDirectory).StartsWith(Path.GetFullPath(caseRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Directory-link fixture escaped its private scratch root.");
            Directory.Move(directory, externalDirectory);
            Directory.CreateSymbolicLink(directory, externalDirectory);
            var externalBefore = Snapshot(externalDirectory);
            // Do not traverse the deliberate directory link while taking a private
            // snapshot. Reject it, then verify its external files remained intact.
            Reject(() => NativeGameSettings.CopyFromSource(linkedDirectory), (atSource ? "source" : "target") + " directory symlink");
            Check(Same(externalBefore, externalDirectory), "linked directory contents changed");
            Directory.Delete(directory);
        }

        return count;
    }
}
