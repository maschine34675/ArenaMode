// Linked into the offline helper tests. Keep this file compatible with net472/C# 7.3.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    /// <summary>
    /// Carries only server-issued reward metadata across the disposable-session boundary.
    /// The normal profile is never a destination. Receipt signatures, eligibility and payment
    /// remain the normal server's responsibility; this helper never interprets a reward amount.
    /// </summary>
    public static class HelperRewardTransfer
    {
        public const string OriginFileName = "arena-rewards-origin.json";
        public const string OutboxFolderName = "arena-rewards-outbox";
        public const int MaxReceiptBytes = 4 * 1024 * 1024;
        public const int MaxReceipts = 4096;
        private const int MaxAuthorityBytes = 16 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        /// <summary>Copies an explicitly enabled handoff, never enrollment/journal/profile state.</summary>
        public static bool CopyHandoff(string sourceRuntime, string profileId, string tree)
        {
            if (!HelperRequest.IsValidProfileId(profileId)) throw new HelperException("Arena reward profile identity is invalid.");
            // An installation that has never enabled rewards keeps the original helper behavior,
            // including its existing path policy. Reward authority, when present, is stricter.
            if (!Exists(Path.Combine(sourceRuntime, "user", "arena-rewards", profileId, "handoff.json"))) return false;
            RequireProfileId(profileId);
            string source = RequireRoot(sourceRuntime);
            string destinationRoot = RequireRoot(tree);
            RequireSeparateRoots(source, destinationRoot);
            string path = Path.Combine(source, "user", "arena-rewards", profileId, "handoff.json");
            RequirePlainPath(path, false);
            if (!Exists(path)) return false;
            byte[] bytes = ReadBounded(path, MaxAuthorityBytes);
            Dictionary<string, object> handoff = ParseHandoff(bytes, source, profileId);
            if (!ArenaMiniJson.GetBool(handoff, "enabled", false)) return false;
            PublishImmutable(Path.Combine(destinationRoot, OriginFileName), bytes, MaxAuthorityBytes);
            return true;
        }

        /// <summary>
        /// Called only after the helper has proved the private server is stopped. Each immutable
        /// result is published and read back before removing its source. A crash between those
        /// steps leaves a byte-identical retry. A failure leaves all not-yet-returned results intact.
        /// </summary>
        public static int ReturnPending(string runtimeRoot, string callerRuntime, Action<string> log = null,
            Action<string> afterPublish = null)
        {
            if (HelperOfflineSession.HasOfflineTree(HelperServerTree.GetTreePath(runtimeRoot)))
                return 0; // native ordinary startup imports signed offline state and receipts together
            if (!Exists(Path.Combine(HelperServerTree.GetTreePath(runtimeRoot), "user", OutboxFolderName))) return 0;
            string tree = RequireRoot(HelperServerTree.GetTreePath(RequireRoot(runtimeRoot)));
            List<string> receipts = PublishedResults(tree);
            if (receipts.Count == 0) return 0;

            // The marker identifies the old session even when the next enter selected another profile.
            string markerPath = Path.Combine(tree, ArenaSessionContract.MarkerFileName);
            HelperMarker marker;
            string error;
            if (!HelperMarker.TryParse(Utf8.GetString(ReadBounded(markerPath, MaxAuthorityBytes)), out marker, out error)
                || marker == null || !marker.RunsFromTree || !HelperRequest.IsValidProfileId(marker.ProfileId)
                || !HelperPathSafety.PathsEqual(marker.ServerExe, Path.Combine(tree, HelperLayout.ServerExeName)))
                throw new HelperException("Arena reward results remain in the private session: its marker is not valid.");

            string source = RequireRoot(marker.SourceRuntime);
            RequireSeparateRoots(source, tree);
            if (callerRuntime != null && !HelperPathSafety.PathsEqual(source, callerRuntime))
                throw new HelperException("Arena reward results belong to another installation; the old private tree was retained.");
            // An origin is mandatory even for a stopped tree. A marker alone cannot choose an inbox.
            Dictionary<string, object> origin = ParseHandoff(ReadBounded(Path.Combine(tree, OriginFileName), MaxAuthorityBytes), source, marker.ProfileId);
            RequirePlainPath(Path.Combine(source, ArenaSessionContract.MarkerFileName), false);
            if (Exists(Path.Combine(source, ArenaSessionContract.MarkerFileName)))
                throw new HelperException("Arena reward destination is not an ordinary runtime.");

            string authority = Path.Combine(source, "user", "arena-rewards", marker.ProfileId);
            // Do not recreate missing origin authority. Such a session needs manual recovery,
            // not a new directory that could silently enroll a different installation/profile.
            RequirePlainPath(authority, true);
            Dictionary<string, object> current = ParseHandoff(ReadBounded(Path.Combine(authority, "handoff.json"), MaxAuthorityBytes), source, marker.ProfileId);
            RequireSameDelegation(origin, current);
            string inbox = Path.Combine(authority, "inbox");
            RequirePlainPath(inbox, false);
            Directory.CreateDirectory(inbox);
            RequirePlainPath(inbox, true);

            int copied = 0;
            foreach (string receipt in receipts)
            {
                byte[] bytes = ReadBounded(receipt, MaxReceiptBytes);
                string destination = Path.Combine(inbox, Path.GetFileName(receipt));
                PublishImmutable(destination, bytes, MaxReceiptBytes);
                if (afterPublish != null) afterPublish(destination);
                // A late write is not silently discarded, even though normal callers stopped the writer.
                if (!Same(bytes, ReadBounded(receipt, MaxReceiptBytes)))
                    throw new HelperException("Arena reward source changed during transfer; it was retained.");
                RequirePlainPath(receipt, true);
                File.Delete(receipt);
                copied++;
            }
            if (log != null) log("Arena reward results returned: " + copied + "; payment waits for the next normal server start.");
            return copied;
        }

        /// <summary>Prevents any caller of Build from deleting unreturned published results.</summary>
        public static void RequireDrainedBeforeRebuild(string runtimeRoot)
        {
            if (!Exists(Path.Combine(HelperServerTree.GetTreePath(runtimeRoot), "user", OutboxFolderName))) return;
            string tree = RequireRoot(HelperServerTree.GetTreePath(RequireRoot(runtimeRoot)));
            if (PublishedResults(tree).Count != 0)
                throw new HelperException("Arena reward results have not been returned; the private session tree was retained. Retry the session return first.");
        }

        public static bool HasPublishedResults(string runtimeRoot)
        {
            if (!Exists(Path.Combine(HelperServerTree.GetTreePath(runtimeRoot), "user", OutboxFolderName))) return false;
            return PublishedResults(RequireRoot(HelperServerTree.GetTreePath(RequireRoot(runtimeRoot)))).Count != 0;
        }

        private static List<string> PublishedResults(string tree)
        {
            string outbox = Path.Combine(tree, "user", OutboxFolderName);
            RequirePlainPath(outbox, false);
            var results = new List<string>();
            if (!Exists(outbox)) return results;
            RequirePlainPath(outbox, true);
            int entries = 0;
            foreach (string path in Directory.EnumerateFileSystemEntries(outbox))
            {
                if (++entries > MaxReceipts * 2)
                    throw new HelperException("Too many Arena reward outbox entries; the private tree was retained.");
                RequirePlainPath(path, true);
                string name = Path.GetFileName(path);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                    throw new HelperException("An Arena reward outbox entry is a directory; the private tree was retained.");
                if (IsUnpublishedTemporary(name)) continue;
                if (name.Length != 37 || !name.EndsWith(".json", StringComparison.Ordinal)
                    || !IsLowerHex(name.Substring(0, 32), 32))
                    throw new HelperException("Unexpected Arena reward outbox entry; the private tree was retained.");
                results.Add(path);
                if (results.Count > MaxReceipts)
                    throw new HelperException("Too many Arena reward results; the private tree was retained.");
            }
            results.Sort(StringComparer.Ordinal);
            return results;
        }

        private static bool IsUnpublishedTemporary(string name)
        {
            const string prefix = ".arena-reward-";
            return name.Length == prefix.Length + 32 + 4 && name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith(".tmp", StringComparison.Ordinal) && IsLowerHex(name.Substring(prefix.Length, 32), 32);
        }

        private static Dictionary<string, object> ParseHandoff(byte[] bytes, string runtime, string profileId)
        {
            object parsed;
            string error;
            if (!ArenaMiniJson.TryParse(Utf8.GetString(bytes), out parsed, out error))
                throw new HelperException("Arena reward handoff is malformed.");
            var document = ArenaMiniJson.AsObject(parsed);
            var binding = ArenaMiniJson.GetObject(document, "binding");
            if (document == null || document.Count != 6 || binding == null || binding.Count != 3
                || ArenaMiniJson.GetInt(document, "schema", -1) != 1
                || !document.ContainsKey("enabled") || !(document["enabled"] is bool)
                || !HelperPathSafety.PathsEqual(ArenaMiniJson.GetString(document, "runtimeRoot"), runtime)
                || !string.Equals(ArenaMiniJson.GetString(binding, "profileId"), profileId, StringComparison.Ordinal)
                || !IsLowerHex(ArenaMiniJson.GetString(binding, "installationId"), 32)
                || !IsLowerHex(ArenaMiniJson.GetString(binding, "epoch"), 32)
                || !IsLowerHex(ArenaMiniJson.GetString(document, "delegationId"), 32))
                throw new HelperException("Arena reward handoff does not match its installation/profile.");
            byte[] key;
            try { key = Convert.FromBase64String(ArenaMiniJson.GetString(document, "key") ?? string.Empty); }
            catch (FormatException) { throw new HelperException("Arena reward handoff key is malformed."); }
            if (key.Length != 32) throw new HelperException("Arena reward handoff key is malformed.");
            return document;
        }

        private static void RequireSameDelegation(Dictionary<string, object> origin, Dictionary<string, object> current)
        {
            Dictionary<string, object> originBinding = ArenaMiniJson.GetObject(origin, "binding");
            Dictionary<string, object> currentBinding = ArenaMiniJson.GetObject(current, "binding");
            foreach (string field in new[] { "installationId", "profileId", "epoch" })
                if (!string.Equals(ArenaMiniJson.GetString(originBinding, field), ArenaMiniJson.GetString(currentBinding, field), StringComparison.Ordinal))
                    throw new HelperException("Arena reward enrollment changed; the private results were retained.");
            foreach (string field in new[] { "delegationId", "key" })
                if (!string.Equals(ArenaMiniJson.GetString(origin, field), ArenaMiniJson.GetString(current, field), StringComparison.Ordinal))
                    throw new HelperException("Arena reward delegation changed; the private results were retained.");
            // Enabled is intentionally not compared: opting out stops new earnings/payout,
            // but does not justify deleting results earned before that choice.
        }

        private static void PublishImmutable(string destination, byte[] bytes, int maximumBytes)
        {
            RequirePlainPath(destination, false);
            if (Exists(destination))
            {
                if (!Same(bytes, ReadBounded(destination, maximumBytes)))
                    throw new HelperException("Conflicting Arena reward result already exists; neither copy was replaced.");
                return;
            }
            string directory = Path.GetDirectoryName(destination);
            RequirePlainPath(directory, true);
            string temporary = Path.Combine(directory, ".arena-reward-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                // CreateNew never opens or truncates an existing link/file under the temporary name.
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                RequirePlainPath(destination, false);
                File.Move(temporary, destination); // no overwrite: another publisher cannot change this identity.
                if (!Same(bytes, ReadBounded(destination, maximumBytes)))
                    throw new HelperException("Arena reward result could not be verified after publication.");
            }
            finally
            {
                // This uniquely named unpublished staging file has no payment authority.
                if (Exists(temporary))
                {
                    RequirePlainPath(temporary, true);
                    File.Delete(temporary);
                }
            }
        }

        private static byte[] ReadBounded(string path, int maximumBytes)
        {
            RequirePlainPath(path, true);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new HelperException("An Arena reward metadata file is a directory.");
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length <= 0 || input.Length > maximumBytes)
                    throw new HelperException("Arena reward metadata exceeds its size limit or is empty.");
                var bytes = new byte[(int)input.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = input.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new EndOfStreamException();
                    offset += count;
                }
                if (input.ReadByte() != -1) throw new HelperException("Arena reward metadata changed while reading.");
                return bytes;
            }
        }

        // Check the full ancestry, not merely below a caller-supplied root. Absence is distinct
        // from an access failure. Live writers/file editors replacing directories are unsupported.
        private static void RequirePlainPath(string path, bool required)
        {
            string full;
            if (!HelperPathSafety.TryNormalize(path, out full) || HelperPathSafety.HasParentSegment(path))
                throw new HelperException("Arena reward metadata requires a local absolute path.");
            string current = full;
            while (current != null)
            {
                FileAttributes attributes;
                if (HelperFileSystem.TryGetAttributes(current, out attributes))
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new HelperException("Arena reward metadata cannot use a junction or symbolic link.");
                    if (!string.Equals(current, full, StringComparison.OrdinalIgnoreCase)
                        && (attributes & FileAttributes.Directory) == 0)
                        throw new HelperException("Arena reward metadata has a file in its parent path.");
                }
                else if (required && string.Equals(current, full, StringComparison.OrdinalIgnoreCase))
                    throw new FileNotFoundException("Arena reward metadata is missing.", full);
                current = HelperPathSafety.GetParent(current);
            }
        }

        private static string RequireRoot(string path)
        {
            string full;
            if (!HelperPathSafety.TryNormalize(path, out full) || HelperPathSafety.HasParentSegment(path))
                throw new HelperException("Arena reward runtime path is invalid.");
            RequirePlainPath(full, false);
            if (Exists(full) && (File.GetAttributes(full) & FileAttributes.Directory) == 0)
                throw new HelperException("Arena reward runtime path is not a directory.");
            return full;
        }

        private static void RequireSeparateRoots(string origin, string tree)
        {
            if (HelperPathSafety.IsSameOrUnder(origin, tree) || HelperPathSafety.IsSameOrUnder(tree, origin))
                throw new HelperException("Arena reward origin and private runtime must be separate.");
        }

        private static void RequireProfileId(string id)
        {
            if (!IsLowerHex(id, 24)) throw new HelperException("Arena reward profile identity is invalid.");
        }

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }

        private static bool Exists(string path)
        {
            FileAttributes attributes;
            return HelperFileSystem.TryGetAttributes(path, out attributes);
        }

        private static bool Same(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }
}
