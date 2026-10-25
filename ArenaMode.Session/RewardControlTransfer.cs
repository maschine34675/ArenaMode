// Linked into the offline helper checks. Keep compatible with net472/C# 7.3.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    /// <summary>
    /// Copies one prepared control capability into a completed disposable runtime. It never
    /// changes consent or opens normal profiles. The normal server still authorizes each change.
    /// </summary>
    public static class HelperRewardControlTransfer
    {
        public const string FileName = "arena-rewards-control.json";
        public const int MaximumBytes = 16 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryCopy(string sourceRuntime, string profileId, string controlId, string tree,
            HelperMarker expectedMarker, DateTime nowUtc, Action<string> log = null)
        {
            // Old helpers/clients and unavailable prepare endpoints keep the original switch behavior.
            if (string.IsNullOrEmpty(controlId)) return false;
            string temporary = null;
            string destination = null;
            bool published = false;
            try
            {
                string source = RequireRoot(sourceRuntime);
                string target = RequireRoot(tree);
                if (HelperPathSafety.IsSameOrUnder(source, target) || HelperPathSafety.IsSameOrUnder(target, source)
                    || !LowerHex(profileId, 24) || !LowerHex(controlId, 32))
                    throw new InvalidDataException();
                RequirePlainPath(Path.Combine(source, ArenaSessionContract.MarkerFileName), false);
                if (Exists(Path.Combine(source, ArenaSessionContract.MarkerFileName))) throw new InvalidDataException();
                string markerPath = Path.Combine(target, ArenaSessionContract.MarkerFileName);
                if (expectedMarker == null || expectedMarker.Schema != ArenaSessionContract.SchemaVersion
                    || expectedMarker.ProfileId != profileId || expectedMarker.RewardControlId != controlId
                    || !HelperPathSafety.PathsEqual(expectedMarker.SourceRuntime, source)
                    || expectedMarker.Port < 1 || expectedMarker.Port > 65535
                    || !HelperPathSafety.PathsEqual(expectedMarker.ServerExe, Path.Combine(target, HelperLayout.ServerExeName)))
                    throw new InvalidDataException();
                string expectedJson = expectedMarker.ToJson();
                RequireMarker(markerPath, expectedJson);

                destination = Path.Combine(target, FileName);
                RequirePlainPath(destination, false);
                // A prior capability is never reused if this optional refresh fails.
                if (Exists(destination)) File.Delete(destination);
                string export = Path.Combine(source, "user", "arena-rewards", profileId, "control.json");
                object raw;
                string parseError;
                if (!ArenaMiniJson.TryParse(Utf8.GetString(ReadBounded(export)), out raw, out parseError))
                    throw new InvalidDataException();
                var grant = ArenaMiniJson.AsObject(raw);
                ValidateGrant(grant, source, profileId, controlId, expectedMarker.Port, nowUtc.ToUniversalTime());
                var sidecar = new Dictionary<string, object>
                {
                    { "schema", 1 }, { "grant", grant }, { "privateRoot", target },
                    { "markerCreatedUtc", expectedMarker.CreatedUtc }, { "port", expectedMarker.Port },
                };
                byte[] bytes = Utf8.GetBytes(ArenaMiniJson.Write(sidecar, true));
                if (bytes.Length > MaximumBytes) throw new InvalidDataException();
                temporary = Path.Combine(target, ".arena-reward-control-" + Guid.NewGuid().ToString("N") + ".tmp");
                RequirePlainPath(temporary, false);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                RequireMarker(markerPath, expectedJson);
                RequirePlainPath(destination, false);
                File.Move(temporary, destination);
                published = true;
                RequireMarker(markerPath, expectedJson);
                if (!Same(bytes, ReadBounded(destination))) throw new InvalidDataException();
                return true;
            }
            catch (Exception)
            {
                // Fixed text only: neither transport reasons, file contents nor bearer tokens reach logs.
                if (published) TryRemovePlain(destination);
                try { if (log != null) log("Arena reward control unavailable; private-session rewards remain read-only."); }
                catch (Exception) { }
                return false;
            }
            finally { if (temporary != null) TryRemovePlain(temporary); }
        }

        private static void ValidateGrant(Dictionary<string, object> grant, string source, string profileId,
            string controlId, int privatePort, DateTime now)
        {
            if (grant == null || grant.Count != 9 || ArenaMiniJson.GetInt(grant, "schema", -1) != 1
                || ArenaMiniJson.GetString(grant, "controlId") != controlId
                || ArenaMiniJson.GetString(grant, "profileId") != profileId
                || !string.Equals(ArenaMiniJson.GetString(grant, "runtimeRoot"), source, StringComparison.OrdinalIgnoreCase)
                || !LowerHex(ArenaMiniJson.GetString(grant, "certificateSha256"), 64))
                throw new InvalidDataException();
            string token = ArenaMiniJson.GetString(grant, "token");
            byte[] decoded = Convert.FromBase64String(token ?? "");
            if (decoded.Length != 32 || Convert.ToBase64String(decoded) != token) throw new InvalidDataException();
            string originText = ArenaMiniJson.GetString(grant, "originUrl");
            Uri origin;
            if (!Uri.TryCreate(originText, UriKind.Absolute, out origin) || origin.Scheme != "https"
                || origin.Port < 1 || origin.Port > 65535 || origin.Port == privatePort
                || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0
                || !(string.Equals(originText, "https://127.0.0.1:" + origin.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    || string.Equals(originText, "https://[::1]:" + origin.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
                throw new InvalidDataException();
            DateTime issued = ReadUtc(grant, "issuedUtc"), expires = ReadUtc(grant, "expiresUtc");
            if (issued > now.AddMinutes(2) || issued < now.AddMinutes(-5)
                || expires <= now || expires <= issued || expires > issued.AddHours(12))
                throw new InvalidDataException();
        }

        private static DateTime ReadUtc(Dictionary<string, object> grant, string key)
        {
            string text = ArenaMiniJson.GetString(grant, key);
            DateTimeOffset value;
            if (text == null || !Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$")
                || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
                throw new InvalidDataException();
            return value.UtcDateTime;
        }

        internal static void RequireMarker(string path, string expected)
        {
            if (!string.Equals(Utf8.GetString(ReadBounded(path)), expected, StringComparison.Ordinal))
                throw new InvalidDataException();
        }

        private static string RequireRoot(string path)
        {
            string full;
            if (!HelperPathSafety.TryNormalize(path, out full) || HelperPathSafety.HasParentSegment(path))
                throw new InvalidDataException();
            RequirePlainPath(full, true);
            if ((File.GetAttributes(full) & FileAttributes.Directory) == 0) throw new InvalidDataException();
            return full;
        }

        internal static void RequirePlainPath(string path, bool required)
        {
            string full;
            if (!HelperPathSafety.TryNormalize(path, out full) || HelperPathSafety.HasParentSegment(path))
                throw new InvalidDataException();
            for (string current = full; current != null; current = HelperPathSafety.GetParent(current))
            {
                FileAttributes attributes;
                if (HelperFileSystem.TryGetAttributes(current, out attributes))
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0
                        || !HelperPathSafety.PathsEqual(current, full) && (attributes & FileAttributes.Directory) == 0)
                        throw new InvalidDataException();
                }
                else if (required && HelperPathSafety.PathsEqual(current, full)) throw new FileNotFoundException();
            }
        }

        internal static byte[] ReadBounded(string path)
        {
            RequirePlainPath(path, true);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new InvalidDataException();
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length <= 0 || input.Length > MaximumBytes) throw new InvalidDataException();
                var bytes = new byte[(int)input.Length];
                int position = 0;
                while (position < bytes.Length)
                {
                    int read = input.Read(bytes, position, bytes.Length - position);
                    if (read == 0) throw new EndOfStreamException();
                    position += read;
                }
                if (input.ReadByte() != -1) throw new InvalidDataException();
                return bytes;
            }
        }

        private static void TryRemovePlain(string path)
        {
            try { RequirePlainPath(path, false); if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        private static bool Exists(string path)
        {
            FileAttributes attributes;
            return HelperFileSystem.TryGetAttributes(path, out attributes);
        }

        private static bool LowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }

        private static bool Same(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }
}
