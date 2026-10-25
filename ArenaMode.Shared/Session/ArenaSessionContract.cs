// Linked source, see ArenaMiniJson.cs for the constraints (netstandard2.1, net472, net10.0; C# 7.3
// syntax; no nullable annotations; BCL only).
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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

        public const string VersionPlanFlag = "-arena-version-plan=";
        public const int VersionPlanRequestSchema = 2;
        // An old helper must refuse rather than silently leave the ordinary server alive.
        public const int OfflineRequestSchema = 3;

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

        /// <summary>
        /// Prefix of the named mutex that keeps one helper instance at a time; the helper appends the SID of the Windows
        /// user it runs as (<c>HelperMutex.Name</c> in the helper). <c>Global\</c>, because a <c>Local\</c> object is one
        /// per Windows session while the runtime root, <c>server.pid</c> and the tree are one per user (review 2026-10-23:
        /// two sessions of one account, a console and a Remote Desktop one, each had a mutex of their own over the same
        /// files). The SID keeps the accounts of one machine apart in the shared namespace, and the helper creates the
        /// mutex with an access list for that user and SYSTEM only (the default list depends on the creating token).
        /// </summary>
        public const string HelperMutexNamePrefix = "Global\\maschine-ArenaMode.Session.";

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
    /// Optional, disposable session restriction, derived from both loaded-mod inventories. It is
    /// carried by the schema-2 request, server marker and gate report. The process argument pins its
    /// effective hash; an old helper rejects schema 2, and an old gate cannot prove that it applied it.
    /// </summary>
    public sealed class ArenaSessionVersionPlan
    {
        public string BasePolicyHash { get; private set; }
        public string PolicyHash { get; private set; }
        public List<string> ExcludedGuids { get; private set; } = new List<string>();
        public List<ArenaSessionUnit> ClientUnits { get; private set; } = new List<ArenaSessionUnit>();
        public List<ArenaSessionUnit> ServerUnits { get; private set; } = new List<ArenaSessionUnit>();

        public static ArenaSessionVersionPlan Create(ArenaWhitelistPolicy policy,
            IEnumerable<ArenaSessionUnit> client, IEnumerable<ArenaSessionUnit> server)
        {
            var plan = new ArenaSessionVersionPlan { BasePolicyHash = policy.Hash };
            plan.ClientUnits.AddRange(client);
            plan.ServerUnits.AddRange(server);
            var omitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDrift(policy, ArenaWhitelistSide.Client, plan.ClientUnits, omitted);
            AddDrift(policy, ArenaWhitelistSide.Server, plan.ServerUnits, omitted);
            bool changed;
            do
            {
                changed = AddDependants(policy, plan.ClientUnits, omitted);
                changed |= AddDependants(policy, plan.ServerUnits, omitted);
            } while (changed);
            foreach (string guid in omitted) plan.ExcludedGuids.Add(guid.ToLowerInvariant());
            plan.ExcludedGuids.Sort(StringComparer.Ordinal);
            plan.PolicyHash = policy.WithSessionExclusions(plan.ExcludedGuids).Hash;
            return plan;
        }

        private static void AddDrift(ArenaWhitelistPolicy policy, ArenaWhitelistSide side,
            List<ArenaSessionUnit> units, HashSet<string> omitted)
        {
            foreach (var difference in ArenaWhitelistDecision.Evaluate(policy, side, units).VersionDifferences)
                if (!Protected(policy, difference.Guid)) omitted.Add(difference.Guid);
        }

        private static bool Protected(ArenaWhitelistPolicy policy, string guid)
        {
            var entry = policy.FindPlugin(guid, ArenaWhitelistSide.Client) ?? policy.FindPlugin(guid, ArenaWhitelistSide.Server);
            return entry != null && (entry.Tier == ArenaWhitelistTier.Platform || entry.Tier == ArenaWhitelistTier.Arena);
        }

        private static bool AddDependants(ArenaWhitelistPolicy policy, List<ArenaSessionUnit> units, HashSet<string> omitted)
        {
            bool changed = false;
            foreach (var unit in units)
            {
                if (omitted.Contains(unit.Guid) || unit.HardDependencies == null) continue;
                foreach (string dependency in unit.HardDependencies)
                {
                    if (!omitted.Contains(dependency)) continue;
                    if (Protected(policy, unit.Guid))
                        throw new InvalidOperationException("a required platform/Arena unit depends on an omitted version: " + unit.DisplayName);
                    changed |= omitted.Add(unit.Guid);
                    break;
                }
            }
            return changed;
        }

        public bool TryApply(ArenaWhitelistPolicy policy, out ArenaWhitelistPolicy effective, out string error)
        {
            effective = null;
            error = null;
            if (!string.Equals(BasePolicyHash, policy.Hash, StringComparison.Ordinal))
            {
                error = "the whitelist policy changed after the version-filtered session was prepared";
                return false;
            }
            try
            {
                var rebuilt = Create(policy, ClientUnits, ServerUnits);
                if (!string.Equals(PolicyHash, rebuilt.PolicyHash, StringComparison.Ordinal)
                    || string.Join("|", ExcludedGuids.ToArray()) != string.Join("|", rebuilt.ExcludedGuids.ToArray()))
                {
                    error = "the version-filtered session plan changed or does not match its mod inventories";
                    return false;
                }
                effective = policy.WithSessionExclusions(ExcludedGuids);
                return true;
            }
            catch (Exception ex) { error = "the version-filtered session plan is invalid: " + ex.Message; return false; }
        }

        public Dictionary<string, object> ToObject()
        {
            return new Dictionary<string, object> {
                { "schema", 1 }, { "basePolicyHash", BasePolicyHash }, { "policyHash", PolicyHash },
                { "excludedGuids", ExcludedGuids }, { "clientUnits", WriteUnits(ClientUnits) }, { "serverUnits", WriteUnits(ServerUnits) }
            };
        }

        private static List<object> WriteUnits(List<ArenaSessionUnit> units)
        {
            var result = new List<object>();
            foreach (var unit in units) result.Add(new Dictionary<string, object> {
                { "guid", unit.Guid }, { "name", unit.Name }, { "version", unit.Version }, { "file", unit.File },
                { "dependencies", unit.HardDependencies ?? new List<string>() }
            });
            return result;
        }

        public static bool TryRead(object value, out ArenaSessionVersionPlan plan, out string error)
        {
            plan = null;
            error = "the version-filtered session plan is missing or malformed";
            var obj = ArenaMiniJson.AsObject(value);
            if (obj == null || ArenaMiniJson.GetInt(obj, "schema", -1) != 1) return false;
            var parsed = new ArenaSessionVersionPlan {
                BasePolicyHash = ArenaMiniJson.GetString(obj, "basePolicyHash"),
                PolicyHash = ArenaMiniJson.GetString(obj, "policyHash")
            };
            if (string.IsNullOrWhiteSpace(parsed.BasePolicyHash) || string.IsNullOrWhiteSpace(parsed.PolicyHash)) return false;
            var exclusions = ArenaMiniJson.GetArray(obj, "excludedGuids");
            if (exclusions == null || exclusions.Count == 0) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in exclusions)
            {
                string guid = item as string;
                if (string.IsNullOrWhiteSpace(guid) || guid != guid.Trim() || !seen.Add(guid)) return false;
                parsed.ExcludedGuids.Add(guid);
            }
            if (!ReadUnits(obj, "clientUnits", parsed.ClientUnits) || !ReadUnits(obj, "serverUnits", parsed.ServerUnits)) return false;
            plan = parsed;
            error = null;
            return true;
        }

        private static bool ReadUnits(object obj, string key, List<ArenaSessionUnit> units)
        {
            var source = ArenaMiniJson.GetArray(obj, key);
            if (source == null || source.Count > 4096) return false;
            foreach (object item in source)
            {
                string guid = ArenaMiniJson.GetString(item, "guid");
                if (string.IsNullOrWhiteSpace(guid)) return false;
                var dependencies = ArenaMiniJson.GetArray(item, "dependencies");
                if (dependencies == null) return false;
                var parsed = new List<string>();
                foreach (object dependency in dependencies)
                {
                    string text = dependency as string;
                    if (string.IsNullOrWhiteSpace(text)) return false;
                    parsed.Add(text);
                }
                units.Add(new ArenaSessionUnit(guid, ArenaMiniJson.GetString(item, "name"),
                    ArenaMiniJson.GetString(item, "version"), ArenaMiniJson.GetString(item, "file"), parsed));
            }
            return true;
        }

        /// <summary>Null means a legacy session; an empty/duplicate flag is deliberately invalid.</summary>
        public static string ExpectedHash(IEnumerable<string> arguments)
        {
            string hash = null;
            if (arguments == null) return null;
            foreach (string argument in arguments)
            {
                if (argument == null || !argument.StartsWith(ArenaSessionContract.VersionPlanFlag, StringComparison.OrdinalIgnoreCase)) continue;
                if (hash != null) return string.Empty;
                hash = argument.Substring(ArenaSessionContract.VersionPlanFlag.Length);
            }
            return hash;
        }

        public bool ValidateAllowed(ArenaWhitelistPolicy effective, ArenaWhitelistSide side,
            IEnumerable<ArenaSessionUnit> units, out string error)
        {
            error = null;
            foreach (var unit in units)
            {
                string reviewed = effective.ReviewedVersion(unit.Guid, side);
                bool forbidden = effective.IsDeniedForSession(unit.Guid)
                    || (!Protected(effective, unit.Guid) && !string.IsNullOrEmpty(reviewed)
                        && ArenaWhitelistPolicy.VersionDiffers(unit.Version, reviewed));
                if (unit.HardDependencies != null)
                    foreach (string dependency in unit.HardDependencies) forbidden |= effective.IsDeniedForSession(dependency);
                if (!forbidden) continue;
                error = "the installed mods changed after the version-filtered session was prepared: " + unit.DisplayName;
                return false;
            }
            return true;
        }

        public static bool TryReadMarker(string markerPath, string expectedHash, string sourceRuntime,
            out ArenaSessionVersionPlan plan, out string error)
        {
            plan = null;
            string text, detail;
            var state = ArenaPolicyFiles.TryRead(markerPath, out text, out detail);
            error = "cannot read the version-filtered session marker: " + ArenaPolicyFiles.Describe(state, detail);
            if (state != ArenaPolicyFileState.Read) return false;
            object value;
            if (!ArenaMiniJson.TryParse(text, out value, out error)) return false;
            var obj = ArenaMiniJson.AsObject(value);
            object raw;
            if (obj == null || !obj.TryGetValue("versionPlan", out raw))
            {
                error = "the version-filtered session marker has no plan";
                return false;
            }
            if (!TryRead(raw, out plan, out error)) return false;
            if (string.IsNullOrEmpty(expectedHash) || !string.Equals(expectedHash, plan.PolicyHash, StringComparison.Ordinal)
                || !string.Equals(ArenaMiniJson.GetString(obj, "policyHash"), plan.PolicyHash, StringComparison.Ordinal)
                || !string.Equals(Path.GetFullPath(ArenaMiniJson.GetString(obj, "sourceRuntime") ?? ".").TrimEnd('\\', '/'),
                    Path.GetFullPath(sourceRuntime).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                plan = null;
                error = "the version-filtered session marker belongs to another plan or installation";
                return false;
            }
            return true;
        }
    }

    /// <summary>What <see cref="ArenaPolicyFiles.TryRead"/> found at a path.</summary>
    public enum ArenaPolicyFileState
    {
        /// <summary>There is no such file.</summary>
        Absent,

        /// <summary>The text was read in full.</summary>
        Read,

        /// <summary>The file is larger than <see cref="ArenaPolicyFiles.MaxBytes"/>; none of it is used.</summary>
        TooLarge,

        /// <summary>The file exists but could not be read (locked, access denied, I/O error).</summary>
        Unreadable,
    }

    /// <summary>
    /// The one way the client and the gate read the two policy files (the shipped default and the user's own),
    /// docs/ARENA-SESSION-20261021.md sections 3, 5 and 7. They must read the same bytes into the same policy:
    /// the client builds the request (and so the server tree) from its reading and the gate applies its own, and
    /// the Arena session refuses when the two policy hashes differ. A size limit that only one side enforced
    /// would make them disagree about the same file for ever, so there is one reader, one limit and one decoding.
    /// A file above <see cref="MaxBytes"/> is not a policy file; none of it is used.
    /// </summary>
    public static class ArenaPolicyFiles
    {
        /// <summary>The largest policy file that is read: 256 KB.</summary>
        public const int MaxBytes = 256 * 1024;

        /// <summary>
        /// Reads the file as UTF-8 text (a byte order mark is honoured, like <c>File.ReadAllText</c>), at most
        /// <see cref="MaxBytes"/> bytes of it, with the sharing mode of <c>File.ReadAllText</c>: a file that an editor
        /// holds open for writing is <see cref="ArenaPolicyFileState.Unreadable"/>, not read half-written. Never
        /// throws. <paramref name="detail"/> says why for <see cref="ArenaPolicyFileState.TooLarge"/> and
        /// <see cref="ArenaPolicyFileState.Unreadable"/>; otherwise it is null, and so is <paramref name="text"/> unless
        /// the state is <see cref="ArenaPolicyFileState.Read"/>.
        /// </summary>
        public static ArenaPolicyFileState TryRead(string path, out string text, out string detail)
        {
            text = null;
            detail = null;
            try
            {
                if (!File.Exists(path)) return ArenaPolicyFileState.Absent;

                // One byte more than the limit is read, so that a file that grows between a size check and the
                // read cannot get past it.
                byte[] buffer = new byte[MaxBytes + 1];
                int total = 0;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    while (total < buffer.Length)
                    {
                        int read = stream.Read(buffer, total, buffer.Length - total);
                        if (read <= 0) break;
                        total += read;
                    }
                }

                if (total > MaxBytes)
                {
                    detail = "larger than " + (MaxBytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
                    return ArenaPolicyFileState.TooLarge;
                }

                using (var reader = new StreamReader(new MemoryStream(buffer, 0, total), Encoding.UTF8, true))
                {
                    text = reader.ReadToEnd();
                }

                return ArenaPolicyFileState.Read;
            }
            catch (Exception ex)
            {
                text = null;
                detail = ex.Message;
                return ArenaPolicyFileState.Unreadable;
            }
        }

        /// <summary>A short phrase for the state, for messages that follow a colon.</summary>
        public static string Describe(ArenaPolicyFileState state, string detail)
        {
            if (state == ArenaPolicyFileState.Absent) return "the file does not exist";
            return string.IsNullOrEmpty(detail) ? state.ToString() : detail;
        }
    }

    /// <summary>
    /// What the gate publishes through <see cref="ArenaSessionContract.GateReportDataKey"/> and
    /// what the client reads back: whether the filter is active, which plugins it let through or
    /// removed, the warnings and the patchers it neutralised. JSON shape:
    /// <c>{ "schema":1, "active":bool, "error":string|null, "policyHash":string,
    /// "unlistedAllowed":bool, "allowed":[{guid,name,version,file,reason}], "excluded":[...],
    /// "warnings":[string], "patchersNeutralised":[file], "patchersRanBeforeGate":[file],
    /// "patchersAllowed":[file] }</c>.
    /// Unknown members are ignored when reading; a missing <c>unlistedAllowed</c> reads as false and a
    /// missing <c>patchersRanBeforeGate</c> or <c>patchersAllowed</c> (a report of an older gate) as empty. A unit's
    /// <c>reason</c> is one of the <c>ArenaWhitelistDecision.Reason*</c> values: <c>platform</c>,
    /// <c>arena</c>, <c>whitelist</c>, <c>tool</c>, <c>unlisted-allowed</c> (blacklist mode),
    /// <c>dependency-of:&lt;guid&gt;</c> for allowed units, <c>not-on-whitelist</c>,
    /// <c>denied-by-user</c> (the user file's deny list), <c>denied</c> (the shipped default
    /// file's deny list) or <c>requires-denied:&lt;guid&gt;</c> (a hard dependency, directly or
    /// through other units, is denied) for excluded ones.
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
            PatchersRanBeforeGate = new List<string>();
            PatchersAllowed = new List<string>();
        }

        public int Schema { get; set; }

        /// <summary>True only when the filter ran to completion; false means nothing was filtered.</summary>
        public bool Active { get; set; }

        /// <summary>Why the gate is not active; null while active.</summary>
        public string Error { get; set; }

        public string PolicyHash { get; set; }

        /// <summary>
        /// True when the policy the gate applied lets units through that nothing names (blacklist mode,
        /// switched on by <c>"unlisted": "allow"</c> in the user file). Set on an active report; it is
        /// the policy's mode, whether or not an installed plugin was actually let through by it.
        /// </summary>
        public bool UnlistedAllowed { get; set; }

        public ArenaSessionVersionPlan VersionPlan { get; set; }

        public List<ArenaSessionUnit> Allowed { get; set; }

        public List<ArenaSessionUnit> Excluded { get; set; }

        public List<string> Warnings { get; set; }

        public List<string> PatchersNeutralised { get; set; }

        /// <summary>
        /// File names of the patchers in <see cref="PatchersNeutralised"/> whose initializer had already run when the
        /// gate got to them: they sort before the gate in the preloader's patcher list, so only their target list,
        /// patch method and finalizer could still be switched off. For those patchers the process is not clean even
        /// though the gate is active. Always a subset of <see cref="PatchersNeutralised"/>; empty when none, and also
        /// empty when the gate's own position in the list could not be determined (the report then carries a warning
        /// that says so).
        /// </summary>
        public List<string> PatchersRanBeforeGate { get; set; }

        /// <summary>
        /// File names of the preloader patchers the gate let run because the policy of the moment allowed them (each
        /// once; platform patchers included, never the gate itself or BepInEx's own patcher, which are not judged).
        /// The client judges them again with the policy as it reads now (review 2026-10-23): a patcher allowed at
        /// the start and denied since has run in this process, which neither the plugin nor the server-mod
        /// comparison can see. Present in every report (provisional, final and inactive); a report without the
        /// member (an older gate) reads as empty.
        /// </summary>
        public List<string> PatchersAllowed { get; set; }

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
            IEnumerable<string> patchersNeutralised, IEnumerable<string> patchersRanBeforeGate = null,
            IEnumerable<string> patchersAllowed = null)
        {
            if (policy == null) throw new ArgumentNullException("policy");
            if (decision == null) throw new ArgumentNullException("decision");

            var report = new ArenaGateReport
            {
                Active = true,
                Error = null,
                PolicyHash = policy.Hash,
                UnlistedAllowed = policy.AllowsUnlisted,
            };
            report.Allowed.AddRange(decision.Allowed);
            report.Excluded.AddRange(decision.Excluded);
            report.Warnings.AddRange(decision.Warnings);
            if (patchersNeutralised != null) report.PatchersNeutralised.AddRange(patchersNeutralised);
            if (patchersRanBeforeGate != null) report.PatchersRanBeforeGate.AddRange(patchersRanBeforeGate);
            if (patchersAllowed != null) report.PatchersAllowed.AddRange(patchersAllowed);
            return report;
        }

        /// <summary>Appended to <see cref="SummaryLine"/> while <see cref="UnlistedAllowed"/> is true.</summary>
        public const string PolicyOverrideSummarySuffix = " (policy override: unlisted mods allowed)";

        /// <summary>
        /// The log line the gate writes: <c>Arena session: N plugins allowed, M excluded, K warnings</c>,
        /// followed by <see cref="PolicyOverrideSummarySuffix"/> when the policy allows unlisted units and by
        /// <c> (N patcher(s) ran before the gate)</c> when <see cref="PatchersRanBeforeGate"/> is not empty.
        /// </summary>
        public string SummaryLine()
        {
            string line = "Arena session: " + Allowed.Count.ToString(CultureInfo.InvariantCulture) + " plugins allowed, "
                + Excluded.Count.ToString(CultureInfo.InvariantCulture) + " excluded, "
                + Warnings.Count.ToString(CultureInfo.InvariantCulture) + " warnings"
                + (UnlistedAllowed ? PolicyOverrideSummarySuffix : string.Empty);
            if (PatchersRanBeforeGate != null && PatchersRanBeforeGate.Count > 0)
            {
                line += " (" + PatchersRanBeforeGate.Count.ToString(CultureInfo.InvariantCulture) + " patcher(s) ran before the gate)";
            }

            return line;
        }

        /// <summary>Compact JSON in the documented shape.</summary>
        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            root["schema"] = Schema;
            root["active"] = Active;
            root["error"] = Error;
            root["policyHash"] = PolicyHash;
            root["unlistedAllowed"] = UnlistedAllowed;
            if (VersionPlan != null) root["versionPlan"] = VersionPlan.ToObject();
            root["allowed"] = UnitsToJson(Allowed);
            root["excluded"] = UnitsToJson(Excluded);
            root["warnings"] = Warnings == null ? new List<string>() : Warnings;
            root["patchersNeutralised"] = PatchersNeutralised == null ? new List<string>() : PatchersNeutralised;
            root["patchersRanBeforeGate"] = PatchersRanBeforeGate == null ? new List<string>() : PatchersRanBeforeGate;
            root["patchersAllowed"] = PatchersAllowed == null ? new List<string>() : PatchersAllowed;
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
                UnlistedAllowed = ArenaMiniJson.GetBool(obj, "unlistedAllowed", false),
            };
            object rawPlan;
            if (obj.TryGetValue("versionPlan", out rawPlan))
            {
                ArenaSessionVersionPlan plan;
                if (!ArenaSessionVersionPlan.TryRead(rawPlan, out plan, out error)) return false;
                parsed.VersionPlan = plan;
            }
            UnitsFromJson(ArenaMiniJson.GetArray(obj, "allowed"), parsed.Allowed);
            UnitsFromJson(ArenaMiniJson.GetArray(obj, "excluded"), parsed.Excluded);
            StringsFromJson(ArenaMiniJson.GetArray(obj, "warnings"), parsed.Warnings);
            StringsFromJson(ArenaMiniJson.GetArray(obj, "patchersNeutralised"), parsed.PatchersNeutralised);
            StringsFromJson(ArenaMiniJson.GetArray(obj, "patchersRanBeforeGate"), parsed.PatchersRanBeforeGate);
            StringsFromJson(ArenaMiniJson.GetArray(obj, "patchersAllowed"), parsed.PatchersAllowed);
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
