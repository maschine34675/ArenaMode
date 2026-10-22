// Linked source, see ArenaMiniJson.cs for the constraints (netstandard2.1, net472, net10.0; C# 7.3
// syntax; no nullable annotations; BCL only).
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ArenaMode.Shared.Session
{
    /// <summary>Which half of Arena a decision is made for: the BepInEx client or the SPT server.</summary>
    public enum ArenaWhitelistSide
    {
        Client = 0,
        Server = 1,
    }

    /// <summary>
    /// Where a whitelisted unit comes from. Platform and Arena are code constants: neither the shipped
    /// default file nor the user file can replace, change or remove them; Mod and Tool come from the
    /// shipped default file or the user file.
    /// </summary>
    public enum ArenaWhitelistTier
    {
        /// <summary>SPT itself, Configuration Manager, Fika (never filtered), the platform patchers.</summary>
        Platform = 0,

        /// <summary>ArenaMode itself.</summary>
        Arena = 1,

        /// <summary>A reviewed third-party mod (<c>"kind": "mod"</c>).</summary>
        Mod = 2,

        /// <summary>A development or test tool (<c>"kind": "tool"</c>).</summary>
        Tool = 3,
    }

    /// <summary>
    /// What the policy does with a unit that no entry names and that neither the user file nor the
    /// default file denies: the user file's optional <c>"unlisted"</c> member. <see cref="Deny"/> is
    /// the whitelist behaviour (the default), <see cref="Allow"/> the blacklist behaviour.
    /// </summary>
    public enum ArenaUnlistedMode
    {
        /// <summary>Whitelist: only the listed units load, everything else is excluded.</summary>
        Deny = 0,

        /// <summary>Blacklist: everything that neither the user file nor the default file denies loads.</summary>
        Allow = 1,
    }

    /// <summary>
    /// One allowed unit: a plugin or server mod identified by GUID, or a preloader patcher
    /// identified by file name. Immutable.
    /// </summary>
    public sealed class ArenaWhitelistEntry
    {
        public ArenaWhitelistEntry(string guid, string patcher, string name, ArenaWhitelistTier tier,
            string clientVersion, string serverVersion, bool appliesToClient, bool appliesToServer)
        {
            Guid = guid;
            Patcher = patcher;
            Name = string.IsNullOrEmpty(name) ? (guid ?? patcher) : name;
            Tier = tier;
            ClientVersion = string.IsNullOrEmpty(clientVersion) ? null : clientVersion;
            ServerVersion = string.IsNullOrEmpty(serverVersion) ? null : serverVersion;
            AppliesToClient = appliesToClient;
            AppliesToServer = appliesToServer;
        }

        /// <summary>BepInEx plugin GUID / SPT server mod GUID; null for a patcher entry.</summary>
        public string Guid { get; private set; }

        /// <summary>Preloader patcher file name (no directory); null for a GUID entry.</summary>
        public string Patcher { get; private set; }

        public string Name { get; private set; }

        public ArenaWhitelistTier Tier { get; private set; }

        /// <summary>Reviewed client version; null means any version without a warning.</summary>
        public string ClientVersion { get; private set; }

        /// <summary>Reviewed server version; null means any version without a warning.</summary>
        public string ServerVersion { get; private set; }

        public bool AppliesToClient { get; private set; }

        public bool AppliesToServer { get; private set; }

        public bool IsPatcher
        {
            get { return Patcher != null; }
        }

        /// <summary>The GUID or the patcher file name.</summary>
        public string Id
        {
            get { return Guid ?? Patcher; }
        }

        public bool AppliesTo(ArenaWhitelistSide side)
        {
            return side == ArenaWhitelistSide.Client ? AppliesToClient : AppliesToServer;
        }

        /// <summary>The reviewed version for one side, or null when any version is fine.</summary>
        public string ReviewedVersion(ArenaWhitelistSide side)
        {
            return side == ArenaWhitelistSide.Client ? ClientVersion : ServerVersion;
        }

        /// <summary>
        /// The "reason" string a decision reports for an allowed unit of this entry:
        /// <c>platform</c>, <c>arena</c>, <c>whitelist</c> or <c>tool</c>.
        /// </summary>
        public string Reason
        {
            get
            {
                switch (Tier)
                {
                    case ArenaWhitelistTier.Platform: return ArenaWhitelistDecision.ReasonPlatform;
                    case ArenaWhitelistTier.Arena: return ArenaWhitelistDecision.ReasonArena;
                    case ArenaWhitelistTier.Tool: return ArenaWhitelistDecision.ReasonTool;
                    default: return ArenaWhitelistDecision.ReasonWhitelist;
                }
            }
        }
    }

    /// <summary>
    /// The effective Arena session policy, a combined allow/deny list: platform + Arena (code) +
    /// the shipped default file (its <c>mods</c> allow, its optional <c>deny</c> blocks) + the
    /// optional user file's <c>allow</c> - its <c>deny</c>, and the user file's <c>unlisted</c> mode
    /// for every unit that none of those names. See docs/ARENA-SESSION-20261021.md, sections 2 and 3.
    /// Immutable after <see cref="TryLoad"/>.
    ///
    /// Per unit, in this order: a platform or Arena unit is always allowed (no file can remove it
    /// and no entry of either file can replace it); a unit the user denied is excluded (and not
    /// pulled in as a hard dependency of an allowed unit either: <see cref="ArenaWhitelistDecision"/>
    /// excludes the units that need it); a unit the user's <c>allow</c> names is allowed; a unit the
    /// default file denies is excluded (also in blacklist mode, and also when the default file
    /// allows it too); a unit that a default entry names is allowed; everything else follows
    /// <see cref="UnlistedMode"/>. So a user <c>allow</c> re-enables a default-denied unit, and a
    /// user <c>deny</c> always has the last word.
    ///
    /// GUID and file name comparison is ordinal and case-insensitive. The shipped default file is
    /// trusted code-adjacent data and is parsed strictly (any defect fails the load, except three
    /// that only produce a diagnostic: a <c>mods</c> entry or a <c>deny</c> entry that names a
    /// platform or Arena unit is skipped, so that the file cannot replace or remove a unit that
    /// code allows, and a unit that <c>mods</c> allows and <c>deny</c> names stays denied); the user
    /// file is hand-edited and is parsed leniently (a defect costs the file or the entry, with a
    /// diagnostic, never the session). Two defects cost the whole file instead: a value of
    /// <c>unlisted</c> that this build does not know, because guessing "deny" or "allow" for it
    /// would silently turn the policy into the opposite of what the user wrote, and, in blacklist
    /// mode only, a <c>deny</c> list that cannot be read in full (not an array, or an entry that is
    /// not a non-blank string). There the deny list is the whole filter, and a lost entry would be
    /// a mod the user wanted out that loads; a lost <c>allow</c> entry, or a lost deny entry under
    /// a whitelist, only leaves out or keeps a mod the list already treats as unlisted. A JSON null
    /// for the user file's <c>unlisted</c>, <c>allow</c> or <c>deny</c> is the same as a missing
    /// member, in both modes and without a diagnostic (in blacklist mode too: a null <c>deny</c> is
    /// an absent deny list, not an unreadable one); the default file's <c>deny</c> is stricter and a
    /// null there fails the load like any other value that is not an array.
    /// </summary>
    public sealed class ArenaWhitelistPolicy
    {
        /// <summary>The only policy file schema this build understands.</summary>
        public const int SupportedSchema = 1;

        /// <summary>Name of the optional top-level member of the user file that sets <see cref="UnlistedMode"/>.</summary>
        public const string UnlistedMemberName = "unlisted";

        /// <summary>Value of <c>unlisted</c> for the whitelist behaviour (the default).</summary>
        public const string UnlistedDenyValue = "deny";

        /// <summary>Value of <c>unlisted</c> for the blacklist behaviour.</summary>
        public const string UnlistedAllowValue = "allow";

        private static readonly char[] PathSeparators = { '/', '\\' };

        // key: lower-cased GUID / patcher file name
        private readonly Dictionary<string, ArenaWhitelistEntry> _plugins =
            new Dictionary<string, ArenaWhitelistEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ArenaWhitelistEntry> _patchers =
            new Dictionary<string, ArenaWhitelistEntry>(StringComparer.OrdinalIgnoreCase);

        // The user file's deny list: both the name as written and its file name (a patcher given as a path).
        private readonly HashSet<string> _denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The shipped default file's deny list, same shape. A default-denied unit is missing from the entry
        // dictionaries unless the user's allow list names it again.
        private readonly HashSet<string> _defaultDenied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<ArenaWhitelistEntry> _entries;

        private ArenaWhitelistPolicy()
        {
        }

        /// <summary>
        /// Stable hash of the effective policy (platform, Arena, default and user entries after
        /// deny; the default file's deny list when it has one; the user's denied names when there
        /// are any, and in blacklist mode the mode as well; display names do not count; order does
        /// not count). 16 lower-case hex digits. For logs, the session marker and the helper request.
        /// A whitelist policy without a default deny list and without a user deny hashes exactly as
        /// it did before the unlisted mode and the default deny list existed.
        /// </summary>
        public string Hash { get; private set; }

        /// <summary>
        /// What happens to a unit that no entry names and that neither the user file nor the default
        /// file denies. Whitelist (<see cref="ArenaUnlistedMode.Deny"/>) unless an accepted user file
        /// says <c>"unlisted": "allow"</c>.
        /// </summary>
        public ArenaUnlistedMode UnlistedMode { get; private set; }

        /// <summary>
        /// True in blacklist mode: everything that neither the user file nor the default file denies
        /// is allowed.
        /// </summary>
        public bool AllowsUnlisted
        {
            get { return UnlistedMode == ArenaUnlistedMode.Allow; }
        }

        /// <summary>Every effective entry, sorted by tier then id. Excludes the Fika prefix rule.</summary>
        public IReadOnlyList<ArenaWhitelistEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>True when a user file was present and accepted.</summary>
        public bool UserFileApplied { get; private set; }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Builds the effective policy. <paramref name="defaultJson"/> is the shipped
        /// <c>arena-whitelist.json</c>; <paramref name="userJsonOrNull"/> the content of the user
        /// file, or null when there is none.
        ///
        /// Returns false (and a null policy) when the default file is missing, empty, unparsable,
        /// has an unknown schema, any invalid entry or a <c>deny</c> member that is not an array of
        /// non-blank strings: the Arena session is then unavailable. An entry (or a deny name) of
        /// the default file that names a platform or Arena unit is not an invalid entry: it is
        /// skipped with a <c>default whitelist:</c> diagnostic and the file stays valid.
        /// A defective user file never makes this return false: an unknown schema, an unknown
        /// <c>unlisted</c> value, a parse error or (blacklist mode only) a <c>deny</c> list that
        /// cannot be read in full ignores the whole user file (a JSON null <c>deny</c> is an absent
        /// one, not an unreadable one), an invalid entry elsewhere ignores
        /// that entry, each with one diagnostic. A <c>unlisted</c> member in the default
        /// file is not read (the mode is the user's decision; the shipped list stays a whitelist).
        /// <paramref name="diagnostics"/> is never null; on a false return it holds at least one
        /// message describing why.
        /// </summary>
        public static bool TryLoad(string defaultJson, string userJsonOrNull,
            out ArenaWhitelistPolicy policy, out List<string> diagnostics)
        {
            policy = null;
            diagnostics = new List<string>();
            try
            {
                var result = new ArenaWhitelistPolicy();
                result.AddPlatformAndArenaUnits();

                if (!result.AddDefaultFile(defaultJson, diagnostics)) return false;

                if (userJsonOrNull != null) result.AddUserFile(userJsonOrNull, diagnostics);

                result.Finish();
                policy = result;
                return true;
            }
            catch (Exception ex)
            {
                policy = null;
                diagnostics.Add("whitelist policy could not be built: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private void AddPlatformAndArenaUnits()
        {
            foreach (string guid in ArenaSessionContract.SptPlatformGuids)
            {
                AddEntry(new ArenaWhitelistEntry(guid, null, "SPT " + guid.Substring(guid.LastIndexOf('.') + 1),
                    ArenaWhitelistTier.Platform, ArenaSessionContract.SptReviewedVersion, null, true, false));
            }

            AddEntry(new ArenaWhitelistEntry(ArenaSessionContract.ConfigurationManagerGuid, null,
                "Configuration Manager", ArenaWhitelistTier.Platform, null, null, true, false));

            foreach (string file in ArenaSessionContract.PlatformPatcherFiles)
            {
                AddEntry(new ArenaWhitelistEntry(null, file, file, ArenaWhitelistTier.Platform,
                    null, null, true, false));
            }

            AddEntry(new ArenaWhitelistEntry(ArenaSessionContract.ArenaPluginGuid, null, "ArenaMode",
                ArenaWhitelistTier.Arena, null, null, true, true));
        }

        private bool AddDefaultFile(string defaultJson, List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(defaultJson))
            {
                diagnostics.Add("default whitelist: the file is missing or empty");
                return false;
            }

            object root;
            string error;
            if (!ArenaMiniJson.TryParse(defaultJson, out root, out error))
            {
                diagnostics.Add("default whitelist: cannot parse: " + error);
                return false;
            }

            var document = ArenaMiniJson.AsObject(root);
            if (document == null)
            {
                diagnostics.Add("default whitelist: the top level must be a JSON object");
                return false;
            }

            if (!SchemaIsSupported(document))
            {
                diagnostics.Add("default whitelist: unsupported schema " + DescribeSchema(document)
                    + " (this build understands " + SupportedSchema.ToString(CultureInfo.InvariantCulture) + ")");
                return false;
            }

            var mods = ArenaMiniJson.GetArray(document, "mods");
            if (mods == null)
            {
                diagnostics.Add("default whitelist: the \"mods\" array is missing");
                return false;
            }

            for (int i = 0; i < mods.Count; i++)
            {
                ArenaWhitelistEntry entry;
                if (!TryParseEntry(mods[i], out entry, out error))
                {
                    diagnostics.Add("default whitelist: mods[" + i.ToString(CultureInfo.InvariantCulture) + "]: " + error);
                    return false;
                }

                if (IsProtected(entry))
                {
                    // Platform and Arena units are allowed by code (AddPlatformAndArenaUnits ran first). An entry
                    // for one must not replace it: AddEntry would swap in a Mod/Tool entry with the file's sides and
                    // versions, and a deny of the same name would then remove the unit, because
                    // AddDefaultDenyList protects code-tier entries only. The shipped file is trusted, so this is
                    // a mistake to report (the build script refuses to ship such a file), not a reason to fail
                    // the load; the user file's allow list skips the same entries.
                    diagnostics.Add("default whitelist: mods[" + i.ToString(CultureInfo.InvariantCulture) + "]: '" + entry.Id
                        + "' is ignored, platform and Arena units are fixed in code and cannot be listed");
                    continue;
                }

                AddEntry(entry);
            }

            return AddDefaultDenyList(document, diagnostics);
        }

        /// <summary>
        /// Reads the optional <c>deny</c> member of the default file: an array of non-blank strings,
        /// each a GUID or a patcher file name (the user file's syntax). Unlike the user file this is
        /// strict: any other shape fails the load, because the list is shipped with the build and a
        /// deny that is silently lost would let a mod through that the build refuses. A unit that
        /// <c>mods</c> allows and <c>deny</c> names is denied (deny wins) and is reported; a
        /// platform or Arena unit cannot be denied and the entry is skipped with a diagnostic.
        /// </summary>
        private bool AddDefaultDenyList(Dictionary<string, object> document, List<string> diagnostics)
        {
            object denyValue;
            if (!document.TryGetValue("deny", out denyValue)) return true;

            var deny = denyValue as List<object>;
            if (deny == null)
            {
                diagnostics.Add("default whitelist: \"deny\" must be an array of GUIDs or patcher file names");
                return false;
            }

            for (int i = 0; i < deny.Count; i++)
            {
                string id = deny[i] as string;
                if (id == null || id.Trim().Length == 0)
                {
                    diagnostics.Add("default whitelist: deny[" + i.ToString(CultureInfo.InvariantCulture)
                        + "]: expected a GUID or a patcher file name");
                    return false;
                }

                id = id.Trim();
                if (IsProtectedId(id))
                {
                    diagnostics.Add("default whitelist: deny of '" + id
                        + "' is ignored, platform and Arena units cannot be denied");
                    continue;
                }

                _defaultDenied.Add(id);
                _defaultDenied.Add(FileNameOf(id));
                bool wasAllowed = _plugins.Remove(id);
                wasAllowed |= _patchers.Remove(FileNameOf(id));
                if (wasAllowed)
                {
                    diagnostics.Add("default whitelist: '" + id
                        + "' is both allowed and denied, the deny wins and the unit stays out");
                }
            }

            return true;
        }

        private void AddUserFile(string userJson, List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(userJson))
            {
                diagnostics.Add("user whitelist: the file is empty and is ignored");
                return;
            }

            object root;
            string error;
            if (!ArenaMiniJson.TryParse(userJson, out root, out error))
            {
                diagnostics.Add("user whitelist: cannot parse, the file is ignored: " + error);
                return;
            }

            var document = ArenaMiniJson.AsObject(root);
            if (document == null)
            {
                diagnostics.Add("user whitelist: the top level must be a JSON object, the file is ignored");
                return;
            }

            if (!SchemaIsSupported(document))
            {
                diagnostics.Add("user whitelist: unknown schema " + DescribeSchema(document)
                    + " (this build understands " + SupportedSchema.ToString(CultureInfo.InvariantCulture)
                    + "), the file is ignored");
                return;
            }

            // Before anything of the file is applied: a mode that cannot be read invalidates the
            // file, so that its allow and deny lists never run under the wrong mode.
            ArenaUnlistedMode unlisted;
            if (!TryReadUnlisted(document, out unlisted, out error))
            {
                diagnostics.Add("user whitelist: " + error + ", the file is ignored");
                return;
            }

            // Blacklist mode only: the deny list is the whole filter there, so one that cannot be read in
            // full must not be applied in part. Checked before anything of the file takes effect.
            if (unlisted == ArenaUnlistedMode.Allow && !TryCheckDenyListForBlacklist(document, out error))
            {
                diagnostics.Add("user whitelist: " + error + ", the file is ignored");
                return;
            }

            UserFileApplied = true;
            UnlistedMode = unlisted;

            // A JSON null member counts as a missing one (allow and deny here, unlisted in TryReadUnlisted),
            // in both modes and without a diagnostic.
            object allowValue;
            if (document.TryGetValue("allow", out allowValue) && allowValue != null)
            {
                var allow = allowValue as List<object>;
                if (allow == null)
                {
                    diagnostics.Add("user whitelist: \"allow\" must be an array, it is ignored");
                }
                else
                {
                    for (int i = 0; i < allow.Count; i++)
                    {
                        ArenaWhitelistEntry entry;
                        if (!TryParseEntry(allow[i], out entry, out error))
                        {
                            diagnostics.Add("user whitelist: allow[" + i.ToString(CultureInfo.InvariantCulture)
                                + "] is ignored: " + error);
                            continue;
                        }

                        if (IsProtected(entry))
                        {
                            // Already allowed by code; the user entry must not change its metadata.
                            continue;
                        }

                        AddEntry(entry);
                    }
                }
            }

            object denyValue;
            if (document.TryGetValue("deny", out denyValue) && denyValue != null)
            {
                var deny = denyValue as List<object>;
                if (deny == null)
                {
                    diagnostics.Add("user whitelist: \"deny\" must be an array, it is ignored");
                }
                else
                {
                    for (int i = 0; i < deny.Count; i++)
                    {
                        string id = (deny[i] as string ?? string.Empty).Trim();
                        if (id.Length == 0)
                        {
                            diagnostics.Add("user whitelist: deny[" + i.ToString(CultureInfo.InvariantCulture)
                                + "] is ignored: expected a GUID or a patcher file name");
                            continue;
                        }

                        if (IsProtectedId(id))
                        {
                            diagnostics.Add("user whitelist: deny of '" + id
                                + "' is ignored, platform and Arena units cannot be denied");
                            continue;
                        }

                        _denied.Add(id);
                        _denied.Add(FileNameOf(id));
                        _plugins.Remove(id);
                        _patchers.Remove(FileNameOf(id));
                    }
                }
            }
        }

        /// <summary>
        /// True when the optional <c>deny</c> member is absent, JSON null or an array of non-blank
        /// strings. Used in blacklist mode, where a deny entry that is silently lost lets a mod load
        /// that the user excluded; otherwise <c>error</c> says what is wrong. JSON null is the same as
        /// absent (<see cref="AddUserFile"/> skips a null member as well), so no entry is lost by it.
        /// </summary>
        private static bool TryCheckDenyListForBlacklist(Dictionary<string, object> document, out string error)
        {
            error = null;
            object value;
            if (!document.TryGetValue("deny", out value) || value == null) return true;

            var deny = value as List<object>;
            if (deny == null)
            {
                error = "\"deny\" must be an array when \"" + UnlistedMemberName + "\" is \"" + UnlistedAllowValue + "\"";
                return false;
            }

            for (int i = 0; i < deny.Count; i++)
            {
                string id = deny[i] as string;
                if (id == null || id.Trim().Length == 0)
                {
                    error = "deny[" + i.ToString(CultureInfo.InvariantCulture) + "] must be a GUID or a patcher file name when \""
                        + UnlistedMemberName + "\" is \"" + UnlistedAllowValue + "\"";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reads the optional <c>unlisted</c> member. Missing and JSON null give the default
        /// (<see cref="ArenaUnlistedMode.Deny"/>); a string is compared ignoring case and
        /// surrounding white space; anything else, the empty string included, is an error.
        /// </summary>
        private static bool TryReadUnlisted(Dictionary<string, object> document, out ArenaUnlistedMode mode, out string error)
        {
            mode = ArenaUnlistedMode.Deny;
            error = null;

            object raw;
            if (!document.TryGetValue(UnlistedMemberName, out raw) || raw == null) return true;

            string text = raw as string;
            if (text != null)
            {
                string value = text.Trim();
                if (string.Equals(value, UnlistedDenyValue, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(value, UnlistedAllowValue, StringComparison.OrdinalIgnoreCase))
                {
                    mode = ArenaUnlistedMode.Allow;
                    return true;
                }
            }

            string shown = ArenaMiniJson.Write(raw);
            if (shown.Length > 40) shown = shown.Substring(0, 40) + "...";
            error = "unknown \"" + UnlistedMemberName + "\" value " + shown
                + " (expected \"" + UnlistedDenyValue + "\" or \"" + UnlistedAllowValue + "\")";
            return false;
        }

        private static bool SchemaIsSupported(Dictionary<string, object> document)
        {
            object schema;
            if (!document.TryGetValue("schema", out schema) || !(schema is double)) return false;
            return (double)schema == SupportedSchema;
        }

        private static string DescribeSchema(Dictionary<string, object> document)
        {
            object schema;
            if (!document.TryGetValue("schema", out schema) || schema == null) return "(missing)";
            return ArenaMiniJson.Write(schema);
        }

        private void Finish()
        {
            var list = new List<ArenaWhitelistEntry>(_plugins.Count + _patchers.Count);
            list.AddRange(_plugins.Values);
            list.AddRange(_patchers.Values);
            list.Sort(CompareEntries);
            _entries = list.AsReadOnly();
            Hash = ComputeHash(list, UnlistedMode, _denied, _defaultDenied);
        }

        private static int CompareEntries(ArenaWhitelistEntry a, ArenaWhitelistEntry b)
        {
            int byTier = ((int)a.Tier).CompareTo((int)b.Tier);
            if (byTier != 0) return byTier;
            int byKind = a.IsPatcher.CompareTo(b.IsPatcher);
            if (byKind != 0) return byKind;
            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        private void AddEntry(ArenaWhitelistEntry entry)
        {
            if (entry.IsPatcher) _patchers[entry.Patcher] = entry;
            else _plugins[entry.Guid] = entry;
        }

        /// <summary>
        /// True when this entry names a platform or Arena unit (a code-tier entry exists for it) or a
        /// Fika GUID. No file may add an entry for such a unit: it would replace the code entry.
        /// </summary>
        private bool IsProtected(ArenaWhitelistEntry entry)
        {
            ArenaWhitelistEntry existing;
            if (entry.IsPatcher) return _patchers.TryGetValue(entry.Patcher, out existing) && IsCodeTier(existing.Tier);
            return IsFikaGuid(entry.Guid) || (_plugins.TryGetValue(entry.Guid, out existing) && IsCodeTier(existing.Tier));
        }

        private bool IsProtectedId(string id)
        {
            ArenaWhitelistEntry existing;
            if (IsFikaGuid(id)) return true;
            if (_plugins.TryGetValue(id, out existing) && IsCodeTier(existing.Tier)) return true;
            return _patchers.TryGetValue(FileNameOf(id), out existing) && IsCodeTier(existing.Tier);
        }

        private static bool IsCodeTier(ArenaWhitelistTier tier)
        {
            return tier == ArenaWhitelistTier.Platform || tier == ArenaWhitelistTier.Arena;
        }

        // ------------------------------------------------------------------ entry parsing

        private static bool TryParseEntry(object item, out ArenaWhitelistEntry entry, out string error)
        {
            entry = null;
            error = null;
            var obj = ArenaMiniJson.AsObject(item);
            if (obj == null)
            {
                error = "an entry must be a JSON object";
                return false;
            }

            string guid, patcher, name, kind, clientVersion, serverVersion;
            if (!TryReadString(obj, "guid", out guid, out error)) return false;
            if (!TryReadString(obj, "patcher", out patcher, out error)) return false;
            if (!TryReadString(obj, "name", out name, out error)) return false;
            if (!TryReadString(obj, "kind", out kind, out error)) return false;
            if (!TryReadString(obj, "clientVersion", out clientVersion, out error)) return false;
            if (!TryReadString(obj, "serverVersion", out serverVersion, out error)) return false;

            if ((guid == null) == (patcher == null))
            {
                error = "exactly one of \"guid\" or \"patcher\" is required";
                return false;
            }

            ArenaWhitelistTier tier;
            if (kind == null || string.Equals(kind, "mod", StringComparison.OrdinalIgnoreCase)) tier = ArenaWhitelistTier.Mod;
            else if (string.Equals(kind, "tool", StringComparison.OrdinalIgnoreCase)) tier = ArenaWhitelistTier.Tool;
            else
            {
                error = "\"kind\" must be \"mod\" or \"tool\", not \"" + kind + "\"";
                return false;
            }

            Version parsed;
            if (clientVersion != null && !TryParseVersion(clientVersion, out parsed))
            {
                error = "\"clientVersion\" '" + clientVersion + "' is not a valid version";
                return false;
            }

            if (serverVersion != null && !TryParseVersion(serverVersion, out parsed))
            {
                error = "\"serverVersion\" '" + serverVersion + "' is not a valid version";
                return false;
            }

            if (patcher != null)
            {
                patcher = FileNameOf(patcher);
                if (patcher.Length == 0)
                {
                    error = "\"patcher\" must be a file name";
                    return false;
                }

                // Patchers carry no checkable version.
                entry = new ArenaWhitelistEntry(null, patcher, name, tier, null, null, true, false);
                return true;
            }

            entry = new ArenaWhitelistEntry(guid, null, name, tier, clientVersion, serverVersion, true, true);
            return true;
        }

        /// <summary>
        /// Reads an optional string member. Missing, JSON null and the empty string all give a null
        /// value; any other JSON type is an error. The value is trimmed.
        /// </summary>
        private static bool TryReadString(Dictionary<string, object> obj, string key, out string value, out string error)
        {
            value = null;
            error = null;
            object raw;
            if (!obj.TryGetValue(key, out raw) || raw == null) return true;
            string text = raw as string;
            if (text == null)
            {
                error = "\"" + key + "\" must be a string";
                return false;
            }

            text = text.Trim();
            value = text.Length == 0 ? null : text;
            return true;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>
        /// True when a client plugin with this GUID may load in an Arena session: an entry names
        /// it, or the policy is in blacklist mode and neither the user file nor the default file
        /// denies it. A blank GUID names no unit and is never allowed here.
        /// </summary>
        public bool IsAllowedPlugin(string guid)
        {
            return FindPlugin(guid, ArenaWhitelistSide.Client) != null
                || (!string.IsNullOrWhiteSpace(guid) && IsUnlistedAllowed(guid));
        }

        /// <summary>
        /// True when a server mod with this GUID may load in the Arena session server (see
        /// <see cref="IsAllowedPlugin"/>).
        /// </summary>
        public bool IsAllowedServerMod(string guid)
        {
            return FindPlugin(guid, ArenaWhitelistSide.Server) != null
                || (!string.IsNullOrWhiteSpace(guid) && IsUnlistedAllowed(guid));
        }

        /// <summary>
        /// True when a preloader patcher may run in an Arena session: an entry names it, or the
        /// policy is in blacklist mode and neither the user file nor the default file denies it.
        /// Matched by file name only (ordinal, case-insensitive): a full path is reduced to its last
        /// segment.
        /// </summary>
        public bool IsAllowedPatcher(string fileName)
        {
            return FindPatcher(fileName) != null
                || (FileNameOf(fileName).Length > 0 && IsUnlistedAllowed(fileName));
        }

        /// <summary>
        /// True when the policy lets a unit through that no entry names: it is in blacklist mode
        /// (<see cref="AllowsUnlisted"/>) and neither the user file nor the default file denies
        /// the unit (a default deny holds in blacklist mode too; only the user's <c>allow</c> lifts
        /// it, and that is an entry, found before this is asked). Platform and Arena units cannot
        /// be denied, so they are never held back by this. The caller decides first whether an
        /// entry names the unit (<see cref="FindPlugin"/>, <see cref="FindPatcher"/>); this answers
        /// only for the rest. A unit without a name cannot be denied and is let through as well.
        /// </summary>
        public bool IsUnlistedAllowed(string guidOrPatcherFile)
        {
            return AllowsUnlisted && !IsDeniedByUser(guidOrPatcherFile) && !IsDeniedByDefault(guidOrPatcherFile);
        }

        /// <summary>
        /// The effective entry that allows <paramref name="guid"/> on <paramref name="side"/>, or
        /// null when no entry names the unit (it is then excluded in whitelist mode and decided by
        /// <see cref="IsUnlistedAllowed"/> in blacklist mode). Fika GUIDs get a synthesized
        /// platform entry.
        /// </summary>
        public ArenaWhitelistEntry FindPlugin(string guid, ArenaWhitelistSide side)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;
            guid = guid.Trim();

            ArenaWhitelistEntry entry;
            if (_plugins.TryGetValue(guid, out entry)) return entry.AppliesTo(side) ? entry : null;

            if (side == ArenaWhitelistSide.Client && IsFikaGuid(guid))
            {
                return new ArenaWhitelistEntry(guid, null, guid, ArenaWhitelistTier.Platform, null, null, true, false);
            }

            return null;
        }

        /// <summary>The effective entry that allows a patcher file, or null.</summary>
        public ArenaWhitelistEntry FindPatcher(string fileNameOrPath)
        {
            string file = FileNameOf(fileNameOrPath);
            if (file.Length == 0) return null;
            ArenaWhitelistEntry entry;
            return _patchers.TryGetValue(file, out entry) ? entry : null;
        }

        /// <summary>
        /// The version this unit was reviewed with on one side, or null when any version is fine
        /// (absent in the file) or the unit is not on the whitelist.
        /// </summary>
        public string ReviewedVersion(string guid, ArenaWhitelistSide side)
        {
            ArenaWhitelistEntry entry = FindPlugin(guid, side);
            return entry == null ? null : entry.ReviewedVersion(side);
        }

        /// <summary>
        /// True when the user file's <c>deny</c> list names this GUID or patcher file and the unit
        /// is not a platform or Arena unit (those cannot be denied). It chooses the exclusion
        /// reason <c>denied-by-user</c> over <c>denied</c> and <c>not-on-whitelist</c> and, in
        /// blacklist mode, is what keeps an unlisted unit out (<see cref="IsUnlistedAllowed"/>).
        /// </summary>
        public bool IsDeniedByUser(string guidOrPatcherFile)
        {
            if (string.IsNullOrWhiteSpace(guidOrPatcherFile)) return false;
            string id = guidOrPatcherFile.Trim();
            if (IsProtectedId(id)) return false;
            return _denied.Contains(id) || _denied.Contains(FileNameOf(id));
        }

        /// <summary>
        /// True when the shipped default file's <c>deny</c> list names this GUID or patcher file and
        /// the unit is not a platform or Arena unit. The list is a statement about the build, not
        /// about the unit being unlisted, so it holds in blacklist mode as well; a user <c>allow</c>
        /// entry for the unit lifts it (the unit then has an entry and is allowed), a user
        /// <c>deny</c> does not need it. True does not mean the unit is excluded, only that the
        /// default file names it as denied.
        /// </summary>
        public bool IsDeniedByDefault(string guidOrPatcherFile)
        {
            if (string.IsNullOrWhiteSpace(guidOrPatcherFile)) return false;
            string id = guidOrPatcherFile.Trim();
            if (IsProtectedId(id)) return false;
            return _defaultDenied.Contains(id) || _defaultDenied.Contains(FileNameOf(id));
        }

        /// <summary>
        /// The reason to report for a unit that is not allowed: <c>denied-by-user</c> when the user
        /// file's deny list names it, <c>denied</c> when the default file's deny list does, otherwise
        /// <c>not-on-whitelist</c>. The user's deny comes first because it is the higher
        /// precedence of the two.
        /// </summary>
        public string ExclusionReason(string guidOrPatcherFile)
        {
            if (IsDeniedByUser(guidOrPatcherFile)) return ArenaWhitelistDecision.ReasonDeniedByUser;
            if (IsDeniedByDefault(guidOrPatcherFile)) return ArenaWhitelistDecision.ReasonDenied;
            return ArenaWhitelistDecision.ReasonNotOnWhitelist;
        }

        /// <summary>True for every GUID that starts with <c>com.fika.</c> (never filtered).</summary>
        public static bool IsFikaGuid(string guid)
        {
            return guid != null
                && guid.Trim().StartsWith(ArenaSessionContract.FikaGuidPrefix, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ versions

        /// <summary>
        /// Parses a version the way policy comparison needs it: surrounding white space and one
        /// leading <c>v</c>/<c>V</c> are dropped, everything from the first <c>-</c> or <c>+</c> on
        /// (pre-release and build metadata) is cut off, a single number counts as <c>N.0</c>, and
        /// the result always has four components (missing ones become 0, so 4.5.1 equals 4.5.1.0).
        /// Returns false for anything <see cref="System.Version"/> cannot read.
        /// </summary>
        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (text == null) return false;

            string trimmed = text.Trim();
            if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V')) trimmed = trimmed.Substring(1);
            int cut = trimmed.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0) trimmed = trimmed.Substring(0, cut);
            trimmed = trimmed.Trim();
            if (trimmed.Length == 0) return false;
            if (trimmed.IndexOf('.') < 0) trimmed += ".0";

            Version parsed;
            if (!Version.TryParse(trimmed, out parsed)) return false;
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
            return true;
        }

        /// <summary>
        /// True when the installed version differs from the reviewed one. An empty
        /// <paramref name="reviewed"/> means "any version": never different. An installed version
        /// that is empty or cannot be parsed counts as different, unless it equals the reviewed
        /// text character for character.
        /// </summary>
        public static bool VersionDiffers(string installed, string reviewed)
        {
            if (string.IsNullOrWhiteSpace(reviewed)) return false;
            if (string.Equals(installed == null ? null : installed.Trim(), reviewed.Trim(), StringComparison.Ordinal)) return false;

            Version installedVersion, reviewedVersion;
            if (!TryParseVersion(installed, out installedVersion) || !TryParseVersion(reviewed, out reviewedVersion)) return true;
            return !installedVersion.Equals(reviewedVersion);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The last path segment of a file name or path; empty for null.</summary>
        internal static string FileNameOf(string fileNameOrPath)
        {
            if (fileNameOrPath == null) return string.Empty;
            string trimmed = fileNameOrPath.Trim();
            int cut = trimmed.LastIndexOfAny(PathSeparators);
            return cut >= 0 ? trimmed.Substring(cut + 1) : trimmed;
        }

        private static string ComputeHash(List<ArenaWhitelistEntry> entries, ArenaUnlistedMode unlisted, HashSet<string> denied,
            HashSet<string> defaultDenied)
        {
            var lines = new List<string>(entries.Count + 3);
            lines.Add("fika-prefix|" + ArenaSessionContract.FikaGuidPrefix.ToLowerInvariant());

            // The default file's deny list is part of the policy only when there is one: a default
            // file without it hashes exactly as it did before the list existed (pinned by a test).
            // Unlike a user deny, it matters in whitelist mode too: it picks the exclusion reason and
            // it is what a user allow overrides. Reduced to the lower-case file name like the user's.
            if (defaultDenied != null && defaultDenied.Count > 0)
            {
                var defaultNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in defaultDenied) defaultNames.Add(FileNameOf(name).ToLowerInvariant());
                foreach (string name in defaultNames) lines.Add("default-denied|" + name);
            }

            // The user's denied names are part of the policy in both modes. In blacklist mode they are the
            // whole filter, so the mode line comes with them. In whitelist mode a deny changes decisions
            // too, even when nothing lists the unit: a denied library is never pulled in as a hard
            // dependency, so every allowed unit that needs it is excluded (deny wins over hard
            // dependencies, ArenaWhitelistDecision). A whitelist policy without a user deny adds nothing
            // here, so its hash is the one earlier builds computed (pinned by a test). The names are
            // reduced to the lower-case file name (the form IsDeniedByUser also matches), so that
            // "x.dll" and "patchers/X.DLL" agree.
            if (unlisted == ArenaUnlistedMode.Allow) lines.Add("unlisted|" + UnlistedAllowValue);
            if (denied != null && denied.Count > 0)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in denied) names.Add(FileNameOf(name).ToLowerInvariant());
                foreach (string name in names) lines.Add("denied|" + name);
            }

            foreach (ArenaWhitelistEntry entry in entries)
            {
                lines.Add((entry.IsPatcher ? "patcher" : "plugin")
                    + "|" + ((int)entry.Tier).ToString(CultureInfo.InvariantCulture)
                    + "|" + entry.Id.ToLowerInvariant()
                    + "|" + (entry.ClientVersion ?? string.Empty)
                    + "|" + (entry.ServerVersion ?? string.Empty)
                    + "|" + (entry.AppliesToClient ? "c" : "-") + (entry.AppliesToServer ? "s" : "-"));
            }

            // Sorting here, not relying on the entry order, keeps the hash order-independent.
            lines.Sort(StringComparer.Ordinal);

            var text = new StringBuilder("arena-whitelist/");
            text.Append(SupportedSchema.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (string line in lines) text.Append(line).Append('\n');

            // FNV-1a, 64 bit, over the UTF-16 code units (low byte first). Same result on every
            // runtime and no dependency on an encoding or a crypto provider.
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    hash ^= (ulong)(c & 0xFF);
                    hash *= prime;
                    hash ^= (ulong)(c >> 8);
                    hash *= prime;
                }
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
