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
    /// reason <c>unlisted-allowed</c> and
    /// no warning of its own; a hard dependency of an allowed unit that is installed but not allowed
    /// (a denied unit, or in whitelist mode any unlisted one) is allowed as well, transitively, with
    /// reason <c>dependency-of:&lt;guid&gt;</c> and a warning; everything else is excluded
    /// (<c>denied-by-user</c> when the user file's deny list names it, <c>denied</c> when the
    /// shipped default file's deny list does, otherwise <c>not-on-whitelist</c>). A reviewed version
    /// that differs from the installed one is a warning, never an exclusion.
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

        public const string ReasonNotOnWhitelist = "not-on-whitelist";

        /// <summary>The user file's deny list names the unit.</summary>
        public const string ReasonDeniedByUser = "denied-by-user";

        /// <summary>
        /// The shipped default file's deny list names the unit (a mod ArenaMode refuses by design) and
        /// the user's allow list does not bring it back. Holds in whitelist and blacklist mode alike.
        /// </summary>
        public const string ReasonDenied = "denied";

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
        /// <c>SAIN: version 4.5.2 differs from reviewed 4.5.1</c> and
        /// <c>BigBrain: pulled in as hard dependency of SAIN</c>; a denied unit adds
        /// <c>(overrides the user's deny)</c> or <c>(overrides the default list's deny)</c>.
        /// </summary>
        public List<string> Warnings { get; private set; }

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
            var reasons = new string[count];
            var direct = new ArenaWhitelistEntry[count];
            var pulledInBy = new ArenaSessionUnit[count];
            var queue = new Queue<int>();
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
                    reasons[i] = entry.Reason;
                    queue.Enqueue(i);
                }
                else if (policy.IsUnlistedAllowed(units[i].Guid))
                {
                    // Blacklist mode. No entry, so no reviewed version and no warning. It joins the
                    // closure like any allowed unit: a denied hard dependency of it must still load.
                    reasons[i] = ReasonUnlistedAllowed;
                    queue.Enqueue(i);
                }
            }

            // Closure over hard dependencies. Breadth first from the directly allowed units in
            // input order, so the dependant named in a reason is the first one that reached the
            // dependency: deterministic for a given input order.
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                ArenaSessionUnit dependant = units[index];
                if (dependant.HardDependencies == null) continue;

                foreach (string dependencyGuid in dependant.HardDependencies)
                {
                    string key = Normalize(dependencyGuid);
                    List<int> installedDependencies;
                    if (key.Length == 0 || !byGuid.TryGetValue(key, out installedDependencies)) continue;

                    foreach (int dependencyIndex in installedDependencies)
                    {
                        if (reasons[dependencyIndex] != null) continue;
                        reasons[dependencyIndex] = ReasonDependencyPrefix + dependant.Guid;
                        pulledInBy[dependencyIndex] = dependant;
                        queue.Enqueue(dependencyIndex);
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                ArenaSessionUnit unit = units[i];
                if (reasons[i] == null)
                {
                    result.Excluded.Add(unit.CopyWithReason(policy.ExclusionReason(unit.Guid)));
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
                    string warning = unit.DisplayName + ": pulled in as hard dependency of " + pulledInBy[i].DisplayName;
                    if (policy.IsDeniedByUser(unit.Guid)) warning += " (overrides the user's deny)";
                    else if (policy.IsDeniedByDefault(unit.Guid)) warning += " (overrides the default list's deny)";
                    result.Warnings.Add(warning);
                }
            }

            return result;
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
