// Pure logic of the Arena session helper (docs/ARENA-SESSION-20261021.md, section 6).
//
// Linked source: compiled into maschine-ArenaMode.Session.exe (net472) and, through a Compile
// Include with Link, into ArenaMode.Tests (net10.0). Therefore it uses only the BCL, only C# 7.3
// syntax and only APIs that exist on both targets, and it does no process, socket or registry
// work and touches no file. The file system side lives in SessionFileSystem.cs, the process side
// in the files that are not linked.
//
// It carries no nullable annotations on purpose (the net472 default language version cannot
// parse them); the pragma below silences the analysis where the tests switch it on. null is an
// ordinary, documented value here ("not found", "no error").
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ArenaMode.Shared.Session;

namespace ArenaMode.SessionHelper
{
    /// <summary>Names, limits and timeouts the helper agrees on with the specification.</summary>
    public static class HelperLayout
    {
        public const string ServerExeName = "SPT.Server.exe";
        public const string LoggerFileName = "sptLogger.json";
        public const string SptDataFolder = "SPT_Data";
        public const string ConfigsFolder = "configs";
        public const string HttpJsonFileName = "http.json";
        public const string UserFolder = "user";
        public const string ModsFolder = "mods";
        public const string ProfilesFolder = "profiles";
        public const string ProfileDataFolder = "profileData";
        public const string CertsFolder = "certs";
        public const string CredentialsFolder = "credentials";

        /// <summary>Prefix of every log file name the arena server writes.</summary>
        public const string LogFilePatternPrefix = "arena-";

        /// <summary>SPT's documented default port, used only when http.json names none.</summary>
        public const int DefaultBackendPort = 6969;

        public const string ConfigArgumentPrefix = "-config=";
        public const string TokenArgumentPrefix = "-token=";
        public const string DoorstopVariablePrefix = "DOORSTOP_";

        /// <summary>The helper waits at most this long for the old game process to exit.</summary>
        public const int GameExitTimeoutSeconds = 120;

        /// <summary>The helper waits at most this long for a server to accept TCP connections.</summary>
        public const int ServerStartTimeoutSeconds = 180;

        /// <summary><c>helper.log</c> is rotated to <c>helper.log.1</c> once it reaches this size.</summary>
        public const long LogRotateBytes = 1024L * 1024L;
    }

    // ------------------------------------------------------------------------------ command line

    /// <summary>
    /// The helper's own command line: <c>enter --request &lt;file&gt;</c>,
    /// <c>leave --request &lt;file&gt;</c>, <c>stop-server</c> and <c>status</c>.
    /// </summary>
    public sealed class HelperCommand
    {
        public const string Enter = "enter";
        public const string Leave = "leave";
        public const string StopServer = "stop-server";
        public const string Status = "status";

        public string Name { get; private set; }

        /// <summary>The value of <c>--request</c>; null for the commands that take none.</summary>
        public string RequestPath { get; private set; }

        public bool NeedsRequest
        {
            get { return Name == Enter || Name == Leave; }
        }

        public static bool TryParse(string[] args, out HelperCommand command, out string error)
        {
            command = null;
            error = null;
            if (args == null || args.Length == 0)
            {
                error = "no command given (expected enter, leave, stop-server or status)";
                return false;
            }

            string name = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
            if (name != Enter && name != Leave && name != StopServer && name != Status)
            {
                error = "unknown command '" + args[0] + "'";
                return false;
            }

            string request = null;
            for (int i = 1; i < args.Length; i++)
            {
                string token = args[i] ?? string.Empty;
                bool takesRequest = name == Enter || name == Leave;
                if (takesRequest && token.Equals("--request", StringComparison.OrdinalIgnoreCase))
                {
                    if (request != null)
                    {
                        error = "--request given twice";
                        return false;
                    }

                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        error = "--request needs a file name";
                        return false;
                    }

                    request = args[++i];
                    continue;
                }

                if (takesRequest && token.StartsWith("--request=", StringComparison.OrdinalIgnoreCase))
                {
                    if (request != null)
                    {
                        error = "--request given twice";
                        return false;
                    }

                    request = token.Substring("--request=".Length);
                    if (string.IsNullOrWhiteSpace(request))
                    {
                        error = "--request needs a file name";
                        return false;
                    }

                    continue;
                }

                error = "unexpected argument '" + token + "' for '" + name + "'";
                return false;
            }

            if ((name == Enter || name == Leave) && request == null)
            {
                error = "'" + name + "' needs --request <file>";
                return false;
            }

