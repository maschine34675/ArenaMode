// Linked source, see ArenaMiniJson.cs for the constraints (netstandard2.1, net472, net10.0; C# 7.3
// syntax; no nullable annotations; BCL only).
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaMode.Shared.Session
{
    /// <summary>
    /// One installed unit as the decision sees it: a BepInEx plugin on the client or an SPT server
    /// mod on the server. It is also the element type of the gate report's allowed and excluded
    /// lists, where <see cref="Reason"/> is filled in.
    ///
    /// A plain mutable data class (no behaviour) so that the gate can fill it from reflected
    /// <c>PluginInfo</c> objects, the client from the server ping and the tests from literals.
    /// </summary>
    public sealed class ArenaSessionUnit
    {
        public ArenaSessionUnit()
        {
            HardDependencies = new List<string>();
        }

        public ArenaSessionUnit(string guid, string name, string version, string file, IEnumerable<string> hardDependencies)
        {
            Guid = guid;
            Name = name;
            Version = version;
            File = file;
            HardDependencies = hardDependencies == null ? new List<string>() : new List<string>(hardDependencies);
        }

        /// <summary>BepInEx plugin GUID / SPT server mod GUID.</summary>
        public string Guid { get; set; }

        public string Name { get; set; }

        /// <summary>Installed version text, e.g. <c>4.5.1</c>.</summary>
        public string Version { get; set; }

        /// <summary>Source DLL (client) or mod folder (server); informational only.</summary>
        public string File { get; set; }

        /// <summary>
        /// GUIDs of this unit's hard dependencies: <c>BepInDependency</c> without the soft flag on the
        /// client, <c>ModDependencies</c> on the server (the server's mod validator refuses to load any
        /// mod when one of them is missing). Never null; empty when the side reports none.
        /// </summary>
        public List<string> HardDependencies { get; set; }

        /// <summary>
        /// Why the unit is allowed or excluded; set on the units a decision returns, empty on input.
        /// See the <c>Reason*</c> constants of <see cref="ArenaWhitelistDecision"/>.
        /// </summary>
        public string Reason { get; set; }

        /// <summary>The name, or the GUID when the name is empty.</summary>
        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Name) ? Guid : Name; }
        }

        internal ArenaSessionUnit CopyWithReason(string reason)
        {
            return new ArenaSessionUnit(Guid, Name, Version, File, HardDependencies) { Reason = reason };
        }
    }

    /// <summary>
    /// The outcome of applying an <see cref="ArenaWhitelistPolicy"/> to the units installed on one
    /// side. Pure function: the same code decides in the gate (client plugins), in the client (the
    /// normal-session check, with the server's mods from the ping) and in tests.
    ///
    /// Rules (docs/ARENA-SESSION-20261021.md, sections 3 and 4):
    /// a unit the policy names is allowed (reason <c>platform</c>, <c>arena</c>, <c>whitelist</c> or
    /// <c>tool</c>); in blacklist mode (<see cref="ArenaWhitelistPolicy.AllowsUnlisted"/>) a unit that
    /// nothing names and neither the user file nor the default file denies is allowed too, with
    /// reason <c>unlisted-allowed</c> and no warning of its own, but the units of that kind are
    /// announced together by ONE policy-override warning per side (see <see cref="PolicyOverridePrefix"/>),
    /// so that a user file that turned the whitelist into a blacklist cannot go unnoticed; a hard
    /// dependency of an allowed unit that is installed but unlisted (whitelist mode only: in blacklist
    /// mode an unlisted unit is allowed anyway) is allowed as well, transitively, with reason
    /// <c>dependency-of:&lt;guid&gt;</c> and a warning; everything else is excluded
    /// (<c>denied-by-user</c> when the user file's deny list names it, <c>denied</c> when the
    /// shipped default file's deny list does, otherwise <c>not-on-whitelist</c>). A reviewed version
    /// that differs from the installed one is a warning, never an exclusion.
    ///
    /// A deny wins over hard dependencies: a unit that either deny list names (and no <c>allow</c>
    /// entry lifts) is never pulled in by the closure. A unit that would be allowed but has such a
    /// unit as an installed hard dependency, directly or through other units, cannot start without
    /// it, so it is excluded itself with reason <c>requires-denied:&lt;guid&gt;</c> (the GUID of the
    /// denied unit it needs) and one warning; so is every unit between it and the denied one. Platform
    /// and Arena units are the exception: no list can remove them (spec section 3, step 1), so they
    /// stay allowed even then (they depend on nothing deniable in practice).
    /// </summary>
    public sealed class ArenaWhitelistDecision
    {
        public const string ReasonPlatform = "platform";
        public const string ReasonArena = "arena";
        public const string ReasonWhitelist = "whitelist";
        public const string ReasonTool = "tool";

        /// <summary>
        /// Blacklist mode: no entry names the unit and neither the user file nor the default file
        /// denies it. The unit has no reviewed version, so it never produces a version warning.
        /// </summary>
        public const string ReasonUnlistedAllowed = "unlisted-allowed";

        /// <summary>Prefix of the dependency reason; the dependant's GUID follows.</summary>
        public const string ReasonDependencyPrefix = "dependency-of:";

        /// <summary>
        /// Prefix of the reason of a unit that is excluded because an installed hard dependency of it
        /// (directly or through other units) is denied by the user file or the default file; the GUID of
        /// that denied unit follows (<c>requires-denied:com.example.lib</c>). The unit would be allowed
        /// otherwise (a listed or unlisted-allowed unit, or an unlisted library an allowed unit needs).
        /// </summary>
        public const string ReasonRequiresDeniedPrefix = "requires-denied:";

        public const string ReasonNotOnWhitelist = "not-on-whitelist";

        /// <summary>The user file's deny list names the unit.</summary>
        public const string ReasonDeniedByUser = "denied-by-user";

        /// <summary>
        /// The shipped default file's deny list names the unit (a mod ArenaMode refuses by design) and
        /// the user's allow list does not bring it back. Holds in whitelist and blacklist mode alike.
        /// </summary>
        public const string ReasonDenied = "denied";

        /// <summary>
        /// Start of the one warning a decision adds when it allowed at least one unit with reason
        /// <see cref="ReasonUnlistedAllowed"/>: <c>policy override: 3 unlisted mod(s) allowed by your
        /// policy file: A, B, C</c>. The reason exists only in blacklist mode, which only the user file
        /// can switch on, hence "your policy file". The warning comes after the per-unit warnings.
        /// </summary>
        public const string PolicyOverridePrefix = "policy override: ";

        /// <summary>The policy-override warning names this many units; the rest is counted (<c>and K more</c>).</summary>
        public const int PolicyOverrideNamesListed = 6;

        private ArenaWhitelistDecision()
        {
            Allowed = new List<ArenaSessionUnit>();
            Excluded = new List<ArenaSessionUnit>();
            Warnings = new List<string>();
        }

        /// <summary>Allowed units in input order, copies with <see cref="ArenaSessionUnit.Reason"/> set.</summary>
        public List<ArenaSessionUnit> Allowed { get; private set; }

        /// <summary>Excluded units in input order, copies with <see cref="ArenaSessionUnit.Reason"/> set.</summary>
        public List<ArenaSessionUnit> Excluded { get; private set; }

        /// <summary>
        /// Human-readable warnings in input order, each starting with the unit name:
        /// <c>SAIN: version 4.5.2 differs from reviewed 4.5.1</c>,
        /// <c>BigBrain: pulled in as hard dependency of SAIN</c> (an unlisted library) and, for a unit
        /// excluded with a <see cref="ReasonRequiresDeniedPrefix"/> reason,
        /// <c>Foreign: excluded because it requires Lib (denied by the user's policy file)</c> or
        /// <c>(denied by the default list)</c>. The name in front of the parenthesis is the denied unit's,
        /// also for a unit that needs it only through other units; the user's list is named when both
        /// lists deny it.
        /// In blacklist mode, when at least one unit was allowed as unlisted, the last entry is the
        /// policy-override warning (<see cref="PolicyOverridePrefix"/>), which names no single unit.
        /// </summary>
        public List<string> Warnings { get; private set; }

        /// <summary>True for the policy-override warning of a decision (it starts with <see cref="PolicyOverridePrefix"/>).</summary>
        public static bool IsPolicyOverrideWarning(string warning)
        {
            return warning != null && warning.StartsWith(PolicyOverridePrefix, StringComparison.Ordinal);
        }

        /// <summary>True when <paramref name="reason"/> is a <see cref="ReasonRequiresDeniedPrefix"/> reason.</summary>
        public static bool IsRequiresDeniedReason(string reason)
        {
            return reason != null && reason.StartsWith(ReasonRequiresDeniedPrefix, StringComparison.Ordinal);
        }

        public bool HasExclusions
        {
            get { return Excluded.Count > 0; }
        }

        /// <summary>The display names of the excluded units, joined with ", ".</summary>
        public string DescribeExcluded()
        {
            var names = new List<string>(Excluded.Count);
            foreach (ArenaSessionUnit unit in Excluded) names.Add(unit.DisplayName);
            return string.Join(", ", names.ToArray());
        }

        /// <summary>
        /// Applies <paramref name="policy"/> to the installed units of one side. Null entries in
        /// <paramref name="installed"/> are skipped. Does not modify its input.
        /// </summary>
        public static ArenaWhitelistDecision Evaluate(ArenaWhitelistPolicy policy, ArenaWhitelistSide side,
            IEnumerable<ArenaSessionUnit> installed)
        {
            if (policy == null) throw new ArgumentNullException("policy");

            var units = new List<ArenaSessionUnit>();
            if (installed != null)
            {
                foreach (ArenaSessionUnit unit in installed)
                {
                    if (unit != null) units.Add(unit);
                }
            }

            var result = new ArenaWhitelistDecision();
            int count = units.Count;
            var initial = new string[count];          // the reason of a unit the policy allows by itself
            var reasons = new string[count];          // the reason of every unit that ends up allowed
            var direct = new ArenaWhitelistEntry[count];
            var pulledInBy = new ArenaSessionUnit[count];
            var denied = new bool[count];             // no entry, and a deny list names it
            var protectedUnit = new bool[count];      // a platform or Arena entry: no list can remove it
            var byGuid = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                string guid = Normalize(units[i].Guid);
                if (guid.Length > 0)
                {
                    List<int> sameGuid;
                    if (!byGuid.TryGetValue(guid, out sameGuid))
                    {
                        sameGuid = new List<int>();
                        byGuid[guid] = sameGuid;
                    }

                    sameGuid.Add(i);
                }

                ArenaWhitelistEntry entry = policy.FindPlugin(units[i].Guid, side);
                if (entry != null)
                {
                    direct[i] = entry;
                    initial[i] = entry.Reason;
                    protectedUnit[i] = entry.Tier == ArenaWhitelistTier.Platform || entry.Tier == ArenaWhitelistTier.Arena;
                }
                else if (policy.IsUnlistedAllowed(units[i].Guid))
                {
                    // Blacklist mode. No entry, so no reviewed version and no warning. It joins the
                    // closure like any allowed unit.
                    initial[i] = ReasonUnlistedAllowed;
                }
                else if (policy.IsDeniedByUser(units[i].Guid) || policy.IsDeniedByDefault(units[i].Guid))
                {
                    // Excluded, and (below) never pulled in as a dependency. A user allow entry would
                    // have been found above, so this is a unit nobody lifted the deny for.
                    denied[i] = true;
                }
            }

            // Dependants of every unit, in input order: dependants[d] lists the units that name d as a
            // hard dependency. A dependency GUID that is not installed (or blank) names nothing.
            var dependants = new List<int>[count];
            for (int user = 0; user < count; user++)
            {
                if (units[user].HardDependencies == null) continue;
                foreach (string dependencyGuid in units[user].HardDependencies)
                {
                    List<int> installedDependencies = InstalledUnits(byGuid, dependencyGuid);
                    if (installedDependencies == null) continue;
                    foreach (int dependencyIndex in installedDependencies)
                    {
                        if (dependants[dependencyIndex] == null) dependants[dependencyIndex] = new List<int>();
                        dependants[dependencyIndex].Add(user);
                    }
                }
            }

            // Deny wins over hard dependencies. A unit that has a denied unit among its installed hard
            // dependencies cannot be allowed (its loader would drop it, or the server's mod validator
            // would drop every mod), and neither can a unit that needs such a unit: requiresDenied[i] is
            // the index of the denied unit unit i is cut off by, or -1. First the units that name a denied
            // unit themselves (their first denied dependency in their own order), then, breadth first, the
            // units that need one of those. Platform and Arena units are never cut off, and a denied unit
            // keeps its own deny reason.
            var requiresDenied = new int[count];
            var cutOff = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                requiresDenied[i] = -1;
                if (denied[i] || protectedUnit[i] || units[i].HardDependencies == null) continue;

                foreach (string dependencyGuid in units[i].HardDependencies)
                {
                    List<int> installedDependencies = InstalledUnits(byGuid, dependencyGuid);
                    if (installedDependencies == null) continue;
                    foreach (int dependencyIndex in installedDependencies)
                    {
                        if (denied[dependencyIndex] && requiresDenied[i] < 0) requiresDenied[i] = dependencyIndex;
                    }

                    if (requiresDenied[i] >= 0) break;
                }

                if (requiresDenied[i] >= 0) cutOff.Enqueue(i);
            }

            while (cutOff.Count > 0)
            {
                int blocked = cutOff.Dequeue();
                if (dependants[blocked] == null) continue;
                foreach (int user in dependants[blocked])
                {
                    if (denied[user] || protectedUnit[user] || requiresDenied[user] >= 0) continue;
                    requiresDenied[user] = requiresDenied[blocked];
                    cutOff.Enqueue(user);
                }
            }

            // Closure over hard dependencies. Breadth first from the directly allowed units that are not
            // cut off, in input order, so the dependant named in a reason is the first one that reached
            // the dependency: deterministic for a given input order. What is pulled in is an unlisted
            // library (whitelist mode); a denied unit is never entered, and neither is a unit that is cut
            // off from an allowed one (only a protected dependant can still name such a unit).
            var queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (initial[i] == null || requiresDenied[i] >= 0) continue;
                reasons[i] = initial[i];
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                ArenaSessionUnit dependant = units[index];
                if (dependant.HardDependencies == null) continue;

                foreach (string dependencyGuid in dependant.HardDependencies)
                {
                    List<int> installedDependencies = InstalledUnits(byGuid, dependencyGuid);
                    if (installedDependencies == null) continue;

                    foreach (int dependencyIndex in installedDependencies)
                    {
                        if (reasons[dependencyIndex] != null || denied[dependencyIndex] || requiresDenied[dependencyIndex] >= 0) continue;
                        reasons[dependencyIndex] = ReasonDependencyPrefix + dependant.Guid;
                        pulledInBy[dependencyIndex] = dependant;
                        queue.Enqueue(dependencyIndex);
                    }
                }
            }

            // Which of the cut-off units are reported as such: the ones that the policy allows by itself
            // and the ones an allowed-by-itself unit would have pulled in. A cut-off unlisted library that
            // nothing allowed needs stays a plain not-on-whitelist exclusion.
            var wouldBeAllowed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (initial[i] == null) continue;
                wouldBeAllowed[i] = true;
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                if (units[index].HardDependencies == null) continue;

                foreach (string dependencyGuid in units[index].HardDependencies)
                {
                    List<int> installedDependencies = InstalledUnits(byGuid, dependencyGuid);
                    if (installedDependencies == null) continue;

                    foreach (int dependencyIndex in installedDependencies)
                    {
                        if (wouldBeAllowed[dependencyIndex] || denied[dependencyIndex]) continue;
                        wouldBeAllowed[dependencyIndex] = true;
                        queue.Enqueue(dependencyIndex);
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                ArenaSessionUnit unit = units[i];
                if (reasons[i] == null)
                {
                    if (requiresDenied[i] >= 0 && wouldBeAllowed[i])
                    {
                        ArenaSessionUnit blocker = units[requiresDenied[i]];
                        result.Excluded.Add(unit.CopyWithReason(ReasonRequiresDeniedPrefix + blocker.Guid));
                        result.Warnings.Add(unit.DisplayName + ": excluded because it requires " + blocker.DisplayName
                            + (policy.IsDeniedByUser(blocker.Guid) ? " (denied by the user's policy file)" : " (denied by the default list)"));
                    }
                    else
                    {
                        result.Excluded.Add(unit.CopyWithReason(policy.ExclusionReason(unit.Guid)));
                    }

                    continue;
                }

                result.Allowed.Add(unit.CopyWithReason(reasons[i]));

                if (direct[i] != null)
                {
                    string reviewed = direct[i].ReviewedVersion(side);
                    if (!string.IsNullOrEmpty(reviewed) && ArenaWhitelistPolicy.VersionDiffers(unit.Version, reviewed))
                    {
                        result.Warnings.Add(unit.DisplayName + ": version " + DescribeVersion(unit.Version)
                            + " differs from reviewed " + reviewed);
                    }
                }
                else if (pulledInBy[i] != null)
                {
                    result.Warnings.Add(unit.DisplayName + ": pulled in as hard dependency of " + pulledInBy[i].DisplayName);
                }
            }

            string policyOverride = DescribePolicyOverride(result.Allowed);
            if (policyOverride != null) result.Warnings.Add(policyOverride);

            return result;
        }

        /// <summary>
        /// The policy-override warning for the units this decision allowed because nothing names them,
        /// or null when there are none. That reason only occurs in blacklist mode, so a whitelist
        /// (the shipped default, or a user file without <c>"unlisted": "allow"</c>) never gets one.
        /// Names are the display names, sorted ignoring case; at most
        /// <see cref="PolicyOverrideNamesListed"/> of them are listed, then <c>and K more</c>.
        /// </summary>
        private static string DescribePolicyOverride(List<ArenaSessionUnit> allowed)
        {
            var names = new List<string>();
            foreach (ArenaSessionUnit unit in allowed)
            {
                if (!string.Equals(unit.Reason, ReasonUnlistedAllowed, StringComparison.Ordinal)) continue;
                string name = unit.DisplayName;
                names.Add(string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim());
            }

            if (names.Count == 0) return null;

            // Case-insensitive first; the ordinal tie-break keeps the text the same on every runtime
            // (List.Sort is not stable).
            names.Sort(delegate (string a, string b)
            {
                int order = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return order != 0 ? order : string.CompareOrdinal(a, b);
            });

            string list = names.Count <= PolicyOverrideNamesListed
                ? string.Join(", ", names.ToArray())
                : string.Join(", ", names.GetRange(0, PolicyOverrideNamesListed).ToArray()) + ", and "
                    + (names.Count - PolicyOverrideNamesListed).ToString(CultureInfo.InvariantCulture) + " more";
            return PolicyOverridePrefix + names.Count.ToString(CultureInfo.InvariantCulture)
                + " unlisted mod(s) allowed by your policy file: " + list;
        }

        /// <summary>The indices of the installed units with this GUID, or null for a blank or unknown one.</summary>
        private static List<int> InstalledUnits(Dictionary<string, List<int>> byGuid, string guid)
        {
            string key = Normalize(guid);
            List<int> indices;
            return key.Length > 0 && byGuid.TryGetValue(key, out indices) ? indices : null;
        }

        private static string Normalize(string guid)
        {
            return guid == null ? string.Empty : guid.Trim();
        }

        private static string DescribeVersion(string version)
        {
            return string.IsNullOrWhiteSpace(version) ? "(unknown)" : version.Trim();
        }
    }
}
