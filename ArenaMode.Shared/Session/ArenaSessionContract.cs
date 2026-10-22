// Linked source, see ArenaMiniJson.cs for the constraints (netstandard2.1, net472, net10.0; C# 7.3
// syntax; no nullable annotations; BCL only).
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ArenaMode.Shared.Session
{
    /// <summary>
    /// The constants the five parts of the Arena session feature agree on: gate (preloader patcher),
    /// session helper, client, server and tests. Specification:
    /// docs/ARENA-SESSION-20261021.md. Change a value here and in that file together.
    /// </summary>
    public static class ArenaSessionContract
    {
        // ------------------------------------------------------------------ process contract

        /// <summary>Command-line flag that marks an Arena session game process (case-insensitive).</summary>
        public const string CommandLineFlag = "-arena-session";

        /// <summary>AppDomain data key under which the gate publishes its report JSON.</summary>
        public const string GateReportDataKey = "com.maschine.ArenaMode.Gate.Report";

        /// <summary>Gate report / policy file schema.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The arena server's port (6971 to 6979 when a foreign process holds it).</summary>
        public const int ArenaPort = 6970;

        public const int FallbackPortFirst = 6971;
        public const int FallbackPortLast = 6979;

        // ------------------------------------------------------------------ file names

        /// <summary>Folder below <c>%LOCALAPPDATA%</c> that holds the session runtime root.</summary>
        public const string RuntimeParentFolderName = "maschine-ArenaMode";

        public const string RuntimeRootFolderName = "session";

        /// <summary>The disposable server tree inside the runtime root; also the server's working directory.</summary>
        public const string ServerTreeFolderName = "server";

        public const string RequestFileName = "request.json";
        public const string HelperLogFileName = "helper.log";

        /// <summary>Marker in the server tree root: its existence makes a server an Arena instance.</summary>
        public const string MarkerFileName = "arena-session.json";

        /// <summary>Process id of the arena server, written by the helper next to the server tree.</summary>
        public const string ServerPidFileName = "server.pid";

        public const string HelperFileName = "maschine-ArenaMode.Session.exe";
        public const string GateFileName = "maschine-ArenaMode.Gate.dll";

        /// <summary>Plugin folder name below <c>BepInEx/plugins</c> and <c>BepInEx/patchers</c>.</summary>
        public const string PluginFolderName = "maschine-ArenaMode";

        /// <summary>Shipped default policy, next to the client DLL.</summary>
        public const string DefaultPolicyFileName = "arena-whitelist.json";

        /// <summary>Optional user policy in <c>BepInEx/config</c>; never shipped.</summary>
        public const string UserPolicyFileName = "com.maschine.ArenaMode.whitelist.json";

        /// <summary>Named mutex that keeps one helper instance at a time.</summary>
        public const string HelperMutexName = "Local\\maschine-ArenaMode.Session";

        public const string DirectionEnter = "enter";
        public const string DirectionLeave = "leave";

        // ------------------------------------------------------------------ code-defined units

        /// <summary>The ArenaMode client plugin and server mod GUID.</summary>
        public const string ArenaPluginGuid = "com.maschine.ArenaMode";

        public const string ConfigurationManagerGuid = "com.bepis.bepinex.configurationmanager";

        /// <summary>Every plugin GUID that starts with this is Fika and is never filtered.</summary>
        public const string FikaGuidPrefix = "com.fika.";

        /// <summary>The SPT version the platform units were reviewed with.</summary>
        public const string SptReviewedVersion = "4.1.6";

        /// <summary>SPT's own client plugins (platform tier, client only).</summary>
        public static readonly IReadOnlyList<string> SptPlatformGuids = Array.AsReadOnly(new[]
        {
            "com.SPT.core",
            "com.SPT.custom",
            "com.SPT.debugging",
            "com.SPT.singleplayer",
        });

        /// <summary>Preloader patchers that always run (platform tier).</summary>
        public static readonly IReadOnlyList<string> PlatformPatcherFiles = Array.AsReadOnly(new[]
        {
            "FixPluginTypesSerialization.dll",
            "spt-prepatch.dll",
            GateFileName,
        });

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// True when <paramref name="args"/> (normally <c>Environment.GetCommandLineArgs()</c>)
        /// contains <see cref="CommandLineFlag"/>, compared case-insensitively. The first element
        /// is the executable path and can never match.
        /// </summary>
        public static bool HasSessionFlag(IEnumerable<string> args)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (arg != null && string.Equals(arg.Trim(), CommandLineFlag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// <c>%LOCALAPPDATA%\maschine-ArenaMode\session</c>, or null when the local application data
        /// folder cannot be determined (callers must treat that as "no session support", never fall
        /// back to a relative path).
        /// </summary>
        public static string GetRuntimeRoot()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) return null;
            return Path.Combine(Path.Combine(local, RuntimeParentFolderName), RuntimeRootFolderName);
        }

        /// <summary><c>&lt;root&gt;\server</c>, or null without a runtime root.</summary>
        public static string GetServerTreePath()
        {
            string root = GetRuntimeRoot();
            return root == null ? null : Path.Combine(root, ServerTreeFolderName);
        }

        /// <summary><c>&lt;root&gt;\request.json</c>, or null without a runtime root.</summary>
        public static string GetRequestFilePath()
        {
            string root = GetRuntimeRoot();
            return root == null ? null : Path.Combine(root, RequestFileName);
        }

        /// <summary><c>&lt;root&gt;\helper.log</c>, or null without a runtime root.</summary>
        public static string GetHelperLogPath()
        {
            string root = GetRuntimeRoot();
            return root == null ? null : Path.Combine(root, HelperLogFileName);
        }
    }

    /// <summary>
    /// What the gate publishes through <see cref="ArenaSessionContract.GateReportDataKey"/> and
    /// what the client reads back: whether the filter is active, which plugins it let through or
    /// removed, the warnings and the patchers it neutralised. JSON shape:
    /// <c>{ "schema":1, "active":bool, "error":string|null, "policyHash":string,
    /// "allowed":[{guid,name,version,file,reason}], "excluded":[...], "warnings":[string],
    /// "patchersNeutralised":[file] }</c>. Unknown members are ignored when reading. A unit's
    /// <c>reason</c> is one of the <c>ArenaWhitelistDecision.Reason*</c> values: <c>platform</c>,
    /// <c>arena</c>, <c>whitelist</c>, <c>tool</c>, <c>unlisted-allowed</c> (blacklist mode),
    /// <c>dependency-of:&lt;guid&gt;</c> for allowed units, <c>not-on-whitelist</c>,
    /// <c>denied-by-user</c> (the user file's deny list) or <c>denied</c> (the shipped default
    /// file's deny list) for excluded ones.
    /// </summary>
    public sealed class ArenaGateReport
    {
        public ArenaGateReport()
        {
            Schema = ArenaSessionContract.SchemaVersion;
            Allowed = new List<ArenaSessionUnit>();
            Excluded = new List<ArenaSessionUnit>();
            Warnings = new List<string>();
            PatchersNeutralised = new List<string>();
        }

        public int Schema { get; set; }

        /// <summary>True only when the filter ran to completion; false means nothing was filtered.</summary>
        public bool Active { get; set; }

        /// <summary>Why the gate is not active; null while active.</summary>
        public string Error { get; set; }

        public string PolicyHash { get; set; }

        public List<ArenaSessionUnit> Allowed { get; set; }

        public List<ArenaSessionUnit> Excluded { get; set; }

        public List<string> Warnings { get; set; }

        public List<string> PatchersNeutralised { get; set; }

        /// <summary>A report for a gate that did not filter anything.</summary>
        public static ArenaGateReport Inactive(string error, string policyHash)
        {
            return new ArenaGateReport { Active = false, Error = error, PolicyHash = policyHash };
        }

        /// <summary>
        /// An active report built from a decision. The unit lists are copied, so later changes to
        /// the decision do not leak in.
        /// </summary>
        public static ArenaGateReport FromDecision(ArenaWhitelistPolicy policy, ArenaWhitelistDecision decision,
            IEnumerable<string> patchersNeutralised)
        {
            if (policy == null) throw new ArgumentNullException("policy");
            if (decision == null) throw new ArgumentNullException("decision");

            var report = new ArenaGateReport { Active = true, Error = null, PolicyHash = policy.Hash };
            report.Allowed.AddRange(decision.Allowed);
            report.Excluded.AddRange(decision.Excluded);
            report.Warnings.AddRange(decision.Warnings);
            if (patchersNeutralised != null) report.PatchersNeutralised.AddRange(patchersNeutralised);
            return report;
        }

        /// <summary>The log line the gate writes: <c>Arena session: N plugins allowed, M excluded, K warnings</c>.</summary>
        public string SummaryLine()
        {
            return "Arena session: " + Allowed.Count.ToString(CultureInfo.InvariantCulture) + " plugins allowed, "
                + Excluded.Count.ToString(CultureInfo.InvariantCulture) + " excluded, "
                + Warnings.Count.ToString(CultureInfo.InvariantCulture) + " warnings";
        }

        /// <summary>Compact JSON in the documented shape.</summary>
        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            root["schema"] = Schema;
            root["active"] = Active;
            root["error"] = Error;
            root["policyHash"] = PolicyHash;
            root["allowed"] = UnitsToJson(Allowed);
            root["excluded"] = UnitsToJson(Excluded);
            root["warnings"] = Warnings == null ? new List<string>() : Warnings;
            root["patchersNeutralised"] = PatchersNeutralised == null ? new List<string>() : PatchersNeutralised;
            return ArenaMiniJson.Write(root);
        }

        /// <summary>
        /// Reads a report. Returns false (report null, a message in <paramref name="error"/>) for
        /// malformed JSON, a non-object top level, or a schema other than
        /// <see cref="ArenaSessionContract.SchemaVersion"/>. Missing members read as empty.
        /// </summary>
        public static bool TryParse(string json, out ArenaGateReport report, out string error)
        {
            report = null;
            object root;
            if (!ArenaMiniJson.TryParse(json, out root, out error)) return false;

            var obj = ArenaMiniJson.AsObject(root);
            if (obj == null)
            {
                error = "the gate report must be a JSON object";
                return false;
            }

            int schema = ArenaMiniJson.GetInt(obj, "schema", -1);
            if (schema != ArenaSessionContract.SchemaVersion)
            {
                error = "unsupported gate report schema " + schema.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            var parsed = new ArenaGateReport
            {
                Schema = schema,
                Active = ArenaMiniJson.GetBool(obj, "active", false),
                Error = ArenaMiniJson.GetString(obj, "error"),
                PolicyHash = ArenaMiniJson.GetString(obj, "policyHash"),
            };
            UnitsFromJson(ArenaMiniJson.GetArray(obj, "allowed"), parsed.Allowed);
            UnitsFromJson(ArenaMiniJson.GetArray(obj, "excluded"), parsed.Excluded);
            StringsFromJson(ArenaMiniJson.GetArray(obj, "warnings"), parsed.Warnings);
            StringsFromJson(ArenaMiniJson.GetArray(obj, "patchersNeutralised"), parsed.PatchersNeutralised);
            report = parsed;
            error = null;
            return true;
        }

        private static List<object> UnitsToJson(List<ArenaSessionUnit> units)
        {
            var list = new List<object>();
            if (units == null) return list;
            foreach (ArenaSessionUnit unit in units)
            {
                if (unit == null) continue;
                var item = new Dictionary<string, object>();
                item["guid"] = unit.Guid;
                item["name"] = unit.Name;
                item["version"] = unit.Version;
                item["file"] = unit.File;
                item["reason"] = unit.Reason;
                list.Add(item);
            }

            return list;
        }

        private static void UnitsFromJson(List<object> source, List<ArenaSessionUnit> target)
        {
            if (source == null) return;
            foreach (object item in source)
            {
                if (ArenaMiniJson.AsObject(item) == null) continue;
                target.Add(new ArenaSessionUnit
                {
                    Guid = ArenaMiniJson.GetString(item, "guid"),
                    Name = ArenaMiniJson.GetString(item, "name"),
                    Version = ArenaMiniJson.GetString(item, "version"),
                    File = ArenaMiniJson.GetString(item, "file"),
                    Reason = ArenaMiniJson.GetString(item, "reason"),
                });
            }
        }

        private static void StringsFromJson(List<object> source, List<string> target)
        {
            if (source == null) return;
            foreach (object item in source)
            {
                string text = item as string;
                if (text != null) target.Add(text);
            }
        }
    }
}