            command = new HelperCommand { Name = name, RequestPath = request };
            return true;
        }
    }

    // ------------------------------------------------------------------------------ path safety

    /// <summary>
    /// String-level path checks. Everything the helper writes or deletes is validated against the
    /// session runtime root with these first; the reparse-point (junction) checks that need the
    /// file system are in <c>HelperSandbox</c>.
    /// </summary>
    public static class HelperPathSafety
    {
        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>
        /// True for <c>X:\...</c> or <c>X:/...</c> only. UNC paths, device paths (<c>\\?\</c>),
        /// drive-relative paths (<c>C:foo</c>), root-relative paths, alternate data streams and
        /// wildcards are all refused: the helper has no business with any of them.
        /// </summary>
        public static bool IsDriveAbsolute(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 3) return false;
            char drive = path[0];
            bool letter = (drive >= 'a' && drive <= 'z') || (drive >= 'A' && drive <= 'Z');
            if (!letter || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) return false;
            if (path.IndexOf(':', 2) >= 0) return false;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            if (path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0) return false;
            return true;
        }

        /// <summary>True when any path segment is <c>..</c> (request paths never contain one).</summary>
        public static bool HasParentSegment(string path)
        {
            if (path == null) return false;
            foreach (string segment in path.Split('\\', '/'))
            {
                if (segment == "..") return true;
            }

            return false;
        }

        /// <summary>
        /// Full path without a trailing separator (the drive root keeps its one). False for anything
        /// that is not <see cref="IsDriveAbsolute"/> or that the platform refuses to normalise.
        /// </summary>
        public static bool TryNormalize(string path, out string full)
        {
            full = null;
            if (!IsDriveAbsolute(path)) return false;
            try
            {
                string result = Path.GetFullPath(path);
                if (!IsDriveAbsolute(result)) return false;
                if (result.Length > 3) result = result.TrimEnd('\\', '/');
                full = result;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Case-insensitive comparison of two normalised paths; false when either is unusable.</summary>
        public static bool PathsEqual(string a, string b)
        {
            string left;
            string right;
            if (!TryNormalize(a, out left) || !TryNormalize(b, out right)) return false;
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or lies below it.</summary>
        public static bool IsSameOrUnder(string path, string root)
        {
            string p;
            string r;
            if (!TryNormalize(path, out p) || !TryNormalize(root, out r)) return false;
            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
            return p.StartsWith(WithSeparator(r), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="path"/> lies below <paramref name="root"/> and is not the root
        /// itself. <c>C:\a\b\..\..\x</c> style tricks are resolved by the normalisation first.
        /// </summary>
        public static bool IsStrictlyUnder(string path, string root)
        {
            string p;
            string r;
            if (!TryNormalize(path, out p) || !TryNormalize(root, out r)) return false;
            string prefix = WithSeparator(r);
            return p.Length > prefix.Length && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when <paramref name="path"/> is exactly one level below <paramref name="parent"/>.</summary>
        public static bool IsDirectChild(string path, string parent)
        {
            string p;
            string r;
            if (!TryNormalize(path, out p) || !TryNormalize(parent, out r)) return false;
            string prefix = WithSeparator(r);
            if (p.Length <= prefix.Length || !p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            return p.IndexOf('\\', prefix.Length) < 0;
        }

        /// <summary>The directory segments between <paramref name="root"/> and <paramref name="path"/>; null when not below.</summary>
        public static List<string> RelativeSegments(string path, string root)
        {
            string p;
            string r;
            if (!TryNormalize(path, out p) || !TryNormalize(root, out r)) return null;
            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return new List<string>();
            string prefix = WithSeparator(r);
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return new List<string>(p.Substring(prefix.Length).Split('\\'));
        }

        /// <summary>
        /// A single, plain file or folder name: no separators, no drive or stream colon, no wildcard,
        /// no <c>.</c>/<c>..</c>, no leading space and no trailing dot or space (Windows silently strips
        /// those, which makes two names the same folder) and no reserved device name.
        /// </summary>
        public static bool IsSafeSingleName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 255) return false;
            if (name == "." || name == "..") return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            char last = name[name.Length - 1];
            if (last == '.' || last == ' ') return false;
            if (name[0] == ' ') return false;
            string stem = name;
            int dot = name.IndexOf('.');
            if (dot >= 0) stem = name.Substring(0, dot);
            foreach (string reserved in ReservedDeviceNames)
            {
                if (string.Equals(stem.TrimEnd(' '), reserved, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        /// <summary>The parent directory of a normalised path; null for a drive root or an unusable path.</summary>
        public static string GetParent(string path)
        {
            string p;
            if (!TryNormalize(path, out p) || p.Length <= 3) return null;
            int cut = p.LastIndexOf('\\');
            if (cut < 0) return null;
            if (cut <= 2) return p.Substring(0, 3);
            return p.Substring(0, cut);
        }

        /// <summary>True when the attribute set marks a reparse point (junction or symbolic link).</summary>
        public static bool IsReparsePoint(FileAttributes attributes)
        {
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }

        private static string WithSeparator(string path)
        {
            return path.EndsWith("\\", StringComparison.Ordinal) ? path : path + "\\";
        }
    }

    // ------------------------------------------------------------------------------ request file

    /// <summary>One server mod the client lists in the request: GUID and its folder below <c>user\mods</c>.</summary>
    public sealed class HelperServerMod
    {
        public string Guid { get; set; }

        public string Folder { get; set; }

        /// <summary>The last path segment of <see cref="Folder"/>, the name of the copy in the arena tree.</summary>
        public string FolderName
        {
            get
            {
                if (string.IsNullOrEmpty(Folder)) return null;
                int cut = Folder.LastIndexOf('\\');
                return cut < 0 ? Folder : Folder.Substring(cut + 1);
            }
        }
    }

    /// <summary>
    /// The request file the client writes before it starts the helper (specification section 6).
    /// <see cref="TryParse"/> validates everything the helper later builds paths from; paths are
    /// stored normalised, without a trailing separator.
    /// </summary>
    public sealed class HelperRequest
    {
        public const int SupportedSchema = 1;

        public HelperRequest()
        {
            GameArgs = new List<string>();
            ServerMods = new List<HelperServerMod>();
        }

        public int Schema { get; set; }

        public string Direction { get; set; }

        public int GamePid { get; set; }

        public string GameExe { get; set; }

        public string GameDir { get; set; }

        /// <summary>The arguments of the running game without argv[0].</summary>
        public List<string> GameArgs { get; set; }

        /// <summary>The logged-in profile; required for <c>enter</c>, 24 hexadecimal characters.</summary>
        public string ProfileId { get; set; }

        public string SptRuntimeDir { get; set; }

        public List<HelperServerMod> ServerMods { get; set; }

        public string PolicyHash { get; set; }

        public bool IsEnter
        {
            get { return Direction == ArenaSessionContract.DirectionEnter; }
        }

        public bool IsLeave
        {
            get { return Direction == ArenaSessionContract.DirectionLeave; }
        }

        /// <summary>A profile id is the 24 hexadecimal characters of a Mongo id, in either case.</summary>
        public static bool IsValidProfileId(string profileId)
        {
            if (profileId == null || profileId.Length != 24) return false;
            foreach (char c in profileId)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }

            return true;
        }

        /// <summary>
        /// Parses and validates a request. <paramref name="expectedDirection"/> may be null to accept
        /// both; otherwise a request of the other direction is refused. On failure
        /// <paramref name="request"/> is null and <paramref name="error"/> names the problem.
        /// </summary>
        public static bool TryParse(string json, string expectedDirection, out HelperRequest request, out string error)
        {
            request = null;
            object root;
            if (!ArenaMiniJson.TryParse(json, out root, out error))
            {
                error = "request is not valid JSON: " + error;
                return false;
            }

            Dictionary<string, object> obj = ArenaMiniJson.AsObject(root);
            if (obj == null)
            {
                error = "the request must be a JSON object";
                return false;
            }

            int schema = ArenaMiniJson.GetInt(obj, "schema", -1);
            if (schema != SupportedSchema)
            {
                error = "unsupported request schema " + schema.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            string direction = ArenaMiniJson.GetString(obj, "direction");
            if (direction != ArenaSessionContract.DirectionEnter && direction != ArenaSessionContract.DirectionLeave)
            {
                error = "direction must be 'enter' or 'leave'";
                return false;
            }

            if (expectedDirection != null && direction != expectedDirection)
            {
                error = "the request direction is '" + direction + "' but the command expects '" + expectedDirection + "'";
                return false;
            }

            var parsed = new HelperRequest { Schema = schema, Direction = direction };

            parsed.GamePid = ArenaMiniJson.GetInt(obj, "gamePid", 0);
            if (parsed.GamePid <= 0)
            {
                error = "gamePid must be a positive integer";
                return false;
            }

            string gameExe;
            string gameDir;
            string runtimeDir;
            if (!ReadPath(obj, "gameExe", out gameExe, ref error)
                || !ReadPath(obj, "gameDir", out gameDir, ref error)
                || !ReadPath(obj, "sptRuntimeDir", out runtimeDir, ref error))
            {
                return false;
            }

            parsed.GameExe = gameExe;
            parsed.GameDir = gameDir;
            parsed.SptRuntimeDir = runtimeDir;

            if (!parsed.GameExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                error = "gameExe must name an .exe file";
                return false;
            }

            if (!HelperPathSafety.IsStrictlyUnder(parsed.GameExe, parsed.GameDir))
            {
                error = "gameExe must lie inside gameDir";
                return false;
            }

            List<object> args = ArenaMiniJson.GetArray(obj, "gameArgs");
            if (args == null)
            {
                error = "gameArgs must be an array of strings";
                return false;
            }

            foreach (object item in args)
            {
                string text = item as string;
                if (text == null || text.IndexOf('\0') >= 0)
                {
                    error = "gameArgs must contain only strings";
                    return false;
                }

                parsed.GameArgs.Add(text);
            }

            string profileId = ArenaMiniJson.GetString(obj, "profileId");
            if (!string.IsNullOrEmpty(profileId) && !IsValidProfileId(profileId))
            {
                error = "profileId must be 24 hexadecimal characters";
                return false;
            }

            if (direction == ArenaSessionContract.DirectionEnter && string.IsNullOrEmpty(profileId))
            {
                error = "profileId is required to enter an Arena session";
                return false;
            }

            parsed.ProfileId = string.IsNullOrEmpty(profileId) ? null : profileId;
            parsed.PolicyHash = ArenaMiniJson.GetString(obj, "policyHash");

            if (!ReadServerMods(obj, parsed, ref error)) return false;

            request = parsed;
            error = null;
            return true;
        }

        private static bool ReadPath(Dictionary<string, object> obj, string key, out string normalised, ref string error)
        {
            normalised = null;
            string raw = ArenaMiniJson.GetString(obj, key);
            if (string.IsNullOrEmpty(raw))
            {
                error = key + " is required";
                return false;
            }

            if (HelperPathSafety.HasParentSegment(raw))
            {
                error = key + " must not contain '..'";
                return false;
            }

            if (!HelperPathSafety.TryNormalize(raw, out normalised))
            {
                normalised = null;
                error = key + " must be an absolute path on a drive (like C:\\SPT41)";
                return false;
            }

            return true;
        }

        private static bool ReadServerMods(Dictionary<string, object> obj, HelperRequest parsed, ref string error)
        {
            List<object> mods = ArenaMiniJson.GetArray(obj, "serverMods");
            if (mods == null)
            {
                if (parsed.IsEnter)
                {
                    error = "serverMods must be an array";
                    return false;
                }

                return true;
            }

            string modsRoot = Path.Combine(Path.Combine(parsed.SptRuntimeDir, HelperLayout.UserFolder), HelperLayout.ModsFolder);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in mods)
            {
                if (ArenaMiniJson.AsObject(item) == null)
                {
                    error = "every serverMods entry must be an object";
                    return false;
                }

                string guid = ArenaMiniJson.GetString(item, "guid");
                string folder = ArenaMiniJson.GetString(item, "folder");
                if (string.IsNullOrWhiteSpace(guid))
                {
                    error = "every serverMods entry needs a guid";
                    return false;
                }

                string normalised;
                if (string.IsNullOrEmpty(folder) || HelperPathSafety.HasParentSegment(folder)
                    || !HelperPathSafety.TryNormalize(folder, out normalised))
                {
                    error = "serverMods folder of '" + guid + "' must be an absolute path without '..'";
                    return false;
                }

                if (!HelperPathSafety.IsDirectChild(normalised, modsRoot))
                {
                    error = "serverMods folder of '" + guid + "' must be a direct child of " + modsRoot;
                    return false;
                }

                var mod = new HelperServerMod { Guid = guid, Folder = normalised };
                if (!HelperPathSafety.IsSafeSingleName(mod.FolderName))
                {
                    error = "serverMods folder name of '" + guid + "' is not a plain folder name";
                    return false;
                }

                if (!seen.Add(mod.FolderName))
                {
                    error = "two serverMods entries use the folder name '" + mod.FolderName + "'";
                    return false;
                }

                parsed.ServerMods.Add(mod);
            }

            if (parsed.IsEnter && parsed.ServerMods.Count == 0)
            {
                error = "serverMods is empty: an Arena server needs at least the ArenaMode server mod";
                return false;
            }

            return true;
        }

        /// <summary>The request as the client writes it (indented JSON, spec section 6).</summary>
        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            root["schema"] = Schema;
            root["direction"] = Direction;
            root["gamePid"] = GamePid;
            root["gameExe"] = GameExe;
            root["gameDir"] = GameDir;
            root["gameArgs"] = GameArgs ?? new List<string>();
            root["profileId"] = ProfileId;
            root["sptRuntimeDir"] = SptRuntimeDir;
            var mods = new List<object>();
            if (ServerMods != null)
            {
                foreach (HelperServerMod mod in ServerMods)
                {
                    var entry = new Dictionary<string, object>();
                    entry["guid"] = mod.Guid;
                    entry["folder"] = mod.Folder;
                    mods.Add(entry);
                }
            }

            root["serverMods"] = mods;
            root["policyHash"] = PolicyHash;
            return ArenaMiniJson.Write(root, true);
        }
    }

    // ------------------------------------------------------------------------------ game arguments

    /// <summary>
    /// Rewrites the game's command line for a session switch and turns an argument list into a
    /// command line string that Windows splits back into exactly the same arguments.
    /// </summary>
    public static class HelperCommandLine
    {
        private static readonly Regex BackendUrlPattern = new Regex(
            "(?<kq>['\"])BackendUrl\\k<kq>\\s*:\\s*(?<vq>['\"])(?<url>[^'\"]*)\\k<vq>",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns the game arguments for the target session: the port of the <c>BackendUrl</c> inside
        /// the single <c>-config=</c> argument is set to <paramref name="backendPort"/> (host, scheme
        /// and every other member stay as they are), any existing <c>-arena-session</c> is removed
        /// and, when <paramref name="arenaSession"/> is set, appended once at the end. All other
        /// arguments keep their order. Fails when there is no (or more than one) <c>-config=</c>
        /// argument or it carries no BackendUrl: the game could not be pointed at the other server.
        /// </summary>
        public static bool TryRewriteGameArguments(IList<string> args, bool arenaSession, int backendPort,
            out List<string> result, out string error)
        {
            result = null;
            error = null;
            if (args == null)
            {
                error = "no game arguments";
                return false;
            }

            if (backendPort < 1 || backendPort > 65535)
            {
                error = "invalid backend port " + backendPort.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            var output = new List<string>();
            int configCount = 0;
            foreach (string arg in args)
            {
                if (arg == null)
                {
                    error = "a game argument is null";
                    return false;
                }

                if (IsSessionFlag(arg)) continue;

                if (IsConfigArgument(arg))
                {
                    configCount++;
                    string rewritten;
                    if (!TryRewriteConfigArgument(arg, backendPort, out rewritten, out error)) return false;
                    output.Add(rewritten);
                    continue;
                }

                output.Add(arg);
            }

            if (configCount == 0)
            {
                error = "the game arguments contain no -config= argument, so the backend cannot be re-routed";
                return false;
            }

            if (configCount > 1)
            {
                error = "the game arguments contain more than one -config= argument";
                return false;
            }

            if (arenaSession) output.Add(ArenaSessionContract.CommandLineFlag);
            result = output;
            return true;
        }

        /// <summary>True for the Arena session flag, compared like <c>ArenaSessionContract.HasSessionFlag</c>.</summary>
        public static bool IsSessionFlag(string arg)
        {
            return arg != null && string.Equals(arg.Trim(), ArenaSessionContract.CommandLineFlag, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsConfigArgument(string arg)
        {
            return arg != null && arg.StartsWith(HelperLayout.ConfigArgumentPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Sets the port of the BackendUrl inside one <c>-config=...</c> argument.</summary>
        public static bool TryRewriteConfigArgument(string configArgument, int port, out string rewritten, out string error)
        {
            rewritten = null;
            error = null;
            if (!IsConfigArgument(configArgument))
            {
                error = "not a -config= argument";
                return false;
            }

            string value = configArgument.Substring(HelperLayout.ConfigArgumentPrefix.Length);
            Match match = BackendUrlPattern.Match(value);
            if (!match.Success)
            {
                error = "the -config= argument carries no BackendUrl";
                return false;
            }

            Group url = match.Groups["url"];
            string newUrl;
            if (!TryReplaceUrlPort(url.Value, port, out newUrl))
            {
                error = "the BackendUrl '" + url.Value + "' is not a URL with a host";
                return false;
            }

            rewritten = configArgument.Substring(0, HelperLayout.ConfigArgumentPrefix.Length)
                + value.Substring(0, url.Index) + newUrl + value.Substring(url.Index + url.Length);
            return true;
        }

        /// <summary>Reads the port of the BackendUrl in a <c>-config=</c> argument; false when absent.</summary>
        public static bool TryReadBackendPort(string configArgument, out int port)
        {
            port = 0;
            if (!IsConfigArgument(configArgument)) return false;
            Match match = BackendUrlPattern.Match(configArgument.Substring(HelperLayout.ConfigArgumentPrefix.Length));
            if (!match.Success) return false;
            string url = match.Groups["url"].Value;
            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0) return false;
            int authorityStart = schemeEnd + 3;
            int authorityEnd = url.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (authorityEnd < 0) authorityEnd = url.Length;
            string hostPort = url.Substring(authorityStart, authorityEnd - authorityStart);
            hostPort = hostPort.Substring(hostPort.LastIndexOf('@') + 1);
            int colon = hostPort.LastIndexOf(':');
            if (colon < 0 || colon < hostPort.LastIndexOf(']')) return false;
            return int.TryParse(hostPort.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port);
        }

        /// <summary>
        /// Replaces (or adds) the port of <c>scheme://[user@]host[:port][/path]</c> and leaves every
        /// other character of the URL alone. Bracketed IPv6 hosts are supported.
        /// </summary>
        public static bool TryReplaceUrlPort(string url, int port, out string replaced)
        {
            replaced = null;
            if (string.IsNullOrEmpty(url)) return false;
            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0) return false;
            int authorityStart = schemeEnd + 3;
            int authorityEnd = url.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (authorityEnd < 0) authorityEnd = url.Length;
            string authority = url.Substring(authorityStart, authorityEnd - authorityStart);
            if (authority.Length == 0) return false;

            int at = authority.LastIndexOf('@');
            string userInfo = at >= 0 ? authority.Substring(0, at + 1) : string.Empty;
            string hostPort = authority.Substring(at + 1);
            string host;
            if (hostPort.StartsWith("[", StringComparison.Ordinal))
            {
                int close = hostPort.IndexOf(']');
                if (close < 0) return false;
                host = hostPort.Substring(0, close + 1);
                string rest = hostPort.Substring(close + 1);
                if (rest.Length > 0 && rest[0] != ':') return false;
            }
            else
            {
                int colon = hostPort.LastIndexOf(':');
                host = colon >= 0 ? hostPort.Substring(0, colon) : hostPort;
                if (host.IndexOf(':') >= 0) return false;
            }

            if (host.Length == 0) return false;
            replaced = url.Substring(0, authorityStart) + userInfo + host + ":"
                + port.ToString(CultureInfo.InvariantCulture) + url.Substring(authorityEnd);
            return true;
        }

        /// <summary>
        /// Quotes one argument for a Windows command line (the rules of the C runtime and
        /// <c>CommandLineToArgvW</c>): plain arguments stay as they are, an empty argument or one with
        /// white space or a double quote is wrapped and its quotes and preceding backslashes escaped.
        /// </summary>
        public static string QuoteArgument(string arg)
        {
            if (arg == null) arg = string.Empty;
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;

            var sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }

                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }

                sb.Append(c);
            }

            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>The arguments joined into one command line (no program name).</summary>
        public static string BuildCommandLine(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(QuoteArgument(arg));
                }
            }

            return sb.ToString();
        }

        /// <summary>The arguments for the log: the value of <c>-token=</c> (the profile session) is hidden.</summary>
        public static string RedactForLog(IEnumerable<string> args)
        {
            var shown = new List<string>();
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (arg != null && arg.StartsWith(HelperLayout.TokenArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        shown.Add(arg.Substring(0, HelperLayout.TokenArgumentPrefix.Length) + "<hidden>");
                    }
                    else
                    {
                        shown.Add(arg);
                    }
                }
            }

            return BuildCommandLine(shown);
        }
    }

    // ------------------------------------------------------------------------------ environment

    /// <summary>
    /// An inherited <c>DOORSTOP_*</c> variable (<c>DOORSTOP_DISABLE</c>, <c>DOORSTOP_INITIALIZED</c>
    /// and friends) would make a game started from the running game skip the BepInEx injection, so
    /// no child of the helper gets one.
    /// </summary>
    public static class HelperEnvironment
    {
        public static bool IsDoorstopVariable(string name)
        {
            return name != null && name.StartsWith(HelperLayout.DoorstopVariablePrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The names from <paramref name="names"/> that must not reach a child process.</summary>
        public static List<string> DoorstopNames(IEnumerable<string> names)
        {
            var result = new List<string>();
            if (names == null) return result;
            foreach (string name in names)
            {
                if (IsDoorstopVariable(name)) result.Add(name);
            }

            return result;
        }

        /// <summary>A copy of <paramref name="source"/> without the DOORSTOP_ variables (names compare case-insensitively).</summary>
        public static Dictionary<string, string> Clean(IDictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;
            foreach (KeyValuePair<string, string> pair in source)
            {
                if (!IsDoorstopVariable(pair.Key)) result[pair.Key] = pair.Value;
            }

            return result;
        }

        /// <summary>
        /// Removes the DOORSTOP_ variables from a <c>ProcessStartInfo.EnvironmentVariables</c> block
        /// in place and returns how many were removed.
        /// </summary>
        public static int RemoveDoorstopVariables(StringDictionary environment)
        {
            if (environment == null) return 0;
            var names = new List<string>();
            foreach (object key in environment.Keys) names.Add((string)key);
            List<string> doomed = DoorstopNames(names);
            foreach (string name in doomed) environment.Remove(name);
            return doomed.Count;
        }
    }

    // ------------------------------------------------------------------------------ json text

    /// <summary>One member of a JSON object found by <see cref="HelperJsonDocument"/>.</summary>
    public sealed class HelperJsonMember
    {
        /// <summary>The decoded key.</summary>
        public string Key { get; set; }

        /// <summary>1 for a member of the top-level object, +1 for every enclosing object (arrays do not count).</summary>
        public int Depth { get; set; }

        /// <summary>Index of the opening quote of the key.</summary>
        public int KeyStart { get; set; }

        /// <summary>Index of the first character of the value.</summary>
        public int ValueStart { get; set; }

        /// <summary>Index after the last character of the value.</summary>
        public int ValueEnd { get; set; }

        /// <summary><c>"</c> string, <c>{</c> object, <c>[</c> array, <c>l</c> number/true/false/null.</summary>
        public char ValueKind { get; set; }
    }

    /// <summary>
    /// A scan of a JSON document that remembers where things are, so a few members can be changed
    /// without rewriting the rest. The scanner accepts what SPT's own JSON reader accepts: comments
    /// (<c>//</c> and <c>/* */</c>) and a leading byte order mark; trailing commas are refused.
    /// </summary>
    public sealed class HelperJsonDocument
    {
        private const int MaxNesting = 64;

        private HelperJsonDocument()
        {
            Members = new List<HelperJsonMember>();
        }

        /// <summary>Every object member in document order, nested ones included.</summary>
        public List<HelperJsonMember> Members { get; private set; }

        /// <summary>Index of the <c>{</c> of the top-level object.</summary>
        public int OpenBrace { get; private set; }

        /// <summary>Number of members of the top-level object.</summary>
        public int TopLevelCount { get; private set; }

        /// <summary>Index after the value of the last top-level member; -1 for an empty object.</summary>
        public int LastTopLevelValueEnd { get; private set; }

        public static bool TryScan(string text, out HelperJsonDocument document, out string error)
        {
            document = null;
            error = null;
            if (text == null)
            {
                error = "JSON text is null";
                return false;
            }

            var scanner = new Scanner(text);
            try
            {
                scanner.ParseDocument();
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }

            document = new HelperJsonDocument
            {
                Members = scanner.Members,
                OpenBrace = scanner.RootOpen,
                TopLevelCount = scanner.RootCount,
                LastTopLevelValueEnd = scanner.RootLastValueEnd,
            };
            return true;
        }

        private sealed class Scanner
        {
            private readonly string _text;
            private int _pos;
            private int _nesting;

            public Scanner(string text)
            {
                _text = text;
                Members = new List<HelperJsonMember>();
                RootOpen = -1;
                RootLastValueEnd = -1;
            }

            public List<HelperJsonMember> Members { get; private set; }

            public int RootOpen { get; private set; }

            public int RootCount { get; private set; }

            public int RootLastValueEnd { get; private set; }

            public void ParseDocument()
            {
                if (_text.Length > 0 && _text[0] == '\uFEFF') _pos = 1;
                SkipTrivia();
                if (_pos >= _text.Length || _text[_pos] != '{') throw Fail("the document must be a JSON object");
                RootOpen = _pos;
                ParseObject(1);
                SkipTrivia();
                if (_pos != _text.Length) throw Fail("unexpected content after the JSON object");
            }

            private FormatException Fail(string message)
            {
                return new FormatException(message + " (at offset " + _pos.ToString(CultureInfo.InvariantCulture) + ")");
            }

            private void SkipTrivia()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == '/' && _pos + 1 < _text.Length)
                    {
                        char next = _text[_pos + 1];
                        if (next == '/')
                        {
                            _pos += 2;
                            while (_pos < _text.Length && _text[_pos] != '\n') _pos++;
                            continue;
                        }

                        if (next == '*')
                        {
                            int end = _text.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
                            if (end < 0) throw Fail("unterminated comment");
                            _pos = end + 2;
                            continue;
                        }
                    }

                    break;
                }
            }

            private void Enter()
            {
                if (++_nesting > MaxNesting) throw Fail("JSON nesting is deeper than " + MaxNesting.ToString(CultureInfo.InvariantCulture));
            }

            private void ParseObject(int depth)
            {
                Enter();
                bool isRoot = depth == 1;
                _pos++; // {
                SkipTrivia();
                if (_pos < _text.Length && _text[_pos] == '}')
                {
                    _pos++;
                    _nesting--;
                    return;
                }

                while (true)
                {
                    SkipTrivia();
                    if (_pos >= _text.Length || _text[_pos] != '"') throw Fail("expected a string key in the object");
                    int keyStart = _pos;
                    string key = ParseString();
                    SkipTrivia();
                    if (_pos >= _text.Length || _text[_pos] != ':') throw Fail("expected ':' after the key '" + key + "'");
                    _pos++;
                    SkipTrivia();
                    if (_pos >= _text.Length) throw Fail("unexpected end of the document");

                    var member = new HelperJsonMember { Key = key, Depth = depth, KeyStart = keyStart, ValueStart = _pos };
                    Members.Add(member);
                    member.ValueKind = ParseValue(depth);
                    member.ValueEnd = _pos;
                    if (isRoot)
                    {
                        RootCount++;
                        RootLastValueEnd = member.ValueEnd;
                    }

                    SkipTrivia();
                    if (_pos >= _text.Length) throw Fail("unterminated object");
                    char c = _text[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == '}')
                    {
                        _pos++;
                        _nesting--;
                        return;
                    }

                    throw Fail("expected ',' or '}' in the object");
                }
            }

            private void ParseArray(int depth)
            {
                Enter();
                _pos++; // [
                SkipTrivia();
                if (_pos < _text.Length && _text[_pos] == ']')
                {
                    _pos++;
                    _nesting--;
                    return;
                }

                while (true)
                {
                    SkipTrivia();
                    if (_pos >= _text.Length) throw Fail("unterminated array");
                    ParseValue(depth);
                    SkipTrivia();
                    if (_pos >= _text.Length) throw Fail("unterminated array");
                    char c = _text[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == ']')
                    {
                        _pos++;
                        _nesting--;
                        return;
                    }

                    throw Fail("expected ',' or ']' in the array");
                }
            }

            // depth = the depth of the object that contains the value (arrays do not add a level).
            private char ParseValue(int depth)
            {
                char c = _text[_pos];
                if (c == '{')
                {
                    ParseObject(depth + 1);
                    return '{';
                }

                if (c == '[')
                {
                    ParseArray(depth);
                    return '[';
                }

                if (c == '"')
                {
                    ParseString();
                    return '"';
                }

                int start = _pos;
                while (_pos < _text.Length)
                {
                    char l = _text[_pos];
                    bool literal = (l >= '0' && l <= '9') || (l >= 'a' && l <= 'z') || (l >= 'A' && l <= 'Z') || l == '+' || l == '-' || l == '.';
                    if (!literal) break;
                    _pos++;
                }

                if (_pos == start) throw Fail("unexpected character '" + c + "'");
                return 'l';
            }

            private string ParseString()
            {
                _pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _text.Length) throw Fail("unterminated string");
                    char c = _text[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ')
                    {
                        _pos--;
                        throw Fail("unescaped control character in a string");
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_pos >= _text.Length) throw Fail("unterminated escape sequence");
                    char e = _text[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw Fail("truncated \\u escape");
                            int code;
                            if (!int.TryParse(_text.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                            {
                                throw Fail("invalid \\u escape");
                            }

                            _pos += 4;
                            sb.Append((char)code);
                            break;
                        default:
                            _pos--;
                            throw Fail("invalid escape sequence '\\" + e + "'");
                    }
                }
            }
        }
    }

    /// <summary>The server address of an SPT <c>http.json</c>.</summary>
    public sealed class HelperHttpEndpoint
    {
        /// <summary>Where the server listens, with wildcard hosts mapped to loopback so it can be probed.</summary>
        public string ServerHost { get; set; }

        /// <summary><c>port</c>: the port the server binds.</summary>
        public int ServerPort { get; set; }

        /// <summary><c>backendPort</c>: the port clients are told to use (the BackendUrl port).</summary>
        public int BackendPort { get; set; }
    }

    /// <summary>
    /// Text-level edits of the two SPT JSON files the helper has to change. Both keep every other
    /// byte (formatting, comments, member order) exactly as it was.
    /// </summary>
    public static class HelperJsonText
    {
        private struct Edit
        {
            public int Start;
            public int End;
            public string Text;
        }

        /// <summary>
        /// Sets the top-level <c>port</c> and <c>backendPort</c> of an <c>http.json</c> to
        /// <paramref name="port"/>; a missing member is added at the end of the object. Members with
        /// those names deeper in the document are not touched.
        /// </summary>
        public static bool TryPatchHttpPorts(string text, int port, out string patched, out string error)
        {
            patched = null;
            error = null;
            if (port < 1 || port > 65535)
            {
                error = "invalid port " + port.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            HelperJsonDocument doc;
            if (!HelperJsonDocument.TryScan(text, out doc, out error)) return false;

            string number = port.ToString(CultureInfo.InvariantCulture);
            var edits = new List<Edit>();
            bool hasPort = false;
            bool hasBackendPort = false;
            HelperJsonMember first = null;
            foreach (HelperJsonMember member in doc.Members)
            {
                if (member.Depth != 1) continue;
                if (first == null) first = member;
                if (member.Key == "port")
                {
                    hasPort = true;
                    edits.Add(new Edit { Start = member.ValueStart, End = member.ValueEnd, Text = number });
                }
                else if (member.Key == "backendPort")
                {
                    hasBackendPort = true;
                    edits.Add(new Edit { Start = member.ValueStart, End = member.ValueEnd, Text = number });
                }
            }

            var missing = new List<string>();

            // Match the file's own style for what has to be added.
            string newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string indent = "  ";
            bool compact = false;
            if (first != null)
            {
                int lineStart = text.LastIndexOf('\n', first.KeyStart > 0 ? first.KeyStart - 1 : 0);
                if (lineStart < 0 || lineStart < doc.OpenBrace)
                {
                    compact = true;
                }
                else
                {
                    string lead = text.Substring(lineStart + 1, first.KeyStart - lineStart - 1);
                    if (lead.Trim().Length == 0) indent = lead;
                    else compact = true;
                }
            }

            string separator = compact ? string.Empty : newline + indent;
            string colon = compact ? ":" : ": ";
            if (!hasPort) missing.Add("\"port\"" + colon + number);
            if (!hasBackendPort) missing.Add("\"backendPort\"" + colon + number);

            if (missing.Count > 0)
            {
                var sb = new StringBuilder();
                if (doc.TopLevelCount > 0)
                {
                    foreach (string item in missing) sb.Append(',').Append(separator).Append(item);
                    edits.Add(new Edit { Start = doc.LastTopLevelValueEnd, End = doc.LastTopLevelValueEnd, Text = sb.ToString() });
                }
                else
                {
                    for (int i = 0; i < missing.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(newline).Append(indent).Append(missing[i]);
                    }

                    sb.Append(newline);
                    edits.Add(new Edit { Start = doc.OpenBrace + 1, End = doc.OpenBrace + 1, Text = sb.ToString() });
                }
            }

            patched = Apply(text, edits);
            return true;
        }

        /// <summary>
        /// Prefixes every string <c>filePattern</c> member (at any depth) of an <c>sptLogger.json</c>
        /// with <paramref name="prefix"/>. A value that already starts with the prefix is left alone,
        /// so patching twice changes nothing more. <paramref name="changed"/> counts the edited values.
        /// </summary>
        public static bool TryPrefixLogFilePatterns(string text, string prefix, out string patched, out int changed, out string error)
        {
            patched = null;
            changed = 0;
            error = null;
            if (string.IsNullOrEmpty(prefix))
            {
                error = "the prefix is empty";
                return false;
            }

            HelperJsonDocument doc;
            if (!HelperJsonDocument.TryScan(text, out doc, out error)) return false;

            var edits = new List<Edit>();
            foreach (HelperJsonMember member in doc.Members)
            {
                if (member.Key != "filePattern" || member.ValueKind != '"') continue;
                int contentStart = member.ValueStart + 1;
                if (string.CompareOrdinal(text, contentStart, prefix, 0, prefix.Length) == 0) continue;
                edits.Add(new Edit { Start = contentStart, End = contentStart, Text = prefix });
            }

            changed = edits.Count;
            patched = Apply(text, edits);
            return true;
        }

        /// <summary>
        /// Reads the addresses out of an <c>http.json</c>. <c>port</c> and <c>backendPort</c> stand in
        /// for each other when only one is given; both default to SPT's 6969.
        /// </summary>
        public static bool TryReadEndpoint(string text, out HelperHttpEndpoint endpoint, out string error)
        {
            endpoint = null;
            HelperJsonDocument doc;
            if (!HelperJsonDocument.TryScan(text, out doc, out error)) return false;

            string ip = null;
            int? port = null;
            int? backendPort = null;
            foreach (HelperJsonMember member in doc.Members)
            {
                if (member.Depth != 1) continue;
                object value;
                string parseError;
                if (member.Key != "ip" && member.Key != "port" && member.Key != "backendPort") continue;
                if (!ArenaMiniJson.TryParse(text.Substring(member.ValueStart, member.ValueEnd - member.ValueStart), out value, out parseError))
                {
                    error = "http.json member '" + member.Key + "': " + parseError;
                    return false;
                }

                if (member.Key == "ip")
                {
                    ip = value as string;
                    continue;
                }

                int parsedPort;
                if (!TryReadPortValue(value, out parsedPort))
                {
                    error = "http.json member '" + member.Key + "' is not a port number";
                    return false;
                }

                if (member.Key == "port") port = parsedPort;
                else backendPort = parsedPort;
            }

            int serverPort = port ?? backendPort ?? HelperLayout.DefaultBackendPort;
            int clientPort = backendPort ?? port ?? HelperLayout.DefaultBackendPort;
            endpoint = new HelperHttpEndpoint
            {
                ServerHost = NormaliseHost(string.IsNullOrEmpty(ip) ? "127.0.0.1" : ip),
                ServerPort = serverPort,
                BackendPort = clientPort,
            };
            return true;
        }

        /// <summary>A bind-to-everything or "localhost" address is probed through the IPv4 loopback address.</summary>
        public static string NormaliseHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return "127.0.0.1";
            string trimmed = host.Trim();
            if (trimmed == "0.0.0.0" || trimmed == "::" || trimmed == "[::]"
                || string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return "127.0.0.1";
            }

            return trimmed;
        }

        private static bool TryReadPortValue(object value, out int port)
        {
            port = 0;
            if (value is double)
            {
                double number = (double)value;
                if (Math.Floor(number) != number || number < 1 || number > 65535) return false;
                port = (int)number;
                return true;
            }

            string text = value as string;
            if (text != null && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port))
            {
                return port >= 1 && port <= 65535;
            }

            return false;
        }

        private static string Apply(string text, List<Edit> edits)
        {
            if (edits.Count == 0) return text;
            edits.Sort(delegate(Edit a, Edit b)
            {
                int byStart = b.Start.CompareTo(a.Start);
                return byStart != 0 ? byStart : b.End.CompareTo(a.End);
            });
            var sb = new StringBuilder(text);
            foreach (Edit edit in edits)
            {
                sb.Remove(edit.Start, edit.End - edit.Start);
                sb.Insert(edit.Start, edit.Text);
            }

            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------------------ plans

    /// <summary>What happens to an entry of <c>SPT_Data</c> when the arena tree is built.</summary>
    public enum HelperSptDataAction
    {
        /// <summary>A directory junction to the original folder.</summary>
        Junction,

        /// <summary>A real copy of the folder.</summary>
        CopyDirectory,

        /// <summary>A real copy of the file.</summary>
        CopyFile,
    }

    public static class HelperTreePlan
    {
        /// <summary>
        /// Every directory of <c>SPT_Data</c> is shared through a junction except <c>configs</c>, which
        /// is copied (its <c>http.json</c> is patched); files are copied.
        /// </summary>
        public static HelperSptDataAction Classify(string name, bool isDirectory)
        {
            if (!isDirectory) return HelperSptDataAction.CopyFile;
            if (string.Equals(name, HelperLayout.ConfigsFolder, StringComparison.OrdinalIgnoreCase)) return HelperSptDataAction.CopyDirectory;
            return HelperSptDataAction.Junction;
        }
    }

    public static class HelperPorts
    {
        /// <summary>
        /// The first port of 6970 to 6979 that <paramref name="isFree"/> accepts and that is not the
        /// normal server's port; -1 when none is free.
        /// </summary>
        public static int PickArenaPort(Func<int, bool> isFree, int normalPort)
        {
            for (int port = ArenaSessionContract.ArenaPort; port <= ArenaSessionContract.FallbackPortLast; port++)
            {
                if (port == normalPort) continue;
                if (isFree(port)) return port;
            }

            return -1;
        }
    }

    /// <summary>The <c>server.pid</c> file and the check that a pid still names the process the helper started.</summary>
    public static class HelperPidFile
    {
        /// <summary>Parses the content of a pid file: one positive integer, white space around it ignored.</summary>
        public static bool TryParsePid(string text, out int pid)
        {
            pid = 0;
            if (text == null) return false;
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 10) return false;
            return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
        }

        /// <summary>
        /// The helper writes the pid file right after it started the server, so the process of a
        /// genuine match started a moment before the file was written. The check allows the process
        /// to have started up to two minutes before the file's write time (a slow start) or five
        /// seconds after it (clock granularity). A process that started later (the pid was reused) or
        /// long before (the file is stale) is not ours.
        /// </summary>
        public static bool ProcessMatchesPidFile(DateTime pidFileWriteUtc, DateTime processStartUtc)
        {
            double seconds = (pidFileWriteUtc - processStartUtc).TotalSeconds;
            return seconds >= -5.0 && seconds <= 120.0;
        }
    }

    /// <summary>The <c>arena-session.json</c> marker in the server tree root.</summary>
    public sealed class HelperMarker
    {
        public int Schema { get; set; }

        public string CreatedUtc { get; set; }

        public string ProfileId { get; set; }

        public int Port { get; set; }

        public string SourceRuntime { get; set; }

        public string PolicyHash { get; set; }

        public string HelperVersion { get; set; }

        public static HelperMarker Create(DateTime createdUtc, string profileId, int port, string sourceRuntime,
            string policyHash, string helperVersion)
        {
            return new HelperMarker
            {
                Schema = ArenaSessionContract.SchemaVersion,
                CreatedUtc = createdUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ProfileId = profileId,
                Port = port,
                SourceRuntime = sourceRuntime,
                PolicyHash = policyHash,
                HelperVersion = helperVersion,
            };
        }

        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            root["schema"] = Schema;
            root["createdUtc"] = CreatedUtc;
            root["profileId"] = ProfileId;
            root["port"] = Port;
            root["sourceRuntime"] = SourceRuntime;
            root["policyHash"] = PolicyHash;
            root["helperVersion"] = HelperVersion;
            return ArenaMiniJson.Write(root, true);
        }

        public static bool TryParse(string json, out HelperMarker marker, out string error)
        {
            marker = null;
            object root;
            if (!ArenaMiniJson.TryParse(json, out root, out error)) return false;
            if (ArenaMiniJson.AsObject(root) == null)
            {
                error = "the marker must be a JSON object";
                return false;
            }

            int schema = ArenaMiniJson.GetInt(root, "schema", -1);
            if (schema != ArenaSessionContract.SchemaVersion)
            {
                error = "unsupported marker schema " + schema.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            marker = new HelperMarker
            {
                Schema = schema,
                CreatedUtc = ArenaMiniJson.GetString(root, "createdUtc"),
                ProfileId = ArenaMiniJson.GetString(root, "profileId"),
                Port = ArenaMiniJson.GetInt(root, "port", 0),
                SourceRuntime = ArenaMiniJson.GetString(root, "sourceRuntime"),
                PolicyHash = ArenaMiniJson.GetString(root, "policyHash"),
                HelperVersion = ArenaMiniJson.GetString(root, "helperVersion"),
            };
            return true;
        }
    }

    /// <summary>The line format of <c>helper.log</c>.</summary>
    public static class HelperLogFormat
    {
        public static string FormatLine(DateTime utc, int processId, string level, string message)
        {
            string text = (message ?? string.Empty).Replace("\r\n", " | ").Replace('\n', ' ').Replace('\r', ' ');
            return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
                + " [" + processId.ToString(CultureInfo.InvariantCulture) + "] " + level + " " + text;
        }

        public static bool ShouldRotate(long length)
        {
            return length >= HelperLayout.LogRotateBytes;
        }
    }
}
